using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TrackMaster.Core;
using TrackMaster.Web.Interop;
using TrackMaster.Web.Shell;

namespace TrackMaster.Web;

/// <summary>Register „Ausstattung“: eigene Modelle (Pro) – Modell-Editor, Bibliothek im Browser, Import und Export.</summary>
public partial class App
{
    /// <summary>Eigene Modelle im Speicher des Browsers (Liste von *.tmmodell-Inhalten).</summary>
    private const string ModelsKey = "trackmaster.modelle";

    /// <summary>Im Editor offenes Modell (null = Editor geschlossen).</summary>
    private ModelDefinition? _editModel;
    private ModelEditorPanel? _editor;
    /// <summary>Nach dem ersten Speichern, z. B. dem Objekt im Plan die eigene Fassung geben.</summary>
    private Action<ModelDefinition>? _afterModelSave;

    /// <summary>Gruppen für eigene Modelle im Register „Ausstattung“.</summary>
    private IEnumerable<Group> EquipmentProGroups() =>
    [
        new(Lang.T("Eigene Modelle"),
        [
            new(Lang.T("Eigene Modelle"), "Cube", "modelsOwn", true, Lang.T("Selbst gebaute und importierte Modelle sowie die im Plan mitgespeicherten.")),
            new(Lang.T("Neues Modell"), "Palette", "modelNew", true, Lang.T("Öffnet den Modell-Editor mit einem leeren Modell: Gebäude und mehr aus Quadern, Dächern, Vielecken und freien Formen bauen.")),
            new(Lang.T("Aus Vorlage…"), "Copy", "modelFromTemplate", false, Lang.T("Ein Grundmodell oder eigenes Modell als Ausgangspunkt kopieren und im Editor anpassen.")),
            new(Lang.T("Bibliothek…"), "List", "modelLibrary", false, Lang.T("Eigene Modelle bearbeiten, löschen, exportieren und Modelle aus dem Plan übernehmen.")),
            new(Lang.T("Importieren…"), "Open", "modelImport", false, Lang.T("Modelldateien (*.tmmodell) oder Sammlungen (*.zip) einlesen.")),
        ]),
    ];

    private void AddEquipmentProCommands()
    {
        LoadOwnModels();
        // Im Plan mitgespeicherte Modelle gehen auch ohne Pro; eigene aus der Bibliothek fragt PlaceModel nach der Lizenz
        Add("modelsOwn", Lang.T("Eigene Modelle"), () =>
        {
            if (_session.Tool == CanvasTool.Model && _session.CurrentModel is { IsBuiltin: false }) { SetTool(CanvasTool.Select); return; }
            AskModel(Lang.T("Eigene Modelle"), OwnAndPlanModels());
        });
        var feature = Lang.T("Modell-Editor und eigene Modelle");
        Pro("modelNew", feature, () => OpenModelEditor(ModelEditor.NewModel(Lang.T("Neues Modell"))));
        Pro("modelFromTemplate", feature, () => PickModel(Lang.T("Vorlage für ein eigenes Modell"), ModelLibrary.All.ToList(), m => OpenModelEditor(ModelEditor.CopyOf(m))));
        Pro("modelLibrary", feature, ShowModelLibrary);
        Pro("modelImport", feature, () => _ = ImportModels());
        Pro("modelEditSelected", feature, EditSelectedModel, () => SelectedModelItem() is not null);
    }

    private List<ModelDefinition> OwnAndPlanModels()
        => ModelLibrary.Own.Concat(_session.PlanModelsNotInLibrary()).OrderBy(m => m.Category).ThenBy(m => m.Name).ToList();

    private void LoadOwnModels()
    {
        var raw = Js.StorageGet(ModelsKey);
        if (string.IsNullOrEmpty(raw)) return;
        var list = new List<ModelDefinition>();
        try
        {
            foreach (var json in JsonSerializer.Deserialize<List<string>>(raw) ?? [])
            {
                try { list.Add(ModelFile.FromJson(json)); }
                catch (InvalidDataException) { }
            }
        }
        catch (JsonException) { }
        ModelLibrary.SetOwn(list);
    }

    private bool StoreOwnModels()
    {
        var ok = Js.StorageSet(ModelsKey, JsonSerializer.Serialize(ModelLibrary.Own.Select(ModelFile.ToJson).ToList()));
        if (!ok) ShowToast(Lang.T("Der Speicher des Browsers ist voll. Bitte Modelle exportieren und nicht mehr benötigte löschen."));
        return ok;
    }

    private bool SaveOwnModel(ModelDefinition m)
    {
        var before = ModelLibrary.Own.FirstOrDefault(o => o.Id == m.Id);
        ModelLibrary.PutOwn(m.Clone());
        if (!StoreOwnModels())
        {
            if (before is null) ModelLibrary.RemoveOwn(m.Id); else ModelLibrary.PutOwn(before);
            return false;
        }
        _session.UpdatePlanModel(m);
        if (_afterModelSave is { } after) { _afterModelSave = null; after(m); }
        Refresh();
        return true;
    }

    private void OpenModelEditor(ModelDefinition model, Action<ModelDefinition>? afterSave = null)
    {
        CloseAllDialogs();
        _afterModelSave = afterSave;
        _editModel = model;
        StateHasChanged();
    }

    private void CloseModelEditor()
    {
        _editModel = null;
        _afterModelSave = null;
        Js_FocusPlan();
        StateHasChanged();
    }

    private SceneryItem? SelectedModelItem()
        => _session.SelectedObject is { } id && _session.Plan.Scenery.FirstOrDefault(s => s.Id == id) is { IsModel: true } s && ModelLibrary.Of(_session.Plan, s) is not null ? s : null;

    /// <summary>Modell des gewählten Objekts bearbeiten; ein Grundmodell wird dazu kopiert und dem Objekt zugewiesen.</summary>
    private void EditSelectedModel()
    {
        if (SelectedModelItem() is not { } item || ModelLibrary.Of(_session.Plan, item) is not { } def) return;
        var itemId = item.Id;
        if (def.IsBuiltin) OpenModelEditor(ModelEditor.CopyOf(def), saved => _session.ReplaceModel(itemId, saved));
        else OpenModelEditor(ModelLibrary.Own.FirstOrDefault(m => m.Id == def.Id) ?? def);
    }

    private async Task ImportModels()
    {
        var result = await Js.PickFileBytes(".tmmodell,.zip,application/zip,application/json");
        if (string.IsNullOrEmpty(result)) return;
        var sep = result.IndexOf('\0');
        int count = 0, skipped = 0;
        try
        {
            var models = ModelFile.Read(Convert.FromBase64String(result[(sep + 1)..]), out skipped);
            foreach (var m in models)
            {
                if (m.IsBuiltin) m.Id = ModelFile.NewId(m.Name);
                ModelLibrary.PutOwn(m);
                count++;
            }
            if (count > 0) StoreOwnModels();
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or IOException) { skipped++; }
        Message(Lang.T("Modelle importieren"), Lang.F($"{count} Modell(e) importiert.") + (skipped > 0 ? " " + Lang.F($"{skipped} Datei(en) ließen sich nicht lesen.") : ""));
        Refresh();
    }

    private void ExportModel(ModelDefinition m)
    {
        var name = ModelFile.SafeFileName(m.Name) + ModelFile.Extension;
        Js.Download(name, "application/json", ModelFile.ToJson(m));
        ShowToast(Lang.F($"Gespeichert: {name}"));
    }

    private void ExportAllModels()
    {
        if (ModelLibrary.Own.Count == 0) return;
        var name = Lang.T("Eigene Modelle") + ".zip";
        Js.DownloadBytes(name, "application/zip", ModelFile.ToZip(ModelLibrary.Own));
        ShowToast(Lang.F($"Gespeichert: {name}"));
    }

    /// <summary>STL (3D-Druck) oder OBJ mit Farben (als ZIP mit der MTL-Datei) im Maßstab der Spur.</summary>
    private void Export3D(ModelDefinition m, bool stl)
    {
        var k = ModelLibrary.MmPerMeter(_session.Plan.Gauge);
        var baseName = ModelFile.SafeFileName(m.Name);
        if (stl)
        {
            var stlName = baseName + ".stl";
            Js.DownloadBytes(stlName, "model/stl", ModelExport.ToStl(m, k));
            ShowToast(Lang.F($"Gespeichert: {stlName}"));
            return;
        }
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Entry(string file, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(file).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Entry(baseName + ".obj", ModelExport.ToObj(m, k, baseName + ".mtl"));
            Entry(baseName + ".mtl", ModelExport.ToMtl(m));
        }
        var zipName = baseName + " (OBJ).zip";
        Js.DownloadBytes(zipName, "application/zip", ms.ToArray());
        ShowToast(Lang.F($"Gespeichert: {zipName}"));
    }

    /// <summary>Tasten für den Modell-Editor; true = verarbeitet.</summary>
    private bool EditorKeyDown(string key, bool ctrl) => _editor?.KeyDown(key, ctrl) ?? false;
}

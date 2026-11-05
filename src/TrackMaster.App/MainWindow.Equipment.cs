using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Register „Ausstattung“, Pro-Teil: Modell-Editor, eigene Bibliothek, Import und Export von Modellen.</summary>
public sealed partial class MainWindow
{
    private readonly ModelStore _modelStore = new(ModelStore.DefaultFolder);
    private const string ModelFilter = "TrackMaster-Modell (*.tmmodell)|*.tmmodell";

    /// <summary>Eigene Modelle aus dem Ordner laden (beim Start).</summary>
    private void LoadOwnModels()
    {
        try
        {
            ModelLibrary.SetOwn(_modelStore.LoadAll(out var skipped));
            if (skipped > 0) Session.Say(Lang.F($"{skipped} Modelldatei(en) im Ordner „Modelle“ ließen sich nicht lesen."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void DefineEquipmentCommands()
    {
        void Pro(string id, Action run, Func<bool>? can = null)
            => _commands[id] = new RelayCommand(() => { if (Session.RequirePro(Lang.T("Modell-Editor und eigene Modelle"))) run(); }, can);
        Pro("modelNew", () => EditModel(ModelEditor.NewModel(Lang.T("Neues Modell"))));
        Pro("modelFromTemplate", () => { if (PickModel(Lang.T("Vorlage für ein eigenes Modell"), ModelLibrary.All.ToList()) is { } m) EditModel(ModelEditor.CopyOf(m)); });
        Pro("modelLibrary", ShowModelLibrary);
        Pro("modelImport", () => ImportModels(this));
        Pro("modelEditSelected", EditSelectedModel, () => SelectedModelItem() is not null);
    }

    /// <summary>Gruppe „Eigene Modelle“ (Pro): Menü der eigenen und im Plan mitgespeicherten Modelle, Editor, Bibliothek.</summary>
    private IEnumerable<RibbonGroup> EquipmentProGroups()
    {
        var own = ModelMenu(Lang.T("Eigene Modelle"), "Cube", Lang.T("Selbst gebaute und importierte Modelle sowie die im Plan mitgespeicherten."),
            () => ModelLibrary.Own.Concat(Session.PlanModelsNotInLibrary()).OrderBy(m => m.Category).ThenBy(m => m.Name));
        own.ToolTipFooterTitle = Lang.T("Pro-Funktion");
        own.ToolTipFooterDescription = Lang.T("Im Plan mitgespeicherte Modelle lassen sich auch ohne Pro-Lizenz einfügen.");
        yield return Group(Lang.T("Eigene Modelle"),
            own,
            ProLarge(Lang.T("Neues Modell"), "Palette", "modelNew", Lang.T("Öffnet den Modell-Editor mit einem leeren Modell: Gebäude und mehr aus Quadern, Dächern, Vielecken und freien Formen bauen.")),
            ProSmall(Lang.T("Aus Vorlage…"), "Copy", "modelFromTemplate", Lang.T("Ein Grundmodell oder eigenes Modell als Ausgangspunkt kopieren und im Editor anpassen.")),
            ProSmall(Lang.T("Bibliothek…"), "List", "modelLibrary", Lang.T("Eigene Modelle bearbeiten, löschen, exportieren und Modelle aus dem Plan übernehmen.")),
            ProSmall(Lang.T("Importieren…"), "Open", "modelImport", Lang.T("Modelldateien (*.tmmodell) oder Sammlungen (*.zip) einlesen.")));
    }

    /// <summary>Gewähltes Objekt im Plan, wenn es ein Ausstattungsmodell ist.</summary>
    private SceneryItem? SelectedModelItem()
        => Session.SelectedObject is { } id && Session.Plan.Scenery.FirstOrDefault(s => s.Id == id) is { IsModel: true } s && ModelLibrary.Of(Session.Plan, s) is not null ? s : null;

    /// <summary>Modell des gewählten Objekts bearbeiten; ein Grundmodell wird dazu als eigenes Modell kopiert und dem Objekt zugewiesen.</summary>
    private void EditSelectedModel()
    {
        if (SelectedModelItem() is not { } item || ModelLibrary.Of(Session.Plan, item) is not { } def) return;
        var itemId = item.Id;
        if (def.IsBuiltin)
        {
            var copy = ModelEditor.CopyOf(def);
            if (EditModel(copy) is { } saved) Session.ReplaceModel(itemId, saved);
            return;
        }
        // eigene Fassung aus der Bibliothek, sonst die im Plan mitgespeicherte
        EditModel(ModelLibrary.Own.FirstOrDefault(m => m.Id == def.Id) ?? def);
    }

    /// <summary>Öffnet den Editor; gibt das zuletzt gespeicherte Modell zurück (null = nicht gespeichert).</summary>
    private ModelDefinition? EditModel(ModelDefinition model)
    {
        ModelDefinition? saved = null;
        ModelEditorWindow? window = null;
        window = new ModelEditorWindow(model, Session.Plan.Gauge, m =>
        {
            if (!SaveOwnModel(m, window!)) return false;
            saved = m;
            return true;
        }) { Owner = this };
        window.ShowDialog();
        return saved;
    }

    /// <summary>Speichert ein eigenes Modell im Ordner, in der Bibliothek und – wenn verwendet – im Plan.</summary>
    private bool SaveOwnModel(ModelDefinition m, Window owner)
    {
        try { _modelStore.Save(m); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, Lang.F($"Speichern nicht möglich.\n\n{ex.Message}"), "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        ModelLibrary.PutOwn(m.Clone());
        Session.UpdatePlanModel(m);
        return true;
    }

    /// <summary>Liest Modelldateien oder ZIP-Sammlungen ein; gleiche Kennung ersetzt das vorhandene Modell.</summary>
    private int ImportModels(Window owner)
    {
        var dlg = new OpenFileDialog { Filter = Lang.T("Modelle (*.tmmodell;*.zip)|*.tmmodell;*.zip|Alle Dateien (*.*)|*.*"), Title = Lang.T("Modelle importieren"), Multiselect = true };
        if (dlg.ShowDialog(owner) != true) return 0;
        int count = 0, skipped = 0;
        foreach (var file in dlg.FileNames)
        {
            try
            {
                var models = ModelFile.Read(File.ReadAllBytes(file), out var bad);
                foreach (var m in models)
                {
                    if (m.IsBuiltin) m.Id = ModelFile.NewId(m.Name);
                    _modelStore.Save(m);
                    ModelLibrary.PutOwn(m);
                    count++;
                }
                skipped += bad;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { skipped++; }
        }
        var text = Lang.F($"{count} Modell(e) importiert.") + (skipped > 0 ? " " + Lang.F($"{skipped} Datei(en) ließen sich nicht lesen.") : "");
        MessageBox.Show(owner, text, Lang.T("Modelle importieren"), MessageBoxButton.OK, count > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        return count;
    }

    private void ExportModels(Window owner, IReadOnlyList<ModelDefinition> models)
    {
        if (models.Count == 0) return;
        var single = models.Count == 1;
        var dlg = new SaveFileDialog
        {
            Title = Lang.T("Modelle exportieren"),
            Filter = single ? ModelFilter : Lang.T("Modellsammlung (*.zip)|*.zip"),
            FileName = single ? ModelFile.SafeFileName(models[0].Name) + ModelFile.Extension : Lang.T("Eigene Modelle") + ".zip",
        };
        if (dlg.ShowDialog(owner) != true) return;
        try
        {
            if (single) File.WriteAllText(dlg.FileName, ModelFile.ToJson(models[0]), new System.Text.UTF8Encoding(false));
            else File.WriteAllBytes(dlg.FileName, ModelFile.ToZip(models));
            ShowMessage(Lang.F($"Gespeichert: {Path.GetFileName(dlg.FileName)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, Lang.F($"Speichern nicht möglich.\n\n{ex.Message}"), "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Dialoge ----------

    private static StackPanel ModelRow(ModelDefinition m, string? note = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        row.Children.Add(new Border
        {
            Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0), Background = Theme.CanvasBrush, CornerRadius = new CornerRadius(3),
            Child = new Image { Source = PlanScene.ModelImage(m), Margin = new Thickness(4) },
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.Label(m.Name, 12, true));
        var (w, d, h) = m.Size;
        text.Children.Add(Theme.Secondary($"{(string.IsNullOrWhiteSpace(m.Category) ? ModelLibrary.OwnCategory : m.Category)} · {Fmt.Num(w, 1)} × {Fmt.Num(d, 1)} × {Fmt.Num(h, 1)} m" + (note is null ? "" : " · " + note)));
        row.Children.Add(text);
        return row;
    }

    private Window DialogWindow(string title, double width, double height) => new()
    {
        Title = title, Width = width, Height = height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        FontFamily = Theme.Font, FontSize = 12 * Theme.FontScale, Background = Theme.WindowBrush, ResizeMode = ResizeMode.CanResizeWithGrip,
    };

    /// <summary>Modell aus einer Liste wählen (z. B. Vorlage).</summary>
    private ModelDefinition? PickModel(string title, IReadOnlyList<ModelDefinition> models)
    {
        var win = DialogWindow(title, 480, 620);
        var list = new ListBox { Margin = new Thickness(12, 12, 12, 6), BorderBrush = Theme.ControlBorderBrush };
        foreach (var m in models) list.Items.Add(new ListBoxItem { Content = ModelRow(m), Tag = m });
        ModelDefinition? result = null;
        void Take() { if (list.SelectedItem is ListBoxItem { Tag: ModelDefinition m }) { result = m; win.Close(); } }
        list.MouseDoubleClick += (_, _) => Take();
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.Click += (_, _) => Take();
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.Click += (_, _) => win.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 6, 8), Children = { ok, cancel } };
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(list);
        win.Content = dock;
        list.SelectedIndex = 0;
        win.ShowDialog();
        return result;
    }

    /// <summary>Bibliothek der eigenen Modelle: bearbeiten, kopieren, löschen, exportieren, importieren und Modelle aus dem Plan übernehmen.</summary>
    private void ShowModelLibrary()
    {
        var win = DialogWindow(Lang.T("Eigene Modelle"), 720, 640);
        var list = new ListBox { Margin = new Thickness(12), BorderBrush = Theme.ControlBorderBrush, SelectionMode = SelectionMode.Extended };
        var info = Theme.Secondary("");
        info.Margin = new Thickness(12, 0, 12, 10);

        IEnumerable<ModelDefinition> Selected() => list.SelectedItems.OfType<ListBoxItem>().Select(i => (ModelDefinition)i.Tag);
        bool InLibrary(ModelDefinition m) => ModelLibrary.Own.Any(o => o.Id == m.Id);

        void Fill(string? select = null)
        {
            list.Items.Clear();
            var used = Session.Plan.Scenery.Where(s => s.IsModel).Select(s => s.Model!).ToHashSet();
            foreach (var m in ModelLibrary.Own.OrderBy(m => m.Category).ThenBy(m => m.Name))
                list.Items.Add(new ListBoxItem { Content = ModelRow(m, used.Contains(m.Id) ? Lang.T("im Plan verwendet") : null), Tag = m, IsSelected = m.Id == select });
            foreach (var m in Session.PlanModelsNotInLibrary())
                list.Items.Add(new ListBoxItem { Content = ModelRow(m, Lang.T("nur im Plan")), Tag = m, IsSelected = m.Id == select });
            info.Text = Lang.F($"{ModelLibrary.Own.Count} eigene Modelle im Ordner {_modelStore.Folder}");
        }

        Button B(string text, string icon, Action run, Func<bool>? can = null)
        {
            var b = Theme.Button(text, icon);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Click += (_, _) => run();
            if (can is not null) list.SelectionChanged += (_, _) => b.IsEnabled = can();
            return b;
        }
        var one = () => list.SelectedItems.Count == 1;
        var any = () => list.SelectedItems.Count > 0;
        var side = new StackPanel { Width = 210, Margin = new Thickness(0, 12, 12, 12) };
        side.Children.Add(B(Lang.T("Neues Modell"), "Plus", () => { if (EditModel(ModelEditor.NewModel(Lang.T("Neues Modell"))) is { } m) Fill(m.Id); }));
        side.Children.Add(B(Lang.T("Aus Vorlage…"), "Copy", () =>
        {
            if (PickModel(Lang.T("Vorlage für ein eigenes Modell"), ModelLibrary.All.ToList()) is { } t && EditModel(ModelEditor.CopyOf(t)) is { } m) Fill(m.Id);
        }));
        side.Children.Add(B(Lang.T("Bearbeiten"), "Palette", () => { if (Selected().FirstOrDefault() is { } m && EditModel(m) is { } s) Fill(s.Id); }, one));
        side.Children.Add(B(Lang.T("Kopie bearbeiten"), "Copy", () => { if (Selected().FirstOrDefault() is { } m && EditModel(ModelEditor.CopyOf(m)) is { } s) Fill(s.Id); }, one));
        side.Children.Add(B(Lang.T("In den Plan einfügen"), "Building", () =>
        {
            if (Selected().FirstOrDefault() is not { } m) return;
            win.Close();
            SetModelTool(m.Id);
        }, one));
        side.Children.Add(new Separator { Margin = new Thickness(0, 4, 6, 10) });
        side.Children.Add(B(Lang.T("In Bibliothek übernehmen"), "Save", () =>
        {
            foreach (var m in Selected().Where(m => !InLibrary(m)).ToList()) SaveOwnModel(m.Clone(), win);
            Fill();
        }, () => Selected().Any(m => !InLibrary(m))));
        side.Children.Add(B(Lang.T("Exportieren…"), "SaveAs", () => ExportModels(win, Selected().ToList()), any));
        side.Children.Add(B(Lang.T("Alle exportieren (ZIP)…"), "SaveAs", () => ExportModels(win, ModelLibrary.Own.ToList()), () => true));
        side.Children.Add(B(Lang.T("Importieren…"), "Open", () => { if (ImportModels(win) > 0) Fill(); }));
        side.Children.Add(B(Lang.T("Ordner öffnen"), "Open", () =>
        {
            try
            {
                Directory.CreateDirectory(_modelStore.Folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _modelStore.Folder, UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }));
        side.Children.Add(new Separator { Margin = new Thickness(0, 4, 6, 10) });
        var delete = B(Lang.T("Löschen"), "Delete", () =>
        {
            var del = Selected().Where(InLibrary).ToList();
            if (del.Count == 0) return;
            var question = del.Count == 1 ? Lang.F($"„{del[0].Name}“ aus der Bibliothek löschen?") : Lang.F($"{del.Count} Modelle aus der Bibliothek löschen?");
            if (MessageBox.Show(win, question + "\n\n" + Lang.T("Pläne, die das Modell verwenden, behalten ihre mitgespeicherte Fassung."), Lang.T("Eigene Modelle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var m in del)
            {
                try { _modelStore.Delete(m.Id); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                ModelLibrary.RemoveOwn(m.Id);
            }
            Fill();
        }, () => Selected().Any(InLibrary));
        side.Children.Add(delete);
        list.MouseDoubleClick += (_, _) => { if (Selected().FirstOrDefault() is { } m && EditModel(m) is { } s) Fill(s.Id); };

        var close = Theme.Button(Lang.T("Schließen"));
        close.Click += (_, _) => win.Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 0, 6, 6) };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(info);
        var dock = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        DockPanel.SetDock(side, Dock.Right);
        dock.Children.Add(side);
        dock.Children.Add(list);
        win.Content = dock;
        Fill();
        list.SelectedIndex = list.Items.Count > 0 ? 0 : -1;
        // Schaltflächen-Zustand anfangs setzen
        list.RaiseEvent(new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        win.ShowDialog();
    }
}

using TrackMaster.Core;

namespace TrackMaster.Tests;

[Collection("Sprache")]
public class ModelEditorTests
{
    private static ModelEditor NewEditor() => new(ModelEditor.NewModel("Testhaus"));

    private static (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) Box(ModelBody b)
    {
        var t = ModelMesh.Body(b).SelectMany(x => new[] { x.A, x.B, x.C }).ToList();
        return (t.Min(p => p.X), t.Max(p => p.X), t.Min(p => p.Y), t.Max(p => p.Y), t.Min(p => p.Z), t.Max(p => p.Z));
    }

    [Fact]
    public void Neues_Modell_ist_eigen_und_Dach_kommt_aufs_Haus()
    {
        var ed = NewEditor();
        Assert.False(ed.Model.IsBuiltin);
        Assert.StartsWith("eigen.", ed.Model.Id);
        ed.Selection.Add(0);
        var roof = ed.Add(BodyShape.Satteldach);
        Assert.Equal(ModelMesh.TopOf(ed.Model.Bodies[0]), roof.Z, 6);
        Assert.Equal(2, ed.Model.Bodies.Count);
        Assert.Equal([1], ed.Selection);
        Assert.True(ed.Dirty);

        ed.Selection.Clear(); ed.Selection.Add(0);
        var door = ed.Add(BodyShape.Tuer);
        var house = ed.Model.Bodies[0];
        Assert.Equal(house.Y + house.D / 2 + door.D / 2, door.Y, 6);
        Assert.Empty(ed.Problems());
    }

    [Fact]
    public void Rueckgaengig_und_Wiederholen()
    {
        var ed = NewEditor();
        ed.Add(BodyShape.Zylinder);
        ed.Add(BodyShape.Treppe);
        Assert.Equal(3, ed.Model.Bodies.Count);
        ed.Undo();
        Assert.Equal(2, ed.Model.Bodies.Count);
        ed.Undo();
        Assert.Single(ed.Model.Bodies);
        Assert.False(ed.CanUndo);
        ed.Redo();
        Assert.Equal(2, ed.Model.Bodies.Count);
        Assert.Equal(BodyShape.Zylinder, ed.Model.Bodies[1].Shape);
        ed.Edit(() => ed.Model.Bodies[0].H = 9);
        Assert.False(ed.CanRedo);
    }

    [Fact]
    public void Vieleck_aus_Punkten_mit_Raster()
    {
        var ed = NewEditor();
        ed.Model.Bodies.Clear();
        var b = ed.AddPolygon([(0.02, 0.01), (10.04, 0), (10, 6.03), (4, 9), (0, 6)], 4);
        Assert.NotNull(b);
        Assert.Equal(5, b!.Points.Count);
        var (minX, maxX, minY, maxY, _, maxZ) = Box(b);
        Assert.Equal(0, minX, 6); Assert.Equal(10, maxX, 6);
        Assert.Equal(0, minY, 6); Assert.Equal(9, maxY, 6);
        Assert.Equal(4, maxZ, 6);
        // zu wenige Punkte bzw. keine Fläche
        Assert.Null(ed.AddPolygon([(0, 0), (1, 0)], 3));
        Assert.Null(ed.AddPolygon([(0, 0), (1, 0), (2, 0)], 3));
    }

    [Fact]
    public void Ecken_verschieben_einfuegen_und_entfernen()
    {
        var ed = NewEditor();
        ed.Model.Bodies.Clear();
        var b = ed.AddPolygon([(0, 0), (4, 0), (4, 4), (0, 4)], 3)!;
        Assert.Equal(1, ed.PickVertex(EditorView.Draufsicht, 3.9, 0.1, 0.5));
        ed.Edit(() => ed.MoveVertex(1, 2, 0, 0));
        Assert.Equal(6, Box(b).MaxX, 6);
        // Höhe einer Ecke in der Vorderansicht ändern
        ed.Edit(() => ed.MoveVertex(2, 0, 0, 1.5));
        Assert.Equal(4.5, Box(b).MaxZ, 6);

        ed.SelectedVertex = 1;
        ed.InsertVertex();
        Assert.Equal(5, b.Points.Count);
        Assert.Equal(2, ed.SelectedVertex);
        ed.RemoveVertex();
        Assert.Equal(4, b.Points.Count);
        Assert.Equal(-1, ed.SelectedVertex);
        ed.Undo();
        Assert.Equal(5, ed.Model.Bodies[0].Points.Count);
    }

    [Fact]
    public void Umwandeln_in_Freiform_behaelt_die_Gestalt()
    {
        var ed = NewEditor();
        ed.Edit(() => { ed.Model.Bodies[0].Rotation = 30; ed.Model.Bodies[0].X = 2; });
        var before = Box(ed.Model.Bodies[0]);
        var trisBefore = ModelMesh.Body(ed.Model.Bodies[0]).Count;
        ed.Selection.Add(0);
        ed.ToFreeform();
        var f = ed.Model.Bodies[0];
        Assert.Equal(BodyShape.Freiform, f.Shape);
        Assert.Equal(8, f.Vertices.Count);
        var after = Box(f);
        Assert.Equal(before.MinX, after.MinX, 6); Assert.Equal(before.MaxY, after.MaxY, 6); Assert.Equal(before.MaxZ, after.MaxZ, 6);
        Assert.True(ModelMesh.Body(f).Count >= trisBefore);

        // freie Ecke anheben: Dachspitze
        var top = Enumerable.Range(0, f.Vertices.Count).First(i => f.Vertices[i].Z > 1);
        ed.SelectedVertex = top;
        ed.InsertVertex();
        Assert.Equal(9, ed.Model.Bodies[0].Vertices.Count);
        var inserted = ed.Model.Bodies[0].Vertices[ed.SelectedVertex];
        Assert.True(inserted.Z > 1e-6);
        ed.Edit(() => ed.MoveVertex(ed.SelectedVertex, 0, 0, 2));
        Assert.Equal(inserted.Z + 2, Box(ed.Model.Bodies[0]).MaxZ, 6);
        // Speichern und Laden der Freiform
        var back = ModelFile.FromJson(ModelFile.ToJson(ed.Model));
        Assert.Equal(ModelMesh.Body(ed.Model.Bodies[0]).Count, ModelMesh.Body(back.Bodies[0]).Count);
    }

    [Fact]
    public void Waehlen_in_den_Ansichten_bevorzugt_Fenster()
    {
        var ed = NewEditor();
        ed.Selection.Add(0);
        var win = ed.Add(BodyShape.Fenster);
        // Vorderansicht schaut von +y: Fenster liegt vor der Wand
        Assert.Equal(1, ed.Pick(EditorView.Vorderansicht, win.X, -(win.Z + win.H / 2)));
        Assert.Equal(0, ed.Pick(EditorView.Vorderansicht, 3.5, -0.3));
        Assert.Equal(0, ed.Pick(EditorView.Draufsicht, -3, -2));
        Assert.Null(ed.Pick(EditorView.Seitenansicht, 30, 0));
        Assert.Empty(ed.Selection);
        ed.Pick(EditorView.Draufsicht, 0, 0);
        ed.Pick(EditorView.Vorderansicht, win.X, -(win.Z + win.H / 2), add: true);
        Assert.Equal([0, 1], ed.Selection);
        var (dx, dy, dz) = ModelEditor.Unproject(EditorView.Seitenansicht, 1, -2);
        Assert.Equal((0, -1, 2), (dx, dy, dz));
    }

    [Fact]
    public void Gruppe_bewegt_sich_gemeinsam_und_Spiegeln()
    {
        var ed = NewEditor();
        ed.Selection.Add(0);
        ed.Add(BodyShape.Tuer);
        ed.Selection.Add(0);
        ed.Group();
        Assert.True(ed.Model.Bodies[0].Group > 0);
        Assert.Equal(ed.Model.Bodies[0].Group, ed.Model.Bodies[1].Group);
        ed.Selection.Clear(); ed.Selection.Add(0);
        ed.Edit(() => ed.MoveSelection(5, 0, 0));
        Assert.Equal(5, ed.Model.Bodies[1].X, 6);

        // Tür an der Vorderseite (+y): nach Spiegeln an y = 0 zeigt sie nach −y
        ed.Selection.Clear(); ed.Selection.Add(1);
        var door = ed.Model.Bodies[1];
        var y = door.Y;
        ed.Mirror(acrossX: false);
        door = ed.Model.Bodies[1];
        Assert.Equal(-y, door.Y, 6);
        Assert.True(Box(door).MinY < -y + 1e-6);

        // Vieleck exakt spiegeln
        ed.Selection.Clear();
        ed.Model.Bodies.Clear();
        var p = ed.AddPolygon([(1, 0), (5, 0), (5, 2), (1, 6)], 3)!;
        p.Rotation = 20;
        var before = Box(p);
        ed.Mirror(acrossX: true);
        var after = Box(ed.Model.Bodies[0]);
        Assert.Equal(-before.MaxX, after.MinX, 6);
        Assert.Equal(-before.MinX, after.MaxX, 6);
        Assert.Equal(before.MaxY, after.MaxY, 6);
    }

    [Fact]
    public void Kopie_eines_Grundmodells_wird_eigenes_Modell()
    {
        var src = BuiltinModels.All.First(m => m.Id == "tm.stellwerk");
        var copy = ModelEditor.CopyOf(src);
        Assert.False(copy.IsBuiltin);
        Assert.NotEqual(src.Id, copy.Id);
        Assert.Equal(ModelLibrary.OwnCategory, copy.Category);
        Assert.Equal(src.Bodies.Count, copy.Bodies.Count);
        copy.Bodies[0].W = 99;
        Assert.NotEqual(99, src.Bodies[0].W);
        var ed = new ModelEditor(new ModelDefinition { Name = " " });
        Assert.Equal(2, ed.Problems().Count);
    }

    [Fact]
    public void Bibliotheksordner_speichert_benennt_um_und_loescht()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tm-modelle-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new ModelStore(dir);
            Assert.Empty(store.LoadAll(out _));
            var a = ModelEditor.NewModel("Haus A");
            var b = ModelEditor.NewModel("Haus A");
            var fa = store.Save(a);
            var fb = store.Save(b);
            Assert.NotEqual(fa, fb);
            a.Name = "Haus Neu";
            var fa2 = store.Save(a);
            Assert.False(File.Exists(fa));
            Assert.EndsWith("Haus Neu.tmmodell", fa2);
            File.WriteAllText(Path.Combine(dir, "kaputt.tmmodell"), "{ nein");

            var again = new ModelStore(dir);
            var all = again.LoadAll(out var skipped);
            Assert.Equal(1, skipped);
            Assert.Equal(2, all.Count);
            again.Delete(b.Id);
            Assert.False(File.Exists(fb));
            Assert.Single(new ModelStore(dir).LoadAll(out _));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Geaendertes_Modell_wird_im_Plan_aktualisiert()
    {
        var own = ModelEditor.NewModel("Lager");
        ModelLibrary.SetOwn([own]);
        try
        {
            var session = new PlanSession { IsPro = () => true };
            session.ModelId = own.Id;
            var item = session.PlaceModel(500, 500, 10)!;
            Assert.True(session.Plan.Models.ContainsKey(own.Id));
            Assert.Empty(session.PlanModelsNotInLibrary());

            var ed = new ModelEditor(own.Clone());
            ed.Edit(() => ed.Model.Bodies[0].H = 12);
            Assert.True(session.UpdatePlanModel(ed.Model));
            Assert.Equal(12, session.Plan.Models[own.Id].Bodies[0].H);
            Assert.Equal(12, ModelLibrary.Of(session.Plan, item)!.Size.H, 6);
            session.Undo();
            Assert.Equal(3, session.Plan.Models[own.Id].Bodies[0].H);
            Assert.False(session.UpdatePlanModel(ModelEditor.NewModel("Fremd")));

            ModelLibrary.SetOwn([]);
            Assert.Single(session.PlanModelsNotInLibrary());

            // Grundmodell im Plan durch eigene Kopie ersetzen; das alte eigene Modell fällt aus dem Plan
            var copy = ModelEditor.CopyOf(BuiltinModels.All.First(m => m.Id == "tm.stellwerk"));
            Assert.True(session.ReplaceModel(item.Id, copy));
            Assert.Equal(copy.Id, session.Plan.Scenery.Single(x => x.Id == item.Id).Model);
            Assert.True(session.Plan.Models.ContainsKey(copy.Id));
            Assert.False(session.Plan.Models.ContainsKey(own.Id));
        }
        finally { ModelLibrary.SetOwn([]); }
    }
}

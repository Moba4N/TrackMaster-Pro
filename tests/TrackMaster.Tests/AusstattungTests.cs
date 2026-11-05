using TrackMaster.Core;

namespace TrackMaster.Tests;

[Collection("Sprache")]
public class AusstattungTests
{
    [Fact]
    public void Alle_Grundmodelle_haben_Koerper_Flaechen_und_Kategorie()
    {
        var ids = new HashSet<string>();
        Assert.True(BuiltinModels.All.Count >= 20);
        foreach (var m in BuiltinModels.All)
        {
            Assert.True(ids.Add(m.Id), m.Id);
            Assert.True(m.IsBuiltin);
            Assert.Contains(m.Category, ModelLibrary.Categories());
            var tris = ModelMesh.Triangles(m).ToList();
            Assert.True(tris.Count >= 12, m.Name);
            var (w, d, h) = m.Size;
            Assert.True(w > 0.5 && d > 0.5 && h > 0.3, m.Name);
            Assert.All(tris, t => Assert.True(double.IsFinite(t.Triangle.A.X + t.Triangle.B.Y + t.Triangle.C.Z)));
        }
        foreach (var name in new[] { "Tunnelportal eingleisig", "Tunnelportal zweigleisig", "Hausbahnsteig", "Mittelbahnsteig", "Bahnsteigdach", "Stellwerk",
                     "Lokschuppen einständig", "Kastenbrücke", "Brückenkopf", "Stützmauer", "Bahnübergang", "Prellbock mit Erdhügel", "Scheune" })
            Assert.Contains(BuiltinModels.All, m => m.Name == name);
    }

    [Fact]
    public void Massstab_kommt_aus_der_Spur()
    {
        Assert.Equal(1000 / 87.0, ModelLibrary.MmPerMeter(Catalog.GetGauge("H0")), 6);
        Assert.Equal(1000 / 220.0, ModelLibrary.MmPerMeter(Catalog.GetGauge("Z")), 6);
        Assert.Equal(1000 / 22.5, ModelLibrary.MmPerMeter(Catalog.GetGauge("G")), 6);
    }

    [Fact]
    public void Modell_in_Standardgroesse_und_im_3D_Modell()
    {
        var plan = LayoutPlan.Empty("H0");
        var house = BuiltinModels.All.First(m => m.Id == "tm.wohnhaus");
        var item = ModelPlacement.Create(plan, house, 500, 300);
        Assert.Equal(house.Size.W * 1000 / 87, item.Width, 0);
        plan.Scenery.Add(item);
        var before = new Scene3DModel(LayoutPlan.Empty("H0")).TriangleCount;
        var model = new Scene3DModel(plan);
        Assert.True(model.TriangleCount > before + 50);
        // Hauptfarbe färbt die Wände um
        item.Color = "#B03A30";
        Assert.Contains(new Scene3DModel(plan).Meshes.Keys, k => k.Color == new Rgb(0xB0, 0x3A, 0x30));
        var shapes = ModelLibrary.Placed(plan, item)!.PlanShapes();
        Assert.Contains(shapes, s => s.Closed);
        Assert.Contains(shapes, s => !s.Closed);
    }

    [Fact]
    public void Bahnsteig_rastet_neben_dem_Gleis_ein_und_folgt_dem_Bogen()
    {
        var plan = SamplePlan.Build();
        var curve = plan.Pieces.First(p => p.Def.Kind == PieceKind.Curve && p.Level == 0);
        var leg = curve.WorldLegs()[0];
        var mid = leg.At(0.5);
        var g = plan.Gauge.TrackWidth;
        // Klick außen neben dem Bogen
        var (nx, ny) = (-Math.Sin(mid.HeadingRad), Math.Cos(mid.HeadingRad));
        var def = BuiltinModels.All.First(m => m.Id == "tm.hausbahnsteig");
        var r = ModelPlacement.Place(plan, def, mid.X + nx * g * 3, mid.Y + ny * g * 3, g * 10);
        Assert.True(r.Snapped);
        var item = r.Item;
        Assert.True(item.Points.Count > 5, "Bahnsteig im Bogen soll gebogen sein");
        // Gleismitte liegt nicht im Bahnsteig, die Kante hat den Abstand
        Assert.False(item.Contains(mid.X, mid.Y));
        var nearest = item.Outline().Min(p => Math.Sqrt((p.X - mid.X) * (p.X - mid.X) + (p.Y - mid.Y) * (p.Y - mid.Y)));
        Assert.True(nearest >= ModelPlacement.PlatformEdge * 1000 / 87 * 0.9, $"Kantenabstand {nearest}");
        // Modell-y zeigt vom Gleis weg: die Kante (−y) liegt am Gleis
        plan.Scenery.Add(item);
        var placed = ModelLibrary.Placed(plan, item)!;
        Assert.True(placed.Bent);
        var b = def.Bounds();
        var edge = placed.Map((b.X0 + b.X1) / 2, b.Y0);
        var back = placed.Map((b.X0 + b.X1) / 2, b.Y1);
        double D((double X, double Y) p) => Math.Sqrt((p.X - mid.X) * (p.X - mid.X) + (p.Y - mid.Y) * (p.Y - mid.Y));
        Assert.True(D(edge) < D(back));
        Assert.True(new Scene3DModel(plan).TriangleCount > 0);
    }

    [Fact]
    public void Bahnsteig_auf_der_anderen_Seite_zeigt_die_Kante_ebenfalls_zum_Gleis()
    {
        var plan = LayoutPlan.Empty("H0");
        var straight = Catalog.Gauges.First(x => x.Id == "H0").Systems.SelectMany(s => s.Pieces).First(p => p.Kind == PieceKind.Straight && p.Length > 150);
        var p = plan.Add(straight.Key);
        p.X = 200; p.Y = 400;
        var def = BuiltinModels.All.First(m => m.Id == "tm.hausbahnsteig");
        foreach (var side in new[] { -1, 1 })
        {
            var r = ModelPlacement.Place(plan, def, 200 + straight.Length / 2, 400 + side * 40, 100);
            Assert.True(r.Snapped);
            Assert.Empty(r.Item.Points);
            Assert.Equal(Math.Sign(r.Item.Y - 400), side);
            plan.Scenery.Add(r.Item);
            var placed = ModelLibrary.Placed(plan, r.Item)!;
            var b = def.Bounds();
            var edge = placed.Map(0, b.Y0);
            var back = placed.Map(0, b.Y1);
            Assert.True(Math.Abs(edge.Y - 400) < Math.Abs(back.Y - 400));
            plan.Scenery.Remove(r.Item);
        }
    }

    [Fact]
    public void Tunnelportal_rastet_am_Tunnelanfang_ein()
    {
        var plan = LayoutPlan.Empty("H0");
        var straight = Catalog.Gauges.First(x => x.Id == "H0").Systems.SelectMany(s => s.Pieces).First(p => p.Kind == PieceKind.Straight && p.Length > 150);
        var a = plan.Add(straight.Key);
        a.X = 100; a.Y = 300;
        var b = plan.Add(straight.Key);
        b.X = 100 + straight.Length; b.Y = 300; b.Tunnel = true;
        var def = BuiltinModels.All.First(m => m.Id == "tm.tunnelportal-1");
        var r = ModelPlacement.Place(plan, def, 100 + straight.Length - 20, 310, 60);
        Assert.True(r.Snapped);
        Assert.Equal(0, Angles.Normalize(r.Item.Rotation), 1);
        // Vorderseite der Portalmauer steht am Tunnelanfang, quer zum Gleis breit genug
        Assert.True(r.Item.Depth > plan.Gauge.TrackWidth * 4);
        plan.Scenery.Add(r.Item);
        var wall = def.Bodies.First(x => x.Shape == BodyShape.Bogenwand);
        var face = ModelLibrary.Placed(plan, r.Item)!.Map(ModelMesh.Footprint(wall).Min(q => q.X), 0);
        Assert.Equal(100 + straight.Length, face.X, 0);
        Assert.Equal(300, face.Y, 0);
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Category == Lang.T("Gebäude"));
    }

    [Fact]
    public void Bruecke_bekommt_die_Hoehe_des_Gleises()
    {
        var plan = LayoutPlan.Empty("H0");
        var straight = Catalog.Gauges.First(x => x.Id == "H0").Systems.SelectMany(s => s.Pieces).First(p => p.Kind == PieceKind.Straight && p.Length > 150);
        var a = plan.Add(straight.Key);
        a.X = 100; a.Y = 300; a.Height = 80;
        var def = BuiltinModels.All.First(m => m.Id == "tm.kastenbruecke");
        var r = ModelPlacement.Place(plan, def, 100 + straight.Length / 2, 305, 50);
        Assert.True(r.Snapped);
        Assert.Equal(80, r.Item.Height, 1);
    }

    [Fact]
    public void Plan_speichert_Modell_Farbe_Bogen_und_eigene_Modelle()
    {
        var plan = SamplePlan.Build();
        var own = new ModelDefinition
        {
            Id = ModelFile.NewId("Turm"), Name = "Turm", Category = "Test",
            Bodies =
            [
                new ModelBody { Shape = BodyShape.Vieleck, Points = [(-2, -2), (2, -2), (3, 1), (0, 3), (-3, 1)], Heights = [8, 8, 9, 10, 9], H = 8, Material = ModelMaterial.Naturstein },
                new ModelBody
                {
                    Shape = BodyShape.Freiform, Z = 8, Material = ModelMaterial.Schiefer,
                    Vertices = [(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0), (0, 0, 5)],
                    Faces = [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [0, 3, 2, 1]],
                },
            ],
        };
        var item = ModelPlacement.Create(plan, own, 300, 300);
        item.Color = "#C89A50";
        item.Id = plan.NextId++;
        plan.Scenery.Add(item);
        plan.Models[own.Id] = own;
        var bent = new SceneryItem { Kind = SceneryKind.Building, Model = "tm.hausbahnsteig", Points = [(0, 0), (100, 10), (200, 40)], Depth = 40, Height = 6, Id = plan.NextId++ };
        plan.Scenery.Add(bent);
        plan.Models["eigen.unbenutzt"] = new ModelDefinition { Id = "eigen.unbenutzt", Name = "x" };

        var json = PlanSerializer.ToJson(plan);
        Assert.DoesNotContain("eigen.unbenutzt", json);
        var back = PlanSerializer.FromJson(json, out _);
        var i2 = back.Scenery.First(s => s.Model == own.Id);
        Assert.Equal("#C89A50", i2.Color);
        Assert.True(back.Models.ContainsKey(own.Id));
        Assert.Equal(2, back.Models[own.Id].Bodies.Count);
        Assert.Equal(5, back.Models[own.Id].Bodies[1].Faces.Count);
        Assert.Equal(3, back.Scenery.First(s => s.Model == "tm.hausbahnsteig").Points.Count);
        // ältere Gebäude bleiben einfache Gebäude
        Assert.Contains(back.Scenery, s => s.Kind == SceneryKind.Building && !s.IsModel);
        Assert.True(new Scene3DModel(back).TriangleCount > 0);
    }

    [Fact]
    public void Modelldatei_und_Sammlung_lesen_und_schreiben()
    {
        var m = BuiltinModels.All.First(x => x.Id == "tm.stellwerk").Clone();
        m.Id = ModelFile.NewId("Mein Stellwerk");
        m.Name = "Mein Stellwerk";
        var json = ModelFile.ToJson(m);
        Assert.Contains("\"format\": \"modell-1\"", json);
        var back = ModelFile.FromJson(json);
        Assert.Equal(m.Bodies.Count, back.Bodies.Count);
        Assert.Equal(m.Size.W, back.Size.W, 3);
        var zip = ModelFile.ToZip([m, back]);
        var list = ModelFile.Read(zip, out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(2, list.Count);
        Assert.Throws<InvalidDataException>(() => ModelFile.FromJson("{\"format\":\"x\"}"));
    }

    /// <summary>Bogen aus dem Beispielplan mit seinen beiden Nachbarn: eine Kette gerade/Bogen/… über drei Gleise.</summary>
    private static (PlanSession Session, PlacedPiece Curve, HashSet<int> Ids) CurveChain()
    {
        var session = new PlanSession { IsPro = () => true };
        session.Replace(SamplePlan.Build(), null);
        var plan = session.Plan;
        var conn = Connectivity.Analyze(plan);
        var curve = plan.Pieces.First(p => p.Def.Kind == PieceKind.Curve && p.Level == 0
            && conn.Of(p.Id).Count(c => c.Link is not null) == 2);
        var ids = new HashSet<int> { curve.Id };
        foreach (var c in conn.Of(curve.Id)) if (c.Link is { } l) ids.Add(l.PieceId);
        return (session, curve, ids);
    }

    [Fact]
    public void Bruecke_entlang_gewaehlter_Gleise_folgt_dem_Bogen_und_der_Hoehe()
    {
        var (session, curve, ids) = CurveChain();
        var plan = session.Plan;
        foreach (var id in ids) plan.Find(id)!.Height = 80;
        session.Commit();
        foreach (var id in ids) session.Selection.Add(id);
        var length = TrackChain.Order(plan, Connectivity.Analyze(plan), ids).Sum(c => c.Length);

        var item = session.PlaceModelOnSelection("tm.kastenbruecke");
        Assert.NotNull(item);
        Assert.True(item!.Points.Count > 5, "Brücke im Bogen soll gebogen sein");
        Assert.Equal(length, PlacedModel.PathLength(item.Points), 0);
        Assert.Equal(80, item.Height, 0);
        // die Brücke liegt unter der Gleismitte des Bogens
        var mid = curve.WorldLegs()[0].At(0.5);
        Assert.Contains(item.Points, q => Math.Abs(q.X - mid.X) + Math.Abs(q.Y - mid.Y) < 2);
        Assert.Empty(session.Selection);
        Assert.Equal(item.Id, session.SelectedObject);
        Assert.True(new Scene3DModel(plan).TriangleCount > 0);
    }

    [Fact]
    public void Stuetzmauer_entlang_gewaehlter_Gleise_und_Seite_wechseln()
    {
        var (session, curve, ids) = CurveChain();
        var plan = session.Plan;
        foreach (var id in ids) session.Selection.Add(id);
        var item = session.PlaceModelOnSelection("tm.stuetzmauer");
        Assert.NotNull(item);
        Assert.True(item!.Points.Count > 5);
        var leg = curve.WorldLegs()[0];
        var (cx, cy) = leg.Center;
        double R((double X, double Y) q) => Math.Sqrt((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy));
        // Punkte im Bereich des Bogens: alle auf einer Seite der Gleismitte, mit Abstand
        var mid = leg.At(0.5);
        var near = item.Points.OrderBy(q => Math.Abs(q.X - mid.X) + Math.Abs(q.Y - mid.Y)).First();
        var before = R(near) - leg.Radius;
        Assert.True(Math.Abs(before) > plan.Gauge.TrackWidth, $"Abstand {before}");
        // Kante (Modell −y) liegt zum Gleis hin
        var def = ModelLibrary.Of(plan, item)!;
        void EdgeTowardsTrack()
        {
            var placed = ModelLibrary.Placed(plan, item)!;
            var b = def.Bounds();
            var m = (b.X0 + b.X1) / 2;
            Assert.True(Math.Abs(R(placed.Map(m, b.Y0)) - leg.Radius) < Math.Abs(R(placed.Map(m, b.Y1)) - leg.Radius));
        }
        EdgeTowardsTrack();

        Assert.True(session.CanFlipModelSide(item.Id));
        session.FlipModelSide(item.Id);
        var near2 = item.Points.OrderBy(q => Math.Abs(q.X - mid.X) + Math.Abs(q.Y - mid.Y)).First();
        var after = R(near2) - leg.Radius;
        Assert.True(Math.Sign(after) != Math.Sign(before), $"vorher {before}, nachher {after}");
        Assert.Equal(Math.Abs(before), Math.Abs(after), 0);
        EdgeTowardsTrack();
        // Rückgängig stellt die alte Seite wieder her
        session.Undo();
        var back = session.Plan.Scenery.First(s => s.Id == item.Id);
        var near3 = back.Points.OrderBy(q => Math.Abs(q.X - mid.X) + Math.Abs(q.Y - mid.Y)).First();
        Assert.Equal(before, R(near3) - leg.Radius, 0);
    }

    [Fact]
    public void Gerade_Stuetzmauer_wechselt_die_Seite()
    {
        var plan = LayoutPlan.Empty("H0");
        var straight = Catalog.Gauges.First(x => x.Id == "H0").Systems.SelectMany(s => s.Pieces).First(p => p.Kind == PieceKind.Straight && p.Length > 150);
        var p = plan.Add(straight.Key);
        p.X = 200; p.Y = 400;
        var def = BuiltinModels.All.First(m => m.Id == "tm.stuetzmauer");
        var r = ModelPlacement.AlongTracks(plan, def, new HashSet<int> { p.Id })!;
        Assert.Empty(r.Item.Points);
        Assert.Equal(straight.Length, r.Item.Width, 0);
        var y0 = r.Item.Y - 400;
        Assert.True(Math.Abs(y0) > plan.Gauge.TrackWidth);
        Assert.True(ModelPlacement.FlipSide(plan, def, r.Item));
        Assert.Equal(-y0, r.Item.Y - 400, 1);
        plan.Scenery.Add(r.Item);
        var placed = ModelLibrary.Placed(plan, r.Item)!;
        var b = def.Bounds();
        Assert.True(Math.Abs(placed.Map(0, b.Y0).Y - 400) < Math.Abs(placed.Map(0, b.Y1).Y - 400));
    }

    [Fact]
    public void Ohne_Auswahl_oder_bei_freien_Modellen_wird_wie_gewohnt_geklickt()
    {
        var (session, _, ids) = CurveChain();
        Assert.Null(session.PlaceModelOnSelection("tm.kastenbruecke"));
        foreach (var id in ids) session.Selection.Add(id);
        Assert.Null(session.PlaceModelOnSelection("tm.wohnhaus"));
        Assert.Null(session.PlaceModelOnSelection("tm.tunnelportal-1"));
        Assert.Equal(ids.Count, session.Selection.Count);
    }

    [Fact]
    public void Bruecke_per_Klick_im_Bogen_wird_gebogen()
    {
        var plan = SamplePlan.Build();
        var curve = plan.Pieces.First(p => p.Def.Kind == PieceKind.Curve && p.Level == 0);
        var mid = curve.WorldLegs()[0].At(0.5);
        var def = BuiltinModels.All.First(m => m.Id == "tm.kastenbruecke");
        var r = ModelPlacement.Place(plan, def, mid.X, mid.Y, 20);
        Assert.True(r.Snapped);
        Assert.True(r.Item.Points.Count > 5);
    }

    [Fact]
    public void Basis_darf_Grundmodelle_einfuegen_aber_keine_eigenen_aus_der_Bibliothek()
    {
        var own = new ModelDefinition { Id = "eigen.test", Name = "Test", Bodies = [new ModelBody { Shape = BodyShape.Quader, W = 4, D = 4, H = 4 }] };
        ModelLibrary.PutOwn(own);
        try
        {
            var session = new PlanSession { IsPro = () => false };
            string? needed = null;
            session.ProRequired += f => needed = f;
            session.ModelId = "tm.wohnhaus";
            Assert.NotNull(session.PlaceModel(500, 500, 20));
            session.ModelId = own.Id;
            Assert.Null(session.PlaceModel(700, 500, 20));
            Assert.NotNull(needed);
            session.IsPro = () => true;
            var item = session.PlaceModel(700, 500, 20);
            Assert.NotNull(item);
            Assert.True(session.Plan.Models.ContainsKey(own.Id));
            // im Plan mitgespeichert: geht auch wieder in der Basis
            session.IsPro = () => false;
            Assert.NotNull(session.PlaceModel(900, 500, 20));
        }
        finally { ModelLibrary.RemoveOwn(own.Id); }
    }

    [Fact]
    public void Alle_Koerperformen_liefern_Dreiecke_und_Grundriss()
    {
        foreach (var shape in Enum.GetValues<BodyShape>())
        {
            var b = new ModelBody { Shape = shape, W = 4, D = 3, H = 2, OpeningW = 2, OpeningH = 1.5, Points = [(0, 0), (2, 0), (1, 2)], Vertices = [(0, 0, 0), (1, 0, 0), (0, 1, 1)], Faces = [[0, 1, 2]] };
            Assert.NotEmpty(ModelMesh.Body(b));
            Assert.True(ModelMesh.Footprint(b).Count >= 3, shape.ToString());
            Assert.True(ModelMesh.TopOf(b) > 0, shape.ToString());
        }
    }
}

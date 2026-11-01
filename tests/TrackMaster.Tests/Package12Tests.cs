using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

/// <summary>Raum, Hintergrundbild, Sperren und Gruppen, Ausrichten, Parallelgleis, Höhenprofil, Nutzlänge, Rückmelder, Preise, DXF.</summary>
public class Package12Tests
{
    private static PlanSession Session(double w = 3000, double h = 2000)
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, w, h, 600, 0));
        return s;
    }

    private static string Straight(PlanSession s) => s.CurrentSystem.Pieces.First(p => p.Kind == PieceKind.Straight).Key;
    private static string Curve(PlanSession s) => s.CurrentSystem.Pieces.First(p => p.Kind == PieceKind.Curve).Key;

    /// <summary>Kette aus drei Geraden ab (500, 500) nach rechts.</summary>
    private static List<PlacedPiece> Line(PlanSession s, int n = 3, double x = 500, double y = 500)
    {
        s.Drop(Straight(s), x, y, 1);
        var list = new List<PlacedPiece> { s.SelectedPieces[0] };
        for (var i = 1; i < n; i++) { s.Attach(Straight(s), (0, 0)); list.Add(s.SelectedPieces[0]); }
        return list;
    }

    private static readonly byte[] Png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Neue_Objekte_ueberstehen_Speichern_und_Laden()
    {
        var s = Session();
        var pieces = Line(s);
        pieces[0].Locked = true;
        pieces[1].Group = 3; pieces[2].Group = 3;
        s.AddRoom(-500, -500, 4000, 3000, 150);
        s.AddScenery(RoomPlan.Element(ObstacleType.Heizkoerper, 100, -400));
        s.AddDetector(pieces[1].Id);
        s.Plan.TrainLength = 1200;
        s.Plan.Prices[pieces[0].Key] = 4.5;
        s.Plan.Stock[pieces[0].Key] = 2;
        s.Plan.Background = ImageInfo.Background(Png1x1, s.Plan)!;
        s.Plan.Background.Opacity = 0.3;

        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out var skipped);
        Assert.Equal(0, skipped);
        Assert.True(back.Pieces[0].Locked);
        Assert.Equal(3, back.Pieces[2].Group);
        var room = Assert.Single(back.Scenery, x => x.Kind == SceneryKind.Room);
        Assert.Equal(4, room.Points.Count);
        Assert.Equal(150, room.Width);
        var heater = Assert.Single(back.Scenery, x => x.Kind == SceneryKind.Obstacle);
        Assert.Equal(ObstacleType.Heizkoerper, heater.Obstacle);
        var det = Assert.Single(back.Detectors);
        Assert.Equal(pieces[1].Id, det.PieceId);
        Assert.Equal(1, det.Address);
        Assert.Equal(1200, back.TrainLength);
        Assert.Equal(4.5, back.Prices[pieces[0].Key]);
        Assert.Equal(2, back.Stock[pieces[0].Key]);
        Assert.NotNull(back.Background);
        Assert.Equal(1, back.Background!.PixelWidth);
        Assert.Equal(0.3, back.Background.Opacity, 3);
        Assert.Equal(s.Plan.Background.Data, back.Background.Data);
    }

    [Fact]
    public void Rueckgaengig_speichert_das_Hintergrundbild_nur_als_Verweis()
    {
        var s = Session();
        var big = new byte[200_000];
        Png1x1.CopyTo(big, 0);
        s.SetBackground(ImageInfo.Background(big, s.Plan));
        var json = PlanSerializer.ToJson(s.Plan, withIds: true);
        Assert.True(json.Length < 10_000);
        s.EditBackground(b => b.Opacity = 0.9);
        s.Undo();
        Assert.Equal(0.5, s.Plan.Background!.Opacity, 3);
        Assert.Equal(Convert.ToBase64String(big), s.Plan.Background.Data);
        s.Undo();
        Assert.Null(s.Plan.Background);
    }

    [Fact]
    public void Bildgroesse_aus_dem_Dateikopf()
    {
        Assert.Equal((1, 1, "image/png"), ImageInfo.Read(Png1x1));
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x01, 0x2C, 0x02, 0x58, 3, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal((600, 300, "image/jpeg"), ImageInfo.Read(jpeg));
        Assert.Null(ImageInfo.Read([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void Hintergrundbild_nach_Massline_skalieren()
    {
        var s = Session();
        s.Plan.Background = new PlanBackground { Data = "x", PixelWidth = 1000, PixelHeight = 500, X = 0, Y = 0, Width = 1000 };
        var dim = s.AddDimension((100, 100), (300, 100));
        Assert.True(s.ScaleBackground(400));
        Assert.Equal(2000, s.Plan.Background.Width, 3);
        Assert.Equal(-100, s.Plan.Background.X, 3);
        Assert.Equal(1000, s.Plan.Background.Height, 3);
        Assert.Equal(500, dim.Points[1].X, 3);
    }

    [Fact]
    public void Gesperrte_Gleise_bleiben_liegen()
    {
        var s = Session();
        var pieces = Line(s);
        s.Select(pieces.Select(p => p.Id), false);
        s.ToggleLock();
        Assert.All(s.Plan.Pieces, p => Assert.True(p.Locked));
        var x = pieces[0].X;
        s.Nudge(10, 0, true);
        s.Rotate(15);
        s.Delete();
        Assert.Equal(3, s.Plan.Pieces.Count);
        Assert.Equal(x, pieces[0].X);
        s.BeginMove();
        s.MoveBy(50, 50);
        s.EndMove(1);
        Assert.Equal(x, pieces[0].X);
        s.ToggleLock();
        s.Delete();
        Assert.Empty(s.Plan.Pieces);
    }

    [Fact]
    public void Klick_auf_ein_Gruppenmitglied_waehlt_die_Gruppe()
    {
        var s = Session();
        var pieces = Line(s, 4);
        s.Select([pieces[1].Id, pieces[2].Id], false);
        s.GroupSelection();
        s.Select([pieces[2].Id], false);
        Assert.Equal(2, s.Selection.Count);
        s.ClearSelection();
        s.Toggle(pieces[1].Id);
        Assert.Contains(pieces[2].Id, s.Selection);
        s.Toggle(pieces[1].Id);
        Assert.Empty(s.Selection);
        s.Select([pieces[1].Id], false);
        s.Ungroup();
        s.Select([pieces[1].Id], false);
        Assert.Single(s.Selection);
    }

    [Fact]
    public void Ausrichten_verschiebt_getrennte_Gruppen()
    {
        var s = Session();
        var a = Line(s, 2, 500, 400);
        var b = Line(s, 2, 800, 900);
        s.Select(a.Concat(b).Select(p => p.Id), false);
        s.Align(AlignMode.Left);
        var boxA = Arrange.Box(a);
        var boxB = Arrange.Box(b);
        Assert.Equal(boxA.X0, boxB.X0, 3);
        // Verbundene Gleise bleiben verbunden
        Assert.Equal(0, s.Connections.Open.Count(c => c.PieceId == a[0].Id && c.Index == 1));
        s.Align(AlignMode.Top);
        Assert.Equal(Arrange.Box(a).Y0, Arrange.Box(b).Y0, 3);
    }

    [Fact]
    public void Parallelgleis_neben_Gerade_und_Bogen()
    {
        var s = Session();
        s.Drop(Straight(s), 500, 1000, 1);
        var first = s.SelectedPieces[0];
        s.Attach(Curve(s), (0, 0));
        s.Attach(Straight(s), (0, 0));
        var ids = s.Plan.Pieces.Select(p => p.Id).ToList();
        var length = s.Plan.Pieces.Sum(p => p.Def.TrackLength);
        s.Select(ids, false);
        Assert.True(s.AddParallel(80, left: true));
        var added = s.Plan.Pieces.Where(p => !ids.Contains(p.Id)).ToList();
        Assert.NotEmpty(added);
        Assert.All(added, p => Assert.True(p.Def.IsFlex));
        // Anfang 80 mm neben dem Anfang des ersten Gleises, quer zur Fahrtrichtung
        var c0 = first.WorldConnectors()[0];
        var p0 = added[0].WorldConnectors()[0];
        Assert.Equal(80, Math.Sqrt(Math.Pow(p0.X - c0.X, 2) + Math.Pow(p0.Y - c0.Y, 2)), 1);
        // Der Abstand bleibt überall gleich: jeder Punkt des Parallelgleises liegt 80 mm von der Kette entfernt
        var legs = ids.SelectMany(i => s.Plan.Find(i)!.WorldLegs()).ToList();
        foreach (var l in added.SelectMany(p => p.WorldLegs()))
            for (var t = 0.0; t <= 1; t += 0.25)
            {
                var q = l.At(t);
                Assert.Equal(80, legs.Min(k => k.DistanceTo(q.X, q.Y)), 0);
            }
        Assert.NotEqual(length, added.Sum(p => p.Def.TrackLength), 0);
    }

    [Fact]
    public void Hoehenprofil_einer_Rampe()
    {
        var s = Session();
        var pieces = Line(s, 4);
        s.Select(pieces.Select(p => p.Id), false);
        s.ApplyGradient(0, 2, false);
        var profile = s.Profile()!;
        Assert.Equal(4, profile.Steps.Count);
        Assert.Equal(5, profile.Points.Count);
        Assert.Equal(pieces.Sum(p => p.Def.TrackLength), profile.Length, 1);
        Assert.Equal(2, profile.MaxGradePercent, 1);
        Assert.True(Math.Abs(profile.Points[^1].Height - profile.Points[0].Height) > 1);
        var chart = profile.Chart(600, 240);
        Assert.Equal(5, chart.Line.Count);
        Assert.All(chart.Line, p => Assert.InRange(p.X, chart.Left - 0.01, chart.Right + 0.01));
        Assert.NotEmpty(chart.HeightTicks);
        Assert.NotEmpty(chart.Grades);
    }

    [Fact]
    public void Nutzlaenge_bis_zur_Weiche_und_Pruefung()
    {
        var s = Session();
        var pieces = Line(s, 3);
        var buffer = s.CurrentSystem.Pieces.First(p => p.Kind == PieceKind.Buffer).Key;
        s.Attach(buffer, (0, 0));
        var run = UsableLength.For(s.Plan, pieces[0].Id)!;
        Assert.Equal(4, run.PieceIds.Count);
        Assert.True(run.EndsAtBuffer);
        s.Plan.TrainLength = run.Length + 500;
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Category == Lang.T("Nutzlänge"));
        Assert.Contains(Lang.T("zu kurz"), UsableLength.Describe(s.Plan, run));
        s.Plan.TrainLength = run.Length - 10;
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == Lang.T("Nutzlänge"));
    }

    [Fact]
    public void Rueckmelder_in_Stueckliste_Kabelplan_und_Steuerung()
    {
        var s = Session();
        var pieces = Line(s, 2);
        s.AddDetector(pieces[0].Id);
        s.DetectorType = DetectorType.Reed;
        s.AddDetector(pieces[1].Id);
        Assert.Equal(2, s.Plan.Detectors.Count);
        Assert.Equal(2, s.Plan.Detectors[1].Address);
        Assert.Contains(BillOfMaterials.Accessories(s.Plan), a => a.Name.Contains(Detector.InputsPerModule.ToString()));
        Assert.Contains("RM2", Electrics.WiringPlan(s.Plan));
        Assert.Contains("\"detectors\"", TrainRoutes.ToJson(s.Plan));
        // Kopieren und Einfügen nimmt Rückmelder mit
        s.Select(pieces.Select(p => p.Id), false);
        var json = s.CopySelection();
        s.Paste(json);
        Assert.Equal(4, s.Plan.Detectors.Count);
        // Gleis löschen entfernt den Rückmelder
        s.Select([pieces[0].Id], false);
        s.Delete();
        Assert.Equal(3, s.Plan.Detectors.Count);
    }

    [Fact]
    public void Preise_Bestand_und_Summen()
    {
        var s = Session();
        var pieces = Line(s, 3);
        var key = pieces[0].Key;
        s.SetCosts([(key, 3.5, 1)]);
        var row = Assert.Single(BillOfMaterials.Costs(s.Plan), r => r.Key == key);
        Assert.Equal(3, row.Count);
        Assert.Equal(2, row.ToBuy);
        var (all, buy, without) = BillOfMaterials.Totals(BillOfMaterials.Costs(s.Plan));
        Assert.Equal(10.5, all, 3);
        Assert.Equal(7, buy, 3);
        Assert.Equal(0, without);
        var csv = BillOfMaterials.ToCsv(s.Plan);
        Assert.Contains(Lang.T("Zu kaufen"), csv);
        Assert.Contains(BillOfMaterials.Money(7), csv);
        // Preisliste einlesen
        var number = pieces[0].Def.Number;
        Assert.Equal(1, s.ImportPrices($"Artikel;Preis\n{number};\"12,90 €\"\nunbekannt;1"));
        Assert.Equal(12.9, s.Plan.Prices[key], 3);
        Assert.Equal(1234.5, BillOfMaterials.ParseMoney("1.234,50"));
        Assert.Equal(1234.5, BillOfMaterials.ParseMoney("1,234.50 EUR"));
        Assert.Null(BillOfMaterials.ParseMoney("abc"));
    }

    [Fact]
    public void Raumpruefung_meldet_Tuer_unter_der_Platte_und_engen_Gang()
    {
        var s = Session(2000, 1000);
        s.AddRoom(-400, -100, 2700, 1300, 120);
        var issues = PlanCheck.Run(s.Plan);
        Assert.Contains(issues, i => i.Category == Lang.T("Gang"));
        Assert.DoesNotContain(issues, i => i.Level == IssueLevel.Fehler && i.Category == Lang.T("Raum"));
        s.AddScenery(RoomPlan.Element(ObstacleType.Tuer, 1900, 950));
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Level == IssueLevel.Fehler && i.Text.Contains(Lang.T("Tür")));
        // Fenster dürfen über der Platte liegen
        s.Plan.Scenery.RemoveAll(x => x.Kind == SceneryKind.Obstacle);
        s.AddScenery(RoomPlan.Element(ObstacleType.Fenster, 1000, 0));
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == Lang.T("Raum"));
        // Raum zu klein
        s.Plan.Scenery.Clear();
        s.AddRoom(0, 0, 1500, 1000, 120);
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Level == IssueLevel.Fehler && i.Category == Lang.T("Raum"));
    }

    [Fact]
    public void Raumelemente_lassen_sich_verschieben_und_drehen()
    {
        var s = Session();
        var door = s.AddScenery(RoomPlan.Element(ObstacleType.Tuer, 100, 100));
        door.RotateAround(100, 100, 90);
        Assert.Equal(90, door.Rotation);
        door.MoveBy(10, 0);
        Assert.Equal(110, door.Center().X);
        var room = s.AddRoom(0, 0, 1000, 1000, 100);
        Assert.Equal(500, room.Center().X);
        Assert.True(RoomPlan.HitsWall(room, 1020, 500, 5));
        Assert.False(RoomPlan.HitsWall(room, 500, 500, 5));
    }

    [Fact]
    public void Dxf_enthaelt_Platte_Gleise_und_Raum()
    {
        var s = Session();
        s.Drop(Straight(s), 500, 500, 1);
        s.Attach(Curve(s), (0, 0));
        s.AddRoom(-300, -300, 3600, 2600, 120);
        s.AddLabel("Bahnhof", 800, 300);
        var dxf = DxfExport.Build(s.Plan);
        Assert.StartsWith("0\r\nSECTION", dxf);
        Assert.Contains("\r\nARC\r\n", dxf);
        Assert.Contains("\r\nLINE\r\n", dxf);
        Assert.Contains("RAUM", dxf);
        Assert.Contains("Bahnhof", dxf);
        Assert.EndsWith("0\r\nEOF\r\n", dxf);
        // Bögen: Endpunkt in CAD-Koordinaten stimmt mit dem Gleisende überein
        var curve = s.Plan.Pieces[1].WorldLegs()[0];
        var lines = dxf.Split("\r\n");
        var i = Array.IndexOf(lines, "ARC");
        double V(int code) { for (var k = i; k < lines.Length; k += 1) if (lines[k] == code.ToString() && k % 2 == 0) return double.Parse(lines[k + 1], System.Globalization.CultureInfo.InvariantCulture); return double.NaN; }
        var (cx, cy, r, a0, a1) = (V(10), V(20), V(40), V(50), V(51));
        var ends = new[] { a0, a1 }.Select(a => (X: cx + r * Math.Cos(a * Math.PI / 180), Y: -(cy + r * Math.Sin(a * Math.PI / 180)))).ToList();
        var e = curve.Offset(0);
        Assert.Contains(ends, q => Math.Abs(q.X - e.X0) + Math.Abs(q.Y - e.Y0) < 0.5 || Math.Abs(q.X - e.X1) + Math.Abs(q.Y - e.Y1) < 0.5);
        Assert.Equal(4, DxfExport.Outset([(0, 0), (100, 0), (100, 100), (0, 100)], 10).Count);
        Assert.Equal(-10, DxfExport.Outset([(0, 0), (100, 0), (100, 100), (0, 100)], 10)[0].X, 3);
    }
}

namespace TrackMaster.Core;

/// <summary>Beispielanlage „Bahnhof Neustadt“ aus Märklin C-Gleis: Oval mit Ladegleis außen und Bw-Gleis innen.</summary>
public static class SamplePlan
{
    public static LayoutPlan Build()
    {
        var plan = new LayoutPlan { Name = "Bahnhof Neustadt", GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1250 };

        PlacedPiece Add(string nr, Pose at, bool flip = false)
        {
            var p = plan.Add("H0|mc|" + nr);
            p.Flipped = flip;
            PlanEditing.PlaceAt(p, 0, at.X, at.Y, at.Heading);
            return p;
        }
        Pose End(PlacedPiece p, int k) => p.WorldConnectors()[Math.Min(k, p.WorldConnectors().Count - 1)];
        Pose Chain(Pose start, IEnumerable<string> numbers, bool flip = false)
        {
            var e = start;
            foreach (var nr in numbers)
                e = End(Add(nr, e, flip), 1);
            return e;
        }
        string[] Repeat(string nr, int n) => Enumerable.Repeat(nr, n).ToArray();

        var w1 = Add("24612", new Pose(600, 1080, 0));
        var e = Chain(End(w1, 1), [.. Repeat("24188", 3), .. Repeat("24230", 6), "24188"]);
        var w2 = Add("24611", e);
        Chain(End(w2, 1), [.. Repeat("24188", 2), .. Repeat("24230", 6)]);
        Chain(End(w1, 2), ["24215", "24207", "24188", "24188", "24977"]);
        Chain(End(w2, 2), ["24215", "24207", "24188", "24977"], flip: true);

        // Gelände und Elektrik, damit die Pro-Funktionen etwas zu zeigen haben
        SceneryItem Scenery(SceneryItem item)
        {
            item.Id = plan.NextId++;
            plan.Scenery.Add(item);
            return item;
        }
        Scenery(new SceneryItem { Kind = SceneryKind.Terrain, Name = "Hügel", Height = 110, Points = [(420, 470), (640, 400), (780, 520), (760, 760), (600, 880), (430, 800), (380, 640)] });
        Scenery(new SceneryItem { Kind = SceneryKind.Water, Name = "Teich", Points = [(1240, 700), (1380, 640), (1520, 690), (1540, 820), (1420, 900), (1270, 860)] });
        Scenery(new SceneryItem { Kind = SceneryKind.Building, Name = "Empfangsgebäude", X = 1060, Y = 110, Width = 300, Depth = 100, Height = 110 });
        Scenery(new SceneryItem { Kind = SceneryKind.Building, Name = "Lokschuppen", X = 1000, Y = 470, Width = 260, Depth = 120, Height = 95, Rotation = 0 });
        Scenery(new SceneryItem { Kind = SceneryKind.Building, Name = "Wohnhaus", X = 1560, Y = 120, Width = 140, Depth = 110, Height = 120, Rotation = 8 });
        foreach (var (x, y, h) in new[] { (500.0, 560.0, 90.0), (640, 640, 110), (560, 760, 80), (1220, 560, 100), (1600, 560, 90), (1660, 980, 110), (300, 1180, 90), (1880, 160, 100) })
            Scenery(new SceneryItem { Kind = SceneryKind.Tree, X = x, Y = y, Width = h * 0.6, Depth = h * 0.6, Height = h, Name = "Baum" });

        plan.Feeders.Add(new Feeder { Id = plan.NextId++, PieceId = 3, Circuit = "Fahrstrom" });
        plan.Signals.Add(new Signal { Id = plan.NextId++, PieceId = 4, Index = 1, Type = SignalType.Hauptsignal, Name = "A1" });
        plan.Signals.Add(new Signal { Id = plan.NextId++, PieceId = 14, Index = 1, Type = SignalType.Ausfahrsignal, Name = "N1" });
        plan.Signals.Add(new Signal { Id = plan.NextId++, PieceId = 2, Index = 0, Type = SignalType.Vorsignal, Name = "V1" });
        return plan;
    }
}

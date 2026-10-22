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
        return plan;
    }
}

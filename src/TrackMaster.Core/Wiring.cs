namespace TrackMaster.Core;

/// <summary>Wofür ein selbst verlegtes Kabel dient; bestimmt die vorgeschlagene Aderfarbe.</summary>
public enum WireUse { Fahrstrom, Rueckleiter, Weichen, Licht, Rueckmelder, Sonstiges }

/// <summary>Eine Aderfarbe zur Auswahl.</summary>
public sealed record WireColor(string Name, string Hex);

/// <summary>
/// Selbst verlegte Kabel (<see cref="SceneryKind.Wire"/>): Linienzug von Punkt zu Punkt, Länge, Farbe und Querschnitt.
/// Die Enden rasten an Einspeisungen, Zentrale/Booster (Bahnstrom und Masse), Verteilern, Rückmeldern, Signalen, Gleisenden und anderen Kabeln ein.
/// Ein Ende auf einem Gleis legt dort eine Einspeisung an (siehe <see cref="PlanSession.AddWire"/>).
/// </summary>
public static class WireLayout
{
    /// <summary>Zuschlag für Durchführungen, Schlaufen und Anschlüsse, wenn man die Kabellänge einkauft.</summary>
    public const double Reserve = 0.15;

    public static IReadOnlyList<WireColor> Colors { get; } =
    [
        new("rot", "#D32F2F"), new("braun", "#795548"), new("blau", "#1E5BD8"), new("schwarz", "#212121"),
        new("gelb", "#F2C200"), new("grün", "#2E7D32"), new("weiß", "#F5F5F5"), new("orange", "#EF6C00"),
        new("grau", "#8A8A8A"), new("violett", "#7B1FA2"),
    ];

    public static string UseName(WireUse use) => use switch
    {
        WireUse.Fahrstrom => Lang.T("Fahrstrom"),
        WireUse.Rueckleiter => Lang.T("Rückleiter / Masse"),
        WireUse.Weichen => Lang.T("Weichen und Signale"),
        WireUse.Licht => Lang.T("Licht und Zubehör"),
        WireUse.Rueckmelder => Lang.T("Rückmelder"),
        _ => Lang.T("Sonstiges"),
    };

    /// <summary>
    /// Übliche Aderfarbe: Wechselstrom (Märklin) rot = Fahrstrom am Mittelleiter, braun = Masse; Gleichstrom rot und blau
    /// (bzw. braun) für die beiden Schienen; gelb Weichen und Zubehör, grau/weiß Licht, violett Rückmelder.
    /// </summary>
    public static string DefaultColor(WireUse use, CurrentKind current) => use switch
    {
        WireUse.Fahrstrom => "#D32F2F",
        WireUse.Rueckleiter => current == CurrentKind.Wechselstrom ? "#795548" : "#1E5BD8",
        WireUse.Weichen => "#F2C200",
        WireUse.Licht => "#8A8A8A",
        WireUse.Rueckmelder => "#7B1FA2",
        _ => "#212121",
    };

    /// <summary>Vorgeschlagener Querschnitt in mm²: Fahrstrom 0,25 (große Spuren 0,5), sonst 0,14.</summary>
    public static double DefaultSection(WireUse use, Gauge gauge)
        => use is WireUse.Fahrstrom or WireUse.Rueckleiter ? (gauge.TrackWidth >= 30 ? 0.5 : 0.25) : 0.14;

    public static WireUse Use(SceneryItem wire) => Enum.IsDefined((WireUse)wire.Variant) ? (WireUse)wire.Variant : WireUse.Sonstiges;

    public static string ColorOf(SceneryItem wire, CurrentKind current)
        => ModelBody.ParseColor(wire.Color) is { } c ? ModelBody.ToHex(c) : DefaultColor(Use(wire), current);

    public static string ColorName(string hex)
        => Colors.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase))?.Name is { } n ? Lang.T(n) : hex;

    public static double Section(SceneryItem wire, Gauge gauge) => wire.Depth > 0 ? wire.Depth : DefaultSection(Use(wire), gauge);

    /// <summary>Länge des Linienzugs in mm.</summary>
    public static double Length(SceneryItem wire)
    {
        double sum = 0;
        for (var i = 1; i < wire.Points.Count; i++)
            sum += Math.Sqrt(Sq(wire.Points[i].X - wire.Points[i - 1].X) + Sq(wire.Points[i].Y - wire.Points[i - 1].Y));
        return sum;
    }

    /// <summary>Kürzester Abstand eines Punkts zum Linienzug.</summary>
    public static double Distance(SceneryItem wire, double x, double y)
    {
        var pts = wire.Points;
        if (pts.Count == 0) return double.MaxValue;
        if (pts.Count == 1) return Math.Sqrt(Sq(x - pts[0].X) + Sq(y - pts[0].Y));
        var best = double.MaxValue;
        for (var i = 1; i < pts.Count; i++) best = Math.Min(best, SegmentDistance(pts[i - 1], pts[i], x, y));
        return best;
    }

    /// <summary>Punkt in der Mitte der Länge (für Beschriftung und Drehen).</summary>
    public static (double X, double Y) Middle(SceneryItem wire)
    {
        var pts = wire.Points;
        if (pts.Count == 1) return pts[0];
        var half = Length(wire) / 2;
        for (var i = 1; i < pts.Count; i++)
        {
            var len = Math.Sqrt(Sq(pts[i].X - pts[i - 1].X) + Sq(pts[i].Y - pts[i - 1].Y));
            if (len >= half && len > 0)
            {
                var t = half / len;
                return (pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t);
            }
            half -= len;
        }
        return pts[^1];
    }

    /// <summary>Ein Punkt, an dem Kabelenden einrasten, mit Beschreibung für Kabelplan und Statuszeile.</summary>
    public sealed record Anchor(double X, double Y, string Text);

    /// <summary>Einrastpunkte: Einspeisungen, Rückmelder, Signale, Gleisenden und Enden anderer Kabel.</summary>
    public static IReadOnlyList<Anchor> Anchors(LayoutPlan plan, int? skipWire = null)
    {
        var g = plan.Gauge.TrackWidth;
        var list = new List<Anchor>();
        foreach (var f in plan.Feeders)
            if (plan.Find(f.PieceId) is { } p)
            {
                var (x, y) = Electrics.FeederPoint(plan, p, f);
                list.Add(new(x, y, f.Ground
                    ? (f.Name.Length > 0 ? Lang.F($"Masse {f.Name} an Gleis Nr. {p.Id}") : Lang.F($"Masse an Gleis Nr. {p.Id}"))
                    : (f.Name.Length > 0 ? Lang.F($"Einspeisung {f.Name} an Gleis Nr. {p.Id}") : Lang.F($"Einspeisung an Gleis Nr. {p.Id}"))));
            }
        foreach (var src in PowerSupply.Sources(plan))
        {
            var type = PowerSupply.Type(src);
            var name = Lang.F($"{src.Name} ({PowerSupply.TypeName(type)})");
            var (x, y) = PowerSupply.Terminal(src);
            list.Add(new(x, y, PowerSupply.HasGround(type) ? Lang.F($"{name} – Bahnstrom (B)") : name));
            if (PowerSupply.GroundTerminal(src) is { } gt) list.Add(new(gt.X, gt.Y, Lang.F($"{name} – Masse (0)")));
        }
        foreach (var mod in PowerSupply.Modules(plan))
            if (Decoders.ModelOf(mod) is { } m)
            {
                var ports = Decoders.Ports(mod);
                for (var i = 0; i < ports.Count; i++) list.Add(new(ports[i].X, ports[i].Y, $"{mod.Name} ({m.Id}) – {Decoders.PortName(m, i)}"));
                if (Decoders.FeedTerminals(mod) is { } ft)
                {
                    list.Add(new(ft.Track.X, ft.Track.Y, Lang.F($"{mod.Name} – Bahnstrom (B) vom Booster")));
                    list.Add(new(ft.Ground.X, ft.Ground.Y, Lang.F($"{mod.Name} – Masse (0)")));
                }
            }
        foreach (var d in PowerSupply.Distributors(plan))
        {
            var name = Lang.F($"{d.Name} ({ColorName(PowerSupply.DistributorColor(d))})");
            foreach (var (x, y) in PowerSupply.ClampPoints(d)) list.Add(new(x, y, name));
        }
        foreach (var d in plan.Detectors)
            if (plan.Find(d.PieceId) is { } p)
            {
                var (x, y) = Electrics.DetectorPoint(p, g);
                list.Add(new(x, y, d.Name.Length > 0 ? Lang.F($"Rückmelder {d.Name}") : Lang.F($"Rückmelder Nr. {d.Id}")));
            }
        foreach (var s in plan.Signals)
        {
            var (x, y, _) = Electrics.SignalPose(plan, s);
            list.Add(new(x, y, s.Name.Length > 0 ? $"Signal {s.Name}" : Lang.F($"Signal Nr. {s.Id}")));
        }
        foreach (var p in plan.Pieces.Where(p => p.Def.Drives > 0))
        {
            var m = p.Middle();
            list.Add(new(m.X + Math.Sin(m.HeadingRad) * g * 3.4, m.Y - Math.Cos(m.HeadingRad) * g * 3.4, Lang.F($"Weichenantrieb Gleis Nr. {p.Id}")));
        }
        foreach (var w in plan.Scenery.Where(s => s.IsWire && s.Id != skipWire && s.Points.Count >= 2))
        {
            list.Add(new(w.Points[0].X, w.Points[0].Y, Lang.F($"{w.Name} (Anfang)")));
            list.Add(new(w.Points[^1].X, w.Points[^1].Y, Lang.F($"{w.Name} (Ende)")));
        }
        return list;
    }

    /// <summary>
    /// Rechtwinklig weiterlegen: ein freier Punkt rastet waagerecht oder senkrecht zum letzten Punkt ein; ein fester Punkt
    /// (Einrastpunkt) bekommt bei Bedarf einen Knick davor. Liefert die anzuhängenden Punkte.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> OrthoStep(IReadOnlyList<(double X, double Y)> sofar, (double X, double Y) target, bool fixedTarget)
    {
        if (sofar.Count == 0) return [target];
        var last = sofar[^1];
        double dx = target.X - last.X, dy = target.Y - last.Y;
        if (Math.Abs(dx) < 0.5 || Math.Abs(dy) < 0.5) return [target];
        // Knick so legen, dass das vorige Stück nicht in sich zurückläuft: nach einem waagerechten Stück erst senkrecht
        var prevHorizontal = sofar.Count >= 2 && Math.Abs(sofar[^2].Y - last.Y) < 0.5;
        var prevVertical = sofar.Count >= 2 && Math.Abs(sofar[^2].X - last.X) < 0.5;
        var horizontalFirst = prevHorizontal ? false : prevVertical ? true : Math.Abs(dx) >= Math.Abs(dy);
        if (!fixedTarget) return [Math.Abs(dx) >= Math.Abs(dy) ? (target.X, last.Y) : (last.X, target.Y)];
        return [horizontalFirst ? (target.X, last.Y) : (last.X, target.Y), target];
    }

    /// <summary>Linienzug rechtwinklig machen: zwischen schräg zueinander liegenden Punkten einen Knick einfügen.</summary>
    public static List<(double X, double Y)> Orthogonalize(IReadOnlyList<(double X, double Y)> points)
    {
        var list = new List<(double X, double Y)>();
        foreach (var p in points) list.AddRange(OrthoStep(list, p, true));
        return list;
    }

    /// <summary>Rastet einen Punkt am nächsten Einrastpunkt im Umkreis <paramref name="tolerance"/> ein.</summary>
    public static (double X, double Y, Anchor? Anchor) Snap(LayoutPlan plan, double x, double y, double tolerance, int? skipWire = null)
    {
        Anchor? best = null;
        var bestD = tolerance;
        foreach (var a in Anchors(plan, skipWire))
        {
            var d = Math.Sqrt(Sq(a.X - x) + Sq(a.Y - y));
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best is null ? (Math.Round(x), Math.Round(y), null) : (best.X, best.Y, best);
    }

    /// <summary>Was an einem Kabelende angeschlossen ist (oder null).</summary>
    public static Anchor? EndAnchor(LayoutPlan plan, SceneryItem wire, bool end, double tolerance = 1)
    {
        if (wire.Points.Count == 0) return null;
        var (x, y) = end ? wire.Points[^1] : wire.Points[0];
        return Anchors(plan, wire.Id).Where(a => Math.Abs(a.X - x) <= tolerance && Math.Abs(a.Y - y) <= tolerance).FirstOrDefault();
    }

    /// <summary>
    /// Leitende Verbindungen aus Kabeln: beide Enden eines Kabels, Kabelenden am selben Punkt und alle Klemmen einer Verteilerleiste
    /// gehören zum selben Netz. Liefert für einen Punkt die Netznummer (oder null, wenn dort nichts angeschlossen ist).
    /// </summary>
    public static Func<(double X, double Y), int?> Nets(LayoutPlan plan, double tolerance = 1)
    {
        var points = new List<(double X, double Y)>();
        var parent = new List<int>();
        int Add((double X, double Y) p) { points.Add(p); parent.Add(parent.Count); return parent.Count - 1; }
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Join(int a, int b) => parent[Root(a)] = Root(b);
        foreach (var w in plan.Scenery.Where(s => s.IsWire && s.Points.Count >= 2)) Join(Add(w.Points[0]), Add(w.Points[^1]));
        foreach (var d in PowerSupply.Distributors(plan))
        {
            var first = -1;
            foreach (var c in PowerSupply.ClampPoints(d))
            {
                var i = Add(c);
                if (first < 0) first = i; else Join(i, first);
            }
        }
        bool Near((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (Near(points[i], points[j])) Join(i, j);
        return p =>
        {
            for (var i = 0; i < points.Count; i++)
                if (Near(points[i], p)) return Root(i);
            return null;
        };
    }

    /// <summary>Kabel, die mit einem Ende an diesem Punkt enden.</summary>
    public static IEnumerable<SceneryItem> WiresAt(LayoutPlan plan, (double X, double Y) p, double tolerance = 1)
        => plan.Scenery.Where(w => w.IsWire && w.Points.Count >= 2 &&
            new[] { w.Points[0], w.Points[^1] }.Any(e => Math.Abs(e.X - p.X) <= tolerance && Math.Abs(e.Y - p.Y) <= tolerance));

    /// <summary>Gesamtlänge je Farbe und Querschnitt in Metern (mit <see cref="Reserve"/>), für Kabelplan und Stückliste.</summary>
    public static IReadOnlyList<(string Color, double Section, double Meters)> Totals(LayoutPlan plan)
    {
        var current = Electrics.Current(plan);
        return plan.Scenery.Where(s => s.IsWire)
            .GroupBy(w => (ColorOf(w, current), Section(w, plan.Gauge)))
            .Select(g => (g.Key.Item1, g.Key.Item2, Math.Ceiling(g.Sum(Length) * (1 + Reserve) / 100) / 10))
            .OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();
    }

    public static string SectionText(double mm2) => Fmt.Num(mm2, 2) + " mm²";

    private static double SegmentDistance((double X, double Y) a, (double X, double Y) b, double x, double y)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        var t = len2 < 1e-12 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / len2, 0, 1);
        return Math.Sqrt(Sq(a.X + dx * t - x) + Sq(a.Y + dy * t - y));
    }

    private static double Sq(double v) => v * v;
}

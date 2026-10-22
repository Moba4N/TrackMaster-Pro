namespace TrackMaster.Core;

/// <summary>Ein verlegtes Gleisstück. (X, Y, Rotation) ist die Lage von Anschluss 0.</summary>
public sealed class PlacedPiece
{
    public int Id { get; set; }
    public required string Key { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public bool Flipped { get; set; }
    /// <summary>Höhe der Schienenoberkante über der Anlagenplatte in mm.</summary>
    public double Height { get; set; }

    public TrackPieceDef Def => Catalog.Get(Key);

    public IReadOnlyList<Leg> WorldLegs() => TrackGeometry.LocalLegs(Def, Flipped).Select(l => l.Transform(X, Y, Rotation)).ToList();

    public IReadOnlyList<Pose> WorldConnectors() => TrackGeometry.LocalConnectors(Def, Flipped).Select(ToWorld).ToList();

    public Pose ToWorld(Pose local)
    {
        var r = Rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        return new Pose(X + local.X * c - local.Y * s, Y + local.X * s + local.Y * c, local.Heading + Rotation);
    }

    /// <summary>Mittelpunkt des ersten Abschnitts, für Auswahlrahmen, Beschriftung und Drehen.</summary>
    public Pose Middle() => WorldLegs()[0].At(0.5);

    public PlacedPiece Clone() => new() { Id = Id, Key = Key, X = X, Y = Y, Rotation = Rotation, Flipped = Flipped, Height = Height };
}

public sealed class LayoutPlan
{
    public string Name { get; set; } = "Neuer Plan";
    public string GaugeId { get; set; } = "H0";
    public double BoardWidth { get; set; } = 2000;
    public double BoardHeight { get; set; } = 1250;
    public List<PlacedPiece> Pieces { get; } = [];
    public int NextId { get; set; } = 1;

    public Gauge Gauge => Catalog.GetGauge(GaugeId);

    public static LayoutPlan Empty(string gaugeId)
    {
        var g = Catalog.GetGauge(gaugeId);
        return new LayoutPlan { GaugeId = g.Id, BoardWidth = g.DefaultBoardWidth, BoardHeight = g.DefaultBoardHeight };
    }

    public PlacedPiece? Find(int id) => Pieces.FirstOrDefault(p => p.Id == id);

    public PlacedPiece Add(string key)
    {
        var piece = new PlacedPiece { Id = NextId++, Key = key };
        Pieces.Add(piece);
        return piece;
    }

    public PlanStatistics Statistics(Connectivity? connectivity = null)
    {
        var defs = Pieces.Select(p => p.Def).ToList();
        var radii = defs.Where(d => d.Radius > 0).Select(d => d.Radius).ToList();
        return new PlanStatistics(
            Pieces.Count,
            defs.Count(d => d.Kind is PieceKind.Switch or PieceKind.Crossing),
            defs.Sum(d => d.TrackLength),
            radii.Count > 0 ? radii.Min() : null,
            (connectivity ?? Connectivity.Analyze(this)).Open.Count);
    }

    /// <summary>Begrenzung von Anlagenplatte und allen Gleisen.</summary>
    public (double X0, double Y0, double X1, double Y1) Bounds()
    {
        double x0 = 0, y0 = 0, x1 = BoardWidth, y1 = BoardHeight;
        foreach (var p in Pieces)
            foreach (var l in p.WorldLegs())
                foreach (var t in new[] { 0, 0.5, 1 })
                {
                    var q = l.At(t);
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
        return (x0, y0, x1, y1);
    }
}

public sealed record PlanStatistics(int Pieces, int Switches, double TrackLengthMm, double? SmallestRadius, int OpenEnds);

namespace TrackMaster.Core;

/// <summary>Ein Anschluss eines verlegten Gleises. Link ist der passende Anschluss eines anderen Gleises, falls verbunden.</summary>
public sealed class ConnectorRef(int pieceId, int index, Pose pose, double z = 0)
{
    public int PieceId { get; } = pieceId;
    public int Index { get; } = index;
    public Pose Pose { get; } = pose;
    /// <summary>Höhe der Schienenoberkante an diesem Anschluss.</summary>
    public double Z { get; } = z;
    public ConnectorRef? Link { get; internal set; }
    public bool IsOpen => Link is null;
}

/// <summary>
/// Welche Gleisenden aufeinandertreffen. Zwei Anschlüsse gelten als verbunden bei unter 1 mm Abstand, entgegengesetzter Richtung (±2°)
/// und gleicher Höhe (±2 mm) – übereinanderliegende Gleise einer Gleiswendel sind also nicht verbunden.
/// </summary>
public sealed class Connectivity
{
    public const double PositionTolerance = 1.0;
    public const double AngleTolerance = 2.0;
    public const double HeightTolerance = 2.0;

    public IReadOnlyList<ConnectorRef> All { get; }
    public IReadOnlyList<ConnectorRef> Open { get; }

    /// <summary>Freie Anschlüsse von Drehscheiben und Schiebebühnen sind keine offenen Gleisenden.</summary>
    private Connectivity(List<ConnectorRef> all, HashSet<int> tables)
    {
        All = all;
        Open = all.Where(c => c.IsOpen && !tables.Contains(c.PieceId)).ToList();
    }

    public IEnumerable<ConnectorRef> Of(int pieceId) => All.Where(c => c.PieceId == pieceId);

    public ConnectorRef? Find(int pieceId, int index) => All.FirstOrDefault(c => c.PieceId == pieceId && c.Index == index);

    public static Connectivity Analyze(LayoutPlan plan)
    {
        var all = new List<ConnectorRef>();
        var cells = new Dictionary<(int, int), List<ConnectorRef>>();
        foreach (var p in plan.Pieces)
        {
            var conns = p.WorldConnectors();
            var heights = p.ConnectorHeights();
            for (var k = 0; k < conns.Count; k++)
            {
                var c = new ConnectorRef(p.Id, k, conns[k], heights[k]);
                all.Add(c);
                var key = Cell(c.Pose);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
                list.Add(c);
            }
        }
        foreach (var c in all)
        {
            if (c.Link is not null) continue;
            var (cx, cy) = Cell(c.Pose);
            for (var i = -1; i <= 1 && c.Link is null; i++)
                for (var j = -1; j <= 1 && c.Link is null; j++)
                {
                    if (!cells.TryGetValue((cx + i, cy + j), out var list)) continue;
                    foreach (var o in list)
                    {
                        if (o == c || o.PieceId == c.PieceId || o.Link is not null) continue;
                        if (Matches(c.Pose, o.Pose) && Math.Abs(c.Z - o.Z) < HeightTolerance)
                        {
                            c.Link = o;
                            o.Link = c;
                            break;
                        }
                    }
                }
        }
        return new Connectivity(all, plan.Pieces.Where(p => p.Def.IsTable).Select(p => p.Id).ToHashSet());
    }

    public static bool Matches(Pose a, Pose b)
        => Math.Abs(a.X - b.X) < PositionTolerance && Math.Abs(a.Y - b.Y) < PositionTolerance
           && Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) < PositionTolerance
           && Math.Abs(Angles.Normalize(a.Heading - b.Heading - 180)) < AngleTolerance;

    private static (int, int) Cell(Pose p) => ((int)Math.Floor(p.X / 4), (int)Math.Floor(p.Y / 4));
}

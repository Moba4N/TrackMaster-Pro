namespace TrackMaster.Core;

/// <summary>Bearbeitungsschritte am Gleisplan. Rückgängig-Schritte legt der Aufrufer an (EditHistory).</summary>
public static class PlanEditing
{
    /// <summary>Legt Anschluss <paramref name="index"/> des Gleises an einen Punkt, dessen Gegenstück nach <paramref name="targetHeading"/> zeigt.</summary>
    public static void PlaceAt(PlacedPiece piece, int index, double x, double y, double targetHeading)
    {
        var c = TrackGeometry.LocalConnectors(piece.Def, piece.Flipped)[index];
        piece.Rotation = Angles.Normalize(targetHeading + 180 - c.Heading);
        var r = piece.Rotation * Math.PI / 180;
        piece.X = x - (c.X * Math.Cos(r) - c.Y * Math.Sin(r));
        piece.Y = y - (c.X * Math.Sin(r) + c.Y * Math.Cos(r));
    }

    /// <summary>
    /// Setzt ein neues Gleis an ein offenes Gleisende an. Ohne Gleisende wird es waagerecht um <paramref name="fallbackCenter"/> gelegt.
    /// </summary>
    public static PlacedPiece Attach(LayoutPlan plan, string key, ConnectorRef? target, (double X, double Y) fallbackCenter)
    {
        var def = Catalog.Get(key);
        if (def.GaugeId != plan.GaugeId)
            throw new InvalidOperationException(Lang.F($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {plan.GaugeId}."));
        var piece = plan.Add(key);
        if (target is not null)
        {
            PlaceAt(piece, 0, target.Pose.X, target.Pose.Y, target.Pose.Heading);
            // Höhe und Steigung des Nachbargleises fortsetzen
            piece.Height = target.Z;
            if (plan.Find(target.PieceId) is { } neighbour)
                piece.Grade = target.Index == 0 ? -neighbour.EffectiveGrade : neighbour.EffectiveGrade;
            piece.Tunnel = plan.Find(target.PieceId)?.Tunnel ?? false;
        }
        else
            PlaceAt(piece, 0, fallbackCenter.X - def.TrackLength / 2, fallbackCenter.Y, 0);
        return piece;
    }

    /// <summary>Abstand entlang des Gleises von Anschluss 0 zu Anschluss <paramref name="index"/>.</summary>
    public static double DistanceFromOrigin(PlacedPiece piece, int index)
    {
        var paths = TrackGeometry.LocalPaths(piece.Def, piece.Flipped);
        return piece.Def.Kind switch
        {
            PieceKind.Straight or PieceKind.Curve when index == 1 => paths[0].Sum(l => l.TrackLength),
            PieceKind.Switch when index >= 1 && index <= paths.Count => paths[index - 1].Sum(l => l.TrackLength),
            _ => 0,
        };
    }

    public static void RotateAround(IEnumerable<PlacedPiece> pieces, double cx, double cy, double degrees)
    {
        var r = degrees * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        foreach (var p in pieces)
        {
            double dx = p.X - cx, dy = p.Y - cy;
            p.X = cx + dx * c - dy * s;
            p.Y = cy + dx * s + dy * c;
            p.Rotation = Angles.Normalize(p.Rotation + degrees);
        }
    }

    /// <summary>Dreht eine Auswahl um ihren Mittelpunkt.</summary>
    public static void Rotate(IReadOnlyCollection<PlacedPiece> pieces, double degrees)
    {
        if (pieces.Count == 0) return;
        var mids = pieces.Select(p => p.Middle()).ToList();
        RotateAround(pieces, mids.Average(m => m.X), mids.Average(m => m.Y), degrees);
    }

    /// <summary>Spiegelt Bögen, Weichen und Kreuzungen. Anschluss 0 bleibt, wo er ist. Gibt die Zahl der gespiegelten Gleise zurück.</summary>
    public static int Flip(IEnumerable<PlacedPiece> pieces)
    {
        var n = 0;
        foreach (var p in pieces.Where(p => p.Def.CanFlip))
        {
            p.Flipped = !p.Flipped;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Rastet eine Gruppe an das nächste freie Gleisende ein (innerhalb <paramref name="tolerance"/> mm, Richtung höchstens 50° daneben).
    /// Die ganze Gruppe wird dazu gedreht und verschoben. Ein einzelnes Gleis rastet in jeder Richtung ein: Es wird so gedreht,
    /// dass das Ende, das über dem freien Gleisende liegt, passend anschließt.
    /// </summary>
    public static bool Snap(LayoutPlan plan, IReadOnlySet<int> ids, double tolerance)
    {
        var conn = Connectivity.Analyze(plan);
        var mine = conn.All.Where(c => ids.Contains(c.PieceId) && !(c.Link is { } l && ids.Contains(l.PieceId))).ToList();
        var targets = conn.All.Where(c => !ids.Contains(c.PieceId) && c.IsOpen).ToList();
        var single = ids.Count == 1;
        (ConnectorRef M, ConnectorRef T)? best = null;
        var bestScore = double.MaxValue;
        foreach (var m in mine)
            foreach (var t in targets)
            {
                var d = Math.Sqrt(Math.Pow(m.Pose.X - t.Pose.X, 2) + Math.Pow(m.Pose.Y - t.Pose.Y, 2));
                var turn = Math.Abs(Angles.Normalize(m.Pose.Heading - t.Pose.Heading - 180));
                if (d >= tolerance || (!single && turn >= 50)) continue;
                // Bei gleichem Abstand gewinnt das Ende, das am wenigsten gedreht werden muss
                var score = d + turn * tolerance / 1800;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (m, t);
                }
            }
        if (best is not { } pair) return false;
        var group = plan.Pieces.Where(p => ids.Contains(p.Id)).ToList();
        RotateAround(group, pair.M.Pose.X, pair.M.Pose.Y, Angles.Normalize(pair.T.Pose.Heading + 180 - pair.M.Pose.Heading));
        foreach (var p in group)
        {
            p.X += pair.T.Pose.X - pair.M.Pose.X;
            p.Y += pair.T.Pose.Y - pair.M.Pose.Y;
            p.Height += pair.T.Z - pair.M.Z;
        }
        return true;
    }

    /// <summary>
    /// Legt ein an genau einer Stelle verbundenes Gleis mit dem nächsten Anschluss an dieselbe Stelle.
    /// Gibt den neuen freien Anschluss zurück oder eine Fehlermeldung.
    /// </summary>
    public static (int? NextOpen, string? Error) SwapAnchor(LayoutPlan plan, PlacedPiece piece)
    {
        var count = TrackGeometry.LocalConnectors(piece.Def, piece.Flipped).Count;
        if (count < 2) return (null, Lang.T("Ein Prellbock hat nur einen Anschluss."));
        var linked = Connectivity.Analyze(plan).Of(piece.Id).Where(c => c.Link is not null).ToList();
        if (linked.Count != 1) return (null, Lang.T("„Anschluss wechseln“ geht bei Gleisen, die an genau einer Stelle verbunden sind."));
        var target = linked[0].Link!;
        var k = (linked[0].Index + 1) % count;
        PlaceAt(piece, k, target.Pose.X, target.Pose.Y, target.Pose.Heading);
        piece.Height += target.Z - piece.ConnectorHeights()[k];
        return ((k + 1) % count, null);
    }

    public static List<PlacedPiece> Duplicate(LayoutPlan plan, IEnumerable<PlacedPiece> pieces, double offset = 60)
    {
        var copies = new List<PlacedPiece>();
        foreach (var p in pieces.ToList())
        {
            var q = plan.Add(p.Key);
            q.X = p.X + offset; q.Y = p.Y + offset; q.Rotation = p.Rotation; q.Flipped = p.Flipped; q.Height = p.Height; q.Grade = p.Grade; q.Tunnel = p.Tunnel;
            copies.Add(q);
        }
        return copies;
    }

    /// <summary>
    /// Legt eine gleichmäßige Steigung über die gewählten Gleisfolgen. Die erste Folge beginnt an einem Ende der Auswahl
    /// (bei <paramref name="reverse"/> am anderen) mit der Höhe <paramref name="startHeight"/> und bekommt die Steigung.
    /// Weitere gewählte Folgen (z. B. das zweite Gleis einer zweigleisigen Wendel oder Rampe) beginnen an ihrem Ende nächst dem
    /// Anfang der ersten; liegen sie parallel daneben, steigen sie so, dass sie auf derselben Höhe enden, sonst mit derselben Steigung.
    /// </summary>
    /// <param name="gradePercent">Steigung in Prozent; negativ = Gefälle.</param>
    public static GradientResult ApplyGradient(LayoutPlan plan, IReadOnlySet<int> ids, double startHeight, double gradePercent, bool reverse = false)
    {
        var conn = Connectivity.Analyze(plan);
        var g = gradePercent / 100;
        // Enden der Auswahl: Anschlüsse ohne Verbindung zu einem anderen gewählten Gleis
        var ends = conn.All.Where(c => ids.Contains(c.PieceId) && !(c.Link is { } l && ids.Contains(l.PieceId))).ToList();
        if (ends.Count == 0) return new GradientResult(0, startHeight, 0, Lang.T("Die Auswahl hat kein Ende – bitte eine offene Gleisfolge wählen, keinen geschlossenen Kreis."));
        var visited = new HashSet<int>();
        var first = reverse ? ends[^1] : ends[0];
        var gauge = plan.Gauge.TrackWidth;
        ConnectorRef? Partner(ConnectorRef from, HashSet<int> done)
            => ends.Where(e => !done.Contains(e.PieceId)).MinBy(e => Dist(e, from) + Math.Abs(e.Z - from.Z));
        {
            // Bei parallelen Gleisen bekommt das kürzere (innere, steilere) Gleis die eingegebene Steigung
            var probe = new HashSet<int>();
            var (_, firstLength) = Walk(plan, conn, ids, probe, first, startHeight, g, apply: false);
            if (Partner(first, probe) is { } other && Dist(other, first) < gauge * 10)
            {
                var (_, otherLength) = Walk(plan, conn, ids, [.. probe], other, startHeight, g, apply: false);
                if (otherLength < firstLength && otherLength > firstLength * 0.5) first = other;
            }
        }
        var (endZ, length) = Walk(plan, conn, ids, visited, first, startHeight, g, apply: true);
        var chains = 1;
        var parallel = 0;
        // Bei Wendeln liegen Anfang und Ende eines Gleises übereinander: die bisherige Höhe entscheidet, welches Ende passt
        while (Partner(first, visited) is { } next)
        {
            // Erst die Länge der Folge bestimmen, dann die Steigung setzen
            var (_, len) = Walk(plan, conn, ids, [.. visited], next, startHeight, g, apply: false);
            var isParallel = Dist(next, first) < gauge * 10 && len > 0 && Math.Abs(len - length) < length * 0.5;
            var grade = isParallel ? (endZ - startHeight) / len : g;
            Walk(plan, conn, ids, visited, next, startHeight, grade, apply: true);
            chains++;
            if (isParallel) parallel++;
        }
        var note = chains == 1 ? null
            : parallel > 0 ? Lang.F($"{chains} Gleise angepasst, alle enden auf {Fmt.Num(endZ, 0)} mm (das längere Außengleis steigt entsprechend flacher).")
            : Lang.F($"{chains} Gleisfolgen mit derselben Steigung angepasst.");
        return new GradientResult(visited.Count, endZ, length, note);
    }

    private static double Dist(ConnectorRef a, ConnectorRef b) => Math.Sqrt(Math.Pow(a.Pose.X - b.Pose.X, 2) + Math.Pow(a.Pose.Y - b.Pose.Y, 2));

    /// <summary>Folgt der Gleisfolge ab <paramref name="start"/> durch die gewählten Gleise; setzt Höhe und Steigung, wenn <paramref name="apply"/>.</summary>
    private static (double EndZ, double Length) Walk(LayoutPlan plan, Connectivity conn, IReadOnlySet<int> ids, HashSet<int> visited, ConnectorRef start, double z, double g, bool apply)
    {
        var piece = plan.Find(start.PieceId)!;
        var entry = start.Index;
        double length = 0;
        while (true)
        {
            visited.Add(piece.Id);
            var d = piece.Def;
            var count = TrackGeometry.LocalConnectors(d, piece.Flipped).Count;
            int? exit = null;
            ConnectorRef? Next(int k) => conn.Find(piece.Id, k)?.Link is { } l && ids.Contains(l.PieceId) && !visited.Contains(l.PieceId) ? l : null;
            if (d.Kind == PieceKind.Crossing)
                exit = entry switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
            else if (d.IsTable)
                exit = null; // Die Steigung endet an Drehscheibe und Schiebebühne
            else if (entry == 0)
                exit = Enumerable.Range(1, Math.Max(count - 1, 0)).Cast<int?>().FirstOrDefault(k => Next(k!.Value) is not null) ?? (count > 1 ? 1 : null);
            else
                exit = 0;

            if (d.Kind is PieceKind.Crossing or PieceKind.Buffer || d.IsTable)
            {
                if (apply) { piece.Height = z; piece.Grade = 0; }
                length += d.TrackLength;
            }
            else if (entry == 0)
            {
                if (apply) { piece.Height = z; piece.Grade = g; }
                var run = exit is { } x ? DistanceFromOrigin(piece, x) : d.TrackLength;
                z += g * run;
                length += run;
            }
            else
            {
                var run = DistanceFromOrigin(piece, entry);
                if (apply) { piece.Height = z + g * run; piece.Grade = -g; }
                z += g * run;
                length += run;
            }
            if (exit is not { } ex || Next(ex) is not { } next) break;
            piece = plan.Find(next.PieceId)!;
            entry = next.Index;
        }
        return (z, length);
    }

    /// <summary>
    /// Baut eine Gleiswendel aus Bögen <paramref name="curveKey"/>: <paramref name="turns"/> Windungen, je Windung <paramref name="risePerTurn"/> mm höher.
    /// Beginnt am Gleisende <paramref name="start"/> oder, ohne Gleisende, mit Mittelpunkt bei <paramref name="center"/>.
    /// </summary>
    public static List<PlacedPiece> BuildHelix(LayoutPlan plan, string curveKey, double turns, double risePerTurn, bool rightHand, ConnectorRef? start, (double X, double Y) center)
        => Helix.Build(plan, new HelixSpec(curveKey, turns, risePerTurn, rightHand), start, center).Inner;
}

public sealed record GradientResult(int Pieces, double EndHeight, double Length, string? Note);

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
            throw new InvalidOperationException($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {plan.GaugeId}.");
        var piece = plan.Add(key);
        if (target is not null)
            PlaceAt(piece, 0, target.Pose.X, target.Pose.Y, target.Pose.Heading);
        else
            PlaceAt(piece, 0, fallbackCenter.X - def.TrackLength / 2, fallbackCenter.Y, 0);
        return piece;
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
    /// Die ganze Gruppe wird dazu gedreht und verschoben.
    /// </summary>
    public static bool Snap(LayoutPlan plan, IReadOnlySet<int> ids, double tolerance)
    {
        var conn = Connectivity.Analyze(plan);
        var mine = conn.All.Where(c => ids.Contains(c.PieceId) && !(c.Link is { } l && ids.Contains(l.PieceId))).ToList();
        var targets = conn.All.Where(c => !ids.Contains(c.PieceId) && c.IsOpen).ToList();
        (ConnectorRef M, ConnectorRef T)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in mine)
            foreach (var t in targets)
            {
                var d = Math.Sqrt(Math.Pow(m.Pose.X - t.Pose.X, 2) + Math.Pow(m.Pose.Y - t.Pose.Y, 2));
                if (d < tolerance && d < bestDistance && Math.Abs(Angles.Normalize(m.Pose.Heading - t.Pose.Heading - 180)) < 50)
                {
                    bestDistance = d;
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
        if (count < 2) return (null, "Ein Prellbock hat nur einen Anschluss.");
        var linked = Connectivity.Analyze(plan).Of(piece.Id).Where(c => c.Link is not null).ToList();
        if (linked.Count != 1) return (null, "„Anschluss wechseln“ geht bei Gleisen, die an genau einer Stelle verbunden sind.");
        var target = linked[0].Link!;
        var k = (linked[0].Index + 1) % count;
        PlaceAt(piece, k, target.Pose.X, target.Pose.Y, target.Pose.Heading);
        return ((k + 1) % count, null);
    }

    public static List<PlacedPiece> Duplicate(LayoutPlan plan, IEnumerable<PlacedPiece> pieces, double offset = 60)
    {
        var copies = new List<PlacedPiece>();
        foreach (var p in pieces.ToList())
        {
            var q = plan.Add(p.Key);
            q.X = p.X + offset; q.Y = p.Y + offset; q.Rotation = p.Rotation; q.Flipped = p.Flipped; q.Height = p.Height;
            copies.Add(q);
        }
        return copies;
    }
}

namespace TrackMaster.Core;

/// <summary>
/// Umriss für automatisch aufgelegte Deckplatten: Grundriss der Anlage (Platte, Schenkel, Module) zusammen mit allen gezogenen
/// Leisten. Was Plattenkanten und Leisten ringsum einschließen – etwa das Dreieck zwischen versetzten Modulen und der Diagonale –,
/// gehört dazu (bis zur Mitte der Leiste). Jede zusammenhängende Gruppe ergibt eine Platte; frei abstehende Leisten zählen nicht.
/// </summary>
public static class FrameOutline
{
    private const double Q = 10;   // Ecken auf 0,1 mm zusammenfassen

    /// <summary>Außenumrisse (je zusammenhängendem Rahmen einer) als Vielecke, größter zuerst. Ohne <paramref name="board"/> nur die Leisten.</summary>
    public static List<List<(double X, double Y)>> Plates(LayoutPlan plan, bool board = true)
    {
        var segs = new List<((double X, double Y) A, (double X, double Y) B)>();
        void Ring(IReadOnlyList<(double X, double Y)> o)
        {
            for (int i = 0, j = o.Count - 1; i < o.Count; j = i++) segs.Add((o[j], o[i]));
        }
        if (board) foreach (var o in BoardGeometry.Outlines(plan)) Ring(o);
        // Leisten mit ihrer Mittellinie: die Platte liegt bis zur Mitte der Leiste auf; frei abstehende Enden fallen weg
        foreach (var b in plan.Scenery.Where(x => x.IsBeam && x.Points.Count == 2 && BeamLayout.Length(x) > 1e-6))
            segs.Add((b.Points[0], b.Points[1]));
        return Outer(segs);
    }

    /// <summary>Außenränder der Flächen, die die Strecken einschließen (je zusammenhängender Gruppe einer).</summary>
    public static List<List<(double X, double Y)>> Outer(IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> segments)
    {
        // 1. Strecken an allen Schnitt- und Berührpunkten teilen
        var cuts = segments.Select(s => new List<double> { 0, 1 }).ToList();
        for (var i = 0; i < segments.Count; i++)
            for (var j = i + 1; j < segments.Count; j++)
                Split(segments[i], segments[j], cuts[i], cuts[j]);
        var edges = new HashSet<(long, long, long, long)>();
        var pos = new Dictionary<(long, long), (double X, double Y)>();
        (long, long) Key((double X, double Y) p)
        {
            var k = ((long)Math.Round(p.X * Q), (long)Math.Round(p.Y * Q));
            pos.TryAdd(k, (k.Item1 / Q, k.Item2 / Q));
            return k;
        }
        for (var i = 0; i < segments.Count; i++)
        {
            var (a, b) = segments[i];
            var ts = cuts[i].Distinct().Order().ToList();
            for (var k = 1; k < ts.Count; k++)
            {
                var p = Key((a.X + (b.X - a.X) * ts[k - 1], a.Y + (b.Y - a.Y) * ts[k - 1]));
                var q = Key((a.X + (b.X - a.X) * ts[k], a.Y + (b.Y - a.Y) * ts[k]));
                if (p == q) continue;
                edges.Add(p.CompareTo(q) < 0 ? (p.Item1, p.Item2, q.Item1, q.Item2) : (q.Item1, q.Item2, p.Item1, p.Item2));
            }
        }
        var adj = new Dictionary<(long, long), HashSet<(long, long)>>();
        foreach (var (x0, y0, x1, y1) in edges)
        {
            (long, long) p = (x0, y0), q = (x1, y1);
            (adj.TryGetValue(p, out var lp) ? lp : adj[p] = []).Add(q);
            (adj.TryGetValue(q, out var lq) ? lq : adj[q] = []).Add(p);
        }
        // 2. lose Enden (frei abstehende Kanten) entfernen, bis nur geschlossene Ränder bleiben
        var loose = new Queue<(long, long)>(adj.Where(kv => kv.Value.Count <= 1).Select(kv => kv.Key));
        while (loose.Count > 0)
        {
            var v = loose.Dequeue();
            if (!adj.TryGetValue(v, out var nb) || nb.Count > 1) continue;
            foreach (var w in nb)
            {
                adj[w].Remove(v);
                if (adj[w].Count <= 1) loose.Enqueue(w);
            }
            adj.Remove(v);
        }
        // 3. Nachbarn je Ecke nach Winkel ordnen, alle Flächen umlaufen
        var order = adj.ToDictionary(kv => kv.Key, kv => kv.Value
            .OrderBy(w => Math.Atan2(pos[w].Y - pos[kv.Key].Y, pos[w].X - pos[kv.Key].X)).ToList());
        var seen = new HashSet<((long, long), (long, long))>();
        var faces = new List<List<(long, long)>>();
        foreach (var (u, nbs) in order)
            foreach (var v0 in nbs)
            {
                if (seen.Contains((u, v0))) continue;
                var face = new List<(long, long)>();
                var (a, b) = (u, v0);
                for (var guard = 0; guard < 100000 && seen.Add((a, b)); guard++)
                {
                    face.Add(a);
                    var list = order[b];
                    var i = list.IndexOf(a);
                    var c = list[(i - 1 + list.Count) % list.Count];
                    (a, b) = (b, c);
                }
                if (face.Count >= 3) faces.Add(face);
            }
        // 4. je zusammenhängender Gruppe der Umlauf mit der größten Fläche = Außenrand
        var group = new Dictionary<(long, long), int>();
        var n = 0;
        foreach (var start in order.Keys)
        {
            if (group.ContainsKey(start)) continue;
            var stack = new Stack<(long, long)>([start]);
            group[start] = n;
            while (stack.Count > 0)
                foreach (var w in order[stack.Pop()])
                    if (group.TryAdd(w, n)) stack.Push(w);
            n++;
        }
        var result = new List<List<(double X, double Y)>>();
        foreach (var g in faces.GroupBy(f => group[f[0]]))
        {
            var outer = g.Select(f => f.Select(k => pos[k]).ToList()).MaxBy(f => Math.Abs(Geometry2D.SignedArea(f)))!;
            var clean = PlateLayout.Clean(outer);
            var area = Math.Abs(Geometry2D.SignedArea(clean));
            var perimeter = clean.Select((p, i) => Math.Sqrt(Math.Pow(clean[(i + 1) % clean.Count].X - p.X, 2) + Math.Pow(clean[(i + 1) % clean.Count].Y - p.Y, 2))).Sum();
            // einzelne Leisten (schmale Streifen) sind keine Platte
            if (clean.Count < 3 || area < PlateLayout.MinSize * PlateLayout.MinSize || area / perimeter < 15) continue;
            result.Add(clean);
        }
        return result.OrderByDescending(r => Math.Abs(Geometry2D.SignedArea(r))).ToList();
    }

    /// <summary>Teilpunkte (0…1) zweier Strecken an ihrem Schnittpunkt bzw. – wenn sie aufeinander liegen – an den Enden der jeweils anderen.</summary>
    private static void Split(((double X, double Y) A, (double X, double Y) B) s, ((double X, double Y) A, (double X, double Y) B) t, List<double> cs, List<double> ct)
    {
        double rx = s.B.X - s.A.X, ry = s.B.Y - s.A.Y, sx = t.B.X - t.A.X, sy = t.B.Y - t.A.Y;
        double ls = Math.Sqrt(rx * rx + ry * ry), lt = Math.Sqrt(sx * sx + sy * sy);
        if (ls < 1e-9 || lt < 1e-9) return;
        var den = rx * sy - ry * sx;
        const double eps = 0.05;   // mm
        if (Math.Abs(den) > 1e-9 * ls * lt)
        {
            var u = ((t.A.X - s.A.X) * sy - (t.A.Y - s.A.Y) * sx) / den;
            var v = ((t.A.X - s.A.X) * ry - (t.A.Y - s.A.Y) * rx) / den;
            if (u < -eps / ls || u > 1 + eps / ls || v < -eps / lt || v > 1 + eps / lt) return;
            cs.Add(Math.Clamp(u, 0, 1));
            ct.Add(Math.Clamp(v, 0, 1));
            return;
        }
        // parallel: nur wenn auf einer Linie
        var dist = Math.Abs((t.A.X - s.A.X) * ry - (t.A.Y - s.A.Y) * rx) / ls;
        if (dist > eps) return;
        void On(List<double> list, (double X, double Y) a, double dx, double dy, double l2, (double X, double Y) p)
        {
            var k = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2;
            if (k > 0 && k < 1) list.Add(k);
        }
        On(cs, s.A, rx, ry, ls * ls, t.A);
        On(cs, s.A, rx, ry, ls * ls, t.B);
        On(ct, t.A, sx, sy, lt * lt, s.A);
        On(ct, t.A, sx, sy, lt * lt, s.B);
    }
}

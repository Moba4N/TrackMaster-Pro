namespace TrackMaster.Core;

/// <summary>Ein Modul der Anlagenplatte: achsparalleles Rechteck in Plankoordinaten (mm).</summary>
public sealed class BoardModule
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1000;
    public double Depth { get; set; } = 500;

    public BoardRect Rect => new(X, Y, X + Width, Y + Depth);

    public BoardModule Clone() => new() { Name = Name, X = X, Y = Y, Width = Width, Depth = Depth };
}

/// <summary>Griff an einer Ecke: <see cref="Module"/> = −1 für die Außenecke der ganzen Platte, <see cref="Corner"/> 0…3 im Uhrzeigersinn ab links oben (−1 = Modul verschieben).</summary>
public sealed record BoardHandle(int Module, int Corner, double X, double Y);

/// <summary>
/// Neue Lage und Größe eines Moduls aus dem Moduldialog; <see cref="Index"/> = −1 für ein neues Modul,
/// <see cref="ModuleLayout.Merged"/> für ein aus mehreren zusammengeführtes Modul (rastet nicht ein, bewegt keine Inhalte).
/// </summary>
public sealed record ModuleEdit(int Index, string Name, double X, double Y, double Width, double Depth)
{
    public BoardRect Rect => new(X, Y, X + Width, Y + Depth);
}

/// <summary>
/// Modulanlage: Die Platte besteht aus einzelnen Modulen, die sich getrennt bauen und dann zusammensetzen lassen.
/// Module lassen sich samt Gleisen und Gelände verschieben; ein anderer Plan kann als weiteres Modul angefügt werden.
/// </summary>
public static class ModuleLayout
{
    public const double MinSize = 50, MaxSize = 20000;

    /// <summary>Kennung für ein zusammengeführtes Modul in <see cref="ModuleEdit.Index"/>.</summary>
    public const int Merged = -2;

    /// <summary>Kleinster Abstand, ab dem ein Modul an die Kante eines anderen einrastet (mm).</summary>
    public const double SnapDistance = 30;

    /// <summary>Macht aus der bisherigen Platte Module (ein Rechteck der Form = ein Modul).</summary>
    public static void ConvertToModules(LayoutPlan plan)
    {
        if (plan.BoardShape == BoardShape.Module && plan.Modules.Count > 0) return;
        var rects = BoardGeometry.Rects(plan);
        plan.Modules.Clear();
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            plan.Modules.Add(new BoardModule { Name = Lang.F($"Modul {i + 1}"), X = r.X0, Y = r.Y0, Width = r.X1 - r.X0, Depth = r.Y1 - r.Y0 });
        }
        plan.BoardShape = BoardShape.Module;
        Fit(plan);
    }

    /// <summary>Module zum Bearbeiten: die vorhandenen oder, bei anderen Formen, die Rechtecke der Platte.</summary>
    public static IReadOnlyList<ModuleEdit> Current(LayoutPlan plan)
    {
        if (plan.BoardShape == BoardShape.Module && plan.Modules.Count > 0)
            return plan.Modules.Select((m, i) => new ModuleEdit(i, m.Name, m.X, m.Y, m.Width, m.Depth)).ToList();
        return BoardGeometry.Rects(plan).Select((r, i) => new ModuleEdit(i, Lang.F($"Modul {i + 1}"), r.X0, r.Y0, r.X1 - r.X0, r.Y1 - r.Y0)).ToList();
    }

    /// <summary>Ein einzelnes Modul als neue Platte.</summary>
    public static void Single(LayoutPlan plan, double width, double depth)
    {
        plan.Modules.Clear();
        plan.Modules.Add(new BoardModule { Name = Lang.T("Modul 1"), Width = Math.Clamp(width, MinSize, MaxSize), Depth = Math.Clamp(depth, MinSize, MaxSize) });
        plan.BoardShape = BoardShape.Module;
        Fit(plan);
    }

    /// <summary>
    /// Plattenmaße an die Module anpassen. Liegt ein Modul links oder oberhalb von 0, wird die ganze Anlage
    /// (Module, Gleise, Gelände) so verschoben, dass sie wieder bei 0, 0 beginnt.
    /// </summary>
    public static void Fit(LayoutPlan plan)
    {
        if (plan.Modules.Count == 0) return;
        double minX = plan.Modules.Min(m => m.X), minY = plan.Modules.Min(m => m.Y);
        if (Math.Abs(minX) > 0.001 || Math.Abs(minY) > 0.001)
        {
            foreach (var m in plan.Modules) { m.X -= minX; m.Y -= minY; }
            foreach (var p in plan.Pieces) { p.X -= minX; p.Y -= minY; }
            foreach (var s in plan.Scenery) s.MoveBy(-minX, -minY);
        }
        plan.BoardWidth = Math.Max(MinSize, plan.Modules.Max(m => m.X + m.Width));
        plan.BoardHeight = Math.Max(MinSize, plan.Modules.Max(m => m.Y + m.Depth));
    }

    /// <summary>Platz für ein neues Modul: rechts neben dem Modul ganz rechts, gleich groß wie das letzte.</summary>
    public static BoardModule NextModule(LayoutPlan plan)
    {
        var last = plan.Modules.LastOrDefault();
        var right = plan.Modules.Count > 0 ? plan.Modules.MaxBy(m => m.X + m.Width)! : null;
        return new BoardModule
        {
            Name = Lang.F($"Modul {plan.Modules.Count + 1}"),
            X = right is null ? 0 : right.X + right.Width,
            Y = right?.Y ?? 0,
            Width = last?.Width ?? 1000,
            Depth = last?.Depth ?? 500,
        };
    }

    /// <summary>
    /// Übernimmt die Änderungen aus dem Moduldialog. Ein verschobenes Modul nimmt Gleise und Gelände mit, die darauf liegen.
    /// Nicht mehr aufgeführte Module werden entfernt (ihre Gleise bleiben liegen). Neue Module rasten an Nachbarkanten ein.
    /// </summary>
    public static void Apply(LayoutPlan plan, IReadOnlyList<ModuleEdit> edits)
    {
        // Andere Formen erst in Module umwandeln: die Reihenfolge entspricht BoardGeometry.Rects
        ConvertToModules(plan);
        var old = plan.Modules.Select(m => m.Clone()).ToList();
        // Inhalte den alten Modulen zuordnen, bevor sich etwas bewegt
        var pieceOwner = plan.Pieces.ToDictionary(p => p, p => Owner(old, p.Middle().X, p.Middle().Y));
        var sceneryOwner = plan.Scenery.ToDictionary(s => s, s => Owner(old, s.Center().X, s.Center().Y));
        var result = new List<BoardModule>();
        var moves = new Dictionary<int, (double Dx, double Dy)>();
        foreach (var e in edits)
        {
            var m = new BoardModule
            {
                Name = string.IsNullOrWhiteSpace(e.Name) ? Lang.F($"Modul {result.Count + 1}") : e.Name.Trim(),
                X = e.X, Y = e.Y,
                Width = Math.Clamp(e.Width, MinSize, MaxSize), Depth = Math.Clamp(e.Depth, MinSize, MaxSize),
            };
            // Neue und verschobene Module rasten an den Kanten der übrigen ein
            if (e.Index == Merged) { result.Add(m); continue; }
            if (e.Index < 0 || e.Index >= old.Count || Math.Abs(old[e.Index].X - m.X) > 0.001 || Math.Abs(old[e.Index].Y - m.Y) > 0.001)
                SnapEdges(m, edits.Where(o => o != e).Select(o => new BoardRect(o.X, o.Y, o.X + o.Width, o.Y + o.Depth)));
            if (e.Index >= 0 && e.Index < old.Count && (Math.Abs(old[e.Index].X - m.X) > 0.001 || Math.Abs(old[e.Index].Y - m.Y) > 0.001))
                moves[e.Index] = (m.X - old[e.Index].X, m.Y - old[e.Index].Y);
            result.Add(m);
        }
        if (result.Count == 0) return;
        foreach (var (p, owner) in pieceOwner)
            if (owner is { } o && moves.TryGetValue(o, out var d)) { p.X += d.Dx; p.Y += d.Dy; }
        foreach (var (s, owner) in sceneryOwner)
            if (owner is { } o && moves.TryGetValue(o, out var d)) s.MoveBy(d.Dx, d.Dy);
        plan.Modules.Clear();
        plan.Modules.AddRange(result);
        plan.BoardShape = BoardShape.Module;
        Fit(plan);
    }

    /// <summary>
    /// Führt die Module <paramref name="pick"/> (Positionen in <paramref name="rows"/>) zu einem zusammen. Das geht nur,
    /// wenn sie lückenlos ein Rechteck ergeben. Liefert die neue Liste oder eine Fehlermeldung.
    /// </summary>
    public static (List<ModuleEdit>? Rows, string? Error) Merge(IReadOnlyList<ModuleEdit> rows, IReadOnlyCollection<int> pick)
    {
        if (pick.Count < 2) return (null, Lang.T("Bitte mindestens zwei Module zum Zusammenführen wählen."));
        var chosen = pick.Order().Select(i => rows[i]).ToList();
        double x0 = chosen.Min(r => r.X), y0 = chosen.Min(r => r.Y), x1 = chosen.Max(r => r.X + r.Width), y1 = chosen.Max(r => r.Y + r.Depth);
        var area = chosen.Sum(r => r.Width * r.Depth);
        var overlap = false;
        for (var i = 0; i < chosen.Count; i++)
            for (var j = i + 1; j < chosen.Count; j++)
            {
                var (a, b) = (chosen[i].Rect, chosen[j].Rect);
                if (Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) > 0.5 && Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0) > 0.5) overlap = true;
            }
        if (overlap || Math.Abs(area - (x1 - x0) * (y1 - y0)) > 1)
            return (null, Lang.T("Zusammenführen geht nur bei Modulen, die lückenlos aneinander liegen und zusammen ein Rechteck ergeben."));
        var merged = new ModuleEdit(Merged, chosen[0].Name, x0, y0, x1 - x0, y1 - y0);
        var result = new List<ModuleEdit>();
        for (var i = 0; i < rows.Count; i++)
            if (i == pick.Min()) result.Add(merged);
            else if (!pick.Contains(i)) result.Add(rows[i]);
        return (result, null);
    }

    /// <summary>Griffe zum Ändern der Platte mit der Maus: die Ecken jedes Moduls bzw. die Außenecken der Platte.</summary>
    public static List<BoardHandle> Handles(LayoutPlan plan)
    {
        var result = new List<BoardHandle>();
        if (plan.BoardShape == BoardShape.Module && plan.Modules.Count > 0)
            for (var i = 0; i < plan.Modules.Count; i++)
                result.AddRange(Corners(plan.Modules[i].Rect).Select((c, k) => new BoardHandle(i, k, c.X, c.Y)));
        else
            result.AddRange(Corners(new BoardRect(0, 0, plan.BoardWidth, plan.BoardHeight)).Select((c, k) => new BoardHandle(-1, k, c.X, c.Y)));
        return result;
    }

    /// <summary>Ecken eines Rechtecks: 0 links oben, 1 rechts oben, 2 rechts unten, 3 links unten.</summary>
    public static (double X, double Y)[] Corners(BoardRect r) => [(r.X0, r.Y0), (r.X1, r.Y0), (r.X1, r.Y1), (r.X0, r.Y1)];

    /// <summary>Modul unter dem Punkt (das zuletzt angelegte zuerst) oder null.</summary>
    public static int? ModuleAt(LayoutPlan plan, double x, double y)
    {
        if (plan.BoardShape != BoardShape.Module) return null;
        for (var i = plan.Modules.Count - 1; i >= 0; i--)
            if (plan.Modules[i].Rect.Contains(x, y)) return i;
        return null;
    }

    /// <summary>Verschiebt ein Modul samt Gleisen und Gelände um dx, dy; es rastet an Nachbarkanten ein.</summary>
    public static void MoveModule(LayoutPlan plan, int index, double dx, double dy)
    {
        var edits = Current(plan).Select(e => e.Index == index ? e with { X = e.X + dx, Y = e.Y + dy } : e).ToList();
        Apply(plan, edits);
    }

    /// <summary>
    /// Zieht Ecke <paramref name="corner"/> eines Moduls (oder der ganzen Platte bei <paramref name="index"/> = −1) auf x, y;
    /// die gegenüberliegende Ecke bleibt liegen. Gleise und Gelände bleiben an ihrem Platz.
    /// </summary>
    public static void DragCorner(LayoutPlan plan, int index, int corner, double x, double y)
    {
        var rect = index >= 0 && index < plan.Modules.Count ? plan.Modules[index].Rect : new BoardRect(0, 0, plan.BoardWidth, plan.BoardHeight);
        var fixedCorner = Corners(rect)[(corner + 2) % 4];
        var left = corner is 0 or 3;
        var top = corner is 0 or 1;
        if (index >= 0)
        {
            // an Kanten der anderen Module einrasten
            var others = plan.Modules.Where((_, i) => i != index).Select(m => m.Rect).ToList();
            x = SnapValue(x, others.SelectMany(o => new[] { o.X0, o.X1 }));
            y = SnapValue(y, others.SelectMany(o => new[] { o.Y0, o.Y1 }));
        }
        double x0 = left ? Math.Min(x, fixedCorner.X - MinSize) : fixedCorner.X, x1 = left ? fixedCorner.X : Math.Max(x, fixedCorner.X + MinSize);
        double y0 = top ? Math.Min(y, fixedCorner.Y - MinSize) : fixedCorner.Y, y1 = top ? fixedCorner.Y : Math.Max(y, fixedCorner.Y + MinSize);
        x0 = Math.Max(x0, x1 - MaxSize * (index >= 0 ? 1 : 2.5));
        y0 = Math.Max(y0, y1 - MaxSize * (index >= 0 ? 1 : 2.5));
        x1 = Math.Min(x1, x0 + MaxSize * (index >= 0 ? 1 : 2.5));
        y1 = Math.Min(y1, y0 + MaxSize * (index >= 0 ? 1 : 2.5));
        x0 = Math.Round(x0); y0 = Math.Round(y0); x1 = Math.Round(x1); y1 = Math.Round(y1);
        if (index >= 0)
        {
            var m = plan.Modules[index];
            (m.X, m.Y, m.Width, m.Depth) = (x0, y0, x1 - x0, y1 - y0);
            Fit(plan);
            return;
        }
        // ganze Platte: sie beginnt immer bei 0, 0 – Inhalte so verschieben, dass sie an ihrem Platz bleiben
        if (Math.Abs(x0) > 0.001 || Math.Abs(y0) > 0.001)
        {
            foreach (var p in plan.Pieces) { p.X -= x0; p.Y -= y0; }
            foreach (var s in plan.Scenery) s.MoveBy(-x0, -y0);
        }
        plan.BoardWidth = x1 - x0;
        plan.BoardHeight = y1 - y0;
    }

    private static double SnapValue(double v, IEnumerable<double> targets)
    {
        var best = SnapDistance;
        var result = v;
        foreach (var t in targets)
            if (Math.Abs(t - v) < best) { best = Math.Abs(t - v); result = t; }
        return result;
    }

    private static void SnapEdges(BoardModule m, IEnumerable<BoardRect> others)
    {
        double bestX = SnapDistance, bestY = SnapDistance, dx = 0, dy = 0;
        foreach (var o in others)
        {
            foreach (var (from, to) in new[] { (m.X, o.X1), (m.X + m.Width, o.X0), (m.X, o.X0), (m.X + m.Width, o.X1) })
                if (Math.Abs(to - from) < bestX) { bestX = Math.Abs(to - from); dx = to - from; }
            foreach (var (from, to) in new[] { (m.Y, o.Y1), (m.Y + m.Depth, o.Y0), (m.Y, o.Y0), (m.Y + m.Depth, o.Y1) })
                if (Math.Abs(to - from) < bestY) { bestY = Math.Abs(to - from); dy = to - from; }
        }
        m.X += dx;
        m.Y += dy;
    }

    private static int? Owner(List<BoardModule> modules, double x, double y)
    {
        for (var i = 0; i < modules.Count; i++)
            if (modules[i].Rect.Contains(x, y)) return i;
        return null;
    }

    /// <summary>
    /// Setzt einen anderen Plan als Modul(e) rechts an die Anlage an: seine Platte wird zu Modulen, Gleise, Gelände,
    /// Einspeisungen, Trennstellen und Signale kommen mit. Gibt eine Fehlermeldung zurück oder null.
    /// </summary>
    public static string? Append(LayoutPlan plan, LayoutPlan other, string name)
    {
        if (other.GaugeId != plan.GaugeId) return Lang.F($"Der Plan ist für Spur {other.GaugeId}, diese Anlage für Spur {plan.GaugeId}.");
        ConvertToModules(plan);
        var dx = plan.Modules.Max(m => m.X + m.Width);
        var rects = BoardGeometry.Rects(other);
        var names = other.BoardShape == BoardShape.Module && other.Modules.Count == rects.Count ? other.Modules.Select(m => m.Name).ToList() : null;
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            plan.Modules.Add(new BoardModule
            {
                Name = names?[i] ?? (rects.Count == 1 ? name : $"{name} {i + 1}"),
                X = r.X0 + dx, Y = r.Y0, Width = r.X1 - r.X0, Depth = r.Y1 - r.Y0,
            });
        }
        var ids = new Dictionary<int, int>();
        foreach (var p in other.Pieces)
        {
            var q = p.Clone();
            q.Id = plan.NextId++;
            q.X += dx;
            ids[p.Id] = q.Id;
            plan.Pieces.Add(q);
        }
        foreach (var s in other.Scenery)
        {
            var q = s.Clone();
            q.Id = plan.NextId++;
            q.MoveBy(dx, 0);
            plan.Scenery.Add(q);
        }
        foreach (var f in other.Feeders.Where(f => ids.ContainsKey(f.PieceId)))
        {
            var q = f.Clone();
            q.Id = plan.NextId++;
            q.PieceId = ids[f.PieceId];
            plan.Feeders.Add(q);
        }
        foreach (var iso in other.Isolations.Where(i => ids.ContainsKey(i.PieceId)))
        {
            var q = iso.Clone();
            q.Id = plan.NextId++;
            q.PieceId = ids[iso.PieceId];
            plan.Isolations.Add(q);
        }
        foreach (var sig in other.Signals.Where(s => ids.ContainsKey(s.PieceId)))
        {
            var q = sig.Clone();
            q.Id = plan.NextId++;
            q.PieceId = ids[sig.PieceId];
            plan.Signals.Add(q);
        }
        Fit(plan);
        return null;
    }

    /// <summary>Gleise, die über eine Modulgrenze hinweg liegen (dort müssen die Schienen getrennt werden).</summary>
    public static List<PlacedPiece> PiecesAcrossJoints(LayoutPlan plan)
    {
        if (plan.BoardShape != BoardShape.Module || plan.Modules.Count < 2) return [];
        var result = new List<PlacedPiece>();
        foreach (var p in plan.Pieces)
        {
            var owners = new HashSet<int>();
            foreach (var l in p.WorldLegs())
                for (var i = 0; i <= 8; i++)
                {
                    var q = l.At(i / 8.0);
                    // Punkte genau auf der Kante zählen nicht (Gleisende an der Modulkante ist richtig)
                    var inside = plan.Modules.Select((m, k) => (m, k)).Where(t => t.m.Rect.Contains(q.X, q.Y, -1)).Select(t => t.k).ToList();
                    if (inside.Count == 1) owners.Add(inside[0]);
                }
            if (owners.Count > 1) result.Add(p);
        }
        return result;
    }
}

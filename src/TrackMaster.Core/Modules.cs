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

/// <summary>Neue Lage und Größe eines Moduls aus dem Moduldialog; <see cref="Index"/> = −1 für ein neues Modul.</summary>
public sealed record ModuleEdit(int Index, string Name, double X, double Y, double Width, double Depth);

/// <summary>
/// Modulanlage: Die Platte besteht aus einzelnen Modulen, die sich getrennt bauen und dann zusammensetzen lassen.
/// Module lassen sich samt Gleisen und Gelände verschieben; ein anderer Plan kann als weiteres Modul angefügt werden.
/// </summary>
public static class ModuleLayout
{
    public const double MinSize = 100, MaxSize = 20000;

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

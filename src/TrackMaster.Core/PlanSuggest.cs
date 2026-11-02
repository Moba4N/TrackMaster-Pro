namespace TrackMaster.Core;

/// <summary>Ein berechneter Gleisplan-Vorschlag: Gleise in Weltkoordinaten und wie viele Stücke des Bestands er braucht.</summary>
public sealed record PlanSuggestion(string Name, string Description, IReadOnlyList<PlacedPiece> Pieces, int Used, double Width, double Height)
{
    /// <summary>Verbrauchte Stückzahl je Artikel.</summary>
    public IReadOnlyDictionary<string, int> Counts => Pieces.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Planvorschlag aus dem eigenen Gleisbestand: baut aus den angegebenen Stückzahlen kleine Anlagen
/// (Oval, Oval mit Abstellgleis, Oval mit Ausweiche, Doppeloval, Acht mit Kreuzung), die auf die Anlagenplatte passen,
/// geschlossen sind und die Planprüfung ohne Fehler bestehen.
/// </summary>
public static class PlanSuggest
{
    public const int MaxResults = 8;

    private sealed record Straight(string Key, double Length);
    private sealed record Curve(string Key, double Radius, double Angle);
    private sealed record Turnout(string Key, double Length, double Angle, double Radius);

    /// <summary>Teile, die der Vorschlag verwenden kann (Geraden, Bögen, einfache Weichen, Prellböcke, Kreuzungen).</summary>
    public static bool IsUsable(TrackPieceDef d) => !d.IsFlex && d.Kind switch
    {
        PieceKind.Straight => d.Length > 0,
        PieceKind.Curve => d.Radius > 0 && d.Angle > 0 && d.Routes is null && d.Paths is null,
        PieceKind.Switch => d.IsSimpleSwitch && d.Drives == 1,
        PieceKind.Buffer => true,
        PieceKind.Crossing => d.RouteCount == 2 && !d.IsDoubleSlip && d.Angle > 0,
        _ => false,
    };

    /// <summary>
    /// Berechnet Vorschläge für die Platte von <paramref name="board"/> aus dem Bestand <paramref name="inventory"/> (Schlüssel → Stückzahl).
    /// Sortiert nach verbauten Gleisen, die größten zuerst.
    /// </summary>
    public static IReadOnlyList<PlanSuggestion> Build(LayoutPlan board, IReadOnlyDictionary<string, int> inventory)
    {
        var stock = new Dictionary<string, int>();
        foreach (var (key, n) in inventory)
            if (n > 0 && Catalog.Find(key) is { } d && d.GaugeId == board.GaugeId && IsUsable(d))
                stock[d.Key] = n;
        if (stock.Count == 0) return [];
        var b = new Builder(board, stock);
        var list = new List<PlanSuggestion>();
        void Add(PlanSuggestion? s)
        {
            if (s is null) return;
            // Gleiche Gleisfolge nur einmal
            if (list.Any(o => o.Used == s.Used && o.Counts.Count == s.Counts.Count && o.Counts.All(kv => s.Counts.TryGetValue(kv.Key, out var v) && v == kv.Value))) return;
            list.Add(s);
        }
        foreach (var r in b.RadiusGroups())
        {
            Add(b.Oval(r));
            Add(b.OvalWithSidings(r, 1));
            Add(b.OvalWithSidings(r, 2));
            Add(b.OvalWithLoop(r));
            Add(b.FigureEight(r));
        }
        var groups = b.RadiusGroups();
        for (var i = 0; i < groups.Count; i++)
            for (var j = i + 1; j < groups.Count; j++)
                Add(b.DoubleOval(groups[i], groups[j]));
        // Von jeder Form zuerst der größte Vorschlag, danach die übrigen
        var sorted = list.OrderByDescending(s => s.Used).ToList();
        var best = sorted.GroupBy(s => s.Name).Select(g => g.First()).ToList();
        return [.. best, .. sorted.Except(best).Take(Math.Max(0, MaxResults - best.Count))];
    }

    private sealed class Builder
    {
        private readonly LayoutPlan _board;
        private readonly Dictionary<string, int> _stock;
        private readonly List<Straight> _straights;
        private readonly List<Curve> _curves;
        private readonly List<Turnout> _turnouts;
        private readonly List<string> _buffers;
        private readonly List<TrackPieceDef> _crossings;
        private readonly BoardRect _area;
        private readonly double _margin;
        private readonly double _spacing;

        public Builder(LayoutPlan board, Dictionary<string, int> stock)
        {
            _board = board;
            _stock = stock;
            var defs = stock.Keys.Select(Catalog.Get).ToList();
            _straights = defs.Where(d => d.Kind == PieceKind.Straight).Select(d => new Straight(d.Key, d.Length)).OrderByDescending(s => s.Length).ToList();
            _curves = defs.Where(d => d.Kind == PieceKind.Curve).Select(d => new Curve(d.Key, d.Radius, d.Angle)).ToList();
            _turnouts = defs.Where(d => d.Kind == PieceKind.Switch).Select(d =>
            {
                var c = TrackGeometry.LocalConnectors(d, false);
                return new Turnout(d.Key, c[1].X, Math.Abs(Angles.Normalize(c[2].Heading)), d.Radius);
            }).Where(t => t.Angle > 1).ToList();
            _buffers = defs.Where(d => d.Kind == PieceKind.Buffer).Select(d => d.Key).ToList();
            _crossings = defs.Where(d => d.Kind == PieceKind.Crossing).ToList();
            _area = BoardGeometry.Rects(board).OrderByDescending(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)).FirstOrDefault(new BoardRect(0, 0, board.BoardWidth, board.BoardHeight));
            _margin = Math.Max(board.Gauge.TrackWidth * 3, 8);
            _spacing = board.Gauge.TrackWidth * PlanCheck.MinSpacingFactor;
        }

        private double LongSide => Math.Max(_area.X1 - _area.X0, _area.Y1 - _area.Y0) - 2 * _margin;
        private double ShortSide => Math.Min(_area.X1 - _area.X0, _area.Y1 - _area.Y0) - 2 * _margin;

        /// <summary>Radien, aus denen sich ein Halbkreis legen lässt, kleinster zuerst.</summary>
        public IReadOnlyList<double> RadiusGroups() => _curves.Select(c => Math.Round(c.Radius, 1)).Distinct().Order().ToList();

        private List<Curve> CurvesOf(double radius) => _curves.Where(c => Math.Abs(c.Radius - radius) < 0.15).OrderByDescending(c => c.Angle).ToList();

        // ---------- Bestand ----------

        private int Avail(string key, Dictionary<string, int> used) => _stock.GetValueOrDefault(key) - used.GetValueOrDefault(key);

        private static void Use(Dictionary<string, int> used, IEnumerable<string> keys, int times = 1)
        {
            foreach (var k in keys) used[k] = used.GetValueOrDefault(k) + times;
        }

        /// <summary>Teile mit Wert, deren Summe genau <paramref name="target"/> ergibt; jedes Teil höchstens <c>Avail / share</c> mal.</summary>
        private static List<string>? Exact(IEnumerable<(string Key, double Value, int Avail)> items, double target, double tolerance, int maxPieces)
        {
            var order = items.Where(i => i.Avail > 0 && i.Value > 0).OrderByDescending(i => i.Value).ToList();
            var pick = new List<string>();
            var budget = 200_000;
            bool Go(int i, double rest, int left)
            {
                if (Math.Abs(rest) <= tolerance) return true;
                if (i >= order.Count || left == 0 || rest < -tolerance || --budget < 0) return false;
                var it = order[i];
                var max = Math.Min(Math.Min(it.Avail, left), (int)Math.Floor((rest + tolerance) / it.Value));
                for (var n = max; n >= 0; n--)
                {
                    for (var k = 0; k < n; k++) pick.Add(it.Key);
                    if (Go(i + 1, rest - n * it.Value, left - n)) return true;
                    pick.RemoveRange(pick.Count - n, n);
                }
                return false;
            }
            if (Math.Abs(target) <= tolerance) return [];
            return Go(0, target, maxPieces) ? pick : null;
        }

        /// <summary>Möglichst lange Gerade bis <paramref name="maxLength"/>, jedes Teil höchstens <c>Avail / share</c> mal (lange Stücke zuerst).</summary>
        private List<string> Fill(Dictionary<string, int> used, int share, double maxLength)
        {
            var pick = new List<string>();
            var len = 0.0;
            foreach (var s in _straights)
            {
                var n = Avail(s.Key, used) / share;
                while (n-- > 0 && len + s.Length <= maxLength + 0.01)
                {
                    pick.Add(s.Key);
                    len += s.Length;
                }
            }
            return pick;
        }

        private static double Length(IEnumerable<string> straights) => straights.Sum(k => Catalog.Get(k).Length);

        /// <summary>Bögen eines Radius, die zusammen <paramref name="angle"/> Grad ergeben; jedes Teil höchstens <c>Avail / share</c> mal.</summary>
        private List<string>? Arc(double radius, double angle, Dictionary<string, int> used, int share)
            => Exact(CurvesOf(radius).Select(c => (c.Key, c.Angle, Avail(c.Key, used) / share)), angle, 0.05, 64);

        private List<string>? ExactStraight(double length, Dictionary<string, int> used, int share = 1)
            => Exact(_straights.Select(s => (s.Key, s.Length, Avail(s.Key, used) / share)), length, 0.4, 12);

        // ---------- Verlegen ----------

        private sealed class Layer(string gaugeId)
        {
            public LayoutPlan Plan { get; } = new() { GaugeId = gaugeId };

            /// <summary>Legt ein Gleis mit Anschluss <paramref name="entry"/> an <paramref name="at"/> und gibt Anschluss <paramref name="exit"/> zurück.</summary>
            public Pose Lay(string key, Pose at, bool turnRight = false, int entry = 0, int exit = 1)
            {
                var d = Catalog.Get(key);
                var p = Plan.Add(key);
                p.Flipped = d.CanFlip && d.RightHand != turnRight;
                PlanEditing.PlaceAt(p, entry, at.X, at.Y, at.Heading);
                var c = p.WorldConnectors();
                return exit < c.Count ? c[exit] : at;
            }

            public Pose Chain(Pose at, IEnumerable<string> keys, bool turnRight = false)
            {
                foreach (var k in keys) at = Lay(k, at, turnRight);
                return at;
            }

            public PlacedPiece Last => Plan.Pieces[^1];
        }

        /// <summary>Richtet die Anlage aus (lange Seite der Platte), mittig auf die Platte, und prüft: geschlossen, auf der Platte, keine Fehler.</summary>
        private PlanSuggestion? Finish(Layer layer, string name, string description)
        {
            var pieces = layer.Plan.Pieces;
            if (pieces.Count == 0) return null;
            var (x0, y0, x1, y1) = Extent(pieces);
            var areaW = _area.X1 - _area.X0;
            var areaH = _area.Y1 - _area.Y0;
            // Gebaut wird waagerecht; bei hochkant stehender Platte um 90° drehen
            if (areaH > areaW && x1 - x0 > y1 - y0)
            {
                foreach (var p in pieces)
                {
                    (p.X, p.Y) = (-p.Y, p.X);
                    p.Rotation = Angles.Normalize(p.Rotation + 90);
                }
                (x0, y0, x1, y1) = Extent(pieces);
            }
            var w = x1 - x0;
            var h = y1 - y0;
            if (w > areaW - 2 * _margin + 0.5 || h > areaH - 2 * _margin + 0.5) return null;
            var dx = Math.Round((_area.X0 + _area.X1) / 2 - (x0 + x1) / 2);
            var dy = Math.Round((_area.Y0 + _area.Y1) / 2 - (y0 + y1) / 2);
            foreach (var p in pieces) { p.X += dx; p.Y += dy; }

            var check = new LayoutPlan
            {
                GaugeId = _board.GaugeId, BoardWidth = _board.BoardWidth, BoardHeight = _board.BoardHeight, BoardShape = _board.BoardShape,
                BoardDepth = _board.BoardDepth, BoardTurn = _board.BoardTurn, BoardLegLeft = _board.BoardLegLeft, BoardLegRight = _board.BoardLegRight,
            };
            check.BoardLegs.AddRange(_board.BoardLegs);
            check.Modules.AddRange(_board.Modules);
            check.Pieces.AddRange(pieces);
            if (Connectivity.Analyze(check).Open.Count > 0) return null;
            foreach (var p in pieces)
                foreach (var l in p.WorldLegs())
                    for (var t = 0.0; t <= 1; t += 0.25)
                        if (l.At(t) is var q && !BoardGeometry.Contains(check, q.X, q.Y)) return null;
            if (PlanCheck.Run(check).Any(i => i.Level == IssueLevel.Fehler)) return null;
            return new PlanSuggestion(name, description, pieces.ToList(), pieces.Count, Math.Round(w), Math.Round(h));
        }

        private static (double X0, double Y0, double X1, double Y1) Extent(IEnumerable<PlacedPiece> pieces)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in pieces)
                foreach (var l in p.WorldLegs())
                    for (var t = 0.0; t <= 1.0001; t += 0.125)
                    {
                        var q = l.At(t);
                        x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                    }
            return (x0, y0, x1, y1);
        }

        private string Size(double r) => Lang.F($"R {Fmt.Num(r, 0)} mm");

        // ---------- Oval ----------

        /// <summary>Geraden je Längsseite, so lang wie die Platte erlaubt.</summary>
        private List<string> Sides(double radius, Dictionary<string, int> used, int share, double reserve = 0)
            => Fill(used, share, LongSide - 2 * radius - reserve);

        public PlanSuggestion? Oval(double radius)
        {
            if (2 * radius > ShortSide) return null;
            var used = new Dictionary<string, int>();
            if (Arc(radius, 180, used, 2) is not { } half) return null;
            Use(used, half, 2);
            var side = Sides(radius, used, 2);
            var layer = new Layer(_board.GaugeId);
            var e = layer.Chain(new Pose(0, 0, 0), side);
            e = layer.Chain(e, half, true);
            e = layer.Chain(e, side);
            layer.Chain(e, half, true);
            return Finish(layer, Lang.T("Oval"), Lang.F($"Einfacher Rundkurs, {Size(radius)}"));
        }

        /// <summary>Weiche (zweigt nach innen ab), Gegenbogen, Geraden, Prellbock. Gibt das Ende des Hauptgleises zurück.</summary>
        private Pose Siding(Layer layer, Pose at, Turnout t, Curve counter, IReadOnlyList<string> straights, string buffer)
        {
            var sw = layer.Lay(t.Key, at, turnRight: true);
            var branch = layer.Last.WorldConnectors()[2];
            var e = layer.Lay(counter.Key, branch, turnRight: false);
            e = layer.Chain(e, straights);
            layer.Lay(buffer, e);
            return sw;
        }

        private Curve? CounterCurve(Turnout t)
            => _curves.Where(c => Math.Abs(c.Angle - t.Angle) < 0.3).OrderBy(c => Math.Abs(c.Radius - t.Radius)).FirstOrDefault();

        public PlanSuggestion? OvalWithSidings(double radius, int count)
        {
            if (2 * radius > ShortSide || _buffers.Count == 0) return null;
            PlanSuggestion? best = null;
            foreach (var t in _turnouts)
            {
                if (CounterCurve(t) is not { } counter) continue;
                var used = new Dictionary<string, int>();
                if (Arc(radius, 180, used, 2) is not { } half) continue;
                Use(used, half, 2);
                Use(used, [t.Key], count);
                Use(used, [counter.Key], count);
                var buffer = _buffers.FirstOrDefault(k => Avail(k, used) >= count);
                if (buffer is null || _stock[t.Key] < count || Avail(counter.Key, used) < 0 || Avail(t.Key, used) < 0) continue;
                Use(used, [buffer], count);
                var side = Sides(radius, used, 2, t.Length);
                var sideUsed = new Dictionary<string, int>(used);
                Use(sideUsed, side, 2);
                // Ohne zweite Weiche braucht die Gegenseite Geraden in Weichenlänge
                List<string>? fill = [];
                if (count == 1 && (fill = ExactStraight(t.Length, sideUsed)) is null) continue;
                Use(sideUsed, fill);
                // Abstellgleis so lang wie möglich, bis es passt
                for (var max = LongSide; max >= 0; max -= Math.Max(LongSide / 12, 20))
                {
                    var stub = Fill(sideUsed, count, max);
                    var layer = new Layer(_board.GaugeId);
                    var e = Siding(layer, new Pose(0, 0, 0), t, counter, stub, buffer);
                    e = layer.Chain(e, side);
                    e = layer.Chain(e, half, true);
                    e = count == 2 ? Siding(layer, e, t, counter, stub, buffer) : layer.Chain(e, fill);
                    e = layer.Chain(e, side);
                    layer.Chain(e, half, true);
                    var name = count == 1 ? Lang.T("Oval mit Abstellgleis") : Lang.T("Oval mit zwei Abstellgleisen");
                    var s = Finish(layer, name, count == 1
                        ? Lang.F($"Rundkurs {Size(radius)}, Abstellgleis innen ({Fmt.Num(Length(stub), 0)} mm Gerade)")
                        : Lang.F($"Rundkurs {Size(radius)}, zwei Abstellgleise innen ({Fmt.Num(Length(stub), 0)} mm Gerade)"));
                    if (s is not null)
                    {
                        if (best is null || s.Used > best.Used) best = s;
                        break;
                    }
                }
            }
            return best;
        }

        /// <summary>Oval mit Ausweiche (zwei Weichen, Parallelgleis innen) auf einer Längsseite.</summary>
        public PlanSuggestion? OvalWithLoop(double radius)
        {
            if (2 * radius > ShortSide) return null;
            PlanSuggestion? best = null;
            foreach (var t in _turnouts)
            {
                if (CounterCurve(t) is not { } counter || _stock[t.Key] < 2) continue;
                var baseUsed = new Dictionary<string, int>();
                if (Arc(radius, 180, baseUsed, 2) is not { } half) continue;
                Use(baseUsed, half, 2);
                Use(baseUsed, [t.Key], 2);
                Use(baseUsed, [counter.Key], 2);
                if (Avail(counter.Key, baseUsed) < 0) continue;
                // Länge des Parallelgleises: n gleiche Geraden, die längste Ausweiche zuerst
                foreach (var s in _straights)
                    for (var n = Avail(s.Key, baseUsed); n >= 1; n--)
                    {
                        var used = new Dictionary<string, int>(baseUsed);
                        var loop = Enumerable.Repeat(s.Key, n).ToList();
                        Use(used, loop);
                        var layer = new Layer(_board.GaugeId);
                        var start = new Pose(0, 0, 0);
                        layer.Lay(t.Key, start, turnRight: true);
                        var a = layer.Last.WorldConnectors();
                        var e = layer.Lay(counter.Key, a[2], turnRight: false);
                        e = layer.Chain(e, loop);
                        e = layer.Lay(counter.Key, e, turnRight: false);
                        // zweite Weiche mit dem abzweigenden Strang ansetzen
                        var swB = layer.Lay(t.Key, e, turnRight: false, entry: 2, exit: 0);
                        var b1 = layer.Last.WorldConnectors()[1];
                        if (Math.Abs(b1.Y - a[1].Y) > 0.3 || b1.X <= a[1].X) continue;
                        var gap = b1.X - a[1].X;
                        if (ExactStraight(gap, used) is not { } main) continue;
                        Use(used, main);
                        layer.Chain(a[1], main);
                        var blockLength = swB.X - start.X;
                        if (blockLength > LongSide - 2 * radius) continue;
                        var side = Sides(radius, used, 2, blockLength);
                        Use(used, side, 2);
                        if (ExactStraight(blockLength, used) is not { } fill) continue;
                        e = layer.Chain(swB, side);
                        e = layer.Chain(e, half, true);
                        e = layer.Chain(e, fill);
                        e = layer.Chain(e, side);
                        layer.Chain(e, half, true);
                        var r = Finish(layer, Lang.T("Oval mit Ausweiche"), Lang.F($"Rundkurs {Size(radius)}, Ausweichgleis innen ({Fmt.Num(Length(loop), 0)} mm Gerade)"));
                        if (r is not null && (best is null || r.Used > best.Used)) best = r;
                        if (r is not null) goto nextTurnout;
                    }
                nextTurnout:;
            }
            return best;
        }

        /// <summary>Zwei Ovale ineinander (zweigleisige Strecke).</summary>
        public PlanSuggestion? DoubleOval(double inner, double outer)
        {
            if (outer - inner < _spacing || 2 * outer > ShortSide) return null;
            var used = new Dictionary<string, int>();
            if (Arc(inner, 180, used, 2) is not { } halfIn) return null;
            Use(used, halfIn, 2);
            if (Arc(outer, 180, used, 2) is not { } halfOut) return null;
            Use(used, halfOut, 2);
            var side = Sides(outer, used, 4);
            var layer = new Layer(_board.GaugeId);
            var e = layer.Chain(new Pose(0, outer - inner, 0), side);
            e = layer.Chain(e, halfIn, true);
            e = layer.Chain(e, side);
            layer.Chain(e, halfIn, true);
            e = layer.Chain(new Pose(0, 0, 0), side);
            e = layer.Chain(e, halfOut, true);
            e = layer.Chain(e, side);
            layer.Chain(e, halfOut, true);
            return Finish(layer, Lang.T("Doppeloval"), Lang.F($"Zwei Rundkurse ineinander, {Size(inner)} und {Size(outer)}"));
        }

        /// <summary>
        /// Acht mit Kreuzung in der Mitte. Jede Schleife dreht um 180° plus Kreuzungswinkel und ist spiegelgleich:
        /// Arm, halber Bogen, Gerade im Scheitel, halber Bogen, Arm. Die Scheitelgerade gleicht den Abstand aus, damit die Schleife schließt.
        /// </summary>
        public PlanSuggestion? FigureEight(double radius)
        {
            PlanSuggestion? best = null;
            foreach (var x in _crossings)
            {
                var theta = x.Angle;
                var used = new Dictionary<string, int> { [x.Key] = 1 };
                // Bögen dieses Radius, zur Not ergänzt um etwas größere (z. B. Weichengegenbögen)
                var pool = _curves.Where(c => c.Radius >= radius - 0.15 && c.Radius <= radius + 3 * _spacing).ToList();
                if (Exact(pool.Select(c => (c.Key, c.Angle, Avail(c.Key, used) / 4)), (180 + theta) / 2, 0.05, 32) is not { } half) continue;
                if (!half.Any(k => Math.Abs(Catalog.Get(k).Radius - radius) < 0.15)) continue;
                half = [.. half.OrderBy(k => Math.Abs(Catalog.Get(k).Radius - radius) < 0.15 ? 1 : 0)];
                Use(used, half, 4);

                Layer Lay(List<string> arm, List<string> apex)
                {
                    var layer = new Layer(_board.GaugeId);
                    layer.Lay(x.Key, new Pose(0, 0, -theta / 2), entry: 0, exit: 1);
                    var c = layer.Last.WorldConnectors();
                    // rechte Schleife im Uhrzeigersinn, linke gegen den Uhrzeigersinn
                    foreach (var (from, right) in new[] { (c[1], true), (c[2], false) })
                    {
                        var e = layer.Chain(from, arm);
                        e = layer.Chain(e, half, right);
                        e = layer.Chain(e, apex);
                        e = layer.Chain(e, Enumerable.Reverse(half), right);
                        layer.Chain(e, Enumerable.Reverse(arm));
                    }
                    return layer;
                }

                var lastArm = -1.0;
                for (var max = LongSide / 2; max >= 0; max -= 5)
                {
                    var arm = Fill(used, 4, max);
                    if (Math.Abs(Length(arm) - lastArm) < 0.01) continue;
                    lastArm = Length(arm);
                    var armUsed = new Dictionary<string, int>(used);
                    Use(armUsed, arm, 4);
                    // Ohne Scheitelgerade legen und messen, wie weit das Ende der rechten Schleife vom Kreuzungsanschluss entfernt ist
                    var probe = Lay(arm, []);
                    var target = probe.Plan.Pieces[0].WorldConnectors()[3];
                    var end = probe.Plan.Pieces[(probe.Plan.Pieces.Count - 1) / 2].WorldConnectors()[1];
                    double gx = target.X - end.X, gy = target.Y - end.Y;
                    // die Lücke muss in Richtung des Scheitels zeigen (bei der rechten Schleife nach unten)
                    var apexDir = (-theta / 2 + (180 + theta) / 2) * Math.PI / 180;
                    var along = gx * Math.Cos(apexDir) + gy * Math.Sin(apexDir);
                    var across = -gx * Math.Sin(apexDir) + gy * Math.Cos(apexDir);
                    if (Math.Abs(across) > 0.5 || along < -0.4) continue;
                    if (Exact(_straights.Select(t => (t.Key, t.Length, Avail(t.Key, armUsed) / 2)), along, 0.6, 8) is not { } apex) continue;
                    var found = Finish(Lay(arm, apex), Lang.T("Acht"), Lang.F($"Achterschleife mit Kreuzung, {Size(radius)}"));
                    if (found is not null)
                    {
                        if (best is null || found.Used > best.Used) best = found;
                        break;
                    }
                }
            }
            return best;
        }
    }
}

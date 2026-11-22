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

    /// <summary>Schlüssel für den Bestand an Flexgleis-Stangen im Bestand (<see cref="Build"/>), z. B. „flexstock|flex-mk“.</summary>
    public static string FlexStockKey(string flexSystemId) => FlexStockPrefix + flexSystemId;
    private const string FlexStockPrefix = "flexstock|";

    /// <summary>
    /// Bogenweiche, die der Vorschlag in einen Halbkreis einbauen kann: der innere Strang ist ein Bogen (Radius, Winkel),
    /// der äußere dieselbe Kurve mit einer Geraden davor (z. B. Märklin C-Gleis 24671: 77,5 mm + R 360 · 30°).
    /// </summary>
    public static (double Radius, double Angle, double Straight)? CurvedTurnout(TrackPieceDef d)
    {
        if (d.Kind != PieceKind.Switch || d.Routes is not null || d.Paths is not [var outer, var inner]) return null;
        if (inner is not [{ Turn: not 0 } arc] || outer is not [{ Turn: 0, Length: > 0 } straight, { Turn: not 0 } outerArc]) return null;
        if (outerArc.Turn != arc.Turn || Math.Abs(outerArc.Radius - arc.Radius) > 0.1 || Math.Abs(outerArc.Angle - arc.Angle) > 0.01) return null;
        return (arc.Radius, arc.Angle, straight.Length);
    }

    /// <summary>Teile, die der Vorschlag verwenden kann (Geraden, Bögen, einfache Weichen, Prellböcke, Kreuzungen).</summary>
    public static bool IsUsable(TrackPieceDef d) => !d.IsFlex && d.Kind switch
    {
        PieceKind.Straight => d.Length > 0,
        PieceKind.Curve => d.Radius > 0 && d.Angle > 0 && d.Routes is null && d.Paths is null,
        PieceKind.Switch => (d.IsPlainTurnout || CurvedTurnout(d) is not null) && d.Drives == 1,
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
        FlexStock? flex = null;
        var flexBudget = 0.0;
        foreach (var (key, n) in inventory)
        {
            if (n <= 0) continue;
            if (key.StartsWith(FlexStockPrefix, StringComparison.Ordinal))
            {
                // Flexgleis: Stangen mit 5 % Verschnitt wie in der Stückliste; mehrere Sorten zählen zusammen als die erste
                if (Catalog.FlexOf(board.GaugeId, key[FlexStockPrefix.Length..]) is { } f)
                {
                    flex ??= f;
                    flexBudget += n * f.Length * 0.95;
                }
            }
            else if (Catalog.Find(key) is { } d && d.GaugeId == board.GaugeId && IsUsable(d))
                stock[d.Key] = n;
        }
        if (stock.Count == 0 && flex is null) return [];
        var b = new Builder(board, stock, flex, flexBudget);
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
            Add(b.OvalWithCurvedSidings(r, 1));
            Add(b.OvalWithCurvedSidings(r, 2));
            Add(b.OvalWithLoop(r));
            Add(b.FigureEight(r));
        }
        foreach (var s in b.Extra) Add(s);
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
        /// <summary>Weichengruppe (Schlüssel des ersten Teils) → Teile mit Ausführung (+1 rechts / −1 links) und Bestand.</summary>
        private readonly Dictionary<string, List<(string Key, int Hand, int Stock)>> _hands = [];
        private readonly List<(string Key, (double Radius, double Angle, double Straight) Shape)> _curvedTurnouts;
        private readonly List<TrackPieceDef> _crossings;
        private readonly BoardRect _area;
        private readonly double _margin;
        private readonly double _spacing;
        /// <summary>Flexgleis im Bestand: daraus entstehen Bögen und Geraden in beliebiger Länge, zusammen höchstens <see cref="_flexBudget"/> mm.</summary>
        private readonly FlexStock? _flex;
        private readonly double _flexBudget;
        private readonly HashSet<string> _flexKeys = [];
        /// <summary>Unter diesem Schlüssel zählt <c>used</c> die verbrauchte Flexgleislänge in mm.</summary>
        private const string FlexUsed = "~flex";

        public Builder(LayoutPlan board, Dictionary<string, int> stock, FlexStock? flex, double flexBudget)
        {
            _board = board;
            _stock = stock;
            _flex = flex;
            _flexBudget = flexBudget;
            // Linke und rechte Ausführung derselben Weiche zählen beim Planen zusammen; welche wo liegt, entscheidet Finish
            foreach (var g in stock.Keys.Select(Catalog.Get).Where(d => d.Kind == PieceKind.Switch && d.IsPlainTurnout).GroupBy(TurnoutShape).ToList())
            {
                var rep = g.First().Key;
                _hands[rep] = g.Select(d => (d.Key, Hand(d, false), stock[d.Key])).ToList();
                foreach (var d in g.Skip(1)) { stock[rep] += stock[d.Key]; stock.Remove(d.Key); }
            }
            var defs = stock.Keys.Select(Catalog.Get).ToList();
            _straights = defs.Where(d => d.Kind == PieceKind.Straight).Select(d => new Straight(d.Key, d.Length)).OrderByDescending(s => s.Length).ToList();
            _curves = defs.Where(d => d.Kind == PieceKind.Curve).Select(d => new Curve(d.Key, d.Radius, d.Angle)).ToList();
            _curvedTurnouts = defs.Where(d => d.Kind == PieceKind.Switch && !d.IsPlainTurnout && CurvedTurnout(d) is not null)
                .Select(d => (d.Key, CurvedTurnout(d)!.Value)).ToList();
            _turnouts = defs.Where(d => d.Kind == PieceKind.Switch && d.IsPlainTurnout).Select(d =>
            {
                var c = TrackGeometry.LocalConnectors(d, false);
                return new Turnout(d.Key, c[1].X, Math.Abs(Angles.Normalize(c[2].Heading)), d.Radius);
            }).Where(t => t.Angle > 1).ToList();
            _buffers = defs.Where(d => d.Kind == PieceKind.Buffer).Select(d => d.Key).ToList();
            _crossings = defs.Where(d => d.Kind == PieceKind.Crossing).ToList();
            if (flex is not null)
            {
                // Bögen aus Flexgleis: in den Radien der Weichen und in üblichen Radien der Spur, je 30° und im Weichenwinkel als Gegenbogen
                var tw = board.Gauge.TrackWidth;
                var radii = new[] { 22, 26, 30, 36 }.Select(k => Math.Round(k * tw / 5) * 5)
                    .Concat(_turnouts.Select(t => Math.Round(t.Radius, 1)).Where(r => r > 0)).Distinct().ToList();
                void AddFlexCurve(double r, double a)
                {
                    var key = Catalog.FlexCurveKey(board.GaugeId, r, a, flex.SystemId);
                    if (Catalog.Find(key) is null || !_flexKeys.Add(key)) return;
                    _curves.Add(new Curve(key, r, a));
                }
                foreach (var r in radii) AddFlexCurve(r, 30);
                foreach (var t in _turnouts)
                    if (t.Radius > 0) AddFlexCurve(Math.Round(t.Radius, 1), Math.Round(t.Angle, 3));
            }
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

        private int Avail(string key, Dictionary<string, int> used)
            => IsFlex(key) ? (int)Math.Floor(FlexLeft(used) / Catalog.Get(key).TrackLength)
             : _stock.GetValueOrDefault(key) - used.GetValueOrDefault(key);

        private bool IsFlex(string key) => _flexKeys.Contains(key);

        /// <summary>Noch freie Flexgleislänge in mm.</summary>
        private double FlexLeft(Dictionary<string, int> used) => _flexBudget - used.GetValueOrDefault(FlexUsed);

        private void Use(Dictionary<string, int> used, IEnumerable<string> keys, int times = 1)
        {
            foreach (var k in keys)
            {
                used[k] = used.GetValueOrDefault(k) + times;
                if (IsFlex(k)) used[FlexUsed] = used.GetValueOrDefault(FlexUsed) + (int)Math.Ceiling(Catalog.Get(k).TrackLength) * times;
            }
        }

        /// <summary>Gerades Flexgleis der Länge <paramref name="length"/> (auf 1 mm abgerundet), wenn genug Flexgleis da ist.</summary>
        private string? FlexStraight(double length, Dictionary<string, int> used, int share)
        {
            if (_flex is null) return null;
            var len = Math.Floor(Math.Min(length, FlexLeft(used) / share - 1));
            if (len < 5) return null;
            var key = Catalog.FlexStraightKey(_board.GaugeId, len, _flex.SystemId);
            if (Catalog.Find(key) is null) return null;
            _flexKeys.Add(key);
            return key;
        }

        /// <summary>Teile mit Wert, deren Summe genau <paramref name="target"/> ergibt; jedes Teil höchstens <c>Avail / share</c> mal.</summary>
        private static List<string>? Exact(IEnumerable<(string Key, double Value, int Avail)> items, double target, double tolerance, int maxPieces, int budget = 200_000)
        {
            var order = items.Where(i => i.Avail > 0 && i.Value > 0).OrderByDescending(i => i.Value).ToList();
            var pick = new List<string>();
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
            // Rest mit Flexgleis auffüllen
            if (maxLength - len >= 5 && FlexStraight(maxLength - len, used, share) is { } flex) pick.Add(flex);
            return pick;
        }

        private static double Length(IEnumerable<string> straights) => straights.Sum(k => Catalog.Get(k).Length);

        private bool UsesFlex(IEnumerable<PlacedPiece> pieces) => pieces.Any(p => IsFlex(p.Key));

        /// <summary>Bögen eines Radius, die zusammen <paramref name="angle"/> Grad ergeben; jedes Teil höchstens <c>Avail / share</c> mal.</summary>
        private List<string>? Arc(double radius, double angle, Dictionary<string, int> used, int share)
            => Exact(CurvesOf(radius).Select(c => (c.Key, c.Angle, Avail(c.Key, used) / share)), angle, 0.05, 64);

        private List<string>? ExactStraight(double length, Dictionary<string, int> used, int share = 1, double tolerance = 0.4, int budget = 200_000)
        {
            if (Exact(_straights.Select(s => (s.Key, s.Length, Avail(s.Key, used) / share)), length, tolerance, 24, budget) is { } exact) return exact;
            // Sonst ein Flexgleis genau dieser Länge
            if (_flex is null || length < 5 || length * share > FlexLeft(used)) return null;
            var key = Catalog.FlexStraightKey(_board.GaugeId, Math.Round(length, 2), _flex.SystemId);
            if (Catalog.Find(key) is null) return null;
            _flexKeys.Add(key);
            return [key];
        }

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

        /// <summary>Lage des Abzweigs: +1 auf der einen, −1 auf der anderen Seite (gespiegelt umgekehrt).</summary>
        private static int Hand(TrackPieceDef d, bool flipped) => Math.Sign(TrackGeometry.LocalConnectors(d, flipped)[2].Y);

        /// <summary>Form einer Weiche ohne Seite: linke und rechte Ausführung ergeben dieselbe Form.</summary>
        private static (double, double, double, double) TurnoutShape(TrackPieceDef d)
        {
            var c = TrackGeometry.LocalConnectors(d, false);
            return (Math.Round(c[1].X, 1), Math.Round(c[2].X, 1), Math.Round(Math.Abs(c[2].Y), 1), Math.Round(Math.Abs(Angles.Normalize(c[2].Heading)), 1));
        }

        /// <summary>
        /// Setzt für jede Weiche die Ausführung (links/rechts) ein, die an ihrer Stelle gebraucht wird, statt eine Weiche zu spiegeln.
        /// Reicht der Bestand so nicht, wird die ganze Anlage gespiegelt (dann tauschen links und rechts). false, wenn beides nicht geht.
        /// </summary>
        private bool AssignHands(List<PlacedPiece> pieces)
        {
            var sw = pieces.Where(p => _hands.ContainsKey(p.Key)).ToList();
            if (sw.Count == 0) return true;
            bool Fits(int sign) => sw.GroupBy(p => p.Key).All(g =>
            {
                var members = _hands[g.Key];
                var plus = g.Count(p => Hand(p.Def, p.Flipped) * sign > 0);
                return members.Where(m => m.Hand > 0).Sum(m => m.Stock) >= plus && members.Where(m => m.Hand < 0).Sum(m => m.Stock) >= g.Count() - plus;
            });
            if (!Fits(1))
            {
                if (!Fits(-1)) return false;
                // an der x-Achse spiegeln: Anschluss 0 bleibt Bezug, gespiegelte Teile wechseln die Seite
                foreach (var p in pieces)
                {
                    var c = p.WorldConnectors()[0];
                    if (p.Def.CanFlip) p.Flipped = !p.Flipped;
                    PlanEditing.PlaceAt(p, 0, c.X, -c.Y, -c.Heading - 180);
                }
            }
            foreach (var g in sw.GroupBy(p => p.Key).ToList())
            {
                var left = _hands[g.Key].ToDictionary(m => m.Key, m => m.Stock);
                foreach (var p in g)
                {
                    var hand = Hand(p.Def, p.Flipped);
                    var key = _hands[g.Key].First(m => m.Hand == hand && left[m.Key] > 0).Key;
                    left[key]--;
                    if (key == p.Key && !p.Flipped) continue;
                    var c = p.WorldConnectors()[0];
                    p.Key = key;
                    p.Flipped = false;
                    PlanEditing.PlaceAt(p, 0, c.X, c.Y, c.Heading - 180);
                }
            }
            return true;
        }

        /// <summary>Richtet die Anlage aus (lange Seite der Platte), mittig auf die Platte, und prüft: geschlossen, auf der Platte, keine Fehler.</summary>
        private PlanSuggestion? Finish(Layer layer, string name, string description)
        {
            var pieces = layer.Plan.Pieces;
            if (pieces.Count == 0 || !AssignHands(pieces)) return null;
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
            if (UsesFlex(pieces)) description += Lang.T(", mit Flexgleis");
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

        /// <summary>
        /// Weiche (zweigt nach innen ab), Gegenbogen, Geraden, Prellbock. Gibt das Ende des Hauptgleises zurück.
        /// <paramref name="trailing"/>: Die Weiche wird von der Herzstückseite befahren, das Abstellgleis zeigt zurück
        /// (braucht die andere Ausführung, links statt rechts).
        /// </summary>
        private Pose Siding(Layer layer, Pose at, Turnout t, IReadOnlyList<string> counter, IReadOnlyList<string> straights, string buffer, bool trailing = false)
        {
            var sw = trailing ? layer.Lay(t.Key, at, turnRight: false, entry: 1, exit: 0) : layer.Lay(t.Key, at, turnRight: true);
            var branch = layer.Last.WorldConnectors()[2];
            var e = layer.Chain(branch, counter, turnRight: trailing);
            e = layer.Chain(e, straights);
            layer.Lay(buffer, e);
            return sw;
        }

        /// <summary>
        /// Gegenbogen hinter dem Abzweig, damit das Nebengleis parallel läuft: Bögen eines Radius, die zusammen den Weichenwinkel ergeben,
        /// möglichst im Radius der Weiche. Jedes Teil höchstens <c>Avail / share</c> mal. null, wenn es keine passenden Bögen gibt.
        /// </summary>
        private List<string>? CounterArc(Turnout t, Dictionary<string, int> used, int share)
        {
            foreach (var r in _curves.Select(c => Math.Round(c.Radius, 1)).Distinct().OrderBy(r => Math.Abs(r - t.Radius)))
                if (Exact(CurvesOf(r).Select(c => (c.Key, c.Angle, Avail(c.Key, used) / share)), t.Angle, 0.3, 8) is { Count: > 0 } arc)
                    return arc;
            return null;
        }

        public PlanSuggestion? OvalWithSidings(double radius, int count)
        {
            if (2 * radius > ShortSide || _buffers.Count == 0) return null;
            PlanSuggestion? best = null;
            // Ohne passenden Gegenbogen – oder wenn das Nebengleis damit zu nah am Hauptgleis liegt – läuft das Abstellgleis schräg nach innen
            var parallelDone = new HashSet<string>();
            foreach (var t in _turnouts)
            foreach (var parallel in new[] { true, false })
            {
                if (_stock[t.Key] < count || !parallel && parallelDone.Contains(t.Key)) continue;
                var used = new Dictionary<string, int>();
                if (Arc(radius, 180, used, 2) is not { } half) continue;
                Use(used, half, 2);
                Use(used, [t.Key], count);
                List<string> counter = [];
                if (parallel)
                {
                    if (CounterArc(t, used, count) is not { } arc) continue;
                    counter = arc;
                }
                Use(used, counter, count);
                var buffer = _buffers.FirstOrDefault(k => Avail(k, used) >= count);
                if (buffer is null) continue;
                Use(used, [buffer], count);
                // Zwei Abstellgleise: beide Längsseiten gleich. Eines: die Gegenseite braucht Geraden in Länge von Seite plus Weiche.
                var side = Sides(radius, used, count == 2 ? 2 : 1, t.Length);
                List<string>? other = side;
                var sideUsed = new Dictionary<string, int>(used);
                if (count == 2) Use(sideUsed, side, 2);
                else
                {
                    other = null;
                    while (true)
                    {
                        var trial = new Dictionary<string, int>(used);
                        Use(trial, side);
                        if ((other = ExactStraight(Length(side) + t.Length, trial)) is not null) { sideUsed = trial; break; }
                        if (side.Count == 0) break;
                        side = side[..^1];
                    }
                    if (other is null) continue;
                    Use(sideUsed, other);
                }
                var angled = counter.Count == 0;
                // Abstellgleis so lang wie möglich, bis es passt
                // Bei zwei Abstellgleisen: entweder beide gleich, oder das zweite am Ende der Gegenseite zurückzeigend (eine linke und eine rechte Weiche)
                for (var max = LongSide; max >= 0; max -= Math.Max(LongSide / 12, 20))
                foreach (var trailing in count == 2 ? new[] { false, true } : [false])
                {
                    var stub = Fill(sideUsed, count, max);
                    var layer = new Layer(_board.GaugeId);
                    var e = Siding(layer, new Pose(0, 0, 0), t, counter, stub, buffer);
                    e = layer.Chain(e, side);
                    e = layer.Chain(e, half, true);
                    e = count == 2 && !trailing ? Siding(layer, e, t, counter, stub, buffer) : e;
                    e = layer.Chain(e, other);
                    if (trailing) e = Siding(layer, e, t, counter, stub, buffer, trailing: true);
                    layer.Chain(e, half, true);
                    var name = count == 1 ? Lang.T("Oval mit Abstellgleis") : Lang.T("Oval mit zwei Abstellgleisen");
                    var where = angled ? Lang.T("schräg innen") : Lang.T("innen");
                    var s = Finish(layer, name, count == 1
                        ? Lang.F($"Rundkurs {Size(radius)}, Abstellgleis {where} ({Fmt.Num(Length(stub), 0)} mm Gerade)")
                        : Lang.F($"Rundkurs {Size(radius)}, zwei Abstellgleise {where} ({Fmt.Num(Length(stub), 0)} mm Gerade)"));
                    if (s is not null)
                    {
                        if (best is null || s.Used > best.Used) best = s;
                        if (parallel) parallelDone.Add(t.Key);
                        goto nextTurnout;
                    }
                }
                nextTurnout:;
            }
            return best;
        }

        /// <summary>
        /// Oval mit Bogenweiche: Die Weiche ersetzt einen Bogen im Halbkreis, das Oval läuft über den äußeren Strang (Gerade + Bogen),
        /// das Abstellgleis zweigt nach innen ab. Bei einem Abstellgleis gleicht im anderen Halbkreis eine Gerade an derselben Stelle die Länge aus.
        /// </summary>
        public PlanSuggestion? OvalWithCurvedSidings(double radius, int count)
        {
            if (2 * radius > ShortSide || _buffers.Count == 0) return null;
            PlanSuggestion? best = null;
            foreach (var (key, shape) in _curvedTurnouts)
            {
                if (Math.Abs(shape.Radius - radius) > 0.15 || _stock[key] < count) continue;
                var used = new Dictionary<string, int>();
                Use(used, [key], count);
                // beide Halbkreise enthalten dieselbe Bogenfolge, damit Weiche und Ausgleichsgerade an derselben Stelle liegen
                if (Arc(radius, 180 - shape.Angle, used, 2) is not { } restA) continue;
                Use(used, restA, 2);
                var buffer = _buffers.FirstOrDefault(k => Avail(k, used) >= count);
                if (buffer is null) continue;
                Use(used, [buffer], count);
                // Stelle der Weiche im Halbkreis: Der innere Strang folgt danach den restlichen Bögen und liegt am Ende parallel zur Längsseite,
                // im Abstand Gerade × sin(Winkel am Weichenanfang). Am besten liegt die Weiche daher im Scheitel (90°).
                double StartAngle(int pos) => restA.Take(pos).Sum(k => Catalog.Get(k).Angle);
                foreach (var pos in Enumerable.Range(0, restA.Count + 1).OrderBy(p => Math.Abs(StartAngle(p) - 90)))
                {
                    var start = StartAngle(pos) * Math.PI / 180;
                    if (shape.Straight * Math.Sin(start) < _spacing) continue;
                    var posUsed = new Dictionary<string, int>(used);
                    // Bögen des inneren Strangs bis zur Längsseite
                    var inner = restA[pos..];
                    if (inner.Distinct().Any(k => Avail(k, posUsed) < count * inner.Count(x => x == k))) continue;
                    Use(posUsed, inner, count);
                    List<string> halfB;
                    if (count == 2) halfB = [.. restA[..pos], key, .. restA[pos..]];
                    else
                    {
                        // Halbkreis B: Bögen von A plus ein Bogen im Weichenwinkel, davor an derselben Stelle die Ausgleichsgerade
                        var extra = CurvesOf(radius).FirstOrDefault(c => Math.Abs(c.Angle - shape.Angle) < 0.05 && Avail(c.Key, posUsed) > 0);
                        if (extra is null) break;
                        if (ExactStraight(shape.Straight, posUsed, tolerance: 0.3, budget: 20_000) is not { } comp) break;
                        Use(posUsed, [extra.Key]);
                        Use(posUsed, comp);
                        halfB = [.. restA[..pos], .. comp, extra.Key, .. restA[pos..]];
                    }
                    var halfA = new List<string>(restA);
                    // die Gerade in der Weiche (und die Ausgleichsgerade) verlängert das Oval um Gerade × cos(Winkel)
                    var side = Sides(radius, posUsed, 2, 2 * shape.Straight * Math.Abs(Math.Cos(start)));
                    var sideUsed = new Dictionary<string, int>(posUsed);
                    Use(sideUsed, side, 2);
                    for (var max = Length(side); max >= 0; max -= Math.Max(LongSide / 12, 20))
                    {
                        var stub = Fill(sideUsed, count, max);
                        var layer = new Layer(_board.GaugeId);
                        Pose HalfWithTurnout(Pose at, List<string> rest)
                        {
                            at = layer.Chain(at, rest[..pos], true);
                            var exit = layer.Lay(key, at, turnRight: true, entry: 0, exit: 1);
                            var e = layer.Chain(layer.Last.WorldConnectors()[2], inner, true);
                            e = layer.Chain(e, stub);
                            layer.Lay(buffer, e);
                            return layer.Chain(exit, rest[pos..], true);
                        }
                        var e = layer.Chain(new Pose(0, 0, 0), side);
                        e = HalfWithTurnout(e, halfA);
                        e = layer.Chain(e, side);
                        if (count == 2) HalfWithTurnout(e, halfA);
                        else
                        {
                            foreach (var k in halfB) e = layer.Lay(k, e, turnRight: true);
                        }
                        var name = count == 1 ? Lang.T("Oval mit Abstellgleis") : Lang.T("Oval mit zwei Abstellgleisen");
                        var r = Finish(layer, name, count == 1
                            ? Lang.F($"Rundkurs {Size(radius)}, Abstellgleis an einer Bogenweiche ({Fmt.Num(Length(stub), 0)} mm Gerade)")
                            : Lang.F($"Rundkurs {Size(radius)}, zwei Abstellgleise an Bogenweichen ({Fmt.Num(Length(stub), 0)} mm Gerade)"));
                        if (r is not null)
                        {
                            if (best is null || r.Used > best.Used) best = r;
                            goto next;
                        }
                    }
                }
                next:;
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
                if (_stock[t.Key] < 2) continue;
                var baseUsed = new Dictionary<string, int>();
                if (Arc(radius, 180, baseUsed, 2) is not { } half) continue;
                Use(baseUsed, half, 2);
                Use(baseUsed, [t.Key], 2);
                if (CounterArc(t, baseUsed, 2) is not { } counter) continue;
                Use(baseUsed, counter, 2);
                // Lage der Weichen ohne Ausweichgerade einmal bestimmen: Versatz und Länge des Hauptgleises
                Pose LayLoop(Layer layer, IReadOnlyList<string> loop, out Pose mainStart, out Pose mainEnd)
                {
                    layer.Lay(t.Key, new Pose(0, 0, 0), turnRight: true);
                    var a = layer.Last.WorldConnectors();
                    var e = layer.Chain(a[2], counter, turnRight: false);
                    e = layer.Chain(e, loop);
                    e = layer.Chain(e, Enumerable.Reverse(counter), turnRight: false);
                    // zweite Weiche mit dem abzweigenden Strang ansetzen
                    var swB = layer.Lay(t.Key, e, turnRight: false, entry: 2, exit: 0);
                    mainStart = a[1];
                    mainEnd = layer.Last.WorldConnectors()[1];
                    return swB;
                }
                LayLoop(new Layer(_board.GaugeId), [], out var s0, out var e0);
                if (Math.Abs(e0.Y - s0.Y) > 0.3) continue;
                var gap0 = e0.X - s0.X;
                // Länge des Parallelgleises: n gleiche Geraden, die längste Ausweiche zuerst; das Hauptgleis gleicht die Länge genau aus
                var loops = _straights.SelectMany(st => Enumerable.Range(1, Math.Max(0, Avail(st.Key, baseUsed))).Reverse()
                    .Select(n => Enumerable.Repeat(st.Key, n).ToList())).ToList();
                // Mit Flexgleis: Ausweichgleis aus einem Stück, von lang nach kurz
                for (var len = LongSide - 2 * radius - 2 * t.Length - gap0; _flex is not null && len >= 50; len -= Math.Max(50, LongSide / 20))
                    if (FlexStraight(len, baseUsed, 2) is { } flexLoop) loops.Add([flexLoop]);
                foreach (var loop in loops)
                    {
                        var used = new Dictionary<string, int>(baseUsed);
                        Use(used, loop);
                        var gap = gap0 + Length(loop);
                        if (gap <= 0 || gap + 2 * t.Length > LongSide - 2 * radius) continue;
                        if (ExactStraight(gap, used, tolerance: 0.6, budget: 20_000) is not { } main) continue;
                        Use(used, main);
                        var layer = new Layer(_board.GaugeId);
                        var swB = LayLoop(layer, loop, out var a1, out _);
                        layer.Chain(a1, main);
                        var blockLength = swB.X;
                        if (blockLength > LongSide - 2 * radius) continue;
                        var side = Sides(radius, used, 2, blockLength);
                        Use(used, side, 2);
                        if (ExactStraight(blockLength, used, tolerance: 0.6, budget: 20_000) is not { } fill) continue;
                        var e = layer.Chain(swB, side);
                        e = layer.Chain(e, half, true);
                        e = layer.Chain(e, fill);
                        e = layer.Chain(e, side);
                        layer.Chain(e, half, true);
                        var r = Finish(layer, Lang.T("Oval mit Ausweiche"), Lang.F($"Rundkurs {Size(radius)}, Ausweichgleis innen ({Fmt.Num(Length(loop), 0)} mm Gerade)"));
                        if (r is null) continue;
                        if (best is null || r.Used > best.Used) best = r;
                        // Mit weiteren Weichen: auf der Gegenseite zusätzlich ein Abstellgleis
                        if (LoopWithSiding(radius, used, loop, main, side, half, blockLength, LayLoop) is { } more) Extra.Add(more);
                        goto nextTurnout;
                    }
                nextTurnout:;
            }
            return best;
        }

        /// <summary>Weitere Vorschläge, die beim Bauen nebenbei entstehen (Ausweiche mit Abstellgleis).</summary>
        public List<PlanSuggestion> Extra { get; } = [];

        private delegate Pose LoopLayer(Layer layer, IReadOnlyList<string> loop, out Pose mainStart, out Pose mainEnd);

        /// <summary>Oval mit Ausweiche wie in <see cref="OvalWithLoop"/>, auf der Gegenseite statt der Ausgleichsgeraden ein Abstellgleis nach innen.</summary>
        private PlanSuggestion? LoopWithSiding(double radius, Dictionary<string, int> loopUsed, List<string> loop, List<string> main, List<string> side,
            List<string> half, double blockLength, LoopLayer layLoop)
        {
            if (_buffers.Count == 0) return null;
            foreach (var t in _turnouts)
            foreach (var parallel in new[] { true, false })
            {
                var used = new Dictionary<string, int>(loopUsed);
                if (Avail(t.Key, used) < 1) continue;
                Use(used, [t.Key]);
                List<string> counter = [];
                if (parallel)
                {
                    if (CounterArc(t, used, 1) is not { } arc) continue;
                    counter = arc;
                }
                Use(used, counter);
                var buffer = _buffers.FirstOrDefault(k => Avail(k, used) >= 1);
                if (buffer is null) continue;
                Use(used, [buffer]);
                // Gegenseite: Weiche plus Geraden so lang wie Ausweichblock plus Seite
                if (blockLength + Length(side) - t.Length is var rest && rest < 0) continue;
                if (ExactStraight(rest, used, tolerance: 0.6, budget: 20_000) is not { } other) continue;
                Use(used, other);
                for (var max = Length(other); max >= 0; max -= Math.Max(LongSide / 12, 20))
                {
                    var stub = Fill(used, 1, max);
                    var layer = new Layer(_board.GaugeId);
                    var swB = layLoop(layer, loop, out var a1, out _);
                    layer.Chain(a1, main);
                    var e = layer.Chain(swB, side);
                    e = layer.Chain(e, half, true);
                    e = Siding(layer, e, t, counter, stub, buffer);
                    e = layer.Chain(e, other);
                    layer.Chain(e, half, true);
                    var s = Finish(layer, Lang.T("Oval mit Ausweiche und Abstellgleis"),
                        Lang.F($"Rundkurs {Size(radius)}, Ausweichgleis ({Fmt.Num(Length(loop), 0)} mm) und Abstellgleis ({Fmt.Num(Length(stub), 0)} mm Gerade)"));
                    if (s is not null) return s;
                }
            }
            return null;
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

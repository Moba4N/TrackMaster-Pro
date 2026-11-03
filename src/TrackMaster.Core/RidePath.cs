namespace TrackMaster.Core;

/// <summary>Eine Richtung, die der Zug an einer spitz befahrenen Weiche oder Kreuzungsweiche nehmen kann.</summary>
/// <param name="Route">Stellung (0 = Grundstellung, 1 = abzweigend …), wie sie in den Weichenstellungen steht.</param>
/// <param name="Side">−1 links, 0 geradeaus, +1 rechts (in Fahrtrichtung gesehen).</param>
public sealed record RideChoice(int Route, int Side)
{
    public string Label => Side < 0 ? Lang.T("links") : Side > 0 ? Lang.T("rechts") : Lang.T("geradeaus");
    public string Arrow => Side < 0 ? "◀" : Side > 0 ? "▶" : "▲";
}

/// <summary>Spitz befahrene Weiche oder Kreuzungsweiche auf dem Fahrweg: Wegstrecke bis zur Weichenspitze, Gleis, Einfahrt.</summary>
public readonly record struct RideStop(double Distance, int PieceId, int Entry, int PointIndex);

/// <summary>
/// Fahrweg zum Mitfahren: ab einem Gleis, an Weichen und Kreuzungsweichen nach der gewählten Stellung (Vorgabe Grundstellung),
/// an Prellböcken und offenen Enden zurück. Wird eine Weiche vor dem Zug umgestellt, ändert sich nur der Weg ab dieser Weiche
/// (<see cref="Reroute"/>); der Weg wächst beim Fahren nach (<see cref="EnsureAhead"/>), der Zug fährt also beliebig lange.
/// </summary>
public sealed class RidePath
{
    private readonly LayoutPlan _plan;
    private readonly Connectivity _conn;
    private readonly double _trackTotal;
    private readonly List<(double X, double Y, double Z)> _points = [];
    private readonly List<double> _dist = [];
    private readonly List<RideStop> _stops = [];
    private (int Piece, int Entry)? _resume;

    public IReadOnlyList<(double X, double Y, double Z)> Points => _points;
    public IReadOnlyList<double> Distances => _dist;
    /// <summary>Spitz befahrene Weichen und Kreuzungsweichen in Fahrtrichtung.</summary>
    public IReadOnlyList<RideStop> Stops => _stops;
    /// <summary>Spitz befahrene Weichen: Wegstrecke bis zur Weichenspitze und Gleis-Kennung.</summary>
    public IReadOnlyList<(double Distance, int PieceId)> Switches => _stops.Select(s => (s.Distance, s.PieceId)).ToList();
    /// <summary>Rundkurs: der Weg kehrt zum Anfang zurück.</summary>
    public bool Closed { get; private set; }
    public double Length => _dist.Count == 0 ? 0 : _dist[^1];
    public bool IsEmpty => _points.Count < 2;

    private RidePath(LayoutPlan plan, Connectivity conn, double trackTotal)
    {
        _plan = plan;
        _conn = conn;
        _trackTotal = trackTotal;
    }

    /// <summary>Maßstab „1:87“ → 87.</summary>
    public static double ScaleDivisor(string scale)
        => double.TryParse(scale.Split(':').Last().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 87;

    /// <summary>Vorbildgeschwindigkeit in km/h als Modell-mm je Sekunde.</summary>
    public static double ModelSpeed(double kmh, string scale) => kmh / 3.6 * 1000 / ScaleDivisor(scale);

    public (double X, double Y, double Z) PointAt(double s)
    {
        var total = Length;
        if (_points.Count == 0) return (0, 0, 0);
        if (_points.Count == 1) return _points[0];
        if (Closed && total > 0) s = ((s % total) + total) % total;
        else s = Math.Clamp(s, 0, total);
        var i = _dist.BinarySearch(s);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, _dist.Count - 1);
        var seg = _dist[i] - _dist[i - 1];
        var t = seg <= 0 ? 0 : (s - _dist[i - 1]) / seg;
        var a = _points[i - 1];
        var b = _points[i];
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    /// <summary>Kamera beim Mitfahren: Augpunkt und Blickziel in 3D-Koordinaten (X, Höhe, Z).</summary>
    public (Vec3 Eye, Vec3 Target) Camera(double s, double gauge)
    {
        // Augpunkt etwa in Führerstandshöhe, Blick leicht nach unten auf das Gleis vor dem Zug
        var eye = PointAt(s);
        var ahead = PointAt(s + gauge * 10);
        var rail = Scene3DModel.RailTop(gauge);
        return (new Vec3(eye.X, eye.Z + rail + gauge * 1.9, eye.Y), new Vec3(ahead.X, ahead.Z + rail + gauge * 0.55, ahead.Y));
    }

    /// <summary>Nächste spitz befahrene Weiche oder Kreuzungsweiche vor <paramref name="s"/> (beim Rundkurs auch nach dem Anfang).</summary>
    public RideStop? NextStop(double s)
    {
        if (_stops.Count == 0) return null;
        if (Closed && Length > 0) s = ((s % Length) + Length) % Length;
        foreach (var w in _stops)
            if (w.Distance > s + 1e-6) return w;
        return Closed ? _stops[0] with { Distance = _stops[0].Distance + Length } : null;
    }

    /// <summary>Nächste spitz befahrene Weiche vor <paramref name="s"/>: Wegstrecke und Gleis-Kennung.</summary>
    public (double Distance, int PieceId)? NextSwitch(double s) => NextStop(s) is { } w ? (w.Distance, w.PieceId) : null;

    /// <summary>Anzahl der Stellungen einer Weiche (zwei, Dreiwegweiche drei, Kreuzungsweiche zwei).</summary>
    public static int RouteCount(PlacedPiece piece)
        => piece.Def.IsDoubleSlip ? 2 : piece.Def.Kind == PieceKind.Switch ? Math.Max(1, TrackGeometry.LocalConnectors(piece.Def, piece.Flipped).Count - 1) : 1;

    /// <summary>Gewählte Stellung einer Weiche (0, falls nichts gewählt).</summary>
    public static int RouteOf(PlacedPiece piece, IReadOnlyDictionary<int, int>? routes)
    {
        var n = RouteCount(piece);
        return routes is not null && routes.TryGetValue(piece.Id, out var r) ? ((r % n) + n) % n : 0;
    }

    /// <summary>Kreuzungsweiche: von welchem Anschluss in welchen abzweigend gefahren wird.</summary>
    private static int SlipExit(int entry) => entry switch { 0 => 3, 3 => 0, 1 => 2, _ => 1 };

    private static int CrossExit(int entry) => entry switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

    /// <summary>Ausfahrt aus einem Gleis bei Einfahrt <paramref name="entry"/> und Stellung <paramref name="route"/>.</summary>
    private static int ExitOf(PlacedPiece piece, int entry, int route, int count)
    {
        var d = piece.Def;
        if (d.IsDoubleSlip && count == 4) return route == 1 ? SlipExit(entry) : CrossExit(entry);
        return d.Kind switch
        {
            PieceKind.Switch when entry == 0 && count > 2 => 1 + route,
            PieceKind.Crossing => CrossExit(entry),
            PieceKind.Buffer => 0,
            _ when count == 1 => 0,
            _ => entry == 0 ? 1 : 0,
        };
    }

    /// <summary>
    /// Richtungen an einer Weiche, links nach rechts geordnet. Die Seite ergibt sich aus der Richtung der Ausfahrt gegenüber der
    /// Einfahrt; bei Bogenweichen (beide Stränge biegen gleich ab) ist der stärker linke Strang „links“.
    /// </summary>
    public IReadOnlyList<RideChoice> Choices(RideStop stop) => _plan.Find(stop.PieceId) is { } p ? Choices(p, stop.Entry) : [];

    public static IReadOnlyList<RideChoice> Choices(PlacedPiece piece, int entry)
    {
        var conns = piece.WorldConnectors();
        var n = RouteCount(piece);
        if (n < 2 || entry >= conns.Count) return [];
        var inward = conns[entry].Heading + 180;
        // Positiver Winkel = im Uhrzeigersinn = in Fahrtrichtung nach rechts (y zeigt nach unten)
        var turns = Enumerable.Range(0, n).Select(r => (Route: r, Delta: Angles.Normalize(conns[ExitOf(piece, entry, r, conns.Count)].Heading - inward)))
                              .OrderBy(t => t.Delta).ToList();
        var straight = turns.Count(t => Math.Abs(t.Delta) < 0.5);
        var result = new List<RideChoice>();
        for (var i = 0; i < turns.Count; i++)
        {
            var (route, delta) = turns[i];
            int side;
            if (straight == 1) side = Math.Abs(delta) < 0.5 ? 0 : Math.Sign(delta);
            else if (turns.Count == 2) side = i == 0 ? -1 : 1;
            else side = i == 0 ? -1 : i == turns.Count - 1 ? 1 : 0;
            result.Add(new RideChoice(route, side));
        }
        return result;
    }

    /// <summary>Text für die Statuszeile: nächste Weiche, Entfernung und gewählte Richtung.</summary>
    public string Describe(double s, IReadOnlyDictionary<int, int>? routes)
    {
        if (NextStop(s) is not { } next || _plan.Find(next.PieceId) is not { } piece) return Lang.T("Keine Weiche voraus");
        var route = RouteOf(piece, routes);
        var label = Choices(next).FirstOrDefault(c => c.Route == route)?.Label ?? "";
        var kind = piece.Def.IsDoubleSlip ? Lang.T("Kreuzungsweiche") : Lang.T("Weiche");
        return Lang.F($"{kind} Nr. {piece.Id} in {Fmt.Num(Math.Max(0, next.Distance - s) / 10, 0)} cm: {label}");
    }

    /// <summary>Fahrweg ab <paramref name="startPiece"/>. <paramref name="routes"/>: Stellung je Weiche (0 = Grundstellung, 1 = abzweigend …).</summary>
    public static RidePath Build(LayoutPlan plan, int? startPiece, IReadOnlyDictionary<int, int>? routes = null)
    {
        var path = new RidePath(plan, Connectivity.Analyze(plan), plan.Pieces.Sum(p => p.Def.TrackLength));
        if (plan.Pieces.Count == 0) return path;
        var piece = (startPiece is { } id ? plan.Find(id) : null) ?? plan.Pieces.FirstOrDefault(p => p.Def.Kind == PieceKind.Straight) ?? plan.Pieces[0];
        path.Walk(piece, 0, routes, true);
        return path;
    }

    /// <summary>
    /// Neuer Fahrweg, der bis zur Weiche <paramref name="stop"/> derselbe bleibt und dahinter nach den Stellungen <paramref name="routes"/> weiterführt.
    /// Positionen vor der Weiche gelten unverändert weiter, der Zug springt also nicht.
    /// </summary>
    public RidePath Reroute(RideStop stop, IReadOnlyDictionary<int, int>? routes)
    {
        var r = new RidePath(_plan, _conn, _trackTotal);
        if (_plan.Find(stop.PieceId) is not { } piece) return this;
        var wrapped = Closed && stop.Distance >= Length - 1e-6;
        if (wrapped)
        {
            foreach (var p in _points) r.Add(p);
            for (var i = 1; i < stop.PointIndex; i++) r.Add(_points[i]);
        }
        else
            for (var i = 0; i < stop.PointIndex && i < _points.Count; i++) r.Add(_points[i]);
        r.Walk(piece, stop.Entry, routes, false);
        return r;
    }

    /// <summary>Stellt die nächste Weiche vor <paramref name="s"/> auf <paramref name="route"/> und bestimmt den Weg dahinter neu.</summary>
    public RidePath SetNextRoute(double s, int route, Dictionary<int, int> routes)
    {
        if (NextStop(s) is not { } next || _plan.Find(next.PieceId) is not { } piece) return this;
        var n = RouteCount(piece);
        routes[piece.Id] = ((route % n) + n) % n;
        return Reroute(next, routes);
    }

    /// <summary>Stellt die nächste Weiche vor <paramref name="s"/> in die nächste Stellung (zum Durchschalten mit einer Taste).</summary>
    public RidePath CycleNextRoute(double s, Dictionary<int, int> routes)
        => NextStop(s) is { } next && _plan.Find(next.PieceId) is { } piece ? SetNextRoute(s, RouteOf(piece, routes) + 1, routes) : this;

    /// <summary>Wählt an der nächsten Weiche die Richtung <paramref name="side"/> (−1 links, 0 geradeaus, +1 rechts), falls es sie gibt.</summary>
    public RidePath ChooseNext(double s, int side, Dictionary<int, int> routes)
    {
        if (NextStop(s) is not { } next) return this;
        var choices = Choices(next);
        var c = choices.FirstOrDefault(c => c.Side == side)
                // Keine Gerade (z. B. Bogenweiche): „geradeaus“ nimmt den flacheren Strang nicht, sondern lässt die Stellung
                ?? (side == 0 ? null : choices.FirstOrDefault(c => Math.Sign(c.Side) == side));
        return c is null ? this : SetNextRoute(s, c.Route, routes);
    }

    /// <summary>Verlängert einen offenen Fahrweg, wenn der Zug bei <paramref name="s"/> sich dem Ende nähert.</summary>
    public void EnsureAhead(double s, IReadOnlyDictionary<int, int>? routes)
    {
        if (Closed || _resume is not { } at || _plan.Find(at.Piece) is not { } piece) return;
        if (Length - s > _trackTotal + 2000) return;
        Walk(piece, at.Entry, routes, false);
    }

    private void Add((double X, double Y, double Z) p)
    {
        if (_points.Count > 0)
        {
            var a = _points[^1];
            if (Math.Abs(a.X - p.X) + Math.Abs(a.Y - p.Y) <= 0.01) return;
            _dist.Add(_dist[^1] + Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y) + (p.Z - a.Z) * (p.Z - a.Z)));
        }
        else _dist.Add(0);
        _points.Add(p);
    }

    private void Walk(PlacedPiece piece, int entry, IReadOnlyDictionary<int, int>? routes, bool detectClosed)
    {
        var plan = _plan;
        var conn = _conn;
        var limit = _trackTotal * 3 + 1000;
        var guard = 20000 + plan.Pieces.Count * 20;
        double length = 0;
        var visits = new HashSet<(int, int)>();
        var step = plan.Gauge.TrackWidth;
        var startState = (piece.Id, entry);
        while (length < limit && guard-- > 0)
        {
            if (detectClosed && !visits.Add((piece.Id, entry)) && (piece.Id, entry) == startState)
            {
                Closed = true;
                _resume = null;
                return;
            }
            var d = piece.Def;
            var count = TrackGeometry.LocalConnectors(d, piece.Flipped).Count;
            if (d.IsTable)
            {
                // Drehscheibe, Schiebebühne: hinein bis zur Bühne, zum gegenüberliegenden (sonst zum nächsten) belegten Anschluss und hinaus
                var ports = piece.WorldConnectors();
                var linked = Enumerable.Range(0, count).Where(k => k != entry && conn.Find(piece.Id, k)?.Link is not null).ToList();
                var exitPort = linked.OrderBy(k => Math.Abs(Math.Abs(Angles.Normalize(ports[k].Heading - ports[entry].Heading)) - 180)).Cast<int?>().FirstOrDefault() ?? entry;
                var stubs = piece.WorldLegs();
                foreach (var (leg, inward) in new[] { (stubs[entry], true), (stubs[exitPort], false) })
                    for (var i = 0; i <= 4; i++)
                    {
                        var q = leg.At(inward ? i / 4.0 : 1 - i / 4.0);
                        Add((q.X, q.Y, piece.Height));
                    }
                length += stubs[entry].TrackLength * 2 + piece.Def.PitRadius * 2 + 1;
                if (conn.Find(piece.Id, exitPort)?.Link is { } after) { piece = plan.Find(after.PieceId)!; entry = after.Index; }
                continue;
            }
            var slip = d.IsDoubleSlip && count == 4;
            var facing = (d.Kind == PieceKind.Switch && entry == 0 && count > 2) || slip;
            if (facing)
            {
                var stop = new RideStop(Length, piece.Id, entry, _points.Count);
                if (_stops.Count == 0 || _stops[^1].PieceId != stop.PieceId || Math.Abs(_stops[^1].Distance - stop.Distance) > 1e-6) _stops.Add(stop);
            }
            var route = facing ? RouteOf(piece, routes) : 0;
            var exit = ExitOf(piece, entry, route, count);
            if (slip && route == 1)
            {
                // Abzweigend durch die Kreuzungsweiche: weicher Bogen vom Einfahrts- zum Ausfahrtsanschluss
                var ports = piece.WorldConnectors();
                var a = ports[entry];
                var b = ports[exit];
                var l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double ha = a.HeadingRad + Math.PI, hb = b.HeadingRad;
                var n = Math.Max(4, (int)Math.Ceiling(l / step));
                for (var i = 0; i <= n; i++)
                {
                    var t = (double)i / n;
                    double h00 = 2 * t * t * t - 3 * t * t + 1, h10 = t * t * t - 2 * t * t + t, h01 = -2 * t * t * t + 3 * t * t, h11 = t * t * t - t * t;
                    Add((h00 * a.X + h10 * l * Math.Cos(ha) + h01 * b.X + h11 * l * Math.Cos(hb),
                         h00 * a.Y + h10 * l * Math.Sin(ha) + h01 * b.Y + h11 * l * Math.Sin(hb), piece.Height));
                }
                length += l;
            }
            else
            {
                var (way, forward) = d.Kind switch
                {
                    PieceKind.Switch => entry == 0 ? (exit - 1, true) : (entry - 1, false),
                    PieceKind.Crossing => (entry / 2, entry % 2 == 0),
                    PieceKind.Buffer => (0, true),
                    _ => (0, entry == 0),
                };
                var all = piece.WorldLegs();
                var paths = piece.WorldPaths();
                var first = paths.Take(way).Sum(p => p.Count);
                var order = Enumerable.Range(first, paths[way].Count);
                foreach (var leg in forward ? order : order.Reverse())
                {
                    var l = all[leg];
                    var n = Math.Max(2, (int)Math.Ceiling(l.TrackLength / step));
                    for (var i = 0; i <= n; i++)
                    {
                        var t = forward ? (double)i / n : 1 - (double)i / n;
                        var q = l.At(t);
                        Add((q.X, q.Y, piece.HeightAt(leg, t)));
                    }
                    if (d.Kind == PieceKind.Buffer)
                    {
                        // Am Prellbock zurück
                        for (var i = n; i >= 0; i--)
                        {
                            var q = l.At((double)i / n);
                            Add((q.X, q.Y, piece.HeightAt(leg, (double)i / n)));
                        }
                    }
                    length += l.TrackLength;
                }
            }
            if (conn.Find(piece.Id, exit)?.Link is { } next)
            {
                piece = plan.Find(next.PieceId)!;
                entry = next.Index;
            }
            else
            {
                // Offenes Ende: auf demselben Gleis zurückfahren
                entry = exit;
            }
        }
        _resume = (piece.Id, entry);
    }
}

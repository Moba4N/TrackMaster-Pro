namespace TrackMaster.Core;

/// <summary>Ergebnis beim Einfügen eines Modells: das neue Objekt und ein Hinweis für die Statuszeile.</summary>
public sealed record ModelPlacementResult(SceneryItem Item, string Message, bool Snapped);

/// <summary>
/// Setzt ein Ausstattungsmodell in den Plan: in Spur-Maßstab umgerechnet und – je nach <see cref="ModelSnap"/> –
/// am nächsten Gleis eingerastet (Tunnelportal am Tunnelanfang, Bahnsteig im Kantenabstand entlang des Gleises, Brücke darunter …).
/// </summary>
public static class ModelPlacement
{
    /// <summary>Abstand Gleismitte – Bahnsteigkante im Vorbild (m).</summary>
    public const double PlatformEdge = 1.65;

    /// <summary>Neues Objekt in Standardgröße an (x, y), ohne Gleisbezug.</summary>
    public static SceneryItem Create(LayoutPlan plan, ModelDefinition def, double x, double y)
    {
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var (w, d, h) = def.Size;
        return new SceneryItem
        {
            Kind = SceneryKind.Building, Model = def.Id, Name = def.Name, X = Math.Round(x), Y = Math.Round(y),
            Width = Round(w * k), Depth = Round(d * k), Height = Round(h * k), Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1),
        };
    }

    private static double Round(double v) => Math.Max(0.5, Math.Round(v, v < 20 ? 1 : 0));

    /// <summary>Setzt das Modell an (x, y) und rastet es, wenn möglich, im Umkreis <paramref name="tolerance"/> (mm) am Gleis ein.</summary>
    public static ModelPlacementResult Place(LayoutPlan plan, ModelDefinition def, double x, double y, double tolerance)
    {
        var item = Create(plan, def, x, y);
        var g = plan.Gauge.TrackWidth;
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var pieces = plan.Pieces.Where(p => p.Level == item.Level || plan.Levels.Count == 1).ToList();
        switch (def.Snap)
        {
            case ModelSnap.Tunnelportal:
            {
                if (Portal(plan, pieces, x, y, tolerance + item.Depth) is not { } portal) break;
                var (c, portalPiece) = portal;
                Link(item, portalPiece);
                var dir = c.Heading * Math.PI / 180;
                // Die Außenseite der Portalmauer steht am Tunnelanfang, das Modell reicht in den Tunnel hinein (+x)
                var bounds = def.Bounds();
                var wall = def.Bodies.FirstOrDefault(o => o.Shape == BodyShape.Bogenwand);
                var face = wall is null ? bounds.X0 : ModelMesh.Footprint(wall).Min(q => q.X);
                var shift = ((bounds.X0 + bounds.X1) / 2 - face) * item.Width / (bounds.X1 - bounds.X0);
                item.X = Math.Round(c.X + Math.Cos(dir) * shift, 2);
                item.Y = Math.Round(c.Y + Math.Sin(dir) * shift, 2);
                item.Rotation = Math.Round(Angles.Normalize(c.Heading), 2);
                return new(item, Lang.F($"{def.Name} am Tunnelanfang eingerastet."), true);
            }
            case ModelSnap.Gleisende:
            {
                var conn = Connectivity.Analyze(plan);
                var open = conn.Open.Where(o => pieces.Any(p => p.Id == o.PieceId))
                    .Select(o => (o.Pose, o.PieceId, D: Math.Sqrt((o.Pose.X - x) * (o.Pose.X - x) + (o.Pose.Y - y) * (o.Pose.Y - y))))
                    .Where(o => o.D <= tolerance + item.Width).OrderBy(o => o.D).ToList();
                if (open.Count == 0) break;
                var pose = open[0].Pose;
                Link(item, plan.Find(open[0].PieceId));
                var dir = pose.Heading * Math.PI / 180;
                item.X = Math.Round(pose.X + Math.Cos(dir) * item.Width / 2, 2);
                item.Y = Math.Round(pose.Y + Math.Sin(dir) * item.Width / 2, 2);
                item.Rotation = Math.Round(Angles.Normalize(pose.Heading), 2);
                return new(item, Lang.F($"{def.Name} am Gleisende eingerastet."), true);
            }
            case ModelSnap.Neben:
            {
                if (Nearest(pieces, x, y, tolerance + item.Depth + PlatformEdge * k) is not { } hit) break;
                var edge = Math.Max(PlatformEdge * k, g * 1.15);
                // Seite: auf der Seite des Klicks
                var (nx, ny) = (-Math.Sin(hit.Pose.HeadingRad), Math.Cos(hit.Pose.HeadingRad));
                var side = (x - hit.Pose.X) * nx + (y - hit.Pose.Y) * ny >= 0 ? 1 : -1;
                var offset = side * (edge + item.Depth / 2);
                var line = Follow(pieces, hit.Pose, item.Width, g);
                var path = Offset(line, offset);
                // Modell-y (von der Gleisseite weg) muss zur Klickseite zeigen: sonst Richtung umkehren
                if (side < 0) path.Reverse();
                SetPath(item, path);
                LinkAlong(plan, def, item, pieces, line);
                return new(item, Lang.F($"{def.Name} neben dem Gleis eingerastet ({Fmt.Num(edge, 1)} mm Kantenabstand).") + HeightNote(plan, def, item), true);
            }
            case ModelSnap.Unter:
            case ModelSnap.Auf:
            case ModelSnap.Quer:
            {
                if (Nearest(pieces, x, y, tolerance + Math.Max(item.Depth, g * 4)) is not { } hit) break;
                item.X = Math.Round(hit.Pose.X, 2); item.Y = Math.Round(hit.Pose.Y, 2);
                item.Rotation = Math.Round(Angles.Normalize(hit.Pose.Heading), 2);
                Link(item, hit.Piece);
                if (def.Snap != ModelSnap.Quer)
                {
                    // längs zum Gleis: im Bogen und an Steigungen mitgehen
                    var line = Follow(pieces, hit.Pose, item.Width, g);
                    SetPath(item, line.Select(p => (p.X, p.Y)).ToList());
                    LinkAlong(plan, def, item, pieces, line);
                }
                if (def.Snap == ModelSnap.Unter)
                {
                    var top = MaxRise(plan, def, item);
                    if (top > g * 1.5) item.Height = Math.Round(top, 1);
                    return new(item, top > g * 1.5 ? Lang.F($"{def.Name} unter dem Gleis eingerastet (Höhe {Fmt.Num(item.Height, 0)} mm).")
                        : Lang.F($"{def.Name} unter dem Gleis eingerastet. Das Gleis liegt flach – für die Brücke Gleis höher legen."), true);
                }
                return new(item, Lang.F($"{def.Name} am Gleis eingerastet.") + HeightNote(plan, def, item), true);
            }
        }
        return new(item, def.Snap == ModelSnap.Frei ? Lang.F($"{def.Name} gesetzt. Größe, Farbe und Drehung stehen in den Eigenschaften.")
            : Lang.F($"{def.Name} gesetzt – kein passendes Gleis in der Nähe zum Einrasten."), false);
    }

    /// <summary>Modelle, die sich an gewählte Gleise setzen lassen (Brücke, Stützmauer, Bahnsteig, Halle).</summary>
    public static bool FitsSelection(ModelDefinition def) => def.Snap is ModelSnap.Neben or ModelSnap.Unter or ModelSnap.Auf;

    /// <summary>
    /// Setzt das Modell entlang der gewählten Gleise: so lang wie die Gleiskette, auch durch Bögen und Flexgleise.
    /// Brücken und Hallen liegen unter bzw. über dem Gleis, Stützmauern und Bahnsteige rechts daneben (in Richtung der Kette);
    /// <see cref="FlipSide"/> setzt sie auf die andere Seite. Null, wenn das Modell nicht passt oder kein Gleis gewählt ist.
    /// </summary>
    public static ModelPlacementResult? AlongTracks(LayoutPlan plan, ModelDefinition def, IReadOnlySet<int> ids)
    {
        if (!FitsSelection(def)) return null;
        var chain = TrackChain.Order(plan, Connectivity.Analyze(plan), ids);
        if (chain.Count == 0) return null;
        var g = plan.Gauge.TrackWidth;
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var total = chain.Sum(c => c.Length);
        if (total < 1) return null;
        var step = Math.Clamp(total / 240, 0.5, Math.Max(g, 1));
        var line = new List<Pose>();
        foreach (var c in chain)
            foreach (var l in c.Legs)
            {
                var n = Math.Max(1, (int)Math.Ceiling(l.TrackLength / step));
                for (var i = line.Count == 0 ? 0 : 1; i <= n; i++) line.Add(l.At((double)i / n));
            }
        var mid = line[line.Count / 2];
        var item = Create(plan, def, mid.X, mid.Y);
        item.Level = chain[0].Piece.Level;
        item.Tracks = chain.Select(c => c.Piece.Id).Distinct().ToList();
        string message;
        List<(double X, double Y)> path;
        if (def.Snap == ModelSnap.Neben)
        {
            var edge = Math.Max(PlatformEdge * k, g * 1.15);
            path = Offset(line, edge + item.Depth / 2);
            message = Lang.F($"{def.Name} neben {chain.Count} Gleis(en) gesetzt ({Fmt.Num(PlacedModel.PathLength(path), 0)} mm). Andere Seite: „Seite wechseln“ in den Eigenschaften.");
        }
        else
        {
            path = line.Select(p => (p.X, p.Y)).ToList();
            message = Lang.F($"{def.Name} entlang {chain.Count} Gleis(en) gesetzt ({Fmt.Num(PlacedModel.PathLength(path), 0)} mm).");
        }
        SetPath(item, path);
        if (def.Snap == ModelSnap.Unter)
        {
            var top = MaxRise(plan, def, item);
            if (top > g * 1.5) item.Height = Math.Round(top, 1);
            else message += " " + Lang.T("Das Gleis liegt flach – für die Brücke Gleis höher legen.");
        }
        else message += HeightNote(plan, def, item);
        return new(item, message, true);
    }

    /// <summary>Stützmauer oder Bahnsteig auf die andere Seite des Gleises setzen.</summary>
    public static bool FlipSide(LayoutPlan plan, ModelDefinition def, SceneryItem item)
    {
        if (def.Snap != ModelSnap.Neben) return false;
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var o = 2 * (Math.Max(PlatformEdge * k, plan.Gauge.TrackWidth * 1.15) + item.Depth / 2);
        // Das Gleis liegt links der Modelllinie: um 2 × Abstand nach links versetzen und die Richtung umkehren
        if (item.Points.Count >= 2)
        {
            var pts = item.Points;
            var poses = pts.Select((p, i) =>
            {
                var a = pts[Math.Max(0, i - 1)]; var b = pts[Math.Min(pts.Count - 1, i + 1)];
                return new Pose(p.X, p.Y, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            }).ToList();
            var path = Offset(poses, -o);
            path.Reverse();
            SetPath(item, path, keepPoints: true);
        }
        else
        {
            var h = item.Rotation * Math.PI / 180;
            item.X = Math.Round(item.X + Math.Sin(h) * o, 2);
            item.Y = Math.Round(item.Y - Math.Cos(h) * o, 2);
            item.Rotation = Math.Round(Angles.Normalize(item.Rotation + 180), 2);
        }
        return true;
    }

    /// <summary>Objekt auf eine Linie legen: Mitte, Richtung und Länge, bei Bögen mit Stützpunkten.</summary>
    private static void SetPath(SceneryItem item, List<(double X, double Y)> path, bool keepPoints = false)
    {
        var length = PlacedModel.PathLength(path);
        var mid = PointAt(path, length / 2);
        item.X = Math.Round(mid.X, 2); item.Y = Math.Round(mid.Y, 2);
        item.Rotation = Math.Round(Angles.Normalize(mid.Heading), 2);
        item.Width = Math.Round(length, 1);
        item.Points = keepPoints || Curved(path) ? path.Select(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2))).ToList() : [];
    }

    // ---------- Höhe und Ebene nach den Gleisen ----------

    /// <summary>Wie das Modell der Gleishöhe folgt, je nach Einrastart.</summary>
    public static TrackHeightMode ModeOf(ModelDefinition def) => def.Snap switch
    {
        ModelSnap.Neben or ModelSnap.Tunnelportal => TrackHeightMode.Stretch,
        ModelSnap.Auf or ModelSnap.Quer or ModelSnap.Gleisende => TrackHeightMode.Lift,
        ModelSnap.Unter => TrackHeightMode.Top,
        _ => TrackHeightMode.None,
    };

    /// <summary>Boden unter dem Modell wie in der 3D-Ansicht: tiefster Punkt des Umrisses, mindestens die Höhe seiner Ebene.</summary>
    public static double BaseGround(LayoutPlan plan, SceneryItem item)
        => Math.Max(item.Outline().Min(c => TerrainField.GroundAt(plan, c.X, c.Y)), item.Level > 0 ? plan.Level(item.Level).Height : double.MinValue);

    /// <summary>
    /// Gleishöhe über dem Boden des Modells, aus den verknüpften Gleisen (<see cref="SceneryItem.Tracks"/>) neu bestimmt:
    /// je Stützpunkt der Modelllinie oder ein Wert für das ganze Modell. Null, wenn das Modell keinen Gleisbezug hat
    /// oder das Gleis auf dem Boden liegt – dann steht es wie ein freies Modell auf Gelände, Modul und Ebene.
    /// </summary>
    public static double[]? TrackRise(LayoutPlan plan, SceneryItem item, ModelDefinition def)
    {
        if (item.Tracks.Count == 0 || ModeOf(def) == TrackHeightMode.None) return null;
        var pieces = item.Tracks.Select(plan.Find).OfType<PlacedPiece>().ToList();
        if (pieces.Count == 0) return null;
        var g = plan.Gauge.TrackWidth;
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var tol = item.Depth + 3 * g + PlatformEdge * k + (def.Snap == ModelSnap.Gleisende ? item.Width : 0);
        IReadOnlyList<(double X, double Y)> pts;
        if (item.Points.Count >= 2) pts = item.Points;
        else if (def.Snap is ModelSnap.Neben or ModelSnap.Unter or ModelSnap.Auf)
        {
            // gerades Modell längs zum Gleis: Gleishöhe an mehreren Stellen entlang, damit es auch Steigungen folgt
            var placed = new PlacedModel(def, item, k);
            var b = def.Bounds();
            var (y, n) = ((b.Y0 + b.Y1) / 2, 9);
            pts = Enumerable.Range(0, n).Select(i => placed.Map(b.X0 + (b.X1 - b.X0) * i / (n - 1), y)).ToList();
        }
        else pts = [item.Center()];
        var ground = BaseGround(plan, item);
        var rise = new double?[pts.Count];
        for (var i = 0; i < pts.Count; i++)
            if (Nearest(pieces, pts[i].X, pts[i].Y, tol) is { } hit) rise[i] = Math.Max(0, hit.Z - ground);
        if (rise.All(r => r is null)) return null;
        // Lücken (z. B. über das Gleisende hinaus) mit dem nächsten bekannten Wert füllen
        var result = new double[pts.Count];
        var last = rise.First(r => r is not null)!.Value;
        for (var i = 0; i < pts.Count; i++) { last = rise[i] ?? last; result[i] = Math.Round(last, 1); }
        return result.Max() < 0.5 ? null : result;
    }

    /// <summary>Höchste Gleishöhe über dem Boden des Modells (0 ohne Gleisbezug).</summary>
    public static double MaxRise(LayoutPlan plan, ModelDefinition def, SceneryItem item)
        => TrackRise(plan, item, def) is { } r ? r.Max() : 0;

    /// <summary>Ebene der Modelle nach ihren Gleisen ausrichten und Verweise auf gelöschte Gleise entfernen.</summary>
    public static void SyncLevels(LayoutPlan plan)
    {
        Dictionary<int, PlacedPiece>? index = null;
        foreach (var s in plan.Scenery)
        {
            if (s.Tracks.Count == 0) continue;
            index ??= plan.PieceIndex();
            s.Tracks.RemoveAll(id => !index.ContainsKey(id));
            if (s.Tracks.Count > 0) s.Level = Math.Clamp(index[s.Tracks[0]].Level, 0, plan.Levels.Count - 1);
        }
    }

    /// <summary>Für die Eigenschaften: Beschriftung des Höhenfelds und die aktuelle Gleishöhe (null ohne Gleisbezug).</summary>
    public static (string HeightLabel, string? TrackHeight) HeightInfo(LayoutPlan plan, ModelDefinition def, SceneryItem item)
    {
        if (TrackRise(plan, item, def) is not { } r) return (Lang.T("Höhe (mm)"), null);
        var (lo, hi) = (r.Min(), r.Max());
        var text = hi - lo < 0.5 ? $"{Fmt.Num(hi, 0)} mm" : $"{Fmt.Num(lo, 0)}–{Fmt.Num(hi, 0)} mm";
        return (ModeOf(def) == TrackHeightMode.Stretch ? Lang.T("Höhe über Gleis (mm)") : Lang.T("Höhe (mm)"), text);
    }

    private static void Link(SceneryItem item, PlacedPiece? piece)
    {
        if (piece is null) return;
        item.Tracks = [piece.Id];
        item.Level = piece.Level;
    }

    /// <summary>Alle Gleise entlang der Modelllinie verknüpfen (das Modell kann über mehrere Gleise reichen).</summary>
    private static void LinkAlong(LayoutPlan plan, ModelDefinition def, SceneryItem item, IReadOnlyList<PlacedPiece> pieces, List<Pose> line)
    {
        var g = plan.Gauge.TrackWidth;
        foreach (var p in line)
            if (Nearest(pieces, p.X, p.Y, g) is { } hit && !item.Tracks.Contains(hit.Piece.Id)) item.Tracks.Add(hit.Piece.Id);
        if (item.Tracks.Count > 0) item.Level = plan.Find(item.Tracks[0])!.Level;
    }

    private static string HeightNote(LayoutPlan plan, ModelDefinition def, SceneryItem item)
    {
        if (TrackRise(plan, item, def) is not { } r) return "";
        return ModeOf(def) == TrackHeightMode.Stretch
            ? " " + Lang.F($"Es folgt der Gleishöhe ({Fmt.Num(r.Min(), 0)}–{Fmt.Num(r.Max(), 0)} mm), die Höhe zählt über dem Gleis.")
            : " " + Lang.F($"Es steht auf Gleishöhe ({Fmt.Num(r.Max(), 0)} mm).");
    }

    /// <summary>Nächster Punkt auf der Gleismitte im Umkreis.</summary>
    public sealed record TrackHit(PlacedPiece Piece, Pose Pose, double Distance, double Z);

    public static TrackHit? Nearest(IEnumerable<PlacedPiece> pieces, double x, double y, double tolerance)
    {
        TrackHit? best = null;
        foreach (var p in pieces)
        {
            if (p.Def.IsTable) continue;
            var legs = p.WorldLegs();
            for (var li = 0; li < legs.Count; li++)
            {
                var (t, pose, d) = Project(legs[li], x, y);
                if (d <= tolerance && (best is null || d < best.Distance)) best = new TrackHit(p, pose, d, p.HeightAt(li, t));
            }
        }
        return best;
    }

    /// <summary>Lotfußpunkt auf einem Abschnitt: Anteil t, Lage mit Richtung und Abstand.</summary>
    public static (double T, Pose Pose, double Distance) Project(Leg l, double x, double y)
    {
        double t;
        if (!l.IsArc)
        {
            var e = l.End;
            double dx = e.X - l.X, dy = e.Y - l.Y, len2 = dx * dx + dy * dy;
            t = len2 < 1e-12 ? 0 : Math.Clamp(((x - l.X) * dx + (y - l.Y) * dy) / len2, 0, 1);
        }
        else
        {
            var (cx, cy) = l.Center;
            var phi = Math.Atan2(y - cy, x - cx) * 180 / Math.PI;
            var along = Angles.Normalize((phi + l.Turn * 90 - l.Heading) * l.Turn);
            // außerhalb des Bogens: zum näheren Ende
            if (along < 0 || along > l.Angle)
                t = Math.Abs(Angles.Normalize(along - l.Angle)) < Math.Abs(along) ? 1 : 0;
            else t = l.Angle < 1e-9 ? 0 : along / l.Angle;
        }
        var p = l.At(t);
        return (t, p, Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)));
    }

    /// <summary>
    /// Gleismitte um einen Punkt herum über <paramref name="length"/> (je die Hälfte vor und zurück), auch über mehrere Gleise
    /// und Bögen hinweg; endet das Gleis, wird gerade weitergeführt.
    /// </summary>
    public static List<Pose> Follow(IReadOnlyList<PlacedPiece> pieces, Pose start, double length, double gauge)
    {
        var step = Math.Clamp(length / 60, 0.5, Math.Max(gauge * 2, 1));
        List<Pose> Walk(double heading)
        {
            var list = new List<Pose>();
            var cur = start with { Heading = heading };
            var n = (int)Math.Ceiling(length / 2 / step);
            for (var i = 0; i < n; i++)
            {
                var h = cur.HeadingRad;
                var next = new Pose(cur.X + Math.Cos(h) * step, cur.Y + Math.Sin(h) * step, cur.Heading);
                // nur vorwärts dem Gleis folgen; am Gleisende geht es gerade weiter
                if (Nearest(pieces, next.X, next.Y, step * 1.5) is { } hit
                    && (hit.Pose.X - cur.X) * Math.Cos(h) + (hit.Pose.Y - cur.Y) * Math.Sin(h) > step * 0.5)
                {
                    var hh = hit.Pose.Heading;
                    if (Math.Abs(Angles.Normalize(hh - cur.Heading)) > 90) hh += 180;
                    next = new Pose(hit.Pose.X, hit.Pose.Y, hh);
                }
                list.Add(next);
                cur = next;
            }
            return list;
        }
        var back = Walk(start.Heading + 180);
        back.Reverse();
        var result = back.Select(p => p with { Heading = p.Heading + 180 }).ToList();
        result.Add(start);
        result.AddRange(Walk(start.Heading));
        return result;
    }

    /// <summary>Linie seitlich versetzt (positiv = rechts in Fahrtrichtung bei y nach unten).</summary>
    private static List<(double X, double Y)> Offset(List<Pose> line, double offset)
        => line.Select(p => (p.X - Math.Sin(p.HeadingRad) * offset, p.Y + Math.Cos(p.HeadingRad) * offset)).ToList();

    private static Pose PointAt(List<(double X, double Y)> path, double s)
    {
        double acc = 0;
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (acc + l >= s || i == path.Count - 1)
            {
                var t = l < 1e-9 ? 0 : Math.Clamp((s - acc) / l, 0, 1);
                return new Pose(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            }
            acc += l;
        }
        return new Pose(path[0].X, path[0].Y, 0);
    }

    private static bool Curved(List<(double X, double Y)> path)
    {
        if (path.Count < 3) return false;
        var h0 = Math.Atan2(path[1].Y - path[0].Y, path[1].X - path[0].X) * 180 / Math.PI;
        for (var i = 2; i < path.Count; i++)
        {
            var h = Math.Atan2(path[i].Y - path[i - 1].Y, path[i].X - path[i - 1].X) * 180 / Math.PI;
            if (Math.Abs(Angles.Normalize(h - h0)) > 0.5) return true;
        }
        return false;
    }

    /// <summary>
    /// Tunnelanfang nahe (x, y): Anschluss zwischen Tunnelgleis und offenem Gleis (oder offenes Ende eines Tunnelgleises).
    /// Ohne Tunnel: nächstes Gleisende. Die Richtung zeigt in den Tunnel hinein.
    /// </summary>
    private static (Pose Pose, PlacedPiece Piece)? Portal(LayoutPlan plan, List<PlacedPiece> pieces, double x, double y, double tolerance)
    {
        PlacedPiece? bestPiece = null;
        var conn = Connectivity.Analyze(plan);
        var ids = pieces.Select(p => p.Id).ToHashSet();
        Pose? best = null;
        var bestD = double.MaxValue;
        var bestScore = int.MaxValue;
        foreach (var c in conn.All.Where(c => ids.Contains(c.PieceId)))
        {
            var d = Math.Sqrt((c.Pose.X - x) * (c.Pose.X - x) + (c.Pose.Y - y) * (c.Pose.Y - y));
            if (d > tolerance) continue;
            var self = plan.Find(c.PieceId)!;
            var other = c.Link is { } l ? plan.Find(l.PieceId) : null;
            int score;
            Pose pose;
            if (self.Tunnel && other is not { Tunnel: true }) { score = 0; pose = c.Pose with { Heading = c.Pose.Heading + 180 }; }
            else if (!self.Tunnel && other is { Tunnel: true }) { score = 0; pose = c.Pose; }
            else if (c.IsOpen) { score = 1; pose = c.Pose; }
            else continue;
            if (score < bestScore || (score == bestScore && d < bestD)) { best = pose; bestD = d; bestScore = score; bestPiece = self; }
        }
        return best is { } b ? (b, bestPiece!) : null;
    }
}

namespace TrackMaster.Core;

/// <summary>Ein verlegtes Gleisstück. (X, Y, Rotation) ist die Lage von Anschluss 0.</summary>
public sealed class PlacedPiece
{
    public int Id { get; set; }
    public required string Key { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public bool Flipped { get; set; }
    /// <summary>Höhe der Schienenoberkante an Anschluss 0 über der Anlagenplatte in mm.</summary>
    public double Height { get; set; }
    /// <summary>Steigung als Höhenänderung je mm Gleis, gemessen von Anschluss 0 weg (0,02 = 2 %). Kreuzungen liegen immer eben.</summary>
    public double Grade { get; set; }
    /// <summary>Gleis liegt im Tunnel.</summary>
    public bool Tunnel { get; set; }
    /// <summary>Digitaladresse (Weichen und Kreuzungsweichen).</summary>
    public int? Address { get; set; }

    public TrackPieceDef Def => Catalog.Get(Key);

    /// <summary>Steigung, die tatsächlich gilt (Kreuzungen und Prellböcke eben).</summary>
    public double EffectiveGrade => Def.Kind is PieceKind.Crossing or PieceKind.Buffer ? 0 : Grade;

    public IReadOnlyList<Leg> WorldLegs() => TrackGeometry.LocalLegs(Def, Flipped).Select(l => l.Transform(X, Y, Rotation)).ToList();

    /// <summary>Fahrwege in Weltkoordinaten (siehe <see cref="TrackGeometry.LocalPaths"/>).</summary>
    public IReadOnlyList<IReadOnlyList<Leg>> WorldPaths()
        => TrackGeometry.LocalPaths(Def, Flipped).Select(p => (IReadOnlyList<Leg>)p.Select(l => l.Transform(X, Y, Rotation)).ToList()).ToList();

    public IReadOnlyList<Pose> WorldConnectors() => TrackGeometry.LocalConnectors(Def, Flipped).Select(ToWorld).ToList();

    /// <summary>Höhe der Schienenoberkante an jedem Anschluss.</summary>
    public IReadOnlyList<double> ConnectorHeights()
    {
        var d = Def;
        var g = EffectiveGrade;
        var paths = TrackGeometry.LocalPaths(d, Flipped);
        return d.Kind switch
        {
            PieceKind.Straight or PieceKind.Curve => [Height, Height + g * paths[0][0].TrackLength],
            PieceKind.Switch => [Height, .. paths.Select(p => Height + g * p.Sum(l => l.TrackLength))],
            PieceKind.Crossing => [Height, Height, Height, Height],
            _ => [Height],
        };
    }

    /// <summary>Höhe auf Abschnitt <paramref name="leg"/> bei Anteil t.</summary>
    public double HeightAt(int leg, double t)
    {
        var legs = TrackGeometry.LocalLegs(Def, Flipped);
        return Height + EffectiveGrade * (legs[leg].From + legs[leg].TrackLength * t);
    }

    public Pose ToWorld(Pose local)
    {
        var r = Rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        return new Pose(X + local.X * c - local.Y * s, Y + local.X * s + local.Y * c, local.Heading + Rotation);
    }

    /// <summary>Mittelpunkt des ersten Abschnitts, für Auswahlrahmen, Beschriftung und Drehen.</summary>
    public Pose Middle() => WorldLegs()[0].At(0.5);

    public PlacedPiece Clone() => new()
    {
        Id = Id, Key = Key, X = X, Y = Y, Rotation = Rotation, Flipped = Flipped, Height = Height, Grade = Grade, Tunnel = Tunnel, Address = Address,
    };
}

public enum SceneryKind { Terrain, Water, Building, Tree }

/// <summary>
/// Gelände und Ausstattung. Höhenflächen und Gewässer sind Vielecke (<see cref="Points"/>),
/// Gebäude Rechtecke um (X, Y) mit Breite, Tiefe und Drehung, Bäume Punkte mit Durchmesser (Width).
/// </summary>
public sealed class SceneryItem
{
    public int Id { get; set; }
    public SceneryKind Kind { get; set; }
    public string Name { get; set; } = "";
    public List<(double X, double Y)> Points { get; set; } = [];
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Rotation { get; set; }
    /// <summary>Höhe über der Platte in mm (Höhenfläche, Gebäude, Baum); bei Gewässern die Tiefe.</summary>
    public double Height { get; set; }

    public bool IsArea => Kind is SceneryKind.Terrain or SceneryKind.Water;

    /// <summary>Eckpunkte in Weltkoordinaten (Vieleck bzw. gedrehtes Rechteck; Bäume als Achteck).</summary>
    public IReadOnlyList<(double X, double Y)> Outline()
    {
        switch (Kind)
        {
            case SceneryKind.Terrain:
            case SceneryKind.Water:
                return Points;
            case SceneryKind.Building:
                var r = Rotation * Math.PI / 180;
                double c = Math.Cos(r), s = Math.Sin(r), hw = Width / 2, hd = Depth / 2;
                return new[] { (-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd) }
                    .Select(p => (X + p.Item1 * c - p.Item2 * s, Y + p.Item1 * s + p.Item2 * c)).ToList();
            default:
                return Enumerable.Range(0, 8).Select(i => (X + Width / 2 * Math.Cos(i * Math.PI / 4), Y + Width / 2 * Math.Sin(i * Math.PI / 4))).ToList();
        }
    }

    public (double X, double Y) Center()
    {
        if (!IsArea || Points.Count == 0) return (X, Y);
        return (Points.Average(p => p.X), Points.Average(p => p.Y));
    }

    public bool Contains(double px, double py) => Geometry2D.PointInPolygon(Outline(), px, py);

    public void MoveBy(double dx, double dy)
    {
        X += dx; Y += dy;
        Points = Points.Select(p => (p.X + dx, p.Y + dy)).ToList();
    }

    public SceneryItem Clone() => new()
    {
        Id = Id, Kind = Kind, Name = Name, Points = [.. Points], X = X, Y = Y, Width = Width, Depth = Depth, Rotation = Rotation, Height = Height,
    };

    public static string KindName(SceneryKind k) => k switch
    {
        SceneryKind.Terrain => "Höhenfläche",
        SceneryKind.Water => "Gewässer",
        SceneryKind.Building => "Gebäude",
        _ => "Baum",
    };
}

/// <summary>Einspeisung: hier wird ein Gleis an einen Stromkreis (Booster/Trafo) angeschlossen.</summary>
public sealed class Feeder
{
    public int Id { get; set; }
    public int PieceId { get; set; }
    public string Circuit { get; set; } = "Stromkreis 1";
    public Feeder Clone() => new() { Id = Id, PieceId = PieceId, Circuit = Circuit };
}

/// <summary>Trennstelle (isolierte Schienenverbinder) am Anschluss <see cref="Index"/> eines Gleises.</summary>
public sealed class Isolation
{
    public int Id { get; set; }
    public int PieceId { get; set; }
    public int Index { get; set; }
    public Isolation Clone() => new() { Id = Id, PieceId = PieceId, Index = Index };
}

public enum SignalType { Hauptsignal, Vorsignal, Ausfahrsignal, Sperrsignal, Blocksignal }

/// <summary>Signal am Anschluss <see cref="Index"/> eines Gleises, rechts neben dem Gleis, gilt für Fahrten auf dieses Gleisende zu.</summary>
public sealed class Signal
{
    public int Id { get; set; }
    public int PieceId { get; set; }
    public int Index { get; set; }
    public SignalType Type { get; set; } = SignalType.Hauptsignal;
    public string Name { get; set; } = "";
    public int? Address { get; set; }
    public Signal Clone() => new() { Id = Id, PieceId = PieceId, Index = Index, Type = Type, Name = Name, Address = Address };

    public static string TypeName(SignalType t) => t switch
    {
        SignalType.Hauptsignal => "Hauptsignal",
        SignalType.Vorsignal => "Vorsignal",
        SignalType.Ausfahrsignal => "Ausfahrsignal",
        SignalType.Sperrsignal => "Sperrsignal",
        _ => "Blocksignal",
    };

    /// <summary>Wie viele Weichendecoder-Adressen das Signal belegt (Vorsignal und Sperrsignal 1, Hauptsignale 2 für Hp0/Hp1/Hp2).</summary>
    public int AddressCount => Type is SignalType.Vorsignal or SignalType.Sperrsignal or SignalType.Blocksignal ? 1 : 2;
}

public sealed class LayoutPlan
{
    public string Name { get; set; } = "Neuer Plan";
    public string GaugeId { get; set; } = "H0";
    public double BoardWidth { get; set; } = 2000;
    public double BoardHeight { get; set; } = 1250;
    public List<PlacedPiece> Pieces { get; } = [];
    public List<SceneryItem> Scenery { get; } = [];
    public List<Feeder> Feeders { get; } = [];
    public List<Isolation> Isolations { get; } = [];
    public List<Signal> Signals { get; } = [];
    /// <summary>Nächste freie Kennung; gilt für Gleise und alle anderen Objekte gemeinsam.</summary>
    public int NextId { get; set; } = 1;

    public Gauge Gauge => Catalog.GetGauge(GaugeId);

    public static LayoutPlan Empty(string gaugeId)
    {
        var g = Catalog.GetGauge(gaugeId);
        return new LayoutPlan { GaugeId = g.Id, BoardWidth = g.DefaultBoardWidth, BoardHeight = g.DefaultBoardHeight };
    }

    public PlacedPiece? Find(int id) => Pieces.FirstOrDefault(p => p.Id == id);

    public PlacedPiece Add(string key)
    {
        var piece = new PlacedPiece { Id = NextId++, Key = key };
        Pieces.Add(piece);
        return piece;
    }

    /// <summary>Entfernt Gleise samt Einspeisungen, Trennstellen und Signalen daran.</summary>
    public int RemovePieces(IReadOnlySet<int> ids)
    {
        var n = Pieces.RemoveAll(p => ids.Contains(p.Id));
        Feeders.RemoveAll(f => ids.Contains(f.PieceId));
        Isolations.RemoveAll(i => ids.Contains(i.PieceId));
        Signals.RemoveAll(s => ids.Contains(s.PieceId));
        return n;
    }

    /// <summary>Entfernt ein Objekt (Gelände, Einspeisung, Trennstelle, Signal) über seine Kennung.</summary>
    public bool RemoveObject(int id)
        => Scenery.RemoveAll(s => s.Id == id) + Feeders.RemoveAll(f => f.Id == id) + Isolations.RemoveAll(i => i.Id == id) + Signals.RemoveAll(s => s.Id == id) > 0;

    /// <summary>Gibt es Inhalte, die nur die Pro-Version bearbeiten kann?</summary>
    public bool HasProContent
        => Scenery.Count > 0 || Feeders.Count > 0 || Isolations.Count > 0 || Signals.Count > 0
           || Pieces.Any(p => p.Tunnel || p.Address is not null || Math.Abs(p.Height) > 0.01 || Math.Abs(p.Grade) > 1e-6);

    public PlanStatistics Statistics(Connectivity? connectivity = null)
    {
        var defs = Pieces.Select(p => p.Def).ToList();
        var radii = defs.Where(d => d.Radius > 0).Select(d => d.Radius).ToList();
        var grades = Pieces.Select(p => Math.Abs(p.EffectiveGrade)).ToList();
        return new PlanStatistics(
            Pieces.Count,
            defs.Count(d => d.Kind is PieceKind.Switch or PieceKind.Crossing),
            defs.Sum(d => d.TrackLength),
            radii.Count > 0 ? radii.Min() : null,
            (connectivity ?? Connectivity.Analyze(this)).Open.Count,
            grades.Count > 0 ? grades.Max() : 0,
            Pieces.SelectMany(p => p.ConnectorHeights()).DefaultIfEmpty(0).Max());
    }

    /// <summary>Begrenzung von Anlagenplatte, Gleisen und Gelände.</summary>
    public (double X0, double Y0, double X1, double Y1) Bounds()
    {
        double x0 = 0, y0 = 0, x1 = BoardWidth, y1 = BoardHeight;
        void Include(double x, double y) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        foreach (var p in Pieces)
            foreach (var l in p.WorldLegs())
                foreach (var t in new[] { 0, 0.5, 1 })
                {
                    var q = l.At(t);
                    Include(q.X, q.Y);
                }
        foreach (var s in Scenery)
            foreach (var (x, y) in s.Outline())
                Include(x, y);
        return (x0, y0, x1, y1);
    }
}

public sealed record PlanStatistics(int Pieces, int Switches, double TrackLengthMm, double? SmallestRadius, int OpenEnds, double MaxGrade = 0, double MaxHeight = 0);

/// <summary>Hilfen für ebene Vielecke.</summary>
public static class Geometry2D
{
    public static bool PointInPolygon(IReadOnlyList<(double X, double Y)> poly, double px, double py)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Fläche mit Vorzeichen (positiv = im Uhrzeigersinn bei y nach unten).</summary>
    public static double SignedArea(IReadOnlyList<(double X, double Y)> poly)
    {
        double a = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
        return a / 2;
    }

    /// <summary>Zerlegt ein einfaches Vieleck in Dreiecke (Ohrenschneiden). Liefert Indizes.</summary>
    public static List<(int A, int B, int C)> Triangulate(IReadOnlyList<(double X, double Y)> poly)
    {
        var result = new List<(int, int, int)>();
        var n = poly.Count;
        if (n < 3) return result;
        var idx = Enumerable.Range(0, n).ToList();
        if (SignedArea(poly) < 0) idx.Reverse();
        var guard = 0;
        while (idx.Count > 3 && guard++ < n * n)
        {
            var cut = false;
            for (var i = 0; i < idx.Count; i++)
            {
                int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                var (ax, ay) = poly[a]; var (bx, by) = poly[b]; var (cx, cy) = poly[c];
                var cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (cross <= 1e-9) continue;
                var ear = true;
                foreach (var k in idx)
                {
                    if (k == a || k == b || k == c) continue;
                    if (InTriangle(poly[k], poly[a], poly[b], poly[c])) { ear = false; break; }
                }
                if (!ear) continue;
                result.Add((a, b, c));
                idx.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut) break;
        }
        if (idx.Count == 3) result.Add((idx[0], idx[1], idx[2]));
        return result;
    }

    private static bool InTriangle((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double D((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3) => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        var d1 = D(p, a, b); var d2 = D(p, b, c); var d3 = D(p, c, a);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}

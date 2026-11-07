namespace TrackMaster.Core;

/// <summary>Ein verlegtes Gleisstück. (X, Y, Rotation) ist die Lage von Anschluss 0.</summary>
public sealed class PlacedPiece
{
    public int Id { get; set; }
    public required string Key { get; set; }
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
    /// <summary>Ebene (0 … <see cref="LayoutPlan.MaxLevels"/> − 1), siehe <see cref="LayoutPlan.Levels"/>.</summary>
    public int Level { get; set; }
    /// <summary>Digitaladresse (Weichen und Kreuzungsweichen).</summary>
    public int? Address { get; set; }
    /// <summary>Gesperrt: lässt sich nicht verschieben, drehen, spiegeln oder löschen.</summary>
    public bool Locked { get; set; }
    /// <summary>Gruppe (0 = keine): ein Klick wählt alle Gleise der Gruppe.</summary>
    public int Group { get; set; }

    public TrackPieceDef Def => Catalog.Get(Key);

    /// <summary>Steigung, die tatsächlich gilt (Kreuzungen und Prellböcke eben).</summary>
    public double EffectiveGrade => Def.Kind is PieceKind.Crossing or PieceKind.Buffer || Def.IsTable ? 0 : Grade;

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
            PieceKind.Straight or PieceKind.Curve => [Height, Height + g * paths[0].Sum(l => l.TrackLength)],
            PieceKind.Switch => [Height, .. paths.Select(p => Height + g * p.Sum(l => l.TrackLength))],
            PieceKind.Crossing => [Height, Height, Height, Height],
            PieceKind.Turntable or PieceKind.TransferTable => Enumerable.Repeat(Height, paths.Count).ToList(),
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
        Id = Id, Key = Key, X = X, Y = Y, Rotation = Rotation, Flipped = Flipped, Height = Height, Grade = Grade, Tunnel = Tunnel, Address = Address, Level = Level, Locked = Locked, Group = Group,
    };
}

/// <summary>Art eines Objekts; <see cref="Beam"/> = Holzleiste des Unterbaus (z. B. Diagonale oder Strebe), frei gezogen.</summary>
public enum SceneryKind { Terrain, Water, Building, Tree, Label, Dimension, Room, Obstacle, Beam, Plate }

/// <summary>Art eines Raumelements (<see cref="SceneryKind.Obstacle"/>).</summary>
public enum ObstacleType { Tuer, Fenster, Saeule, Heizkoerper, Moebel, Sonstiges }

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
    /// <summary>
    /// Nur bei Ausstattungsmodellen am Gleis: die Gleise, an denen das Modell sitzt. Höhe und Ebene richten sich danach
    /// (siehe <see cref="ModelPlacement.TrackRise"/>), auch wenn die Gleise später angehoben werden.
    /// </summary>
    public List<int> Tracks { get; set; } = [];
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Rotation { get; set; }
    /// <summary>Höhe über der Platte in mm (Höhenfläche, Gebäude, Baum); bei Gewässern die Tiefe.</summary>
    public double Height { get; set; }
    /// <summary>Ebene, auf der das Objekt liegt.</summary>
    public int Level { get; set; }
    /// <summary>Art des Raumelements (<see cref="ObstacleType"/>), sonst 0.</summary>
    public int Variant { get; set; }
    /// <summary>Gebäude: Kennung des Ausstattungsmodells (<see cref="ModelLibrary"/>); leer = einfaches Gebäude mit Satteldach.</summary>
    public string? Model { get; set; }
    /// <summary>Gebäude: Hauptfarbe als #RRGGBB; leer = Farbe des Modells.</summary>
    public string? Color { get; set; }

    /// <summary>Ist das ein Ausstattungsmodell (kein einfaches Gebäude)?</summary>
    public bool IsModel => Kind == SceneryKind.Building && !string.IsNullOrEmpty(Model);

    public ObstacleType Obstacle => Enum.IsDefined((ObstacleType)Variant) ? (ObstacleType)Variant : ObstacleType.Sonstiges;

    /// <summary>Raum (Wände als Vieleck, <see cref="Width"/> = Wandstärke) oder Raumelement (Tür, Fenster, Säule …).</summary>
    public bool IsRoom => Kind is SceneryKind.Room or SceneryKind.Obstacle;

    public bool IsArea => Kind is SceneryKind.Terrain or SceneryKind.Water;
    /// <summary>Beschriftung oder Maßlinie: wird immer gezeichnet, auch wenn das Gelände ausgeblendet ist.</summary>
    public bool IsAnnotation => Kind is SceneryKind.Label or SceneryKind.Dimension;
    /// <summary>Holzleiste des Unterbaus: <see cref="Points"/> = Anfang und Ende der Mittellinie, <see cref="Width"/> = Breite von oben,
    /// <see cref="Depth"/> = Höhe der Latte, <see cref="Height"/> = Oberkante über der Plattenoberkante (meist negativ, unter der Platte).</summary>
    public bool IsBeam => Kind == SceneryKind.Beam;
    /// <summary>Deckplatte auf dem Unterbau: <see cref="X"/>, <see cref="Y"/> = linke obere Ecke, <see cref="Width"/> × <see cref="Depth"/>,
    /// <see cref="Height"/> = Oberkante, <see cref="Variant"/> = Stärke in mm.</summary>
    public bool IsPlate => Kind == SceneryKind.Plate;

    /// <summary>Eckpunkte in Weltkoordinaten (Vieleck bzw. gedrehtes Rechteck; Bäume als Achteck).</summary>
    public IReadOnlyList<(double X, double Y)> Outline()
    {
        switch (Kind)
        {
            case SceneryKind.Terrain:
            case SceneryKind.Water:
            case SceneryKind.Room:
                return Points;
            case SceneryKind.Label:
                return Annotation.LabelOutline(this);
            case SceneryKind.Dimension:
                return Annotation.DimensionOutline(this);
            case SceneryKind.Beam:
                return BeamLayout.Outline(this);
            case SceneryKind.Plate:
                return PlateLayout.Outline(this);
            case SceneryKind.Building when Points.Count >= 2:
                return PlacedModel.PathOutline(Points, Depth);
            case SceneryKind.Building:
            case SceneryKind.Obstacle:
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
        if (Kind is SceneryKind.Dimension or SceneryKind.Beam && Points.Count == 2) return ((Points[0].X + Points[1].X) / 2, (Points[0].Y + Points[1].Y) / 2);
        if (Kind == SceneryKind.Plate) return (X + Width / 2, Y + Depth / 2);
        if ((!IsArea && Kind != SceneryKind.Room) || Points.Count == 0) return (X, Y);
        return (Points.Average(p => p.X), Points.Average(p => p.Y));
    }

    public bool Contains(double px, double py) => Geometry2D.PointInPolygon(Outline(), px, py);

    /// <summary>Dreht das Objekt um einen Punkt (Grad im Uhrzeigersinn bei y nach unten).</summary>
    public void RotateAround(double cx, double cy, double degrees)
    {
        var r = degrees * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        (double X, double Y) Turn(double x, double y) => (cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c);
        if (Kind == SceneryKind.Plate)
        {
            // Deckplatten bleiben achsparallel: Mitte drehen, bei Vierteldrehung Breite und Tiefe tauschen
            var m = Turn(X + Width / 2, Y + Depth / 2);
            if (Math.Abs(Math.IEEERemainder(degrees, 180)) > 45) (Width, Depth) = (Depth, Width);
            (X, Y) = (m.X - Width / 2, m.Y - Depth / 2);
            return;
        }
        (X, Y) = Turn(X, Y);
        Points = Points.Select(p => Turn(p.X, p.Y)).ToList();
        if (Kind is SceneryKind.Building or SceneryKind.Label or SceneryKind.Obstacle) Rotation = Angles.Normalize(Rotation + degrees);
    }

    public void MoveBy(double dx, double dy)
    {
        X += dx; Y += dy;
        Points = Points.Select(p => (p.X + dx, p.Y + dy)).ToList();
    }

    public SceneryItem Clone() => new()
    {
        Id = Id, Kind = Kind, Name = Name, Points = [.. Points], Tracks = [.. Tracks], X = X, Y = Y, Width = Width, Depth = Depth, Rotation = Rotation, Height = Height, Level = Level, Variant = Variant, Model = Model, Color = Color,
    };

    public static string KindName(SceneryKind k) => k switch
    {
        SceneryKind.Terrain => Lang.T("Höhenfläche"),
        SceneryKind.Water => Lang.T("Gewässer"),
        SceneryKind.Building => Lang.T("Gebäude"),
        SceneryKind.Label => Lang.T("Beschriftung"),
        SceneryKind.Dimension => Lang.T("Maß"),
        SceneryKind.Room => Lang.T("Raum"),
        SceneryKind.Obstacle => Lang.T("Raumelement"),
        SceneryKind.Beam => Lang.T("Leiste"),
        SceneryKind.Plate => Lang.T("Deckplatte"),
        _ => Lang.T("Baum"),
    };

    public static string ObstacleName(ObstacleType t) => t switch
    {
        ObstacleType.Tuer => Lang.T("Tür"),
        ObstacleType.Fenster => Lang.T("Fenster"),
        ObstacleType.Saeule => Lang.T("Säule"),
        ObstacleType.Heizkoerper => Lang.T("Heizkörper"),
        ObstacleType.Moebel => Lang.T("Möbel"),
        _ => Lang.T("Sonstiges"),
    };

    /// <summary>Übliche Maße (Breite, Tiefe, Höhe in mm) eines Raumelements.</summary>
    public static (double Width, double Depth, double Height) ObstacleSize(ObstacleType t) => t switch
    {
        ObstacleType.Tuer => (900, 900, 2000),
        ObstacleType.Fenster => (1200, 200, 1400),
        ObstacleType.Saeule => (300, 300, 2500),
        ObstacleType.Heizkoerper => (1000, 120, 600),
        ObstacleType.Moebel => (1000, 450, 900),
        _ => (600, 600, 1000),
    };

    /// <summary>Darf eine Anlagenplatte darüber liegen (Fenster und Heizkörper unter der Platte sind oft in Ordnung, Türen nicht)?</summary>
    public static bool BoardMayCover(ObstacleType t) => t is ObstacleType.Fenster;
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
        SignalType.Hauptsignal => Lang.T("Hauptsignal"),
        SignalType.Vorsignal => Lang.T("Vorsignal"),
        SignalType.Ausfahrsignal => Lang.T("Ausfahrsignal"),
        SignalType.Sperrsignal => Lang.T("Sperrsignal"),
        _ => Lang.T("Blocksignal"),
    };

    /// <summary>Wie viele Weichendecoder-Adressen das Signal belegt (Vorsignal und Sperrsignal 1, Hauptsignale 2 für Hp0/Hp1/Hp2).</summary>
    public int AddressCount => Type is SignalType.Vorsignal or SignalType.Sperrsignal or SignalType.Blocksignal ? 1 : 2;
}

public enum DetectorType { Strom, Kontakt, Reed }

/// <summary>Rückmelder auf einem Gleis: meldet der Steuerung, ob das Gleis besetzt ist bzw. ein Zug vorbeifährt.</summary>
public sealed class Detector
{
    public int Id { get; set; }
    public int PieceId { get; set; }
    public DetectorType Type { get; set; } = DetectorType.Strom;
    public string Name { get; set; } = "";
    /// <summary>Kontakt bzw. Eingang am Rückmeldemodul (z. B. s88: Modul × 16 + Eingang).</summary>
    public int? Address { get; set; }
    public Detector Clone() => new() { Id = Id, PieceId = PieceId, Type = Type, Name = Name, Address = Address };

    public static string TypeName(DetectorType t) => t switch
    {
        DetectorType.Kontakt => Lang.T("Schaltgleis / Kontakt"),
        DetectorType.Reed => Lang.T("Reedkontakt"),
        _ => Lang.T("Gleisbesetztmelder"),
    };

    /// <summary>Eingänge je Rückmeldemodul (s88, RM-GB-8 o. ä. mit 16 bzw. 8 Eingängen).</summary>
    public const int InputsPerModule = 16;
}

/// <summary>Hintergrundbild (z. B. eingescannter Gleisplan oder Raumskizze), maßstäblich unter dem Plan.</summary>
public sealed class PlanBackground
{
    /// <summary>Bild als Base64 (PNG oder JPEG).</summary>
    public string Data { get; set; } = "";
    public string Mime { get; set; } = "image/png";
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    /// <summary>Linke obere Ecke in mm.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Breite in mm; die Höhe folgt aus dem Seitenverhältnis.</summary>
    public double Width { get; set; } = 1000;
    public double Opacity { get; set; } = 0.5;
    public bool Visible { get; set; } = true;

    public double Height => PixelWidth > 0 ? Width * PixelHeight / PixelWidth : Width;
    public string DataUrl => $"data:{Mime};base64,{Data}";
    /// <summary>Kurze Kennung des Bildinhalts (zum Zwischenspeichern des entpackten Bildes).</summary>
    public string Key => $"bg{Data.Length}_{(Data.Length > 64 ? Data.Substring(Data.Length / 2, 32) : Data).GetHashCode():X}";

    public PlanBackground Clone() => new()
    {
        Data = Data, Mime = Mime, PixelWidth = PixelWidth, PixelHeight = PixelHeight, X = X, Y = Y, Width = Width, Opacity = Opacity, Visible = Visible,
    };
}

/// <summary>Ebene der Anlage (z. B. Schattenbahnhof, Hauptebene, Bergstrecke) mit Grundhöhe für neue Gleise.</summary>
public sealed class PlanLevel
{
    public string Name { get; set; } = "";
    /// <summary>Grundhöhe über der Platte in mm; neue Gleise auf dieser Ebene beginnen in dieser Höhe.</summary>
    public double Height { get; set; }
    public bool Visible { get; set; } = true;

    public PlanLevel Clone() => new() { Name = Name, Height = Height, Visible = Visible };

    public static string DefaultName(int index) => Lang.F($"Ebene {index + 1}");
}

/// <summary>Zeile im Ebenen-Dialog: bisherige Ebene (−1 = neu), Name, Grundhöhe, sichtbar.</summary>
public sealed record LevelEdit(int Index, string Name, double Height, bool Visible);

public sealed class LayoutPlan
{
    /// <summary>Höchstens so viele Ebenen.</summary>
    public const int MaxLevels = 10;
    public string Name { get; set; } = Lang.T("Neuer Plan");
    public string GaugeId { get; set; } = "H0";
    /// <summary>Gleissystem, mit dem im Plan gearbeitet wurde (wird beim Öffnen in der Bibliothek gewählt).</summary>
    public string? SystemId { get; set; }
    public double BoardWidth { get; set; } = 2000;
    public double BoardHeight { get; set; } = 1250;
    /// <summary>Grundform der Platte; Breite und Tiefe sind die äußeren Maße (siehe <see cref="BoardGeometry"/>).</summary>
    public BoardShape BoardShape { get; set; } = BoardShape.Rechteck;
    /// <summary>Schenkeltiefe bei L-, U-, T-, Z-Form und Ring in mm.</summary>
    public double BoardDepth { get; set; } = 600;
    /// <summary>Ausrichtung der Form in Vierteldrehungen im Uhrzeigersinn (0…3).</summary>
    public int BoardTurn { get; set; }
    /// <summary>U-Form: Länge des linken bzw. rechten Schenkels in Grundstellung in mm (0 = volle Tiefe).</summary>
    public double BoardLegLeft { get; set; }
    public double BoardLegRight { get; set; }
    /// <summary>Eigene Maße je Schenkel (Reihenfolge siehe <see cref="BoardGeometry.LegNames"/>); leer = alle wie <see cref="BoardDepth"/>.</summary>
    public List<BoardLeg> BoardLegs { get; } = [];
    /// <summary>Module der Platte bei <see cref="BoardShape.Module"/> (Rechtecke ab 0, 0; Breite und Tiefe der Platte umfassen alle).</summary>
    public List<BoardModule> Modules { get; } = [];
    public List<PlacedPiece> Pieces { get; } = [];
    public List<SceneryItem> Scenery { get; } = [];
    public List<Feeder> Feeders { get; } = [];
    public List<Isolation> Isolations { get; } = [];
    public List<Signal> Signals { get; } = [];
    public List<Detector> Detectors { get; } = [];
    /// <summary>Im Plan mitgespeicherte eigene Ausstattungsmodelle (Kennung → Modell), damit der Plan überall gleich aussieht.</summary>
    public Dictionary<string, ModelDefinition> Models { get; } = [];
    /// <summary>Hintergrundbild; null = keins.</summary>
    public PlanBackground? Background { get; set; }
    /// <summary>Preis je Artikel (Schlüssel wie in der Stückliste) in der Währung des Anwenders.</summary>
    public Dictionary<string, double> Prices { get; } = [];
    /// <summary>Vorhandene Stückzahl je Artikel (eigener Bestand).</summary>
    public Dictionary<string, int> Stock { get; } = [];
    /// <summary>Längster Zug in mm für Nutzlängen (0 = nicht prüfen).</summary>
    public double TrainLength { get; set; }
    /// <summary>Holz-Unterbau (Rahmen, Beine, Deckplatte); null = keiner geplant.</summary>
    public BenchworkSpec? Benchwork { get; set; }
    /// <summary>Ebenen; immer mindestens eine.</summary>
    public List<PlanLevel> Levels { get; } = [new() { Name = PlanLevel.DefaultName(0) }];
    /// <summary>Ebene, auf die neue Gleise und Objekte kommen.</summary>
    public int ActiveLevel { get; set; }
    /// <summary>Länge des längsten Fahrzeugs in mm für die Planprüfung (0 = Vorgabe der Spur, siehe <see cref="VehicleDefaults"/>).</summary>
    public double VehicleLength { get; set; }
    public double EffectiveVehicleLength => VehicleLength > 0 ? VehicleLength : VehicleDefaults.Length(Gauge);
    /// <summary>Nächste freie Kennung; gilt für Gleise und alle anderen Objekte gemeinsam.</summary>
    public int NextId { get; set; } = 1;

    public Gauge Gauge => Catalog.GetGauge(GaugeId);

    public static LayoutPlan Empty(string gaugeId)
    {
        var g = Catalog.GetGauge(gaugeId);
        return new LayoutPlan { GaugeId = g.Id, BoardWidth = g.DefaultBoardWidth, BoardHeight = g.DefaultBoardHeight };
    }

    public PlacedPiece? Find(int id) => Pieces.FirstOrDefault(p => p.Id == id);

    /// <summary>Vorschlag für den Abstand zur nächsten Ebene: lichte Höhe der Spur plus Trasse und Unterbau.</summary>
    public double LevelStep => Math.Round((Gauge.ClearanceHeight + 40) / 10) * 10;

    public PlanLevel Level(int index) => Levels[Math.Clamp(index, 0, Levels.Count - 1)];

    public bool LevelVisible(int index) => Level(index).Visible;

    /// <summary>Sind alle Ebenen sichtbar?</summary>
    public bool AllLevelsVisible => Levels.All(l => l.Visible);

    /// <summary>
    /// Plan mit den sichtbaren Ebenen zum Zeichnen und Antippen: dieselben Objekte, nur ausgeblendete Ebenen fehlen.
    /// Sind alle Ebenen sichtbar, ist es der Plan selbst.
    /// </summary>
    public LayoutPlan VisibleView()
    {
        if (AllLevelsVisible) return this;
        var v = new LayoutPlan
        {
            Name = Name, GaugeId = GaugeId, BoardWidth = BoardWidth, BoardHeight = BoardHeight, BoardShape = BoardShape, BoardDepth = BoardDepth,
            BoardTurn = BoardTurn, BoardLegLeft = BoardLegLeft, BoardLegRight = BoardLegRight, VehicleLength = VehicleLength, NextId = NextId, ActiveLevel = ActiveLevel,
            Benchwork = Benchwork, Background = Background, TrainLength = TrainLength,
        };
        v.Levels.Clear();
        v.Levels.AddRange(Levels);
        v.BoardLegs.AddRange(BoardLegs);
        v.Modules.AddRange(Modules);
        v.Pieces.AddRange(Pieces.Where(p => LevelVisible(p.Level)));
        var ids = v.Pieces.Select(p => p.Id).ToHashSet();
        v.Scenery.AddRange(Scenery.Where(s => LevelVisible(s.Level)));
        v.Feeders.AddRange(Feeders.Where(f => ids.Contains(f.PieceId)));
        v.Isolations.AddRange(Isolations.Where(i => ids.Contains(i.PieceId)));
        v.Signals.AddRange(Signals.Where(s => ids.Contains(s.PieceId)));
        v.Detectors.AddRange(Detectors.Where(d => ids.Contains(d.PieceId)));
        foreach (var kv in Models) v.Models[kv.Key] = kv.Value;
        foreach (var kv in Prices) v.Prices[kv.Key] = kv.Value;
        foreach (var kv in Stock) v.Stock[kv.Key] = kv.Value;
        return v;
    }

    public PlacedPiece Add(string key)
    {
        var level = Math.Clamp(ActiveLevel, 0, Levels.Count - 1);
        var piece = new PlacedPiece { Id = NextId++, Key = key, Level = level, Height = Levels[level].Height };
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
        Detectors.RemoveAll(d => ids.Contains(d.PieceId));
        return n;
    }

    /// <summary>Entfernt ein Objekt (Gelände, Einspeisung, Trennstelle, Signal) über seine Kennung.</summary>
    public bool RemoveObject(int id)
        => Scenery.RemoveAll(s => s.Id == id) + Feeders.RemoveAll(f => f.Id == id) + Isolations.RemoveAll(i => i.Id == id) + Signals.RemoveAll(s => s.Id == id) + Detectors.RemoveAll(d => d.Id == id) > 0;

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
        if (Background is { Visible: true } bg)
        {
            Include(bg.X, bg.Y);
            Include(bg.X + bg.Width, bg.Y + bg.Height);
        }
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

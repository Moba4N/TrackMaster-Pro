namespace TrackMaster.Core;

/// <summary>Grundkörper eines Ausstattungsmodells (Gebäude, Bahnsteig, Tunnelportal …).</summary>
public enum BodyShape
{
    /// <summary>Quader; mit <see cref="ModelBody.Top"/> kleiner 1 ein Pyramidenstumpf.</summary>
    Quader,
    /// <summary>Zylinder mit elliptischer Grundfläche (Breite × Tiefe); mit Top kleiner 1 ein Kegelstumpf.</summary>
    Zylinder,
    /// <summary>Keil: hinten (−y) hoch, vorne (+y) auf null; als Pultdach oder Rampe.</summary>
    Keil,
    /// <summary>Satteldach mit First entlang der Breite (x).</summary>
    Satteldach,
    /// <summary>Walmdach: First entlang der Breite, an den Stirnseiten abgewalmt; bei quadratischem Grundriss ein Zeltdach.</summary>
    Walmdach,
    /// <summary>Pultdach (wie Keil, Dachfarbe).</summary>
    Pultdach,
    /// <summary>Gewölbe entlang der Tiefe (y): Halbellipse Breite × Höhe, innen mit Öffnung (Tunnelröhre, Hallendach).</summary>
    Gewoelbe,
    /// <summary>Wand mit Rundbogen-Öffnung unten in der Mitte (Tunnelportal, Brückenbogen, Tor).</summary>
    Bogenwand,
    /// <summary>Fenster: flache Scheibe, nachts beleuchtet.</summary>
    Fenster,
    /// <summary>Tür: flaches Türblatt.</summary>
    Tuer,
    /// <summary>Tor: breites flaches Torblatt.</summary>
    Tor,
    /// <summary>Treppe: Stufen entlang der Tiefe (y) bis zur Höhe.</summary>
    Treppe,
    /// <summary>Freier Grundriss als Vieleck, hochgezogen; Höhe je Ecke einstellbar (schräge Oberseite).</summary>
    Vieleck,
    /// <summary>Freie Form aus Eckpunkten und Flächen; jeder Eckpunkt ist frei verschiebbar.</summary>
    Freiform,
}

/// <summary>Material eines Körpers; bestimmt die Grundfarbe (eigene Farbe möglich).</summary>
public enum ModelMaterial { Putz, Ziegel, Naturstein, Holz, Beton, Glas, Dachziegel, Schiefer, Blech, Metall, Asphalt, Pflaster, Kante, Rasen, Schotter }

/// <summary>Bezug zum Gleis beim Einfügen: wie das Modell am nächsten Gleis einrastet. Das Gleis verläuft immer entlang x des Modells.</summary>
public enum ModelSnap
{
    /// <summary>Frei platzieren.</summary>
    Frei,
    /// <summary>Tunnelportal: über dem Gleis an einem Gleisende bzw. Tunnelanfang, quer zum Gleis.</summary>
    Tunnelportal,
    /// <summary>Bahnsteig, Stützmauer: neben dem Gleis im Kantenabstand, längs zum Gleis, folgt Bögen.</summary>
    Neben,
    /// <summary>Bahnübergang: auf dem Gleis, quer dazu.</summary>
    Quer,
    /// <summary>Brücke: unter dem Gleis, längs dazu, Höhe bis zur Gleisunterkante.</summary>
    Unter,
    /// <summary>Gleisabschluss: an einem offenen Gleisende, längs zum Gleis.</summary>
    Gleisende,
    /// <summary>Über dem Gleis, längs dazu (Lokschuppen, Halle).</summary>
    Auf,
}

/// <summary>
/// Ein Körper eines Modells. Maße in Metern im Vorbild; x = Breite (Länge), y = Tiefe, z = Höhe.
/// (X, Y, Z) ist die Mitte der Grundfläche, die Drehung gilt um die Hochachse durch diesen Punkt.
/// </summary>
public sealed class ModelBody
{
    public BodyShape Shape { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double W { get; set; } = 1;
    public double D { get; set; } = 1;
    public double H { get; set; } = 1;
    /// <summary>Drehung um die Hochachse in Grad (im Uhrzeigersinn bei y nach unten, wie im Gleisplan).</summary>
    public double Rotation { get; set; }
    public ModelMaterial Material { get; set; }
    /// <summary>Eigene Farbe als #RRGGBB; leer = Farbe des Materials.</summary>
    public string? Color { get; set; }
    /// <summary>Verjüngung der Oberseite (1 = gerade, 0 = Spitze) für Quader, Zylinder und Vieleck.</summary>
    public double Top { get; set; } = 1;
    /// <summary>Gewölbe und Bogenwand: Breite und Höhe der Öffnung.</summary>
    public double OpeningW { get; set; }
    public double OpeningH { get; set; }
    /// <summary>Vieleck: Grundriss relativ zu (X, Y), vor der Drehung.</summary>
    public List<(double X, double Y)> Points { get; set; } = [];
    /// <summary>Vieleck: Höhe je Ecke; leer oder kürzer = <see cref="H"/>.</summary>
    public List<double> Heights { get; set; } = [];
    /// <summary>Freiform: Eckpunkte relativ zu (X, Y, Z), vor der Drehung.</summary>
    public List<(double X, double Y, double Z)> Vertices { get; set; } = [];
    /// <summary>Freiform: Flächen als Liste von Eckpunkt-Nummern (Dreiecke oder Vielecke).</summary>
    public List<int[]> Faces { get; set; } = [];
    /// <summary>Gruppe im Editor (0 = keine): Körper einer Gruppe werden zusammen gewählt und verschoben.</summary>
    public int Group { get; set; }

    public bool IsDetail => Shape is BodyShape.Fenster or BodyShape.Tuer or BodyShape.Tor;
    public bool IsRoof => Shape is BodyShape.Satteldach or BodyShape.Walmdach or BodyShape.Pultdach;

    public ModelBody Clone() => new()
    {
        Shape = Shape, X = X, Y = Y, Z = Z, W = W, D = D, H = H, Rotation = Rotation, Material = Material, Color = Color, Top = Top,
        OpeningW = OpeningW, OpeningH = OpeningH, Points = [.. Points], Heights = [.. Heights], Vertices = [.. Vertices],
        Faces = Faces.Select(f => (int[])f.Clone()).ToList(), Group = Group,
    };

    public static string ShapeName(BodyShape s) => s switch
    {
        BodyShape.Quader => Lang.T("Quader"),
        BodyShape.Zylinder => Lang.T("Zylinder"),
        BodyShape.Keil => Lang.T("Keil"),
        BodyShape.Satteldach => Lang.T("Satteldach"),
        BodyShape.Walmdach => Lang.T("Walmdach"),
        BodyShape.Pultdach => Lang.T("Pultdach"),
        BodyShape.Gewoelbe => Lang.T("Gewölbe"),
        BodyShape.Bogenwand => Lang.T("Bogenwand"),
        BodyShape.Fenster => Lang.T("Fenster"),
        BodyShape.Tuer => Lang.T("Tür"),
        BodyShape.Tor => Lang.T("Tor"),
        BodyShape.Treppe => Lang.T("Treppe"),
        BodyShape.Vieleck => Lang.T("Vieleck"),
        _ => Lang.T("Freiform"),
    };

    public static string MaterialName(ModelMaterial m) => m switch
    {
        ModelMaterial.Putz => Lang.T("Putz"),
        ModelMaterial.Ziegel => Lang.T("Ziegel"),
        ModelMaterial.Naturstein => Lang.T("Naturstein"),
        ModelMaterial.Holz => Lang.T("Holz"),
        ModelMaterial.Beton => Lang.T("Beton"),
        ModelMaterial.Glas => Lang.T("Glas"),
        ModelMaterial.Dachziegel => Lang.T("Dachziegel"),
        ModelMaterial.Schiefer => Lang.T("Schiefer"),
        ModelMaterial.Blech => Lang.T("Blech"),
        ModelMaterial.Metall => Lang.T("Metall"),
        ModelMaterial.Asphalt => Lang.T("Asphalt"),
        ModelMaterial.Pflaster => Lang.T("Pflaster"),
        ModelMaterial.Kante => Lang.T("Bahnsteigkante"),
        ModelMaterial.Rasen => Lang.T("Rasen"),
        _ => Lang.T("Schotter"),
    };

    /// <summary>Grundfarbe des Materials.</summary>
    public static Rgb MaterialColor(ModelMaterial m) => m switch
    {
        ModelMaterial.Putz => new Rgb(0xE4, 0xD6, 0xBE),
        ModelMaterial.Ziegel => new Rgb(0xA8, 0x55, 0x3C),
        ModelMaterial.Naturstein => new Rgb(0x9A, 0x92, 0x86),
        ModelMaterial.Holz => new Rgb(0x8A, 0x64, 0x40),
        ModelMaterial.Beton => new Rgb(0xB4, 0xB2, 0xAC),
        ModelMaterial.Glas => Scene3DModel.WindowColor,
        ModelMaterial.Dachziegel => new Rgb(0xA8, 0x4A, 0x36),
        ModelMaterial.Schiefer => new Rgb(0x4E, 0x52, 0x5A),
        ModelMaterial.Blech => new Rgb(0x8C, 0x92, 0x98),
        ModelMaterial.Metall => new Rgb(0x5A, 0x60, 0x68),
        ModelMaterial.Asphalt => new Rgb(0x5C, 0x5C, 0x5E),
        ModelMaterial.Pflaster => new Rgb(0x8E, 0x88, 0x80),
        ModelMaterial.Kante => new Rgb(0xF2, 0xF2, 0xEE),
        ModelMaterial.Rasen => new Rgb(0x6E, 0x9A, 0x48),
        _ => new Rgb(0x8A, 0x84, 0x7C),
    };

    /// <summary>Liest eine Farbe #RRGGBB; null bei ungültigem Text.</summary>
    public static Rgb? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        return new Rgb((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF));
    }

    public static string ToHex(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>
/// Ausstattungsmodell: fest eingebautes Grundmodell oder selbst gebautes Modell (Modell-Editor, Pro).
/// Maße im Vorbild in Metern, im Plan auf den Maßstab der Spur umgerechnet.
/// </summary>
public sealed class ModelDefinition
{
    /// <summary>Eindeutige Kennung: „tm.…“ für Grundmodelle, sonst frei (eigene Modelle: „eigen.…“).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public ModelSnap Snap { get; set; }
    /// <summary>Material, das die Farbe aus den Eigenschaften im Plan übernimmt (Hauptfarbe); null = das erste Wandmaterial.</summary>
    public ModelMaterial? MainMaterial { get; set; }
    public List<ModelBody> Bodies { get; set; } = [];

    public bool IsBuiltin => Id.StartsWith(BuiltinPrefix, StringComparison.Ordinal);
    public const string BuiltinPrefix = "tm.";

    /// <summary>Material, das von der Hauptfarbe umgefärbt wird.</summary>
    public ModelMaterial EffectiveMainMaterial
        => MainMaterial ?? Bodies.FirstOrDefault(b => !b.IsRoof && !b.IsDetail && b.Material != ModelMaterial.Glas)?.Material ?? ModelMaterial.Putz;

    /// <summary>Umgebender Quader (x0, y0, x1, y1, z1) in Metern; leer = 1 × 1 × 1 um den Ursprung.</summary>
    public (double X0, double Y0, double X1, double Y1, double Z1) Bounds()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = 0;
        foreach (var b in Bodies)
        {
            foreach (var (x, y) in ModelMesh.Footprint(b))
            {
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            z1 = Math.Max(z1, ModelMesh.TopOf(b));
        }
        if (x0 > x1) return (-0.5, -0.5, 0.5, 0.5, 1);
        if (x1 - x0 < 1e-6) { x0 -= 0.05; x1 += 0.05; }
        if (y1 - y0 < 1e-6) { y0 -= 0.05; y1 += 0.05; }
        return (x0, y0, x1, y1, Math.Max(z1, 0.01));
    }

    /// <summary>Größe in Metern: Breite (x), Tiefe (y), Höhe.</summary>
    public (double W, double D, double H) Size
    {
        get { var b = Bounds(); return (b.X1 - b.X0, b.Y1 - b.Y0, b.Z1); }
    }

    public ModelDefinition Clone() => new()
    {
        Id = Id, Name = Name, Category = Category, Description = Description, Snap = Snap, MainMaterial = MainMaterial,
        Bodies = Bodies.Select(b => b.Clone()).ToList(),
    };
}

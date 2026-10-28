namespace TrackMaster.Core;

/// <summary>Eine Lampe im Signalschirm. Koordinaten in Schirmbreiten, x nach rechts (vom Zug aus gesehen), y von der Schirmoberkante nach unten.</summary>
public sealed record SignalLamp(double X, double Y, double R, Rgb Color, bool Lit, string Meaning);

/// <summary>
/// Ansicht eines Lichtsignals nach dem H/V-Signalsystem der DB, wie es vor dem Zug steht: schwarzer Schirm mit den Lampen an ihrer
/// echten Stelle, Mast und – bei Hauptsignalen – das weiß-rot-weiße Mastschild. Gezeigt wird das Haltbegriff (Hp 0, Hp 00, Vr 0, Sh 0),
/// die übrigen Lampen sind dunkel. Maße in Schirmbreiten.
/// </summary>
public sealed record SignalFace(
    double Width, double Height, double Corner, IReadOnlyList<SignalLamp> Lamps,
    bool MastPlate, double Mast, string Aspect, string Description)
{
    public static readonly Rgb Red = new(0xE8, 0x1E, 0x10);
    public static readonly Rgb Green = new(0x10, 0xD0, 0x60);
    public static readonly Rgb Yellow = new(0xFF, 0xA8, 0x00);
    public static readonly Rgb White = new(0xF4, 0xF4, 0xEA);
    public static readonly Rgb Dark = new(0x2A, 0x2A, 0x2A);
    public static readonly Rgb ShieldColor = new(0x14, 0x14, 0x14);
    public static readonly Rgb MastColor = new(0x7A, 0x7E, 0x82);
    public static readonly Rgb PlateRed = new(0xD0, 0x10, 0x10);
    public static readonly Rgb PlateWhite = new(0xF6, 0xF6, 0xF6);

    /// <summary>Mastschild: Höhe, Breite und Abstand unter dem Schirm (in Schirmbreiten).</summary>
    public const double PlateHeight = 0.9, PlateWidth = 0.56, PlateGap = 0.2;

    /// <summary>Gesamthöhe des Bildes vom Fuß bis zur Schirmoberkante.</summary>
    public double TotalHeight => Height + Mast;

    /// <summary>Farbe einer Lampe, wie sie gerade aussieht (dunkel, wenn sie nicht leuchtet).</summary>
    public static Rgb Shown(SignalLamp l) => l.Lit ? l.Color : Dark;

    public static SignalFace For(SignalType type) => type switch
    {
        SignalType.Hauptsignal => new(1, 2.3, 0.18,
            [new(0, 0.42, 0.2, Green, false, "Hp 1"), new(0, 1.12, 0.2, Red, true, "Hp 0"), new(0, 1.84, 0.2, Yellow, false, "Hp 2")],
            true, 1.5, "Hp 0 – Halt", Lang.T("Hauptsignal (Einfahrsignal): grün oben, rot in der Mitte, gelb unten (grün + gelb = Hp 2, Langsamfahrt).")),
        SignalType.Ausfahrsignal => new(1.15, 2.55, 0.18,
            [new(0, 0.42, 0.2, Green, false, "Hp 1"), new(-0.27, 1.1, 0.2, Red, true, "Hp 00"), new(0.27, 1.1, 0.2, Red, true, "Hp 00"),
             new(0.12, 1.76, 0.2, Yellow, false, "Hp 2"), new(-0.32, 2.3, 0.11, White, false, "Sh 1"), new(-0.05, 2.12, 0.11, White, false, "Sh 1")],
            true, 1.5, "Hp 00 – Halt", Lang.T("Ausfahrsignal: zwei rote Lampen nebeneinander (Hp 00), dazu grün, gelb und zwei weiße Lampen für Sh 1 (Rangierfahrt erlaubt).")),
        SignalType.Blocksignal => new(1, 1.9, 0.18,
            [new(0, 0.45, 0.2, Green, false, "Hp 1"), new(0, 1.4, 0.2, Red, true, "Hp 0")],
            true, 1.5, "Hp 0 – Halt", Lang.T("Blocksignal: grün oben, rot unten.")),
        SignalType.Vorsignal => new(1.25, 1.35, 0.2,
            [new(-0.3, 1.05, 0.17, Yellow, true, "Vr 0"), new(0.3, 0.68, 0.17, Yellow, true, "Vr 0"),
             new(-0.3, 0.68, 0.17, Green, false, "Vr 1"), new(0.3, 0.3, 0.17, Green, false, "Vr 1")],
            false, 1.6, Lang.T("Vr 0 – Halt erwarten"), Lang.T("Vorsignal: zwei gelbe Lampen nach rechts steigend (Vr 0), zwei grüne (Vr 1), gelb + grün (Vr 2).")),
        _ => new(1, 1.1, 0.42,
            [new(0.27, 0.25, 0.14, White, false, "Sh 1"), new(-0.27, 0.55, 0.14, Red, true, "Sh 0"), new(0.27, 0.55, 0.14, Red, true, "Sh 0"),
             new(-0.27, 0.85, 0.14, White, false, "Sh 1")],
            false, 0.25, Lang.T("Sh 0 – Halt, Fahrverbot"), Lang.T("Sperrsignal (Zwergsignal): zwei rote Lampen waagrecht (Sh 0), zwei weiße nach rechts steigend (Sh 1).")),
    };

    /// <summary>
    /// Wandelt einen Punkt des Signalbildes (Schirmkoordinaten) in Plankoordinaten: Das Bild liegt neben dem Gleis,
    /// der Fuß am Signalstandort, die Oberkante zeigt vom Gleis weg. <paramref name="scale"/> = mm je Schirmbreite.
    /// </summary>
    public static (double M11, double M12, double M21, double M22, double OffsetX, double OffsetY) PlanTransform(double x, double y, double heading, double scale, SignalFace face)
    {
        var h = heading * Math.PI / 180;
        // „oben“ im Bild = vom Gleis weg (rechts der Fahrtrichtung), „rechts“ im Bild = gegen die Fahrtrichtung
        double upX = -Math.Sin(h), upY = Math.Cos(h);
        double rightX = -upY, rightY = upX;
        var topX = x + upX * face.TotalHeight * scale;
        var topY = y + upY * face.TotalHeight * scale;
        return (rightX * scale, rightY * scale, -upX * scale, -upY * scale, topX, topY);
    }

    /// <summary>Größe des Signalbildes im Plan: maßstäblich, aber nie kleiner als <paramref name="minPixels"/> Bildschirmpunkte je Schirmbreite.</summary>
    public static double PlanScale(double gauge, double pixelsPerMm, double minPixels = 4.5) => Math.Max(gauge * 0.75, minPixels / pixelsPerMm);

    /// <summary>Punkte entlang des Signalbildes im Plan (Fuß, Mitte, Schirm) für die Trefferprüfung.</summary>
    public static IEnumerable<(double X, double Y)> PlanHitPoints(double x, double y, double heading, double scale, SignalFace face)
    {
        var h = heading * Math.PI / 180;
        double upX = -Math.Sin(h), upY = Math.Cos(h);
        for (var t = 0.0; t <= 1.0001; t += 0.25)
            yield return (x + upX * face.TotalHeight * scale * t, y + upY * face.TotalHeight * scale * t);
    }
}

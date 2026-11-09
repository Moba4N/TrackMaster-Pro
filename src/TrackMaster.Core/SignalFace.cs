namespace TrackMaster.Core;

/// <summary>Eine Lampe im Signalschirm. Koordinaten in Schirmbreiten, x nach rechts (vom Zug aus gesehen), y von der Schirmoberkante nach unten.</summary>
public sealed record SignalLamp(double X, double Y, double R, Rgb Color, bool Lit, string Meaning);

/// <summary>
/// Fläche im Signalbild (Flügel, Scheibe, Mast, Signalkopf), als Vieleck in Schirmkoordinaten.
/// <paramref name="Behind"/>: wird vor dem Schirm gezeichnet (z. B. weißer Rand um den Schirm).
/// </summary>
public sealed record SignalPart(IReadOnlyList<(double X, double Y)> Points, Rgb Fill, bool Behind = false);

/// <summary>
/// Ansicht eines Signals, wie es vor dem Zug steht: Schirm mit den Lampen an ihrer echten Stelle (Lichtsignale) oder Flügel und
/// Scheiben (Formsignale), Mast und – bei deutschen Hauptsignalen – das weiß-rot-weiße Mastschild. Gezeigt wird der Haltbegriff,
/// die übrigen Lampen sind dunkel. Maße in Schirmbreiten. Für jedes <see cref="SignalSystem"/> gibt es eigene Bilder.
/// </summary>
public sealed record SignalFace(
    double Width, double Height, double Corner, IReadOnlyList<SignalLamp> Lamps,
    bool MastPlate, double Mast, string Aspect, string Description, IReadOnlyList<SignalPart>? Parts = null, bool Shield = true)
{
    public static readonly Rgb Red = new(0xE8, 0x1E, 0x10);
    public static readonly Rgb Green = new(0x10, 0xD0, 0x60);
    public static readonly Rgb Yellow = new(0xFF, 0xA8, 0x00);
    public static readonly Rgb White = new(0xF4, 0xF4, 0xEA);
    public static readonly Rgb Violet = new(0xA0, 0x40, 0xF0);
    public static readonly Rgb Dark = new(0x2A, 0x2A, 0x2A);
    public static readonly Rgb ShieldColor = new(0x14, 0x14, 0x14);
    public static readonly Rgb MastColor = new(0x7A, 0x7E, 0x82);
    public static readonly Rgb PlateRed = new(0xD0, 0x10, 0x10);
    public static readonly Rgb PlateWhite = new(0xF6, 0xF6, 0xF6);
    public static readonly Rgb DiscYellow = new(0xF2, 0xC0, 0x00);

    /// <summary>Mastschild: Höhe, Breite und Abstand unter dem Schirm (in Schirmbreiten).</summary>
    public const double PlateHeight = 0.9, PlateWidth = 0.56, PlateGap = 0.2;

    /// <summary>Gesamthöhe des Bildes vom Fuß bis zur Schirmoberkante.</summary>
    public double TotalHeight => Height + Mast;

    /// <summary>Flächen, die vor dem Schirm gezeichnet werden, und die darüber.</summary>
    public IEnumerable<SignalPart> BackParts => (Parts ?? []).Where(p => p.Behind);
    public IEnumerable<SignalPart> FrontParts => (Parts ?? []).Where(p => !p.Behind);

    /// <summary>Farbe einer Lampe, wie sie gerade aussieht (dunkel, wenn sie nicht leuchtet).</summary>
    public static Rgb Shown(SignalLamp l) => l.Lit ? l.Color : Dark;

    public static SignalFace For(Signal s) => For(s.Type, s.System);

    // ---------- Bausteine ----------

    private static SignalLamp L(double x, double y, double r, Rgb c, bool lit, string meaning) => new(x, y, r, c, lit, meaning);

    private static SignalPart Rect(double x0, double y0, double x1, double y1, Rgb c, bool behind = false)
        => new([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], c, behind);

    private static SignalPart Circle(double cx, double cy, double r, Rgb c, bool behind = false)
        => new(Enumerable.Range(0, 24).Select(i => (cx + r * Math.Cos(i * Math.PI / 12), cy + r * Math.Sin(i * Math.PI / 12))).ToList(), c, behind);

    /// <summary>Weißer Rand um einen eckigen Schirm (z. B. Frankreich), außen dunkel abgesetzt.</summary>
    private static IReadOnlyList<SignalPart> Edge(double w, double h, double e = 0.08)
        => [Rect(-w / 2 - e - 0.03, -e - 0.03, w / 2 + e + 0.03, h + e + 0.03, ShieldColor, true), Rect(-w / 2 - e, -e, w / 2 + e, h + e, PlateWhite, true)];

    /// <summary>Lampen senkrecht untereinander, mittig, ab y0 im Abstand step.</summary>
    private static SignalLamp[] Column(double y0, double step, double r, params (Rgb Color, bool Lit, string Meaning)[] lamps)
        => lamps.Select((l, i) => L(0, y0 + i * step, r, l.Color, l.Lit, l.Meaning)).ToArray();

    /// <summary>Lichtsperrsignal (Zwergsignal) nach DB-Art: zwei rote Lampen waagrecht, zwei weiße nach rechts steigend.</summary>
    private static SignalFace Dwarf(string aspect, string description) => new(1, 1.1, 0.42,
        [L(0.27, 0.25, 0.14, White, false, "Sh 1"), L(-0.27, 0.55, 0.14, Red, true, "Sh 0"), L(0.27, 0.55, 0.14, Red, true, "Sh 0"),
         L(-0.27, 0.85, 0.14, White, false, "Sh 1")],
        false, 0.25, aspect, description);

    public static SignalFace For(SignalType type, SignalSystem system = SignalSystem.HV) => system switch
    {
        SignalSystem.Form => FormFace(type),
        SignalSystem.Ks => KsFace(type),
        SignalSystem.Hl => HlFace(type),
        SignalSystem.OeBB => OebbFace(type),
        SignalSystem.SBB => SbbFace(type),
        SignalSystem.SNCF => SncfFace(type),
        SignalSystem.NS => NsFace(type),
        SignalSystem.UK => UkFace(type),
        SignalSystem.USA => UsaFace(type),
        _ => HvFace(type),
    };

    // ---------- Deutschland: H/V-Lichtsignale ----------

    private static SignalFace HvFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal => new(1, 2.3, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Hp 1"), L(0, 1.12, 0.2, Red, true, "Hp 0"), L(0, 1.84, 0.2, Yellow, false, "Hp 2")],
            true, 1.5, "Hp 0 – Halt", Lang.T("Hauptsignal (Einfahrsignal): grün oben, rot in der Mitte, gelb unten (grün + gelb = Hp 2, Langsamfahrt).")),
        SignalType.Ausfahrsignal => new(1.15, 2.55, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Hp 1"), L(-0.27, 1.1, 0.2, Red, true, "Hp 00"), L(0.27, 1.1, 0.2, Red, true, "Hp 00"),
             L(0.12, 1.76, 0.2, Yellow, false, "Hp 2"), L(-0.32, 2.3, 0.11, White, false, "Sh 1"), L(-0.05, 2.12, 0.11, White, false, "Sh 1")],
            true, 1.5, "Hp 00 – Halt", Lang.T("Ausfahrsignal: zwei rote Lampen nebeneinander (Hp 00), dazu grün, gelb und zwei weiße Lampen für Sh 1 (Rangierfahrt erlaubt).")),
        SignalType.Blocksignal => new(1, 1.9, 0.18,
            [L(0, 0.45, 0.2, Green, false, "Hp 1"), L(0, 1.4, 0.2, Red, true, "Hp 0")],
            true, 1.5, "Hp 0 – Halt", Lang.T("Blocksignal: grün oben, rot unten.")),
        SignalType.Vorsignal => new(1.25, 1.35, 0.2,
            [L(-0.3, 1.05, 0.17, Yellow, true, "Vr 0"), L(0.3, 0.68, 0.17, Yellow, true, "Vr 0"),
             L(-0.3, 0.68, 0.17, Green, false, "Vr 1"), L(0.3, 0.3, 0.17, Green, false, "Vr 1")],
            false, 1.6, Lang.T("Vr 0 – Halt erwarten"), Lang.T("Vorsignal: zwei gelbe Lampen nach rechts steigend (Vr 0), zwei grüne (Vr 1), gelb + grün (Vr 2).")),
        _ => Dwarf(Lang.T("Sh 0 – Halt, Fahrverbot"), Lang.T("Sperrsignal (Zwergsignal): zwei rote Lampen waagrecht (Sh 0), zwei weiße nach rechts steigend (Sh 1).")),
    };

    // ---------- Deutschland: Formsignale ----------

    /// <summary>Flügel eines Formhauptsignals: rot mit weißem Rand, am Ende die runde Scheibe. Waagrecht (Halt) oder am Mast herabhängend.</summary>
    private static IEnumerable<SignalPart> Wing(double y, bool hanging)
    {
        if (!hanging)
        {
            yield return Rect(0.06, y - 0.14, 0.56, y + 0.14, ShieldColor);
            yield return Rect(0.07, y - 0.12, 0.55, y + 0.12, PlateWhite);
            yield return Rect(0.09, y - 0.08, 0.53, y + 0.08, PlateRed);
            yield return Circle(0.58, y, 0.21, ShieldColor);
            yield return Circle(0.58, y, 0.19, PlateWhite);
            yield return Circle(0.58, y, 0.14, PlateRed);
        }
        else
        {
            yield return Rect(0.06, y - 0.01, 0.3, y + 0.76, ShieldColor);
            yield return Rect(0.07, y, 0.29, y + 0.75, PlateWhite);
            yield return Rect(0.1, y + 0.03, 0.26, y + 0.72, PlateRed);
            yield return Circle(0.18, y + 0.87, 0.17, ShieldColor);
            yield return Circle(0.18, y + 0.87, 0.15, PlateWhite);
            yield return Circle(0.18, y + 0.87, 0.1, PlateRed);
        }
    }

    private static SignalFace FormFace(SignalType type)
    {
        var mast = new Rgb(0x6E, 0x72, 0x76);
        switch (type)
        {
            case SignalType.Vorsignal:
                return new(1.3, 1.6, 0, [L(-0.2, 1.47, 0.08, Yellow, true, "Vr 0"), L(0.2, 1.33, 0.08, Yellow, true, "Vr 0")],
                    false, 1.8, Lang.T("Vr 0 – Halt erwarten (Scheibe sichtbar)"),
                    Lang.T("Form-Vorsignal: runde gelbe Scheibe mit schwarzem Ring und weißem Rand zeigt Vr 0; weggeklappt = Vr 1. Nachts zwei gelbe Lichter nach rechts steigend."),
                    [Rect(-0.06, 0.6, 0.06, 1.6, mast), Circle(0, 0.62, 0.61, ShieldColor), Circle(0, 0.62, 0.58, PlateWhite), Circle(0, 0.62, 0.53, DiscYellow), Circle(0, 0.62, 0.4, ShieldColor),
                     Circle(0, 0.62, 0.33, DiscYellow), Rect(-0.34, 1.22, 0.34, 1.58, ShieldColor)], false);
            case SignalType.Sperrsignal:
                return new(1, 1, 0.5, [], false, 0.25, Lang.T("Sh 0 – Halt, Fahrverbot"),
                    Lang.T("Form-Sperrsignal: waagrechter schwarzer Streifen auf runder weißer Scheibe (Sh 0); Streifen schräg nach rechts steigend = Sh 1."),
                    [Circle(0, 0.5, 0.5, ShieldColor), Circle(0, 0.5, 0.44, PlateWhite), Rect(-0.38, 0.42, 0.38, 0.58, ShieldColor)], false);
            default:
                var two = type != SignalType.Blocksignal;
                var parts = new List<SignalPart> { Rect(-0.07, 0.05, 0.07, two ? 2.6 : 1.6, mast), Rect(-0.11, 0, 0.11, 0.1, ShieldColor) };
                parts.AddRange(Wing(0.37, false));
                if (two) parts.AddRange(Wing(1.05, true));
                parts.Add(Rect(-0.44, 0.7, -0.12, 1.1, ShieldColor));
                var desc = type switch
                {
                    SignalType.Blocksignal => Lang.T("Form-Blocksignal mit einem Flügel: waagrecht = Hp 0 (Halt), schräg nach rechts oben = Hp 1 (Fahrt)."),
                    SignalType.Ausfahrsignal => Lang.T("Form-Ausfahrsignal mit zwei Flügeln: oberer Flügel waagrecht = Hp 0, schräg = Hp 1, beide schräg = Hp 2 (Langsamfahrt). Nachts rotes Licht in der Laterne."),
                    _ => Lang.T("Form-Hauptsignal (Einfahrsignal) mit zwei Flügeln: oberer Flügel waagrecht = Hp 0, schräg = Hp 1, beide schräg = Hp 2 (Langsamfahrt). Nachts rotes Licht in der Laterne."),
                };
                return new(1.5, two ? 2.6 : 1.6, 0, [L(-0.28, 0.9, 0.1, Red, true, Lang.T("Hp 0 (Nachtzeichen)"))], false, two ? 1.3 : 1.8,
                    Lang.T("Hp 0 – Halt (Flügel waagrecht)"), desc, parts, false);
        }
    }

    // ---------- Deutschland: Ks-Signale ----------

    private static SignalFace KsFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal => new(1, 2.0, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Ks 1"), L(0.22, 0.95, 0.18, Yellow, false, "Ks 2"), L(-0.18, 1.5, 0.2, Red, true, "Hp 0"),
             L(0.28, 1.78, 0.07, White, false, Lang.T("Kennlicht"))],
            true, 1.6, "Hp 0 – Halt", Lang.T("Ks-Hauptsignal (Mehrabschnittssignal): grün = Ks 1 (Fahrt), gelb = Ks 2 (Halt erwarten), rot = Hp 0.")),
        SignalType.Ausfahrsignal => new(1, 2.5, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Ks 1"), L(0.22, 0.95, 0.18, Yellow, false, "Ks 2"), L(-0.18, 1.5, 0.2, Red, true, "Hp 0"),
             L(0.28, 1.78, 0.07, White, false, Lang.T("Kennlicht")), L(-0.3, 2.3, 0.1, White, false, "Sh 1"), L(0, 2.08, 0.1, White, false, "Sh 1")],
            true, 1.5, "Hp 0 – Halt", Lang.T("Ks-Ausfahrsignal: grün, gelb und rot, dazu zwei weiße Lampen für Sh 1 (Rangierfahrt erlaubt).")),
        SignalType.Blocksignal => new(1, 1.6, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Ks 1"), L(0, 1.15, 0.2, Red, true, "Hp 0")],
            true, 1.6, "Hp 0 – Halt", Lang.T("Ks-Blocksignal: grün = Ks 1, rot = Hp 0.")),
        SignalType.Vorsignal => new(1, 1.4, 0.18,
            [L(0, 0.42, 0.2, Green, false, "Ks 1"), L(0.2, 0.95, 0.18, Yellow, true, "Ks 2"), L(-0.28, 1.22, 0.07, White, false, Lang.T("Zusatzlicht"))],
            false, 1.8, Lang.T("Ks 2 – Halt erwarten"), Lang.T("Ks-Vorsignal: gelb = Ks 2 (Halt erwarten), grün = Ks 1 (Fahrt erwarten).")),
        _ => Dwarf(Lang.T("Sh 0 – Halt, Fahrverbot"), Lang.T("Lichtsperrsignal zum Ks-System: zwei rote Lampen waagrecht (Sh 0), zwei weiße nach rechts steigend (Sh 1).")),
    };

    // ---------- DDR: Hl-Signale ----------

    private static SignalFace HlFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal or SignalType.Ausfahrsignal => new(1, 2.75, 0.18,
            [.. Column(0.42, 0.58, 0.2, (Yellow, false, "Hl 10"), (Green, false, "Hl 1"), (Red, true, "Hp 0"), (Yellow, false, "Hl 3")),
             .. type == SignalType.Ausfahrsignal ? new[] { L(-0.3, 2.56, 0.09, White, false, "Zs 1") } : []],
            true, 1.4, "Hp 0 – Halt", type == SignalType.Ausfahrsignal
                ? Lang.T("Hl-Ausfahrsignal (DDR): gelb, grün, rot und unteres gelb; das weiße Licht ist das Ersatzsignal Zs 1.")
                : Lang.T("Hl-Hauptsignal (DDR): oben gelb (Halt erwarten), grün (Fahrt), rot (Halt), unten gelb (Geschwindigkeit begrenzt).")),
        SignalType.Blocksignal => new(1, 1.65, 0.18, Column(0.42, 0.58, 0.2, (Green, false, "Hl 1"), (Red, true, "Hp 0")),
            true, 1.6, "Hp 0 – Halt", Lang.T("Hl-Blocksignal (DDR): grün = Fahrt, rot = Halt.")),
        SignalType.Vorsignal => new(1, 1.45, 0.18, Column(0.42, 0.58, 0.2, (Yellow, true, "Hl 10"), (Green, false, "Hl 1")),
            false, 1.8, Lang.T("Hl 10 – Halt erwarten"), Lang.T("Hl-Vorsignal (DDR): gelb = Halt erwarten, grün = Fahrt erwarten.")),
        _ => Dwarf(Lang.T("Sh 0 – Halt, Fahrverbot"), Lang.T("Sperrsignal (DDR): zwei rote Lampen waagrecht (Sh 0), zwei weiße nach rechts steigend (Sh 1).")),
    };

    // ---------- Österreich ----------

    private static SignalFace OebbFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal or SignalType.Ausfahrsignal => new(1, type == SignalType.Ausfahrsignal ? 2.55 : 2.15, 0.18,
            [.. Column(0.42, 0.62, 0.2, (Green, false, Lang.T("Frei")), (Red, true, Lang.T("Halt")), (Yellow, false, Lang.T("Frei mit 40/60"))),
             .. type == SignalType.Ausfahrsignal ? new[] { L(-0.25, 2.3, 0.1, White, false, Lang.T("Verschub erlaubt")) } : []],
            false, 1.5, Lang.T("Halt"), Lang.T("ÖBB-Hauptsignal: grün = Frei, rot = Halt, grün über orange = Frei mit 40 bzw. 60 km/h.")),
        SignalType.Blocksignal => new(1, 1.55, 0.18, Column(0.42, 0.62, 0.2, (Green, false, Lang.T("Frei")), (Red, true, Lang.T("Halt"))),
            false, 1.6, Lang.T("Halt"), Lang.T("ÖBB-Blocksignal: grün = Frei, rot = Halt.")),
        SignalType.Vorsignal => new(1, 1.5, 0.18, Column(0.42, 0.62, 0.2, (Green, false, Lang.T("Frei")), (Yellow, true, Lang.T("Vorsicht"))),
            false, 1.8, Lang.T("Vorsicht – Halt erwarten"), Lang.T("ÖBB-Vorsignal: orange = Vorsicht (Halt erwarten), grün = Frei.")),
        _ => Dwarf(Lang.T("Verschubverbot"), Lang.T("ÖBB-Verschubsignal (Zwergsignal): zwei rote Lampen waagrecht = Verschubverbot, weiß schräg = Verschub erlaubt.")),
    };

    // ---------- Schweiz ----------

    private static SignalFace SbbFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal or SignalType.Ausfahrsignal => new(1, 2.65, 0.15,
            Column(0.42, 0.6, 0.2, (Green, false, Lang.T("Fahrbegriff 1")), (Red, true, Lang.T("Halt")), (Yellow, false, Lang.T("Fahrbegriff 2")), (Green, false, Lang.T("Fahrbegriff 3"))),
            false, 1.4, Lang.T("Halt"), Lang.T("SBB-Hauptsignal (System L): rot = Halt, grün = Fahrbegriff 1, grün + gelb oder zwei grüne = Fahrt mit begrenzter Geschwindigkeit.")),
        SignalType.Blocksignal => new(1, 1.6, 0.15, Column(0.42, 0.62, 0.2, (Green, false, Lang.T("Fahrt")), (Red, true, Lang.T("Halt"))),
            false, 1.6, Lang.T("Halt"), Lang.T("SBB-Blocksignal: grün = Fahrt, rot = Halt.")),
        SignalType.Vorsignal => new(1, 1.35, 0.15,
            [L(-0.25, 0.42, 0.18, Yellow, true, Lang.T("Warnung")), L(0.25, 0.42, 0.18, Yellow, true, Lang.T("Warnung")),
             L(-0.25, 0.95, 0.18, Green, false, Lang.T("Fahrt")), L(0.25, 0.95, 0.18, Green, false, Lang.T("Fahrt"))],
            false, 1.8, Lang.T("Warnung – Halt erwarten"), Lang.T("SBB-Vorsignal: zwei gelbe Lampen waagrecht = Warnung, grüne = Fahrt erwarten.")),
        _ => new(1, 1, 0.12,
            [L(-0.28, 0.28, 0.14, White, false, Lang.T("Fahrt mit Vorsicht")), L(0.28, 0.28, 0.14, White, false, Lang.T("Fahrt")),
             L(-0.28, 0.72, 0.14, White, true, Lang.T("Halt")), L(0.28, 0.72, 0.14, White, true, Lang.T("Halt"))],
            false, 0.25, Lang.T("Halt"), Lang.T("SBB-Zwergsignal: zwei weiße Lampen waagrecht = Halt, schräg nach rechts steigend = Fahrt, nach links = Fahrt mit Vorsicht.")),
    };

    // ---------- Frankreich ----------

    private static SignalFace SncfFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal or SignalType.Ausfahrsignal => new(1, 2.25, 0.06,
            Column(0.38, 0.5, 0.18, (Red, true, Lang.T("Carré")), (Yellow, false, Lang.T("Avertissement")), (Green, false, Lang.T("Voie libre")), (Red, true, Lang.T("Carré"))),
            false, 1.6, Lang.T("Carré – Halt"), Lang.T("SNCF-Zieltafel (cible): zwei rote Lampen = Carré (absoluter Halt), gelb = Avertissement, grün = Voie libre."),
            Edge(1, 2.25)),
        SignalType.Blocksignal => new(1, 1.75, 0.06,
            Column(0.38, 0.5, 0.18, (Yellow, false, Lang.T("Avertissement")), (Red, true, Lang.T("Sémaphore")), (Green, false, Lang.T("Voie libre"))),
            false, 1.7, Lang.T("Sémaphore – Halt"), Lang.T("SNCF-Blocksignal: eine rote Lampe = Sémaphore (Halt), gelb = Avertissement, grün = Voie libre."),
            Edge(1, 1.75)),
        SignalType.Vorsignal => new(1, 1.3, 0.06,
            Column(0.38, 0.5, 0.18, (Yellow, true, Lang.T("Avertissement")), (Green, false, Lang.T("Voie libre"))),
            false, 1.8, Lang.T("Avertissement – Halt erwarten"), Lang.T("SNCF-Vorsignal: gelb = Avertissement (Halt erwarten), grün = Voie libre."),
            Edge(1, 1.3)),
        _ => new(0.8, 1.15, 0.06,
            [L(0, 0.35, 0.17, Violet, true, Lang.T("Carré violet")), L(0, 0.8, 0.14, White, false, Lang.T("Manœuvre"))],
            false, 0.25, Lang.T("Carré violet – Halt"), Lang.T("SNCF-Rangiersignal: violettes Licht = Carré violet (Halt für Rangierfahrten), weiß = Rangieren erlaubt."),
            Edge(0.8, 1.15, 0.06)),
    };

    // ---------- Niederlande ----------

    private static SignalFace NsFace(SignalType type) => type switch
    {
        SignalType.Vorsignal => new(1, 1.5, 0.5, Column(0.45, 0.6, 0.2, (Yellow, true, Lang.T("Geel")), (Green, false, Lang.T("Groen"))),
            false, 1.8, Lang.T("Geel – Halt erwarten"), Lang.T("NS-Lichtsignal als Vorsignal: gelb = nächstes Signal zeigt Halt, grün = Fahrt.")),
        SignalType.Sperrsignal => new(0.9, 1.05, 0.42, [L(0, 0.32, 0.15, Red, true, Lang.T("Stop")), L(0, 0.73, 0.15, White, false, Lang.T("Rangeren"))],
            false, 0.25, Lang.T("Halt"), Lang.T("NS-Zwergsignal: rot = Halt, weiß = Rangieren erlaubt.")),
        _ => new(1, 2.1, 0.5, Column(0.45, 0.6, 0.2, (Green, false, Lang.T("Groen")), (Yellow, false, Lang.T("Geel")), (Red, true, Lang.T("Rood"))),
            false, 1.6, Lang.T("Rood – Halt"), Lang.T("NS-Lichtsignal: grün = Fahrt, gelb = Halt erwarten bzw. 40 km/h, rot = Halt.")),
    };

    // ---------- Großbritannien ----------

    private static SignalFace UkFace(SignalType type) => type switch
    {
        SignalType.Hauptsignal => new(1, 2.55, 0.12,
            Column(0.42, 0.58, 0.2, (Yellow, false, Lang.T("Double yellow")), (Green, false, Lang.T("Green")), (Yellow, false, Lang.T("Single yellow")), (Red, true, Lang.T("Danger"))),
            false, 1.5, Lang.T("Danger – Halt"), Lang.T("Britisches Vierbegriff-Lichtsignal: rot = Danger, gelb = Caution, zwei gelbe = Preliminary caution, grün = Clear.")),
        SignalType.Ausfahrsignal => new(1, 2.45, 0.12,
            [.. Column(0.42, 0.58, 0.2, (Green, false, Lang.T("Green")), (Yellow, false, Lang.T("Single yellow")), (Red, true, Lang.T("Danger"))),
             L(-0.25, 2.28, 0.09, White, false, Lang.T("Position light")), L(0.2, 2.05, 0.09, White, false, Lang.T("Position light"))],
            false, 1.5, Lang.T("Danger – Halt"), Lang.T("Britisches Lichtsignal mit Rangierlicht: rot = Danger; zwei weiße Lichter schräg = Rangierfahrt erlaubt.")),
        SignalType.Blocksignal => new(1, 1.95, 0.12,
            Column(0.42, 0.58, 0.2, (Green, false, Lang.T("Green")), (Yellow, false, Lang.T("Single yellow")), (Red, true, Lang.T("Danger"))),
            false, 1.6, Lang.T("Danger – Halt"), Lang.T("Britisches Dreibegriff-Lichtsignal: rot = Danger, gelb = Caution, grün = Clear.")),
        SignalType.Vorsignal => new(1, 1.4, 0.12, Column(0.42, 0.58, 0.2, (Yellow, true, Lang.T("Caution")), (Green, false, Lang.T("Green"))),
            false, 1.8, Lang.T("Caution – Halt erwarten"), Lang.T("Britisches Vorsignal (Distant): gelb = Caution, grün = Clear.")),
        _ => new(1, 0.85, 0.42,
            [L(-0.25, 0.22, 0.13, White, false, Lang.T("Proceed")), L(-0.25, 0.6, 0.13, Red, true, Lang.T("Stop")), L(0.25, 0.6, 0.13, White, true, Lang.T("Stop"))],
            false, 0.25, Lang.T("Stop – Halt"), Lang.T("Britisches Rangiersignal (Position light): rot + weiß waagrecht = Stop, zwei weiße schräg = Proceed.")),
    };

    // ---------- USA ----------

    private static SignalFace UsaFace(SignalType type)
    {
        var head = Column(0.3, 0.38, 0.17, (Green, false, Lang.T("Clear")), (Yellow, false, Lang.T("Approach")), (Red, true, Lang.T("Stop")));
        switch (type)
        {
            case SignalType.Hauptsignal or SignalType.Ausfahrsignal:
                var lower = Column(1.9, 0.38, 0.17, (Green, false, Lang.T("Diverging clear")), (Yellow, false, Lang.T("Diverging approach")), (Red, true, Lang.T("Stop")));
                return new(0.9, 2.95, 0.3, [.. head, .. lower], false, 1.2, Lang.T("Stop – Halt (rot über rot)"),
                    Lang.T("US-Lichtsignal mit zwei Signalköpfen: rot über rot = Stop, der obere Kopf gilt für die Hauptstrecke, der untere für die Abzweigung."),
                    [Rect(-0.45, 0, 0.45, 1.36, ShieldColor), Rect(-0.45, 1.6, 0.45, 2.95, ShieldColor), Rect(-0.06, 1.36, 0.06, 1.6, MastColor)], false);
            case SignalType.Vorsignal:
                return new(0.9, 1.36, 0.3, Column(0.3, 0.38, 0.17, (Green, false, Lang.T("Clear")), (Yellow, true, Lang.T("Approach")), (Red, false, Lang.T("Stop"))),
                    false, 1.8, Lang.T("Approach – Halt erwarten"), Lang.T("US-Vorsignal (Distant): gelb = Approach (Halt erwarten), grün = Clear."));
            case SignalType.Sperrsignal:
                return new(0.8, 0.95, 0.35, [L(0, 0.3, 0.16, Red, true, Lang.T("Stop")), L(0, 0.66, 0.16, Yellow, false, Lang.T("Restricting"))],
                    false, 0.2, Lang.T("Stop – Halt"), Lang.T("US-Zwergsignal: rot = Stop, gelb = Restricting (Fahrt auf Sicht)."));
            default:
                return new(0.9, 1.36, 0.3, head, false, 1.8, Lang.T("Stop – Halt"), Lang.T("US-Blocksignal: grün = Clear, gelb = Approach, rot = Stop."));
        }
    }

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

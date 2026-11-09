namespace TrackMaster.Core;

/// <summary>Betriebsart der Anlage: klassisch mit Fahrtrafo (analog) oder mit Digitalzentrale und Boostern.</summary>
public enum PowerMode { Digital, Analog }

/// <summary>Art einer Stromquelle im Plan (<see cref="SceneryKind.Power"/>, <see cref="SceneryItem.Variant"/>).</summary>
public enum PowerSourceType { Zentrale, Booster, Fahrtrafo, Zubehoertrafo, Verteiler, Baustein }

/// <summary>
/// Stromquellen als Symbol im Gleisplan: Digitalzentrale, Booster, Fahrtrafo (analog) und Zubehörtrafo (Licht, Weichen),
/// dazu Verteilerleisten in Aderfarbe (rot, schwarz, blau, gelb, braun), an denen mehrere Kabel zusammenlaufen.
/// Gleisanschlüsse (<see cref="Feeder"/>) hängen über ihren Stromkreis (<see cref="Feeder.Circuit"/> = Name der Quelle) daran.
/// Das Symbol ist ein Rechteck um (X, Y) mit Breite <see cref="SceneryItem.Width"/> und Tiefe <see cref="SceneryItem.Depth"/>.
/// </summary>
public static class PowerSupply
{
    public static PowerSourceType Type(SceneryItem s) => Enum.IsDefined((PowerSourceType)s.Variant) ? (PowerSourceType)s.Variant : PowerSourceType.Zentrale;

    public static string TypeName(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => Lang.T("Digitalzentrale"),
        PowerSourceType.Booster => Lang.T("Booster"),
        PowerSourceType.Fahrtrafo => Lang.T("Fahrtrafo"),
        PowerSourceType.Verteiler => Lang.T("Verteiler"),
        PowerSourceType.Baustein => Lang.T("Decoder-Baustein"),
        _ => Lang.T("Zubehörtrafo"),
    };

    /// <summary>Text im Symbol: Gerätetyp (z. B. „YD7010“), sonst die Art.</summary>
    public static string Label(SceneryItem s) => !string.IsNullOrEmpty(s.Model) ? s.Model : ShortName(Type(s));

    /// <summary>Kurzer Text im Symbol.</summary>
    public static string ShortName(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => Lang.T("Zentrale"),
        PowerSourceType.Booster => Lang.T("Booster"),
        PowerSourceType.Fahrtrafo => Lang.T("Trafo"),
        PowerSourceType.Verteiler => Lang.T("Verteiler"),
        PowerSourceType.Baustein => Lang.T("Decoder"),
        _ => Lang.T("Licht"),
    };

    public static string Description(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => Lang.T("Digitalbetrieb: erzeugt das Digitalsignal und speist den ersten Stromkreis (z. B. Märklin CS3, ESU ECoS, Roco Z21)."),
        PowerSourceType.Booster => Lang.T("Digitalbetrieb: weiterer Stromkreis für große Anlagen; zwischen Booster-Abschnitten gehören Trennstellen."),
        PowerSourceType.Fahrtrafo => Lang.T("Analogbetrieb: Fahrregler bzw. Trafo, der einen Stromkreis mit Fahrstrom versorgt."),
        PowerSourceType.Verteiler => Lang.T("Verteilerleiste: verbindet alle Kabel, die an ihren Klemmen enden – z. B. die Masse (braun) von Zentrale, Booster und Gleis oder die Zubehörleitungen (gelb)."),
        PowerSourceType.Baustein => Lang.T("Decoder-Baustein (z. B. YaMoRC): schaltet Weichen und Signale oder meldet besetzte Gleise; speist kein Gleis."),
        _ => Lang.T("Licht, Weichen und Signale (Zubehör); speist kein Gleis."),
    };

    /// <summary>Hat die Quelle eine eigene Masse-Klemme (0) neben der Bahnstrom-Klemme (B)?</summary>
    public static bool HasGround(PowerSourceType t) => t is PowerSourceType.Zentrale or PowerSourceType.Booster;

    /// <summary>Farben der Verteilerleisten (wie die Aderfarben).</summary>
    public static IReadOnlyList<WireColor> DistributorColors { get; } =
        new[] { "rot", "schwarz", "blau", "gelb", "braun" }.Select(n => WireLayout.Colors.First(c => c.Name == n)).ToList();

    /// <summary>Farbe einer Verteilerleiste (Standard braun).</summary>
    public static string DistributorColor(SceneryItem s)
        => DistributorColors.FirstOrDefault(c => string.Equals(c.Hex, s.Color, StringComparison.OrdinalIgnoreCase))?.Hex ?? "#795548";

    /// <summary>Klemmen je Verteilerleiste.</summary>
    public const int Clamps = 10;

    /// <summary>Speist diese Quelle Gleise mit Fahrstrom?</summary>
    public static bool FeedsTrack(PowerSourceType t) => t is not (PowerSourceType.Zubehoertrafo or PowerSourceType.Verteiler or PowerSourceType.Baustein);

    /// <summary>Passt die Quelle zur Betriebsart (als Fahrstromquelle)?</summary>
    public static bool Fits(PowerSourceType t, PowerMode mode) => t switch
    {
        PowerSourceType.Zentrale or PowerSourceType.Booster => mode == PowerMode.Digital,
        PowerSourceType.Fahrtrafo => mode == PowerMode.Analog,
        _ => true,
    };

    /// <summary>Quellen, die bei dieser Betriebsart angeboten werden (die passende Fahrstromquelle zuerst).</summary>
    public static IReadOnlyList<PowerSourceType> TypesFor(PowerMode mode) => mode == PowerMode.Digital
        ? [PowerSourceType.Zentrale, PowerSourceType.Booster, PowerSourceType.Zubehoertrafo]
        : [PowerSourceType.Fahrtrafo, PowerSourceType.Zubehoertrafo];

    public static string ModeName(PowerMode m) => m == PowerMode.Analog ? Lang.T("Analog") : Lang.T("Digital");

    /// <summary>Alle Symbole der Stromversorgung: Quellen und Verteiler.</summary>
    public static IEnumerable<SceneryItem> Items(LayoutPlan plan) => plan.Scenery.Where(s => s.Kind == SceneryKind.Power);

    /// <summary>Stromquellen (ohne Verteiler und Decoder-Bausteine).</summary>
    public static IEnumerable<SceneryItem> Sources(LayoutPlan plan) => Items(plan).Where(s => Type(s) is not (PowerSourceType.Verteiler or PowerSourceType.Baustein));

    /// <summary>Decoder-Bausteine (Weichen-, Schalt-, Servo- und Rückmeldedecoder, Kehrschleifen- und Drehscheibenmodule).</summary>
    public static IEnumerable<SceneryItem> Modules(LayoutPlan plan) => Items(plan).Where(s => Type(s) == PowerSourceType.Baustein);

    public static IEnumerable<SceneryItem> Distributors(LayoutPlan plan) => Items(plan).Where(s => Type(s) == PowerSourceType.Verteiler);

    /// <summary>Quellen, die Fahrstrom liefern (Name = Stromkreis der Gleisanschlüsse).</summary>
    public static IEnumerable<SceneryItem> TrackSources(LayoutPlan plan) => Sources(plan).Where(s => FeedsTrack(Type(s)));

    public static SceneryItem? SourceOf(LayoutPlan plan, Feeder f) => TrackSources(plan).FirstOrDefault(s => s.Name == f.Circuit);

    public static IEnumerable<Feeder> FeedersOf(LayoutPlan plan, SceneryItem source) => plan.Feeders.Where(f => f.Circuit == source.Name);

    /// <summary>Symbolgröße nach Spurweite (H0: etwa 165 × 100 mm, ungefähr wie eine Zentrale).</summary>
    public static (double Width, double Depth) Size(Gauge gauge)
    {
        var w = Math.Round(Math.Clamp(gauge.TrackWidth * 10, 60, 500));
        return (w, Math.Round(w * 0.62));
    }

    /// <summary>Verteilerleiste: lang und schmal.</summary>
    public static (double Width, double Depth) DistributorSize(Gauge gauge)
    {
        var w = Math.Round(Math.Clamp(gauge.TrackWidth * 9, 50, 450));
        return (w, Math.Round(w * 0.2));
    }

    /// <summary>Punkt im Symbol (lokal, Mitte = 0, y nach unten) in Plankoordinaten.</summary>
    public static (double X, double Y) Local(SceneryItem s, double lx, double ly)
    {
        var r = s.Rotation * Math.PI / 180;
        double c = Math.Cos(r), n = Math.Sin(r);
        return (s.X + lx * c - ly * n, s.Y + lx * n + ly * c);
    }

    /// <summary>
    /// Bahnstrom-Klemme (B) an der unteren Kante: hier rasten Kabel ein, hier beginnen die roten Verbindungslinien zu den Gleisanschlüssen.
    /// Zentrale und Booster haben daneben die Masse-Klemme (<see cref="GroundTerminal"/>), sonst sitzt die Klemme in der Mitte.
    /// </summary>
    public static (double X, double Y) Terminal(SceneryItem s)
        => Local(s, HasGround(Type(s)) ? -s.Width / 5 : 0, s.Depth / 2);

    /// <summary>
    /// Verbindungslinie Klemme → Gleisanschluss, rechtwinklig: erst ein Stück senkrecht aus der Klemme, dann waagerecht, dann senkrecht zum Gleis.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> LinkPath((double X, double Y) terminal, (double X, double Y) feeder, SceneryItem src)
    {
        var r = src.Rotation * Math.PI / 180;
        // Richtung „aus der Klemme heraus“ (untere Kante des Symbols), auf die Hauptachse gerundet
        double ox = -Math.Sin(r), oy = Math.Cos(r);
        var off = Math.Max(src.Depth * 0.35, 10);
        if (Math.Abs(ox) >= Math.Abs(oy))
        {
            var x1 = terminal.X + Math.Sign(ox) * off;
            return [terminal, (x1, terminal.Y), (x1, feeder.Y), feeder];
        }
        var y1 = terminal.Y + Math.Sign(oy) * off;
        return [terminal, (terminal.X, y1), (feeder.X, y1), feeder];
    }

    /// <summary>Masse-Klemme (0) von Zentrale und Booster, sonst null.</summary>
    public static (double X, double Y)? GroundTerminal(SceneryItem s)
        => HasGround(Type(s)) ? Local(s, s.Width / 5, s.Depth / 2) : null;

    /// <summary>Klemmen einer Verteilerleiste, gleichmäßig auf der Mittellinie.</summary>
    public static IReadOnlyList<(double X, double Y)> ClampPoints(SceneryItem s)
        => Enumerable.Range(0, Clamps).Select(i => Local(s, -s.Width / 2 + s.Width * (i + 0.5) / Clamps, 0)).ToList();

    /// <summary>Freier Name: „Zentrale“, „Booster 1“, „Trafo 2“ …</summary>
    public static string NewName(LayoutPlan plan, PowerSourceType t)
    {
        var used = plan.Scenery.Select(s => s.Name).Concat(plan.Feeders.Select(f => f.Circuit)).ToHashSet();
        var baseName = t switch
        {
            PowerSourceType.Zentrale => Lang.T("Zentrale"),
            PowerSourceType.Booster => Lang.T("Booster"),
            PowerSourceType.Fahrtrafo => Lang.T("Trafo"),
            PowerSourceType.Verteiler => Lang.T("Verteiler"),
            PowerSourceType.Baustein => Lang.T("Decoder"),
            _ => Lang.T("Zubehörtrafo"),
        };
        if (t == PowerSourceType.Zentrale && !used.Contains(baseName)) return baseName;
        for (var n = 1; ; n++)
            if (!used.Contains($"{baseName} {n}")) return $"{baseName} {n}";
    }

    /// <summary>Neues Symbol (noch nicht im Plan).</summary>
    public static SceneryItem Create(LayoutPlan plan, PowerSourceType t, double x, double y, string? color = null)
    {
        var (w, d) = t == PowerSourceType.Verteiler ? DistributorSize(plan.Gauge) : Size(plan.Gauge);
        return new SceneryItem
        {
            Kind = SceneryKind.Power, Variant = (int)t, Name = NewName(plan, t), X = Math.Round(x), Y = Math.Round(y), Width = w, Depth = d,
            Color = t == PowerSourceType.Verteiler ? color ?? "#795548" : null,
        };
    }

    /// <summary>Art ändern; zwischen Gerät und Verteilerleiste wechseln dabei Größe und Farbe.</summary>
    public static void ApplyType(SceneryItem item, PowerSourceType type, Gauge gauge, string distributorColor = "#795548")
    {
        var was = Type(item) == PowerSourceType.Verteiler;
        // Gerät (z. B. YaMoRC-Zentrale) passt nach dem Wechsel der Art nicht mehr
        if (Yamorc.ModelOf(item) is { } m && Yamorc.PlacedAs(m) != type) { item.Model = null; item.Address = null; }
        item.Variant = (int)type;
        if (was == (type == PowerSourceType.Verteiler)) return;
        (item.Width, item.Depth) = type == PowerSourceType.Verteiler ? DistributorSize(gauge) : Size(gauge);
        item.Color = type == PowerSourceType.Verteiler ? distributorColor : null;
    }

    /// <summary>Platz für eine automatisch angelegte Quelle: unter der Platte links, neben schon vorhandenen Quellen.</summary>
    public static (double X, double Y) DefaultSpot(LayoutPlan plan)
    {
        var (w, d) = Size(plan.Gauge);
        var n = Items(plan).Count();
        return (w * 0.75 + n * w * 1.3, plan.BoardHeight + d * 1.1);
    }

    /// <summary>Farbe des Symbols als #RRGGBB.</summary>
    public static string Color(SceneryItem s) => Type(s) == PowerSourceType.Verteiler ? DistributorColor(s) : Color(Type(s));

    public static string Color(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => "#37474F",
        PowerSourceType.Booster => "#455A64",
        PowerSourceType.Fahrtrafo => "#1565C0",
        PowerSourceType.Verteiler => "#795548",
        PowerSourceType.Baustein => "#1B5E20",
        _ => "#8D6E00",
    };
}

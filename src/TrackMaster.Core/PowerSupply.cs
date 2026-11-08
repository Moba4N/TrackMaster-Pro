namespace TrackMaster.Core;

/// <summary>Betriebsart der Anlage: klassisch mit Fahrtrafo (analog) oder mit Digitalzentrale und Boostern.</summary>
public enum PowerMode { Digital, Analog }

/// <summary>Art einer Stromquelle im Plan (<see cref="SceneryKind.Power"/>, <see cref="SceneryItem.Variant"/>).</summary>
public enum PowerSourceType { Zentrale, Booster, Fahrtrafo, Zubehoertrafo }

/// <summary>
/// Stromquellen als Symbol im Gleisplan: Digitalzentrale, Booster, Fahrtrafo (analog) und Zubehörtrafo (Licht, Weichen).
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
        _ => Lang.T("Zubehörtrafo"),
    };

    /// <summary>Kurzer Text im Symbol.</summary>
    public static string ShortName(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => Lang.T("Zentrale"),
        PowerSourceType.Booster => Lang.T("Booster"),
        PowerSourceType.Fahrtrafo => Lang.T("Trafo"),
        _ => Lang.T("Licht"),
    };

    public static string Description(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => Lang.T("Digitalbetrieb: erzeugt das Digitalsignal und speist den ersten Stromkreis (z. B. Märklin CS3, ESU ECoS, Roco Z21)."),
        PowerSourceType.Booster => Lang.T("Digitalbetrieb: weiterer Stromkreis für große Anlagen; zwischen Booster-Abschnitten gehören Trennstellen."),
        PowerSourceType.Fahrtrafo => Lang.T("Analogbetrieb: Fahrregler bzw. Trafo, der einen Stromkreis mit Fahrstrom versorgt."),
        _ => Lang.T("Licht, Weichen und Signale (Zubehör); speist kein Gleis."),
    };

    /// <summary>Speist diese Quelle Gleise mit Fahrstrom?</summary>
    public static bool FeedsTrack(PowerSourceType t) => t != PowerSourceType.Zubehoertrafo;

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

    public static IEnumerable<SceneryItem> Sources(LayoutPlan plan) => plan.Scenery.Where(s => s.Kind == SceneryKind.Power);

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

    /// <summary>Anschlussklemme des Symbols (Mitte der unteren Kante): hier rasten Kabel ein, hier beginnen die Verbindungslinien.</summary>
    public static (double X, double Y) Terminal(SceneryItem s)
    {
        var r = s.Rotation * Math.PI / 180;
        return (s.X - Math.Sin(r) * s.Depth / 2, s.Y + Math.Cos(r) * s.Depth / 2);
    }

    /// <summary>Freier Name: „Zentrale“, „Booster 1“, „Trafo 2“ …</summary>
    public static string NewName(LayoutPlan plan, PowerSourceType t)
    {
        var used = plan.Scenery.Select(s => s.Name).Concat(plan.Feeders.Select(f => f.Circuit)).ToHashSet();
        var baseName = t switch
        {
            PowerSourceType.Zentrale => Lang.T("Zentrale"),
            PowerSourceType.Booster => Lang.T("Booster"),
            PowerSourceType.Fahrtrafo => Lang.T("Trafo"),
            _ => Lang.T("Zubehörtrafo"),
        };
        if (t == PowerSourceType.Zentrale && !used.Contains(baseName)) return baseName;
        for (var n = 1; ; n++)
            if (!used.Contains($"{baseName} {n}")) return $"{baseName} {n}";
    }

    /// <summary>Neues Symbol (noch nicht im Plan).</summary>
    public static SceneryItem Create(LayoutPlan plan, PowerSourceType t, double x, double y)
    {
        var (w, d) = Size(plan.Gauge);
        return new SceneryItem { Kind = SceneryKind.Power, Variant = (int)t, Name = NewName(plan, t), X = Math.Round(x), Y = Math.Round(y), Width = w, Depth = d };
    }

    /// <summary>Platz für eine automatisch angelegte Quelle: unter der Platte links, neben schon vorhandenen Quellen.</summary>
    public static (double X, double Y) DefaultSpot(LayoutPlan plan)
    {
        var (w, d) = Size(plan.Gauge);
        var n = Sources(plan).Count();
        return (w * 0.75 + n * w * 1.3, plan.BoardHeight + d * 1.1);
    }

    /// <summary>Farbe des Symbols als #RRGGBB.</summary>
    public static string Color(PowerSourceType t) => t switch
    {
        PowerSourceType.Zentrale => "#37474F",
        PowerSourceType.Booster => "#455A64",
        PowerSourceType.Fahrtrafo => "#1565C0",
        _ => "#8D6E00",
    };
}

namespace TrackMaster.Core;

/// <summary>Gruppe eines Decoder-Bausteins (Untermenü).</summary>
public enum ModuleGroup { Magnetartikel, Servo, Schalten, Rueckmelder, Zentrale, Booster, Sondermodul, Zubehoer }

/// <summary>Was ein Baustein mit seinen Adressen tut.</summary>
public enum ModuleRole
{
    /// <summary>Weichen, Signale und Schaltausgänge über Zubehöradressen.</summary>
    Schalten,
    /// <summary>Rückmelder: Kontakte für Besetztmelder.</summary>
    Melden,
    /// <summary>Zentrale bzw. Booster (als Stromquelle gesetzt).</summary>
    Strom,
    /// <summary>Ohne eigene Adressen (Adapter, Netzteil, Sondermodul).</summary>
    Keine,
}

/// <summary>
/// Ein Decoder-Typ eines Herstellers. <see cref="Outputs"/> = Anschlüsse am Gerät (Ausgänge bzw. Melder-Eingänge),
/// <see cref="Addresses"/> = belegte Zubehöradressen bzw. Rückmeldekontakte ab der Startadresse.
/// </summary>
public sealed record DecoderModel(string Id, string Name, ModuleGroup Group, ModuleRole Role, int Outputs, int Addresses, string Bus, string Description)
{
    public string Maker => "YaMoRC";
    public string Title => $"{Id} – {Name}";
}

/// <summary>
/// Decoder und Module von YaMoRC als einzelne Bausteine für den Plan. Die Angaben stammen aus Händlerlisten und
/// Herstellerunterlagen (Stand November 2026); Anschlüsse und Adressen sind die üblichen Werte, am Gerät einstellbar.
/// Ein Baustein im Plan ist ein Symbol der Stromversorgung (<see cref="PowerSourceType.Baustein"/>) mit
/// <see cref="SceneryItem.Model"/> = Typ und <see cref="SceneryItem.Address"/> = Startadresse.
/// Zentralen und Booster werden als <see cref="PowerSourceType.Zentrale"/> bzw. <see cref="PowerSourceType.Booster"/> gesetzt.
/// </summary>
public static class Yamorc
{
    public static IReadOnlyList<DecoderModel> Models { get; } =
    [
        // Weichendecoder für Doppelspulen-Antriebe
        new("YD8008", Lang.T("8× Magnetartikel mit CDU"), ModuleGroup.Magnetartikel, ModuleRole.Schalten, 8, 8, "DCC",
            Lang.T("Weichendecoder für 8 Doppelspulenantriebe (je 9,5 A Impuls) mit eingebautem Kondensator-Speicher (CDU).")),
        new("YD8044", Lang.T("4× Magnetartikel mit CDU und Herzstückpolarisation"), ModuleGroup.Magnetartikel, ModuleRole.Schalten, 4, 4, "DCC",
            Lang.T("Weichendecoder für 4 Doppelspulenantriebe mit CDU, dazu je ein Relais für die Herzstückpolarisation.")),
        // Servo
        new("YD8248", Lang.T("4× Servo und 8× Signal/Schalten"), ModuleGroup.Servo, ModuleRole.Schalten, 12, 12, "DCC",
            Lang.T("4 Servos für Weichen, Schranken oder Türen und 8 Schaltausgänge für Signale oder Licht.")),
        new("YD8291", Lang.T("4× Servo-Erweiterung für YD8248"), ModuleGroup.Servo, ModuleRole.Keine, 4, 0, "ES-Link",
            Lang.T("Erweitert den YD8248 um 4 weitere Servoanschlüsse.")),
        new("YD8290", Lang.T("4× Relais-Erweiterung für YD8248"), ModuleGroup.Servo, ModuleRole.Keine, 4, 0, "ES-Link",
            Lang.T("4 Relais am YD8248, z. B. für Herzstückpolarisation bei Servoweichen.")),
        // Schalt- und Signaldecoder
        new("YD8116", Lang.T("16× Signal/Schalten"), ModuleGroup.Schalten, ModuleRole.Schalten, 16, 16, "DCC",
            Lang.T("16 Ausgänge (je 2,5 A, zusammen 5 A) für Lichtsignale, Beleuchtung, Motorantriebe und Schaltaufgaben.")),
        new("YD6940", Lang.T("Motorantrieb-Adapter (2 Stück)"), ModuleGroup.Schalten, ModuleRole.Keine, 2, 0, "–",
            Lang.T("Adapter für Motorantriebe mit Polwechsel (z. B. Tortoise, Cobalt) am YD8116.")),
        new("YD6942", Lang.T("Adapter für Signale mit gemeinsamer Kathode"), ModuleGroup.Schalten, ModuleRole.Keine, 2, 0, "–",
            Lang.T("Plus/Minus-Wandler für Lichtsignale mit gemeinsamer Kathode am YD8116.")),
        // Rückmelder
        new("YD6016LN-CS", Lang.T("16× Stromfühler, LocoNet"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "LocoNet, s88",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern (2 Gruppen à 8), meldet über LocoNet oder s88.")),
        new("YD6016LN-GND", Lang.T("16× Masse-Eingang, LocoNet"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "LocoNet, s88",
            Lang.T("16 Eingänge gegen Masse für Schaltgleise, Reedkontakte und Taster, meldet über LocoNet oder s88.")),
        new("YD6016LN-RC", Lang.T("16× RailCom-Melder, LocoNet"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "LocoNet, RailCom",
            Lang.T("16 Besetztmelder mit RailCom (erkennt die Lokadresse) und ein globaler RailCom-Melder.")),
        new("YD6016ES-CS", Lang.T("16× Stromfühler, s88"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "s88N",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern am s88-Bus.")),
        new("YD6016ES-GND", Lang.T("16× Masse-Eingang, s88"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "s88N",
            Lang.T("16 Eingänge gegen Masse am s88-Bus, z. B. für Märklin-Kontaktgleise.")),
        new("YD6016RB-CS", Lang.T("16× Stromfühler, R-Bus"), ModuleGroup.Rueckmelder, ModuleRole.Melden, 16, 16, "R-Bus, s88",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern für den Roco/Fleischmann R-Bus (Z21).")),
        new("YD6903-S88", Lang.T("s88-auf-s88N-Adapter"), ModuleGroup.Rueckmelder, ModuleRole.Keine, 1, 0, "s88, s88N",
            Lang.T("Verbindet klassische s88-Module mit dem s88N-Netzwerkkabel (RJ45).")),
        // Zentralen
        new("YD7010", Lang.T("Digitalzentrale mit WLAN"), ModuleGroup.Zentrale, ModuleRole.Strom, 1, 0, "LAN/WLAN",
            Lang.T("DCC-Zentrale mit WLAN und LAN für Apps und Steuerprogramme.")),
        new("YD7001", Lang.T("Multi-Protokoll-Zentrale (LAN)"), ModuleGroup.Zentrale, ModuleRole.Strom, 1, 0, "LAN",
            Lang.T("Zentrale mit mehreren LAN-Protokollen für gängige Steuerprogramme.")),
        new("YD9401", Lang.T("Netzwerk-Modul"), ModuleGroup.Zentrale, ModuleRole.Keine, 1, 0, "LAN/WLAN",
            Lang.T("LAN/WLAN-Nachrüstmodul (z. B. für die Digikeijs DR5000).")),
        new("YD7460-15", Lang.T("Netzteil 15 V"), ModuleGroup.Zentrale, ModuleRole.Keine, 1, 0, "–",
            Lang.T("Netzteil 15 V / 60 W für Zentrale und Booster (N, TT).")),
        new("YD7460-18", Lang.T("Netzteil 18 V"), ModuleGroup.Zentrale, ModuleRole.Keine, 1, 0, "–",
            Lang.T("Netzteil 18 V / 60 W für Zentrale und Booster (H0).")),
        // Booster
        new("YD7403", Lang.T("Booster 3,5 A, galvanisch getrennt"), ModuleGroup.Booster, ModuleRole.Strom, 1, 0, "LocoNet-B, B-Bus, CDE",
            Lang.T("Universeller Booster mit 3,5 A für einen weiteren Stromkreis, galvanisch getrennt.")),
        new("YD7039", Lang.T("LocoNet-auf-CDE-Adapter"), ModuleGroup.Booster, ModuleRole.Keine, 1, 0, "LocoNet, CDE",
            Lang.T("Führt das Boostersignal von LocoNet auf die CDE-Klemmen.")),
        // Sondermodule
        new("YD7432", Lang.T("Kehrschleifenmodul"), ModuleGroup.Sondermodul, ModuleRole.Keine, 2, 0, "–",
            Lang.T("Polt den Kehrschleifenabschnitt automatisch um (Zweileiter-Gleichstrom).")),
        new("YD7652", Lang.T("Drehscheibenmodul"), ModuleGroup.Sondermodul, ModuleRole.Keine, 1, 0, "LocoNet, RailCom",
            Lang.T("Steuert Drehscheiben über Weichenbefehle, mit RailCom-Melder auf der Bühne.")),
        new("YD7652EX-TURN", Lang.T("Drehscheiben-Erweiterung"), ModuleGroup.Sondermodul, ModuleRole.Keine, 1, 0, "ES-Link",
            Lang.T("Zusatzmodul für ältere Roco- und Märklin-Drehscheiben.")),
        new("YD7652ES-OPTO", Lang.T("Positionssensor für Drehscheiben"), ModuleGroup.Sondermodul, ModuleRole.Keine, 1, 0, "ES-Link",
            Lang.T("Optische Positionserkennung der Bühne für das Drehscheibenmodul.")),
        new("YD7652ST-STEP", Lang.T("Schrittmotor-Treiber für Drehscheiben"), ModuleGroup.Sondermodul, ModuleRole.Keine, 1, 0, "ES-Link",
            Lang.T("Schrittmotor-Antrieb für das Drehscheibenmodul.")),
        // Zubehör
        new("YD9100", Lang.T("USB-Konfigurationsstick"), ModuleGroup.Zubehoer, ModuleRole.Keine, 1, 0, "ES-Link, USB",
            Lang.T("Programmiert YaMoRC-Bausteine am PC über ES-Link.")),
        new("YD6902ES-EXT", Lang.T("ES-Link-Verlängerung (RJ45)"), ModuleGroup.Zubehoer, ModuleRole.Keine, 2, 0, "ES-Link",
            Lang.T("Ein Paar Adapter, um ES-Link über ein Netzwerkkabel zu verlängern.")),
    ];

    public static DecoderModel? Find(string? id) => id is null ? null : Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string GroupName(ModuleGroup g) => g switch
    {
        ModuleGroup.Magnetartikel => Lang.T("Weichendecoder (Magnetartikel)"),
        ModuleGroup.Servo => Lang.T("Servodecoder"),
        ModuleGroup.Schalten => Lang.T("Schalt- und Signaldecoder"),
        ModuleGroup.Rueckmelder => Lang.T("Rückmelder"),
        ModuleGroup.Zentrale => Lang.T("Zentralen"),
        ModuleGroup.Booster => Lang.T("Booster"),
        ModuleGroup.Sondermodul => Lang.T("Kehrschleife und Drehscheibe"),
        _ => Lang.T("Zubehör"),
    };

    /// <summary>Bausteine nach Gruppen, in der Reihenfolge des Untermenüs.</summary>
    public static IReadOnlyList<(ModuleGroup Group, IReadOnlyList<DecoderModel> Models)> Groups { get; } =
        Enum.GetValues<ModuleGroup>().Select(g => (g, (IReadOnlyList<DecoderModel>)Models.Where(m => m.Group == g).ToList())).Where(x => x.Item2.Count > 0).ToList();

    /// <summary>Wie der Typ im Plan gesetzt wird.</summary>
    public static PowerSourceType PlacedAs(DecoderModel m) => m.Role != ModuleRole.Strom ? PowerSourceType.Baustein
        : m.Group == ModuleGroup.Booster ? PowerSourceType.Booster : PowerSourceType.Zentrale;

    public static DecoderModel? ModelOf(SceneryItem s) => s.Kind == SceneryKind.Power ? Find(s.Model) : null;

    /// <summary>Adressbereich eines Bausteins (erste bis letzte Adresse), null ohne Adressen oder Startadresse.</summary>
    public static (int First, int Last)? Range(SceneryItem s)
        => ModelOf(s) is { Addresses: > 0 } m && s.Address is { } a ? (a, a + m.Addresses - 1) : null;

    /// <summary>Symbolgröße: lang und flach, Breite nach Zahl der Anschlüsse.</summary>
    public static (double Width, double Depth) Size(Gauge gauge, DecoderModel m)
    {
        var (w, d) = PowerSupply.Size(gauge);
        var factor = m.Outputs >= 12 ? 1.15 : m.Outputs >= 8 ? 1.0 : m.Outputs >= 4 ? 0.85 : 0.6;
        return (Math.Round(w * factor), Math.Round(d * 0.8));
    }

    /// <summary>Anschlussklemmen an der Unterkante (Ausgänge bzw. Melder-Eingänge), höchstens 16.</summary>
    public static IReadOnlyList<(double X, double Y)> Ports(SceneryItem s)
    {
        var n = Math.Clamp(ModelOf(s)?.Outputs ?? 0, 0, 16);
        return Enumerable.Range(0, n).Select(i => PowerSupply.Local(s, -s.Width / 2 + s.Width * (i + 0.5) / n, s.Depth / 2)).ToList();
    }

    /// <summary>Bezeichnung einer Klemme, z. B. „Ausgang 3“ oder „Melder 5“.</summary>
    public static string PortName(DecoderModel m, int index) => m.Role == ModuleRole.Melden ? Lang.F($"Melder {index + 1}") : Lang.F($"Ausgang {index + 1}");

    /// <summary>Nächste freie Startadresse für einen neuen Baustein dieser Rolle (hinter den vorhandenen Bausteinen und Adressen).</summary>
    public static int? NextStart(LayoutPlan plan, DecoderModel m)
    {
        if (m.Addresses == 0) return null;
        var used = PowerSupply.Modules(plan).Where(s => ModelOf(s)?.Role == m.Role).Select(Range).OfType<(int First, int Last)>().Select(r => r.Last).DefaultIfEmpty(0).Max();
        return used + 1;
    }

    /// <summary>Weichen und Signale (Schaltdecoder) bzw. Rückmelder (Rückmeldebausteine), deren Adresse im Bereich des Bausteins liegt, mit Anschlussnummer.</summary>
    public static IReadOnlyList<(string Label, int Address, int Port)> Assigned(LayoutPlan plan, SceneryItem module)
    {
        if (ModelOf(module) is not { } m || Range(module) is not { } r) return [];
        var list = new List<(string, int, int)>();
        if (m.Role == ModuleRole.Melden)
        {
            foreach (var d in plan.Detectors.Where(d => d.Address is { } a && a >= r.First && a <= r.Last).OrderBy(d => d.Address))
                list.Add((d.Name.Length > 0 ? d.Name : Lang.F($"Rückmelder Nr. {d.Id}"), d.Address!.Value, d.Address!.Value - r.First + 1));
            return list;
        }
        foreach (var i in Electrics.SwitchingItems(plan))
            if (i.Address is { } a)
                for (var k = 0; k < i.AddressCount; k++)
                    if (a + k >= r.First && a + k <= r.Last) list.Add((i.Label, a + k, a + k - r.First + 1));
        return list.OrderBy(x => x.Item2).ToList();
    }

    /// <summary>Baustein, der diese Adresse schaltet bzw. diesen Kontakt meldet.</summary>
    public static SceneryItem? ModuleFor(LayoutPlan plan, int address, ModuleRole role)
        => PowerSupply.Modules(plan).FirstOrDefault(s => ModelOf(s)?.Role == role && Range(s) is { } r && address >= r.First && address <= r.Last);

    /// <summary>Wo eine Adresse angeschlossen ist: „YD8116 1, Ausgang 3“, sonst null.</summary>
    public static string? Location(LayoutPlan plan, int? address, ModuleRole role = ModuleRole.Schalten)
        => address is { } a && ModuleFor(plan, a, role) is { } m && Range(m) is { } r
            ? (role == ModuleRole.Melden ? Lang.F($"{m.Name}, Melder {a - r.First + 1}") : Lang.F($"{m.Name}, Ausgang {a - r.First + 1}"))
            : null;

    /// <summary>Kurzbeschreibung für Listen: „YD8116 · Adressen 1–16“.</summary>
    public static string Summary(SceneryItem s)
    {
        if (ModelOf(s) is not { } m) return s.Model ?? "";
        return Range(s) is { } r
            ? (m.Role == ModuleRole.Melden ? Lang.F($"{m.Id} · Kontakte {r.First}–{r.Last}") : Lang.F($"{m.Id} · Adressen {r.First}–{r.Last}"))
            : m.Id;
    }
}

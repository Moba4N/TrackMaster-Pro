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
/// <see cref="Id"/> ist die Artikel- bzw. Typbezeichnung und über alle Hersteller eindeutig (wird im Plan gespeichert).
/// </summary>
public sealed record DecoderModel(string Maker, string Id, string Name, ModuleGroup Group, ModuleRole Role, int Outputs, int Addresses, string Bus, string Description)
{
    public string Title => $"{Id} – {Name}";

    /// <summary>Kurzname für Symbol und Baustein-Namen: die Typbezeichnung, bei reinen Artikelnummern mit Hersteller („ESU 51830“).</summary>
    public string Short => Id.Any(char.IsLetter) ? Id : $"{Maker} {Id}";
}

/// <summary>
/// Digital-Decoder und Module der gängigen Hersteller als einzelne Bausteine für den Plan. Die Angaben stammen aus
/// Herstellerunterlagen und Händlerlisten (Stand November 2026); Anschlüsse und Adressen sind die üblichen Werte, am Gerät einstellbar.
/// Ein Baustein im Plan ist ein Symbol der Stromversorgung (<see cref="PowerSourceType.Baustein"/>) mit
/// <see cref="SceneryItem.Model"/> = Typ und <see cref="SceneryItem.Address"/> = Startadresse.
/// Zentralen und Booster werden als <see cref="PowerSourceType.Zentrale"/> bzw. <see cref="PowerSourceType.Booster"/> gesetzt.
/// </summary>
public static class Decoders
{
    private const ModuleGroup Mag = ModuleGroup.Magnetartikel, Srv = ModuleGroup.Servo, Sch = ModuleGroup.Schalten, Rm = ModuleGroup.Rueckmelder,
        Zen = ModuleGroup.Zentrale, Boo = ModuleGroup.Booster, Son = ModuleGroup.Sondermodul, Zub = ModuleGroup.Zubehoer;

    private static DecoderModel D(string maker, string id, string name, ModuleGroup g, int outputs, int addresses, string bus, string description)
        => new(maker, id, name, g, g switch
        {
            Zen or Boo => ModuleRole.Strom,
            Rm when addresses > 0 => ModuleRole.Melden,
            _ when addresses > 0 => ModuleRole.Schalten,
            _ => ModuleRole.Keine,
        }, outputs, addresses, bus, description);

    /// <summary>Gerät ohne eigene Adressen und ohne Stromquelle (Adapter, Netzteil, Erweiterung).</summary>
    private static DecoderModel X(string maker, string id, string name, ModuleGroup g, int outputs, string bus, string description)
        => new(maker, id, name, g, ModuleRole.Keine, outputs, 0, bus, description);

    public static IReadOnlyList<DecoderModel> Models { get; } =
    [
        // ── neutral (ohne Hersteller, für „Melder verkabeln“) ──
        D("Allgemein", "Besetztmelder", Lang.T("Besetztmelder (16 Eingänge, neutral)"), Rm, 16, 16, "s88, LocoNet, R-Bus",
            Lang.T("Neutraler Gleisbesetztmelder mit 16 Eingängen. Oben Bahnstrom (B) und Masse (0) von Booster bzw. Zentrale, unten die Ausgänge zu den Meldeabschnitten.")),

        X("Allgemein", "Kehrschleifenmodul", Lang.T("Kehrschleifenmodul (neutral)"), Son, 2, "–",
            Lang.T("Polt den Kehrschleifenabschnitt automatisch um (Zweileiter). Oben Bahnstrom (B) und Masse (0) von Booster bzw. Zentrale, unten die beiden Ausgänge zur Kehrschleife.")),

        // ── ESU ──
        D("ESU", "51830", Lang.T("SwitchPilot 3 (4× Magnetartikel, 2× Servo)"), Mag, 6, 6, "DCC, Motorola",
            Lang.T("4 Ausgänge für Doppelspulenantriebe oder Licht und 2 Servoanschlüsse.")),
        D("ESU", "51831", Lang.T("SwitchPilot 3 Plus (8× Magnetartikel)"), Mag, 8, 8, "DCC, Motorola",
            Lang.T("8 Ausgänge für Doppelspulenantriebe, Motorantriebe oder Licht.")),
        D("ESU", "51832", Lang.T("SwitchPilot 3 Servo (8× Servo)"), Srv, 8, 8, "DCC, Motorola, RailCom",
            Lang.T("8 Servos für Weichen, Schranken oder Türen, mit RailCom-Rückmeldung.")),
        D("ESU", "50094", Lang.T("ECoSDetector (16× Rückmelder)"), Rm, 16, 16, "ECoSlink",
            Lang.T("16 Rückmeldeeingänge mit Stromfühlern am ECoSlink-Bus.")),
        D("ESU", "50210", Lang.T("ECoS (Zentrale)"), Zen, 1, 0, "ECoSlink, LAN",
            Lang.T("Digitalzentrale mit Farbbildschirm für DCC, Motorola, M4 und Selectrix.")),
        D("ESU", "50010", Lang.T("ECoSBoost 4 A"), Boo, 1, 0, "ECoSlink",
            Lang.T("Booster mit 4 A für einen weiteren Stromkreis.")),
        D("ESU", "50011", Lang.T("ECoSBoost 8 A"), Boo, 1, 0, "ECoSlink",
            Lang.T("Booster mit 8 A für Großspuren und große Stromkreise.")),

        // ── Märklin ──
        D("Märklin", "60832", Lang.T("Decoder m83 (4× Magnetartikel)"), Mag, 4, 4, "Motorola, DCC",
            Lang.T("4 Weichen oder Formsignale mit Doppelspulenantrieb, dazu 4 Tastereingänge.")),
        D("Märklin", "60842", Lang.T("Decoder m84 (4× Schalten)"), Sch, 4, 4, "Motorola, DCC",
            Lang.T("4 Schaltausgänge mit Relais für Licht, Entkuppler und Lichtsignale.")),
        D("Märklin", "60883", Lang.T("Link s88 (16× Rückmelder)"), Rm, 16, 16, "CAN, s88",
            Lang.T("16 Rückmeldeeingänge, dazu Anschluss für weitere s88-Module (Central Station 2/3).")),
        D("Märklin", "60881", Lang.T("s88 AC (16× Rückmelder)"), Rm, 16, 16, "s88",
            Lang.T("16 Eingänge gegen Masse für Kontaktgleise (Mittelleiter).")),
        D("Märklin", "60882", Lang.T("s88 DC (16× Rückmelder)"), Rm, 16, 16, "s88",
            Lang.T("16 Eingänge für Gleisbesetztmelder im Zweileitergleis.")),
        D("Märklin", "60226", Lang.T("Central Station 3"), Zen, 1, 0, "CAN, LAN",
            Lang.T("Digitalzentrale mit Touch-Bildschirm für mfx, Motorola und DCC.")),
        D("Märklin", "60216", Lang.T("Central Station 3plus"), Zen, 1, 0, "CAN, LAN, s88",
            Lang.T("Central Station 3 mit eingebautem s88-Anschluss und als Zweitgerät verwendbar.")),
        D("Märklin", "60175", Lang.T("Booster"), Boo, 1, 0, "CAN",
            Lang.T("Booster für einen weiteren Stromkreis an der Central Station.")),

        // ── Roco / Fleischmann (Z21) ──
        D("Roco", "10836", Lang.T("Z21 switch DECODER (8× Magnetartikel)"), Mag, 8, 8, "DCC, RailCom",
            Lang.T("8 Weichen mit Doppelspulen- oder Motorantrieb, mit RailCom-Rückmeldung.")),
        D("Roco", "10837", Lang.T("Z21 signal DECODER (8× Lichtsignal)"), Sch, 8, 8, "DCC, RailCom",
            Lang.T("Lichtsignaldecoder für bis zu 8 Signale mit vorbildgetreuen Signalbildern.")),
        D("Roco", "10775", Lang.T("Weichendecoder (4× Magnetartikel)"), Mag, 4, 4, "DCC",
            Lang.T("4 Weichen mit Doppelspulenantrieb.")),
        D("Roco", "10808", Lang.T("Z21 DETECTOR (8× Rückmelder, RailCom)"), Rm, 8, 8, "CAN, R-Bus",
            Lang.T("8 Gleisbesetztmelder mit RailCom (erkennt die Lokadresse).")),
        D("Roco", "10787", Lang.T("Rückmeldemodul (8× Rückmelder)"), Rm, 8, 8, "R-Bus",
            Lang.T("8 Eingänge für Gleiskontakte oder Besetztmelder am R-Bus.")),
        D("Roco", "10820", Lang.T("Z21 (Zentrale)"), Zen, 1, 0, "LAN, R-Bus, CAN",
            Lang.T("Digitalzentrale für DCC und Motorola, Steuerung per App und Handregler.")),
        D("Roco", "10870", Lang.T("Z21 XL (Zentrale)"), Zen, 1, 0, "LAN, R-Bus, CAN",
            Lang.T("Z21 mit höherem Fahrstrom für Großspuren.")),
        D("Roco", "10806", Lang.T("Z21 single BOOSTER"), Boo, 1, 0, "CAN, B-Bus",
            Lang.T("Booster mit 3 A für einen weiteren Stromkreis.")),
        D("Roco", "10807", Lang.T("Z21 dual BOOSTER"), Boo, 1, 0, "CAN, B-Bus",
            Lang.T("Booster mit zwei getrennten Ausgängen für zwei Stromkreise.")),

        // ── Lenz ──
        D("Lenz", "LS100", Lang.T("Schaltdecoder 4× mit Rückmeldung"), Mag, 4, 4, "DCC, RS-Bus",
            Lang.T("4 Weichen mit Doppelspulenantrieb, meldet die Lage über den RS-Bus.")),
        D("Lenz", "LS150", Lang.T("Schaltdecoder 6×"), Mag, 6, 6, "DCC",
            Lang.T("6 Weichen mit Doppelspulenantrieb oder 12 Ausgänge.")),
        D("Lenz", "LR101", Lang.T("Rückmeldemodul (8× Rückmelder)"), Rm, 8, 8, "RS-Bus",
            Lang.T("8 Eingänge für Gleiskontakte oder Besetztmelder am RS-Bus.")),
        D("Lenz", "LZV200", Lang.T("Zentrale LZV200"), Zen, 1, 0, "XpressNet, RS-Bus",
            Lang.T("Digitalzentrale mit Verstärker für DCC.")),
        D("Lenz", "LV103", Lang.T("Verstärker LV103"), Boo, 1, 0, "CDE",
            Lang.T("Booster für einen weiteren Stromkreis.")),

        // ── Viessmann ──
        D("Viessmann", "5211", Lang.T("Magnetartikeldecoder (4× Motorola)"), Mag, 4, 4, "Motorola",
            Lang.T("4 Weichen oder Formsignale mit Doppelspulenantrieb.")),
        D("Viessmann", "5212", Lang.T("Magnetartikeldecoder (4× DCC)"), Mag, 4, 4, "DCC",
            Lang.T("4 Weichen oder Formsignale mit Doppelspulenantrieb.")),
        D("Viessmann", "5280", Lang.T("Weichen- und Schaltdecoder"), Sch, 4, 4, "DCC, Motorola",
            Lang.T("4 Ausgänge für Weichen, Licht und Schaltaufgaben.")),
        D("Viessmann", "5300", Lang.T("Commander (Zentrale)"), Zen, 1, 0, "DCC, Motorola, LAN",
            Lang.T("Digitalzentrale mit Bildschirm für Fahren, Schalten und Stellwerk.")),

        // ── Uhlenbrock ──
        D("Uhlenbrock", "67600", Lang.T("Schaltdecoder (4× Relais)"), Sch, 4, 4, "DCC, Motorola",
            Lang.T("4 Relaisausgänge für Licht, Entkuppler und Lichtsignale.")),
        D("Uhlenbrock", "63320", Lang.T("LocoNet-Rückmelder 2-Leiter (16×)"), Rm, 16, 16, "LocoNet",
            Lang.T("16 Gleisbesetztmelder für das Zweileitergleis.")),
        D("Uhlenbrock", "63330", Lang.T("LocoNet-Rückmelder 3-Leiter (16×)"), Rm, 16, 16, "LocoNet",
            Lang.T("16 Eingänge gegen Masse für das Mittelleitergleis.")),
        D("Uhlenbrock", "65100", Lang.T("Intellibox II (Zentrale)"), Zen, 1, 0, "LocoNet, s88",
            Lang.T("Digitalzentrale für DCC, Motorola und Selectrix.")),

        // ── Digikeijs / Digirails ──
        D("Digikeijs", "DR4018", Lang.T("16× Schalten und Signale"), Sch, 16, 16, "DCC",
            Lang.T("16 Ausgänge für Weichen, Lichtsignale und Licht.")),
        D("Digikeijs", "DR4024", Lang.T("4× Servo und 4× Schalten"), Srv, 8, 8, "DCC",
            Lang.T("4 Servos und 4 Schaltausgänge, z. B. für Herzstückpolarisation.")),
        D("Digikeijs", "DR4088LN-CS", Lang.T("16× Stromfühler, LocoNet"), Rm, 16, 16, "LocoNet",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern.")),
        D("Digikeijs", "DR4088LN-OPTO", Lang.T("16× Optokoppler, LocoNet"), Rm, 16, 16, "LocoNet",
            Lang.T("16 Eingänge über Optokoppler für Kontaktgleise und Taster.")),
        D("Digikeijs", "DR5000", Lang.T("DR5000 (Zentrale)"), Zen, 1, 0, "LocoNet, XpressNet, R-Bus, s88",
            Lang.T("Mehrprotokoll-Zentrale mit vielen Bus-Anschlüssen.")),
        D("Digikeijs", "DR5033", Lang.T("Booster 3 A"), Boo, 1, 0, "LocoNet-B, CDE",
            Lang.T("Booster mit 3 A für einen weiteren Stromkreis.")),

        // ── Littfinski (LDT) ──
        D("LDT", "S-DEC-4", Lang.T("4× Magnetartikel"), Mag, 4, 4, "DCC, Motorola",
            Lang.T("4 Weichen oder Formsignale mit Doppelspulenantrieb.")),
        D("LDT", "M-DEC", Lang.T("4× Motorweiche"), Mag, 4, 4, "DCC, Motorola",
            Lang.T("4 Weichen mit motorischem Antrieb.")),
        D("LDT", "SA-DEC-4", Lang.T("4× Schalten mit Relais"), Sch, 4, 4, "DCC, Motorola",
            Lang.T("4 bistabile Relais für Licht, Abschaltgleise und Polarisation.")),
        D("LDT", "Light-DEC", Lang.T("Lichtsignaldecoder (4 Signale)"), Sch, 16, 8, "DCC, Motorola",
            Lang.T("16 Ausgänge für bis zu 4 Lichtsignale mit weichem Überblenden.")),
        D("LDT", "RM-88-N", Lang.T("16× Rückmelder, s88-N"), Rm, 16, 16, "s88-N",
            Lang.T("16 Eingänge für Kontaktgleise und Schaltkontakte.")),
        D("LDT", "RM-GB-8-N", Lang.T("8× Gleisbesetztmelder, s88-N"), Rm, 8, 8, "s88-N",
            Lang.T("8 Gleisbesetztmelder mit Stromfühlern.")),

        // ── Tams ──
        D("Tams", "WD-34.2", Lang.T("4× Magnetartikel"), Mag, 4, 4, "DCC, Motorola",
            Lang.T("4 Weichen oder Formsignale mit Doppelspulenantrieb.")),
        D("Tams", "SD-34.2", Lang.T("4× Schalten"), Sch, 4, 4, "DCC, Motorola",
            Lang.T("4 Schaltausgänge für Licht und Zubehör.")),
        D("Tams", "s88-3", Lang.T("16× Rückmelder, s88"), Rm, 16, 16, "s88",
            Lang.T("16 Eingänge für Kontaktgleise und Schaltkontakte.")),
        D("Tams", "mc2", Lang.T("MasterControl.2 (Zentrale)"), Zen, 1, 0, "LAN, s88, XpressNet",
            Lang.T("Mehrprotokoll-Zentrale mit Netzwerkanschluss.")),
        D("Tams", "B-4", Lang.T("Booster B-4"), Boo, 1, 0, "CDE",
            Lang.T("Booster mit Anzeige für einen weiteren Stromkreis.")),

        // ── Qdecoder ──
        D("Qdecoder", "Z1-8", Lang.T("8× Schalten und Signale"), Sch, 8, 8, "DCC, Motorola",
            Lang.T("8 Ausgänge für Lichtsignale, Licht und Weichen.")),
        D("Qdecoder", "Z1-16+", Lang.T("16× Schalten und Signale"), Sch, 16, 16, "DCC, Motorola",
            Lang.T("16 Ausgänge für Lichtsignale, Licht und Weichen.")),
        D("Qdecoder", "Z2-8", Lang.T("8× Lichtsignale"), Sch, 8, 8, "DCC, Motorola",
            Lang.T("Signaldecoder für vorbildgetreue Lichtsignalbilder.")),

        // ── Digitrax ──
        D("Digitrax", "DS64", Lang.T("4× Magnetartikel"), Mag, 4, 4, "DCC, LocoNet",
            Lang.T("4 Weichen mit Doppelspulen- oder Motorantrieb.")),
        D("Digitrax", "DS74", Lang.T("4× Magnetartikel"), Mag, 4, 4, "DCC, LocoNet",
            Lang.T("4 Weichen mit Doppelspulen- oder Motorantrieb.")),
        D("Digitrax", "DS78V", Lang.T("8× Servo"), Srv, 8, 8, "DCC, LocoNet",
            Lang.T("8 Servos für Weichen.")),
        D("Digitrax", "BDL168", Lang.T("16× Gleisbesetztmelder"), Rm, 16, 16, "LocoNet",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern.")),
        D("Digitrax", "BXP88", Lang.T("8× Gleisbesetztmelder"), Rm, 8, 8, "LocoNet",
            Lang.T("8 Gleisbesetztmelder mit Stromfühlern.")),
        D("Digitrax", "DCS240", Lang.T("DCS240 (Zentrale)"), Zen, 1, 0, "LocoNet",
            Lang.T("DCC-Zentrale mit Verstärker.")),
        D("Digitrax", "DB210", Lang.T("Booster 5/8 A"), Boo, 1, 0, "LocoNet",
            Lang.T("Booster für einen weiteren Stromkreis.")),

        // ── NCE ──
        D("NCE", "Switch8", Lang.T("8× Magnetartikel"), Mag, 8, 8, "DCC",
            Lang.T("8 Weichen mit Doppelspulen- oder Motorantrieb.")),
        D("NCE", "Switch-It", Lang.T("2× Magnetartikel"), Mag, 2, 2, "DCC",
            Lang.T("2 Weichen mit Doppelspulen- oder Motorantrieb.")),
        D("NCE", "PowerPro", Lang.T("Power Pro (Zentrale)"), Zen, 1, 0, "Cab-Bus",
            Lang.T("DCC-Zentrale mit Verstärker.")),

        // ── YaMoRC ──
        // Weichendecoder für Doppelspulen-Antriebe
        D("YaMoRC", "YD8008", Lang.T("8× Magnetartikel mit CDU"), Mag, 8, 8, "DCC",
            Lang.T("Weichendecoder für 8 Doppelspulenantriebe (je 9,5 A Impuls) mit eingebautem Kondensator-Speicher (CDU).")),
        D("YaMoRC", "YD8044", Lang.T("4× Magnetartikel mit CDU und Herzstückpolarisation"), Mag, 4, 4, "DCC",
            Lang.T("Weichendecoder für 4 Doppelspulenantriebe mit CDU, dazu je ein Relais für die Herzstückpolarisation.")),
        D("YaMoRC", "YD8248", Lang.T("4× Servo und 8× Signal/Schalten"), Srv, 12, 12, "DCC",
            Lang.T("4 Servos für Weichen, Schranken oder Türen und 8 Schaltausgänge für Signale oder Licht.")),
        X("YaMoRC", "YD8291", Lang.T("4× Servo-Erweiterung für YD8248"), Srv, 4, "ES-Link",
            Lang.T("Erweitert den YD8248 um 4 weitere Servoanschlüsse.")),
        X("YaMoRC", "YD8290", Lang.T("4× Relais-Erweiterung für YD8248"), Srv, 4, "ES-Link",
            Lang.T("4 Relais am YD8248, z. B. für Herzstückpolarisation bei Servoweichen.")),
        D("YaMoRC", "YD8116", Lang.T("16× Signal/Schalten"), Sch, 16, 16, "DCC",
            Lang.T("16 Ausgänge (je 2,5 A, zusammen 5 A) für Lichtsignale, Beleuchtung, Motorantriebe und Schaltaufgaben.")),
        X("YaMoRC", "YD6940", Lang.T("Motorantrieb-Adapter (2 Stück)"), Sch, 2, "–",
            Lang.T("Adapter für Motorantriebe mit Polwechsel (z. B. Tortoise, Cobalt) am YD8116.")),
        X("YaMoRC", "YD6942", Lang.T("Adapter für Signale mit gemeinsamer Kathode"), Sch, 2, "–",
            Lang.T("Plus/Minus-Wandler für Lichtsignale mit gemeinsamer Kathode am YD8116.")),
        D("YaMoRC", "YD6016LN-CS", Lang.T("16× Stromfühler, LocoNet"), Rm, 16, 16, "LocoNet, s88",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern (2 Gruppen à 8), meldet über LocoNet oder s88.")),
        D("YaMoRC", "YD6016LN-GND", Lang.T("16× Masse-Eingang, LocoNet"), Rm, 16, 16, "LocoNet, s88",
            Lang.T("16 Eingänge gegen Masse für Schaltgleise, Reedkontakte und Taster, meldet über LocoNet oder s88.")),
        D("YaMoRC", "YD6016LN-RC", Lang.T("16× RailCom-Melder, LocoNet"), Rm, 16, 16, "LocoNet, RailCom",
            Lang.T("16 Besetztmelder mit RailCom (erkennt die Lokadresse) und ein globaler RailCom-Melder.")),
        D("YaMoRC", "YD6016ES-CS", Lang.T("16× Stromfühler, s88"), Rm, 16, 16, "s88N",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern am s88-Bus.")),
        D("YaMoRC", "YD6016ES-GND", Lang.T("16× Masse-Eingang, s88"), Rm, 16, 16, "s88N",
            Lang.T("16 Eingänge gegen Masse am s88-Bus, z. B. für Märklin-Kontaktgleise.")),
        D("YaMoRC", "YD6016RB-CS", Lang.T("16× Stromfühler, R-Bus"), Rm, 16, 16, "R-Bus, s88",
            Lang.T("16 Gleisbesetztmelder mit Stromfühlern für den Roco/Fleischmann R-Bus (Z21).")),
        X("YaMoRC", "YD6903-S88", Lang.T("s88-auf-s88N-Adapter"), Rm, 1, "s88, s88N",
            Lang.T("Verbindet klassische s88-Module mit dem s88N-Netzwerkkabel (RJ45).")),
        D("YaMoRC", "YD7010", Lang.T("Digitalzentrale mit WLAN"), Zen, 1, 0, "LAN/WLAN",
            Lang.T("DCC-Zentrale mit WLAN und LAN für Apps und Steuerprogramme.")),
        D("YaMoRC", "YD7001", Lang.T("Multi-Protokoll-Zentrale (LAN)"), Zen, 1, 0, "LAN",
            Lang.T("Zentrale mit mehreren LAN-Protokollen für gängige Steuerprogramme.")),
        X("YaMoRC", "YD9401", Lang.T("Netzwerk-Modul"), Zen, 1, "LAN/WLAN",
            Lang.T("LAN/WLAN-Nachrüstmodul (z. B. für die Digikeijs DR5000).")),
        X("YaMoRC", "YD7460-15", Lang.T("Netzteil 15 V"), Zen, 1, "–",
            Lang.T("Netzteil 15 V / 60 W für Zentrale und Booster (N, TT).")),
        X("YaMoRC", "YD7460-18", Lang.T("Netzteil 18 V"), Zen, 1, "–",
            Lang.T("Netzteil 18 V / 60 W für Zentrale und Booster (H0).")),
        D("YaMoRC", "YD7403", Lang.T("Booster 3,5 A, galvanisch getrennt"), Boo, 1, 0, "LocoNet-B, B-Bus, CDE",
            Lang.T("Universeller Booster mit 3,5 A für einen weiteren Stromkreis, galvanisch getrennt.")),
        X("YaMoRC", "YD7039", Lang.T("LocoNet-auf-CDE-Adapter"), Boo, 1, "LocoNet, CDE",
            Lang.T("Führt das Boostersignal von LocoNet auf die CDE-Klemmen.")),
        X("YaMoRC", "YD7432", Lang.T("Kehrschleifenmodul"), Son, 2, "–",
            Lang.T("Polt den Kehrschleifenabschnitt automatisch um (Zweileiter-Gleichstrom).")),
        X("YaMoRC", "YD7652", Lang.T("Drehscheibenmodul"), Son, 1, "LocoNet, RailCom",
            Lang.T("Steuert Drehscheiben über Weichenbefehle, mit RailCom-Melder auf der Bühne.")),
        X("YaMoRC", "YD7652EX-TURN", Lang.T("Drehscheiben-Erweiterung"), Son, 1, "ES-Link",
            Lang.T("Zusatzmodul für ältere Roco- und Märklin-Drehscheiben.")),
        X("YaMoRC", "YD7652ES-OPTO", Lang.T("Positionssensor für Drehscheiben"), Son, 1, "ES-Link",
            Lang.T("Optische Positionserkennung der Bühne für das Drehscheibenmodul.")),
        X("YaMoRC", "YD7652ST-STEP", Lang.T("Schrittmotor-Treiber für Drehscheiben"), Son, 1, "ES-Link",
            Lang.T("Schrittmotor-Antrieb für das Drehscheibenmodul.")),
        X("YaMoRC", "YD9100", Lang.T("USB-Konfigurationsstick"), Zub, 1, "ES-Link, USB",
            Lang.T("Programmiert YaMoRC-Bausteine am PC über ES-Link.")),
        X("YaMoRC", "YD6902ES-EXT", Lang.T("ES-Link-Verlängerung (RJ45)"), Zub, 2, "ES-Link",
            Lang.T("Ein Paar Adapter, um ES-Link über ein Netzwerkkabel zu verlängern.")),
    ];

    /// <summary>Hersteller in Menü-Reihenfolge (alphabetisch), je mit ihren Bausteinen nach Gruppen.</summary>
    public static IReadOnlyList<(string Maker, IReadOnlyList<(ModuleGroup Group, IReadOnlyList<DecoderModel> Models)> Groups)> Makers { get; } =
        Models.Select(m => m.Maker).Distinct().OrderBy(m => m, StringComparer.CurrentCultureIgnoreCase)
            .Select(mk => (mk, (IReadOnlyList<(ModuleGroup, IReadOnlyList<DecoderModel>)>)Enum.GetValues<ModuleGroup>()
                .Select(g => (g, (IReadOnlyList<DecoderModel>)Models.Where(m => m.Maker == mk && m.Group == g).ToList())).Where(x => x.Item2.Count > 0).ToList()))
            .ToList();

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

    /// <summary>Rückmeldebaustein: Klemmen an der Oberkante für Bahnstrom (B) und Masse (0) von Booster bzw. Zentrale, sonst null.</summary>
    public static ((double X, double Y) Track, (double X, double Y) Ground)? FeedTerminals(SceneryItem s)
        => ModelOf(s) is { } m && (m.Role == ModuleRole.Melden || m.Id is "Kehrschleifenmodul" or "YD7432") ? (PowerSupply.Local(s, -s.Width / 6, -s.Depth / 2), PowerSupply.Local(s, s.Width / 6, -s.Depth / 2)) : null;

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
            ? (m.Role == ModuleRole.Melden ? Lang.F($"{m.Short} · Kontakte {r.First}–{r.Last}") : Lang.F($"{m.Short} · Adressen {r.First}–{r.Last}"))
            : m.Short;
    }
}

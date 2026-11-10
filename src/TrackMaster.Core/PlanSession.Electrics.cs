namespace TrackMaster.Core;

/// <summary>Ein Gleisende mit Trennstelle (ja/nein) und ob dort ein anderes Gleis anschließt.</summary>
public sealed record PieceEnd(int Index, bool Isolated, bool Connected);

/// <summary>Elektrik am Gleis: Stromart, Anschlüsse (Einspeisung, Trennstellen, Rückmelder), Kabel und der Sprung zu Befunden.</summary>
public sealed partial class PlanSession
{
    /// <summary>
    /// Ist die Elektrik markiert (<see cref="ShowSections"/>), werden die übrigen Beschriftungen (Höhen, Artikelnummern)
    /// ausgeblendet und stattdessen die Abschnitte beschriftet.
    /// </summary>
    public bool ElectricLabelsOnly { get; set; } = true;

    /// <summary>Höhen an den Gleisen zeigen (aus, solange nur die Elektrik beschriftet wird).</summary>
    public bool HeightsVisible => ShowHeights && !ElectricLabelsActive;

    /// <summary>Artikelnummern an den Gleisen zeigen (aus, solange nur die Elektrik beschriftet wird).</summary>
    public bool LabelsVisible => ShowLabels && !ElectricLabelsActive;

    /// <summary>Elektrik markiert und nur deren Beschriftung gewünscht.</summary>
    public bool ElectricLabelsActive => ShowSections && ElectricLabelsOnly;

    /// <summary>Verwendung für das Werkzeug „Kabel verlegen“.</summary>
    public WireUse WireUse { get; set; } = WireUse.Fahrstrom;

    // ---------- Befund zeigen ----------

    /// <summary>Befund, zu dem zuletzt gesprungen wurde; die Zeichenfläche hebt seine Gleise hervor, solange er in <see cref="Issues"/> steht.</summary>
    public PlanIssue? FocusedIssue { get; private set; }

    /// <summary>Gleise des hervorgehobenen Befunds (leer, wenn keiner aktiv ist).</summary>
    public IReadOnlyList<int> FocusPieces => FocusedIssue is { } f && Issues.Contains(f) ? f.PieceIds : [];

    /// <summary>
    /// Springt zu einem Befund: blendet die Ebenen der betroffenen Gleise ein, wählt die Gleise (bzw. das Kabel) und merkt sich den Befund
    /// zum Hervorheben. Liefert den Bereich, den die Ansicht zeigen soll (mit Rand, mindestens etwa 25 Gleisbreiten groß).
    /// </summary>
    public (double X0, double Y0, double X1, double Y1) FocusIssue(PlanIssue issue)
    {
        var pieces = issue.PieceIds.Select(Plan.Find).OfType<PlacedPiece>().ToList();
        var levels = pieces.Select(p => p.Level).ToHashSet();
        if (issue.ObjectId is { } oid && Plan.Scenery.FirstOrDefault(s => s.Id == oid) is { } obj) levels.Add(obj.Level);
        var shown = false;
        foreach (var l in levels.Where(l => l >= 0 && l < Plan.Levels.Count && !Plan.Levels[l].Visible))
        {
            Plan.Levels[l].Visible = true;
            shown = true;
        }
        if (shown) Dirty = true;
        FocusedIssue = issue;
        Active = null;
        Selection.Clear();
        SelectedObject = null;
        if (pieces.Count > 0) foreach (var p in pieces) Selection.Add(p.Id);
        else if (issue.ObjectId is { } id && FindObject(id) is not null) SelectedObject = id;
        if (shown) Commit(); else Changed?.Invoke();

        double x0 = issue.X, y0 = issue.Y, x1 = issue.X, y1 = issue.Y;
        foreach (var p in pieces)
            foreach (var leg in p.WorldLegs())
                foreach (var t in new[] { 0, 0.5, 1 })
                {
                    var q = leg.At(t);
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
        if (issue.ObjectId is { } wid && Plan.Scenery.FirstOrDefault(s => s.Id == wid) is { } item)
            foreach (var (x, y) in item.Outline()) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        var min = Plan.Gauge.TrackWidth * 25;
        var pad = Math.Max(Plan.Gauge.TrackWidth * 4, Math.Max(x1 - x0, y1 - y0) * 0.08);
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double hw = Math.Max((x1 - x0) / 2 + pad, min / 2), hh = Math.Max((y1 - y0) / 2 + pad, min / 2);
        if (shown) Message?.Invoke(Lang.T("Die Ebene des Befunds wurde dafür eingeblendet."));
        return (cx - hw, cy - hh, cx + hw, cy + hh);
    }

    /// <summary>Hervorhebung des Befunds beenden.</summary>
    public void ClearFocus()
    {
        if (FocusedIssue is null) return;
        FocusedIssue = null;
        Changed?.Invoke();
    }

    // ---------- Stromart ----------

    public void SetCurrent(CurrentKind kind)
    {
        if (Plan.Current == kind) return;
        Record();
        Plan.Current = kind;
        Commit();
        Say(Lang.F($"Stromart: {Electrics.CurrentName(kind)}") + (kind == CurrentKind.Auto ? Lang.F($" – erkannt: {Electrics.CurrentName(Electrics.Current(Plan))}.") : "."));
    }

    // ---------- Anschlüsse am Gleis ----------

    /// <summary>Das eine gewählte Gleis (für Kontextmenü und Ribbon „Anschlüsse“), sonst null.</summary>
    public PlacedPiece? ConnectionPiece => Selection.Count == 1 ? Plan.Find(Selection.First()) : null;

    public IReadOnlyList<Feeder> FeedersOn(int pieceId) => Plan.Feeders.Where(f => f.PieceId == pieceId).ToList();

    public Detector? DetectorOn(int pieceId) => Plan.Detectors.FirstOrDefault(d => d.PieceId == pieceId);

    /// <summary>Gleisenden mit Trennstelle (auch wenn sie am verbundenen Nachbargleis eingetragen ist).</summary>
    public IReadOnlyList<PieceEnd> PieceEnds(int pieceId)
    {
        if (Plan.Find(pieceId) is not { } p) return [];
        var n = p.WorldConnectors().Count;
        var list = new List<PieceEnd>();
        for (var i = 0; i < n; i++)
        {
            var c = Connections.Find(pieceId, i);
            var iso = Plan.Isolations.Any(x => (x.PieceId == pieceId && x.Index == i) || (c?.Link is { } l && x.PieceId == l.PieceId && x.Index == l.Index));
            list.Add(new PieceEnd(i, iso, c?.Link is not null));
        }
        return list;
    }

    /// <summary>Stromkreise: die Fahrstromquellen im Plan (Zentrale, Booster, Trafo), dazu Stromkreise älterer Gleisanschlüsse ohne Symbol.</summary>
    public IReadOnlyList<string> CircuitNames
    {
        get
        {
            var list = PowerSupply.TrackSources(Plan).Select(s => s.Name)
                .Concat(Plan.Feeders.Select(f => f.Circuit)).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            return list.Count == 0 ? [DefaultSourceName] : list;
        }
    }

    /// <summary>Name der Quelle, die ein neuer Gleisanschluss bekommt, wenn noch keine im Plan steht.</summary>
    private string DefaultSourceName => PowerSupply.NewName(Plan, Plan.Operation == PowerMode.Analog ? PowerSourceType.Fahrtrafo : PowerSourceType.Zentrale);

    // ---------- Stromquellen: Zentrale, Booster, Trafo ----------

    /// <summary>Art der Quelle für das Werkzeug „Trafo / Zentrale“.</summary>
    public PowerSourceType PowerType { get => _powerType; set { _powerType = value; ModuleModel = null; } }
    private PowerSourceType _powerType = PowerSourceType.Zentrale;

    /// <summary>Decoder-Baustein, den das Werkzeug „Stromversorgung“ als Nächstes setzt (Typ aus dem Decoder-Katalog), sonst null.</summary>
    public string? ModuleModel { get; private set; }

    /// <summary>Werkzeug auf einen Decoder-Baustein stellen; ein Klick in den Plan setzt ihn.</summary>
    public void SelectModule(string id)
    {
        if (Decoders.Find(id) is not { } m) return;
        PowerType = Decoders.PlacedAs(m);
        ModuleModel = m.Id;
    }

    /// <summary>Decoder-Baustein setzen, mit der nächsten freien Startadresse.</summary>
    public SceneryItem? AddModule(string id, double x, double y)
    {
        if (Decoders.Find(id) is not { } m) return null;
        var type = Decoders.PlacedAs(m);
        SceneryItem item;
        if (type == PowerSourceType.Baustein) item = DetectorWiring.NewModule(Plan, m, x, y, Decoders.NextStart(Plan, m));
        else
        {
            item = PowerSupply.Create(Plan, type, x, y);
            item.Model = m.Id;
        }
        item = AddScenery(item);
        Say(Lang.F($"{m.Maker} {m.Title} „{item.Name}“ gesetzt.") + " " + (Decoders.Range(item) is { } r
            ? (m.Role == ModuleRole.Melden ? Lang.F($"Rückmeldekontakte {r.First}–{r.Last}: Rückmelder mit diesen Kontakten gehören zu diesem Baustein.")
                : Lang.F($"Adressen {r.First}–{r.Last}: Weichen und Signale mit diesen Adressen hängen an diesem Baustein."))
            : m.Description));
        return item;
    }

    /// <summary>
    /// Rückmelder automatisch mit Rückmeldemodulen verkabeln (an) bzw. diese Kabel entfernen (aus). Eingeschaltet bekommt jeder
    /// Rückmelder mit Kontakt ein Kabel zur passenden Klemme; fehlende Module werden unter dem Plan angelegt. Die Module bleiben beim Ausschalten stehen.
    /// </summary>
    public void SetDetectorWiring(bool on)
    {
        if (Plan.DetectorWiring == on) return;
        Record();
        Plan.DetectorWiring = on;
        if (on)
        {
            // Melder ohne Quelle bekommen Zentrale bzw. Booster ihres Abschnitts
            var r = DetectorWiring.Sync(Plan);
            Commit();
            var withContact = Plan.Detectors.Count(d => d.Address is not null);
            Say(withContact == 0
                ? Lang.T("Rückmelder werden automatisch verkabelt, sobald sie einen Kontakt haben.")
                : Lang.F($"Melder verkabelt: {r.Added} Kabel verlegt, {r.Modules} Besetztmelder angelegt. Bahnstrom (B) und Masse (0) kommen von Booster bzw. Zentrale. Ändert sich Kontakt, Quelle oder Gleis, folgen die Kabel."));
            return;
        }
        var (n, mods) = DetectorWiring.RemoveAll(Plan);
        Commit();
        Say(Lang.F($"{n} Kabel und {mods} automatisch angelegte Besetztmelder entfernt. Selbst gesetzte Rückmeldemodule bleiben im Plan."));
    }

    /// <summary>
    /// Automatisch verkabeln (siehe <see cref="AutoWiring"/>): Strom und Masse für jeden Abschnitt, Booster bei großen Anlagen,
    /// Kehrschleifenmodule bei Gleichstrom. Ein zweiter Lauf ersetzt das vorher automatisch Angelegte.
    /// </summary>
    public void AutoWire()
    {
        if (Plan.Pieces.Count == 0) { Say(Lang.T("Noch keine Gleise im Plan.")); return; }
        Record();
        var r = AutoWiring.Build(Plan);
        if (r.Detectors > 0) ShowWires = true;
        Commit();
        var power = Plan.Operation == PowerMode.Analog ? Lang.T("Der Fahrtrafo speist alle Abschnitte.")
            : r.CentralFeeds ? Lang.T("Kleine Anlage: die Zentrale speist alle Abschnitte.")
            : Lang.F($"{r.Boosters} Booster speisen die Abschnitte (je Ebene bzw. Gleislänge), die Zentrale bleibt fürs Schalten.");
        var loops = r.Loops > 0 ? " " + Lang.F($"{r.Loops} Kehrschleife(n) über Kehrschleifenmodul (KS) gespeist, {r.Isolations} Trennstelle(n) dafür gesetzt.") : "";
        var dets = r.Detectors == 0 ? ""
            : " " + (r.ThreeRail ? Lang.F($"{r.Detectors} Melder über isolierte Außenschiene (Kontaktgleis) am Rückmeldemodul.")
                                 : Lang.F($"{r.Detectors} Melder über Besetztmelder angeschlossen."));
        var system = r.ThreeRail ? Lang.F($"Gleissystem {r.System}: Mittelleiter, Bahnstrom an den Mittelleiter, Masse an die Schienen.")
                                 : Lang.F($"Gleissystem {r.System}: Zweileiter, Bahnstrom und Masse je an eine Schiene, gleiche Seite im Abschnitt.");
        var adjusted = r.CurrentAdjusted ? " " + Lang.T("Die eingestellte Stromart passte nicht zum Gleis und steht jetzt auf „Automatisch (nach Gleis)“.") : "";
        Say(system + adjusted + " " + Lang.F($"Automatisch verkabelt: {r.Sections} Abschnitt(e), {r.Feeds} Einspeisung(en) mit Masse, {r.Wires} Kabel.") + " " + power + loops + dets);
    }

    /// <summary>Startadresse eines Bausteins ändern.</summary>
    public void SetModuleAddress(int id, int? address)
    {
        if (Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { IsPower: true } item || item.Address == address) return;
        Record();
        item.Address = address;
        Commit();
    }

    /// <summary>Kabel und die roten Verbindungslinien im Plan zeigen (Ansicht, wird nicht gespeichert).</summary>
    public bool ShowWires { get; set; } = true;

    /// <summary>
    /// Elektrik durchnummerieren, von links oben nach rechts unten: Gleisanschlüsse G1, G2 …, Rückmelder M1, M2 …, Kabel K1, K2 …
    /// und Weichen W1, W2 … (nur Weichen ohne eigenen Namen). Rückmelder ohne Kontakt bekommen ihre Nummer auch als Kontakt, sofern die noch frei ist.
    /// </summary>
    public void NumberElectrics()
    {
        var plan = Plan;
        var switches = plan.Pieces.Where(p => p.Def.Drives > 0).ToList();
        if (plan.Feeders.Count + plan.Detectors.Count + plan.Scenery.Count(s => s.IsWire) + switches.Count == 0)
        {
            Say(Lang.T("Noch nichts zu nummerieren: keine Gleisanschlüsse, Rückmelder, Weichen oder Kabel im Plan."));
            return;
        }
        // Reihenfolge: in Streifen von 10 cm von oben nach unten, darin von links nach rechts
        static (double, double) Key((double X, double Y) p) => (Math.Floor(p.Y / 100), p.X);
        (double X, double Y) At(int pieceId) => plan.Find(pieceId) is { } piece ? (piece.Middle().X, piece.Middle().Y) : (0, 0);
        Record();
        var n = 0;
        foreach (var f in plan.Feeders.OrderBy(f => Key(At(f.PieceId)))) f.Name = $"G{++n}";
        n = 0;
        var used = plan.Detectors.Where(d => d.Address is not null).Select(d => d.Address!.Value).ToHashSet();
        foreach (var d in plan.Detectors.OrderBy(d => Key(At(d.PieceId))))
        {
            d.Name = $"Melder {++n}";
            if (d.Address is null && used.Add(n)) d.Address = n;
        }
        n = 0;
        foreach (var w in plan.Scenery.Where(s => s.IsWire && s.Points.Count > 0).OrderBy(w => Key(w.Points[0]))) w.Name = $"K{++n}";
        var wires = n;
        // Weichen: eigene Namen bleiben, automatische (W1, W2 …) werden neu vergeben
        static bool Auto(string name) => name.Length == 0 || (name.Length > 1 && name[0] == 'W' && name.Skip(1).All(char.IsDigit));
        var taken = switches.Where(p => !Auto(p.Name)).Select(p => p.Name).ToHashSet();
        n = 0;
        var named = 0;
        foreach (var p in switches.Where(p => Auto(p.Name)).OrderBy(p => Key((p.Middle().X, p.Middle().Y))))
        {
            do n++; while (taken.Contains($"W{n}"));
            p.Name = $"W{n}";
            named++;
        }
        Commit();
        Say(Lang.F($"Nummeriert: {plan.Feeders.Count} Gleisanschlüsse (G), {plan.Detectors.Count} Melder (Melder 1 …), {named} Weichen (W), {wires} Kabel (K). Die vollständige Liste steht im Kabelplan und lässt sich als CSV oder Text speichern."));
    }

    /// <summary>Farbe für neue Verteilerleisten (Werkzeug „Verteiler“).</summary>
    public string DistributorColor { get; set; } = "#795548";

    /// <summary>Betriebsart (analog/digital) umstellen. Quellen, die nicht passen, meldet die Elektrikprüfung.</summary>
    public void SetOperation(PowerMode mode)
    {
        if (Plan.Operation == mode) return;
        Record();
        Plan.Operation = mode;
        Commit();
        PowerType = PowerSupply.TypesFor(mode)[0];
        var wrong = PowerSupply.TrackSources(Plan).Count(s => !PowerSupply.Fits(PowerSupply.Type(s), mode));
        Say(Lang.F($"Betrieb: {PowerSupply.ModeName(mode)}.") + (wrong > 0 ? " " + Lang.F($"{wrong} Stromquelle(n) passen nicht dazu – in den Eigenschaften die Art ändern.") : ""));
    }

    /// <summary>Stromquelle als Symbol an diese Stelle setzen.</summary>
    public SceneryItem AddPowerSource(PowerSourceType type, double x, double y)
    {
        if (ModuleModel is { } model && Decoders.Find(model) is { } m && Decoders.PlacedAs(m) == type && AddModule(model, x, y) is { } module) return module;
        var item = AddScenery(PowerSupply.Create(Plan, type, x, y, DistributorColor));
        Say(Lang.F($"{PowerSupply.TypeName(type)} „{item.Name}“ gesetzt.") + " " + (type == PowerSourceType.Verteiler
            ? Lang.T("Kabel rasten an jeder Klemme der Leiste ein; alles, was dort hängt, ist miteinander verbunden.")
            : PowerSupply.HasGround(type)
            ? Lang.T("Rechtsklick bzw. Eigenschaften › „Kabel von hier verlegen“ und auf dem Gleis enden lassen – dort entsteht die Einspeisung. Die Masse (0) genauso mit „Masse-Kabel von hier verlegen“ auf die andere Schiene.")
            : PowerSupply.FeedsTrack(type)
            ? Lang.T("„Kabel von hier verlegen“ und auf dem Gleis enden lassen – dort entsteht die Einspeisung.")
            : Lang.T("Für Licht und Zubehör; Kabel rasten an der Klemme unten ein.")));
        return item;
    }

    /// <summary>Art einer Quelle ändern (z. B. Trafo → Zentrale).</summary>
    public void SetPowerType(int id, PowerSourceType type)
    {
        if (Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { IsPower: true } item || PowerSupply.Type(item) == type) return;
        Record();
        PowerSupply.ApplyType(item, type, Plan.Gauge, DistributorColor);
        Commit();
    }

    /// <summary>Farbe einer Verteilerleiste ändern.</summary>
    public void SetDistributorColor(int id, string hex)
    {
        if (Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { IsPower: true } item || PowerSupply.Type(item) != PowerSourceType.Verteiler
            || string.Equals(PowerSupply.DistributorColor(item), hex, StringComparison.OrdinalIgnoreCase)) return;
        Record();
        item.Color = hex;
        Commit();
    }

    /// <summary>Quelle umbenennen; die Gleisanschlüsse daran wandern mit.</summary>
    public void RenamePowerSource(int id, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { IsPower: true } item || item.Name == name) return;
        if (PowerSupply.Items(Plan).Any(s => s.Id != id && s.Name == name)) { Say(Lang.F($"Es gibt schon eine Stromquelle „{name}“.")); return; }
        Record();
        foreach (var f in Plan.Feeders.Where(f => f.Circuit == item.Name)) f.Circuit = name;
        foreach (var d in Plan.Detectors.Where(d => d.Source == item.Name)) d.Source = name;
        item.Name = name;
        Commit();
    }

    /// <summary>Stromkreis für einen neuen Gleisanschluss: der zuletzt benutzte, sonst die erste Fahrstromquelle; fehlt eine, wird sie angelegt.</summary>
    private string CircuitForNewFeeder()
    {
        var sources = PowerSupply.TrackSources(Plan).ToList();
        var last = Plan.Feeders.LastOrDefault()?.Circuit;
        if (last is not null && (sources.Count == 0 || sources.Any(s => s.Name == last))) return last;
        if ((sources.FirstOrDefault(s => PowerSupply.Fits(PowerSupply.Type(s), Plan.Operation)) ?? sources.FirstOrDefault()) is { } src) return src.Name;
        // Noch keine Quelle: passende Zentrale bzw. Trafo neben die Platte stellen
        var type = Plan.Operation == PowerMode.Analog ? PowerSourceType.Fahrtrafo : PowerSourceType.Zentrale;
        var (x, y) = PowerSupply.DefaultSpot(Plan);
        var item = PowerSupply.Create(Plan, type, x, y);
        item.Id = Plan.NextId++;
        item.Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1);
        Plan.Scenery.Add(item);
        _autoSource = item;
        return item.Name;
    }

    /// <summary>Beim letzten Gleisanschluss automatisch angelegte Quelle (für die Meldung).</summary>
    private SceneryItem? _autoSource;

    private string AutoSourceNote()
    {
        if (_autoSource is not { } a) return "";
        _autoSource = null;
        return " " + Lang.F($"Dafür steht jetzt „{a.Name}“ ({PowerSupply.TypeName(PowerSupply.Type(a))}) als Symbol unter der Platte – verschieben, wohin sie gehört.");
    }


    /// <summary>
    /// Einspeisung an ein Gleis setzen; die Auswahl bleibt. Bei Gleichstrom wird die Polung so gewählt,
    /// dass sie zu den übrigen Einspeisungen desselben Abschnitts passt.
    /// </summary>
    public Feeder AddFeederTo(int pieceId, string? circuit = null)
    {
        Record();
        var f = new Feeder { Id = Plan.NextId++, PieceId = pieceId, Circuit = string.IsNullOrWhiteSpace(circuit) ? CircuitForNewFeeder() : circuit.Trim() };
        f.Reversed = MatchingPolarity(pieceId);
        if (!string.IsNullOrWhiteSpace(circuit) && !PowerSupply.Sources(Plan).Any(s => s.Name == f.Circuit)) AddSourceFor(f.Circuit);
        Plan.Feeders.Add(f);
        Commit();
        Say(Lang.F($"Gleisanschluss an Gleis Nr. {pieceId} → {f.Circuit}.") + AutoSourceNote());
        return f;
    }

    /// <summary>Gleichstrom: Polung, die zu einer vorhandenen Einspeisung im selben Abschnitt passt (sonst nicht vertauscht).</summary>
    private bool MatchingPolarity(int pieceId)
    {
        var sections = Electrics.Sections(Plan, Connections);
        var section = sections.FirstOrDefault(s => s.PieceIds.Contains(pieceId));
        if (section is null || section.Feeders.Count == 0) return false;
        var sides = Electrics.RailSides(Plan, Connections);
        var other = section.Feeders[0];
        bool Left(int id) => sides.TryGetValue(id, out var v) && v;
        // Plus der vorhandenen Einspeisung auf Schiene B?
        var plusOnB = !Left(other.PieceId) ^ other.Reversed;
        return (!Left(pieceId)) != plusOnB;
    }

    public void SetFeederCircuit(int feederId, string circuit)
    {
        if (Plan.Feeders.FirstOrDefault(f => f.Id == feederId) is not { } f || string.IsNullOrWhiteSpace(circuit) || f.Circuit == circuit.Trim()) return;
        Record();
        f.Circuit = circuit.Trim();
        if (!PowerSupply.Sources(Plan).Any(s => s.Name == f.Circuit)) AddSourceFor(f.Circuit);
        Commit();
        Say(Lang.F($"Gleisanschluss Nr. {f.Id} hängt jetzt an {f.Circuit}.") + AutoSourceNote());
    }

    /// <summary>Neuer Stromkreis ohne Symbol: Booster (digital) bzw. weiteren Trafo (analog) mit diesem Namen anlegen.</summary>
    private void AddSourceFor(string name)
    {
        var digital = Plan.Operation == PowerMode.Digital;
        var type = digital ? (PowerSupply.TrackSources(Plan).Any() ? PowerSourceType.Booster : PowerSourceType.Zentrale) : PowerSourceType.Fahrtrafo;
        var (x, y) = PowerSupply.DefaultSpot(Plan);
        var item = PowerSupply.Create(Plan, type, x, y);
        item.Name = name;
        item.Id = Plan.NextId++;
        item.Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1);
        Plan.Scenery.Add(item);
        _autoSource = item;
    }

    /// <summary>Name für einen weiteren Stromkreis: nächster Booster (digital) bzw. Trafo (analog).</summary>
    public string NewCircuitName() => PowerSupply.NewName(Plan, Plan.Operation == PowerMode.Analog ? PowerSourceType.Fahrtrafo
        : PowerSupply.TrackSources(Plan).Any() ? PowerSourceType.Booster : PowerSourceType.Zentrale);

    public void ToggleFeederPolarity(int feederId)
    {
        if (Plan.Feeders.FirstOrDefault(f => f.Id == feederId) is not { } f) return;
        Record();
        f.Reversed = !f.Reversed;
        Commit();
        Say(f.Reversed ? Lang.F($"Anschluss Nr. {f.Id}: jetzt an der rechten Schiene.") : Lang.F($"Anschluss Nr. {f.Id}: jetzt an der linken Schiene."));
    }

    /// <summary>Alle Einspeisungen eines Gleises entfernen; die Auswahl bleibt.</summary>
    public void RemoveFeedersOn(int pieceId)
    {
        if (!Plan.Feeders.Any(f => f.PieceId == pieceId)) return;
        Record();
        var n = Plan.Feeders.RemoveAll(f => f.PieceId == pieceId);
        Commit();
        Say(n == 1 ? Lang.T("Gleisanschluss entfernt.") : Lang.F($"{n} Gleisanschlüsse entfernt."));
    }

    /// <summary>Rückmelder auf ein Gleis setzen oder (wenn schon einer da ist) entfernen; die Auswahl bleibt.</summary>
    public void ToggleDetectorOn(int pieceId)
    {
        Record();
        if (DetectorOn(pieceId) is { } d)
        {
            Plan.Detectors.Remove(d);
            Commit();
            Say(Lang.F($"Rückmelder {d.Name} entfernt."));
            return;
        }
        var det = NewDetector(pieceId);
        Plan.Detectors.Add(det);
        Commit();
        Say(DetectorPlacedText(det));
    }

    /// <summary>Neuer Melder für ein Gleis: „Melder n“, nächster freier Kontakt und (digital) Fahrstrom von Zentrale bzw. Booster.</summary>
    internal Detector NewDetector(int pieceId) => new()
    {
        Id = Plan.NextId++, PieceId = pieceId, Type = DetectorType, Name = Detector.NextName(Plan),
        Address = Plan.Detectors.Select(x => x.Address ?? 0).DefaultIfEmpty(0).Max() + 1, Source = DefaultDetectorSource(pieceId),
    };

    internal string DetectorPlacedText(Detector d)
 => Lang.F($"{d.Name} an Gleis Nr. {d.PieceId} (Kontakt {d.Address}).") + (d.Source is { Length: > 0 } s ? " " + Lang.F($"Fahrstrom über den Melder von „{s}“.") : "");

    /// <summary>Zentralen und Booster, von denen ein Melder den Fahrstrom bekommen kann.</summary>
    public IReadOnlyList<SceneryItem> DetectorSources
        => PowerSupply.Sources(Plan).Where(s => PowerSupply.Type(s) is PowerSourceType.Zentrale or PowerSourceType.Booster).ToList();

    /// <summary>
    /// Quelle für einen neuen Melder (nur digital): die Zentrale bzw. der Booster, der den Abschnitt schon speist, sonst der erste Booster
    /// bzw. die Zentrale im Plan; ohne solche Quelle keine.
    /// </summary>
    internal string? DefaultDetectorSource(int pieceId)
    {
        if (Plan.Operation == PowerMode.Analog) return null;
        var names = DetectorSources.Select(s => s.Name).ToHashSet();
        if (names.Count == 0) return null;
        var section = Electrics.Sections(Plan, Connections).FirstOrDefault(s => s.PieceIds.Contains(pieceId));
        if (section?.Circuits.FirstOrDefault(names.Contains) is { } fed) return fed;
        var boosters = DetectorSources.Where(s => PowerSupply.Type(s) == PowerSourceType.Booster).ToList();
        return (boosters.Count > 0 ? boosters[0] : DetectorSources[0]).Name;
    }

    /// <summary>Zentrale bzw. Booster eines Melders wählen (null = keine): sein Gleis bekommt den Fahrstrom über den Melder.</summary>
    public void SetDetectorSource(int detectorId, string? source)
    {
        if (Plan.Detectors.FirstOrDefault(d => d.Id == detectorId) is not { } d) return;
        // "" = bewusst keine Verbindung (null hieße: noch nicht gewählt, dann setzt das Programm eine Quelle)
        source = string.IsNullOrWhiteSpace(source) ? "" : source.Trim();
        if (d.Source == source) return;
        Record();
        d.Source = source;
        Commit();
        Say(source.Length == 0 ? Lang.F($"{d.Name}: keine Verbindung zu Zentrale oder Booster.") : Lang.F($"{d.Name}: Fahrstrom von „{source}“ über den Melder – der Abschnitt gilt als gespeist."));
    }

    /// <summary>Trennstellen an allen Enden des Gleises setzen (bzw. alle entfernen, wenn schon alle getrennt sind).</summary>
    public void ToggleAllIsolations(int pieceId)
    {
        var ends = PieceEnds(pieceId).Where(e => e.Connected).ToList();
        if (ends.Count == 0) { Say(Lang.T("Das Gleis ist an keinem Ende mit einem anderen verbunden.")); return; }
        var remove = ends.All(e => e.Isolated);
        Record();
        foreach (var e in ends)
        {
            var c = Connections.Find(pieceId, e.Index);
            var existing = Plan.Isolations.Where(i => (i.PieceId == pieceId && i.Index == e.Index) || (c?.Link is { } l && i.PieceId == l.PieceId && i.Index == l.Index)).ToList();
            if (remove) foreach (var x in existing) Plan.Isolations.Remove(x);
            else if (existing.Count == 0) Plan.Isolations.Add(new Isolation { Id = Plan.NextId++, PieceId = pieceId, Index = e.Index });
        }
        Commit();
        Say(remove ? Lang.T("Trennstellen am Gleis entfernt.") : Lang.F($"Gleis Nr. {pieceId} an {ends.Count} Enden getrennt – es bildet jetzt einen eigenen Abschnitt."));
    }

    /// <summary>Kurzbeschreibung der Anschlüsse eines Gleises für Statuszeile, Menü und Eigenschaften.</summary>
    public string ConnectionSummary(int pieceId)
    {
        var parts = new List<string>();
        var feeders = FeedersOn(pieceId);
        var supply = feeders.Where(f => !f.Ground).ToList();
        var grounds = feeders.Where(f => f.Ground).ToList();
        if (supply.Count > 0) parts.Add(supply.Count == 1 ? Lang.F($"Einspeisung ← {supply[0].Circuit}") : Lang.F($"{supply.Count} Einspeisungen ← {string.Join(", ", supply.Select(f => f.Circuit).Distinct())}"));
        if (grounds.Count > 0) parts.Add(Lang.F($"Masse ← {string.Join(", ", grounds.Select(f => f.Circuit).Distinct())}"));
        var iso = PieceEnds(pieceId).Count(e => e.Isolated);
        if (iso > 0) parts.Add(Lang.F($"{iso} Trennstelle(n)"));
        if (DetectorOn(pieceId) is { } d) parts.Add(Lang.F($"Rückmelder {d.Name}"));
        var signals = Plan.Signals.Count(s => s.PieceId == pieceId);
        if (signals > 0) parts.Add(Lang.F($"{signals} Signal(e)"));
        return parts.Count == 0 ? Lang.T("keine Anschlüsse") : string.Join(" · ", parts);
    }

    // ---------- Kabel ----------

    /// <summary>
    /// Kabel als Linienzug anlegen (mindestens zwei Punkte). Farbe und Querschnitt folgen der Verwendung.
    /// Endet ein Kabel auf einem Gleis, entsteht dort eine Einspeisung: Bahnstrom (B), wenn das Kabel – direkt oder über
    /// Verteiler und weitere Kabel – an der Bahnstromklemme einer Zentrale, eines Boosters oder Trafos hängt, Masse bei der Masseklemme (0).
    /// Ohne erkennbare Quelle entscheidet die Verwendung (Fahrstrom bzw. Rückleiter/Masse).
    /// </summary>
    public SceneryItem? AddWire(IReadOnlyList<(double X, double Y)> points, WireUse? use = null)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var q in points)
            if (pts.Count == 0 || Math.Abs(pts[^1].X - q.X) + Math.Abs(pts[^1].Y - q.Y) > 0.5) pts.Add(q);
        if (pts.Count < 2 || (pts.Count == 2 && Math.Abs(pts[0].X - pts[1].X) + Math.Abs(pts[0].Y - pts[1].Y) < 1))
        {
            Say(Lang.T("Ein Kabel braucht mindestens zwei Punkte."));
            return null;
        }
        var u = use ?? WireUse;
        Record();
        var n = Plan.Scenery.Count(s => s.IsWire) + 1;
        var item = new SceneryItem
        {
            Kind = SceneryKind.Wire, Variant = (int)u, Name = Lang.F($"Kabel {n}"), Id = Plan.NextId++,
            Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1),
            Color = WireLayout.DefaultColor(u, Electrics.Current(Plan)), Depth = WireLayout.DefaultSection(u, Plan.Gauge),
        };
        // Enden, die auf einem Gleis liegen (und an keinem anderen Einrastpunkt): dort entstehen Einspeisungen
        var anchors = WireLayout.Anchors(Plan);
        bool AtAnchor((double X, double Y) p) => anchors.Any(a => Math.Abs(a.X - p.X) <= 1 && Math.Abs(a.Y - p.Y) <= 1);
        var hidden = HiddenPieceIds();
        var hits = new (PlacedPiece Piece, double At, bool Right, double Offset)?[2];
        for (var e = 0; e < 2; e++)
        {
            var p = e == 0 ? pts[0] : pts[^1];
            if (!AtAnchor(p)) hits[e] = Electrics.TrackHit(Plan, p.X, p.Y, hidden);
        }
        // Kabel liegen immer rechtwinklig
        pts = WireLayout.Orthogonalize(pts);
        item.Points = pts;
        item.X = pts[0].X;
        item.Y = pts[0].Y;
        Plan.Scenery.Add(item);
        var made = new List<Feeder>();
        for (var e = 0; e < 2; e++)
        {
            if (hits[e] is not { } hit) continue;
            var other = e == 0 ? pts[^1] : pts[0];
            var (src, ground) = SourceAt(other);
            if (src is null && u is not (WireUse.Fahrstrom or WireUse.Rueckleiter)) continue;
            ground = src is null ? u == WireUse.Rueckleiter : ground;
            var current = Electrics.Current(Plan);
            var f = new Feeder
            {
                Id = Plan.NextId++, PieceId = hit.Piece.Id, At = hit.At, WireId = item.Id, Ground = ground,
                Circuit = src?.Name ?? CircuitForNewFeeder(),
                // Wechselstrom: Bahnstrom am Mittelleiter, Masse an der getroffenen Schiene; Gleichstrom: getroffene Schiene
                Reversed = current == CurrentKind.Wechselstrom && !ground ? false : hit.Right,
            };
            Plan.Feeders.Add(f);
            made.Add(f);
            // Kabelende genau auf den Anschlusspunkt legen
            var fp = Electrics.FeederPoint(Plan, hit.Piece, f);
            if (e == 0) pts[0] = fp; else pts[^1] = fp;
        }
        if (made.Count > 0)
        {
            item.Points = WireLayout.Orthogonalize(pts);
            item.X = item.Points[0].X;
            item.Y = item.Points[0].Y;
        }
        SelectedObject = item.Id;
        Selection.Clear();
        Commit();
        var msg = Lang.F($"{item.Name} verlegt: {Fmt.Num(WireLayout.Length(item) / 1000, 2)} m, {WireLayout.ColorName(item.Color!)}.");
        foreach (var f in made)
            msg += " " + (f.Ground
                ? Lang.F($"Masse von „{f.Circuit}“ an Gleis Nr. {f.PieceId}.")
                : Lang.F($"Einspeisung von „{f.Circuit}“ an Gleis Nr. {f.PieceId}."));
        Say(msg + AutoSourceNote());
        return item;
    }

    /// <summary>
    /// Kabelpunkt beim Verlegen einrasten: an Klemmen, Verteilern, Rückmeldern usw.; liegt der Punkt sonst auf einem Gleis,
    /// rastet er dort ein (das Kabelende wird zur Einspeisung bzw. zum Masseanschluss).
    /// </summary>
    public (double X, double Y, WireLayout.Anchor? Anchor) SnapWire(double x, double y, double tolerance)
    {
        var view = View;
        var snap = WireLayout.Snap(view, x, y, tolerance);
        if (snap.Anchor is not null) return snap;
        if (Electrics.TrackHit(view, x, y) is { } hit)
            return (snap.X, snap.Y, new WireLayout.Anchor(snap.X, snap.Y, Lang.F($"Gleis {hit.Piece.Def.Number} (Nr. {hit.Piece.Id}) – dort entsteht die Einspeisung")));
        return snap;
    }

    /// <summary>Welche Quellklemme liegt im selben Leitungsnetz wie dieser Punkt (Bahnstrom oder Masse)?</summary>
    private (SceneryItem? Source, bool Ground) SourceAt((double X, double Y) point)
    {
        var nets = WireLayout.Nets(Plan);
        if (nets(point) is not { } net) return (null, false);
        foreach (var src in PowerSupply.Sources(Plan))
        {
            if (nets(PowerSupply.Terminal(src)) == net) return (src, false);
            if (PowerSupply.GroundTerminal(src) is { } gt && nets(gt) == net) return (src, true);
        }
        return (null, false);
    }

    /// <summary>Gleise ausgeblendeter Ebenen (Kabel rasten dort nicht ein).</summary>
    private HashSet<int> HiddenPieceIds()
        => Plan.Pieces.Where(p => p.Level >= 0 && p.Level < Plan.Levels.Count && !Plan.Levels[p.Level].Visible).Select(p => p.Id).ToHashSet();

    /// <summary>Wo „Kabel von hier verlegen“ an einer Quelle beginnt: Bahnstromklemme bzw. Masseklemme (0).</summary>
    public (double X, double Y)? WireStart(int sourceId, bool ground)
    {
        if (Plan.Scenery.FirstOrDefault(s => s.Id == sourceId) is not { IsPower: true } src) return null;
        if (ground) return PowerSupply.GroundTerminal(src);
        return PowerSupply.Terminal(src);
    }

    /// <summary>Verwendung eines Kabels ändern; eine Standardfarbe wandert mit.</summary>
    public void SetWireUse(int id, WireUse use)
    {
        if (Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { IsWire: true } w) return;
        Record();
        var current = Electrics.Current(Plan);
        var wasDefault = string.Equals(WireLayout.ColorOf(w, current), WireLayout.DefaultColor(WireLayout.Use(w), current), StringComparison.OrdinalIgnoreCase);
        var sectionDefault = w.Depth <= 0 || Math.Abs(w.Depth - WireLayout.DefaultSection(WireLayout.Use(w), Plan.Gauge)) < 1e-9;
        w.Variant = (int)use;
        if (wasDefault) w.Color = WireLayout.DefaultColor(use, current);
        if (sectionDefault) w.Depth = WireLayout.DefaultSection(use, Plan.Gauge);
        Commit();
    }
}

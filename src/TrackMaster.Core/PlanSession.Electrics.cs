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

    /// <summary>Stromkreise, die im Plan vorkommen (mindestens „Stromkreis 1“).</summary>
    public IReadOnlyList<string> CircuitNames
    {
        get
        {
            var list = Plan.Feeders.Select(f => f.Circuit).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            return list.Count == 0 ? ["Stromkreis 1"] : list;
        }
    }

    /// <summary>Name für einen weiteren Stromkreis („Stromkreis n“).</summary>
    public string NewCircuitName()
    {
        var names = CircuitNames.ToHashSet();
        for (var n = 1; ; n++)
            if (!names.Contains($"Stromkreis {n}")) return $"Stromkreis {n}";
    }

    /// <summary>
    /// Einspeisung an ein Gleis setzen; die Auswahl bleibt. Bei Gleichstrom wird die Polung so gewählt,
    /// dass sie zu den übrigen Einspeisungen desselben Abschnitts passt.
    /// </summary>
    public Feeder AddFeederTo(int pieceId, string? circuit = null)
    {
        Record();
        var f = new Feeder { Id = Plan.NextId++, PieceId = pieceId, Circuit = string.IsNullOrWhiteSpace(circuit) ? Plan.Feeders.LastOrDefault()?.Circuit ?? "Stromkreis 1" : circuit.Trim() };
        f.Reversed = MatchingPolarity(pieceId);
        Plan.Feeders.Add(f);
        Commit();
        Say(Lang.F($"Einspeisung an Gleis Nr. {pieceId} ({f.Circuit})."));
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
        Commit();
        Say(Lang.F($"Einspeisung Nr. {f.Id} hängt jetzt an {f.Circuit}."));
    }

    public void ToggleFeederPolarity(int feederId)
    {
        if (Plan.Feeders.FirstOrDefault(f => f.Id == feederId) is not { } f) return;
        Record();
        f.Reversed = !f.Reversed;
        Commit();
        Say(f.Reversed ? Lang.F($"Einspeisung Nr. {f.Id}: Polung getauscht.") : Lang.F($"Einspeisung Nr. {f.Id}: Polung wieder normal."));
    }

    /// <summary>Alle Einspeisungen eines Gleises entfernen; die Auswahl bleibt.</summary>
    public void RemoveFeedersOn(int pieceId)
    {
        if (!Plan.Feeders.Any(f => f.PieceId == pieceId)) return;
        Record();
        var n = Plan.Feeders.RemoveAll(f => f.PieceId == pieceId);
        Commit();
        Say(n == 1 ? Lang.T("Einspeisung entfernt.") : Lang.F($"{n} Einspeisungen entfernt."));
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
        var next = Plan.Detectors.Select(x => x.Address ?? 0).DefaultIfEmpty(0).Max() + 1;
        var det = new Detector { Id = Plan.NextId++, PieceId = pieceId, Type = DetectorType, Name = $"RM{Plan.Detectors.Count + 1}", Address = next };
        Plan.Detectors.Add(det);
        Commit();
        Say(Lang.F($"Rückmelder {det.Name} an Gleis Nr. {pieceId} (Kontakt {next})."));
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
        if (feeders.Count > 0) parts.Add(Lang.F($"Einspeisung ({string.Join(", ", feeders.Select(f => f.Circuit).Distinct())})"));
        var iso = PieceEnds(pieceId).Count(e => e.Isolated);
        if (iso > 0) parts.Add(Lang.F($"{iso} Trennstelle(n)"));
        if (DetectorOn(pieceId) is { } d) parts.Add(Lang.F($"Rückmelder {d.Name}"));
        var signals = Plan.Signals.Count(s => s.PieceId == pieceId);
        if (signals > 0) parts.Add(Lang.F($"{signals} Signal(e)"));
        return parts.Count == 0 ? Lang.T("keine Anschlüsse") : string.Join(" · ", parts);
    }

    // ---------- Kabel ----------

    /// <summary>Kabel als Linienzug anlegen (mindestens zwei Punkte). Farbe und Querschnitt folgen der Verwendung.</summary>
    public SceneryItem? AddWire(IReadOnlyList<(double X, double Y)> points, WireUse? use = null)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var q in points)
            if (pts.Count == 0 || Math.Abs(pts[^1].X - q.X) + Math.Abs(pts[^1].Y - q.Y) > 0.5) pts.Add(q);
        if (pts.Count < 2)
        {
            Say(Lang.T("Ein Kabel braucht mindestens zwei Punkte."));
            return null;
        }
        var u = use ?? WireUse;
        var n = Plan.Scenery.Count(s => s.IsWire) + 1;
        var item = new SceneryItem
        {
            Kind = SceneryKind.Wire, Points = pts, Variant = (int)u, Name = Lang.F($"Kabel {n}"),
            Color = WireLayout.DefaultColor(u, Electrics.Current(Plan)), Depth = WireLayout.DefaultSection(u, Plan.Gauge),
        };
        item.X = pts[0].X;
        item.Y = pts[0].Y;
        AddScenery(item);
        Say(Lang.F($"{item.Name} verlegt: {Fmt.Num(WireLayout.Length(item) / 1000, 2)} m, {WireLayout.ColorName(item.Color!)}. Farbe und Querschnitt stehen in den Eigenschaften."));
        return item;
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

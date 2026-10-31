namespace TrackMaster.Core;

public enum CanvasTool { Select, Pan, Board, Terrain, Water, Building, Tree, Feeder, Isolation, Signal, Flex, Label, Dimension }

/// <summary>
/// Der geöffnete Plan mit Auswahl, aktivem Gleisende und Rückgängig-Verlauf.
/// Zeichenfläche, Bibliothek und Eigenschaften hören auf <see cref="Changed"/>.
/// </summary>
public sealed class PlanSession
{
    public LayoutPlan Plan { get; private set; } = SamplePlan.Build();
    public Connectivity Connections { get; private set; }
    public HashSet<int> Selection { get; } = [];
    /// <summary>Gleisende, an das ein Klick in der Bibliothek das nächste Gleis ansetzt.</summary>
    public (int PieceId, int Index)? Active { get; set; }
    public EditHistory History { get; } = new();
    public string? FilePath { get; private set; }
    public bool Dirty { get; private set; }

    public bool ShowGrid { get; set; } = true;
    public bool ShowEnds { get; set; } = true;
    public bool ShowLabels { get; set; }
    public bool ShowHeights { get; set; } = true;
    public bool ShowScenery { get; set; } = true;
    public bool ShowSections { get; set; }
    public bool SnapEnabled { get; set; } = true;
    public CanvasTool Tool { get; set; } = CanvasTool.Select;
    /// <summary>Signaltyp für das Werkzeug „Signal setzen“.</summary>
    public SignalType SignalType { get; set; } = SignalType.Hauptsignal;
    /// <summary>Gewähltes Objekt (Gelände, Einspeisung, Trennstelle, Signal) – Gleise stehen in <see cref="Selection"/>.</summary>
    public int? SelectedObject { get; private set; }
    /// <summary>Ergebnis der letzten Prüfung; die Zeichenfläche markiert die Stellen.</summary>
    public IReadOnlyList<PlanIssue> Issues { get; set; } = [];
    /// <summary>Pro-Funktionen frei? Wird vom Hauptfenster aus der Lizenz gesetzt.</summary>
    public Func<bool> IsPro { get; set; } = () => true;
    /// <summary>Gewähltes Gleissystem je Spur.</summary>
    public Dictionary<string, string> SystemByGauge { get; } = new();

    public event Action? Changed;
    public event Action<string>? Message;

    public PlanSession() => Connections = Connectivity.Analyze(Plan);

    private LayoutPlan? _view;
    private (LayoutPlan Plan, int Pieces, int Scenery, int Objects)? _viewKey;

    /// <summary>
    /// Was die Zeichenfläche zeigt und antippen lässt: der Plan ohne ausgeblendete Ebenen (dieselben Objekte).
    /// Änderungen gehen immer an <see cref="Plan"/>.
    /// </summary>
    public LayoutPlan View
    {
        get
        {
            if (Plan.AllLevelsVisible) return Plan;
            var key = (Plan, Plan.Pieces.Count, Plan.Scenery.Count, Plan.Feeders.Count + Plan.Isolations.Count + Plan.Signals.Count);
            if (_view is null || _viewKey != key) { _view = Plan.VisibleView(); _viewKey = key; }
            return _view;
        }
    }

    public TrackSystem CurrentSystem
    {
        get
        {
            var g = Plan.Gauge;
            return g.Systems.FirstOrDefault(s => SystemByGauge.TryGetValue(g.Id, out var id) && s.Id == id) ?? g.Systems[0];
        }
    }

    /// <summary>Gleisenden der sichtbaren Ebenen (Zeichenfläche: Antippen und Einrasten).</summary>
    public IEnumerable<ConnectorRef> VisibleConnectors(bool openOnly = false)
    {
        var all = openOnly ? Connections.Open : Connections.All;
        if (Plan.AllLevelsVisible) return all;
        var plan = Plan;
        return all.Where(c => plan.Find(c.PieceId) is { } p && plan.LevelVisible(p.Level));
    }

    public ConnectorRef? ActiveConnector => Active is { } a && Connections.Find(a.PieceId, a.Index) is { IsOpen: true } c ? c : null;

    public IReadOnlyList<PlacedPiece> SelectedPieces => Plan.Pieces.Where(p => Selection.Contains(p.Id)).ToList();

    public void Say(string text)
    {
        History.Label(text);
        Message?.Invoke(text);
    }

    /// <summary>Vor einer Änderung: Zustand für Rückgängig merken.</summary>
    public void Record()
    {
        History.Record(Plan);
        Dirty = true;
    }

    /// <summary>Nach einer Änderung: Verbindungen neu bestimmen und alle Ansichten aktualisieren.</summary>
    public void Commit()
    {
        _view = null;
        Connections = Connectivity.Analyze(Plan);
        Selection.RemoveWhere(id => Plan.Find(id) is null);
        if (SelectedObject is { } so && FindObject(so) is null) SelectedObject = null;
        if (Active is { } a && Connections.Find(a.PieceId, a.Index) is not { IsOpen: true }) Active = null;
        Changed?.Invoke();
    }

    public void Replace(LayoutPlan plan, string? path, bool keepHistory = false)
    {
        if (!keepHistory) History.Clear();
        Plan = plan;
        FilePath = path;
        Dirty = false;
        Selection.Clear();
        SelectedObject = null;
        Issues = [];
        Active = null;
        Commit();
    }

    // ---------- Datei ----------

    public void NewPlan()
    {
        Record();
        var gauge = Plan.GaugeId;
        Plan = LayoutPlan.Empty(gauge);
        FilePath = null;
        Selection.Clear();
        Active = null;
        Commit();
        Say(Lang.T("Neuer Plan angelegt. Rückgängig holt den vorherigen zurück."));
    }

    /// <summary>Neuer Plan mit Spur und Plattenform.</summary>
    public void NewPlan(BoardSetup setup)
    {
        var (gaugeId, shape, width, height, depth, turn, legLeft, legRight) = setup;
        Record();
        Plan = LayoutPlan.Empty(gaugeId);
        Plan.BoardShape = shape;
        Plan.BoardWidth = Math.Clamp(width, 50, 50000);
        Plan.BoardHeight = Math.Clamp(height, 50, 50000);
        Plan.BoardDepth = BoardGeometry.Depth(shape, Plan.BoardWidth, Plan.BoardHeight, depth, turn);
        Plan.BoardTurn = ((turn % 4) + 4) % 4;
        Plan.BoardLegLeft = shape == BoardShape.U ? Math.Max(0, legLeft) : 0;
        Plan.BoardLegRight = shape == BoardShape.U ? Math.Max(0, legRight) : 0;
        // Modulanlage: Breite und Tiefe sind die Maße des ersten Moduls
        if (shape == BoardShape.Module) ModuleLayout.Single(Plan, Plan.BoardWidth, Plan.BoardHeight);
        FilePath = null;
        Selection.Clear();
        Active = null;
        Commit();
        Say(Lang.F($"Neuer Plan angelegt: Spur {Plan.GaugeId}, {BoardGeometry.Describe(Plan)}. Rückgängig holt den vorherigen zurück."));
    }

    /// <summary>Übernimmt Lage und Größe der Module (verschobene Module nehmen ihre Gleise mit).</summary>
    public void ApplyModules(IReadOnlyList<ModuleEdit> edits)
    {
        if (edits.Count == 0) { Say(Lang.T("Eine Modulanlage braucht mindestens ein Modul.")); return; }
        Record();
        ModuleLayout.Apply(Plan, edits);
        Commit();
        Say(Lang.F($"Module übernommen: {BoardGeometry.Describe(Plan)}."));
    }

    /// <summary>Setzt einen gespeicherten Plan als weiteres Modul rechts an die Anlage an.</summary>
    public bool AppendModule(LayoutPlan other, string name)
    {
        if (other.GaugeId != Plan.GaugeId) { Say(Lang.F($"Der Plan ist für Spur {other.GaugeId}, diese Anlage für Spur {Plan.GaugeId}.")); return false; }
        Record();
        ModuleLayout.Append(Plan, other, name);
        Commit();
        Say(Lang.F($"„{name}“ als Modul angesetzt ({other.Pieces.Count} Gleise): {BoardGeometry.Describe(Plan)}."));
        return true;
    }

    public void LoadSample()
    {
        if (Plan.Pieces.Count > 0) Record();
        Plan = SamplePlan.Build();
        FilePath = null;
        Selection.Clear();
        Active = null;
        Commit();
    }

    public void Open(string path)
    {
        var plan = PlanSerializer.FromJson(File.ReadAllText(path), out var skipped, out var newer);
        Replace(plan, path);
        Say(OpenedMessage(plan, skipped, newer));
    }

    /// <summary>Öffnet einen Plan aus JSON (z. B. vom Android-Dateiauswahldialog).</summary>
    public void OpenJson(string json, string? name)
    {
        var plan = PlanSerializer.FromJson(json, out var skipped, out var newer);
        Replace(plan, name);
        Say(OpenedMessage(plan, skipped, newer));
    }

    private static string OpenedMessage(LayoutPlan plan, int skipped, bool newer)
        => Lang.F($"„{plan.Name}“ geöffnet{(skipped > 0 ? Lang.F($" – {skipped} unbekannte Teile übersprungen") : "")}.") +
           (newer ? Lang.T(" Die Datei stammt aus einer neueren Version von TrackMaster Pro; Neues daraus kann fehlen – bitte das Programm aktualisieren.") : "");

    /// <summary>Plan nach einem Neustart des Fensters (z. B. neue Darstellung) wiederherstellen, mit Dateiname und Änderungsstand.</summary>
    public void Restore(string json, string? path, bool dirty)
    {
        Replace(PlanSerializer.FromJson(json, out _), path);
        Dirty = dirty;
        Changed?.Invoke();
    }

    /// <summary>Nach dem Speichern über einen Dateidialog ohne festen Pfad.</summary>
    public void MarkSaved(string name)
    {
        FilePath = name;
        Dirty = false;
        Changed?.Invoke();
        Say(Lang.F($"Gespeichert: {name}"));
    }

    public void Save(string path)
    {
        File.WriteAllText(path, PlanSerializer.ToJson(Plan));
        FilePath = path;
        Dirty = false;
        Changed?.Invoke();
        Say(Lang.F($"Gespeichert: {Path.GetFileName(path)}"));
    }

    // ---------- Bearbeiten ----------

    public void Attach(string key, (double X, double Y) fallbackCenter)
    {
        var def = Catalog.Get(key);
        if (def.GaugeId != Plan.GaugeId) { Say(Lang.F($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {Plan.GaugeId}.")); return; }
        Record();
        var piece = PlanEditing.Attach(Plan, key, ActiveConnector, fallbackCenter);
        Selection.Clear();
        Selection.Add(piece.Id);
        Active = TrackGeometry.LocalConnectors(def, piece.Flipped).Count > 1 ? (piece.Id, 1) : null;
        Commit();
    }

    public void Drop(string key, double x, double y, double snapTolerance)
    {
        var def = Catalog.Find(key);
        if (def is null) return;
        if (def.GaugeId != Plan.GaugeId) { Say(Lang.F($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {Plan.GaugeId}.")); return; }
        Record();
        var piece = Plan.Add(key);
        PlanEditing.PlaceAt(piece, 0, x, y, 0);
        if (SnapEnabled) PlanEditing.Snap(Plan, new HashSet<int> { piece.Id }, snapTolerance);
        Selection.Clear();
        Selection.Add(piece.Id);
        Active = TrackGeometry.LocalConnectors(def, false).Count > 1 ? (piece.Id, 1) : null;
        Commit();
    }

    public void Rotate(double degrees)
    {
        var sel = SelectedPieces;
        if (sel.Count == 0) return;
        Record();
        PlanEditing.Rotate(sel, degrees);
        Commit();
    }

    public void Flip()
    {
        var sel = SelectedPieces;
        if (!sel.Any(p => p.Def.CanFlip)) { Say(Lang.T("Gerade Gleise und Prellböcke lassen sich nicht spiegeln.")); return; }
        Record();
        PlanEditing.Flip(sel);
        Commit();
    }

    public void Delete()
    {
        if (SelectedObject is { } obj)
        {
            Record();
            Plan.RemoveObject(obj);
            SelectedObject = null;
            Commit();
            Say(Lang.T("Gelöscht. Rückgängig holt es zurück."));
            return;
        }
        var n = Selection.Count;
        if (n == 0) return;
        Record();
        Plan.RemovePieces(Selection);
        Selection.Clear();
        Commit();
        Say(Lang.F($"{n} {(n == 1 ? Lang.T("Gleis") : Lang.T("Gleise"))} gelöscht. Rückgängig holt sie zurück."));
    }

    public void Duplicate()
    {
        var sel = SelectedPieces;
        if (sel.Count == 0) return;
        Record();
        var copies = PlanEditing.Duplicate(Plan, sel);
        Selection.Clear();
        foreach (var c in copies) Selection.Add(c.Id);
        Active = null;
        Commit();
    }

    /// <summary>
    /// Kopiert die gewählten Gleise mit ihren Einspeisungen, Trennstellen und Signalen sowie das gewählte Objekt
    /// (Gelände, Beschriftung, Maß) als Plan-Text für die Zwischenablage. Null, wenn nichts gewählt ist.
    /// </summary>
    public string? CopySelection()
    {
        var temp = new LayoutPlan { GaugeId = Plan.GaugeId, Name = Lang.T("Zwischenablage") };
        foreach (var p in SelectedPieces) temp.Pieces.Add(p.Clone());
        var ids = new HashSet<int>(temp.Pieces.Select(p => p.Id));
        temp.Feeders.AddRange(Plan.Feeders.Where(f => ids.Contains(f.PieceId)).Select(f => f.Clone()));
        temp.Isolations.AddRange(Plan.Isolations.Where(i => ids.Contains(i.PieceId)).Select(i => i.Clone()));
        temp.Signals.AddRange(Plan.Signals.Where(g => ids.Contains(g.PieceId)).Select(g => g.Clone()));
        if (SelectedObject is { } so && Plan.Scenery.FirstOrDefault(s => s.Id == so) is { } item) temp.Scenery.Add(item.Clone());
        if (temp.Pieces.Count == 0 && temp.Scenery.Count == 0) return null;
        var n = temp.Pieces.Count + temp.Scenery.Count;
        Say(n == 1 ? Lang.T("1 Objekt kopiert.") : Lang.F($"{n} Objekte kopiert."));
        return PlanSerializer.ToJson(temp, withIds: true);
    }

    /// <summary>
    /// Fügt kopierte Gleise und Objekte ein. Mit <paramref name="at"/> landet die Mitte dort, sonst leicht versetzt zum Original.
    /// Alle Teile bekommen neue Kennungen; die eingefügten Gleise sind danach gewählt.
    /// </summary>
    public bool Paste(string? json, (double X, double Y)? at = null)
    {
        LayoutPlan? src = null;
        try { if (!string.IsNullOrWhiteSpace(json)) src = PlanSerializer.FromJson(json, out _); }
        catch (InvalidDataException) { }
        if (src is null || src.Pieces.Count == 0 && src.Scenery.Count == 0)
        {
            Say(Lang.T("In der Zwischenablage sind keine kopierten Gleise."));
            return false;
        }
        if (src.GaugeId != Plan.GaugeId)
        {
            Say(Lang.F($"Die kopierten Gleise sind für Spur {src.GaugeId}, der Plan ist Spur {Plan.GaugeId} – Einfügen nicht möglich."));
            return false;
        }
        var points = src.Pieces.Select(p => (p.X, p.Y)).Concat(src.Scenery.Select(s => s.Center())).ToList();
        var cx = (points.Min(q => q.X) + points.Max(q => q.X)) / 2;
        var cy = (points.Min(q => q.Y) + points.Max(q => q.Y)) / 2;
        var (dx, dy) = at is { } t ? (t.X - cx, t.Y - cy) : (60.0, 60.0);
        Record();
        var map = new Dictionary<int, int>();
        Selection.Clear();
        foreach (var p in src.Pieces)
        {
            var q = p.Clone();
            q.Id = Plan.NextId++;
            q.X += dx; q.Y += dy;
            q.Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1);
            map[p.Id] = q.Id;
            Plan.Pieces.Add(q);
            Selection.Add(q.Id);
        }
        foreach (var f in src.Feeders) Plan.Feeders.Add(new Feeder { Id = Plan.NextId++, PieceId = map[f.PieceId], Circuit = f.Circuit });
        foreach (var i in src.Isolations) Plan.Isolations.Add(new Isolation { Id = Plan.NextId++, PieceId = map[i.PieceId], Index = i.Index });
        foreach (var g in src.Signals)
        {
            var sig = g.Clone();
            sig.Id = Plan.NextId++; sig.PieceId = map[g.PieceId]; sig.Address = null;
            Plan.Signals.Add(sig);
        }
        int? pastedObject = null;
        foreach (var s in src.Scenery)
        {
            var item = s.Clone();
            item.Id = Plan.NextId++;
            item.MoveBy(dx, dy);
            item.Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1);
            Plan.Scenery.Add(item);
            pastedObject = item.Id;
        }
        Active = null;
        SelectedObject = Selection.Count == 0 ? pastedObject : null;
        Commit();
        var n = src.Pieces.Count + src.Scenery.Count;
        Say(n == 1 ? Lang.T("1 Objekt eingefügt.") : Lang.F($"{n} Objekte eingefügt."));
        return true;
    }

    public void SwapAnchor()
    {
        if (SelectedPieces is not [var piece]) return;
        History.Record(Plan);
        var (next, error) = PlanEditing.SwapAnchor(Plan, piece);
        if (error is not null)
        {
            // Nichts geändert: Rückgängig-Schritt wieder verwerfen
            History.DiscardLast();
            Say(error);
            return;
        }
        Dirty = true;
        Active = next is { } k ? (piece.Id, k) : null;
        Commit();
    }

    /// <summary>Wählt das nächste offene Gleisende als aktives Ende.</summary>
    public Pose? NextOpenEnd()
    {
        var open = Connections.Open;
        if (open.Count == 0) { Say(Lang.T("Keine offenen Gleisenden – der Plan ist geschlossen.")); return null; }
        var i = Active is { } a ? open.ToList().FindIndex(c => c.PieceId == a.PieceId && c.Index == a.Index) : -1;
        var next = open[(i + 1) % open.Count];
        Active = (next.PieceId, next.Index);
        Changed?.Invoke();
        return next.Pose;
    }

    public void Nudge(double dx, double dy, bool record)
    {
        if (Selection.Count == 0) return;
        if (record) Record();
        foreach (var p in SelectedPieces) { p.X += dx; p.Y += dy; }
        Commit();
    }

    // ---------- Platte mit der Maus ändern ----------

    private (BoardHandle? Grip, LegGrip? Leg, string Start, double X, double Y, bool Changed)? _boardDrag;

    /// <summary>Platte bearbeiten: Ecke anfassen (Größe) oder Modul anfassen (verschieben). False, wenn dort nichts ist.</summary>
    public bool BeginBoardDrag(double x, double y, double tolerance)
    {
        var grip = ModuleLayout.Handles(Plan).Where(h => Math.Abs(h.X - x) <= tolerance && Math.Abs(h.Y - y) <= tolerance)
            .OrderBy(h => Math.Abs(h.X - x) + Math.Abs(h.Y - y)).FirstOrDefault();
        var leg = grip is not null ? null : BoardGeometry.LegGrips(Plan).Where(h => Math.Abs(h.X - x) <= tolerance && Math.Abs(h.Y - y) <= tolerance)
            .OrderBy(h => Math.Abs(h.X - x) + Math.Abs(h.Y - y)).FirstOrDefault();
        if (grip is null && leg is null && ModuleLayout.ModuleAt(Plan, x, y) is { } m) grip = new BoardHandle(m, -1, x, y);
        if (grip is null && leg is null) return false;
        Record();
        _boardDrag = (grip, leg, PlanSerializer.ToJson(Plan, withIds: true), x, y, false);
        return true;
    }

    /// <summary>Während des Ziehens: Zustand vom Anfang neu berechnen, damit sich nichts aufsummiert.</summary>
    public void BoardDragTo(double x, double y)
    {
        if (_boardDrag is not { } d) return;
        var plan = PlanSerializer.FromJson(d.Start, out _);
        if (d.Leg is { } leg)
        {
            var value = leg.Value + (x - d.X) * leg.AxisX + (y - d.Y) * leg.AxisY;
            if (leg.Length) BoardGeometry.SetLeg(plan, leg.Leg, length: Math.Max(1, value));
            else BoardGeometry.SetLeg(plan, leg.Leg, depth: Math.Max(1, value));
        }
        else if (d.Grip!.Corner < 0) ModuleLayout.MoveModule(plan, d.Grip.Module, x - d.X, y - d.Y);
        else ModuleLayout.DragCorner(plan, d.Grip.Module, d.Grip.Corner, x, y);
        Plan = plan;
        _boardDrag = d with { Changed = true };
        Commit();
    }

    public void EndBoardDrag()
    {
        if (_boardDrag is not { } d) return;
        _boardDrag = null;
        if (!d.Changed) { History.DiscardLast(); return; }
        if (d.Leg is { } leg)
        {
            var r = BoardGeometry.ResolvedLegs(Plan)[leg.Leg];
            var name = BoardGeometry.LegNames(Plan.BoardShape)[leg.Leg];
            Say(leg.Length ? Lang.F($"{name}: Länge {Fmt.Num(r.Length, 0)} mm.") : Lang.F($"{name}: Tiefe {Fmt.Num(r.Depth, 0)} mm."));
            return;
        }
        Say(d.Grip!.Corner < 0 && d.Grip.Module < Plan.Modules.Count
            ? Lang.F($"{Plan.Modules[d.Grip.Module].Name} verschoben: {BoardGeometry.Describe(Plan)}.")
            : Lang.F($"Plattengröße geändert: {BoardGeometry.Describe(Plan)}."));
    }

    public bool BoardDragging => _boardDrag is not null;

    public void Undo() => Undo(1);

    /// <summary>Mehrere Schritte zurück (Verlaufsliste).</summary>
    public void Undo(int steps)
    {
        if (History.Undo(Plan, steps) is not { } plan) return;
        KeepVisibility(plan);
        Plan = plan;
        Dirty = true;
        Commit();
    }

    public void Redo() => Redo(1);

    /// <summary>Mehrere Schritte vor (Verlaufsliste).</summary>
    public void Redo(int steps)
    {
        if (History.Redo(Plan, steps) is not { } plan) return;
        KeepVisibility(plan);
        Plan = plan;
        Dirty = true;
        Commit();
    }

    public void SelectAll()
    {
        SelectedObject = null;
        Selection.Clear();
        foreach (var p in View.Pieces) Selection.Add(p.Id);
        Changed?.Invoke();
    }

    // ---------- Ebenen ----------

    /// <summary>Ein- und Ausblenden ist keine Planänderung: Rückgängig lässt die Sichtbarkeit, wie sie ist.</summary>
    private void KeepVisibility(LayoutPlan restored)
    {
        for (var i = 0; i < restored.Levels.Count && i < Plan.Levels.Count; i++) restored.Levels[i].Visible = Plan.Levels[i].Visible;
        restored.ActiveLevel = Math.Clamp(Plan.ActiveLevel, 0, restored.Levels.Count - 1);
    }

    /// <summary>Ebene ein- oder ausblenden. Gewählte Gleise einer ausgeblendeten Ebene werden abgewählt.</summary>
    public void SetLevelVisible(int index, bool visible)
    {
        if (index < 0 || index >= Plan.Levels.Count || Plan.Levels[index].Visible == visible) return;
        Plan.Levels[index].Visible = visible;
        Dirty = true;
        DropHidden();
        Commit();
        Say(visible ? Lang.F($"{Plan.Levels[index].Name} eingeblendet.") : Lang.F($"{Plan.Levels[index].Name} ausgeblendet."));
    }

    /// <summary>Nur eine Ebene zeigen (oder mit <paramref name="index"/> = −1 alle).</summary>
    public void ShowOnlyLevel(int index)
    {
        for (var i = 0; i < Plan.Levels.Count; i++) Plan.Levels[i].Visible = index < 0 || i == index;
        if (index >= 0 && index < Plan.Levels.Count) Plan.ActiveLevel = index;
        Dirty = true;
        DropHidden();
        Commit();
    }

    public void SetActiveLevel(int index)
    {
        if (index < 0 || index >= Plan.Levels.Count) return;
        Plan.ActiveLevel = index;
        if (!Plan.Levels[index].Visible) Plan.Levels[index].Visible = true;
        Dirty = true;
        Commit();
        Say(Lang.F($"Neue Gleise kommen auf {Plan.Levels[index].Name} ({Fmt.Num(Plan.Levels[index].Height, 0)} mm)."));
    }

    private void DropHidden()
    {
        Selection.RemoveWhere(id => Plan.Find(id) is { } p && !Plan.LevelVisible(p.Level));
        if (SelectedObject is { } so && Plan.Scenery.FirstOrDefault(s => s.Id == so) is { } item && !Plan.LevelVisible(item.Level)) SelectedObject = null;
        if (Active is { } a && Plan.Find(a.PieceId) is { } ap && !Plan.LevelVisible(ap.Level)) Active = null;
    }

    /// <summary>
    /// Übernimmt die Ebenen aus dem Dialog. <see cref="LevelEdit.Index"/> ist die bisherige Ebene (−1 = neu).
    /// Ändert sich die Höhe einer Ebene, wandern ihre Gleise mit. Gleise und Objekte einer entfernten Ebene kommen auf die Ebene darunter.
    /// </summary>
    public void ApplyLevels(IReadOnlyList<LevelEdit> edits, int active)
    {
        if (edits.Count == 0) return;
        edits = edits.Take(LayoutPlan.MaxLevels).ToList();
        var structural = edits.Count != Plan.Levels.Count || edits.Select((e, i) => (e, i)).Any(t =>
            t.e.Index != t.i || t.e.Name != Plan.Levels[t.i].Name || Math.Abs(t.e.Height - Plan.Levels[t.i].Height) > 1e-6);
        if (structural) Record();
        // Neue Nummer jeder bisherigen Ebene; entfernte Ebenen gehen auf die nächste erhaltene darunter
        var map = new int[Plan.Levels.Count];
        for (var old = 0; old < map.Length; old++)
        {
            var target = -1;
            for (var o = old; o >= 0 && target < 0; o--) target = edits.ToList().FindIndex(e => e.Index == o);
            map[old] = target < 0 ? 0 : target;
        }
        var heightShift = new double[map.Length];
        for (var old = 0; old < map.Length; old++)
            heightShift[old] = edits.ToList().FindIndex(e => e.Index == old) is var k and >= 0 ? edits[k].Height - Plan.Levels[old].Height
                : edits[map[old]].Height - Plan.Levels[old].Height;
        foreach (var p in Plan.Pieces)
        {
            var old = Math.Clamp(p.Level, 0, map.Length - 1);
            p.Height += heightShift[old];
            p.Level = map[old];
        }
        foreach (var s in Plan.Scenery) s.Level = map[Math.Clamp(s.Level, 0, map.Length - 1)];
        Plan.Levels.Clear();
        for (var i = 0; i < edits.Count; i++)
            Plan.Levels.Add(new PlanLevel
            {
                Name = string.IsNullOrWhiteSpace(edits[i].Name) ? PlanLevel.DefaultName(i) : edits[i].Name.Trim(),
                Height = edits[i].Height, Visible = edits[i].Visible,
            });
        Plan.ActiveLevel = Math.Clamp(active, 0, Plan.Levels.Count - 1);
        Plan.Levels[Plan.ActiveLevel].Visible = true;
        Dirty = true;
        DropHidden();
        Commit();
        var hidden = Plan.Levels.Count(l => !l.Visible);
        Say(hidden == 0
            ? Lang.F($"{Plan.Levels.Count} Ebenen, alle sichtbar. Neue Gleise kommen auf {Plan.Levels[Plan.ActiveLevel].Name}.")
            : Lang.F($"{Plan.Levels.Count} Ebenen, {hidden} ausgeblendet. Neue Gleise kommen auf {Plan.Levels[Plan.ActiveLevel].Name}."));
    }

    /// <summary>Gewählte Gleise (und das gewählte Objekt) auf eine Ebene legen; Gleise ändern ihre Höhe um den Höhenunterschied der Ebenen.</summary>
    public void MoveSelectionToLevel(int index)
    {
        if (index < 0 || index >= Plan.Levels.Count) return;
        var pieces = SelectedPieces.Where(p => p.Level != index).ToList();
        var item = SelectedObject is { } so ? Plan.Scenery.FirstOrDefault(s => s.Id == so && s.Level != index) : null;
        if (pieces.Count == 0 && item is null) { Say(Lang.T("Erst Gleise oder ein Objekt wählen, die auf eine andere Ebene sollen.")); return; }
        Record();
        foreach (var p in pieces)
        {
            p.Height += Plan.Levels[index].Height - Plan.Level(p.Level).Height;
            p.Level = index;
        }
        if (item is not null) item.Level = index;
        if (!Plan.Levels[index].Visible) Plan.Levels[index].Visible = true;
        Commit();
        var n = pieces.Count + (item is null ? 0 : 1);
        Say(Lang.F($"{n} auf {Plan.Levels[index].Name} gelegt."));
    }

    public void ClearSelection()
    {
        Selection.Clear();
        SelectedObject = null;
        Active = null;
        Changed?.Invoke();
    }

    public void Select(IEnumerable<int> ids, bool add)
    {
        SelectedObject = null;
        if (!add) Selection.Clear();
        foreach (var id in ids) Selection.Add(id);
        Changed?.Invoke();
    }

    public void SelectObject(int? id)
    {
        Selection.Clear();
        Active = null;
        SelectedObject = id;
        Changed?.Invoke();
    }

    public object? FindObject(int id)
        => (object?)Plan.Scenery.FirstOrDefault(s => s.Id == id) ?? (object?)Plan.Feeders.FirstOrDefault(f => f.Id == id)
           ?? (object?)Plan.Isolations.FirstOrDefault(i => i.Id == id) ?? Plan.Signals.FirstOrDefault(s => s.Id == id);

    public void Toggle(int id)
    {
        if (!Selection.Remove(id)) Selection.Add(id);
        Changed?.Invoke();
    }

    public void SetActive(int pieceId, int index)
    {
        Active = (pieceId, index);
        Changed?.Invoke();
    }

    public bool SetGauge(string gaugeId)
    {
        if (gaugeId == Plan.GaugeId) return true;
        if (Plan.Pieces.Count > 0)
        {
            Say(Lang.F($"Dieser Plan ist für Spur {Plan.GaugeId}. Für Spur {gaugeId} bitte einen neuen Plan anlegen (Datei › Neu)."));
            return false;
        }
        Record();
        var g = Catalog.GetGauge(gaugeId);
        Plan.GaugeId = g.Id;
        Plan.BoardWidth = g.DefaultBoardWidth;
        Plan.BoardHeight = g.DefaultBoardHeight;
        Commit();
        return true;
    }

    /// <summary>Ändert eine Eigenschaft mit Rückgängig-Schritt.</summary>
    public void Edit(Action change)
    {
        Record();
        change();
        Commit();
    }

    /// <summary>Schnellbefehle im Ribbon: erstes passendes Teil des gewählten Systems ansetzen.</summary>
    public void Quick(PieceKind kind, bool? rightHand, (double X, double Y) fallbackCenter)
    {
        bool Fits(TrackPieceDef p) => p.Kind == kind && (rightHand is null || p.RightHand == rightHand) && (kind != PieceKind.Switch || p.IsSimpleSwitch) && !p.IsDoubleSlip;
        var def = CurrentSystem.Pieces.FirstOrDefault(Fits) ?? Plan.Gauge.Systems.SelectMany(s => s.Pieces).FirstOrDefault(Fits);
        if (def is null) { Say(Lang.T("Dieses Gleissystem hat kein passendes Teil.")); return; }
        Attach(def.Key, fallbackCenter);
    }

    // ---------- Verschieben mit der Maus ----------

    private List<(PlacedPiece Piece, double X, double Y)>? _moveStart;
    private (SceneryItem Item, double Dx, double Dy)? _moveObject;
    private bool _moveRecorded;

    public void BeginMove()
    {
        _moveStart = SelectedPieces.Select(p => (p, p.X, p.Y)).ToList();
        _moveObject = SelectedObject is { } id && Plan.Scenery.FirstOrDefault(s => s.Id == id) is { } item ? (item, 0, 0) : null;
        _moveRecorded = false;
    }

    public void MoveBy(double dx, double dy)
    {
        if (_moveStart is null) return;
        if (!_moveRecorded)
        {
            Record();
            _moveRecorded = true;
        }
        foreach (var (p, x, y) in _moveStart) { p.X = x + dx; p.Y = y + dy; }
        if (_moveObject is { } mo)
        {
            mo.Item.MoveBy(dx - mo.Dx, dy - mo.Dy);
            _moveObject = (mo.Item, dx, dy);
        }
        Commit();
    }

    public void EndMove(double snapTolerance)
    {
        if (_moveStart is { Count: > 0 } && _moveRecorded && SnapEnabled)
        {
            PlanEditing.Snap(Plan, Selection, snapTolerance);
            Commit();
        }
        _moveStart = null;
        _moveObject = null;
    }

    // ---------- Pro: Höhe, Gelände, Elektrik ----------

    /// <summary>Meldet, dass eine Funktion die Pro-Version braucht. Das Hauptfenster zeigt dazu den Lizenzhinweis.</summary>
    public event Action<string>? ProRequired;

    /// <summary>Prüft die Pro-Freischaltung; ohne Lizenz Hinweis und false.</summary>
    public bool RequirePro(string feature)
    {
        if (IsPro()) return true;
        ProRequired?.Invoke(feature);
        return false;
    }

    /// <summary>So viele Gleise darf ein Plan in der Basisversion haben, damit er sich speichern lässt.</summary>
    public const int BasisSaveLimit = 50;

    /// <summary>Speichern erlaubt? Pro immer, Basis bis <see cref="BasisSaveLimit"/> Gleise; sonst Lizenzhinweis.</summary>
    public bool RequireSave()
    {
        if (IsPro() || Plan.Pieces.Count <= BasisSaveLimit) return true;
        ProRequired?.Invoke(Lang.F($"Speichern von Plänen mit mehr als {BasisSaveLimit} Gleisen"));
        return false;
    }

    public void AttachFlex(string key, (double X, double Y) fallbackCenter) => Attach(key, fallbackCenter);

    // ---------- Flexgleis formen ----------

    /// <summary>Flexgleis des aktuellen Gleissystems (bzw. das zuletzt gewählte Flexgleis) für Ziehen und Lücke schließen.</summary>
    public FlexStock CurrentFlex
    {
        get => _flex is { } f && Catalog.FlexOf(Plan.GaugeId, f.SystemId) is not null && Catalog.Flexes(Plan.Gauge, CurrentSystem).Any(x => x.SystemId == f.SystemId)
            ? f : Catalog.Flex(Plan.Gauge, CurrentSystem);
        set => _flex = value;
    }
    private FlexStock? _flex;

    /// <summary>Legt ein geformtes Flexgleis ab <paramref name="start"/> (Fahrtrichtung). <paramref name="from"/>/<paramref name="to"/> sind die Gleisenden an Anfang und Ende, falls vorhanden.</summary>
    public PlacedPiece? PlaceFlex(FlexStock stock, Pose start, IReadOnlyList<Segment> segments, ConnectorRef? from, ConnectorRef? to)
    {
        if (segments.Count == 0) { Say(Lang.T("Das Flexgleis ist zu kurz.")); return null; }
        var (key, flipped) = FlexTrack.Key(Plan.GaugeId, stock.SystemId, segments);
        if (Catalog.Find(key) is not { } def) { Say(Lang.T("Diese Form lässt sich nicht als Flexgleis anlegen.")); return null; }
        Record();
        var piece = Plan.Add(key);
        piece.Flipped = flipped;
        PlanEditing.PlaceAt(piece, 0, start.X, start.Y, start.Heading);
        if (from is not null)
        {
            piece.Height = from.Z;
            if (Plan.Find(from.PieceId) is { } neighbour)
            {
                piece.Grade = from.Index == 0 ? -neighbour.EffectiveGrade : neighbour.EffectiveGrade;
                piece.Tunnel = neighbour.Tunnel;
                piece.Level = neighbour.Level;
            }
        }
        if (to is not null) piece.Grade = (to.Z - piece.Height) / def.TrackLength;
        Selection.Clear();
        Selection.Add(piece.Id);
        Active = to is null ? (piece.Id, 1) : null;
        Commit();
        Say(FlexMessage(segments) + (to is not null ? Lang.T(" – Lücke geschlossen.") : ""));
        return piece;
    }

    private string FlexMessage(IReadOnlyList<Segment> segments)
    {
        var text = FlexTrack.Describe(segments);
        var catalogMin = Plan.Gauge.Systems.SelectMany(s => s.Pieces).Where(p => p.Kind == PieceKind.Curve).Select(p => p.Radius).DefaultIfEmpty(0).Min();
        if (FlexTrack.SmallestRadius(segments) is { } r && catalogMin > 0 && r < catalogMin * 0.95)
            text += Lang.F($". Achtung: enger als der kleinste Katalogradius der Spur ({Fmt.Num(catalogMin, 0)} mm)");
        return text;
    }

    /// <summary>
    /// Lücke mit Flexgleis schließen: vom aktiven Gleisende zum nächsten offenen Ende der gewählten Gleise
    /// (sonst zum nächsten offenen Ende davor), ohne aktives Ende zwischen den nächsten offenen Enden zweier gewählter Gleise.
    /// </summary>
    public void ConnectEnds(FlexStock stock)
    {
        var open = Connections.Open;
        static double Dist(ConnectorRef a, ConnectorRef b) => Math.Sqrt(Math.Pow(a.Pose.X - b.Pose.X, 2) + Math.Pow(a.Pose.Y - b.Pose.Y, 2));
        ConnectorRef? a = ActiveConnector, b = null;
        if (a is not null)
        {
            b = open.Where(c => c.PieceId != a.PieceId && Selection.Contains(c.PieceId)).MinBy(c => Dist(a, c));
            if (b is null)
            {
                double hx = Math.Cos(a.Pose.HeadingRad), hy = Math.Sin(a.Pose.HeadingRad);
                b = open.Where(c => c.PieceId != a.PieceId && (c.Pose.X - a.Pose.X) * hx + (c.Pose.Y - a.Pose.Y) * hy > 0).MinBy(c => Dist(a, c));
            }
        }
        else
        {
            var ends = open.Where(c => Selection.Contains(c.PieceId)).ToList();
            var best = double.MaxValue;
            foreach (var x in ends)
                foreach (var y in ends)
                    if (x.PieceId < y.PieceId && Dist(x, y) < best) { best = Dist(x, y); a = x; b = y; }
        }
        if (a is null || b is null)
        {
            Say(Lang.T("Lücke schließen: ein Gleisende anklicken (orange) und das Zielgleis mit Strg+Klick dazu wählen – oder zwei Gleise mit offenen Enden wählen."));
            return;
        }
        var start = new Pose(a.Pose.X, a.Pose.Y, a.Pose.Heading);
        var segments = FlexTrack.Connect(start, new Pose(b.Pose.X, b.Pose.Y, b.Pose.Heading + 180));
        if (segments.Count == 0) { Say(Lang.T("Diese beiden Gleisenden lassen sich nicht mit einem Flexgleis verbinden.")); return; }
        PlaceFlex(stock, start, segments, a, b);
    }

    /// <summary>
    /// Nächstes Gleisende, an dem ein Flexgleis einrasten kann (offen oder nur mit <paramref name="ignorePiece"/> verbunden),
    /// höchstens <paramref name="tolerance"/> mm von (x, y) entfernt.
    /// </summary>
    public ConnectorRef? FlexTarget(double x, double y, double tolerance, int? ignorePiece = null, ConnectorRef? exclude = null)
        => Connections.All
            .Where(c => c.PieceId != ignorePiece && c != exclude && (c.IsOpen || (ignorePiece is not null && c.Link?.PieceId == ignorePiece)))
            .Where(c => Math.Sqrt(Math.Pow(c.Pose.X - x, 2) + Math.Pow(c.Pose.Y - y, 2)) <= tolerance)
            .MinBy(c => Math.Pow(c.Pose.X - x, 2) + Math.Pow(c.Pose.Y - y, 2));

    /// <summary>Beginnt ein neues Flexgleis an einem Gleisende (Fahrtrichtung vom Ende weg) oder frei an (x, y).</summary>
    public FlexDraft BeginFlexDraw(double x, double y, ConnectorRef? from)
        => from is not null ? new FlexDraft(new Pose(from.Pose.X, from.Pose.Y, from.Pose.Heading), from) : new FlexDraft(new Pose(x, y, 0), null);

    public PlacedPiece? FinishFlexDraw(FlexDraft draft)
        => draft.Segments.Count == 0 ? null : PlaceFlex(CurrentFlex, draft.Start, draft.Segments, draft.From, draft.Target);

    private (int PieceId, int Fixed, double Z, double Slope)? _reshape;

    /// <summary>Beginnt das Formen eines Flexgleises am Ende <paramref name="movedIndex"/>; das andere Ende bleibt liegen.</summary>
    public bool BeginReshape(int pieceId, int movedIndex)
    {
        if (Plan.Find(pieceId) is not { } p || !p.Def.IsFlex || p.WorldConnectors().Count != 2) return false;
        Record();
        var fix = 1 - movedIndex;
        var z = p.ConnectorHeights()[fix];
        _reshape = (pieceId, fix, z, fix == 0 ? p.EffectiveGrade : -p.EffectiveGrade);
        return true;
    }

    /// <summary>Formt das Flexgleis neu: gebogen durch (x, y) oder passend an das Gleisende <paramref name="target"/>.</summary>
    public IReadOnlyList<Segment> Reshape(double x, double y, ConnectorRef? target)
    {
        if (_reshape is not { } r || Plan.Find(r.PieceId) is not { } piece) return [];
        var fixedPose = piece.WorldConnectors()[r.Fixed];
        var start = new Pose(fixedPose.X, fixedPose.Y, fixedPose.Heading + 180);
        var segments = target is not null
            ? FlexTrack.Connect(start, new Pose(target.Pose.X, target.Pose.Y, target.Pose.Heading + 180))
            : FlexTrack.Bend(start, x, y);
        var (key, flipped) = FlexTrack.Key(Plan.GaugeId, piece.Def.SystemId, segments);
        if (segments.Count == 0 || Catalog.Find(key) is not { } def) return [];
        piece.Key = key;
        piece.Flipped = flipped;
        PlanEditing.PlaceAt(piece, 0, start.X, start.Y, start.Heading);
        piece.Height = r.Z;
        piece.Grade = target is not null ? (target.Z - r.Z) / def.TrackLength : r.Slope;
        // Ab jetzt liegt das feste Ende an Anschluss 0
        _reshape = r with { Fixed = 0, Slope = piece.Grade };
        Commit();
        return segments;
    }

    public void EndReshape(IReadOnlyList<Segment> last)
    {
        if (_reshape is null) return;
        _reshape = null;
        if (last.Count > 0) Say(FlexMessage(last));
    }

    public void SetTunnel(bool on)
    {
        var sel = SelectedPieces;
        if (sel.Count == 0) { Say(Lang.T("Bitte zuerst die Gleise wählen, die im Tunnel liegen.")); return; }
        Record();
        foreach (var p in sel) p.Tunnel = on;
        Commit();
        Say(on ? Lang.F($"{sel.Count} Gleis(e) als Tunnel markiert.") : Lang.F($"Tunnel bei {sel.Count} Gleis(en) aufgehoben."));
    }

    public void ApplyGradient(double startHeight, double percent, bool reverse)
    {
        if (Selection.Count == 0) { Say(Lang.T("Bitte zuerst die Gleise der Rampe wählen.")); return; }
        Record();
        var r = PlanEditing.ApplyGradient(Plan, Selection, startHeight, percent, reverse);
        if (r.Pieces == 0) { History.DiscardLast(); Say(r.Note ?? Lang.T("Keine Steigung gesetzt.")); return; }
        Commit();
        Say(Lang.F($"Steigung {Fmt.Num(percent, 1)} % über {r.Pieces} Gleise ({Fmt.Num(r.Length / 1000, 2)} m), Endhöhe {Fmt.Num(r.EndHeight, 0)} mm.{(r.Note is null ? "" : " " + r.Note)}"));
    }

    public void BuildHelix(string curveKey, double turns, double rise, bool rightHand, (double X, double Y) center)
        => BuildHelix(new HelixSpec(curveKey, turns, rise, rightHand), center);

    /// <summary>Baut eine Gleiswendel (rund oder oval, ein- oder zweigleisig) und liefert die Holzliste dazu; null bei Fehlern.</summary>
    public HelixMaterial? BuildHelix(HelixSpec spec, (double X, double Y) center)
    {
        Record();
        try
        {
            var (inner, outer) = Helix.Build(Plan, spec, ActiveConnector, center);
            Selection.Clear();
            foreach (var p in inner.Concat(outer)) Selection.Add(p.Id);
            var last = inner[^1];
            Active = (last.Id, 1);
            SelectedObject = null;
            Commit();
            var shape = spec.Shape == HelixShape.Oval ? Lang.T("Ovale") : Lang.T("Runde");
            var tracks = spec.Tracks == 2 ? Lang.T("zweigleisige") : Lang.T("eingleisige");
            Say(Lang.F($"{shape} {tracks} Gleiswendel mit {inner.Count + outer.Count} Gleisen gebaut, Endhöhe {Fmt.Num(last.ConnectorHeights()[1], 0)} mm."));
            return Helix.Material(spec, Plan.Gauge);
        }
        catch (InvalidOperationException e)
        {
            History.DiscardLast();
            Say(e.Message);
            return null;
        }
    }

    /// <summary>Beschriftung an einer Stelle des Plans.</summary>
    public SceneryItem AddLabel(string text, double x, double y)
    {
        var item = AddScenery(new SceneryItem { Kind = SceneryKind.Label, Name = text.Trim(), X = Math.Round(x), Y = Math.Round(y), Height = Annotation.DefaultTextSize(Plan.Gauge) });
        Say(Lang.T("Beschriftung gesetzt. Ziehen verschiebt sie, Text, Größe und Drehung stehen in den Eigenschaften."));
        return item;
    }

    /// <summary>Maßlinie zwischen zwei Punkten, mit einem kleinen Abstand zur Messstrecke.</summary>
    public SceneryItem AddDimension((double X, double Y) a, (double X, double Y) b)
    {
        var item = AddScenery(new SceneryItem
        {
            Kind = SceneryKind.Dimension, Points = [a, b], Width = Math.Round(Annotation.DefaultTextSize(Plan.Gauge) * 1.5),
            Height = Annotation.DefaultTextSize(Plan.Gauge) * 0.8,
        });
        var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        Say(Lang.F($"Maß {Fmt.Num(len, 0)} mm gesetzt. Abstand der Maßlinie und Schriftgröße stehen in den Eigenschaften."));
        return item;
    }

    public SceneryItem AddScenery(SceneryItem item)
    {
        Record();
        item.Id = Plan.NextId++;
        item.Level = Math.Clamp(Plan.ActiveLevel, 0, Plan.Levels.Count - 1);
        if (string.IsNullOrWhiteSpace(item.Name))
            item.Name = $"{SceneryItem.KindName(item.Kind)} {Plan.Scenery.Count(s => s.Kind == item.Kind) + 1}";
        Plan.Scenery.Add(item);
        SelectedObject = item.Kind == SceneryKind.Tree ? SelectedObject : item.Id;
        Selection.Clear();
        Commit();
        return item;
    }

    /// <summary>Entfernt ein Objekt (Einspeisung, Signal, Trennstelle, Gelände) direkt, z. B. mit dem Werkzeug.</summary>
    public void RemoveObject(int id)
    {
        var what = FindObject(id) switch { Feeder => Lang.T("Einspeisung"), Signal => "Signal", Isolation => Lang.T("Trennstelle"), _ => Lang.T("Objekt") };
        Record();
        if (!Plan.RemoveObject(id)) { History.DiscardLast(); return; }
        if (SelectedObject == id) SelectedObject = null;
        Commit();
        Say(Lang.F($"{what} entfernt. Rückgängig holt es zurück."));
    }

    public void AddFeeder(int pieceId)
    {
        Record();
        var circuit = Plan.Feeders.LastOrDefault()?.Circuit ?? "Stromkreis 1";
        var f = new Feeder { Id = Plan.NextId++, PieceId = pieceId, Circuit = circuit };
        Plan.Feeders.Add(f);
        SelectedObject = f.Id;
        Selection.Clear();
        Commit();
        Say(Lang.F($"Einspeisung an Gleis Nr. {pieceId} ({circuit})."));
    }

    /// <summary>Setzt oder entfernt eine Trennstelle an einem Gleisende (an verbundenen Enden auch auf der Gegenseite).</summary>
    public void ToggleIsolation(int pieceId, int index)
    {
        var c = Connections.Find(pieceId, index);
        var existing = Plan.Isolations.FirstOrDefault(i => (i.PieceId == pieceId && i.Index == index) || (c?.Link is { } l && i.PieceId == l.PieceId && i.Index == l.Index));
        Record();
        if (existing is not null)
        {
            Plan.Isolations.Remove(existing);
            Say(Lang.T("Trennstelle entfernt."));
        }
        else
        {
            Plan.Isolations.Add(new Isolation { Id = Plan.NextId++, PieceId = pieceId, Index = index });
            Say(Lang.T("Trennstelle gesetzt."));
        }
        Commit();
    }

    public void AddSignal(int pieceId, int index)
    {
        Record();
        var n = Plan.Signals.Count(s => s.Type == SignalType) + 1;
        var prefix = SignalType switch { SignalType.Vorsignal => "V", SignalType.Sperrsignal => "Sh", SignalType.Ausfahrsignal => "N", SignalType.Blocksignal => "B", _ => "A" };
        var sig = new Signal { Id = Plan.NextId++, PieceId = pieceId, Index = index, Type = SignalType, Name = $"{prefix}{n}" };
        Plan.Signals.Add(sig);
        SelectedObject = sig.Id;
        Selection.Clear();
        Commit();
    }

    public void AssignAddresses(int first, bool onlyMissing)
    {
        Record();
        var n = Electrics.AssignAddresses(Plan, first, onlyMissing);
        Commit();
        Say(Lang.F($"{n} Adressen vergeben."));
    }
}

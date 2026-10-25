namespace TrackMaster.Core;

public enum CanvasTool { Select, Pan, Terrain, Water, Building, Tree, Feeder, Isolation, Signal }

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

    public TrackSystem CurrentSystem
    {
        get
        {
            var g = Plan.Gauge;
            return g.Systems.FirstOrDefault(s => SystemByGauge.TryGetValue(g.Id, out var id) && s.Id == id) ?? g.Systems[0];
        }
    }

    public ConnectorRef? ActiveConnector => Active is { } a && Connections.Find(a.PieceId, a.Index) is { IsOpen: true } c ? c : null;

    public IReadOnlyList<PlacedPiece> SelectedPieces => Plan.Pieces.Where(p => Selection.Contains(p.Id)).ToList();

    public void Say(string text) => Message?.Invoke(text);

    /// <summary>Vor einer Änderung: Zustand für Rückgängig merken.</summary>
    public void Record()
    {
        History.Record(Plan);
        Dirty = true;
    }

    /// <summary>Nach einer Änderung: Verbindungen neu bestimmen und alle Ansichten aktualisieren.</summary>
    public void Commit()
    {
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
        Say("Neuer Plan angelegt. Rückgängig holt den vorherigen zurück.");
    }

    /// <summary>Neuer Plan mit Spur und Plattenform.</summary>
    public void NewPlan(BoardSetup setup)
    {
        var (gaugeId, shape, width, height, depth, turn) = setup;
        Record();
        Plan = LayoutPlan.Empty(gaugeId);
        Plan.BoardShape = shape;
        Plan.BoardWidth = Math.Clamp(width, 100, 50000);
        Plan.BoardHeight = Math.Clamp(height, 100, 50000);
        Plan.BoardDepth = BoardGeometry.Depth(shape, Plan.BoardWidth, Plan.BoardHeight, depth, turn);
        Plan.BoardTurn = ((turn % 4) + 4) % 4;
        FilePath = null;
        Selection.Clear();
        Active = null;
        Commit();
        Say($"Neuer Plan angelegt: Spur {Plan.GaugeId}, {BoardGeometry.Describe(Plan)}. Rückgängig holt den vorherigen zurück.");
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
        var plan = PlanSerializer.FromJson(File.ReadAllText(path), out var skipped);
        Replace(plan, path);
        Say($"„{plan.Name}“ geöffnet{(skipped > 0 ? $" – {skipped} unbekannte Teile übersprungen" : "")}.");
    }

    /// <summary>Öffnet einen Plan aus JSON (z. B. vom Android-Dateiauswahldialog).</summary>
    public void OpenJson(string json, string? name)
    {
        var plan = PlanSerializer.FromJson(json, out var skipped);
        Replace(plan, name);
        Say($"„{plan.Name}“ geöffnet{(skipped > 0 ? $" – {skipped} unbekannte Teile übersprungen" : "")}.");
    }

    /// <summary>Nach dem Speichern über einen Dateidialog ohne festen Pfad.</summary>
    public void MarkSaved(string name)
    {
        FilePath = name;
        Dirty = false;
        Changed?.Invoke();
        Say($"Gespeichert: {name}");
    }

    public void Save(string path)
    {
        File.WriteAllText(path, PlanSerializer.ToJson(Plan));
        FilePath = path;
        Dirty = false;
        Changed?.Invoke();
        Say($"Gespeichert: {Path.GetFileName(path)}");
    }

    // ---------- Bearbeiten ----------

    public void Attach(string key, (double X, double Y) fallbackCenter)
    {
        var def = Catalog.Get(key);
        if (def.GaugeId != Plan.GaugeId) { Say($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {Plan.GaugeId}."); return; }
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
        if (def.GaugeId != Plan.GaugeId) { Say($"Dieses Gleis ist für Spur {def.GaugeId}, der Plan für Spur {Plan.GaugeId}."); return; }
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
        if (!sel.Any(p => p.Def.CanFlip)) { Say("Gerade Gleise und Prellböcke lassen sich nicht spiegeln."); return; }
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
            Say("Gelöscht. Rückgängig holt es zurück.");
            return;
        }
        var n = Selection.Count;
        if (n == 0) return;
        Record();
        Plan.RemovePieces(Selection);
        Selection.Clear();
        Commit();
        Say($"{n} {(n == 1 ? "Gleis" : "Gleise")} gelöscht. Rückgängig holt sie zurück.");
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
        if (open.Count == 0) { Say("Keine offenen Gleisenden – der Plan ist geschlossen."); return null; }
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

    public void Undo()
    {
        if (History.Undo(Plan) is not { } plan) return;
        Plan = plan;
        Dirty = true;
        Commit();
    }

    public void Redo()
    {
        if (History.Redo(Plan) is not { } plan) return;
        Plan = plan;
        Dirty = true;
        Commit();
    }

    public void SelectAll()
    {
        SelectedObject = null;
        Selection.Clear();
        foreach (var p in Plan.Pieces) Selection.Add(p.Id);
        Changed?.Invoke();
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
            Say($"Dieser Plan ist für Spur {Plan.GaugeId}. Für Spur {gaugeId} bitte einen neuen Plan anlegen (Datei › Neu).");
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
        if (def is null) { Say("Dieses Gleissystem hat kein passendes Teil."); return; }
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

    public void AttachFlex(string key, (double X, double Y) fallbackCenter) => Attach(key, fallbackCenter);

    public void SetTunnel(bool on)
    {
        var sel = SelectedPieces;
        if (sel.Count == 0) { Say("Bitte zuerst die Gleise wählen, die im Tunnel liegen."); return; }
        Record();
        foreach (var p in sel) p.Tunnel = on;
        Commit();
        Say(on ? $"{sel.Count} Gleis(e) als Tunnel markiert." : $"Tunnel bei {sel.Count} Gleis(en) aufgehoben.");
    }

    public void ApplyGradient(double startHeight, double percent, bool reverse)
    {
        if (Selection.Count == 0) { Say("Bitte zuerst die Gleise der Rampe wählen."); return; }
        Record();
        var r = PlanEditing.ApplyGradient(Plan, Selection, startHeight, percent, reverse);
        if (r.Pieces == 0) { History.DiscardLast(); Say(r.Note ?? "Keine Steigung gesetzt."); return; }
        Commit();
        Say($"Steigung {Fmt.Num(percent, 1)} % über {r.Pieces} Gleise ({Fmt.Num(r.Length / 1000, 2)} m), Endhöhe {Fmt.Num(r.EndHeight, 0)} mm.{(r.Note is null ? "" : " " + r.Note)}");
    }

    public void BuildHelix(string curveKey, double turns, double rise, bool rightHand, (double X, double Y) center)
    {
        Record();
        try
        {
            var pieces = PlanEditing.BuildHelix(Plan, curveKey, turns, rise, rightHand, ActiveConnector, center);
            Selection.Clear();
            foreach (var p in pieces) Selection.Add(p.Id);
            var last = pieces[^1];
            Active = (last.Id, 1);
            SelectedObject = null;
            Commit();
            Say($"Gleiswendel mit {pieces.Count} Bögen gebaut, Endhöhe {Fmt.Num(last.ConnectorHeights()[1], 0)} mm.");
        }
        catch (InvalidOperationException e)
        {
            History.DiscardLast();
            Say(e.Message);
        }
    }

    public SceneryItem AddScenery(SceneryItem item)
    {
        Record();
        item.Id = Plan.NextId++;
        if (string.IsNullOrWhiteSpace(item.Name))
            item.Name = $"{SceneryItem.KindName(item.Kind)} {Plan.Scenery.Count(s => s.Kind == item.Kind) + 1}";
        Plan.Scenery.Add(item);
        SelectedObject = item.Kind == SceneryKind.Tree ? SelectedObject : item.Id;
        Selection.Clear();
        Commit();
        return item;
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
        Say($"Einspeisung an Gleis Nr. {pieceId} ({circuit}).");
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
            Say("Trennstelle entfernt.");
        }
        else
        {
            Plan.Isolations.Add(new Isolation { Id = Plan.NextId++, PieceId = pieceId, Index = index });
            Say("Trennstelle gesetzt.");
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
        Say($"{n} Adressen vergeben.");
    }
}

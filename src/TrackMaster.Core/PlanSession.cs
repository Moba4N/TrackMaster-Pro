namespace TrackMaster.Core;

public enum CanvasTool { Select, Pan, Terrain, Water, Building, Tree, Feeder, Isolation, Signal, Flex }

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
        var (gaugeId, shape, width, height, depth, turn, legLeft, legRight) = setup;
        Record();
        Plan = LayoutPlan.Empty(gaugeId);
        Plan.BoardShape = shape;
        Plan.BoardWidth = Math.Clamp(width, 100, 50000);
        Plan.BoardHeight = Math.Clamp(height, 100, 50000);
        Plan.BoardDepth = BoardGeometry.Depth(shape, Plan.BoardWidth, Plan.BoardHeight, depth, turn);
        Plan.BoardTurn = ((turn % 4) + 4) % 4;
        Plan.BoardLegLeft = shape == BoardShape.U ? Math.Max(0, legLeft) : 0;
        Plan.BoardLegRight = shape == BoardShape.U ? Math.Max(0, legRight) : 0;
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

    // ---------- Flexgleis formen (wie in WinTrack) ----------

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
        if (segments.Count == 0) { Say("Das Flexgleis ist zu kurz."); return null; }
        var (key, flipped) = FlexTrack.Key(Plan.GaugeId, stock.SystemId, segments);
        if (Catalog.Find(key) is not { } def) { Say("Diese Form lässt sich nicht als Flexgleis anlegen."); return null; }
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
            }
        }
        if (to is not null) piece.Grade = (to.Z - piece.Height) / def.TrackLength;
        Selection.Clear();
        Selection.Add(piece.Id);
        Active = to is null ? (piece.Id, 1) : null;
        Commit();
        Say(FlexMessage(segments) + (to is not null ? " – Lücke geschlossen." : ""));
        return piece;
    }

    private string FlexMessage(IReadOnlyList<Segment> segments)
    {
        var text = FlexTrack.Describe(segments);
        var catalogMin = Plan.Gauge.Systems.SelectMany(s => s.Pieces).Where(p => p.Kind == PieceKind.Curve).Select(p => p.Radius).DefaultIfEmpty(0).Min();
        if (FlexTrack.SmallestRadius(segments) is { } r && catalogMin > 0 && r < catalogMin * 0.95)
            text += $". Achtung: enger als der kleinste Katalogradius der Spur ({Fmt.Num(catalogMin, 0)} mm)";
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
            Say("Lücke schließen: ein Gleisende anklicken (orange) und das Zielgleis mit Strg+Klick dazu wählen – oder zwei Gleise mit offenen Enden wählen.");
            return;
        }
        var start = new Pose(a.Pose.X, a.Pose.Y, a.Pose.Heading);
        var segments = FlexTrack.Connect(start, new Pose(b.Pose.X, b.Pose.Y, b.Pose.Heading + 180));
        if (segments.Count == 0) { Say("Diese beiden Gleisenden lassen sich nicht mit einem Flexgleis verbinden."); return; }
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
            var shape = spec.Shape == HelixShape.Oval ? "Ovale" : "Runde";
            var tracks = spec.Tracks == 2 ? "zweigleisige" : "eingleisige";
            Say($"{shape} {tracks} Gleiswendel mit {inner.Count + outer.Count} Gleisen gebaut, Endhöhe {Fmt.Num(last.ConnectorHeights()[1], 0)} mm.");
            return Helix.Material(spec, Plan.Gauge);
        }
        catch (InvalidOperationException e)
        {
            History.DiscardLast();
            Say(e.Message);
            return null;
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

    /// <summary>Entfernt ein Objekt (Einspeisung, Signal, Trennstelle, Gelände) direkt, z. B. mit dem Werkzeug.</summary>
    public void RemoveObject(int id)
    {
        var what = FindObject(id) switch { Feeder => "Einspeisung", Signal => "Signal", Isolation => "Trennstelle", _ => "Objekt" };
        Record();
        if (!Plan.RemoveObject(id)) { History.DiscardLast(); return; }
        if (SelectedObject == id) SelectedObject = null;
        Commit();
        Say($"{what} entfernt. Rückgängig holt es zurück.");
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

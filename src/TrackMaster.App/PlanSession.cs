using System.IO;
using TrackMaster.Core;

namespace TrackMaster.App;

public enum CanvasTool { Select, Pan }

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
    public bool SnapEnabled { get; set; } = true;
    public CanvasTool Tool { get; set; } = CanvasTool.Select;
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
        Say("Neuer Plan angelegt. Strg+Z holt den vorherigen zurück.");
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
        var n = Selection.Count;
        if (n == 0) return;
        Record();
        Plan.Pieces.RemoveAll(p => Selection.Contains(p.Id));
        Selection.Clear();
        Commit();
        Say($"{n} {(n == 1 ? "Gleis" : "Gleise")} gelöscht. Strg+Z macht es rückgängig.");
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
        Selection.Clear();
        foreach (var p in Plan.Pieces) Selection.Add(p.Id);
        Changed?.Invoke();
    }

    public void ClearSelection()
    {
        Selection.Clear();
        Active = null;
        Changed?.Invoke();
    }

    public void Select(IEnumerable<int> ids, bool add)
    {
        if (!add) Selection.Clear();
        foreach (var id in ids) Selection.Add(id);
        Changed?.Invoke();
    }

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
        var def = CurrentSystem.Pieces.FirstOrDefault(p => p.Kind == kind && (rightHand is null || p.RightHand == rightHand))
                  ?? Plan.Gauge.Systems.SelectMany(s => s.Pieces).FirstOrDefault(p => p.Kind == kind && (rightHand is null || p.RightHand == rightHand));
        if (def is null) { Say("Dieses Gleissystem hat kein passendes Teil."); return; }
        Attach(def.Key, fallbackCenter);
    }

    // ---------- Verschieben mit der Maus ----------

    private List<(PlacedPiece Piece, double X, double Y)>? _moveStart;
    private bool _moveRecorded;

    public void BeginMove()
    {
        _moveStart = SelectedPieces.Select(p => (p, p.X, p.Y)).ToList();
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
        Commit();
    }

    public void EndMove(double snapTolerance)
    {
        if (_moveStart is not null && _moveRecorded && SnapEnabled)
        {
            PlanEditing.Snap(Plan, Selection, snapTolerance);
            Commit();
        }
        _moveStart = null;
    }
}

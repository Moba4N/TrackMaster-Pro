namespace TrackMaster.Core;

/// <summary>Rückgängig/Wiederholen über Momentaufnahmen des ganzen Plans (bis 200 Schritte).</summary>
public sealed class EditHistory
{
    private const int Limit = 200;
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Vor jeder Änderung aufrufen.</summary>
    public void Record(LayoutPlan plan)
    {
        _undo.AddLast(PlanSerializer.ToJson(plan, withIds: true));
        if (_undo.Count > Limit) _undo.RemoveFirst();
        _redo.Clear();
    }

    public LayoutPlan? Undo(LayoutPlan current)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _redo.Push(PlanSerializer.ToJson(current, withIds: true));
        return PlanSerializer.FromJson(last.Value, out _);
    }

    public LayoutPlan? Redo(LayoutPlan current)
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(PlanSerializer.ToJson(current, withIds: true));
        return PlanSerializer.FromJson(_redo.Pop(), out _);
    }

    /// <summary>Den zuletzt angelegten Schritt verwerfen, wenn sich doch nichts geändert hat.</summary>
    public void DiscardLast()
    {
        if (_undo.Count > 0) _undo.RemoveLast();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

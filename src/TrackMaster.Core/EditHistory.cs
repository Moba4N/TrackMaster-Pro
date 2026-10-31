namespace TrackMaster.Core;

/// <summary>Rückgängig/Wiederholen über Momentaufnahmen des ganzen Plans (bis 200 Schritte), jeder Schritt mit Bezeichnung.</summary>
public sealed class EditHistory
{
    private const int Limit = 200;
    /// <summary>So viele Schritte zeigt die Verlaufsliste je Richtung.</summary>
    public const int ListLength = 20;

    private sealed record Step(string Json, string Label, DateTime Time);

    private readonly LinkedList<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private bool _unlabeled;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Vor jeder Änderung aufrufen.</summary>
    public void Record(LayoutPlan plan)
    {
        _undo.AddLast(new Step(PlanSerializer.ToJson(plan, withIds: true), Lang.T("Änderung"), DateTime.Now));
        if (_undo.Count > Limit) _undo.RemoveFirst();
        _redo.Clear();
        _unlabeled = true;
    }

    /// <summary>Benennt den zuletzt aufgezeichneten Schritt (die erste Meldung nach der Änderung).</summary>
    public void Label(string text)
    {
        // nur eine Meldung direkt nach der Änderung beschreibt sie; spätere Meldungen gehören zu etwas anderem
        if (!_unlabeled || _undo.Last is not { } last || (DateTime.Now - last.Value.Time).TotalSeconds > 2) return;
        _unlabeled = false;
        var label = text.Split(". Rückgängig")[0].Split(" Rückgängig")[0].TrimEnd('.', ' ');
        if (label.Length > 90) label = label[..89] + "…";
        last.Value = last.Value with { Label = label };
    }

    /// <summary>Bezeichnungen der Schritte, die Rückgängig zurücknimmt, neuester zuerst.</summary>
    public IReadOnlyList<string> UndoLabels => _undo.Reverse().Take(ListLength).Select(Text).ToList();

    /// <summary>Bezeichnungen der Schritte, die Wiederholen wiederherstellt, nächster zuerst.</summary>
    public IReadOnlyList<string> RedoLabels => _redo.Take(ListLength).Select(Text).ToList();

    private static string Text(Step s) => $"{s.Time:HH:mm}  {s.Label}";

    public LayoutPlan? Undo(LayoutPlan current) => Undo(current, 1);

    /// <summary>Nimmt <paramref name="steps"/> Schritte auf einmal zurück.</summary>
    public LayoutPlan? Undo(LayoutPlan current, int steps)
    {
        _unlabeled = false;
        string? json = null;
        var now = PlanSerializer.ToJson(current, withIds: true);
        for (var i = 0; i < steps && _undo.Last is { } last; i++)
        {
            _undo.RemoveLast();
            _redo.Push(last.Value with { Json = json ?? now });
            json = last.Value.Json;
        }
        return json is null ? null : PlanSerializer.FromJson(json, out _);
    }

    public LayoutPlan? Redo(LayoutPlan current) => Redo(current, 1);

    /// <summary>Stellt <paramref name="steps"/> Schritte auf einmal wieder her.</summary>
    public LayoutPlan? Redo(LayoutPlan current, int steps)
    {
        _unlabeled = false;
        string? json = null;
        var now = PlanSerializer.ToJson(current, withIds: true);
        for (var i = 0; i < steps && _redo.Count > 0; i++)
        {
            var next = _redo.Pop();
            _undo.AddLast(next with { Json = json ?? now });
            json = next.Json;
        }
        return json is null ? null : PlanSerializer.FromJson(json, out _);
    }

    /// <summary>Den zuletzt angelegten Schritt verwerfen, wenn sich doch nichts geändert hat.</summary>
    public void DiscardLast()
    {
        if (_undo.Count > 0) _undo.RemoveLast();
        _unlabeled = false;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _unlabeled = false;
    }
}

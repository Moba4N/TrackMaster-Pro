using Microsoft.AspNetCore.Components;
using TrackMaster.Core;

namespace TrackMaster.Web;

/// <summary>Dialoge als Fenster über der Seite: Titel, Inhalt und Schaltflächen. Die Inhalte stehen in App.razor.</summary>
public partial class App
{
    private sealed class Dialog(string title, RenderFragment body)
    {
        public string Title { get; } = title;
        public RenderFragment Body { get; } = body;
        public List<DialogButton> Buttons { get; } = [];
        public bool Wide { get; init; }
        public string? Error { get; set; }
    }

    /// <summary>Schaltfläche im Dialog; <paramref name="Click"/> gibt false zurück, wenn der Dialog offen bleiben soll.</summary>
    private sealed record DialogButton(string Label, Func<bool> Click, bool Accent = false);

    private Dialog? _dialog;
    private readonly Stack<Dialog> _dialogStack = new();

    /// <summary>Eingaben des offenen Dialogs als Text (wie in den Eingabefeldern).</summary>
    private readonly Dictionary<string, string> _d = [];

    private Dialog Show(string title, RenderFragment body, bool wide = false, bool keepFields = false)
    {
        // Ein Dialog aus einem Dialog heraus (z. B. „Pro-Funktion“ aus der Stückliste) legt den alten zurück
        if (_dialog is not null) _dialogStack.Push(_dialog);
        if (!keepFields && _dialogStack.Count == 0) _d.Clear();
        _dialog = new Dialog(title, body) { Wide = wide };
        StateHasChanged();
        return _dialog;
    }

    private void CloseDialog()
    {
        _dialog = _dialogStack.Count > 0 ? _dialogStack.Pop() : null;
        if (_dialog is null) Js_FocusPlan();
        StateHasChanged();
    }

    private void CloseAllDialogs()
    {
        _dialogStack.Clear();
        _dialog = null;
        Js_FocusPlan();
        StateHasChanged();
    }

    private static void Js_FocusPlan() => Interop.Js.Focus(Plan.PlanView.ElementId);

    private void Click(DialogButton b)
    {
        var current = _dialog;
        bool close;
        try { close = b.Click(); }
        catch (Exception e) { Report(e); close = false; }
        // Hat die Schaltfläche selbst einen neuen Dialog geöffnet, bleibt dieser stehen
        if (close && _dialog == current) CloseDialog();
        else StateHasChanged();
    }

    private string D(string key) => _d.GetValueOrDefault(key, "");

    private static string N(double v, int decimals = 0) => Fmt.Num(v, decimals).Replace(Lang.IsEnglish ? "," : ".", "");

    private bool Read(string key, string name, double min, double max, List<string> errors, out double value)
    {
        value = Fmt.Parse(D(key)) ?? double.NaN;
        if (double.IsNaN(value)) { errors.Add(Lang.F($"{name}: bitte eine Zahl eingeben.")); return false; }
        if (value < min || value > max) { errors.Add(Lang.F($"{name}: zwischen {Fmt.Num(min)} und {Fmt.Num(max)}.")); return false; }
        return true;
    }

    private bool Fail(List<string> errors)
    {
        if (_dialog is not null) _dialog.Error = errors.Count > 0 ? string.Join("\n", errors) : null;
        return errors.Count == 0;
    }

    private void Message(string title, string text)
    {
        var d = Show(title, MessageBody(text));
        d.Buttons.Add(new("OK", () => true, true));
    }
}

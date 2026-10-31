using Android.Content;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Tiefe und Länge jedes Schenkels einzeln; leere Felder = gemeinsame Schenkeltiefe bzw. volle Länge.</summary>
    public static void AskLegs(Context c, PlanSession session)
    {
        var plan = session.Plan;
        if (!BoardGeometry.HasLegs(plan.BoardShape)) return;
        var names = BoardGeometry.LegNames(plan.BoardShape);
        var resolved = BoardGeometry.ResolvedLegs(plan);
        var body = Ui.Column(c, 6);
        body.AddView(Hint(c, Lang.F($"{BoardGeometry.Name(plan.BoardShape)}: Tiefe und Länge jedes Schenkels in mm. Leere Felder nehmen die gemeinsame Schenkeltiefe ({Fmt.Num(BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn), 0)} mm) bzw. die volle Länge. Mit „Platte ziehen“ lassen sich die runden Griffe auch mit der Maus ziehen.")));
        var boxes = new List<(EditText Depth, EditText? Length)>();
        for (var i = 0; i < names.Count; i++)
        {
            var own = i < plan.BoardLegs.Count ? plan.BoardLegs[i] : new BoardLeg(0, 0);
            var ownLength = plan.BoardShape == BoardShape.U && i < 2 ? (i == 0 ? plan.BoardLegLeft : plan.BoardLegRight) : own.Length;
            var depth = Ui.TextField(c, own.Depth > 0 ? Fmt.Num(resolved[i].Depth, 0) : "");
            depth.InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal;
            depth.Hint = Fmt.Num(resolved[i].Depth, 0);
            EditText? length = null;
            if (BoardGeometry.LegHasLength(plan.BoardShape, i))
            {
                length = Ui.TextField(c, ownLength > 0 ? Fmt.Num(resolved[i].Length, 0) : "");
                length.InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal;
                length.Hint = Fmt.Num(resolved[i].Length, 0);
            }
            body.AddView(Ui.Label(c, names[i], 13, true));
            body.AddView(length is null
                ? Columns(c, Ui.Field(c, Lang.T("Tiefe"), depth))
                : Columns(c, Ui.Field(c, Lang.T("Tiefe"), depth), Ui.Field(c, Lang.T("Länge"), length)));
            boxes.Add((depth, length));
        }
        var error = Error(c);
        body.AddView(error);
        Show(c, Lang.T("Schenkel einzeln"), body, Lang.T("Übernehmen"), () =>
        {
            var errors = new List<string>();
            var values = new List<(double, double)>();
            for (var i = 0; i < boxes.Count; i++)
            {
                double d = 0, l = 0;
                if ((boxes[i].Depth.Text ?? "").Trim().Length > 0) Read(boxes[i].Depth, Lang.F($"{names[i]} Tiefe"), 50, 50000, errors, out d);
                if (boxes[i].Length is { } lb && (lb.Text ?? "").Trim().Length > 0) Read(lb, Lang.F($"{names[i]} Länge"), 50, 50000, errors, out l);
                values.Add((d, l));
            }
            SetError(error, errors);
            if (errors.Count > 0) return false;
            session.Edit(() =>
            {
                BoardGeometry.ResetLegs(plan);
                for (var i = 0; i < values.Count; i++) BoardGeometry.SetLeg(plan, i, values[i].Item1, values[i].Item2);
            });
            session.Say(BoardGeometry.Describe(plan));
            return true;
        }, neutral: (Lang.T("Alle zurücksetzen"), () =>
        {
            session.Edit(() => BoardGeometry.ResetLegs(plan));
            session.Say(BoardGeometry.Describe(plan));
        }));
    }
}

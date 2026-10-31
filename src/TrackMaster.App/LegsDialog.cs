using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>
    /// Tiefe und Länge jedes Schenkels einzeln. Liefert je Schenkel (Tiefe, Länge) mit 0 = Standard, oder null bei Abbruch.
    /// <paramref name="reset"/> ist true, wenn alle Schenkel zurückgesetzt werden sollen.
    /// </summary>
    public static IReadOnlyList<(double Depth, double Length)>? AskLegs(Window owner, LayoutPlan plan, out bool reset)
    {
        reset = false;
        var names = BoardGeometry.LegNames(plan.BoardShape);
        var resolved = BoardGeometry.ResolvedLegs(plan);
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition());
        string[] heads = [Lang.T("Schenkel"), Lang.T("Tiefe"), Lang.T("Länge")];
        for (var c = 0; c < heads.Length; c++)
        {
            var t = Theme.Label(heads[c], 12, true, Theme.TextSecondaryBrush);
            Grid.SetColumn(t, c);
            table.Children.Add(t);
        }
        var boxes = new List<(TextBox Depth, TextBox? Length)>();
        for (var i = 0; i < names.Count; i++)
        {
            table.RowDefinitions.Add(new RowDefinition());
            var own = i < plan.BoardLegs.Count ? plan.BoardLegs[i] : new BoardLeg(0, 0);
            var ownLength = plan.BoardShape == BoardShape.U && i < 2 ? (i == 0 ? plan.BoardLegLeft : plan.BoardLegRight) : own.Length;
            var label = Theme.Label(names[i], 12.5, false, Theme.TextBrush);
            label.VerticalAlignment = VerticalAlignment.Center;
            var depth = new TextBox { Text = own.Depth > 0 ? Fmt.Num(resolved[i].Depth, 0) : "", ToolTip = Lang.F($"Leer = {Fmt.Num(resolved[i].Depth, 0)} mm"), Margin = new Thickness(0, 2, 6, 2) };
            TextBox? length = BoardGeometry.LegHasLength(plan.BoardShape, i)
                ? new TextBox { Text = ownLength > 0 ? Fmt.Num(resolved[i].Length, 0) : "", ToolTip = Lang.F($"Leer = {Fmt.Num(resolved[i].Length, 0)} mm"), Margin = new Thickness(0, 2, 6, 2) }
                : null;
            FrameworkElement lengthCell = length is not null ? length : Theme.Label(Lang.T("durch Außenmaß"), 11.5, false, Theme.TextSecondaryBrush);
            lengthCell.VerticalAlignment = VerticalAlignment.Center;
            foreach (var (el, col) in new (FrameworkElement, int)[] { (label, 0), (depth, 1), (lengthCell, 2) })
            {
                Grid.SetRow(el, i + 1);
                Grid.SetColumn(el, col);
                table.Children.Add(el);
            }
            boxes.Add((depth, length));
        }
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.F($"{BoardGeometry.Name(plan.BoardShape)}: Tiefe und Länge jedes Schenkels in mm. Leere Felder nehmen die gemeinsame Schenkeltiefe ({Fmt.Num(BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn), 0)} mm) bzw. die volle Länge. Mit „Platte ziehen“ lassen sich die runden Griffe auch mit der Maus ziehen.")));
        body.Children.Add(table);
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var resetButton = Theme.Button(Lang.T("Alle zurücksetzen"));
        var win = Small(owner, Lang.T("Schenkel einzeln"), body, 560, 460, resetButton, cancel, ok);
        IReadOnlyList<(double, double)>? result = null;
        var doReset = false;
        resetButton.Click += (_, _) => { doReset = true; result = []; win.Close(); };
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            var values = new List<(double, double)>();
            for (var i = 0; i < boxes.Count; i++)
            {
                double d = 0, l = 0;
                if (boxes[i].Depth.Text.Trim().Length > 0) Read(boxes[i].Depth, Lang.F($"{names[i]} Tiefe"), 50, 50000, errors, out d);
                if (boxes[i].Length is { } lb && lb.Text.Trim().Length > 0) Read(lb, Lang.F($"{names[i]} Länge"), 50, 50000, errors, out l);
                values.Add((d, l));
            }
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = values;
            win.Close();
        };
        win.ShowDialog();
        reset = doReset;
        return result;
    }
}

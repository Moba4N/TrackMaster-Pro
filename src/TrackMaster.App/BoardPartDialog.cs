using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>Was nach dem Klick mit „Platte ziehen“ geschehen soll.</summary>
    public enum BoardPartChoice { None, Apply, Rotate, More }

    /// <summary>
    /// Maße eines Moduls oder der ganzen Platte von Hand eintippen. <paramref name="fields"/> enthält
    /// (Beschriftung, Startwert, Minimum, Maximum); bei <see cref="BoardPartChoice.Apply"/> stehen die Werte in <paramref name="values"/>.
    /// </summary>
    public static BoardPartChoice AskBoardPart(Window owner, string title, string hint, string? name, IReadOnlyList<(string Label, double Value, double Min, double Max)> fields,
        string? moreLabel, out string newName, out double[] values)
    {
        var body = new StackPanel();
        body.Children.Add(Hint(hint));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBox? nameBox = null;
        var row = 0;
        void Put(string label, TextBox box)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var l = Theme.Label(label, 12.5, false, Theme.TextBrush);
            l.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(0, 2, 6, 2);
            Grid.SetRow(l, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            grid.Children.Add(l); grid.Children.Add(box);
            row++;
        }
        if (name is not null) Put(Lang.T("Name"), nameBox = new TextBox { Text = name });
        var boxes = new List<TextBox>();
        foreach (var f in fields)
        {
            var b = NumberBox(f.Value, 0);
            boxes.Add(b);
            Put(f.Label, b);
        }
        body.Children.Add(grid);
        var error = ErrorText();
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var rotate = Theme.Button(Lang.T("Um 90° drehen"), "RotateRight");
        var footer = new List<Button> { rotate };
        Button? more = null;
        if (moreLabel is not null) footer.Add(more = Theme.Button(moreLabel));
        footer.Add(cancel);
        footer.Add(ok);
        var win = Small(owner, title, body, 520, 300 + 34 * row, footer.ToArray());
        var choice = BoardPartChoice.None;
        var result = new double[fields.Count];
        rotate.Click += (_, _) => { choice = BoardPartChoice.Rotate; win.Close(); };
        if (more is not null) more.Click += (_, _) => { choice = BoardPartChoice.More; win.Close(); };
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            for (var i = 0; i < fields.Count; i++) Read(boxes[i], fields[i].Label, fields[i].Min, fields[i].Max, errors, out result[i]);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            choice = BoardPartChoice.Apply;
            win.Close();
        };
        win.ShowDialog();
        newName = nameBox?.Text ?? "";
        values = result;
        return choice;
    }
}

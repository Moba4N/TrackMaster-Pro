using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    private static readonly string[] Turns = ["Grundstellung", "90° gedreht", "180° gedreht", "270° gedreht"];

    /// <summary>Neuer Plan: Spur, Form der Anlagenplatte (Rechteck, L, U, T, Z, Ring), Maße und Ausrichtung.</summary>
    public static BoardSetup? AskNewPlan(Window owner, string currentGauge)
    {
        var gauges = Catalog.Gauges.ToList();
        var gaugeBox = new ComboBox { Height = 28 };
        foreach (var g in gauges) gaugeBox.Items.Add($"Spur {g.Id} ({g.Scale})");
        gaugeBox.SelectedIndex = Math.Max(0, gauges.FindIndex(g => g.Id == currentGauge));
        var shapeBox = new ComboBox { Height = 28 };
        foreach (var s in BoardGeometry.All) shapeBox.Items.Add(BoardGeometry.Name(s));
        shapeBox.SelectedIndex = 0;
        var turnBox = new ComboBox { Height = 28 };
        foreach (var t in Turns) turnBox.Items.Add(t);
        turnBox.SelectedIndex = 0;
        var start = BoardSetup.Default(gauges[gaugeBox.SelectedIndex].Id);
        var width = NumberBox(start.Width, 0);
        var height = NumberBox(start.Height, 0);
        var depth = NumberBox(start.Depth, 0);
        var depthField = Theme.Field("Schenkeltiefe (mm)", depth);
        var turnField = Theme.Field("Ausrichtung", turnBox);
        var preview = new System.Windows.Shapes.Path { Fill = Theme.BoardBrush, Stroke = Theme.BoardStrokeBrush, StrokeThickness = 1.5, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round };
        var previewBox = new Border
        {
            Height = 170, Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(12), Background = Theme.WindowBrush,
            BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = preview,
        };
        var info = Theme.Secondary("");
        var error = ErrorText();
        var updating = false;

        BoardShape Shape() => BoardGeometry.All[Math.Max(0, shapeBox.SelectedIndex)];
        void Update()
        {
            var shape = Shape();
            depthField.Visibility = turnField.Visibility = BoardGeometry.HasLegs(shape) ? Visibility.Visible : Visibility.Collapsed;
            double w = Fmt.Parse(width.Text) ?? 0, h = Fmt.Parse(height.Text) ?? 0, d = Fmt.Parse(depth.Text) ?? 0;
            if (w < 100 || h < 100) return;
            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = geometry.Open())
                foreach (var o in BoardGeometry.Outlines(shape, w, h, d, turnBox.SelectedIndex))
                {
                    ctx.BeginFigure(new Point(o[0].X, o[0].Y), true, true);
                    ctx.PolyLineTo(o.Skip(1).Select(q => new Point(q.X, q.Y)).ToList(), true, false);
                }
            preview.Data = geometry;
            var area = BoardGeometry.Rects(shape, w, h, d, turnBox.SelectedIndex).Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)) / 1e6;
            info.Text = BoardGeometry.HasLegs(shape)
                ? $"Außenmaße {Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} mm · Schenkel {Fmt.Num(BoardGeometry.Depth(shape, w, h, d, turnBox.SelectedIndex), 0)} mm · Fläche {Fmt.Num(area, 2)} m²"
                : $"{Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} mm · Fläche {Fmt.Num(area, 2)} m²";
        }
        void Suggest()
        {
            // Maße passend zu Spur und Form vorschlagen
            var s = BoardSetup.Default(gauges[Math.Max(0, gaugeBox.SelectedIndex)].Id, Shape());
            updating = true;
            width.Text = Fmt.Num(s.Width, 0);
            height.Text = Fmt.Num(s.Height, 0);
            depth.Text = Fmt.Num(s.Depth, 0);
            updating = false;
            Update();
        }
        gaugeBox.SelectionChanged += (_, _) => Suggest();
        shapeBox.SelectionChanged += (_, _) => Suggest();
        turnBox.SelectionChanged += (_, _) => Update();
        foreach (var box in new[] { width, height, depth })
            box.TextChanged += (_, _) => { if (!updating) Update(); };

        var body = new StackPanel();
        body.Children.Add(Hint("Der aktuelle Plan wird verworfen; Rückgängig holt ihn zurück. Form und Maße lassen sich später in den Planeigenschaften ändern."));
        body.Children.Add(Theme.Field("Spur", gaugeBox));
        body.Children.Add(Theme.Field("Form der Anlagenplatte", shapeBox));
        body.Children.Add(previewBox);
        body.Children.Add(Columns2(Theme.Field("Breite außen (mm)", width), Theme.Field("Tiefe außen (mm)", height)));
        body.Children.Add(Columns2(depthField, turnField));
        body.Children.Add(info);
        body.Children.Add(error);
        Update();

        var ok = Theme.Button("Plan anlegen", "New", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button("Abbrechen");
        cancel.IsCancel = true;
        var win = Small(owner, "Neuer Plan", body, 520, 640, cancel, ok);
        BoardSetup? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(width, "Breite", 100, 50000, errors, out var w);
            Read(height, "Tiefe", 100, 50000, errors, out var h);
            var d = 0.0;
            if (BoardGeometry.HasLegs(Shape())) Read(depth, "Schenkeltiefe", 50, 50000, errors, out d);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = new BoardSetup(gauges[Math.Max(0, gaugeBox.SelectedIndex)].Id, Shape(), w, h, d > 0 ? d : 600, turnBox.SelectedIndex);
            win.Close();
        };
        win.ShowDialog();
        return result;
    }

    private static Grid Columns2(FrameworkElement a, FrameworkElement b)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(b);
        return grid;
    }
}

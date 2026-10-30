using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    private static readonly string[] Turns = [Lang.T("Grundstellung"), Lang.T("90° gedreht"), Lang.T("180° gedreht"), Lang.T("270° gedreht")];

    /// <summary>Neuer Plan: Spur, Form der Anlagenplatte (Rechteck, L, U, T, Z, Ring, G, E, Module), Maße und Ausrichtung.</summary>
    public static BoardSetup? AskNewPlan(Window owner, string currentGauge)
    {
        var gauges = Catalog.Gauges.ToList();
        var gaugeBox = new ComboBox { Height = 28 };
        foreach (var g in gauges) gaugeBox.Items.Add(Lang.F($"Spur {g.Id} ({g.Scale})"));
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
        // Schenkellängen der U-Form: leer = volle Länge
        var legLeft = new TextBox();
        var legRight = new TextBox();
        var depthField = Theme.Field(Lang.T("Schenkeltiefe (mm)"), depth);
        var legsRow = Columns2(Theme.Field(Lang.T("Linker Schenkel lang (mm, leer = ganz)"), legLeft), Theme.Field(Lang.T("Rechter Schenkel lang (mm, leer = ganz)"), legRight));
        var turnField = Theme.Field(Lang.T("Ausrichtung"), turnBox);
        var preview = new System.Windows.Shapes.Path { Fill = Theme.BoardBrush, Stroke = Theme.BoardStrokeBrush, StrokeThickness = 1.5, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round };
        var previewBox = new Border
        {
            Height = 170, Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(12), Background = Theme.WindowBrush,
            BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = preview,
        };
        var moduleHint = Hint(Lang.T("Modulanlage: Breite und Tiefe gelten für das erste Modul. Weitere Module hängst du unter Gelände › Module… an, dort lassen sich auch gespeicherte Pläne als Modul ansetzen."));
        var info = Theme.Secondary("");
        var error = ErrorText();
        var updating = false;

        BoardShape Shape() => BoardGeometry.All[Math.Max(0, shapeBox.SelectedIndex)];
        void Update()
        {
            var shape = Shape();
            depthField.Visibility = turnField.Visibility = BoardGeometry.HasLegs(shape) ? Visibility.Visible : Visibility.Collapsed;
            legsRow.Visibility = shape == BoardShape.U ? Visibility.Visible : Visibility.Collapsed;
            moduleHint.Visibility = shape == BoardShape.Module ? Visibility.Visible : Visibility.Collapsed;
            double w = Fmt.Parse(width.Text) ?? 0, h = Fmt.Parse(height.Text) ?? 0, d = Fmt.Parse(depth.Text) ?? 0;
            double l1 = Fmt.Parse(legLeft.Text) ?? 0, l2 = Fmt.Parse(legRight.Text) ?? 0;
            if (w < 100 || h < 100) return;
            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = geometry.Open())
                foreach (var o in BoardGeometry.Outlines(shape, w, h, d, turnBox.SelectedIndex, l1, l2))
                {
                    ctx.BeginFigure(new Point(o[0].X, o[0].Y), true, true);
                    ctx.PolyLineTo(o.Skip(1).Select(q => new Point(q.X, q.Y)).ToList(), true, false);
                }
            preview.Data = geometry;
            var area = BoardGeometry.Rects(shape, w, h, d, turnBox.SelectedIndex, l1, l2).Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)) / 1e6;
            info.Text = BoardGeometry.HasLegs(shape)
                ? Lang.F($"Außenmaße {Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} mm · Schenkel {Fmt.Num(BoardGeometry.Depth(shape, w, h, d, turnBox.SelectedIndex), 0)} mm · Fläche {Fmt.Num(area, 2)} m²")
                : Lang.F($"{Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} mm · Fläche {Fmt.Num(area, 2)} m²");
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
        foreach (var box in new[] { width, height, depth, legLeft, legRight })
            box.TextChanged += (_, _) => { if (!updating) Update(); };

        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Der aktuelle Plan wird verworfen; Rückgängig holt ihn zurück. Form und Maße lassen sich später in den Planeigenschaften ändern.")));
        body.Children.Add(Theme.Field(Lang.T("Spur"), gaugeBox));
        body.Children.Add(Theme.Field(Lang.T("Form der Anlagenplatte"), shapeBox));
        body.Children.Add(previewBox);
        body.Children.Add(moduleHint);
        body.Children.Add(Columns2(Theme.Field(Lang.T("Breite außen (mm)"), width), Theme.Field(Lang.T("Tiefe außen (mm)"), height)));
        body.Children.Add(Columns2(depthField, turnField));
        body.Children.Add(legsRow);
        body.Children.Add(info);
        body.Children.Add(error);
        Update();

        var ok = Theme.Button(Lang.T("Plan anlegen"), "New", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var win = Small(owner, Lang.T("Neuer Plan"), body, 520, 700, cancel, ok);
        BoardSetup? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(width, Lang.T("Breite"), 100, 50000, errors, out var w);
            Read(height, Lang.T("Tiefe"), 100, 50000, errors, out var h);
            var d = 0.0;
            if (BoardGeometry.HasLegs(Shape())) Read(depth, Lang.T("Schenkeltiefe"), 50, 50000, errors, out d);
            double l1 = 0, l2 = 0;
            if (Shape() == BoardShape.U)
            {
                if (legLeft.Text.Trim().Length > 0) Read(legLeft, Lang.T("Linker Schenkel"), 50, 50000, errors, out l1);
                if (legRight.Text.Trim().Length > 0) Read(legRight, Lang.T("Rechter Schenkel"), 50, 50000, errors, out l2);
            }
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            var full = turnBox.SelectedIndex % 2 == 0 ? h : w;
            result = new BoardSetup(gauges[Math.Max(0, gaugeBox.SelectedIndex)].Id, Shape(), w, h, d > 0 ? d : 600, turnBox.SelectedIndex,
                l1 >= full ? 0 : l1, l2 >= full ? 0 : l2);
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

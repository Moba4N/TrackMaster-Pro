using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Farben und Bausteine im Stil von Windows 11 (hell). Akzent und Zeichenfläche kommen aus dem gewählten Design.</summary>
public static class Theme
{
    private static readonly Design D = Appearance.Current.Palette;
    private static Color C(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    private static Color A(byte alpha, Rgb c) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static readonly Color Accent = C(D.Accent);
    public static readonly Color AccentDark = C(D.AccentDark);
    public static readonly Color Text = Color.FromRgb(0x1B, 0x1B, 0x1B);
    public static readonly Color IconColor = Color.FromRgb(0x33, 0x33, 0x33);
    public static readonly Color LogoGreen = C(Design.LogoGreen);
    public static readonly Color LogoDark = C(Design.LogoDark);

    public static readonly SolidColorBrush AccentBrush = Freeze(Accent);
    public static readonly SolidColorBrush AccentLightBrush = Freeze(C(D.AccentLight));
    public static readonly SolidColorBrush AccentBorderBrush = Freeze(C(D.AccentBorder));
    public static readonly SolidColorBrush TextBrush = Freeze(Text);
    public static readonly SolidColorBrush TextSecondaryBrush = Freeze(Color.FromRgb(0x55, 0x55, 0x55));
    public static readonly SolidColorBrush WindowBrush = Freeze(Color.FromRgb(0xF3, 0xF3, 0xF3));
    public static readonly SolidColorBrush PanelBrush = Freeze(Colors.White);
    public static readonly SolidColorBrush PaneHeaderBrush = Freeze(Color.FromRgb(0xF7, 0xF7, 0xF7));
    public static readonly SolidColorBrush BorderBrush = Freeze(Color.FromRgb(0xE1, 0xE1, 0xE1));
    public static readonly SolidColorBrush BorderStrongBrush = Freeze(Color.FromRgb(0xD6, 0xD6, 0xD6));
    public static readonly SolidColorBrush ControlBorderBrush = Freeze(Color.FromRgb(0xD1, 0xD1, 0xD1));
    public static readonly SolidColorBrush HoverBrush = Freeze(Color.FromRgb(0xEB, 0xEB, 0xEB));
    public static readonly SolidColorBrush SuccessBrush = Freeze(Color.FromRgb(0x0F, 0x7B, 0x0F));
    public static readonly SolidColorBrush DangerBrush = Freeze(Color.FromRgb(0xC4, 0x2B, 0x1C));
    public static readonly SolidColorBrush OpenEndBrush = Freeze(Color.FromRgb(0xD4, 0x6B, 0x00));
    public static readonly SolidColorBrush WarningBackBrush = Freeze(Color.FromRgb(0xFF, 0xF4, 0xCE));
    public static readonly SolidColorBrush WarningTextBrush = Freeze(Color.FromRgb(0x6B, 0x4E, 0x00));
    public static readonly SolidColorBrush TipBackBrush = Freeze(C(D.TipBack));

    // Zeichenfläche
    public static readonly SolidColorBrush CanvasBrush = Freeze(C(D.Canvas));
    public static readonly SolidColorBrush CanvasTextBrush = Freeze(C(D.CanvasText));
    public static readonly SolidColorBrush BoardBrush = Freeze(C(D.Board));
    public static readonly SolidColorBrush BoardStrokeBrush = Freeze(C(D.BoardStroke));
    public static readonly SolidColorBrush GridMinorBrush = Freeze(A(0x24, D.Grid));
    public static readonly SolidColorBrush GridMajorBrush = Freeze(A(0x48, D.Grid));
    public static readonly SolidColorBrush BedBrush = Freeze(C(D.Bed));
    public static readonly SolidColorBrush SleeperBrush = Freeze(C(D.Sleeper));
    public static readonly SolidColorBrush RailBrush = Freeze(C(D.Rail));
    public static readonly SolidColorBrush SelectionBrush = Freeze(A(0x5A, D.Accent));
    public static readonly SolidColorBrush BandBrush = Freeze(A(0x22, D.Accent));

    /// <summary>Schrift der Oberfläche und der Beschriftungen im Plan.</summary>
    public static readonly FontFamily Font = new(Appearance.Current.FontChoice.Windows);
    /// <summary>Faktor für alle Schriftgrößen.</summary>
    public static readonly double FontScale = Appearance.Current.Scale;

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static TextBlock Label(string text, double size = 12, bool bold = false, Brush? brush = null) => new()
    {
        Text = text,
        FontSize = size * FontScale,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = brush ?? TextBrush,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Secondary(string text) => Label(text, 11, false, TextSecondaryBrush);

    /// <summary>Kopfzeile eines Seitenpanels.</summary>
    public static Border PaneHeader(string title, out TextBlock right)
    {
        right = Secondary("");
        right.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel { LastChildFill = false };
        var t = Label(title, 13, true);
        t.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(t);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        return new Border
        {
            Height = 34, Padding = new Thickness(12, 0, 12, 0), Background = PaneHeaderBrush,
            BorderBrush = BorderBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = dock,
        };
    }

    /// <summary>Abgerundete Schaltfläche mit Rahmen (oder Akzentfarbe).</summary>
    public static Button Button(string text, string? icon = null, bool accent = false, bool danger = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is not null)
            content.Children.Add(new System.Windows.Controls.Image { Source = Icons.Get(icon, accent ? Colors.White : danger ? DangerBrush.Color : null), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 6),
            Background = accent ? AccentBrush : new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD)),
            Foreground = accent ? Brushes.White : danger ? DangerBrush : TextBrush,
            BorderBrush = accent ? new SolidColorBrush(AccentDark) : ControlBorderBrush,
            FontSize = 12 * FontScale,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedTemplate(),
        };
        return b;
    }

    private static ControlTemplate? _rounded;

    /// <summary>Vorlage: Rahmen mit 4 px Rundung, beim Überfahren leicht abgedunkelt.</summary>
    public static ControlTemplate RoundedTemplate()
    {
        if (_rounded is not null) return _rounded;
        var t = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ButtonBase));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(Lang.T("Background")) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(Lang.T("Padding")) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "Bd"));
        t.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Bd"));
        t.Triggers.Add(disabled);
        _rounded = t;
        return t;
    }

    /// <summary>Eingabefeld mit Beschriftung darüber.</summary>
    public static StackPanel Field(string label, TextBox box)
    {
        box.Height = 28;
        box.Padding = new Thickness(6, 0, 6, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.BorderBrush = ControlBorderBrush;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 8) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 * FontScale, Foreground = TextSecondaryBrush, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(box);
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        return panel;
    }

    /// <summary>Auswahlliste mit Beschriftung darüber.</summary>
    public static StackPanel Field(string label, ComboBox box)
    {
        box.Height = 28;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 8) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 * FontScale, Foreground = TextSecondaryBrush, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(box);
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        return panel;
    }
}

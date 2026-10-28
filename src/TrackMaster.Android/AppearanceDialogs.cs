using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.Mobile;

/// <summary>Gewählte Darstellung, gespeichert in den App-Einstellungen.</summary>
public static class Appearance
{
    private const string Prefs = "darstellung";

    public static AppearanceSettings Load(Context c)
    {
        var p = c.GetSharedPreferences(Prefs, FileCreationMode.Private)!;
        return new AppearanceSettings(p.GetString("design", "trackplan")!, p.GetString("font", "standard")!, p.GetFloat("scale", 1f));
    }

    public static void Save(Context c, AppearanceSettings s)
    {
        var e = c.GetSharedPreferences(Prefs, FileCreationMode.Private)!.Edit()!;
        e.PutString("design", s.Design);
        e.PutString("font", s.Font);
        e.PutFloat("scale", (float)s.Scale);
        e.Apply();
    }

    /// <summary>Überträgt die Auswahl auf die Bausteine der Oberfläche.</summary>
    public static void Use(AppearanceSettings s)
    {
        Ui.D = s.Palette;
        Ui.FontFamily = s.FontChoice.Android;
        Ui.FontScale = s.Scale;
    }
}

public static partial class Dialogs
{
    /// <summary>Fragt einen Winkel in Grad ab (Ansicht drehen, Auswahl frei drehen).</summary>
    /// <summary>Text für eine Beschriftung abfragen.</summary>
    public static void AskText(Context c, string title, string label, Action<string> apply)
    {
        var body = Ui.Column(c, 8);
        var box = Ui.TextField(c, "");
        body.AddView(Ui.Field(c, label, box));
        Show(c, title, body, Lang.T("Übernehmen"), () =>
        {
            var text = (box.Text ?? "").Trim();
            if (text.Length == 0) return false;
            apply(text);
            return true;
        });
    }

    public static void AskAngle(Context c, string title, string hint, double value, string action, Action<double> apply)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, hint));
        var box = Ui.Number(c, value, 1, true);
        body.AddView(Ui.Field(c, Lang.T("Winkel (°, positiv = im Uhrzeigersinn)"), box));
        var quick = Ui.Row(c, 6);
        foreach (var a in new[] { -90, -45, -15, 15, 45, 90 })
            quick.AddView(Ui.Button(c, $"{(a > 0 ? "+" : "")}{a}°", () => box.Text = Fmt.Num(a, 1)), Ui.Weight(1, vertical: false));
        body.AddView(quick, Ui.Fill());
        var error = Error(c);
        body.AddView(error);
        Show(c, title, body, action, () =>
        {
            var errors = new List<string>();
            Read(box, Lang.T("Winkel"), -360, 360, errors, out var v);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            apply(v);
            return true;
        });
    }

    /// <summary>Design, Schriftart und Schriftgröße wählen. <paramref name="apply"/> bekommt nur eine geänderte Auswahl.</summary>
    public static void AskAppearance(Context c, AppearanceSettings current, Action<AppearanceSettings> apply)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, Lang.T("Das Design bestimmt Akzentfarbe, Zeichenfläche und Gleisdarstellung. Nach „Übernehmen“ wird die Oberfläche neu aufgebaut; der Plan bleibt erhalten.")));
        body.AddView(Section(c, Lang.T("Design")));
        var design = current.Design;
        var cards = new List<(LinearLayout Card, string Id)>();
        void Mark()
        {
            foreach (var (card, id) in cards)
                card.Background = Ui.Rounded(id == design ? Ui.TipBack : Ui.Panel, id == design ? Ui.Accent : Ui.Border, 8, id == design ? 2 : 1);
        }
        foreach (var d in Design.All)
        {
            var card = Ui.Row(c, 10);
            card.SetPadding(Ui.Dp(8), Ui.Dp(8), Ui.Dp(8), Ui.Dp(8));
            card.AddView(new DesignPreview(c, d), new LinearLayout.LayoutParams(Ui.Dp(96), Ui.Dp(48)));
            var text = Ui.Column(c, 0);
            text.AddView(Ui.Label(c, d.Name, 14, true));
            text.AddView(Ui.Label(c, d.Description, 12.5, color: Ui.TextSecondary));
            card.AddView(text, Ui.Weight(1, vertical: false));
            var id = d.Id;
            card.Click += (_, _) => { design = id; Mark(); };
            cards.Add((card, id));
            body.AddView(card, Ui.Fill());
        }
        Mark();

        var font = Choice(c, UiFonts.All.Select(f => f.Name).ToList(), UiFonts.All.ToList().FindIndex(f => f.Id == current.FontChoice.Id));
        var sizeIndex = UiFonts.Sizes.ToList().FindIndex(s => Math.Abs(s.Scale - current.Scale) < 0.01);
        var size = Choice(c, UiFonts.Sizes.Select(s => s.Name).ToList(), sizeIndex < 0 ? 1 : sizeIndex);
        body.AddView(Ui.Field(c, Lang.T("Schriftart"), font));
        body.AddView(Ui.Field(c, Lang.T("Schriftgröße"), size));
        var sample = Ui.Label(c, Lang.T("Gleisplan Bahnhof Neustadt · R 437,5 mm · 2,5 %"), 14);
        sample.Background = Ui.Rounded(Ui.Window);
        sample.SetPadding(Ui.Dp(10), Ui.Dp(8), Ui.Dp(10), Ui.Dp(8));
        void Sample()
        {
            var f = UiFonts.All[Math.Max(0, font.SelectedItemPosition)];
            sample.Typeface = Typeface.Create(f.Android, TypefaceStyle.Normal);
            sample.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)(14 * UiFonts.Sizes[Math.Max(0, size.SelectedItemPosition)].Scale));
        }
        font.ItemSelected += (_, _) => Sample();
        size.ItemSelected += (_, _) => Sample();
        Sample();
        body.AddView(sample, Ui.Fill());

        Show(c, Lang.T("Darstellung"), body, Lang.T("Übernehmen"), () =>
        {
            var chosen = new AppearanceSettings(design,
                UiFonts.All[Math.Max(0, font.SelectedItemPosition)].Id,
                UiFonts.Sizes[Math.Max(0, size.SelectedItemPosition)].Scale);
            if (chosen != current) apply(chosen);
            return true;
        });
    }

    /// <summary>Kleine Vorschau eines Designs: Zeichenfläche, Platte, Gleisbogen und Akzentleiste.</summary>
    private sealed class DesignPreview(Context c, Design d) : View(c)
    {
        private static Color C(Rgb v) => Color.Rgb(v.R, v.G, v.B);

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float w = Width, h = Height;
            using var p = new Paint { AntiAlias = true };
            canvas.DrawColor(C(d.Canvas));
            p.Color = C(d.Accent);
            canvas.DrawRect(0, 0, w, h * 0.12f, p);
            p.Color = C(d.Board);
            canvas.DrawRect(w * 0.08f, h * 0.24f, w * 0.92f, h * 0.9f, p);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = C(d.BoardStroke);
            p.StrokeWidth = 1;
            canvas.DrawRect(w * 0.08f, h * 0.24f, w * 0.92f, h * 0.9f, p);
            using var path = new Android.Graphics.Path();
            path.MoveTo(w * 0.2f, h * 0.84f);
            path.QuadTo(w * 0.3f, h * 0.42f, w * 0.62f, h * 0.48f);
            path.LineTo(w * 0.86f, h * 0.48f);
            foreach (var (color, width) in new[] { (d.Bed, h * 0.12f), (d.Sleeper, h * 0.08f), (d.Rail, Math.Max(1.2f, h * 0.025f)) })
            {
                p.Color = C(color);
                p.StrokeWidth = width;
                canvas.DrawPath(path, p);
            }
        }
    }

    /// <summary>Info über das Programm mit Logo, Version und Ausbaustufe.</summary>
    public static void ShowAbout(Context c, LicenseManager license, string version)
    {
        var body = Ui.Column(c, 6);
        body.SetGravity(GravityFlags.CenterHorizontal);
        var logo = new ImageView(c);
        logo.SetImageResource(c.Resources!.GetIdentifier("logo", "drawable", c.PackageName));
        logo.SetAdjustViewBounds(true);
        body.AddView(logo, new LinearLayout.LayoutParams(Ui.Dp(200), Ui.Dp(200)) { Gravity = GravityFlags.CenterHorizontal });
        foreach (var (text, size, bold) in new[]
                 {
                     ($"{AppFolders.Name} {license.EditionName}", 16.0, true),
                     (Lang.F($"Version {version}"), 13.0, false),
                     (Lang.T("Gleisplanung für Modellbahnanlagen – Gleise, Höhen, Gleiswendel, Landschaft, Elektrik und 3D."), 13.0, false),
                     (license.Summary, 12.0, false),
                 })
        {
            var t = Ui.Label(c, text, size, bold, bold ? Ui.Text : Ui.TextSecondary);
            t.Gravity = GravityFlags.CenterHorizontal;
            body.AddView(t, Ui.Fill());
        }
        Show(c, Lang.T("Über das Programm"), body, Lang.T("Schließen"), cancelText: null);
    }
}

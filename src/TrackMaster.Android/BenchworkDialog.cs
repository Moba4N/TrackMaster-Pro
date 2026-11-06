using Android.Content;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Maße des Holz-Unterbaus; danach die Materialliste.</summary>
    public static void AskBenchwork(Context c, PlanSession session, Action showList)
    {
        var plan = session.Plan;
        var s = plan.Benchwork ?? BenchworkSpec.Defaults(plan.Gauge);
        EditText Box(double v) => Ui.Number(c, v);
        var height = Box(s.Height); var top = Box(s.TopThickness); var ft = Box(s.FrameThickness); var fh = Box(s.FrameHeight); var cross = Box(s.CrossSpacing);
        var style = Choice(c, [Lang.T("Kantholz"), Lang.T("L-Bein aus zwei Latten")], (int)s.LegStyle);
        var lw = Box(s.LegWidth); var ld = Box(s.LegDepth); var lspace = Box(s.LegSpacing);
        var braces = new CheckBox(c) { Text = Lang.T("Fußtraversen in Höhe"), Checked = s.Braces };
        var bh = Box(s.BraceHeight);
        var feet = new CheckBox(c) { Text = Lang.T("Stellfüße, Höhe"), Checked = s.Feet };
        var foot = Box(s.FootHeight);
        var stock = Box(s.StockLength); var sw = Box(s.SheetWidth); var sd = Box(s.SheetDepth);
        var manual = new CheckBox(c) { Text = Lang.T("Rahmen selbst zeichnen"), Checked = s.Manual };
        var templates = BenchTemplate.All;
        var template = Choice(c, new[] { "…" }.Concat(templates.Select(t => t.Name)).ToList(), 0);
        string T(double v) => Fmt.Num(v, 0).Replace(".", "");
        template.ItemSelected += (_, e) =>
        {
            if (e.Position < 1) return;
            var v = templates[e.Position - 1].Spec(plan.Gauge);
            height.Text = T(v.Height); top.Text = T(v.TopThickness); ft.Text = T(v.FrameThickness); fh.Text = T(v.FrameHeight); cross.Text = T(v.CrossSpacing);
            style.SetSelection((int)v.LegStyle); lw.Text = T(v.LegWidth); ld.Text = T(v.LegDepth); lspace.Text = T(v.LegSpacing);
            braces.Checked = v.Braces; bh.Text = T(v.BraceHeight); feet.Checked = v.Feet; foot.Text = T(v.FootHeight); manual.Checked = v.Manual;
        };
        var body = Ui.Column(c, 6);
        body.AddView(Hint(c, Lang.F($"Holzkonstrukt unter der Platte ({BoardGeometry.Describe(plan)}): Zargen rundum und Querriegel unter jedem Plattenteil, Beine innen in den Ecken und entlang der langen Seiten. Alle Maße in mm. Die 3D-Ansicht zeigt den Unterbau.")));
        body.AddView(Ui.Field(c, Lang.T("Vorlage übernehmen"), template));
        body.AddView(manual);
        body.AddView(Ui.Label(c, Lang.T("Nur Deckplatte und eigene Leisten (Gelände › Leiste ziehen), kein berechneter Rahmen."), 12, color: Ui.TextSecondary));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Höhe Oberkante Platte über Boden"), height), Ui.Field(c, Lang.T("Plattenstärke (0 = offener Rahmen)"), top)));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Latte für Rahmen: Stärke"), ft), Ui.Field(c, Lang.T("Latte für Rahmen: Höhe"), fh)));
        body.AddView(Ui.Field(c, Lang.T("Querriegel höchstens alle"), cross));
        body.AddView(Ui.Field(c, Lang.T("Beine"), style));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Bein: Breite"), lw), Ui.Field(c, Lang.T("Bein: Tiefe"), ld)));
        body.AddView(Ui.Field(c, Lang.T("Beine höchstens alle"), lspace));
        body.AddView(braces);
        body.AddView(bh);
        body.AddView(feet);
        body.AddView(foot);
        body.AddView(Ui.Field(c, Lang.T("Lattenlänge im Handel"), stock));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Plattenformat: Breite"), sw), Ui.Field(c, Lang.T("Plattenformat: Tiefe"), sd)));
        var error = Error(c);
        body.AddView(error);
        (string Text, Action Click)? remove = null;
        if (plan.Benchwork is not null) remove = (Lang.T("Entfernen"), () => session.SetBenchwork(null));
        Show(c, Lang.T("Unterbau"), body, Lang.T("Übernehmen"), () =>
        {
            double V(EditText b) => Fmt.Parse(b.Text) ?? double.NaN;
            var spec = new BenchworkSpec
            {
                Height = V(height), TopThickness = V(top), FrameThickness = V(ft), FrameHeight = V(fh), CrossSpacing = V(cross),
                LegStyle = style.SelectedItemPosition == 1 ? BenchLegStyle.LBein : BenchLegStyle.Kantholz, LegWidth = V(lw), LegDepth = V(ld), LegSpacing = V(lspace),
                Braces = braces.Checked, BraceHeight = V(bh), Feet = feet.Checked, FootHeight = V(foot),
                StockLength = V(stock), SheetWidth = V(sw), SheetDepth = V(sd), Manual = manual.Checked,
            };
            var errors = Benchwork.Validate(spec);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            session.SetBenchwork(spec);
            showList();
            return true;
        }, neutral: remove);
    }

    /// <summary>Materialliste und Zuschnitt des Unterbaus.</summary>
    public static void ShowBenchwork(Context c, BenchworkPlan b, Action edit, Action copy, Action saveCsv)
    {
        var body = Ui.Column(c, 4);
        foreach (var n in b.Notes) body.AddView(Ui.Label(c, "• " + n, 12.5, color: Ui.TextSecondary));
        body.AddView(Ui.Label(c, Lang.T("Material"), 14, true));
        foreach (var m in b.Materials)
        {
            body.AddView(Ui.Label(c, $"{Fmt.Num(m.Quantity, 0)} {m.Unit}  {m.Item}  {m.Size}", 13.5));
            if (m.Note.Length > 0) body.AddView(Ui.Label(c, m.Note, 12, color: Ui.TextSecondary));
        }
        body.AddView(Ui.Label(c, Lang.T("Zuschnitt"), 14, true));
        foreach (var cut in b.Cuts) body.AddView(Ui.Label(c, $"{cut.Count} × {Fmt.Num(cut.Length, 0)} mm  {cut.Use}  {cut.Section} mm", 13));
        Android.App.AlertDialog? dialog = null;
        body.AddView(Ui.Button(c, Lang.T("Maße ändern…"), () => { dialog?.Dismiss(); edit(); }));
        body.AddView(Ui.Button(c, Lang.T("Kopieren"), copy));
        dialog = Show(c, Lang.T("Unterbau – Materialliste"), body, Lang.T("Schließen"), cancelText: null, neutral: (Lang.T("Als CSV speichern"), saveCsv));
    }
}

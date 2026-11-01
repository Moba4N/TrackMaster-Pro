using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;
using Path = Android.Graphics.Path;

namespace TrackMaster.Mobile;

/// <summary>Dialoge für Raum, Hintergrundbild, Parallelgleis, Höhenprofil, Rückmelder und die Stückliste mit Preisen.</summary>
public static partial class Dialogs
{
    // ---------- Raum ----------

    public static void AskRoom(Context c, LayoutPlan plan, Action<double, double, double, double, double> create)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, Lang.T("Innenmaße des Raums (von Wand zu Wand). Die Lage gibt an, wo die linke obere Innenecke relativ zur Anlagenplatte liegt – negativ heißt: links bzw. oberhalb der Platte.")));
        if (plan.Scenery.Any(s => s.Kind == SceneryKind.Room))
            body.AddView(Hint(c, Lang.T("Der Plan hat schon einen Raum; es kommt ein weiterer dazu. Den alten bei Bedarf wählen und löschen.")));
        var w = Ui.Number(c, Math.Round((plan.BoardWidth + 1200) / 100) * 100);
        var d = Ui.Number(c, Math.Round((plan.BoardHeight + 1200) / 100) * 100);
        var x = Ui.Number(c, -600, 0, true);
        var y = Ui.Number(c, -600, 0, true);
        var wall = Ui.Number(c, RoomPlan.DefaultWall);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Breite innen (mm)"), w), Ui.Field(c, Lang.T("Tiefe innen (mm)"), d)));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Innenecke X (mm)"), x), Ui.Field(c, Lang.T("Innenecke Y (mm)"), y)));
        body.AddView(Ui.Field(c, Lang.T("Wandstärke (mm)"), wall));
        var error = Error(c);
        body.AddView(error);
        Show(c, Lang.T("Raum"), body, Lang.T("Anlegen"), () =>
        {
            var errors = new List<string>();
            Read(w, Lang.T("Breite"), 300, 100000, errors, out var wv);
            Read(d, Lang.T("Tiefe"), 300, 100000, errors, out var dv);
            Read(x, "X", -100000, 100000, errors, out var xv);
            Read(y, "Y", -100000, 100000, errors, out var yv);
            Read(wall, Lang.T("Wandstärke"), 0, 1000, errors, out var wallv);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            create(xv, yv, wv, dv, wallv);
            return true;
        });
    }

    // ---------- Hintergrundbild ----------

    public static void AskBackground(Context c, PlanBackground bg, Action<double, double, double, double, bool> apply, Action remove, Action replace)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, Lang.F($"{bg.PixelWidth} × {bg.PixelHeight} Pixel, {Fmt.Num(bg.Width, 0)} × {Fmt.Num(bg.Height, 0)} mm"), 13, color: Ui.TextSecondary));
        var x = Ui.Number(c, bg.X, 1, true);
        var y = Ui.Number(c, bg.Y, 1, true);
        var w = Ui.Number(c, bg.Width, 1);
        var o = Ui.Number(c, bg.Opacity * 100);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Links (mm)"), x), Ui.Field(c, Lang.T("Oben (mm)"), y)));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Breite (mm)"), w), Ui.Field(c, Lang.T("Deckkraft (%)"), o)));
        var visible = new CheckBox(c) { Text = Lang.T("Bild zeigen"), Checked = bg.Visible };
        body.AddView(visible);
        body.AddView(Hint(c, Lang.T("Maßstab genau einstellen: Mit dem Werkzeug „Maß“ eine Linie über eine bekannte Strecke im Bild ziehen, die Linie wählen und „Nach Maß skalieren“ aufrufen.")));
        AlertDialog? dialog = null;
        var buttons = Ui.Row(c, 6);
        buttons.AddView(Ui.Button(c, Lang.T("Entfernen"), () => { dialog?.Dismiss(); remove(); }));
        buttons.AddView(Ui.Button(c, Lang.T("Anderes Bild…"), () => { dialog?.Dismiss(); replace(); }));
        body.AddView(buttons);
        var error = Error(c);
        body.AddView(error);
        dialog = Show(c, Lang.T("Hintergrundbild"), body, Lang.T("Übernehmen"), () =>
        {
            var errors = new List<string>();
            Read(x, Lang.T("Links"), -100000, 100000, errors, out var xv);
            Read(y, Lang.T("Oben"), -100000, 100000, errors, out var yv);
            Read(w, Lang.T("Breite"), 10, 200000, errors, out var wv);
            Read(o, Lang.T("Deckkraft"), 5, 100, errors, out var ov);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            apply(xv, yv, wv, ov / 100, visible.Checked);
            return true;
        });
    }

    public static void AskScaleBackground(Context c, double measured, Action<double> scale)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, Lang.F($"Die gewählte Maßlinie misst im Bild jetzt {Fmt.Num(measured, 0)} mm. Wie lang ist diese Strecke in Wirklichkeit? Das Bild wird passend vergrößert oder verkleinert.")));
        var len = Ui.Number(c, measured);
        body.AddView(Ui.Field(c, Lang.T("Echte Länge (mm)"), len));
        var error = Error(c);
        body.AddView(error);
        Show(c, Lang.T("Nach Maß skalieren"), body, Lang.T("Skalieren"), () =>
        {
            var errors = new List<string>();
            Read(len, Lang.T("Länge"), 1, 1000000, errors, out var v);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            scale(v);
            return true;
        });
    }

    // ---------- Parallelgleis ----------

    public static void AskParallel(Context c, Gauge gauge, Func<double, bool, bool> build)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, Lang.T("Legt ein Flexgleis im festen Abstand neben die gewählten Gleise – Bögen bekommen den passenden größeren bzw. kleineren Radius. Die Seite gilt in Fahrtrichtung, die Kette läuft möglichst von links nach rechts.")));
        var spacing = Ui.Number(c, Helix.DefaultSpacing(gauge));
        body.AddView(Ui.Field(c, Lang.T("Gleisabstand Mitte zu Mitte (mm)"), spacing));
        var group = new RadioGroup(c) { Orientation = Orientation.Horizontal };
        var left = new RadioButton(c) { Text = Lang.T("links (meist oben)"), Id = View.GenerateViewId() };
        var right = new RadioButton(c) { Text = Lang.T("rechts (meist unten)"), Id = View.GenerateViewId() };
        group.AddView(left);
        group.AddView(right);
        group.Check(left.Id);
        body.AddView(group);
        var error = Error(c);
        body.AddView(error);
        Show(c, Lang.T("Parallelgleis"), body, Lang.T("Anlegen"), () =>
        {
            var errors = new List<string>();
            Read(spacing, Lang.T("Gleisabstand"), gauge.TrackWidth * 1.5, 2000, errors, out var s);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            return build(s, left.Checked);
        });
    }

    // ---------- Höhenprofil ----------

    public static void ShowProfile(Context c, HeightProfile profile)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, profile.Summary(), 13, color: Ui.TextSecondary));
        body.AddView(new ProfileView(c, profile), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Ui.Dp(240)));
        body.AddView(Ui.Label(c, Lang.T("Höhe der Schienenoberkante in mm über der Platte, entlang der gewählten Gleise. Rote Werte liegen über der empfohlenen Steigung."), 12, color: Ui.TextSecondary));
        Show(c, Lang.T("Höhenprofil"), body, Lang.T("Schließen"), cancelText: null);
    }

    /// <summary>Diagramm des Höhenprofils, passend zur Breite der Ansicht.</summary>
    private sealed class ProfileView(Context c, HeightProfile profile) : View(c)
    {
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Width <= 0 || Height <= 0) return;
            // In Diagrammpixeln zeichnen und auf die Ansicht skalieren
            var scale = Ui.Density;
            double w = Width / scale, h = Height / scale;
            var chart = profile.Chart(w, h);
            if (chart.Line.Count == 0) return;
            canvas.Save();
            canvas.Scale(scale, scale);
            using var p = new Paint { AntiAlias = true };
            p.SetStyle(Paint.Style.Stroke);
            p.StrokeWidth = 1;
            p.Color = Color.Argb(0x30, 0x80, 0x80, 0x80);
            foreach (var (y, _) in chart.HeightTicks) canvas.DrawLine((float)chart.Left, (float)y, (float)chart.Right, (float)y, p);
            p.Color = Ui.TextSecondary;
            canvas.DrawLine((float)chart.Left, (float)chart.Top, (float)chart.Left, (float)chart.Bottom, p);
            canvas.DrawLine((float)chart.Left, (float)chart.Bottom, (float)chart.Right, (float)chart.Bottom, p);
            var line = new Path();
            var fill = new Path();
            fill.MoveTo((float)chart.Line[0].X, (float)chart.Bottom);
            for (var i = 0; i < chart.Line.Count; i++)
            {
                var (x, y) = chart.Line[i];
                if (i == 0) line.MoveTo((float)x, (float)y); else line.LineTo((float)x, (float)y);
                fill.LineTo((float)x, (float)y);
            }
            fill.LineTo((float)chart.Line[^1].X, (float)chart.Bottom);
            fill.Close();
            p.SetStyle(Paint.Style.Fill);
            p.Color = Color.Argb(0x30, Ui.Accent.R, Ui.Accent.G, Ui.Accent.B);
            canvas.DrawPath(fill, p);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = Ui.Accent;
            p.StrokeWidth = 2.5f;
            p.StrokeJoin = Paint.Join.Round;
            canvas.DrawPath(line, p);
            p.SetStyle(Paint.Style.Fill);
            foreach (var (x, y) in chart.Line) canvas.DrawCircle((float)x, (float)y, 3, p);
            using var t = new Paint { AntiAlias = true, TextSize = 11, Color = Ui.TextSecondary, TextAlign = Paint.Align.Right };
            t.SetTypeface(Ui.Font());
            foreach (var (y, text) in chart.HeightTicks) canvas.DrawText(text, (float)chart.Left - 6, (float)y + 4, t);
            t.TextAlign = Paint.Align.Center;
            foreach (var (x, text) in chart.DistanceTicks) canvas.DrawText(text, (float)x, (float)chart.Bottom + 16, t);
            foreach (var g in chart.Grades)
            {
                t.Color = g.Steep ? Ui.Danger : Ui.Text;
                canvas.DrawText(g.Text, (float)g.X, (float)g.Y, t);
            }
            canvas.Restore();
        }
    }

    // ---------- Rückmelder ----------

    public static void AskDetectorType(Context c, DetectorType current, Action<DetectorType> pick)
    {
        var body = Ui.Column(c, 6);
        body.AddView(Hint(c, Lang.T("Art wählen, dann auf die Gleise klicken. Die Kontakte werden fortlaufend nummeriert (16 je Rückmeldemodul, z. B. s88).")));
        AlertDialog? dialog = null;
        foreach (var t in Enum.GetValues<DetectorType>())
        {
            var type = t;
            var item = Ui.Column(c, 2);
            item.SetPadding(Ui.Dp(10), Ui.Dp(8), Ui.Dp(10), Ui.Dp(8));
            item.Background = Ui.Rounded(t == current ? Color.Rgb(0xE8, 0xF1, 0xFB) : Color.Rgb(0xFA, 0xFA, 0xFA), t == current ? Ui.Accent : Ui.Border);
            item.Clickable = true;
            item.AddView(Ui.Label(c, Detector.TypeName(t), 15, true));
            item.AddView(Ui.Label(c, t switch
            {
                DetectorType.Strom => Lang.T("Meldet ein besetztes Gleis über den Stromverbrauch (Zweileiter: eine Schiene getrennt)."),
                DetectorType.Kontakt => Lang.T("Meldet, wenn ein Zug über das Schaltgleis fährt (Märklin: Kontaktgleis bzw. Schaltgleis)."),
                _ => Lang.T("Reedkontakt im Gleis, ausgelöst durch einen Magneten unter dem Fahrzeug."),
            }, 12.5, color: Ui.TextSecondary));
            item.Click += (_, _) => { dialog?.Dismiss(); pick(type); };
            body.AddView(item, Ui.Fill());
        }
        dialog = Show(c, Lang.T("Rückmelder setzen"), body, okText: null);
    }

    // ---------- Stückliste mit Bestand und Preisen ----------

    /// <summary>
    /// Stückliste mit eigenem Bestand und Preisen je Artikel. „Übernehmen“ speichert die Werte im Plan;
    /// die Schaltflächen für CSV und Preisliste übernehmen sie vorher ebenfalls.
    /// </summary>
    public static void ShowBillOfMaterials(Context c, PlanSession session, bool pro, Action exportCsv, Action importPrices)
    {
        var plan = session.Plan;
        var rows = BillOfMaterials.Costs(plan);
        var stats = plan.Statistics();
        var body = Ui.Column(c, 6);
        body.AddView(Ui.Label(c, Lang.F($"{plan.Name} · Spur {plan.GaugeId} · {stats.Pieces} Teile · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m Gleis"), 13, color: Ui.TextSecondary));
        var stockBoxes = new Dictionary<string, EditText>();
        var priceBoxes = new Dictionary<string, EditText>();
        var buyTexts = new Dictionary<string, TextView>();
        var totals = Ui.Column(c, 2);
        if (rows.Count == 0) body.AddView(Ui.Label(c, Lang.T("Der Plan enthält noch keine Gleise."), 14));
        else body.AddView(Hint(c, Lang.T("Preise und eigenen Bestand eintragen: Die Liste zeigt dann, was noch zu kaufen ist und was es kostet. Die Werte werden mit dem Plan gespeichert.")));
        void Update()
        {
            double all = 0, buy = 0;
            var withPrice = 0;
            foreach (var r in rows)
            {
                var st = int.TryParse((stockBoxes[r.Key].Text ?? "").Trim(), out var n) && n > 0 ? n : 0;
                var toBuy = Math.Max(0, r.Count - st);
                var price = BillOfMaterials.ParseMoney((priceBoxes[r.Key].Text ?? "").Trim());
                buyTexts[r.Key].Text = Lang.F($"zu kaufen: {toBuy}") + (price is { } p ? $" · {BillOfMaterials.Money(p * toBuy)}" : "");
                buyTexts[r.Key].SetTextColor(toBuy > 0 ? Ui.TextSecondary : Ui.Success);
                if (price is { } pp) { withPrice++; all += pp * r.Count; buy += pp * toBuy; }
            }
            totals.RemoveAllViews();
            if (withPrice == 0) return;
            var lines = new List<(string, string, Color?)> { (Lang.T("Noch zu kaufen"), BillOfMaterials.Money(buy), Ui.Text), (Lang.T("Alles neu gekauft"), BillOfMaterials.Money(all), null) };
            if (withPrice < rows.Count) lines.Add((Lang.T("Ohne Preis"), Lang.F($"{rows.Count - withPrice} Artikel"), null));
            totals.AddView(Rows(c, [.. lines]), Ui.Fill());
        }
        foreach (var r in rows)
        {
            var row = Ui.Row(c, 10);
            var count = Ui.Label(c, $"{r.Count}×", 15, true);
            count.SetMinWidth(Ui.Dp(40));
            row.AddView(count);
            var text = Ui.Column(c, 0);
            text.AddView(Ui.Label(c, r.Article.Length > 0 ? $"{r.Article} · {r.Name}" : r.Name, 14));
            text.AddView(Ui.Label(c, $"{r.Detail} · {r.System}", 12.5, color: Ui.TextSecondary));
            var buyText = Ui.Label(c, "", 12.5, color: Ui.TextSecondary);
            buyTexts[r.Key] = buyText;
            text.AddView(buyText);
            row.AddView(text, Ui.Weight(1, vertical: false));
            var stock = Ui.TextField(c, r.Stock > 0 ? r.Stock.ToString() : "", Lang.T("Bestand"));
            stock.InputType = InputTypes.ClassNumber;
            var price = Ui.TextField(c, r.Price is { } p ? Fmt.Num(p, 2).Replace(Lang.IsEnglish ? "," : ".", "") : "", Lang.T("Preis"));
            price.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
            price.KeyListener = Android.Text.Method.DigitsKeyListener.GetInstance("0123456789,.");
            stockBoxes[r.Key] = stock;
            priceBoxes[r.Key] = price;
            row.AddView(stock, new LinearLayout.LayoutParams(Ui.Dp(64), ViewGroup.LayoutParams.WrapContent));
            row.AddView(price, new LinearLayout.LayoutParams(Ui.Dp(80), ViewGroup.LayoutParams.WrapContent));
            stock.AfterTextChanged += (_, _) => Update();
            price.AfterTextChanged += (_, _) => Update();
            body.AddView(row, Ui.Fill());
        }
        body.AddView(totals, Ui.Fill());
        var error = Error(c);
        body.AddView(error);
        if (rows.Count > 0) Update();

        bool Apply()
        {
            var changes = new List<(string Key, double? Price, int Stock)>();
            var errors = new List<string>();
            var changed = false;
            foreach (var r in rows)
            {
                var pt = (priceBoxes[r.Key].Text ?? "").Trim();
                double? price = pt.Length == 0 ? null : BillOfMaterials.ParseMoney(pt);
                if (pt.Length > 0 && price is null) { errors.Add(Lang.F($"{r.Name}: Preis bitte als Zahl eingeben.")); price = r.Price; }
                var st = (stockBoxes[r.Key].Text ?? "").Trim();
                var stock = st.Length == 0 ? 0 : int.TryParse(st, out var n) && n >= 0 ? n : -1;
                if (stock < 0) { errors.Add(Lang.F($"{r.Name}: Bestand bitte als ganze Zahl eingeben.")); stock = r.Stock; }
                if (price != r.Price || stock != r.Stock) changed = true;
                changes.Add((r.Key, price, stock));
            }
            SetError(error, errors);
            if (errors.Count > 0) return false;
            if (changed) session.SetCosts(changes);
            return true;
        }

        AlertDialog? dialog = null;
        var buttons = Ui.Row(c, 6);
        buttons.AddView(Ui.Button(c, pro ? Lang.T("Als CSV speichern") : Lang.T("CSV (Pro)"), () => { if (Apply()) exportCsv(); }));
        buttons.AddView(Ui.Button(c, Lang.T("Preisliste einlesen…"), () => { if (!Apply()) return; dialog?.Dismiss(); importPrices(); }));
        body.AddView(buttons);
        dialog = Show(c, Lang.T("Stückliste"), body, Lang.T("Übernehmen"), Apply);
    }
}

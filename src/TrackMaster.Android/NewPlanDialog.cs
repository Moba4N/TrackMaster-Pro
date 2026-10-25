using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;
using Path = Android.Graphics.Path;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    private static readonly string[] BoardTurns = ["Grundstellung", "90° gedreht", "180° gedreht", "270° gedreht"];

    /// <summary>Neuer Plan: Spur, Form der Anlagenplatte (Rechteck, L, U, T, Z, Ring), Maße und Ausrichtung.</summary>
    public static void AskNewPlan(Context c, string currentGauge, Action<BoardSetup> create)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, "Der aktuelle Plan wird verworfen; Rückgängig holt ihn zurück. Form und Maße lassen sich später unter „Plan“ ändern."));
        var gauges = Catalog.Gauges.ToList();
        var gauge = Choice(c, gauges.Select(g => $"Spur {g.Id} ({g.Scale})").ToList(), gauges.FindIndex(g => g.Id == currentGauge));
        body.AddView(Ui.Field(c, "Spur", gauge));
        var shapes = BoardGeometry.All;
        var shape = Choice(c, shapes.Select(BoardGeometry.Name).ToList(), 0);
        body.AddView(Ui.Field(c, "Form der Anlagenplatte", shape));
        var preview = new BoardPreview(c);
        body.AddView(preview, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Ui.Dp(150)));
        var start = BoardSetup.Default(gauges[Math.Max(0, gauge.SelectedItemPosition)].Id);
        var w = Ui.Number(c, start.Width);
        var h = Ui.Number(c, start.Height);
        var d = Ui.Number(c, start.Depth);
        var turn = Choice(c, BoardTurns, 0);
        body.AddView(Columns(c, Ui.Field(c, "Breite außen (mm)", w), Ui.Field(c, "Tiefe außen (mm)", h)));
        var legs = Columns(c, Ui.Field(c, "Schenkeltiefe (mm)", d), Ui.Field(c, "Ausrichtung", turn));
        body.AddView(legs);
        var info = Ui.Label(c, "", 12.5, color: Ui.TextSecondary);
        body.AddView(info);
        var error = Error(c);
        body.AddView(error);

        BoardShape Shape() => shapes[Math.Max(0, shape.SelectedItemPosition)];
        var suggested = false;
        void Update()
        {
            var s = Shape();
            legs.Visibility = BoardGeometry.HasLegs(s) ? ViewStates.Visible : ViewStates.Gone;
            double bw = Ui.Parse(w, 0), bh = Ui.Parse(h, 0), bd = Ui.Parse(d, 0);
            if (bw < 100 || bh < 100) return;
            var t = turn.SelectedItemPosition;
            preview.Outlines = BoardGeometry.Outlines(s, bw, bh, bd, t);
            var area = BoardGeometry.Rects(s, bw, bh, bd, t).Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)) / 1e6;
            info.Text = BoardGeometry.HasLegs(s)
                ? $"Außenmaße {Fmt.Num(bw, 0)} × {Fmt.Num(bh, 0)} mm · Schenkel {Fmt.Num(BoardGeometry.Depth(s, bw, bh, bd, t), 0)} mm · Fläche {Fmt.Num(area, 2)} m²"
                : $"{Fmt.Num(bw, 0)} × {Fmt.Num(bh, 0)} mm · Fläche {Fmt.Num(area, 2)} m²";
        }
        void Suggest()
        {
            // Spinner melden die erste Auswahl beim Anzeigen; dann nichts überschreiben
            if (!suggested) { suggested = true; Update(); return; }
            var s = BoardSetup.Default(gauges[Math.Max(0, gauge.SelectedItemPosition)].Id, Shape());
            w.Text = Fmt.Num(s.Width, 0).Replace(".", "");
            h.Text = Fmt.Num(s.Height, 0).Replace(".", "");
            d.Text = Fmt.Num(s.Depth, 0).Replace(".", "");
            Update();
        }
        var firstGauge = true;
        gauge.ItemSelected += (_, _) => { if (firstGauge) { firstGauge = false; return; } Suggest(); };
        shape.ItemSelected += (_, _) => Suggest();
        turn.ItemSelected += (_, _) => Update();
        foreach (var box in new[] { w, h, d }) box.AfterTextChanged += (_, _) => Update();
        Update();

        Show(c, "Neuer Plan", body, "Anlegen", () =>
        {
            var errors = new List<string>();
            Read(w, "Breite", 100, 50000, errors, out var bw);
            Read(h, "Tiefe", 100, 50000, errors, out var bh);
            var bd = 600.0;
            if (BoardGeometry.HasLegs(Shape())) Read(d, "Schenkeltiefe", 50, 50000, errors, out bd);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            create(new BoardSetup(gauges[Math.Max(0, gauge.SelectedItemPosition)].Id, Shape(), bw, bh, bd, turn.SelectedItemPosition));
            return true;
        });
    }

    /// <summary>Kleine Vorschau der Plattenform.</summary>
    private sealed class BoardPreview(Context c) : View(c)
    {
        private IReadOnlyList<IReadOnlyList<(double X, double Y)>> _outlines = [];
        private readonly Paint _fill = new(PaintFlags.AntiAlias) { Color = Ui.Board };
        private readonly Paint _stroke = new(PaintFlags.AntiAlias) { Color = Ui.BoardStroke, StrokeWidth = Ui.Dp(1.5f), StrokeJoin = Paint.Join.Round };

        public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines
        {
            get => _outlines;
            set { _outlines = value; Invalidate(); }
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (_outlines.Count == 0) return;
            var pts = _outlines.SelectMany(o => o).ToList();
            double maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
            float pad = Ui.Dp(8);
            var scale = (float)Math.Min((Width - 2 * pad) / maxX, (Height - 2 * pad) / maxY);
            float ox = (float)((Width - maxX * scale) / 2), oy = (float)((Height - maxY * scale) / 2);
            var path = new Path();
            path.SetFillType(Path.FillType.EvenOdd!);
            foreach (var o in _outlines)
            {
                path.MoveTo(ox + (float)o[0].X * scale, oy + (float)o[0].Y * scale);
                foreach (var q in o.Skip(1)) path.LineTo(ox + (float)q.X * scale, oy + (float)q.Y * scale);
                path.Close();
            }
            _fill.SetStyle(Paint.Style.Fill);
            canvas.DrawPath(path, _fill);
            _stroke.SetStyle(Paint.Style.Stroke);
            canvas.DrawPath(path, _stroke);
        }
    }
}

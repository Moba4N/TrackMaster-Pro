using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

/// <summary>
/// Gleisbibliothek als Leiste unter der Zeichenfläche: Spur, Hersteller, Kategorie und die Teile als Kacheln.
/// Tippen setzt das Teil am aktiven Gleisende an (ohne aktives Ende in der Bildmitte).
/// </summary>
public sealed class LibraryPanel : LinearLayout
{
    private readonly PlanSession _session;
    private readonly Func<(double X, double Y)> _center;
    private readonly LinearLayout _gauges;
    private readonly Spinner _systems;
    private readonly LinearLayout _categories;
    private readonly LinearLayout _pieces;
    private readonly HorizontalScrollView _pieceScroll;
    private string _category = Lang.T("Alle");
    private string _shownGauge = "";
    private bool _updatingSystems;

    private static readonly string[] Categories = [Lang.T("Alle"), Lang.T("Gerade"), Lang.T("Bögen"), Lang.T("Weichen"), Lang.T("Kreuzungen"), Lang.T("Flexgleis"), Lang.T("Sonstige")];

    public LibraryPanel(Context c, PlanSession session, Func<(double X, double Y)> center) : base(c)
    {
        _session = session;
        _center = center;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Ui.Panel);
        SetPadding(Ui.Dp(8), Ui.Dp(6), Ui.Dp(8), Ui.Dp(6));

        var top = Ui.Row(c, 6);
        _gauges = Ui.Row(c, 4);
        top.AddView(_gauges);
        _systems = new Spinner(c, SpinnerMode.Dropdown);
        _systems.ItemSelected += (_, e) =>
        {
            if (_updatingSystems) return;
            var g = _session.Plan.Gauge;
            if (e.Position < g.Systems.Count)
            {
                _session.SystemByGauge[g.Id] = g.Systems[e.Position].Id;
                FillPieces();
            }
        };
        top.AddView(_systems, Ui.Wrap());
        _categories = Ui.Row(c, 4);
        top.AddView(_categories);
        var topScroll = new HorizontalScrollView(c) { HorizontalScrollBarEnabled = false };
        topScroll.AddView(top);
        AddView(topScroll, Ui.Fill());

        _pieces = Ui.Row(c, 6);
        _pieceScroll = new HorizontalScrollView(c) { HorizontalScrollBarEnabled = false };
        _pieceScroll.AddView(_pieces);
        var lp = Ui.Fill();
        lp.TopMargin = Ui.Dp(6);
        AddView(_pieceScroll, lp);

        foreach (var cat in Categories)
        {
            var name = cat;
            var b = Ui.Button(c, cat, () => { _category = name; Refresh(force: true); });
            b.SetMinimumHeight(Ui.Dp(34));
            b.SetPadding(Ui.Dp(10), 0, Ui.Dp(10), 0);
            _categories.AddView(b);
        }
        session.Changed += () => Refresh();
        Refresh(force: true);
    }

    /// <summary>Neu aufbauen, wenn sich Spur oder Auswahl der Kategorie geändert hat.</summary>
    public void Refresh(bool force = false)
    {
        var gauge = _session.Plan.Gauge;
        if (!force && gauge.Id == _shownGauge) return;
        _shownGauge = gauge.Id;

        _gauges.RemoveAllViews();
        foreach (var g in Catalog.Gauges)
        {
            var id = g.Id;
            var b = Ui.Button(Context!, g.Id, () =>
            {
                if (_session.SetGauge(id)) Refresh(force: true);
            });
            b.SetMinimumHeight(Ui.Dp(34));
            b.SetPadding(Ui.Dp(10), 0, Ui.Dp(10), 0);
            Ui.SetButtonState(b, g.Id == gauge.Id, false);
            _gauges.AddView(b);
        }
        for (var i = 0; i < _categories.ChildCount; i++)
            if (_categories.GetChildAt(i) is Button b) Ui.SetButtonState(b, false, b.Text == _category);

        _updatingSystems = true;
        var names = gauge.Systems.Select(s => s.Name + (s.Verified ? "" : Lang.T(" (vorläufig)"))).ToList();
        _systems.Adapter = new ArrayAdapter<string>(Context!, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, names);
        _systems.SetSelection(Math.Max(0, gauge.Systems.ToList().IndexOf(_session.CurrentSystem)));
        _updatingSystems = false;
        FillPieces();
    }

    private bool Matches(TrackPieceDef d)
    {
        if (_category == Lang.T("Gerade")) return d.Kind == PieceKind.Straight;
        if (_category == Lang.T("Bögen")) return d.Kind == PieceKind.Curve;
        if (_category == Lang.T("Weichen")) return d.Kind == PieceKind.Switch;
        if (_category == Lang.T("Kreuzungen")) return d.Kind == PieceKind.Crossing;
        if (_category == Lang.T("Flexgleis")) return false;
        if (_category == Lang.T("Sonstige")) return d.Kind is not (PieceKind.Straight or PieceKind.Curve or PieceKind.Switch or PieceKind.Crossing);
        return true;
    }

    private void FillPieces()
    {
        _pieces.RemoveAllViews();
        var system = _session.CurrentSystem;
        if (_category == Lang.T("Alle") || _category == Lang.T("Flexgleis"))
        {
            var gauge = _session.Plan.Gauge;
            var stocks = Catalog.Flexes(gauge, system);
            foreach (var stock in stocks)
            {
                var sample = Catalog.Get(Catalog.FlexCurveKey(gauge.Id, Math.Round(gauge.TrackWidth * 30), 30, stock.SystemId));
                var flex = new PieceTile(Context!, sample, stocks.Count > 1 ? Lang.F($"Flexgleis {stock.Number}…") : Lang.T("Flexgleis…"), $"{(stock.Number == Lang.T("Flexgleis") ? Lang.T("allgemein") : stock.Number)} · {Fmt.Num(stock.Length, 0)} mm");
                flex.Click += (_, _) =>
                {
                    // Dieses Flexgleis gilt ab jetzt auch für „Flexgleis ziehen“ und „Lücke schließen“
                    _session.CurrentFlex = stock;
                    Dialogs.AskFlex(Context!, gauge, stock, key => _session.AttachFlex(key, _center()));
                };
                _pieces.AddView(flex, new LayoutParams(Ui.Dp(112), Ui.Dp(92)));
            }
        }
        foreach (var def in system.Pieces.Where(Matches))
        {
            var key = def.Key;
            var tile = new PieceTile(Context!, def);
            tile.Click += (_, _) =>
            {
                _session.Attach(key, _center());
            };
            _pieces.AddView(tile, new LayoutParams(Ui.Dp(112), Ui.Dp(92)));
        }
        _pieceScroll.ScrollTo(0, 0);
    }
}

/// <summary>Kachel eines Gleisteils: Vorschaubild, Artikelnummer und Maß.</summary>
public sealed class PieceTile : View
{
    private readonly TrackPieceDef _def;
    private readonly string _title;
    private readonly string _subtitle;
    private readonly Paint _text = new(PaintFlags.AntiAlias);

    public PieceTile(Context c, TrackPieceDef def, string? title = null, string? subtitle = null) : base(c)
    {
        _def = def;
        _title = title ?? def.Number;
        _subtitle = subtitle ?? def.Spec;
        Clickable = true;
        Background = Ui.Rounded(Color.Rgb(0xFA, 0xFA, 0xFA), Ui.Border);
        ContentDescription = $"{def.Name} {_title}";
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var w = Width;
        var h = Height;
        var thumbH = h * 0.56f;
        canvas.Save();
        canvas.Translate(0, Ui.Dp(2));
        PlanPainter.Thumbnail(canvas, _def, w, thumbH);
        canvas.Restore();
        var sd = Resources!.DisplayMetrics!.ScaledDensity;
        _text.Color = Ui.Text;
        _text.TextSize = 12 * sd;
        _text.FakeBoldText = true;
        _text.TextAlign = Paint.Align.Center;
        canvas.DrawText(Ellipsize(_title, w - Ui.Dp(8)), w / 2f, thumbH + 13 * sd, _text);
        _text.FakeBoldText = false;
        _text.Color = Ui.TextSecondary;
        _text.TextSize = 10.5f * sd;
        canvas.DrawText(Ellipsize(_subtitle, w - Ui.Dp(8)), w / 2f, thumbH + 27 * sd, _text);
    }

    private string Ellipsize(string s, float max)
    {
        if (_text.MeasureText(s) <= max) return s;
        while (s.Length > 1 && _text.MeasureText(s + "…") > max) s = s[..^1];
        return s + "…";
    }
}

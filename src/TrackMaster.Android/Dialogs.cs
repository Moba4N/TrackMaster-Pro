using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.Mobile;

/// <summary>Dialoge der Android-Version (Gegenstück zu Dialogs/ProDialogs unter Windows).</summary>
public static partial class Dialogs
{
    /// <summary>Dialog mit eigenem Inhalt. <paramref name="ok"/> gibt false zurück, wenn er offen bleiben soll (Eingabefehler).</summary>
    public static AlertDialog Show(Context c, string title, View body, string? okText = "OK", Func<bool>? ok = null, string? cancelText = "Abbrechen", (string Text, Action Click)? neutral = null)
    {
        var b = new AlertDialog.Builder(c).SetTitle(title)!.SetView(Ui.Scroll(c, body))!;
        if (okText is not null) b.SetPositiveButton(okText, (IDialogInterfaceOnClickListener?)null);
        if (cancelText is not null) b.SetNegativeButton(cancelText, (IDialogInterfaceOnClickListener?)null);
        if (neutral is { } n) b.SetNeutralButton(n.Text, (IDialogInterfaceOnClickListener?)null);
        var d = b.Create()!;
        d.ShowEvent += (_, _) =>
        {
            d.GetButton((int)DialogButtonType.Positive)?.SetOnClickListener(new Click(() => { if (ok?.Invoke() ?? true) d.Dismiss(); }));
            if (neutral is { } nn) d.GetButton((int)DialogButtonType.Neutral)?.SetOnClickListener(new Click(nn.Click));
        };
        d.Show();
        return d;
    }

    public static void Message(Context c, string title, string text)
    {
        var t = Ui.Label(c, text, 14);
        Show(c, title, t, "OK", cancelText: null);
    }

    private sealed class Click(Action action) : Java.Lang.Object, View.IOnClickListener
    {
        public void OnClick(View? v) => action();
    }

    private static TextView Hint(Context c, string text)
    {
        var t = Ui.Label(c, text, 13, color: Ui.TextSecondary);
        t.Background = Ui.Rounded(Ui.TipBack);
        t.SetPadding(Ui.Dp(10), Ui.Dp(8), Ui.Dp(10), Ui.Dp(8));
        return t;
    }

    private static TextView Error(Context c)
    {
        var t = Ui.Label(c, "", 13, color: Ui.Danger);
        t.Visibility = ViewStates.Gone;
        return t;
    }

    private static void SetError(TextView t, IReadOnlyList<string> errors)
    {
        t.Text = string.Join("\n", errors);
        t.Visibility = errors.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
    }

    private static bool Read(EditText box, string name, double min, double max, List<string> errors, out double value)
    {
        value = Ui.Parse(box, double.NaN);
        if (double.IsNaN(value)) { errors.Add($"{name}: bitte eine Zahl eingeben."); return false; }
        if (value < min || value > max) { errors.Add($"{name}: zwischen {Fmt.Num(min)} und {Fmt.Num(max)}."); return false; }
        return true;
    }

    private static View Rows(Context c, params (string Key, string Value, Color? Color)[] rows)
    {
        var col = Ui.Column(c, 4);
        foreach (var (k, v, color) in rows)
        {
            var row = Ui.Row(c, 8);
            row.AddView(Ui.Label(c, k, 13.5, color: Ui.TextSecondary), Ui.Weight(1, vertical: false));
            var value = Ui.Label(c, v, 13.5, color is not null, color);
            value.Gravity = GravityFlags.End;
            row.AddView(value, Ui.Weight(1.2f, vertical: false));
            col.AddView(row, Ui.Fill());
        }
        return col;
    }

    private static TextView Section(Context c, string title)
    {
        var t = Ui.Label(c, title, 13, true, Ui.TextSecondary);
        t.SetPadding(0, Ui.Dp(10), 0, 0);
        return t;
    }

    private static LinearLayout Columns(Context c, params View[] views)
    {
        var row = Ui.Row(c, 10);
        foreach (var v in views) row.AddView(v, Ui.Weight(1, vertical: false));
        return row;
    }

    private static Spinner Choice(Context c, IReadOnlyList<string> items, int selected)
    {
        var s = new Spinner(c, SpinnerMode.Dropdown)
        {
            Adapter = new ArrayAdapter<string>(c, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, items.ToList()),
        };
        s.SetSelection(Math.Clamp(selected, 0, Math.Max(0, items.Count - 1)));
        return s;
    }

    // ---------- Eigenschaften ----------

    /// <summary>Eigenschaften des gewählten Gleises, Objekts oder (ohne Auswahl) des Plans. Änderungen gelten mit OK.</summary>
    public static void ShowProperties(Context c, PlanSession session, Action<string> run)
    {
        var sel = session.SelectedPieces;
        if (session.SelectedObject is { } obj && session.FindObject(obj) is { } o) ObjectProperties(c, session, o, run);
        else if (sel.Count == 1) PieceProperties(c, session, sel[0], run);
        else if (sel.Count > 1) MultiProperties(c, session, sel, run);
        else PlanProperties(c, session, run);
    }

    private static void PlanProperties(Context c, PlanSession session, Action<string> run)
    {
        var plan = session.Plan;
        var stats = plan.Statistics(session.Connections);
        var gauge = plan.Gauge;
        var body = Ui.Column(c, 8);
        var name = Ui.TextField(c, plan.Name);
        body.AddView(Ui.Field(c, "Planname", name));
        body.AddView(Section(c, "Anlagenplatte"));
        var shapes = BoardGeometry.All;
        var shape = Choice(c, shapes.Select(BoardGeometry.Name).ToList(), shapes.ToList().IndexOf(plan.BoardShape));
        body.AddView(Ui.Field(c, "Form", shape));
        var w = Ui.Number(c, plan.BoardWidth);
        var h = Ui.Number(c, plan.BoardHeight);
        body.AddView(Columns(c, Ui.Field(c, "Breite außen (mm)", w), Ui.Field(c, "Tiefe außen (mm)", h)));
        var depth = Ui.Number(c, BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn));
        var turn = Choice(c, BoardTurns, plan.BoardTurn);
        var legs = Columns(c, Ui.Field(c, "Schenkeltiefe (mm)", depth), Ui.Field(c, "Ausrichtung", turn));
        body.AddView(legs);
        void SyncLegs() => legs.Visibility = BoardGeometry.HasLegs(shapes[Math.Max(0, shape.SelectedItemPosition)]) ? ViewStates.Visible : ViewStates.Gone;
        shape.ItemSelected += (_, _) => SyncLegs();
        SyncLegs();
        body.AddView(Section(c, "Übersicht"));
        body.AddView(Rows(c,
            ("Spur", $"{gauge.Id} · {gauge.Scale} · {Fmt.Num(gauge.TrackWidth)} mm", null),
            ("Gleisstücke", stats.Pieces.ToString(), null),
            ("Weichen und Kreuzungen", stats.Switches.ToString(), null),
            ("Gleislänge", $"{Fmt.Num(stats.TrackLengthMm / 1000, 2)} m", null),
            ("Kleinster Radius", stats.SmallestRadius is { } r ? $"{Fmt.Num(r)} mm" : "–", null),
            ("Größte Steigung", stats.MaxGrade > 0 ? $"{Fmt.Num(stats.MaxGrade * 100, 1)} %" : "eben", stats.MaxGrade * 100 > PlanCheck.WarnGradePercent ? Ui.Danger : null),
            ("Höchster Punkt", $"{Fmt.Num(stats.MaxHeight, 0)} mm", null),
            ("Gelände / Elektrik", $"{plan.Scenery.Count} Obj. · {plan.Feeders.Count} Einsp. · {plan.Signals.Count} Sig.", null),
            ("Offene Gleisenden", stats.OpenEnds.ToString(), stats.OpenEnds > 0 ? Ui.OpenEnd : Ui.Success)));
        body.AddView(Hint(c, "So geht’s: Tippe ein orangefarbenes Gleisende an und dann ein Gleis in der Bibliothek – es wird dort angesetzt. " +
                             "Gewählte Gleise lassen sich mit dem Finger verschieben und rasten an freien Enden ein. Zwei Finger zoomen und verschieben die Ansicht, " +
                             "langes Drücken nimmt weitere Gleise zur Auswahl hinzu."));
        Show(c, "Plan", body, "Übernehmen", () =>
        {
            var errors = new List<string>();
            Read(w, "Breite", 100, 50000, errors, out var bw);
            Read(h, "Tiefe", 100, 50000, errors, out var bh);
            var newShape = shapes[Math.Max(0, shape.SelectedItemPosition)];
            var bd = plan.BoardDepth;
            if (BoardGeometry.HasLegs(newShape)) Read(depth, "Schenkeltiefe", 50, 50000, errors, out bd);
            if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
            var n = (name.Text ?? "").Trim();
            var t = turn.SelectedItemPosition;
            if (n != plan.Name || bw != plan.BoardWidth || bh != plan.BoardHeight || newShape != plan.BoardShape || bd != plan.BoardDepth || t != plan.BoardTurn)
                session.Edit(() =>
                {
                    if (n.Length > 0) plan.Name = n;
                    plan.BoardWidth = bw;
                    plan.BoardHeight = bh;
                    plan.BoardShape = newShape;
                    plan.BoardTurn = t;
                    plan.BoardDepth = BoardGeometry.Depth(newShape, bw, bh, bd, t);
                });
            return true;
        }, neutral: ("Stückliste", () => run("bom")));
    }

    private static void PieceProperties(Context c, PlanSession session, PlacedPiece p, Action<string> run)
    {
        var d = p.Def;
        var pro = session.IsPro();
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, d.Name, 16, true));
        body.AddView(Ui.Label(c, $"{d.SystemName} · {d.Number}", 13, color: Ui.TextSecondary));
        if (!d.Verified)
        {
            var warn = Ui.Label(c, "Geometrie vorläufig – vor dem Bau mit dem Herstellerkatalog prüfen.", 12.5, color: Ui.Warning);
            warn.Background = Ui.Rounded(Ui.WarningBack);
            warn.SetPadding(Ui.Dp(8), Ui.Dp(6), Ui.Dp(8), Ui.Dp(6));
            body.AddView(warn);
        }
        var leftHanded = !d.RightHand != p.Flipped;
        body.AddView(d.Kind switch
        {
            PieceKind.Curve => Rows(c, ("Radius", $"{Fmt.Num(d.Radius)} mm", null), ("Winkel", $"{Fmt.Num(d.Angle)}°", null), ("Bogenlänge", $"{Fmt.Num(d.TrackLength)} mm", null), ("Richtung", leftHanded ? "links" : "rechts", null)),
            PieceKind.Switch when !d.IsSimpleSwitch => Rows(c, ("Maße", d.Spec, null), ("Stränge", $"{d.RouteCount}", null), ("Antriebe", $"{d.Drives}", null)),
            PieceKind.Switch => Rows(c, ("Länge", $"{Fmt.Num(d.Length)} mm", null), ("Abzweig", $"R {Fmt.Num(d.Radius)} · {Fmt.Num(d.Angle)}°", null), ("Richtung", leftHanded ? "links" : "rechts", null)),
            PieceKind.Crossing => Rows(c, ("Länge", $"{Fmt.Num(d.Length)} mm", null), ("Kreuzungswinkel", $"{Fmt.Num(d.Angle)}°", null)),
            _ => Rows(c, ("Länge", $"{Fmt.Num(d.Length)} mm", null)),
        });
        body.AddView(Section(c, "Position"));
        var x = Ui.Number(c, p.X, 1, true);
        var y = Ui.Number(c, p.Y, 1, true);
        var rot = Ui.Number(c, p.Rotation, 1, true);
        body.AddView(Columns(c, Ui.Field(c, "X (mm)", x), Ui.Field(c, "Y (mm)", y), Ui.Field(c, "Drehung (°)", rot)));
        body.AddView(Section(c, "Höhe" + (pro ? "" : " (Pro)")));
        var height = Ui.Number(c, p.Height, 1, true);
        var grade = Ui.Number(c, p.EffectiveGrade * 100, 2, true);
        height.Enabled = grade.Enabled = pro;
        body.AddView(Columns(c, Ui.Field(c, "Höhe Anfang (mm)", height), Ui.Field(c, "Steigung (%)", grade)));
        var heights = p.ConnectorHeights();
        if (heights.Count > 1) body.AddView(Rows(c, ("Höhe am Ende", $"{Fmt.Num(heights[1], 1)} mm", null)));
        var tunnel = new CheckBox(c) { Text = "liegt im Tunnel", Checked = p.Tunnel, Enabled = pro };
        body.AddView(tunnel);
        EditText? address = null;
        if (d.Drives > 0)
        {
            body.AddView(Section(c, "Digital" + (pro ? "" : " (Pro)")));
            address = Ui.Number(c, p.Address ?? 0);
            address.Enabled = pro;
            body.AddView(Ui.Field(c, "Adresse (0 = keine)", address));
        }
        var conns = session.Connections.Of(p.Id).ToList();
        var linked = conns.Count(cn => !cn.IsOpen);
        body.AddView(Rows(c, ("Verbindungen", $"{linked} von {conns.Count} verbunden", linked == conns.Count ? Ui.Success : Ui.OpenEnd)));
        AlertDialog? dialog = null;
        var buttons = Ui.Row(c, 6);
        void Cmd(string text, string command) => buttons.AddView(Ui.Button(c, text, () => { dialog?.Dismiss(); run(command); }));
        Cmd("Drehen", "rotR");
        if (d.CanFlip) Cmd("Spiegeln", "flip");
        if (conns.Count > 1) Cmd("Anschluss", "swap");
        Cmd("Löschen", "delete");
        var scroll = new HorizontalScrollView(c);
        scroll.AddView(buttons);
        body.AddView(scroll);
        dialog = Show(c, "Gleis", body, "Übernehmen", () =>
        {
            var errors = new List<string>();
            Read(x, "X", -100000, 100000, errors, out var vx);
            Read(y, "Y", -100000, 100000, errors, out var vy);
            Read(rot, "Drehung", -100000, 100000, errors, out var vr);
            Read(height, "Höhe", -500, 3000, errors, out var vh);
            Read(grade, "Steigung", -30, 30, errors, out var vg);
            double va = 0;
            if (address is not null) Read(address, "Adresse", 0, 2048, errors, out va);
            if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
            int? addr = address is null ? p.Address : va >= 1 ? (int)Math.Round(va) : null;
            var changed = Math.Abs(vx - p.X) > 1e-6 || Math.Abs(vy - p.Y) > 1e-6 || Math.Abs(Angles.Normalize(vr) - p.Rotation) > 1e-6
                          || Math.Abs(vh - p.Height) > 1e-6 || Math.Abs(vg / 100 - p.EffectiveGrade) > 1e-9 || tunnel.Checked != p.Tunnel || addr != p.Address;
            if (changed)
                session.Edit(() =>
                {
                    p.X = vx; p.Y = vy; p.Rotation = Angles.Normalize(vr);
                    if (pro)
                    {
                        p.Height = vh;
                        if (Math.Abs(vg / 100 - p.EffectiveGrade) > 1e-9) p.Grade = vg / 100;
                        p.Tunnel = tunnel.Checked;
                        p.Address = addr;
                    }
                });
            return true;
        });
    }

    private static void MultiProperties(Context c, PlanSession session, IReadOnlyList<PlacedPiece> sel, Action<string> run)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Rows(c, ("Gleisstücke", sel.Count.ToString(), null), ("Gleislänge", $"{Fmt.Num(sel.Sum(p => p.Def.TrackLength) / 1000, 2)} m", null)));
        AlertDialog? dialog = null;
        var grid = Ui.Column(c, 6);
        void Line(params (string Text, string Command)[] items)
        {
            var row = Ui.Row(c, 6);
            foreach (var (t, cmd) in items) row.AddView(Ui.Button(c, t, () => { dialog?.Dismiss(); run(cmd); }), Ui.Weight(1, vertical: false));
            grid.AddView(row, Ui.Fill());
        }
        Line(("15° links", "rotL"), ("15° rechts", "rotR"));
        Line(("Duplizieren", "duplicate"), ("Löschen", "delete"));
        Line(("Steigung…", "gradient"), (sel.All(p => p.Tunnel) ? "Tunnel aufheben" : "Als Tunnel", "tunnel"));
        body.AddView(grid);
        body.AddView(Hint(c, "Ziehe die Auswahl an ein freies Gleisende – die ganze Gruppe rastet dort ein."));
        dialog = Show(c, $"{sel.Count} Gleise gewählt", body, "Schließen", cancelText: null);
    }

    private static void ObjectProperties(Context c, PlanSession session, object o, Action<string> run)
    {
        var plan = session.Plan;
        var pro = session.IsPro();
        var body = Ui.Column(c, 8);
        AlertDialog? dialog = null;
        void DeleteButton() => body.AddView(Ui.Button(c, "Löschen", () => { dialog?.Dismiss(); run("delete"); }), Ui.Wrap());
        switch (o)
        {
            case SceneryItem s:
            {
                var name = Ui.TextField(c, s.Name);
                name.Enabled = pro;
                body.AddView(Ui.Field(c, "Name", name));
                var fields = new List<(EditText Box, string Name, double Min, double Max, Action<double> Apply)>();
                EditText Num(double v, string label, double min, double max, Action<double> apply)
                {
                    var e = Ui.Number(c, v, 0, min < 0);
                    e.Enabled = pro;
                    fields.Add((e, label, min, max, apply));
                    return e;
                }
                switch (s.Kind)
                {
                    case SceneryKind.Terrain:
                        body.AddView(Ui.Field(c, "Höhe (mm)", Num(s.Height, "Höhe", -500, 3000, v => s.Height = v)));
                        body.AddView(Rows(c, ("Eckpunkte", s.Points.Count.ToString(), null), ("Fläche", $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null)));
                        break;
                    case SceneryKind.Water:
                        body.AddView(Ui.Field(c, "Tiefe (mm)", Num(s.Height, "Tiefe", 0, 200, v => s.Height = v)));
                        body.AddView(Rows(c, ("Fläche", $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null)));
                        break;
                    case SceneryKind.Building:
                        body.AddView(Columns(c,
                            Ui.Field(c, "Breite (mm)", Num(s.Width, "Breite", 5, 5000, v => s.Width = v)),
                            Ui.Field(c, "Tiefe (mm)", Num(s.Depth, "Tiefe", 5, 5000, v => s.Depth = v))));
                        body.AddView(Columns(c,
                            Ui.Field(c, "Höhe (mm)", Num(s.Height, "Höhe", 5, 2000, v => s.Height = v)),
                            Ui.Field(c, "Drehung (°)", Num(s.Rotation, "Drehung", -360, 360, v => s.Rotation = Angles.Normalize(v)))));
                        break;
                    case SceneryKind.Tree:
                        body.AddView(Columns(c,
                            Ui.Field(c, "Durchmesser (mm)", Num(s.Width, "Durchmesser", 3, 1000, v => { s.Width = v; s.Depth = v; })),
                            Ui.Field(c, "Höhe (mm)", Num(s.Height, "Höhe", 5, 2000, v => s.Height = v))));
                        break;
                }
                DeleteButton();
                body.AddView(Hint(c, "Ein gewähltes Objekt lässt sich mit dem Finger verschieben."));
                dialog = Show(c, SceneryItem.KindName(s.Kind), body, "Übernehmen", () =>
                {
                    if (!pro) return true;
                    var errors = new List<string>();
                    var values = fields.Select(f => (f, ok: Read(f.Box, f.Name, f.Min, f.Max, errors, out var v), v)).ToList();
                    if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
                    var n = (name.Text ?? "").Trim();
                    session.Edit(() =>
                    {
                        if (n.Length > 0) s.Name = n;
                        foreach (var (f, _, v) in values) f.Apply(v);
                    });
                    return true;
                });
                break;
            }
            case Feeder f:
            {
                var circuits = plan.Feeders.Select(x => x.Circuit).Append("Stromkreis 1").Append("Stromkreis 2").Append("Booster 1").Append("Booster 2").Distinct().ToList();
                var circuit = new AutoCompleteTextView(c) { Text = f.Circuit, Threshold = 0, Enabled = pro };
                circuit.Adapter = new ArrayAdapter<string>(c, global::Android.Resource.Layout.SimpleDropDownItem1Line, circuits);
                circuit.Click += (_, _) => circuit.ShowDropDown();
                body.AddView(Ui.Field(c, "Stromkreis / Booster", circuit));
                var section = Electrics.Sections(plan).FirstOrDefault(x => x.PieceIds.Contains(f.PieceId));
                body.AddView(Rows(c, ("Gleis", $"{plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId})", null),
                    ("Stromabschnitt", section is null ? "–" : $"{section.Number} · {Fmt.Num(section.LengthMm / 1000, 2)} m", null)));
                DeleteButton();
                dialog = Show(c, "Einspeisung", body, "Übernehmen", () =>
                {
                    var t = (circuit.Text ?? "").Trim();
                    if (pro && t.Length > 0 && t != f.Circuit) session.Edit(() => f.Circuit = t);
                    return true;
                });
                break;
            }
            case Isolation i:
                body.AddView(Rows(c, ("Gleis", $"{plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId})", null), ("Gleisende", i.Index.ToString(), null),
                    ("Ausführung", Electrics.IsThreeRail(plan) ? "Mittelleiter getrennt" : "beide Schienen isoliert", null)));
                DeleteButton();
                dialog = Show(c, "Trennstelle", body, "Schließen", cancelText: null);
                break;
            case Signal g:
            {
                var name = Ui.TextField(c, g.Name);
                name.Enabled = pro;
                body.AddView(Ui.Field(c, "Name", name));
                var types = Enum.GetValues<SignalType>();
                var type = Choice(c, types.Select(Signal.TypeName).ToList(), Array.IndexOf(types, g.Type));
                type.Enabled = pro;
                body.AddView(Ui.Field(c, "Art", type));
                var address = Ui.Number(c, g.Address ?? 0);
                address.Enabled = pro;
                body.AddView(Ui.Field(c, "Adresse (0 = keine)", address));
                body.AddView(Rows(c, ("Belegt Adressen", g.AddressCount.ToString(), null), ("Gleis", $"{plan.Find(g.PieceId)?.Def.Number} (Nr. {g.PieceId}), Ende {g.Index}", null)));
                DeleteButton();
                dialog = Show(c, "Signal", body, "Übernehmen", () =>
                {
                    if (!pro) return true;
                    var errors = new List<string>();
                    Read(address, "Adresse", 0, 2048, errors, out var a);
                    if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
                    var t = types[Math.Max(0, type.SelectedItemPosition)];
                    var n = (name.Text ?? "").Trim();
                    int? addr = a >= 1 ? (int)Math.Round(a) : null;
                    if (n != g.Name || t != g.Type || addr != g.Address) session.Edit(() => { g.Name = n; g.Type = t; g.Address = addr; });
                    return true;
                });
                break;
            }
        }
    }

    // ---------- Gleise ----------

    public static void AskFlex(Context c, Gauge gauge, FlexStock stock, Action<string> attach)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, $"Flexgleis {stock.Name}{(stock.Number == "Flexgleis" ? "" : $" ({stock.Number})")}. Es wird am aktiven Gleisende angesetzt; Bögen lassen sich danach spiegeln. In der Stückliste wird die Gesamtlänge in Stücke zu {Fmt.Num(stock.Length, 0)} mm umgerechnet."));
        var kinds = new RadioGroup(c) { Orientation = Orientation.Horizontal };
        var straight = new RadioButton(c) { Text = "Gerade", Id = 1 };
        var curve = new RadioButton(c) { Text = "Bogen", Id = 2 };
        kinds.AddView(straight);
        kinds.AddView(curve);
        kinds.Check(1);
        body.AddView(kinds);
        var length = Ui.Number(c, Math.Round(stock.Length / 2));
        var radius = Ui.Number(c, Math.Round(gauge.TrackWidth * 30));
        var angle = Ui.Number(c, 30);
        var lengthField = Ui.Field(c, "Länge (mm)", length);
        var curveFields = Columns(c, Ui.Field(c, "Radius (mm)", radius), Ui.Field(c, "Winkel (°)", angle));
        body.AddView(lengthField);
        body.AddView(curveFields);
        void Sync()
        {
            lengthField.Visibility = straight.Checked ? ViewStates.Visible : ViewStates.Gone;
            curveFields.Visibility = straight.Checked ? ViewStates.Gone : ViewStates.Visible;
        }
        kinds.CheckedChange += (_, _) => Sync();
        Sync();
        var error = Error(c);
        body.AddView(error);
        Show(c, "Flexgleis", body, "Ansetzen", () =>
        {
            var errors = new List<string>();
            string? key = null;
            if (straight.Checked)
            {
                if (Read(length, "Länge", 5, 5000, errors, out var l)) key = Catalog.FlexStraightKey(gauge.Id, l, stock.SystemId);
            }
            else
            {
                var okR = Read(radius, "Radius", 50, 20000, errors, out var r);
                var okA = Read(angle, "Winkel", 1, 180, errors, out var a);
                if (okR && okA) key = Catalog.FlexCurveKey(gauge.Id, r, a, stock.SystemId);
            }
            SetError(error, errors);
            if (errors.Count > 0 || key is null) return false;
            attach(key);
            return true;
        });
    }

    public static void AskGradient(Context c, int pieces, double startHeight, Action<double, double, bool> apply)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, $"Die Steigung wird gleichmäßig über die {pieces} gewählten Gleise gelegt, vom einen Ende der Gleisfolge zum anderen. " +
                             "Empfohlen: höchstens 2,5–3 %, in Bögen weniger. Negative Werte ergeben ein Gefälle."));
        var start = Ui.Number(c, startHeight, 0, true);
        var percent = Ui.Number(c, 2.5, 1, true);
        body.AddView(Ui.Field(c, "Höhe am Anfang (mm über Platte)", start));
        body.AddView(Ui.Field(c, "Steigung (%)", percent));
        var reverse = new CheckBox(c) { Text = "Am anderen Ende beginnen" };
        body.AddView(reverse);
        var error = Error(c);
        body.AddView(error);
        Show(c, "Steigung", body, "Anlegen", () =>
        {
            var errors = new List<string>();
            Read(start, "Höhe am Anfang", -500, 3000, errors, out var s);
            Read(percent, "Steigung", -15, 15, errors, out var p);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            apply(s, p, reverse.Checked);
            return true;
        });
    }

    public static void AskHelix(Context c, Gauge gauge, IReadOnlyList<TrackPieceDef> curves, bool fromEnd, Action<string, double, double, bool> build)
    {
        var ordered = curves.OrderBy(x => x.Radius).ToList();
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, fromEnd
            ? "Die Wendel beginnt am aktiven Gleisende und steigt gleichmäßig an."
            : "Die Wendel entsteht in der Mitte der Ansicht. Tipp: Vorher ein Gleisende antippen, dann schließt sie dort an."));
        var curve = Choice(c, ordered.Select(x => $"{x.Number} – R {Fmt.Num(x.Radius)} mm, {Fmt.Num(x.Angle)}°").ToList(), Math.Min(1, ordered.Count - 1));
        body.AddView(Ui.Field(c, "Bogengleis", curve));
        var turns = Ui.Number(c, 2, 2);
        var rise = Ui.Number(c, gauge.ClearanceHeight + 10);
        body.AddView(Columns(c, Ui.Field(c, "Windungen", turns), Ui.Field(c, "Höhe je Windung (mm)", rise)));
        var direction = Choice(c, ["links herum (gegen den Uhrzeigersinn)", "rechts herum (im Uhrzeigersinn)"], 0);
        body.AddView(Ui.Field(c, "Richtung", direction));
        var info = Ui.Label(c, "", 12.5, color: Ui.TextSecondary);
        body.AddView(info);
        void Update()
        {
            if (ordered.Count == 0) return;
            var d = ordered[Math.Max(0, curve.SelectedItemPosition)];
            var r = Ui.Parse(rise, 0);
            var t = Ui.Parse(turns, 0);
            info.Text = $"Steigung {Fmt.Num(r / (2 * Math.PI * d.Radius) * 100, 1)} % · {Math.Round(360 / d.Angle * t)} Bögen · Gesamthöhe {Fmt.Num(r * t, 0)} mm · Durchmesser {Fmt.Num(2 * d.Radius + gauge.TrackWidth * 3, 0)} mm";
        }
        curve.ItemSelected += (_, _) => Update();
        turns.TextChanged += (_, _) => Update();
        rise.TextChanged += (_, _) => Update();
        Update();
        var error = Error(c);
        body.AddView(error);
        Show(c, "Gleiswendel", body, "Bauen", () =>
        {
            var errors = new List<string>();
            if (ordered.Count == 0) errors.Add("Dieses Gleissystem hat kein passendes Bogengleis.");
            Read(turns, "Windungen", 0.25, 20, errors, out var t);
            Read(rise, "Höhe je Windung", 10, 1000, errors, out var r);
            if (errors.Count == 0 && r < gauge.ClearanceHeight)
                errors.Add($"Mit {Fmt.Num(r, 0)} mm passt kein Zug unter die nächste Windung (mindestens {Fmt.Num(gauge.ClearanceHeight, 0)} mm).");
            SetError(error, errors);
            if (errors.Count > 0) return false;
            build(ordered[Math.Max(0, curve.SelectedItemPosition)].Key, t, r, direction.SelectedItemPosition == 1);
            return true;
        });
    }

    public static void AskSignalType(Context c, SignalType current, Action<SignalType> pick)
    {
        var types = Enum.GetValues<SignalType>();
        new AlertDialog.Builder(c)
            .SetTitle("Signal setzen")!
            .SetSingleChoiceItems(types.Select(Signal.TypeName).ToArray(), Array.IndexOf(types, current), (s, e) =>
            {
                pick(types[e.Which]);
                ((IDialogInterface)s!).Dismiss();
            })!
            .SetNegativeButton("Abbrechen", (IDialogInterfaceOnClickListener?)null)!
            .Show();
    }

    // ---------- Prüfen und Listen ----------

    public static void ShowIssues(Context c, string title, IReadOnlyList<PlanIssue> issues, Action<PlanIssue> pick, string? footer = null)
    {
        var body = Ui.Column(c, 6);
        AlertDialog? dialog = null;
        if (issues.Count == 0)
            body.AddView(Ui.Label(c, "Keine Probleme gefunden.", 14, true, Ui.Success));
        else
        {
            var e = issues.Count(i => i.Level == IssueLevel.Fehler);
            var w = issues.Count(i => i.Level == IssueLevel.Warnung);
            var h = issues.Count - e - w;
            body.AddView(Ui.Label(c, $"{e} Fehler · {w} Warnungen · {h} Hinweise – antippen zeigt die Stelle.", 13, color: Ui.TextSecondary));
            foreach (var issue in issues)
            {
                var color = issue.Level switch { IssueLevel.Fehler => Ui.Danger, IssueLevel.Warnung => Ui.OpenEnd, _ => Ui.Accent };
                var item = Ui.Column(c, 2);
                item.Background = Ui.Rounded(Color.Rgb(0xFA, 0xFA, 0xFA), Ui.Border);
                item.SetPadding(Ui.Dp(10), Ui.Dp(8), Ui.Dp(10), Ui.Dp(8));
                item.AddView(Ui.Label(c, $"{issue.Level} · {issue.Category}", 12.5, true, color));
                item.AddView(Ui.Label(c, issue.Text, 13.5));
                var target = issue;
                item.Clickable = true;
                item.Click += (_, _) => { dialog?.Dismiss(); pick(target); };
                body.AddView(item, Ui.Fill());
            }
        }
        if (footer is not null) body.AddView(Hint(c, footer));
        dialog = Show(c, title, body, "Schließen", cancelText: null);
    }

    public static void ShowBillOfMaterials(Context c, LayoutPlan plan, bool pro, Action exportCsv)
    {
        var rows = BillOfMaterials.Build(plan);
        var stats = plan.Statistics();
        var body = Ui.Column(c, 6);
        body.AddView(Ui.Label(c, $"{plan.Name} · Spur {plan.GaugeId} · {stats.Pieces} Teile · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m Gleis", 13, color: Ui.TextSecondary));
        if (rows.Count == 0) body.AddView(Ui.Label(c, "Der Plan enthält noch keine Gleise.", 14));
        foreach (var r in rows)
        {
            var row = Ui.Row(c, 10);
            var count = Ui.Label(c, $"{r.Count}×", 15, true);
            count.SetMinWidth(Ui.Dp(40));
            row.AddView(count);
            var text = Ui.Column(c, 0);
            text.AddView(Ui.Label(c, $"{r.Def.Number} · {r.Def.Name}", 14));
            text.AddView(Ui.Label(c, $"{r.Def.Spec} · {r.Def.SystemName}", 12.5, color: Ui.TextSecondary));
            row.AddView(text, Ui.Weight(1, vertical: false));
            body.AddView(row, Ui.Fill());
        }
        var extras = BillOfMaterials.Accessories(plan);
        if (extras.Count > 0)
        {
            body.AddView(Section(c, "Zubehör"));
            foreach (var a in extras)
            {
                var row = Ui.Row(c, 10);
                var count = Ui.Label(c, $"{a.Count}×", 15, true);
                count.SetMinWidth(Ui.Dp(40));
                row.AddView(count);
                row.AddView(Ui.Label(c, $"{a.Name} – {a.Detail}", 13.5), Ui.Weight(1, vertical: false));
                body.AddView(row, Ui.Fill());
            }
        }
        Show(c, "Stückliste", body, "Schließen", cancelText: null, neutral: (pro ? "Als CSV speichern" : "CSV (Pro)", exportCsv));
    }

    public static void ShowAddresses(Context c, PlanSession session, Action wiring)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, "Weichen belegen eine Adresse, DKW und Hauptsignale zwei (Hp0/Hp1/Hp2). Ein Weichendecoder hat vier Ausgänge: Adressen 1–4 = Decoder 1, 5–8 = Decoder 2 usw."));
        var autoRow = Ui.Row(c, 8);
        var first = Ui.Number(c, 1);
        var onlyMissing = new CheckBox(c) { Text = "nur fehlende", Checked = true };
        var list = Ui.Column(c, 6);
        void Fill()
        {
            list.RemoveAllViews();
            var items = Electrics.SwitchingItems(session.Plan);
            var conflicts = Electrics.AddressConflicts(items).SelectMany(x => x.Items).ToHashSet();
            if (items.Count == 0) list.AddView(Ui.Label(c, "Der Plan hat noch keine Weichen oder Signale.", 14, color: Ui.TextSecondary));
            foreach (var it in items)
            {
                var row = Ui.Row(c, 8);
                var text = Ui.Column(c, 0);
                text.AddView(Ui.Label(c, it.Label, 14, color: conflicts.Contains(it) ? Ui.Danger : null));
                text.AddView(Ui.Label(c, $"{it.Kind} · Decoder {it.Decoder?.ToString() ?? "–"}, Ausgang {it.Port?.ToString() ?? "–"}", 12, color: Ui.TextSecondary));
                row.AddView(text, Ui.Weight(1, vertical: false));
                var box = new EditText(c) { Text = it.Address?.ToString() ?? "", InputType = Android.Text.InputTypes.ClassNumber };
                box.SetMinWidth(Ui.Dp(70));
                var item = it;
                box.FocusChange += (_, e) =>
                {
                    if (e.HasFocus) return;
                    var t = (box.Text ?? "").Trim();
                    int? value = t.Length == 0 ? null : int.TryParse(t, out var v) && v is > 0 and <= 2048 ? v : -1;
                    if (value == -1) { session.Say("Adressen von 1 bis 2048."); box.Text = item.Address?.ToString() ?? ""; return; }
                    if (value == item.Address) return;
                    session.Edit(() =>
                    {
                        if (item.PieceId is { } pid && session.Plan.Find(pid) is { } p) p.Address = value;
                        else if (session.Plan.Signals.FirstOrDefault(s => s.Id == item.SignalId) is { } s) s.Address = value;
                    });
                    list.Post(Fill);
                };
                row.AddView(box);
                list.AddView(row, Ui.Fill());
            }
        }
        autoRow.AddView(Ui.Button(c, "Vergeben ab", () =>
        {
            session.AssignAddresses(Math.Clamp((int)Ui.Parse(first, 1), 1, 2048), onlyMissing.Checked);
            Fill();
        }));
        autoRow.AddView(first, new LinearLayout.LayoutParams(Ui.Dp(70), -2));
        autoRow.AddView(onlyMissing);
        body.AddView(autoRow);
        body.AddView(list);
        Fill();
        Show(c, "Decoder und Adressen", body, "Schließen", cancelText: null, neutral: ("Kabelplan", wiring));
    }

    public static void ShowText(Context c, string title, string text, string saveLabel, Action save)
    {
        var t = Ui.Label(c, text, 12.5);
        t.Typeface = Typeface.Monospace;
        t.SetTextIsSelectable(true);
        Show(c, title, t, "Schließen", cancelText: null, neutral: (saveLabel, save));
    }

    // ---------- Lizenz ----------

    public static void ShowLicense(Context c, LicenseManager license, Action chooseFile)
    {
        var body = Ui.Column(c, 8);
        var badge = Ui.Label(c, license.IsPro ? "Pro" : "Basis", 13, true, Color.White);
        badge.Background = Ui.Rounded(license.IsPro ? Ui.Pro : Ui.TextSecondary, radius: 10);
        badge.SetPadding(Ui.Dp(10), Ui.Dp(3), Ui.Dp(10), Ui.Dp(3));
        body.AddView(badge, Ui.Wrap());
        body.AddView(Ui.Label(c, license.Summary, 15, true));
        body.AddView(Section(c, "Basis"));
        body.AddView(Ui.Label(c, string.Join("\n", EditionInfo.Basis.Select(x => "• " + x)), 13.5));
        body.AddView(Section(c, "Pro"));
        body.AddView(Ui.Label(c, string.Join("\n", EditionInfo.Pro.Select(x => "• " + x)), 13.5));
        body.AddView(Section(c, "Gerätekennung (für gerätegebundene Lizenzen)"));
        var id = Ui.Label(c, license.MachineId, 15, true);
        id.Typeface = Typeface.Monospace;
        id.SetTextIsSelectable(true);
        body.AddView(id);
        body.AddView(Hint(c, "Eine Pro-Lizenz ist eine Datei „trackmaster.lic“. Sie gilt für Windows und Android und wird hier über „Lizenzdatei wählen“ eingespielt."));
        Show(c, "Lizenz", body, "Schließen", cancelText: null, neutral: ("Lizenzdatei wählen", chooseFile));
    }

    public static void ShowProRequired(Context c, string feature, Action openLicense)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, $"„{feature}“ gehört zu TrackMaster Pro.", 15, true));
        body.AddView(Ui.Label(c, "Pro enthält zusätzlich:\n" + string.Join("\n", EditionInfo.Pro.Skip(1).Select(x => "• " + x)), 13.5));
        Show(c, "Pro-Funktion", body, "Lizenz…", () => { openLicense(); return true; }, "Schließen");
    }

    public static void ShowHelp(Context c)
    {
        var rows = new (string, string)[]
        {
            ("Gleis in der Bibliothek antippen", "am aktiven (blauen) Gleisende ansetzen"),
            ("Orangefarbenes Gleisende antippen", "als aktives Ende wählen"),
            ("Gleis antippen", "wählen"),
            ("Gewähltes Gleis ziehen", "verschieben, rastet an freien Enden ein"),
            ("Lange auf ein Gleis drücken", "zur Auswahl hinzufügen"),
            ("Lange auf freie Fläche drücken, ziehen", "Rahmen zum Auswählen"),
            ("Doppeltippen auf Gleis/Objekt", "Eigenschaften"),
            ("Doppeltippen auf freie Fläche", "ganze Anlage zeigen"),
            ("Ein Finger auf freier Fläche", "Ansicht verschieben"),
            ("Zwei Finger", "zoomen und verschieben"),
            ("Gelände formen / Gewässer", "Punkte antippen, dann „Fertig“"),
            ("3D-Ansicht", "ein Finger dreht, zwei Finger zoomen und verschieben"),
        };
        var body = Ui.Column(c, 6);
        foreach (var (k, v) in rows)
        {
            var col = Ui.Column(c, 0);
            col.AddView(Ui.Label(c, k, 14, true));
            col.AddView(Ui.Label(c, v, 13.5, color: Ui.TextSecondary));
            body.AddView(col);
        }
        body.AddView(Hint(c, "Pläne sind mit der Windows-Version austauschbar (*.trackmaster.json)."));
        Show(c, "Bedienung", body, "OK", cancelText: null);
    }
}

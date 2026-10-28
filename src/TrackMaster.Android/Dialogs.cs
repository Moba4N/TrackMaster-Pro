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
        if (cancelText is not null) b.SetNegativeButton(Lang.T(cancelText), (IDialogInterfaceOnClickListener?)null);
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
        if (double.IsNaN(value)) { errors.Add(Lang.F($"{name}: bitte eine Zahl eingeben.")); return false; }
        if (value < min || value > max) { errors.Add(Lang.F($"{name}: zwischen {Fmt.Num(min)} und {Fmt.Num(max)}.")); return false; }
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
        body.AddView(Ui.Field(c, Lang.T("Planname"), name));
        body.AddView(Section(c, Lang.T("Anlagenplatte")));
        var shapes = BoardGeometry.All;
        var shape = Choice(c, shapes.Select(BoardGeometry.Name).ToList(), shapes.ToList().IndexOf(plan.BoardShape));
        body.AddView(Ui.Field(c, Lang.T("Form"), shape));
        var w = Ui.Number(c, plan.BoardWidth);
        var h = Ui.Number(c, plan.BoardHeight);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Breite außen (mm)"), w), Ui.Field(c, Lang.T("Tiefe außen (mm)"), h)));
        var depth = Ui.Number(c, BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn));
        var turn = Choice(c, BoardTurns, plan.BoardTurn);
        var legs = Columns(c, Ui.Field(c, Lang.T("Schenkeltiefe (mm)"), depth), Ui.Field(c, Lang.T("Ausrichtung"), turn));
        body.AddView(legs);
        var full0 = plan.BoardTurn % 2 == 0 ? plan.BoardHeight : plan.BoardWidth;
        var d0 = BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn);
        var legLeft = Ui.Number(c, BoardGeometry.Leg(plan.BoardLegLeft, d0, full0));
        var legRight = Ui.Number(c, BoardGeometry.Leg(plan.BoardLegRight, d0, full0));
        var uLegs = Columns(c, Ui.Field(c, Lang.T("Linker Schenkel lang (mm)"), legLeft), Ui.Field(c, Lang.T("Rechter Schenkel lang (mm)"), legRight));
        body.AddView(uLegs);
        void SyncLegs()
        {
            var sh = shapes[Math.Max(0, shape.SelectedItemPosition)];
            legs.Visibility = BoardGeometry.HasLegs(sh) ? ViewStates.Visible : ViewStates.Gone;
            uLegs.Visibility = sh == BoardShape.U ? ViewStates.Visible : ViewStates.Gone;
        }
        shape.ItemSelected += (_, _) => SyncLegs();
        SyncLegs();
        body.AddView(Section(c, Lang.T("Planprüfung")));
        var vehicle = Ui.Number(c, plan.EffectiveVehicleLength);
        body.AddView(Ui.Field(c, Lang.T("Längstes Fahrzeug (mm)"), vehicle));
        body.AddView(Section(c, Lang.T("Übersicht")));
        body.AddView(Rows(c,
            (Lang.T("Spur"), $"{gauge.Id} · {gauge.Scale} · {Fmt.Num(gauge.TrackWidth)} mm", null),
            (Lang.T("Gleisstücke"), stats.Pieces.ToString(), null),
            (Lang.T("Weichen und Kreuzungen"), stats.Switches.ToString(), null),
            (Lang.T("Gleislänge"), $"{Fmt.Num(stats.TrackLengthMm / 1000, 2)} m", null),
            (Lang.T("Kleinster Radius"), stats.SmallestRadius is { } r ? $"{Fmt.Num(r)} mm" : "–", null),
            (Lang.T("Größte Steigung"), stats.MaxGrade > 0 ? $"{Fmt.Num(stats.MaxGrade * 100, 1)} %" : Lang.T("eben"), stats.MaxGrade * 100 > PlanCheck.WarnGradePercent ? Ui.Danger : null),
            (Lang.T("Höchster Punkt"), $"{Fmt.Num(stats.MaxHeight, 0)} mm", null),
            (Lang.T("Gelände / Elektrik"), Lang.F($"{plan.Scenery.Count} Obj. · {plan.Feeders.Count} Einsp. · {plan.Signals.Count} Sig."), null),
            (Lang.T("Offene Gleisenden"), stats.OpenEnds.ToString(), stats.OpenEnds > 0 ? Ui.OpenEnd : Ui.Success)));
        body.AddView(Hint(c, Lang.T("So geht’s: Tippe ein orangefarbenes Gleisende an und dann ein Gleis in der Bibliothek – es wird dort angesetzt. ") +
                             Lang.T("Gewählte Gleise lassen sich mit dem Finger verschieben und rasten an freien Enden ein. Zwei Finger zoomen und verschieben die Ansicht, ") +
                             Lang.T("langes Drücken nimmt weitere Gleise zur Auswahl hinzu.")));
        Show(c, Lang.T("Plan"), body, Lang.T("Übernehmen"), () =>
        {
            var errors = new List<string>();
            Read(w, Lang.T("Breite"), 100, 50000, errors, out var bw);
            Read(h, Lang.T("Tiefe"), 100, 50000, errors, out var bh);
            var newShape = shapes[Math.Max(0, shape.SelectedItemPosition)];
            var bd = plan.BoardDepth;
            if (BoardGeometry.HasLegs(newShape)) Read(depth, Lang.T("Schenkeltiefe"), 50, 50000, errors, out bd);
            double l1 = 0, l2 = 0;
            if (newShape == BoardShape.U)
            {
                Read(legLeft, Lang.T("Linker Schenkel"), 50, 50000, errors, out l1);
                Read(legRight, Lang.T("Rechter Schenkel"), 50, 50000, errors, out l2);
            }
            Read(vehicle, Lang.T("Fahrzeuglänge"), 20, 5000, errors, out var vl);
            if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
            var n = (name.Text ?? "").Trim();
            var t = turn.SelectedItemPosition;
            var full = t % 2 == 0 ? bh : bw;
            l1 = l1 >= full ? 0 : l1;
            l2 = l2 >= full ? 0 : l2;
            vl = Math.Abs(vl - VehicleDefaults.Length(gauge)) < 0.5 ? 0 : vl;
            if (vl != plan.VehicleLength || n != plan.Name || bw != plan.BoardWidth || bh != plan.BoardHeight || newShape != plan.BoardShape || bd != plan.BoardDepth || t != plan.BoardTurn
                || l1 != plan.BoardLegLeft || l2 != plan.BoardLegRight)
                session.Edit(() =>
                {
                    if (n.Length > 0) plan.Name = n;
                    plan.BoardWidth = bw;
                    plan.BoardHeight = bh;
                    plan.BoardShape = newShape;
                    plan.BoardTurn = t;
                    plan.BoardDepth = BoardGeometry.Depth(newShape, bw, bh, bd, t);
                    plan.BoardLegLeft = l1;
                    plan.BoardLegRight = l2;
                    plan.VehicleLength = vl;
                });
            return true;
        }, neutral: (Lang.T("Stückliste"), () => run("bom")));
    }

    private static void PieceProperties(Context c, PlanSession session, PlacedPiece p, Action<string> run)
    {
        var d = p.Def;
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, d.Name, 16, true));
        body.AddView(Ui.Label(c, $"{d.SystemName} · {d.Number}", 13, color: Ui.TextSecondary));
        if (!d.Verified)
        {
            var warn = Ui.Label(c, Lang.T("Geometrie vorläufig – vor dem Bau mit dem Herstellerkatalog prüfen."), 12.5, color: Ui.Warning);
            warn.Background = Ui.Rounded(Ui.WarningBack);
            warn.SetPadding(Ui.Dp(8), Ui.Dp(6), Ui.Dp(8), Ui.Dp(6));
            body.AddView(warn);
        }
        var leftHanded = !d.RightHand != p.Flipped;
        body.AddView(d.Kind switch
        {
            PieceKind.Curve => Rows(c, (Lang.T("Radius"), $"{Fmt.Num(d.Radius)} mm", null), (Lang.T("Winkel"), $"{Fmt.Num(d.Angle)}°", null), (Lang.T("Bogenlänge"), $"{Fmt.Num(d.TrackLength)} mm", null), (Lang.T("Richtung"), leftHanded ? Lang.T("links") : Lang.T("rechts"), null)),
            PieceKind.Switch when !d.IsSimpleSwitch => Rows(c, (Lang.T("Maße"), d.Spec, null), (Lang.T("Stränge"), $"{d.RouteCount}", null), (Lang.T("Antriebe"), $"{d.Drives}", null)),
            PieceKind.Switch => Rows(c, (Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null), (Lang.T("Abzweig"), $"R {Fmt.Num(d.Radius)} · {Fmt.Num(d.Angle)}°", null), (Lang.T("Richtung"), leftHanded ? Lang.T("links") : Lang.T("rechts"), null)),
            PieceKind.Crossing => Rows(c, (Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null), (Lang.T("Kreuzungswinkel"), $"{Fmt.Num(d.Angle)}°", null)),
            PieceKind.Turntable or PieceKind.TransferTable => Rows(c, (Lang.T("Maße"), d.Spec, null), (Lang.T("Anschlüsse"), $"{d.RouteCount}", null)),
            _ => Rows(c, (Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null)),
        });
        body.AddView(Section(c, Lang.T("Position")));
        var x = Ui.Number(c, p.X, 1, true);
        var y = Ui.Number(c, p.Y, 1, true);
        var rot = Ui.Number(c, p.Rotation, 1, true);
        body.AddView(Columns(c, Ui.Field(c, "X (mm)", x), Ui.Field(c, "Y (mm)", y), Ui.Field(c, Lang.T("Drehung (°)"), rot)));
        body.AddView(Section(c, Lang.T("Höhe")));
        var height = Ui.Number(c, p.Height, 1, true);
        var grade = Ui.Number(c, p.EffectiveGrade * 100, 2, true);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Höhe Anfang (mm)"), height), Ui.Field(c, Lang.T("Steigung (%)"), grade)));
        var heights = p.ConnectorHeights();
        if (heights.Count > 1) body.AddView(Rows(c, (Lang.T("Höhe am Ende"), $"{Fmt.Num(heights[1], 1)} mm", null)));
        var tunnel = new CheckBox(c) { Text = Lang.T("liegt im Tunnel"), Checked = p.Tunnel };
        body.AddView(tunnel);
        EditText? address = null;
        if (d.Drives > 0)
        {
            body.AddView(Section(c, Lang.T("Digital")));
            address = Ui.Number(c, p.Address ?? 0);
            body.AddView(Ui.Field(c, Lang.T("Adresse (0 = keine)"), address));
        }
        var conns = session.Connections.Of(p.Id).ToList();
        var linked = conns.Count(cn => !cn.IsOpen);
        body.AddView(Rows(c, (Lang.T("Verbindungen"), Lang.F($"{linked} von {conns.Count} verbunden"), linked == conns.Count ? Ui.Success : Ui.OpenEnd)));
        AlertDialog? dialog = null;
        var buttons = Ui.Row(c, 6);
        void Cmd(string text, string command) => buttons.AddView(Ui.Button(c, text, () => { dialog?.Dismiss(); run(command); }));
        Cmd(Lang.T("Drehen"), "rotR");
        if (d.CanFlip) Cmd(Lang.T("Spiegeln"), "flip");
        if (conns.Count > 1) Cmd(Lang.T("Anschluss"), "swap");
        Cmd(Lang.T("Löschen"), "delete");
        var scroll = new HorizontalScrollView(c);
        scroll.AddView(buttons);
        body.AddView(scroll);
        dialog = Show(c, Lang.T("Gleis"), body, Lang.T("Übernehmen"), () =>
        {
            var errors = new List<string>();
            Read(x, "X", -100000, 100000, errors, out var vx);
            Read(y, "Y", -100000, 100000, errors, out var vy);
            Read(rot, Lang.T("Drehung"), -100000, 100000, errors, out var vr);
            Read(height, Lang.T("Höhe"), -500, 3000, errors, out var vh);
            Read(grade, Lang.T("Steigung"), -30, 30, errors, out var vg);
            double va = 0;
            if (address is not null) Read(address, Lang.T("Adresse"), 0, 2048, errors, out va);
            if (errors.Count > 0) { session.Say(string.Join(" ", errors)); return false; }
            int? addr = address is null ? p.Address : va >= 1 ? (int)Math.Round(va) : null;
            var changed = Math.Abs(vx - p.X) > 1e-6 || Math.Abs(vy - p.Y) > 1e-6 || Math.Abs(Angles.Normalize(vr) - p.Rotation) > 1e-6
                          || Math.Abs(vh - p.Height) > 1e-6 || Math.Abs(vg / 100 - p.EffectiveGrade) > 1e-9 || tunnel.Checked != p.Tunnel || addr != p.Address;
            if (changed)
                session.Edit(() =>
                {
                    p.X = vx; p.Y = vy; p.Rotation = Angles.Normalize(vr);
                    p.Height = vh;
                    if (Math.Abs(vg / 100 - p.EffectiveGrade) > 1e-9) p.Grade = vg / 100;
                    p.Tunnel = tunnel.Checked;
                    p.Address = addr;
                });
            return true;
        });
    }

    private static void MultiProperties(Context c, PlanSession session, IReadOnlyList<PlacedPiece> sel, Action<string> run)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Rows(c, (Lang.T("Gleisstücke"), sel.Count.ToString(), null), (Lang.T("Gleislänge"), $"{Fmt.Num(sel.Sum(p => p.Def.TrackLength) / 1000, 2)} m", null)));
        AlertDialog? dialog = null;
        var grid = Ui.Column(c, 6);
        void Line(params (string Text, string Command)[] items)
        {
            var row = Ui.Row(c, 6);
            foreach (var (t, cmd) in items) row.AddView(Ui.Button(c, t, () => { dialog?.Dismiss(); run(cmd); }), Ui.Weight(1, vertical: false));
            grid.AddView(row, Ui.Fill());
        }
        Line((Lang.T("15° links"), "rotL"), (Lang.T("15° rechts"), "rotR"));
        Line((Lang.T("Duplizieren"), "duplicate"), (Lang.T("Löschen"), "delete"));
        Line((Lang.T("Steigung…"), "gradient"), (sel.All(p => p.Tunnel) ? Lang.T("Tunnel aufheben") : Lang.T("Als Tunnel"), "tunnel"));
        body.AddView(grid);
        body.AddView(Hint(c, Lang.T("Ziehe die Auswahl an ein freies Gleisende – die ganze Gruppe rastet dort ein.")));
        dialog = Show(c, Lang.F($"{sel.Count} Gleise gewählt"), body, Lang.T("Schließen"), cancelText: null);
    }

    private static void ObjectProperties(Context c, PlanSession session, object o, Action<string> run)
    {
        var plan = session.Plan;
        var body = Ui.Column(c, 8);
        AlertDialog? dialog = null;
        void DeleteButton() => body.AddView(Ui.Button(c, Lang.T("Löschen"), () => { dialog?.Dismiss(); run("delete"); }), Ui.Wrap());
        switch (o)
        {
            case SceneryItem s:
            {
                var name = Ui.TextField(c, s.Name);
                body.AddView(Ui.Field(c, s.Kind == SceneryKind.Label ? "Text" : Lang.T("Name"), name));
                var fields = new List<(EditText Box, string Name, double Min, double Max, Action<double> Apply)>();
                EditText Num(double v, string label, double min, double max, Action<double> apply)
                {
                    var e = Ui.Number(c, v, 0, min < 0);
                    fields.Add((e, label, min, max, apply));
                    return e;
                }
                switch (s.Kind)
                {
                    case SceneryKind.Label:
                        body.AddView(Columns(c,
                            Ui.Field(c, Lang.T("Schrifthöhe (mm)"), Num(s.Height, Lang.T("Schrifthöhe"), 2, 500, v => s.Height = v)),
                            Ui.Field(c, Lang.T("Drehung (°)"), Num(s.Rotation, Lang.T("Drehung"), -360, 360, v => s.Rotation = Angles.Normalize(v)))));
                        break;
                    case SceneryKind.Dimension when s.Points.Count == 2:
                        body.AddView(Columns(c,
                            Ui.Field(c, Lang.T("Abstand (mm)"), Num(s.Width, Lang.T("Abstand"), -5000, 5000, v => s.Width = v)),
                            Ui.Field(c, Lang.T("Schrifthöhe (mm)"), Num(s.Height, Lang.T("Schrifthöhe"), 2, 500, v => s.Height = v))));
                        body.AddView(Rows(c, (Lang.T("Länge"), $"{Fmt.Num(Annotation.Dimension(s, s.Height).Length, 1)} mm", null)));
                        break;
                    case SceneryKind.Terrain:
                        body.AddView(Ui.Field(c, Lang.T("Höhe (mm)"), Num(s.Height, Lang.T("Höhe"), -500, 3000, v => s.Height = v)));
                        body.AddView(Rows(c, (Lang.T("Eckpunkte"), s.Points.Count.ToString(), null), (Lang.T("Fläche"), $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null)));
                        break;
                    case SceneryKind.Water:
                        body.AddView(Ui.Field(c, Lang.T("Tiefe (mm)"), Num(s.Height, Lang.T("Tiefe"), 0, 200, v => s.Height = v)));
                        body.AddView(Rows(c, (Lang.T("Fläche"), $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null)));
                        break;
                    case SceneryKind.Building:
                        body.AddView(Columns(c,
                            Ui.Field(c, Lang.T("Breite (mm)"), Num(s.Width, Lang.T("Breite"), 5, 5000, v => s.Width = v)),
                            Ui.Field(c, Lang.T("Tiefe (mm)"), Num(s.Depth, Lang.T("Tiefe"), 5, 5000, v => s.Depth = v))));
                        body.AddView(Columns(c,
                            Ui.Field(c, Lang.T("Höhe (mm)"), Num(s.Height, Lang.T("Höhe"), 5, 2000, v => s.Height = v)),
                            Ui.Field(c, Lang.T("Drehung (°)"), Num(s.Rotation, Lang.T("Drehung"), -360, 360, v => s.Rotation = Angles.Normalize(v)))));
                        break;
                    case SceneryKind.Tree:
                        body.AddView(Columns(c,
                            Ui.Field(c, Lang.T("Durchmesser (mm)"), Num(s.Width, Lang.T("Durchmesser"), 3, 1000, v => { s.Width = v; s.Depth = v; })),
                            Ui.Field(c, Lang.T("Höhe (mm)"), Num(s.Height, Lang.T("Höhe"), 5, 2000, v => s.Height = v))));
                        break;
                }
                DeleteButton();
                body.AddView(Hint(c, Lang.T("Ein gewähltes Objekt lässt sich mit dem Finger verschieben.")));
                dialog = Show(c, SceneryItem.KindName(s.Kind), body, Lang.T("Übernehmen"), () =>
                {
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
                var circuit = new AutoCompleteTextView(c) { Text = f.Circuit, Threshold = 0 };
                circuit.Adapter = new ArrayAdapter<string>(c, global::Android.Resource.Layout.SimpleDropDownItem1Line, circuits);
                circuit.Click += (_, _) => circuit.ShowDropDown();
                body.AddView(Ui.Field(c, Lang.T("Stromkreis / Booster"), circuit));
                var section = Electrics.Sections(plan).FirstOrDefault(x => x.PieceIds.Contains(f.PieceId));
                body.AddView(Rows(c, (Lang.T("Gleis"), $"{plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId})", null),
                    (Lang.T("Stromabschnitt"), section is null ? "–" : $"{section.Number} · {Fmt.Num(section.LengthMm / 1000, 2)} m", null)));
                DeleteButton();
                dialog = Show(c, Lang.T("Einspeisung"), body, Lang.T("Übernehmen"), () =>
                {
                    var t = (circuit.Text ?? "").Trim();
                    if (t.Length > 0 && t != f.Circuit) session.Edit(() => f.Circuit = t);
                    return true;
                });
                break;
            }
            case Isolation i:
                body.AddView(Rows(c, (Lang.T("Gleis"), $"{plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId})", null), (Lang.T("Gleisende"), i.Index.ToString(), null),
                    (Lang.T("Ausführung"), Electrics.IsThreeRail(plan) ? Lang.T("Mittelleiter getrennt") : Lang.T("beide Schienen isoliert"), null)));
                DeleteButton();
                dialog = Show(c, Lang.T("Trennstelle"), body, Lang.T("Schließen"), cancelText: null);
                break;
            case Signal g:
            {
                var name = Ui.TextField(c, g.Name);
                body.AddView(Ui.Field(c, Lang.T("Name"), name));
                var types = Enum.GetValues<SignalType>();
                var type = Choice(c, types.Select(Signal.TypeName).ToList(), Array.IndexOf(types, g.Type));
                body.AddView(Ui.Field(c, Lang.T("Art"), type));
                var face = SignalFace.For(g.Type);
                var picture = Ui.Row(c, 12);
                picture.AddView(new SignalIconView(c, g.Type), new LinearLayout.LayoutParams(Ui.Dp(36), Ui.Dp(64)));
                picture.AddView(Ui.Label(c, $"{face.Aspect}. {face.Description}", 12, color: Ui.TextSecondary), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
                body.AddView(picture);
                var address = Ui.Number(c, g.Address ?? 0);
                body.AddView(Ui.Field(c, Lang.T("Adresse (0 = keine)"), address));
                body.AddView(Rows(c, (Lang.T("Belegt Adressen"), g.AddressCount.ToString(), null), (Lang.T("Gleis"), Lang.F($"{plan.Find(g.PieceId)?.Def.Number} (Nr. {g.PieceId}), Ende {g.Index}"), null)));
                DeleteButton();
                dialog = Show(c, "Signal", body, Lang.T("Übernehmen"), () =>
                {
                    var errors = new List<string>();
                    Read(address, Lang.T("Adresse"), 0, 2048, errors, out var a);
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
        body.AddView(Hint(c, Lang.F($"Flexgleis {stock.Name}{(stock.Number == Lang.T("Flexgleis") ? "" : $" ({stock.Number})")}. Es wird am aktiven Gleisende angesetzt; Bögen lassen sich danach spiegeln. In der Stückliste wird die Gesamtlänge in Stücke zu {Fmt.Num(stock.Length, 0)} mm umgerechnet.")));
        var kinds = new RadioGroup(c) { Orientation = Orientation.Horizontal };
        var straight = new RadioButton(c) { Text = Lang.T("Gerade"), Id = 1 };
        var curve = new RadioButton(c) { Text = Lang.T("Bogen"), Id = 2 };
        kinds.AddView(straight);
        kinds.AddView(curve);
        kinds.Check(1);
        body.AddView(kinds);
        var length = Ui.Number(c, Math.Round(stock.Length / 2));
        var radius = Ui.Number(c, Math.Round(gauge.TrackWidth * 30));
        var angle = Ui.Number(c, 30);
        var lengthField = Ui.Field(c, Lang.T("Länge (mm)"), length);
        var curveFields = Columns(c, Ui.Field(c, "Radius (mm)", radius), Ui.Field(c, Lang.T("Winkel (°)"), angle));
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
        Show(c, Lang.T("Flexgleis"), body, Lang.T("Ansetzen"), () =>
        {
            var errors = new List<string>();
            string? key = null;
            if (straight.Checked)
            {
                if (Read(length, Lang.T("Länge"), 5, 5000, errors, out var l)) key = Catalog.FlexStraightKey(gauge.Id, l, stock.SystemId);
            }
            else
            {
                var okR = Read(radius, Lang.T("Radius"), 50, 20000, errors, out var r);
                var okA = Read(angle, Lang.T("Winkel"), 1, 180, errors, out var a);
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
        body.AddView(Hint(c, Lang.F($"Die Steigung wird gleichmäßig über die {pieces} gewählten Gleise gelegt, vom einen Ende der Gleisfolge zum anderen. ") +
                             Lang.T("Empfohlen: höchstens 2,5–3 %, in Bögen weniger. Negative Werte ergeben ein Gefälle.")));
        var start = Ui.Number(c, startHeight, 0, true);
        var percent = Ui.Number(c, 2.5, 1, true);
        body.AddView(Ui.Field(c, Lang.T("Höhe am Anfang (mm über Platte)"), start));
        body.AddView(Ui.Field(c, Lang.T("Steigung (%)"), percent));
        var reverse = new CheckBox(c) { Text = Lang.T("Am anderen Ende beginnen") };
        body.AddView(reverse);
        var error = Error(c);
        body.AddView(error);
        Show(c, Lang.T("Steigung"), body, Lang.T("Anlegen"), () =>
        {
            var errors = new List<string>();
            Read(start, Lang.T("Höhe am Anfang"), -500, 3000, errors, out var s);
            Read(percent, Lang.T("Steigung"), -15, 15, errors, out var p);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            apply(s, p, reverse.Checked);
            return true;
        });
    }

    /// <summary>Gleiswendel rund oder oval, ein- oder zweigleisig. <paramref name="build"/> bekommt die Angaben.</summary>
    public static void AskHelix(Context c, Gauge gauge, IReadOnlyList<TrackPieceDef> curves, IReadOnlyList<TrackPieceDef> straights, bool fromEnd, Action<HelixSpec> build)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, (fromEnd
            ? Lang.T("Die Wendel beginnt am aktiven Gleisende und steigt gleichmäßig an.")
            : Lang.T("Die Wendel entsteht in der Mitte der Ansicht. Tipp: Vorher ein Gleisende antippen, dann schließt sie dort an.")) + Lang.T(" Nach dem Bauen folgt die Holzliste.")));
        var shape = Choice(c, [Lang.T("Rund"), Lang.T("Oval")], 0);
        var tracks = Choice(c, [Lang.T("eingleisig"), Lang.T("zweigleisig")], 0);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Form"), shape), Ui.Field(c, Lang.T("Gleise"), tracks)));
        var curve = Choice(c, [], 0);
        body.AddView(Ui.Field(c, Lang.T("Bogengleis (innen)"), curve));
        var straightList = straights.OrderBy(x => x.Length).ToList();
        var straight = Choice(c, straightList.Select(x => $"{x.Number} – {Fmt.Num(x.Length)} mm").ToList(),
            straightList.Count == 0 ? 0 : straightList.IndexOf(straightList.MinBy(x => Math.Abs(x.Length - gauge.TrackWidth * 11))!));
        var straightCount = Ui.Number(c, 2);
        var oval = Ui.Column(c, 8);
        oval.AddView(Columns(c, Ui.Field(c, Lang.T("Gerade an den Längsseiten"), straight), Ui.Field(c, Lang.T("Anzahl je Seite"), straightCount)));
        body.AddView(oval);
        var outer = Choice(c, [], 0);
        var spacing = Ui.Number(c, Helix.DefaultSpacing(gauge));
        var spacingField = Ui.Field(c, Lang.T("Gleisabstand bei Flexgleis (mm)"), spacing);
        var outerPanel = Ui.Column(c, 8);
        outerPanel.AddView(Ui.Field(c, Lang.T("Außengleis"), outer));
        outerPanel.AddView(spacingField);
        body.AddView(outerPanel);
        var turns = Ui.Number(c, 2, 2);
        var rise = Ui.Number(c, gauge.ClearanceHeight + 10);
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Windungen"), turns), Ui.Field(c, Lang.T("Höhe je Windung (mm)"), rise)));
        var direction = Choice(c, [Lang.T("links herum (gegen den Uhrzeigersinn)"), Lang.T("rechts herum (im Uhrzeigersinn)")], 0);
        body.AddView(Ui.Field(c, Lang.T("Richtung"), direction));
        var info = Ui.Label(c, "", 12.5, color: Ui.TextSecondary);
        body.AddView(info);
        var error = Error(c);
        body.AddView(error);

        HelixShape Shape() => shape.SelectedItemPosition == 1 ? HelixShape.Oval : HelixShape.Rund;
        var inners = new List<TrackPieceDef>();
        var outers = new List<TrackPieceDef?>();
        void SetItems(Spinner sp, IReadOnlyList<string> items, int selected)
        {
            sp.Adapter = new ArrayAdapter<string>(c, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, items.ToList());
            sp.SetSelection(Math.Clamp(selected, 0, Math.Max(0, items.Count - 1)));
        }
        TrackPieceDef? Inner() => inners.Count == 0 ? null : inners[Math.Clamp(curve.SelectedItemPosition, 0, inners.Count - 1)];
        void FillCurves()
        {
            var keep = Inner()?.Key;
            inners = curves.Where(x => Helix.Fits(x, Shape())).OrderBy(x => x.Radius).ToList();
            var k = inners.FindIndex(x => x.Key == keep);
            SetItems(curve, inners.Select(x => $"{x.Number} – R {Fmt.Num(x.Radius)} mm, {Fmt.Num(x.Angle)}°").ToList(), k >= 0 ? k : Math.Min(1, inners.Count - 1));
            FillOuter();
        }
        void FillOuter()
        {
            outers = [null];
            if (Inner() is { } inner)
                outers.AddRange(curves.Where(x => x.Radius > inner.Radius && Helix.Fits(x, Shape())).OrderBy(x => x.Radius));
            var best = Inner() is { } i ? Helix.OuterCurve(curves, i, Ui.Parse(spacing, Helix.DefaultSpacing(gauge)), Shape()) : null;
            SetItems(outer, outers.Select(x => x is null ? Lang.T("Flexgleis mit frei gewähltem Abstand") : Lang.F($"{x.Number} – R {Fmt.Num(x.Radius)} mm (Abstand {Fmt.Num(x.Radius - Inner()!.Radius)} mm)")).ToList(),
                best is null ? 0 : outers.IndexOf(best));
            Update();
        }
        HelixSpec? Spec(List<string> errors)
        {
            if (Inner() is not { } inner) { errors.Add(Lang.T("Dieses Gleissystem hat kein passendes Bogengleis.")); return null; }
            Read(turns, Lang.T("Windungen"), 0.25, 20, errors, out var t);
            Read(rise, Lang.T("Höhe je Windung"), 10, 1000, errors, out var r);
            double n = 0, sp = 0;
            TrackPieceDef? st = null;
            if (Shape() == HelixShape.Oval)
            {
                if (straightList.Count == 0) errors.Add(Lang.T("Dieses Gleissystem hat kein gerades Gleis."));
                else st = straightList[Math.Clamp(straight.SelectedItemPosition, 0, straightList.Count - 1)];
                Read(straightCount, Lang.T("Geraden je Längsseite"), 1, 50, errors, out n);
            }
            var two = tracks.SelectedItemPosition == 1;
            var oc = two && outers.Count > 0 ? outers[Math.Clamp(outer.SelectedItemPosition, 0, outers.Count - 1)] : null;
            if (two && oc is null) Read(spacing, Lang.T("Gleisabstand"), gauge.TrackWidth * PlanCheck.MinSpacingFactor, gauge.TrackWidth * 20, errors, out sp);
            if (errors.Count > 0) return null;
            return new HelixSpec(inner.Key, t, r, direction.SelectedItemPosition == 1, Shape(), st?.Key, (int)Math.Round(n), two ? 2 : 1,
                oc is not null ? oc.Radius - inner.Radius : sp, oc?.Key);
        }
        void Update()
        {
            oval.Visibility = Shape() == HelixShape.Oval ? ViewStates.Visible : ViewStates.Gone;
            outerPanel.Visibility = tracks.SelectedItemPosition == 1 ? ViewStates.Visible : ViewStates.Gone;
            spacingField.Visibility = outer.SelectedItemPosition <= 0 ? ViewStates.Visible : ViewStates.Gone;
            if (Spec([]) is not { } spec) { info.Text = ""; return; }
            var m = Helix.Material(spec, gauge);
            info.Text = Lang.F($"Steigung {Fmt.Num(Helix.GradePercent(spec), 1)} % · Gesamthöhe {Fmt.Num(spec.TotalHeight, 0)} mm · Platzbedarf {Fmt.Num(2 * m.OuterBoardRadius + spec.StraightLength, 0)} × {Fmt.Num(2 * m.OuterBoardRadius, 0)} mm");
        }
        shape.ItemSelected += (_, _) => FillCurves();
        curve.ItemSelected += (_, _) => FillOuter();
        foreach (var sp in new[] { tracks, outer, straight, direction }) sp.ItemSelected += (_, _) => Update();
        foreach (var box in new[] { turns, rise, straightCount, spacing }) box.TextChanged += (_, _) => Update();
        FillCurves();
        Show(c, Lang.T("Gleiswendel"), body, Lang.T("Bauen"), () =>
        {
            var errors = new List<string>();
            var spec = Spec(errors);
            if (spec is not null && spec.Rise < gauge.ClearanceHeight)
                errors.Add(Lang.F($"Mit {Fmt.Num(spec.Rise, 0)} mm passt kein Zug unter die nächste Windung (mindestens {Fmt.Num(gauge.ClearanceHeight, 0)} mm)."));
            SetError(error, errors);
            if (errors.Count > 0 || spec is null) return false;
            build(spec);
            return true;
        });
    }

    /// <summary>Holzliste der Gleiswendel; teilen bzw. als CSV speichern.</summary>
    public static void ShowHelixMaterial(Context c, HelixMaterial m, Action saveCsv)
    {
        var body = Ui.Column(c, 6);
        foreach (var p in m.Parts)
        {
            var row = Ui.Column(c, 1);
            row.SetPadding(0, Ui.Dp(4), 0, Ui.Dp(4));
            row.AddView(Ui.Label(c, $"{p.Count} × {p.Name}", 14, true));
            if (p.Size.Length > 0) row.AddView(Ui.Label(c, p.Size, 13));
            if (p.Note.Length > 0) row.AddView(Ui.Label(c, p.Note, 12, color: Ui.TextSecondary));
            body.AddView(row);
        }
        foreach (var n in m.Notes)
            body.AddView(Ui.Label(c, "• " + n, 12.5, color: n.StartsWith(Lang.T("Achtung")) ? Color.Rgb(0xC4, 0x2B, 0x1C) : Ui.TextSecondary));
        Show(c, Lang.T("Holzliste Gleiswendel"), body, Lang.T("Schließen"), cancelText: null, neutral: (Lang.T("Als CSV speichern"), saveCsv));
    }

    public static void AskSignalType(Context c, SignalType current, Action<SignalType> pick)
    {
        var body = Ui.Column(c, 6);
        AlertDialog? dialog = null;
        foreach (var t in Enum.GetValues<SignalType>())
        {
            var type = t;
            var face = SignalFace.For(t);
            var row = Ui.Row(c, 12);
            row.SetPadding(Ui.Dp(8), Ui.Dp(6), Ui.Dp(8), Ui.Dp(6));
            row.Background = Ui.Rounded(t == current ? Color.Rgb(0xE8, 0xF1, 0xFB) : Color.Rgb(0xFA, 0xFA, 0xFA), t == current ? Ui.Accent : Ui.Border);
            row.Clickable = true;
            row.AddView(new SignalIconView(c, t), new LinearLayout.LayoutParams(Ui.Dp(40), Ui.Dp(64)));
            var texts = Ui.Column(c, 2);
            texts.AddView(Ui.Label(c, Signal.TypeName(t), 15, true));
            texts.AddView(Ui.Label(c, $"{face.Aspect}. {face.Description}", 12, color: Ui.TextSecondary));
            row.AddView(texts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            row.Click += (_, _) => { pick(type); dialog?.Dismiss(); };
            body.AddView(row);
        }
        dialog = Show(c, Lang.T("Signal setzen"), body, okText: null);
    }

    /// <summary>Kleines Signalbild (H/V-Lichtsignal) für Listen.</summary>
    private sealed class SignalIconView(Context c, SignalType type) : View(c)
    {
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            PlanPainter.SignalIcon(canvas, type, Width, Height);
        }
    }

    // ---------- Prüfen und Listen ----------

    public static void ShowIssues(Context c, string title, IReadOnlyList<PlanIssue> issues, Action<PlanIssue> pick, string? footer = null)
    {
        var body = Ui.Column(c, 6);
        AlertDialog? dialog = null;
        if (issues.Count == 0)
            body.AddView(Ui.Label(c, Lang.T("Keine Probleme gefunden."), 14, true, Ui.Success));
        else
        {
            var e = issues.Count(i => i.Level == IssueLevel.Fehler);
            var w = issues.Count(i => i.Level == IssueLevel.Warnung);
            var h = issues.Count - e - w;
            body.AddView(Ui.Label(c, Lang.F($"{e} Fehler · {w} Warnungen · {h} Hinweise – antippen zeigt die Stelle."), 13, color: Ui.TextSecondary));
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
        dialog = Show(c, title, body, Lang.T("Schließen"), cancelText: null);
    }

    public static void ShowBillOfMaterials(Context c, LayoutPlan plan, bool pro, Action exportCsv)
    {
        var rows = BillOfMaterials.Build(plan);
        var stats = plan.Statistics();
        var body = Ui.Column(c, 6);
        body.AddView(Ui.Label(c, Lang.F($"{plan.Name} · Spur {plan.GaugeId} · {stats.Pieces} Teile · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m Gleis"), 13, color: Ui.TextSecondary));
        if (rows.Count == 0) body.AddView(Ui.Label(c, Lang.T("Der Plan enthält noch keine Gleise."), 14));
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
            body.AddView(Section(c, Lang.T("Zubehör")));
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
        Show(c, Lang.T("Stückliste"), body, Lang.T("Schließen"), cancelText: null, neutral: (pro ? Lang.T("Als CSV speichern") : Lang.T("CSV (Pro)"), exportCsv));
    }

    public static void ShowAddresses(Context c, PlanSession session, Action wiring)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Hint(c, Lang.T("Weichen belegen eine Adresse, DKW und Hauptsignale zwei (Hp0/Hp1/Hp2). Ein Weichendecoder hat vier Ausgänge: Adressen 1–4 = Decoder 1, 5–8 = Decoder 2 usw.")));
        var autoRow = Ui.Row(c, 8);
        var first = Ui.Number(c, 1);
        var onlyMissing = new CheckBox(c) { Text = Lang.T("nur fehlende"), Checked = true };
        var list = Ui.Column(c, 6);
        void Fill()
        {
            list.RemoveAllViews();
            var items = Electrics.SwitchingItems(session.Plan);
            var conflicts = Electrics.AddressConflicts(items).SelectMany(x => x.Items).ToHashSet();
            if (items.Count == 0) list.AddView(Ui.Label(c, Lang.T("Der Plan hat noch keine Weichen oder Signale."), 14, color: Ui.TextSecondary));
            foreach (var it in items)
            {
                var row = Ui.Row(c, 8);
                var text = Ui.Column(c, 0);
                text.AddView(Ui.Label(c, it.Label, 14, color: conflicts.Contains(it) ? Ui.Danger : null));
                text.AddView(Ui.Label(c, Lang.F($"{it.Kind} · Decoder {it.Decoder?.ToString() ?? "–"}, Ausgang {it.Port?.ToString() ?? "–"}"), 12, color: Ui.TextSecondary));
                row.AddView(text, Ui.Weight(1, vertical: false));
                var box = new EditText(c) { Text = it.Address?.ToString() ?? "", InputType = Android.Text.InputTypes.ClassNumber };
                box.SetMinWidth(Ui.Dp(70));
                var item = it;
                box.FocusChange += (_, e) =>
                {
                    if (e.HasFocus) return;
                    var t = (box.Text ?? "").Trim();
                    int? value = t.Length == 0 ? null : int.TryParse(t, out var v) && v is > 0 and <= 2048 ? v : -1;
                    if (value == -1) { session.Say(Lang.T("Adressen von 1 bis 2048.")); box.Text = item.Address?.ToString() ?? ""; return; }
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
        autoRow.AddView(Ui.Button(c, Lang.T("Vergeben ab"), () =>
        {
            session.AssignAddresses(Math.Clamp((int)Ui.Parse(first, 1), 1, 2048), onlyMissing.Checked);
            Fill();
        }));
        autoRow.AddView(first, new LinearLayout.LayoutParams(Ui.Dp(70), -2));
        autoRow.AddView(onlyMissing);
        body.AddView(autoRow);
        body.AddView(list);
        Fill();
        Show(c, Lang.T("Decoder und Adressen"), body, Lang.T("Schließen"), cancelText: null, neutral: (Lang.T("Kabelplan"), wiring));
    }

    public static void ShowText(Context c, string title, string text, string saveLabel, Action save)
    {
        var t = Ui.Label(c, text, 12.5);
        t.Typeface = Typeface.Monospace;
        t.SetTextIsSelectable(true);
        Show(c, title, t, Lang.T("Schließen"), cancelText: null, neutral: (saveLabel, save));
    }

    // ---------- Lizenz ----------

    public static void ShowLicense(Context c, LicenseManager license, Action chooseFile)
    {
        var body = Ui.Column(c, 8);
        var badge = Ui.Label(c, license.IsPro ? Lang.T("Pro") : Lang.T("Basis"), 13, true, Color.White);
        badge.Background = Ui.Rounded(license.IsPro ? Ui.Pro : Ui.TextSecondary, radius: 10);
        badge.SetPadding(Ui.Dp(10), Ui.Dp(3), Ui.Dp(10), Ui.Dp(3));
        body.AddView(badge, Ui.Wrap());
        body.AddView(Ui.Label(c, license.Summary, 15, true));
        body.AddView(Section(c, Lang.T("Basis")));
        body.AddView(Ui.Label(c, string.Join("\n", EditionInfo.Basis.Select(x => "• " + x)), 13.5));
        body.AddView(Section(c, Lang.T("Pro")));
        body.AddView(Ui.Label(c, string.Join("\n", EditionInfo.Pro.Select(x => "• " + x)), 13.5));
        body.AddView(Section(c, Lang.T("Gerätekennung (für gerätegebundene Lizenzen)")));
        var id = Ui.Label(c, license.MachineId, 15, true);
        id.Typeface = Typeface.Monospace;
        id.SetTextIsSelectable(true);
        body.AddView(id);
        body.AddView(Hint(c, Lang.T("Eine Pro-Lizenz ist eine Datei „trackmaster.lic“. Sie gilt für Windows und Android und wird hier über „Lizenzdatei wählen“ eingespielt.")));
        Show(c, Lang.T("Lizenz"), body, Lang.T("Schließen"), cancelText: null, neutral: (Lang.T("Lizenzdatei wählen"), chooseFile));
    }

    public static void ShowProRequired(Context c, string feature, Action openLicense)
    {
        var body = Ui.Column(c, 8);
        body.AddView(Ui.Label(c, Lang.F($"„{feature}“ gehört zu TrackMaster Pro."), 15, true));
        body.AddView(Ui.Label(c, Lang.T("Pro enthält zusätzlich:\n") + string.Join("\n", EditionInfo.Pro.Skip(1).Select(x => "• " + x)), 13.5));
        Show(c, Lang.T("Pro-Funktion"), body, Lang.T("Lizenz…"), () => { openLicense(); return true; }, Lang.T("Schließen"));
    }

    public static void ShowHelp(Context c)
    {
        var rows = new (string, string)[]
        {
            (Lang.T("Gleis in der Bibliothek antippen"), Lang.T("am aktiven (blauen) Gleisende ansetzen")),
            (Lang.T("Orangefarbenes Gleisende antippen"), Lang.T("als aktives Ende wählen")),
            (Lang.T("Gleis antippen"), Lang.T("wählen")),
            (Lang.T("Gewähltes Gleis ziehen"), Lang.T("verschieben, rastet an freien Enden ein")),
            (Lang.T("Lange auf ein Gleis drücken"), Lang.T("zur Auswahl hinzufügen")),
            (Lang.T("Lange auf freie Fläche drücken, ziehen"), Lang.T("Rahmen zum Auswählen")),
            (Lang.T("Doppeltippen auf Gleis/Objekt"), Lang.T("Eigenschaften")),
            (Lang.T("Doppeltippen auf freie Fläche"), Lang.T("ganze Anlage zeigen")),
            (Lang.T("Ein Finger auf freier Fläche"), Lang.T("Ansicht verschieben")),
            (Lang.T("Zwei Finger"), Lang.T("zoomen und verschieben")),
            (Lang.T("Zwei Finger drehen"), Lang.T("Ansicht drehen; Kompass antippen richtet sie wieder gerade aus")),
            (Lang.T("Gelände formen / Gewässer"), Lang.T("Punkte antippen, dann „Fertig“")),
            (Lang.T("3D-Ansicht"), Lang.T("ein Finger dreht, zwei Finger zoomen und verschieben")),
        };
        var body = Ui.Column(c, 6);
        foreach (var (k, v) in rows)
        {
            var col = Ui.Column(c, 0);
            col.AddView(Ui.Label(c, k, 14, true));
            col.AddView(Ui.Label(c, v, 13.5, color: Ui.TextSecondary));
            body.AddView(col);
        }
        body.AddView(Hint(c, Lang.T("Pläne sind mit der Windows-Version austauschbar (*.trackmaster.json).")));
        Show(c, Lang.T("Bedienung"), body, "OK", cancelText: null);
    }
}

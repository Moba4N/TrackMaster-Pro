using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Module der Anlage bearbeiten: Name, Lage und Größe je Modul, Module anhängen oder entfernen, einen Plan als Modul ansetzen.</summary>
    public static void AskModules(Context c, LayoutPlan plan, Action<IReadOnlyList<ModuleEdit>> apply, Action appendPlan)
    {
        var rows = new List<(int Index, EditText Name, EditText X, EditText Y, EditText W, EditText D, CheckBox Pick)>();
        Spinner? splitModule = null, joinMove = null, joinTarget = null;
        IReadOnlyList<string> Names() => rows.Select(r => r.Name.Text ?? "").ToList();
        void Renamed()
        {
            if (splitModule is null || joinMove is null || joinTarget is null) return;
            foreach (var (sp, pick) in new[] { (splitModule, 0), (joinMove, 1), (joinTarget, 0) })
            {
                var old = sp.SelectedItemPosition;
                sp.Adapter = new ArrayAdapter<string>(c, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, Names().ToList());
                sp.SetSelection(old >= 0 && old < rows.Count ? old : Math.Min(pick, rows.Count - 1));
            }
        }
        var body = Ui.Column(c, 6);
        body.AddView(Hint(c, plan.BoardShape == BoardShape.Module
            ? Lang.T("Lage (X, Y = linke obere Ecke) und Größe jedes Moduls in mm. Ein verschobenes Modul nimmt seine Gleise und sein Gelände mit; Kanten rasten auf 30 mm an Nachbarmodulen ein. Gleise über Modulgrenzen meldet die Planprüfung.")
            : Lang.F($"Die Platte ({BoardGeometry.Name(plan.BoardShape)}) wird beim Übernehmen in Module zerlegt, je Rechteck eines. Danach lassen sich Module anhängen und verschieben.")));
        var list = Ui.Column(c, 6);
        body.AddView(list);
        void Fill()
        {
            list.RemoveAllViews();
            foreach (var row in rows)
            {
                var r = row;
                var card = Ui.Column(c, 2);
                foreach (var v in new View[] { r.Name, r.X, r.Y, r.W, r.D, r.Pick }) (v.Parent as ViewGroup)?.RemoveView(v);
                var head = Ui.Row(c, 8);
                head.AddView(r.Pick);
                head.AddView(Ui.Field(c, Lang.T("Name"), r.Name), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
                var remove = Ui.Button(c, "✕", () => { rows.Remove(r); Fill(); Renamed(); });
                remove.Enabled = rows.Count > 1;
                head.AddView(remove);
                card.AddView(head);
                card.AddView(Columns(c, Ui.Field(c, "X", r.X), Ui.Field(c, "Y", r.Y), Ui.Field(c, Lang.T("Breite"), r.W), Ui.Field(c, Lang.T("Tiefe"), r.D)));
                list.AddView(card);
            }
        }
        (int, EditText, EditText, EditText, EditText, EditText, CheckBox) Row(int index, string name, double x, double y, double w, double d)
            => (index, Ui.TextField(c, name), Ui.Number(c, x, 0, signed: true), Ui.Number(c, y, 0, signed: true), Ui.Number(c, w), Ui.Number(c, d), new CheckBox(c));
        foreach (var m in ModuleLayout.Current(plan)) rows.Add(Row(m.Index, m.Name, m.X, m.Y, m.Width, m.Depth));
        Fill();
        body.AddView(Ui.Button(c, Lang.T("Modul anhängen"), () =>
        {
            var temp = new LayoutPlan();
            foreach (var r in rows)
                if (Fmt.Parse(r.X.Text) is { } x && Fmt.Parse(r.Y.Text) is { } y && Fmt.Parse(r.W.Text) is { } w && Fmt.Parse(r.D.Text) is { } d)
                    temp.Modules.Add(new BoardModule { X = x, Y = y, Width = w, Depth = d });
            var next = ModuleLayout.NextModule(temp);
            rows.Add(Row(-1, Lang.F($"Modul {rows.Count + 1}"), next.X, next.Y, next.Width, next.Depth));
            Fill();
            Renamed();
        }));
        var error = Error(c);
        List<ModuleEdit>? Edits()
        {
            var errors = new List<string>();
            var edits = new List<ModuleEdit>();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var name = Lang.F($"Modul {i + 1}");
                Read(r.X, $"{name} X", -50000, 50000, errors, out var x);
                Read(r.Y, $"{name} Y", -50000, 50000, errors, out var y);
                Read(r.W, Lang.F($"{name} Breite"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var w);
                Read(r.D, Lang.F($"{name} Tiefe"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var d);
                edits.Add(new ModuleEdit(r.Index, r.Name.Text ?? "", x, y, w, d));
            }
            SetError(error, errors);
            return errors.Count > 0 ? null : edits;
        }
        body.AddView(Ui.Button(c, Lang.T("Gewählte zusammenführen"), () =>
        {
            if (Edits() is not { } edits) return;
            var (merged, problem) = ModuleLayout.Merge(edits, rows.Select((r, i) => (r, i)).Where(t => t.r.Pick.Checked).Select(t => t.i).ToList());
            if (merged is null) { SetError(error, [problem!]); return; }
            rows.Clear();
            foreach (var m in merged) rows.Add(Row(m.Index, m.Name, m.X, m.Y, m.Width, m.Depth));
            Fill();
            Renamed();
        }));
        // Teilen und Anfügen
        void Replace(IReadOnlyList<ModuleEdit> edits)
        {
            var picks = rows.Select(r => r.Pick.Checked).ToList();
            rows.Clear();
            foreach (var m in edits) rows.Add(Row(m.Index, m.Name, m.X, m.Y, m.Width, m.Depth));
            for (var i = 0; i < picks.Count && i < rows.Count; i++) rows[i].Pick.Checked = picks[i];
            Fill();
            Renamed();
        }
        var splitBox = Choice(c, Names(), 0);
        splitModule = splitBox;
        var splitDir = Choice(c, [Lang.T("senkrecht (Breite teilen)"), Lang.T("waagerecht (Tiefe teilen)")], 0);
        var splitAt = Ui.TextField(c, "", Lang.T("bei mm (leer = Mitte)"));
        splitAt.InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal;
        body.AddView(Ui.Label(c, Lang.T("Teilen"), 13, true));
        body.AddView(Columns(c, splitBox, splitDir));
        body.AddView(Columns(c, splitAt, Ui.Button(c, Lang.T("Teilen"), () =>
        {
            if (Edits() is not { } edits || splitBox.SelectedItemPosition < 0) return;
            var i = splitBox.SelectedItemPosition;
            var across = splitDir.SelectedItemPosition == 0;
            var at = Fmt.Parse(splitAt.Text) ?? (across ? edits[i].Width : edits[i].Depth) / 2;
            var (split, problem) = ModuleLayout.Split(edits, i, across, at);
            if (split is null) { SetError(error, [problem!]); return; }
            splitAt.Text = "";
            Replace(split);
        })));
        var moveBox = Choice(c, Names(), Math.Min(1, rows.Count - 1));
        joinMove = moveBox;
        var joinLong = Choice(c, [Lang.T("mit der langen Seite"), Lang.T("mit der kurzen Seite")], 0);
        var targetBox = Choice(c, Names(), 0);
        joinTarget = targetBox;
        var joinSide = Choice(c, [Lang.T("rechts"), Lang.T("unten"), Lang.T("links"), Lang.T("oben")], 0);
        body.AddView(Ui.Label(c, Lang.T("Anfügen"), 13, true));
        body.AddView(Columns(c, moveBox, joinLong));
        body.AddView(Columns(c, Ui.Label(c, Lang.T("an"), 13, color: Ui.TextSecondary), targetBox, joinSide));
        body.AddView(Ui.Button(c, Lang.T("Anfügen"), () =>
        {
            if (Edits() is not { } edits || moveBox.SelectedItemPosition < 0 || targetBox.SelectedItemPosition < 0) return;
            var (joined, problem) = ModuleLayout.Join(edits, moveBox.SelectedItemPosition, targetBox.SelectedItemPosition, joinSide.SelectedItemPosition, joinLong.SelectedItemPosition == 0);
            if (joined is null) { SetError(error, [problem!]); return; }
            Replace(joined);
        }));
        body.AddView(error);
        AlertDialog? dialog = null;
        dialog = Show(c, Lang.T("Module"), body, Lang.T("Übernehmen"), () =>
        {
            if (Edits() is not { } edits) return false;
            apply(edits);
            return true;
        }, neutral: (Lang.T("Plan ansetzen…"), () => { dialog?.Dismiss(); appendPlan(); }));
    }
}

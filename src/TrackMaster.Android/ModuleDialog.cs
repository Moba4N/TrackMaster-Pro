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
        var rows = new List<(int Index, EditText Name, EditText X, EditText Y, EditText W, EditText D)>();
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
                foreach (var v in new View[] { r.Name, r.X, r.Y, r.W, r.D }) (v.Parent as ViewGroup)?.RemoveView(v);
                var head = Ui.Row(c, 8);
                head.AddView(Ui.Field(c, Lang.T("Name"), r.Name), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
                var remove = Ui.Button(c, "✕", () => { rows.Remove(r); Fill(); });
                remove.Enabled = rows.Count > 1;
                head.AddView(remove);
                card.AddView(head);
                card.AddView(Columns(c, Ui.Field(c, "X", r.X), Ui.Field(c, "Y", r.Y), Ui.Field(c, Lang.T("Breite"), r.W), Ui.Field(c, Lang.T("Tiefe"), r.D)));
                list.AddView(card);
            }
        }
        (int, EditText, EditText, EditText, EditText, EditText) Row(int index, string name, double x, double y, double w, double d)
            => (index, Ui.TextField(c, name), Ui.Number(c, x, 0, signed: true), Ui.Number(c, y, 0, signed: true), Ui.Number(c, w), Ui.Number(c, d));
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
        }));
        var error = Error(c);
        body.AddView(error);
        AlertDialog? dialog = null;
        dialog = Show(c, Lang.T("Module"), body, Lang.T("Übernehmen"), () =>
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
            if (errors.Count > 0) return false;
            apply(edits);
            return true;
        }, neutral: (Lang.T("Plan ansetzen…"), () => { dialog?.Dismiss(); appendPlan(); }));
    }
}

using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Ebenen bearbeiten: Name, Grundhöhe, sichtbar, aktive Ebene; hinzufügen, entfernen, Gewählte auf die aktive Ebene legen.</summary>
    public static void AskLevels(Context c, PlanSession session)
    {
        var plan = session.Plan;
        var rows = new List<(int Index, EditText Name, EditText Height, CheckBox Visible)>();
        (int, EditText, EditText, CheckBox) Row(int index, string name, double height, bool visible)
            => (index, Ui.TextField(c, name), Ui.Number(c, height, 0, signed: true), new CheckBox(c) { Text = Lang.T("Sichtbar"), Checked = visible });
        for (var i = 0; i < plan.Levels.Count; i++) rows.Add(Row(i, plan.Levels[i].Name, plan.Levels[i].Height, plan.Levels[i].Visible));
        var active = plan.ActiveLevel;
        var body = Ui.Column(c, 6);
        body.AddView(Hint(c, Lang.T("Neue Gleise kommen auf die aktive Ebene und beginnen in ihrer Grundhöhe. Ausgeblendete Ebenen fehlen in Plan und 3D-Ansicht, bleiben aber in Stückliste und Planprüfung. Ändert sich die Grundhöhe, wandern die Gleise der Ebene mit.")));
        var list = Ui.Column(c, 6);
        body.AddView(list);
        Button? add = null;
        void Fill()
        {
            list.RemoveAllViews();
            for (var i = 0; i < rows.Count; i++)
            {
                var k = i;
                var r = rows[k];
                foreach (var v in new View[] { r.Name, r.Height, r.Visible }) (v.Parent as ViewGroup)?.RemoveView(v);
                var card = Ui.Column(c, 2);
                var head = Ui.Row(c, 8);
                var radio = new RadioButton(c) { Text = Lang.T("Aktiv"), Checked = active == k };
                radio.Click += (_, _) => { active = k; r.Visible.Checked = true; Fill(); };
                head.AddView(radio);
                head.AddView(r.Visible);
                var only = Ui.Button(c, Lang.T("Nur diese"), () => { foreach (var o in rows) o.Visible.Checked = ReferenceEquals(o.Name, r.Name); active = k; Fill(); });
                head.AddView(only);
                var remove = Ui.Button(c, "✕", () => { rows.RemoveAt(k); if (active >= k) active = Math.Max(0, active - 1); Fill(); });
                remove.Enabled = rows.Count > 1;
                head.AddView(remove);
                card.AddView(head);
                card.AddView(Columns(c, Ui.Field(c, Lang.T("Name"), r.Name), Ui.Field(c, Lang.T("Grundhöhe"), r.Height)));
                list.AddView(card);
            }
            if (add is not null) add.Enabled = rows.Count < LayoutPlan.MaxLevels;
        }
        Fill();
        add = Ui.Button(c, Lang.T("Ebene hinzufügen"), () =>
        {
            if (rows.Count >= LayoutPlan.MaxLevels) return;
            var top = rows.Select(r => Fmt.Parse(r.Height.Text) ?? 0).DefaultIfEmpty(0).Max();
            rows.Add(Row(-1, PlanLevel.DefaultName(rows.Count), top + plan.LevelStep, true));
            active = rows.Count - 1;
            Fill();
        });
        add.Enabled = rows.Count < LayoutPlan.MaxLevels;
        body.AddView(add);
        body.AddView(Ui.Button(c, Lang.T("Alle zeigen"), () => { foreach (var r in rows) r.Visible.Checked = true; }));
        var error = Error(c);
        List<LevelEdit>? Edits()
        {
            var errors = new List<string>();
            var edits = new List<LevelEdit>();
            for (var i = 0; i < rows.Count; i++)
            {
                Read(rows[i].Height, Lang.F($"Ebene {i + 1} Grundhöhe"), -1000, 5000, errors, out var h);
                edits.Add(new LevelEdit(rows[i].Index, rows[i].Name.Text ?? "", h, rows[i].Visible.Checked || i == active));
            }
            SetError(error, errors);
            return errors.Count > 0 ? null : edits;
        }
        var selected = session.Selection.Count + (session.SelectedObject is null ? 0 : 1);
        AlertDialog? dialog = null;
        var move = Ui.Button(c, Lang.F($"Gewählte ({selected}) auf aktive Ebene"), () =>
        {
            if (Edits() is not { } edits) return;
            dialog?.Dismiss();
            session.ApplyLevels(edits, active);
            session.MoveSelectionToLevel(session.Plan.ActiveLevel);
        });
        move.Enabled = selected > 0;
        body.AddView(move);
        body.AddView(error);
        dialog = Show(c, Lang.T("Ebenen"), body, Lang.T("Übernehmen"), () =>
        {
            if (Edits() is not { } edits) return false;
            session.ApplyLevels(edits, active);
            return true;
        });
    }
}

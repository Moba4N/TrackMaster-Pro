using Android.App;
using Android.Content;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Klick mit „Platte ziehen“: Maße eines Moduls oder der ganzen Platte eintippen oder sie um 90° drehen.</summary>
    public static void AskBoardPart(Context c, PlanSession session, int module, Action allModules, Action legs)
    {
        var plan = session.Plan;
        var body = Ui.Column(c, 6);
        var error = Error(c);
        AlertDialog? dialog = null;
        if (module >= 0 && plan.BoardShape == BoardShape.Module && module < plan.Modules.Count)
        {
            var m = plan.Modules[module];
            body.AddView(Hint(c, Lang.T("Lage (X, Y = linke obere Ecke) und Größe in mm. Gleise und Gelände auf dem Modul wandern mit.")));
            var name = Ui.TextField(c, m.Name);
            var x = Ui.Number(c, m.X, 0, signed: true);
            var y = Ui.Number(c, m.Y, 0, signed: true);
            var w = Ui.Number(c, m.Width);
            var d = Ui.Number(c, m.Depth);
            body.AddView(Ui.Field(c, Lang.T("Name"), name));
            body.AddView(Columns(c, Ui.Field(c, "X", x), Ui.Field(c, "Y", y)));
            body.AddView(Columns(c, Ui.Field(c, Lang.T("Breite (mm)"), w), Ui.Field(c, Lang.T("Tiefe (mm)"), d)));
            body.AddView(Columns(c, Ui.Button(c, Lang.T("Um 90° drehen"), () => { dialog?.Dismiss(); session.RotateBoard(module); }),
                Ui.Button(c, Lang.T("Alle Module…"), () => { dialog?.Dismiss(); allModules(); })));
            body.AddView(error);
            dialog = Show(c, Lang.F($"Maße: {m.Name}"), body, Lang.T("Übernehmen"), () =>
            {
                var errors = new List<string>();
                Read(x, "X", -50000, 50000, errors, out var vx);
                Read(y, "Y", -50000, 50000, errors, out var vy);
                Read(w, Lang.T("Breite"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var vw);
                Read(d, Lang.T("Tiefe"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var vd);
                SetError(error, errors);
                if (errors.Count > 0) return false;
                session.SetModule(module, name.Text ?? "", vx, vy, vw, vd);
                return true;
            });
            return;
        }
        var hasLegs = BoardGeometry.HasLegs(plan.BoardShape);
        body.AddView(Hint(c, Lang.F($"{BoardGeometry.Describe(plan)}. Breite und Tiefe sind die Außenmaße in mm.")));
        var bw = Ui.Number(c, plan.BoardWidth);
        var bh = Ui.Number(c, plan.BoardHeight);
        var depth = Ui.Number(c, BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn));
        body.AddView(Columns(c, Ui.Field(c, Lang.T("Breite außen (mm)"), bw), Ui.Field(c, Lang.T("Tiefe außen (mm)"), bh)));
        if (hasLegs) body.AddView(Ui.Field(c, Lang.T("Schenkeltiefe (mm)"), depth));
        var rotate = Ui.Button(c, Lang.T("Um 90° drehen"), () => { dialog?.Dismiss(); session.RotateBoard(); });
        body.AddView(hasLegs ? (Android.Views.View)Columns(c, rotate, Ui.Button(c, Lang.T("Schenkel einzeln…"), () => { dialog?.Dismiss(); legs(); })) : rotate);
        body.AddView(error);
        dialog = Show(c, Lang.T("Plattenmaße"), body, Lang.T("Übernehmen"), () =>
        {
            var errors = new List<string>();
            Read(bw, Lang.T("Breite"), 50, 50000, errors, out var vw);
            Read(bh, Lang.T("Tiefe"), 50, 50000, errors, out var vh);
            double vd = 0;
            if (hasLegs) Read(depth, Lang.T("Schenkeltiefe"), 50, 50000, errors, out vd);
            SetError(error, errors);
            if (errors.Count > 0) return false;
            session.SetBoardSize(vw, vh, hasLegs ? vd : null);
            return true;
        });
    }
}

using Android.App;
using Android.Content;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

public static partial class Dialogs
{
    /// <summary>Auswahl einer Gebäudevorlage für die Spur des Plans; Antippen setzt das Gebäude.</summary>
    public static void AskBuildingKit(Context c, Gauge gauge, Action<BuildingKit> pick)
    {
        var kits = BuildingKits.For(gauge);
        var body = Ui.Column(c, 4);
        body.AddView(Hint(c, kits.FirstOrDefault()?.Converted == true
            ? $"Für Spur {gauge.Id} gibt es keine eigenen Bausätze; die Vorlagen sind maßstäblich auf {gauge.Scale} umgerechnet."
            : $"Vorlagen nach Bausätzen in Spur {gauge.Id}, mit Grundmaß und Höhe. Antippen setzt das Gebäude in die Mitte der Ansicht."));
        AlertDialog? dialog = null;
        foreach (var cat in BuildingKits.Categories)
        {
            var items = kits.Where(k => k.Category == cat).ToList();
            if (items.Count == 0) continue;
            body.AddView(Section(c, cat));
            foreach (var k in items)
            {
                var row = Ui.Column(c, 0);
                row.SetPadding(Ui.Dp(10), Ui.Dp(8), Ui.Dp(10), Ui.Dp(8));
                row.Background = Ui.Rounded(Ui.Panel, Ui.Border, 6);
                row.AddView(Ui.Label(c, k.Label, 14, true));
                row.AddView(Ui.Label(c, k.Details, 12.5, color: Ui.TextSecondary));
                var kit = k;
                row.Click += (_, _) => { dialog?.Dismiss(); pick(kit); };
                body.AddView(row, Ui.Fill());
            }
        }
        dialog = Show(c, "Gebäudevorlage", body, okText: null, cancelText: "Abbrechen");
    }
}

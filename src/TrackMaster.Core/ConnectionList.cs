using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>Eine Zeile der Anschlussliste.</summary>
public sealed record ConnectionRow(string Kind, string No, string Name, string Location, string From, string To,
    string Color, string Section, string Length, string Address, string Note);

/// <summary>
/// Vollständige Anschlussliste der Elektrik: Stromquellen, Verteiler, Gleisanschlüsse, Kabel (von – nach), Rückmelder,
/// Weichen und Signale mit Adressen. Als CSV (Semikolon, UTF-8) oder als Text zum Ausdrucken.
/// </summary>
public static class ConnectionList
{
    public static IReadOnlyList<ConnectionRow> Build(LayoutPlan plan)
    {
        var rows = new List<ConnectionRow>();
        var current = Electrics.Current(plan);
        var g = plan.Gauge.TrackWidth;
        var sections = Electrics.Sections(plan);
        string Piece(int id) => plan.Find(id) is { } p ? Lang.F($"Gleis {p.Def.Number} (Nr. {id})") : Lang.F($"Gleis Nr. {id}");
        string Section(int pieceId) => sections.FirstOrDefault(s => s.PieceIds.Contains(pieceId)) is { } s ? Lang.F($"Abschnitt {s.Number}") : "";
        string Wires((double X, double Y) p) => string.Join(", ", WireLayout.WiresAt(plan, p).Select(w => w.Name));
        string Join(params string[] parts) => string.Join(", ", parts.Where(x => x.Length > 0));

        foreach (var src in PowerSupply.Sources(plan))
        {
            var type = PowerSupply.Type(src);
            var feeders = string.Join(", ", PowerSupply.FeedersOf(plan, src).Select(f => f.Label));
            var gt = PowerSupply.GroundTerminal(src);
            var note = gt is { } t ? (WireLayout.WiresAt(plan, t).Any() ? Lang.F($"Masse (0): {Wires(t)}") : Lang.T("Masse (0) fehlt")) : "";
            var bWires = Wires(PowerSupply.Terminal(src));
            rows.Add(new(Lang.T("Stromquelle"), src.Name, PowerSupply.TypeName(type), "",
                bWires.Length == 0 ? "" : PowerSupply.HasGround(type) ? Lang.F($"Bahnstrom (B): {bWires}") : bWires,
                feeders.Length == 0 ? "" : Lang.F($"Gleisanschlüsse {feeders}"), "", "", "", "", note));
        }
        foreach (var d in PowerSupply.Distributors(plan))
        {
            var wires = PowerSupply.ClampPoints(d).SelectMany(c => WireLayout.WiresAt(plan, c)).DistinctBy(w => w.Id).Select(w => w.Name);
            rows.Add(new(Lang.T("Verteiler"), d.Name, Lang.T("Verteilerleiste"), "", string.Join(", ", wires), "",
                WireLayout.ColorName(PowerSupply.DistributorColor(d)), "", "", "", Lang.F($"{PowerSupply.Clamps} Klemmen")));
        }
        foreach (var mod in PowerSupply.Modules(plan))
        {
            var m = Yamorc.ModelOf(mod);
            var wires = Yamorc.Ports(mod).SelectMany(c => WireLayout.WiresAt(plan, c)).DistinctBy(w => w.Id).Select(w => w.Name);
            var assigned = Yamorc.Assigned(plan, mod).Select(a => Lang.F($"{a.Port}: {a.Label}"));
            var range = Yamorc.Range(mod) is { } r ? $"{r.First}–{r.Last}" : "";
            rows.Add(new(Lang.T("Decoder-Baustein"), mod.Name, m is null ? mod.Model ?? "" : $"{m.Maker} {m.Title}", "", string.Join(", ", wires), string.Join("; ", assigned),
                "", "", "", range, m?.Bus ?? ""));
        }
        foreach (var f in plan.Feeders)
        {
            if (plan.Find(f.PieceId) is not { } p) continue;
            var pt = Electrics.FeederPoint(plan, p, f);
            rows.Add(new(Lang.T("Gleisanschluss"), f.Label, "", Join(Piece(f.PieceId), Section(f.PieceId)), f.Circuit, Wires(pt), "", "", "", "",
                current == CurrentKind.Gleichstrom && f.Reversed ? Lang.T("Polung getauscht") : ""));
        }
        foreach (var w in plan.Scenery.Where(s => s.IsWire && s.Points.Count >= 2))
        {
            var len = WireLayout.Length(w);
            rows.Add(new(Lang.T("Kabel"), w.Name, WireLayout.UseName(WireLayout.Use(w)), "",
                WireLayout.EndAnchor(plan, w, false)?.Text ?? Lang.T("frei"), WireLayout.EndAnchor(plan, w, true)?.Text ?? Lang.T("frei"),
                WireLayout.ColorName(WireLayout.ColorOf(w, current)), WireLayout.SectionText(WireLayout.Section(w, plan.Gauge)),
                Fmt.Num(len / 1000, 2), "", Lang.F($"mit Zugabe {Fmt.Num(len * (1 + WireLayout.Reserve) / 1000, 2)} m")));
        }
        foreach (var d in plan.Detectors)
        {
            if (plan.Find(d.PieceId) is not { } p) continue;
            var feedback = d.Address is { } da && Yamorc.ModuleFor(plan, da, ModuleRole.Melden) is { } fm ? Lang.F($"{fm.Name}, Melder {da - Yamorc.Range(fm)!.Value.First + 1}") : "";
            rows.Add(new(Lang.T("Rückmelder"), d.Name.Length > 0 ? d.Name : Lang.F($"Nr. {d.Id}"), Detector.TypeName(d.Type),
                Join(Piece(d.PieceId), Section(d.PieceId)), Wires(Electrics.DetectorPoint(p, g)), feedback, "", "", "",
                d.Address?.ToString(CultureInfo.InvariantCulture) ?? "", ""));
        }
        foreach (var i in Electrics.SwitchingItems(plan))
            rows.Add(new(i.Kind, i.Label, "", i.PieceId is { } pid ? Piece(pid) : "", i.Address is { } ma && Yamorc.ModuleFor(plan, ma, ModuleRole.Schalten) is { } mod
                    ? Lang.F($"{mod.Name}, Ausgang {ma - Yamorc.Range(mod)!.Value.First + 1}") : "", "", "", "", "",
                i.Address is { } a ? (i.AddressCount > 1 ? $"{a}–{a + i.AddressCount - 1}" : a.ToString(CultureInfo.InvariantCulture)) : "", ""));
        return rows;
    }

    private static string[] Head =>
    [
        Lang.T("Art"), Lang.T("Nr."), Lang.T("Bezeichnung"), Lang.T("Ort"), Lang.T("Von / Anschluss"), Lang.T("Nach"),
        Lang.T("Farbe"), Lang.T("Querschnitt"), Lang.T("Länge (m)"), Lang.T("Adresse"), Lang.T("Hinweis"),
    ];

    private static string[] Cells(ConnectionRow r) => [r.Kind, r.No, r.Name, r.Location, r.From, r.To, r.Color, r.Section, r.Length, r.Address, r.Note];

    /// <summary>CSV mit Semikolon (für Excel/LibreOffice in deutscher Einstellung), UTF-8 mit BOM.</summary>
    public static string ToCsv(LayoutPlan plan)
    {
        static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("﻿");
        sb.Append(string.Join(';', Head.Select(Quote))).Append("\r\n");
        foreach (var r in Build(plan)) sb.Append(string.Join(';', Cells(r).Select(Quote))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Anschlussliste als Text, nach Art gruppiert.</summary>
    public static string ToText(LayoutPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.F($"Anschlussliste – {plan.Name}"));
        sb.AppendLine(new string('=', 60));
        var rows = Build(plan);
        if (rows.Count == 0) { sb.AppendLine(Lang.T("Noch keine Elektrik im Plan.")); return sb.ToString(); }
        foreach (var grp in rows.GroupBy(r => r.Kind))
        {
            sb.AppendLine();
            sb.AppendLine($"{grp.Key} ({grp.Count()})");
            foreach (var r in grp)
            {
                var parts = new List<string>();
                if (r.Name.Length > 0) parts.Add(r.Name);
                if (r.Location.Length > 0) parts.Add(r.Location);
                if (r.From.Length > 0 || r.To.Length > 0) parts.Add(r.To.Length == 0 ? r.From : r.From.Length == 0 ? $"→ {r.To}" : $"{r.From} → {r.To}");
                if (r.Color.Length > 0) parts.Add(r.Color);
                if (r.Section.Length > 0) parts.Add(r.Section);
                if (r.Length.Length > 0) parts.Add($"{r.Length} m");
                if (r.Address.Length > 0) parts.Add(Lang.F($"Adresse {r.Address}"));
                if (r.Note.Length > 0) parts.Add(r.Note);
                sb.AppendLine($"  {r.No,-8} {string.Join(" · ", parts)}");
            }
        }
        return sb.ToString();
    }
}

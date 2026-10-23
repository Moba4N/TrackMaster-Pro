using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

public sealed record BomRow(TrackPieceDef Def, int Count);

/// <summary>Zubehör für die Stückliste (Antriebe, Decoder, Signale, Einspeiser, Isolierverbinder).</summary>
public sealed record AccessoryRow(string Name, string Detail, int Count);

public static class BillOfMaterials
{
    /// <summary>Gleise nach Artikel gezählt. Flexgleis wird zusammengefasst: Gesamtlänge und Zahl der Stücke im Handel (+5 % Verschnitt).</summary>
    public static IReadOnlyList<BomRow> Build(LayoutPlan plan)
    {
        var rows = plan.Pieces.Where(p => !p.Def.IsFlex).GroupBy(p => p.Key)
            .Select(g => new BomRow(Catalog.Get(g.Key), g.Count()))
            .OrderBy(r => r.Def.SystemName).ThenBy(r => r.Def.Category).ThenBy(r => r.Def.Number, StringComparer.Ordinal)
            .ToList();
        var flex = plan.Pieces.Where(p => p.Def.IsFlex).Sum(p => p.Def.TrackLength);
        if (flex > 0)
        {
            var stock = plan.Gauge.FlexStockLength;
            var def = new TrackPieceDef
            {
                Number = "Flexgleis", Kind = PieceKind.Straight, Length = flex, Category = "Flexgleis",
                Name = $"Flexgleis, Stücke zu {Fmt.Num(stock, 0)} mm", Spec = $"gesamt {Fmt.Num(flex / 1000, 2)} m",
                GaugeId = plan.GaugeId, SystemId = Catalog.FlexSystemId, SystemName = "Flexgleis", Verified = true, Key = $"{plan.GaugeId}|{Catalog.FlexSystemId}|summe",
            };
            rows.Add(new BomRow(def, (int)Math.Ceiling(flex * 1.05 / stock)));
        }
        return rows;
    }

    public static IReadOnlyList<AccessoryRow> Accessories(LayoutPlan plan)
    {
        var rows = new List<AccessoryRow>();
        var drives = plan.Pieces.Sum(p => p.Def.Drives);
        if (drives > 0) rows.Add(new("Weichenantrieb", "je Weiche einer, DKW zwei", drives));
        foreach (var g in plan.Signals.GroupBy(s => s.Type).OrderBy(g => g.Key))
            rows.Add(new(Signal.TypeName(g.Key), "Lichtsignal", g.Count()));
        if (plan.Feeders.Count > 0) rows.Add(new("Anschlussgleis / Einspeiser", "Einspeisungen", plan.Feeders.Count));
        if (plan.Isolations.Count > 0)
        {
            var threeRail = Electrics.IsThreeRail(plan);
            rows.Add(threeRail
                ? new("Mittelleiter-Trennstelle", "Isolierung des Mittelleiters", plan.Isolations.Count)
                : new("Isolierschienenverbinder", "zwei je Trennstelle", plan.Isolations.Count * 2));
        }
        var addresses = Electrics.SwitchingItems(plan).Sum(i => i.AddressCount);
        if (addresses > 0) rows.Add(new("Weichendecoder (4 Ausgänge)", $"{addresses} Adressen", (int)Math.Ceiling(addresses / 4.0)));
        return rows;
    }

    /// <summary>CSV mit Semikolon und BOM, damit Excel Umlaute und Spalten richtig erkennt.</summary>
    public static string ToCsv(LayoutPlan plan)
    {
        var sb = new StringBuilder("﻿");
        sb.Append("\"Hersteller/System\";\"Artikel\";\"Bezeichnung\";\"Daten\";\"Anzahl\"\r\n");
        foreach (var r in Build(plan))
            sb.Append(string.Join(';', new[] { r.Def.SystemName, r.Def.Number, r.Def.Name, r.Def.Spec, r.Count.ToString(CultureInfo.InvariantCulture) }.Select(Quote))).Append("\r\n");
        foreach (var a in Accessories(plan))
            sb.Append(string.Join(';', new[] { "Zubehör", "", a.Name, a.Detail, a.Count.ToString(CultureInfo.InvariantCulture) }.Select(Quote))).Append("\r\n");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}

/// <summary>Gleisplan als SVG im Maßstab 1:1 (1 Einheit = 1 mm), z. B. zum Ausdrucken in Originalgröße.</summary>
public static class SvgExport
{
    public static string Build(LayoutPlan plan)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 2).ToString(inv);
        var g = plan.Gauge.TrackWidth;
        var bed = new StringBuilder();
        var rails = new StringBuilder();
        var stops = new StringBuilder();
        foreach (var p in plan.Pieces)
        {
            var legs = p.WorldLegs();
            foreach (var l in legs)
            {
                bed.Append(PathData(l, 0, N));
                rails.Append(PathData(l, g / 2, N)).Append(PathData(l, -g / 2, N));
            }
            if (p.Def.Kind == PieceKind.Buffer)
            {
                var e = legs[0].End;
                double nx = -Math.Sin(e.HeadingRad) * g * 1.1, ny = Math.Cos(e.HeadingRad) * g * 1.1;
                stops.Append($"M{N(e.X - nx)} {N(e.Y - ny)}L{N(e.X + nx)} {N(e.Y + ny)}");
            }
        }
        const double m = 40;
        double w = plan.BoardWidth, h = plan.BoardHeight;
        var title = System.Security.SecurityElement.Escape(plan.Name);
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="{N(w + 2 * m)}mm" height="{N(h + 2 * m)}mm" viewBox="{N(-m)} {N(-m)} {N(w + 2 * m)} {N(h + 2 * m)}">
<title>{title} – Spur {plan.GaugeId}</title>
<rect x="0" y="0" width="{N(w)}" height="{N(h)}" fill="#E3EBD7" stroke="#8FA67A" stroke-width="1"/>
<path d="{bed}" fill="none" stroke="#CFC3AF" stroke-width="{N(g * 2.4)}"/>
<path d="{bed}" fill="none" stroke="#8C7B66" stroke-width="{N(g * 1.9)}" stroke-dasharray="{N(g * 0.16)} {N(g * 0.3)}"/>
<path d="{rails}" fill="none" stroke="#4E5257" stroke-width="{N(Math.Max(g * 0.11, 0.5))}"/>
{(stops.Length > 0 ? $"<path d=\"{stops}\" fill=\"none\" stroke=\"#C42B1C\" stroke-width=\"{N(g * 0.5)}\" stroke-linecap=\"round\"/>" : "")}
<text x="0" y="-12" font-family="Segoe UI, sans-serif" font-size="14" fill="#333">{title} · Spur {plan.GaugeId} · {Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} mm · Maßstab 1:1</text>
</svg>
""";
    }

    private static string PathData(Leg l, double offset, Func<double, string> n)
    {
        var (x0, y0, x1, y1, r) = l.Offset(offset);
        return l.IsArc
            ? $"M{n(x0)} {n(y0)}A{n(r)} {n(r)} 0 0 {(l.Turn > 0 ? 1 : 0)} {n(x1)} {n(y1)}"
            : $"M{n(x0)} {n(y0)}L{n(x1)} {n(y1)}";
    }
}

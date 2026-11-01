using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

public sealed record BomRow(TrackPieceDef Def, int Count);

/// <summary>Zubehör für die Stückliste (Antriebe, Decoder, Signale, Einspeiser, Isolierverbinder).</summary>
public sealed record AccessoryRow(string Name, string Detail, int Count);

/// <summary>Zeile der Stückliste mit Bestand und Preis. <see cref="Key"/> ist der Schlüssel in <see cref="LayoutPlan.Prices"/> und <see cref="LayoutPlan.Stock"/>.</summary>
public sealed record CostRow(string Key, string System, string Article, string Name, string Detail, int Count, int Stock, int ToBuy, double? Price)
{
    public double? Total => Price * Count;
    public double? TotalToBuy => Price * ToBuy;
}

public static class BillOfMaterials
{
    /// <summary>Gleise nach Artikel gezählt. Flexgleis wird zusammengefasst: Gesamtlänge und Zahl der Stücke im Handel (+5 % Verschnitt).</summary>
    public static IReadOnlyList<BomRow> Build(LayoutPlan plan)
    {
        var rows = plan.Pieces.Where(p => !p.Def.IsFlex).GroupBy(p => p.Key)
            .Select(g => new BomRow(Catalog.Get(g.Key), g.Count()))
            .OrderBy(r => r.Def.SystemName).ThenBy(r => r.Def.Category).ThenBy(r => r.Def.Number, StringComparer.Ordinal)
            .ToList();
        foreach (var group in plan.Pieces.Where(p => p.Def.IsFlex).GroupBy(p => p.Def.SystemId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var flex = group.Sum(p => p.Def.TrackLength);
            var stock = Catalog.FlexOf(plan.GaugeId, group.Key) ?? Catalog.Flex(plan.Gauge, null);
            var def = new TrackPieceDef
            {
                Number = stock.Number, Kind = PieceKind.Straight, Length = flex, Category = Lang.T("Flexgleis"),
                Name = Lang.F($"Flexgleis, Stücke zu {Fmt.Num(stock.Length, 0)} mm"), Spec = $"gesamt {Fmt.Num(flex / 1000, 2)} m",
                GaugeId = plan.GaugeId, SystemId = group.Key, SystemName = group.First().Def.SystemName, Verified = group.First().Def.Verified,
                Key = $"{plan.GaugeId}|{group.Key}|summe",
            };
            rows.Add(new BomRow(def, (int)Math.Ceiling(flex * 1.05 / stock.Length)));
        }
        return rows;
    }

    public static IReadOnlyList<AccessoryRow> Accessories(LayoutPlan plan)
    {
        var rows = new List<AccessoryRow>();
        var drives = plan.Pieces.Sum(p => p.Def.Drives);
        if (drives > 0) rows.Add(new(Lang.T("Weichenantrieb"), Lang.T("je Weiche einer, DKW zwei"), drives));
        foreach (var g in plan.Signals.GroupBy(s => s.Type).OrderBy(g => g.Key))
            rows.Add(new(Signal.TypeName(g.Key), Lang.T("Lichtsignal"), g.Count()));
        if (plan.Feeders.Count > 0) rows.Add(new(Lang.T("Anschlussgleis / Einspeiser"), Lang.T("Einspeisungen"), plan.Feeders.Count));
        if (plan.Isolations.Count > 0)
        {
            var threeRail = Electrics.IsThreeRail(plan);
            rows.Add(threeRail
                ? new(Lang.T("Mittelleiter-Trennstelle"), Lang.T("Isolierung des Mittelleiters"), plan.Isolations.Count)
                : new(Lang.T("Isolierschienenverbinder"), Lang.T("zwei je Trennstelle"), plan.Isolations.Count * 2));
        }
        var addresses = Electrics.SwitchingItems(plan).Sum(i => i.AddressCount);
        if (addresses > 0) rows.Add(new(Lang.T("Weichendecoder (4 Ausgänge)"), Lang.F($"{addresses} Adressen"), (int)Math.Ceiling(addresses / 4.0)));
        foreach (var g in plan.Detectors.GroupBy(d => d.Type).OrderBy(g => g.Key))
            rows.Add(new(Detector.TypeName(g.Key), Lang.T("Rückmelder"), g.Count()));
        if (plan.Detectors.Count > 0)
            rows.Add(new(Lang.F($"Rückmeldemodul ({Detector.InputsPerModule} Eingänge)"), Lang.F($"{plan.Detectors.Count} Rückmelder"), (int)Math.Ceiling(plan.Detectors.Count / (double)Detector.InputsPerModule)));
        return rows;
    }

    /// <summary>Schlüssel für Preis und Bestand eines Zubehörteils.</summary>
    public static string AccessoryKey(AccessoryRow a) => "zubehoer|" + a.Name;

    /// <summary>
    /// Stückliste mit Bestand und Preisen: was gebraucht wird, was schon da ist, was noch zu kaufen ist und was es kostet.
    /// </summary>
    public static IReadOnlyList<CostRow> Costs(LayoutPlan plan)
    {
        CostRow Row(string key, string system, string article, string name, string detail, int count)
        {
            var stock = plan.Stock.GetValueOrDefault(key);
            double? price = plan.Prices.TryGetValue(key, out var p) ? p : null;
            var buy = Math.Max(0, count - stock);
            return new CostRow(key, system, article, name, detail, count, stock, buy, price);
        }
        var rows = Build(plan).Select(r => Row(r.Def.Key, r.Def.SystemName, r.Def.Number, r.Def.Name, r.Def.Spec, r.Count)).ToList();
        rows.AddRange(Accessories(plan).Select(a => Row(AccessoryKey(a), Lang.T("Zubehör"), "", a.Name, a.Detail, a.Count)));
        return rows;
    }

    /// <summary>Summen: alles neu gekauft, noch zu kaufen (abzüglich Bestand) und Zahl der Zeilen ohne Preis.</summary>
    public static (double All, double ToBuy, int WithoutPrice) Totals(IReadOnlyList<CostRow> rows)
        => (rows.Sum(r => (r.Price ?? 0) * r.Count), rows.Sum(r => (r.Price ?? 0) * r.ToBuy), rows.Count(r => r.Price is null));

    /// <summary>Geldbetrag mit zwei Nachkommastellen (ohne Währungszeichen).</summary>
    public static string Money(double v) => Fmt.Num(v, 2);

    /// <summary>CSV mit Semikolon und BOM, damit Excel Umlaute und Spalten richtig erkennt; mit Bestand und Preisen, soweit eingetragen.</summary>
    public static string ToCsv(LayoutPlan plan)
    {
        var sb = new StringBuilder("﻿");
        var rows = Costs(plan);
        var money = plan.Prices.Count > 0;
        var stock = plan.Stock.Count > 0;
        var head = new List<string> { Lang.T("Hersteller/System"), Lang.T("Artikel"), Lang.T("Bezeichnung"), Lang.T("Daten"), Lang.T("Anzahl") };
        if (stock) head.AddRange([Lang.T("Bestand"), Lang.T("Zu kaufen")]);
        if (money) head.AddRange([Lang.T("Einzelpreis"), Lang.T("Summe")]);
        sb.Append(string.Join(';', head.Select(Quote))).Append("\r\n");
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        foreach (var r in rows)
        {
            var cells = new List<string> { r.System, r.Article, r.Name, r.Detail, I(r.Count) };
            if (stock) cells.AddRange([I(r.Stock), I(r.ToBuy)]);
            if (money) cells.AddRange([r.Price is { } p ? Money(p) : "", r.Price is { } q ? Money(q * (stock ? r.ToBuy : r.Count)) : ""]);
            sb.Append(string.Join(';', cells.Select(Quote))).Append("\r\n");
        }
        if (money)
        {
            var (all, buy, _) = Totals(rows);
            var cells = new List<string> { "", "", stock ? Lang.T("Summe zu kaufen") : Lang.T("Summe"), "", "" };
            if (stock) cells.AddRange(["", ""]);
            cells.AddRange(["", Money(stock ? buy : all)]);
            sb.Append(string.Join(';', cells.Select(Quote))).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Liest eine Preisliste als CSV (Artikelnummer; Preis – Trennzeichen Semikolon, Komma oder Tabulator) und trägt die Preise
    /// für alle Artikel des Plans mit dieser Nummer ein. Liefert die Zahl der übernommenen Preise.
    /// </summary>
    public static int ImportPrices(LayoutPlan plan, string csv)
    {
        var byNumber = Costs(plan).Where(r => r.Article.Length > 0).GroupBy(r => r.Article.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(r => r.Key).ToList(), StringComparer.OrdinalIgnoreCase);
        var byName = Costs(plan).GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(r => r.Key).ToList(), StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var raw in csv.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            var sep = line.Contains(';') ? ';' : line.Contains('\t') ? '\t' : ',';
            var cells = line.Split(sep).Select(c => c.Trim().Trim('"').Trim()).ToList();
            if (cells.Count < 2) continue;
            // Preis: letzte Zelle, die eine Zahl ist (mit Komma oder Punkt, gern mit €)
            double? price = null;
            for (var i = cells.Count - 1; i >= 1 && price is null; i--)
                price = ParseMoney(cells[i]);
            if (price is not { } value) continue;
            var keys = byNumber.GetValueOrDefault(cells[0]) ?? byName.GetValueOrDefault(cells[0]);
            if (keys is null) continue;
            foreach (var k in keys) plan.Prices[k] = value;
            n++;
        }
        return n;
    }

    /// <summary>Betrag aus Text („12,90“, „12.90 €“, „1.234,50“); null, wenn es keiner ist.</summary>
    public static double? ParseMoney(string text)
    {
        var t = text.Replace("€", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Replace("$", "").Replace(" ", "").Trim();
        if (t.Length == 0 || !t.Any(char.IsDigit) || t.Any(ch => !(char.IsDigit(ch) || ch is ',' or '.' or '-'))) return null;
        if (t.Contains(',') && t.Contains('.'))
            t = t.LastIndexOf(',') > t.LastIndexOf('.') ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 && double.IsFinite(v) ? Math.Round(v, 2) : null;
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
        var tables = new StringBuilder();
        foreach (var t in plan.Pieces.Where(p => p.Def.IsTable).Select(p => TableShape.Of(p)!))
        {
            if (t.Round)
                tables.Append(Lang.F($"<circle cx=\"{N(t.Center.X)}\" cy=\"{N(t.Center.Y)}\" r=\"{N(t.RimRadius)}\" fill=\"#C9C2B6\" stroke=\"#555\" stroke-width=\"0.6\"/>"))
                      .Append(Lang.F($"<circle cx=\"{N(t.Center.X)}\" cy=\"{N(t.Center.Y)}\" r=\"{N(t.PitRadius)}\" fill=\"#7A7062\" stroke=\"#555\" stroke-width=\"0.6\"/>"));
            else
                tables.Append($"<path d=\"M{string.Join("L", t.Pit.Select(q => $"{N(q.X)} {N(q.Y)}"))}Z\" fill=\"#7A7062\" stroke=\"#555\" stroke-width=\"0.6\"/>");
            tables.Append($"<path d=\"M{N(t.BridgeA.X)} {N(t.BridgeA.Y)}L{N(t.BridgeB.X)} {N(t.BridgeB.Y)}\" stroke=\"#4E463E\" stroke-width=\"{N(g * 2.6)}\"/>");
        }
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
        // Beschriftungen und Maßlinien
        var notes = new StringBuilder();
        string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        foreach (var s in plan.Scenery.Where(s => s.IsAnnotation))
        {
            if (s.Kind == SceneryKind.Label)
            {
                notes.Append(Lang.F($"<text x=\"{N(s.X)}\" y=\"{N(s.Y)}\" font-size=\"{N(s.Height)}\" text-anchor=\"middle\" dominant-baseline=\"central\" font-family=\"Segoe UI, sans-serif\" font-weight=\"600\" transform=\"rotate({N(s.Rotation)} {N(s.X)} {N(s.Y)})\">{Esc(s.Name)}</text>"));
                continue;
            }
            if (s.Points.Count < 2) continue;
            var d = Annotation.Dimension(s, s.Height);
            var sw = N(Math.Max(d.TextSize * 0.06, 0.4));
            notes.Append($"<path d=\"M{N(d.P1.X)} {N(d.P1.Y)}L{N(d.A.X)} {N(d.A.Y)}M{N(d.P2.X)} {N(d.P2.Y)}L{N(d.B.X)} {N(d.B.Y)}M{N(d.A.X)} {N(d.A.Y)}L{N(d.B.X)} {N(d.B.Y)}\" stroke=\"#1F4E79\" stroke-width=\"{sw}\" fill=\"none\"/>");
            notes.Append(Lang.F($"<text x=\"{N(d.TextAt.X)}\" y=\"{N(d.TextAt.Y)}\" font-size=\"{N(d.TextSize)}\" fill=\"#1F4E79\" text-anchor=\"middle\" dominant-baseline=\"central\" font-family=\"Segoe UI, sans-serif\" transform=\"rotate({N(d.TextAngle)} {N(d.TextAt.X)} {N(d.TextAt.Y)})\">{Esc(d.Text)}</text>"));
        }
        const double m = 40;
        double w = plan.BoardWidth, h = plan.BoardHeight;
        var title = System.Security.SecurityElement.Escape(plan.Name);
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="{N(w + 2 * m)}mm" height="{N(h + 2 * m)}mm" viewBox="{N(-m)} {N(-m)} {N(w + 2 * m)} {N(h + 2 * m)}">
<title>{title} – Spur {plan.GaugeId}</title>
<path d="{string.Concat(BoardGeometry.Outlines(plan).Select(o => "M" + string.Join("L", o.Select(q => $"{N(q.X)} {N(q.Y)}")) + "Z"))}" fill="#E3EBD7" fill-rule="evenodd" stroke="#8FA67A" stroke-width="1"/>
{tables}
<path d="{bed}" fill="none" stroke="#CFC3AF" stroke-width="{N(g * 2.4)}"/>
<path d="{bed}" fill="none" stroke="#8C7B66" stroke-width="{N(g * 1.9)}" stroke-dasharray="{N(g * 0.16)} {N(g * 0.3)}"/>
<path d="{rails}" fill="none" stroke="#4E5257" stroke-width="{N(Math.Max(g * 0.11, 0.5))}"/>
{(stops.Length > 0 ? $"<path d=\"{stops}\" fill=\"none\" stroke=\"#C42B1C\" stroke-width=\"{N(g * 0.5)}\" stroke-linecap=\"round\"/>" : "")}
{notes}
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

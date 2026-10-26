namespace TrackMaster.Core;

public enum HelixShape { Rund, Oval }

/// <summary>
/// Angaben für eine Gleiswendel: rund oder oval (zwei Halbkreise mit geraden Stücken dazwischen), ein- oder zweigleisig.
/// Beim zweigleisigen Bau liegt das zweite Gleis außen, im Abstand <see cref="Spacing"/>; ohne passendes Katalog-Bogengleis
/// (<see cref="OuterCurveKey"/> = null) wird es aus Flexgleis mit genau diesem Radius gebaut.
/// </summary>
public sealed record HelixSpec(
    string CurveKey, double Turns, double Rise, bool RightHand,
    HelixShape Shape = HelixShape.Rund, string? StraightKey = null, int StraightCount = 0,
    int Tracks = 1, double Spacing = 0, string? OuterCurveKey = null)
{
    public TrackPieceDef Curve => Catalog.Get(CurveKey);
    public TrackPieceDef? Straight => Shape == HelixShape.Oval && StraightKey is not null && StraightCount > 0 ? Catalog.Get(StraightKey) : null;
    /// <summary>Länge der Geraden auf jeder Längsseite des Ovals.</summary>
    public double StraightLength => Straight is { } s ? s.TrackLength * StraightCount : 0;
    public double OuterRadius => Tracks == 2 ? (OuterCurveKey is { } k ? Catalog.Get(k).Radius : Curve.Radius + Spacing) : Curve.Radius;
    public double TotalHeight => Rise * Turns;
}

/// <summary>Ein Zuschnitt- oder Kaufteil für den Wendelbau.</summary>
public sealed record HelixPart(string Name, int Count, string Size, string Note, double AreaM2 = 0);

/// <summary>Holz und Kleinteile für eine Gleiswendel, mit allen Aufmaßen.</summary>
public sealed record HelixMaterial(IReadOnlyList<HelixPart> Parts, IReadOnlyList<string> Notes,
    double BoardThickness, double TrackBedWidth, double Edge, double SawAllowance, double InnerBoardRadius, double OuterBoardRadius, int Sheets)
{
    public string ToText()
    {
        var lines = new List<string> { "Materialliste Gleiswendel", "" };
        foreach (var p in Parts) lines.Add($"{p.Count} × {p.Name}{(p.Size.Length > 0 ? ": " + p.Size : "")}{(p.Note.Length > 0 ? $" ({p.Note})" : "")}");
        lines.Add("");
        lines.AddRange(Notes);
        return string.Join(Environment.NewLine, lines);
    }

    public string ToCsv()
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "Anzahl;Teil;Maße;Hinweis" };
        lines.AddRange(Parts.Select(p => $"{p.Count};{Q(p.Name)};{Q(p.Size)};{Q(p.Note)}"));
        lines.Add("");
        lines.AddRange(Notes.Select(Q));
        return string.Join("\r\n", lines);
    }
}

public static class Helix
{
    /// <summary>Platte, aus der die Trassenteile geschnitten werden (übliches Baumarktmaß).</summary>
    public const double SheetLength = 2500, SheetWidth = 1250;

    /// <summary>Breite des Gleises mit Schwellen bzw. Bettung, geschätzt aus der Spurweite (H0: 45 mm).</summary>
    public static double TrackBedWidth(Gauge g) => Math.Round(g.TrackWidth * 2.7);

    /// <summary>Üblicher Gleisabstand für zweigleisige Wendeln (H0: 62 mm).</summary>
    public static double DefaultSpacing(Gauge g) => Math.Round(g.TrackWidth * 3.75);

    /// <summary>Bogengleis für das äußere Gleis: Katalogradius möglichst nah an Radius + Abstand (höchstens 15 % daneben), sonst null (= Flexgleis).</summary>
    public static TrackPieceDef? OuterCurve(IEnumerable<TrackPieceDef> curves, TrackPieceDef inner, double spacing, HelixShape shape)
        => curves.Where(c => c.Kind == PieceKind.Curve && c.GaugeId == inner.GaugeId && c.Radius > inner.Radius && Fits(c, shape)
                             && Math.Abs(c.Radius - inner.Radius - spacing) <= spacing * 0.15)
                 .MinBy(c => Math.Abs(c.Radius - inner.Radius - spacing));

    /// <summary>Lässt sich mit dem Bogen ein voller Kreis (rund) bzw. ein Halbkreis (oval) aus ganzen Stücken legen?</summary>
    public static bool Fits(TrackPieceDef curve, HelixShape shape)
    {
        var part = shape == HelixShape.Oval ? 180 : 360;
        var n = part / curve.Angle;
        return Math.Abs(n - Math.Round(n)) < 0.01 && Math.Round(n) >= 1;
    }

    /// <summary>Länge einer Windung auf dem Gleis mit Radius <paramref name="radius"/>.</summary>
    public static double TurnLength(HelixSpec s, double radius) => 2 * Math.PI * radius + 2 * s.StraightLength;

    /// <summary>Steigung des inneren (steilsten) Gleises in Prozent.</summary>
    public static double GradePercent(HelixSpec s) => s.Rise / TurnLength(s, s.Curve.Radius) * 100;

    /// <summary>Baut die Wendel in den Plan. Liefert die Gleise des inneren und (zweigleisig) des äußeren Gleises.</summary>
    public static (List<PlacedPiece> Inner, List<PlacedPiece> Outer) Build(LayoutPlan plan, HelixSpec spec, ConnectorRef? start, (double X, double Y) center)
    {
        var inner = spec.Curve;
        if (inner.Kind != PieceKind.Curve) throw new InvalidOperationException("Für eine Gleiswendel bitte ein Bogengleis wählen.");
        if (inner.GaugeId != plan.GaugeId) throw new InvalidOperationException($"Dieses Gleis ist für Spur {inner.GaugeId}, der Plan für Spur {plan.GaugeId}.");
        if (!Fits(inner, spec.Shape))
            throw new InvalidOperationException(spec.Shape == HelixShape.Oval
                ? $"Mit {Fmt.Num(inner.Angle)}°-Bögen lässt sich kein Halbkreis legen. Bitte einen Bogen wählen, der 180° glatt teilt."
                : $"Mit {Fmt.Num(inner.Angle)}°-Bögen lässt sich kein voller Kreis legen.");
        if (spec.Tracks == 2 && spec.OuterCurveKey is null && spec.Spacing < plan.Gauge.TrackWidth * PlanCheck.MinSpacingFactor)
            throw new InvalidOperationException($"Der Gleisabstand muss mindestens {Fmt.Num(plan.Gauge.TrackWidth * PlanCheck.MinSpacingFactor, 0)} mm betragen.");

        Pose at;
        double z;
        if (start is not null)
        {
            at = start.Pose;
            z = start.Z;
        }
        else
        {
            // Unterer Halbkreis beginnt unten in der Mitte, Fahrtrichtung nach rechts (bei Linksbögen gegen den Uhrzeigersinn)
            at = new Pose(center.X - spec.StraightLength / 2, center.Y + (spec.RightHand ? -inner.Radius : inner.Radius), 0);
            z = 0;
        }
        var pieces = Track(plan, spec, spec.CurveKey, at, z);
        var outer = new List<PlacedPiece>();
        if (spec.Tracks == 2)
        {
            // Äußeres Gleis parallel daneben, auf der vom Wendelmittelpunkt abgewandten Seite
            var h = at.HeadingRad;
            var d = spec.OuterRadius - inner.Radius;
            var side = spec.RightHand ? -1 : 1;
            var outerStart = new Pose(at.X - Math.Sin(h) * d * side, at.Y + Math.Cos(h) * d * side, at.Heading);
            var outerKey = spec.OuterCurveKey ?? OuterFlexKey(plan, spec);
            outer = Track(plan, spec with { CurveKey = outerKey }, outerKey, outerStart, z);
        }
        return (pieces, outer);
    }

    private static string OuterFlexKey(LayoutPlan plan, HelixSpec spec)
    {
        var inner = spec.Curve;
        var stock = Catalog.FlexOf(plan.GaugeId, Catalog.Flex(plan.Gauge, Catalog.GetSystem(plan.GaugeId, inner.SystemId)).SystemId)
                    ?? Catalog.Flex(plan.Gauge, null);
        return Catalog.FlexCurveKey(plan.GaugeId, inner.Radius + spec.Spacing, inner.Angle, stock.SystemId);
    }

    private static List<PlacedPiece> Track(LayoutPlan plan, HelixSpec spec, string curveKey, Pose at, double z)
    {
        var curve = Catalog.Get(curveKey);
        var half = (int)Math.Round(180 / curve.Angle);
        // Eine Windung: Halbkreis, Geraden, Halbkreis, Geraden (rund: nur die Bögen)
        var turn = new List<string>();
        for (var side = 0; side < 2; side++)
        {
            turn.AddRange(Enumerable.Repeat(curveKey, half));
            if (spec.Straight is not null) turn.AddRange(Enumerable.Repeat(spec.StraightKey!, spec.StraightCount));
        }
        if (spec.Shape == HelixShape.Rund && spec.Straight is null && !Fits(curve, HelixShape.Oval))
        {
            turn.Clear();
            turn.AddRange(Enumerable.Repeat(curveKey, (int)Math.Round(360 / curve.Angle)));
        }
        var turnLength = turn.Sum(k => Catalog.Get(k).TrackLength);
        var grade = spec.Rise / turnLength;
        var total = Math.Max(1, (int)Math.Round(turn.Count * spec.Turns));
        var pieces = new List<PlacedPiece>();
        for (var i = 0; i < total; i++)
        {
            var key = turn[i % turn.Count];
            var def = Catalog.Get(key);
            var p = plan.Add(key);
            p.Flipped = def.Kind == PieceKind.Curve && def.RightHand != spec.RightHand;
            PlanEditing.PlaceAt(p, 0, at.X, at.Y, at.Heading);
            p.Height = z;
            p.Grade = grade;
            z = p.ConnectorHeights()[1];
            at = p.WorldConnectors()[1];
            pieces.Add(p);
        }
        return pieces;
    }

    /// <summary>
    /// Holzliste für den Bau: Trassenteile (Ringsegmente, beim Oval dazu gerade Bretter) aus Sperrholz mit Randaufmaß und Sägezugabe,
    /// Stoßlaschen, Grundplatte, Gewindestangen mit Muttern und Scheiben. Alle Maße in mm.
    /// </summary>
    public static HelixMaterial Material(HelixSpec spec, Gauge gauge)
    {
        var g = gauge.TrackWidth;
        var bed = TrackBedWidth(gauge);
        // Randaufmaß je Seite: Platz für die Bohrung der Gewindestange (12 mm vom Rand) und eine Leitplanke
        var edge = Math.Max(25, Math.Round(g * 1.5));
        var saw = 5.0;
        var thickness = g < 10 ? 8.0 : g < 25 ? 10.0 : 15.0;
        var ri = spec.Curve.Radius - bed / 2 - edge;
        var ro = spec.OuterRadius + bed / 2 + edge;
        var width = ro - ri;
        var straight = spec.StraightLength;

        // Ringsegmente: so viele je Kreis, dass ein Segment höchstens 1200 mm lang ist und auf die Platte passt
        var perCircle = new[] { 4, 6, 8, 12 }.First(n => Chord(ro, 360.0 / n) + saw <= 1200 || n == 12);
        var angle = 360.0 / perCircle;
        var segLength = Math.Ceiling(Chord(ro, angle) + saw);
        var segDepth = Math.Ceiling(ro - ri * Math.Cos(angle / 2 * Math.PI / 180) + saw);
        var segments = (int)Math.Ceiling(spec.Turns * perCircle);
        var boards = spec.Shape == HelixShape.Oval && straight > 0 ? (int)Math.Ceiling(spec.Turns * 2) : 0;
        var boardLength = Math.Ceiling(straight + saw);
        var boardWidth = Math.Ceiling(width + saw);
        var joints = segments + boards;
        var lascheLength = Math.Round(Math.Max(120, g * 8));
        // Lasche so breit wie das Gleisbett plus 10 mm je Seite, damit sie die Bohrungen am Rand frei lässt
        var lascheWidth = Math.Ceiling(width - 2 * edge + 20);

        var parts = new List<HelixPart>
        {
            new($"Trassensegment (Kreisring {Fmt.Num(angle, 0)}°)", segments,
                $"Zuschnitt {Fmt.Num(segLength, 0)} × {Fmt.Num(segDepth, 0)} × {Fmt.Num(thickness, 0)} mm, Ring innen R {Fmt.Num(ri, 0)} / außen R {Fmt.Num(ro, 0)} mm",
                $"Sperrholz {Fmt.Num(thickness, 0)} mm, {perCircle} Segmente je Kreis", segments * segLength * segDepth / 1e6),
        };
        if (boards > 0)
            parts.Add(new("Gerades Trassenbrett", boards, $"{Fmt.Num(boardLength, 0)} × {Fmt.Num(boardWidth, 0)} × {Fmt.Num(thickness, 0)} mm",
                $"je Längsseite eins, Gerade {Fmt.Num(straight, 0)} mm", boards * boardLength * boardWidth / 1e6));
        parts.Add(new("Stoßlasche", joints, $"{Fmt.Num(lascheLength, 0)} × {Fmt.Num(lascheWidth, 0)} × {Fmt.Num(thickness, 0)} mm",
            "unter jeden Stoß, verleimt und verschraubt", joints * lascheLength * lascheWidth / 1e6));

        // Grundplatte mit 50 mm Rand
        double baseW = Math.Ceiling(2 * ro + straight + 100), baseD = Math.Ceiling(2 * ro + 100);
        parts.Add(new("Grundplatte", 1, $"{Fmt.Num(baseW, 0)} × {Fmt.Num(baseD, 0)} × {Fmt.Num(Math.Max(thickness, 12), 0)} mm", "oder Wendel auf die Anlagenplatte stellen", baseW * baseD / 1e6));

        // Stützen: an jedem Segmentstoß innen und außen, beim Oval zusätzlich an den Enden und alle 400 mm der Geraden
        var straightPosts = straight > 0 ? 2 * (1 + (int)Math.Floor(straight / 400)) : 0;
        var posts = perCircle + straightPosts;
        var rods = posts * 2;
        var rodLength = Math.Ceiling((spec.TotalHeight + Math.Max(thickness, 12) + thickness + 60) / 10) * 10;
        var levels = (int)Math.Ceiling(spec.Turns) + 1;
        var rodSize = gauge.TrackWidth >= 30 ? "M10" : "M8";
        parts.Add(new($"Gewindestange {rodSize}", rods, $"{Fmt.Num(rodLength, 0)} mm lang (aus {Math.Max(1, (int)Math.Ceiling(rodLength / 1000))} m-Stangen)",
            $"{posts} Stützpunkte, je einer innen und außen, 12 mm vom Rand"));
        parts.Add(new($"Mutter {rodSize}", rods * levels * 2, "", "je Stütze und Ebene oben und unten, zum Einstellen der Höhe"));
        parts.Add(new($"Unterlegscheibe {rodSize} (groß)", rods * levels * 2, "", "unter jede Mutter"));

        var area = parts.Where(p => p.Name.StartsWith("Trassen") || p.Name.StartsWith("Gerades") || p.Name.StartsWith("Stoß")).Sum(p => p.AreaM2);
        var sheets = Math.Max(1, (int)Math.Ceiling(area * 1.25 / (SheetLength * SheetWidth / 1e6)));
        var clear = spec.Rise - thickness - Math.Round(g * 0.4);
        var notes = new List<string>
        {
            $"Trassenbreite {Fmt.Num(width, 0)} mm = {(spec.Tracks == 2 ? $"Gleisabstand {Fmt.Num(spec.OuterRadius - spec.Curve.Radius, 0)} mm + " : "")}Gleisbreite {Fmt.Num(bed, 0)} mm + je Seite {Fmt.Num(edge, 0)} mm Randaufmaß.",
            $"Sägezugabe {Fmt.Num(saw, 0)} mm in jedem Zuschnittmaß enthalten; Segmente mit Stichsäge auf Radius schneiden (Zirkel/Schnur vom Wendelmittelpunkt).",
            $"Sperrholz {Fmt.Num(thickness, 0)} mm: etwa {Fmt.Num(area, 2)} m² Trassenfläche, mit 25 % Verschnitt {sheets} Platte(n) {Fmt.Num(SheetLength, 0)} × {Fmt.Num(SheetWidth, 0)} mm. Segmente lassen sich ineinander geschachtelt zuschneiden.",
            $"Höhe je Windung {Fmt.Num(spec.Rise, 0)} mm, Gesamthöhe {Fmt.Num(spec.TotalHeight, 0)} mm, Steigung innen {Fmt.Num(GradePercent(spec), 1)} %{(spec.Tracks == 2 ? $", außen {Fmt.Num(spec.Rise / TurnLength(spec, spec.OuterRadius) * 100, 1)} %" : "")}.",
            $"Lichte Höhe unter der Trasse etwa {Fmt.Num(clear, 0)} mm (Höhe je Windung − Brett − Gleis); nötig sind {Fmt.Num(gauge.ClearanceHeight - thickness, 0)} mm für Spur {gauge.Id}.",
            $"Platzbedarf {Fmt.Num(2 * ro + straight, 0)} × {Fmt.Num(2 * ro, 0)} mm (Außenkante Trasse).",
        };
        if (spec.Tracks == 2 && spec.OuterCurveKey is null)
            notes.Add($"Außengleis aus Flexgleis mit Radius {Fmt.Num(spec.OuterRadius, 0)} mm (kein passendes Bogengleis im Katalog).");
        if (clear < gauge.ClearanceHeight - thickness)
            notes.Add("Achtung: Die lichte Höhe ist knapp – Höhe je Windung vergrößern oder dünneres Holz nehmen.");
        return new HelixMaterial(parts, notes, thickness, bed, edge, saw, ri, ro, sheets);
    }

    private static double Chord(double r, double angleDeg) => angleDeg >= 180 ? 2 * r : 2 * r * Math.Sin(angleDeg / 2 * Math.PI / 180);
}

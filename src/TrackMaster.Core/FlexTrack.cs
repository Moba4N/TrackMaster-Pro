using System.Globalization;

namespace TrackMaster.Core;

/// <summary>
/// Flexgleis frei formen wie in WinTrack: von einem Gleisende aus biegen (ein Bogen tangential zum Gleisende durch einen Punkt)
/// oder zwei Gleisenden verbinden (Doppelbogen, der an beiden Enden tangential anschließt; fast gerade Bögen werden zu Geraden).
/// Richtungen sind Fahrtrichtungen: am Anfang vom Gleisende weg, am Ende in das Zielgleis hinein.
/// </summary>
public static class FlexTrack
{
    public const double MinLength = 5;
    /// <summary>Längstes Flexgleis in einem Stück (wie bei geraden Flexgleisen), kleinster Radius.</summary>
    public const double MaxLength = 20000;
    public const double MinRadius = 10;
    /// <summary>Größter Radius, ab dem ein Bogen als Gerade gilt (Abweichung unter 0,1 mm auf 1 m).</summary>
    private const double StraightRadius = 1_000_000;

    /// <summary>Gerade oder ein Bogen, der in <paramref name="start"/> tangential beginnt und im Punkt (x, y) endet.</summary>
    public static IReadOnlyList<Segment> Bend(Pose start, double x, double y)
    {
        double tx = Math.Cos(start.HeadingRad), ty = Math.Sin(start.HeadingRad);
        double cx = x - start.X, cy = y - start.Y;
        var chord = Math.Sqrt(cx * cx + cy * cy);
        if (chord < MinLength) return [];
        var along = tx * cx + ty * cy;
        var cross = tx * cy - ty * cx;
        if (along > 0 && Math.Abs(cross) < 1e-9 * chord) return chord <= MaxLength ? [Segment.Line(chord)] : [];
        // Punkt (fast) genau hinter dem Gleisende: kein sinnvoller Bogen möglich
        if (Math.Abs(cross) < 1e-6 * chord) return [];
        var r = chord * chord / (2 * Math.Abs(cross));
        if (r > StraightRadius && along > 0) return chord <= MaxLength ? [Segment.Line(chord)] : [];
        var sweep = 2 * Math.Atan2(Math.Abs(cross), along) * 180 / Math.PI;
        IReadOnlyList<Segment> arc = [new Segment(0, r, sweep, cross > 0 ? 1 : -1)];
        return IsValid(arc) ? arc : [];
    }

    /// <summary>Gerade vom Punkt (x0, y0) zum Punkt (x1, y1), für ein frei begonnenes Flexgleis.</summary>
    public static (Pose Start, IReadOnlyList<Segment> Segments) Straight(double x0, double y0, double x1, double y1)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var start = new Pose(x0, y0, Math.Atan2(y1 - y0, x1 - x0) * 180 / Math.PI);
        return (start, len < MinLength || len > MaxLength ? [] : [Segment.Line(len)]);
    }

    /// <summary>Verbindet <paramref name="start"/> mit <paramref name="end"/> (Fahrtrichtung beim Ankommen) durch höchstens zwei Bögen.</summary>
    public static IReadOnlyList<Segment> Connect(Pose start, Pose end)
    {
        double t1x = Math.Cos(start.HeadingRad), t1y = Math.Sin(start.HeadingRad);
        double t2x = Math.Cos(end.HeadingRad), t2y = Math.Sin(end.HeadingRad);
        double vx = end.X - start.X, vy = end.Y - start.Y;
        var vv = vx * vx + vy * vy;
        if (vv < MinLength * MinLength) return [];
        var c = t1x * t2x + t1y * t2y;
        var vt = vx * (t1x + t2x) + vy * (t1y + t2y);
        var denom = 2 * (1 - c);
        double d;
        if (Math.Abs(denom) < 1e-9)
        {
            var vt2 = vx * t2x + vy * t2y;
            if (vt2 <= 1e-9) return [];
            d = vv / (4 * vt2);
        }
        else
            d = (-vt + Math.Sqrt(vt * vt + denom * vv)) / denom;
        if (!double.IsFinite(d) || d <= 0) return [];
        // Verbindungspunkt der beiden Bögen
        var mx = (start.X + d * t1x + end.X - d * t2x) / 2;
        var my = (start.Y + d * t1y + end.Y - d * t2y) / 2;
        static double Dist(Pose a, double x, double y) => Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y));
        var first = Bend(start, mx, my);
        // Ein leerer Bogen ist nur in Ordnung, wenn der Punkt praktisch auf dem Anfang liegt
        if (first.Count == 0 && Dist(start, mx, my) >= MinLength) return [];
        var mid = first.Count == 0 ? start with { X = mx, Y = my } : EndPose(start, first);
        var second = Bend(mid, end.X, end.Y);
        if (second.Count == 0 && Dist(mid, end.X, end.Y) >= MinLength) return [];
        if (first.Count == 0 && second.Count == 0) return [];
        var all = Merge([.. first, .. second]);
        return IsValid(all) ? all : [];
    }

    /// <summary>Lässt sich die Form als Flexgleis verlegen (endliche Maße, Radius ab 10 mm, höchstens 20 m lang)?</summary>
    public static bool IsValid(IReadOnlyList<Segment> segments)
        => segments.Count is > 0 and <= 16
           && segments.All(s => s.Turn == 0
               ? double.IsFinite(s.Length) && s.Length > 0
               : double.IsFinite(s.Radius) && s.Radius >= MinRadius && s.Radius <= StraightRadius && s.Angle > 0 && s.Angle < 360)
           && Length(segments) <= MaxLength;

    /// <summary>Endpunkt und Fahrtrichtung nach den Abschnitten.</summary>
    public static Pose EndPose(Pose start, IReadOnlyList<Segment> segments) => Legs(start, segments) is { Count: > 0 } legs ? legs[^1].End : start;

    /// <summary>Abschnitte in Weltkoordinaten ab <paramref name="start"/>, z. B. für die Vorschau beim Ziehen.</summary>
    public static IReadOnlyList<Leg> Legs(Pose start, IReadOnlyList<Segment> segments)
    {
        var legs = new List<Leg>();
        var pose = start;
        double from = 0;
        foreach (var s in segments)
        {
            var leg = s.Turn == 0
                ? new Leg(pose.X, pose.Y, pose.Heading, false, s.Length, From: from)
                : new Leg(pose.X, pose.Y, pose.Heading, true, 0, s.Radius, s.Angle, s.Turn, from);
            legs.Add(leg);
            pose = leg.End;
            from += leg.TrackLength;
        }
        return legs;
    }

    public static double Length(IReadOnlyList<Segment> segments) => segments.Sum(s => s.Turn == 0 ? s.Length : s.Radius * s.Angle * Math.PI / 180);

    /// <summary>Kleinster Radius der Bögen oder null, wenn alles gerade ist.</summary>
    public static double? SmallestRadius(IReadOnlyList<Segment> segments) => segments.Where(s => s.Turn != 0).Select(s => (double?)s.Radius).Min();

    /// <summary>Kurzbeschreibung für die Statuszeile: „Flexgleis 812 mm, Radius 430 mm“.</summary>
    public static string Describe(IReadOnlyList<Segment> segments)
        => $"Flexgleis {Fmt.Num(Length(segments), 0)} mm" + (SmallestRadius(segments) is { } r ? $", kleinster Radius {Fmt.Num(r, 0)} mm" : ", gerade");

    /// <summary>Gerade Stücke zusammenfassen, sehr kurze weglassen.</summary>
    private static IReadOnlyList<Segment> Merge(IReadOnlyList<Segment> segments)
    {
        var list = new List<Segment>();
        foreach (var s in segments)
        {
            if (Length([s]) < 0.05) continue;
            if (s.Turn == 0 && list.Count > 0 && list[^1].Turn == 0) list[^1] = Segment.Line(list[^1].Length + s.Length);
            else list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// Katalogschlüssel für ein geformtes Flexgleis: Gerade „L…“, ein Bogen „R…A…“ (Rechtsbogen gespiegelt),
    /// sonst ein Fahrweg „P…“ aus Geraden „L…“ und Bögen „R…A…“ (negativer Winkel = rechts), getrennt durch „_“.
    /// </summary>
    public static (string Key, bool Flipped) Key(string gaugeId, string flexSystemId, IReadOnlyList<Segment> segments)
    {
        if (segments is [{ Turn: 0 } line]) return (Catalog.FlexStraightKey(gaugeId, line.Length, flexSystemId), false);
        if (segments is [var arc]) return (Catalog.FlexCurveKey(gaugeId, arc.Radius, arc.Angle, flexSystemId), arc.Turn > 0);
        var inv = CultureInfo.InvariantCulture;
        var parts = segments.Select(s => s.Turn == 0
            ? "L" + Math.Round(s.Length, 2).ToString(inv)
            : "R" + Math.Round(s.Radius, 2).ToString(inv) + "A" + Math.Round(s.Turn > 0 ? -s.Angle : s.Angle, 3).ToString(inv));
        return ($"{gaugeId}|{flexSystemId}|P{string.Join("_", parts)}", false);
    }

    /// <summary>Liest den Fahrweg eines Schlüssels „P…“ (ohne das „P“); null bei ungültigen Angaben.</summary>
    public static IReadOnlyList<Segment>? ParsePath(string spec)
    {
        var inv = CultureInfo.InvariantCulture;
        var list = new List<Segment>();
        foreach (var part in spec.Split('_'))
        {
            if (part.StartsWith('L') && double.TryParse(part[1..], NumberStyles.Float, inv, out var len) && len is > 0 and <= 20000)
                list.Add(Segment.Line(len));
            else if (part.StartsWith('R') && part.IndexOf('A') is > 1 and var a
                     && double.TryParse(part[1..a], NumberStyles.Float, inv, out var r) && r is >= 10 and <= StraightRadius
                     && double.TryParse(part[(a + 1)..], NumberStyles.Float, inv, out var ang) && Math.Abs(ang) is > 0 and < 360)
                list.Add(new Segment(0, r, Math.Abs(ang), ang < 0 ? 1 : -1));
            else
                return null;
        }
        return IsValid(list) ? list : null;
    }
}

/// <summary>Ein Flexgleis, das gerade mit Maus oder Finger gezogen wird: Anfang fest, Ende folgt dem Zeiger oder rastet an einem Gleisende ein.</summary>
public sealed class FlexDraft
{
    public FlexDraft(Pose start, ConnectorRef? from)
    {
        Start = start;
        From = from;
    }

    /// <summary>Anfang (Fahrtrichtung). Ohne Gleisende am Anfang dreht er sich mit, bis das Ende eingerastet ist.</summary>
    public Pose Start { get; private set; }
    public ConnectorRef? From { get; }
    public ConnectorRef? Target { get; private set; }
    public IReadOnlyList<Segment> Segments { get; private set; } = [];
    public IReadOnlyList<Leg> Legs => FlexTrack.Legs(Start, Segments);

    public void Update(double x, double y, ConnectorRef? target)
    {
        Target = target;
        if (From is null)
        {
            if (target is not null)
            {
                // Freier Anfang zu einem Gleisende: gerade, bzw. Bogen, der tangential ins Zielgleis läuft (rückwärts gebogen)
                var end = new Pose(target.Pose.X, target.Pose.Y, target.Pose.Heading);
                var back = FlexTrack.Bend(end, Start.X, Start.Y);
                if (back.Count > 0)
                {
                    var b = FlexTrack.EndPose(end, back);
                    Start = new Pose(Start.X, Start.Y, b.Heading + 180);
                    Segments = FlexTrack.Connect(Start, new Pose(end.X, end.Y, end.Heading + 180));
                    return;
                }
            }
            var (start, segments) = FlexTrack.Straight(Start.X, Start.Y, x, y);
            Start = start;
            Segments = segments;
            return;
        }
        Segments = target is not null
            ? FlexTrack.Connect(Start, new Pose(target.Pose.X, target.Pose.Y, target.Pose.Heading + 180))
            : FlexTrack.Bend(Start, x, y);
    }
}

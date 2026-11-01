using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>
/// Gleisplan als DXF (AutoCAD R12, ASCII) im Maßstab 1:1 in Millimetern, für CAD-Programme, CNC-Fräsen und Laser.
/// Ebenen: PLATTE, GLEISACHSE, SCHIENEN, PRELLBOCK, GELAENDE, RAUM, TEXT. Die y-Achse zeigt wie in CAD üblich nach oben.
/// </summary>
public static class DxfExport
{
    private static readonly (string Name, int Color)[] Layers =
    [
        ("PLATTE", 3), ("GLEISACHSE", 1), ("SCHIENEN", 8), ("PRELLBOCK", 1), ("GELAENDE", 94), ("RAUM", 250), ("TEXT", 5),
    ];

    public static string Build(LayoutPlan plan)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void G(int code, string value) => sb.Append(code.ToString(inv)).Append("\r\n").Append(value).Append("\r\n");
        void N(int code, double value) => G(code, Math.Round(value, 4).ToString("0.####", inv));
        var b = plan.Bounds();

        G(0, "SECTION"); G(2, "HEADER");
        G(9, "$ACADVER"); G(1, "AC1009");
        G(9, "$INSUNITS"); G(70, "4");
        G(9, "$EXTMIN"); N(10, b.X0); N(20, -b.Y1);
        G(9, "$EXTMAX"); N(10, b.X1); N(20, -b.Y0);
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LAYER"); G(70, Layers.Length.ToString(inv));
        foreach (var (name, color) in Layers)
        {
            G(0, "LAYER"); G(2, name); G(70, "0"); G(62, color.ToString(inv)); G(6, "CONTINUOUS");
        }
        G(0, "ENDTAB");
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "ENTITIES");
        void Line(string layer, double x0, double y0, double x1, double y1)
        {
            G(0, "LINE"); G(8, layer); N(10, x0); N(20, -y0); N(11, x1); N(21, -y1);
        }
        void Poly(string layer, IReadOnlyList<(double X, double Y)> pts, bool closed)
        {
            if (pts.Count < 2) return;
            G(0, "POLYLINE"); G(8, layer); G(66, "1"); N(10, 0); N(20, 0); G(70, closed ? "1" : "0");
            foreach (var (x, y) in pts) { G(0, "VERTEX"); G(8, layer); N(10, x); N(20, -y); }
            G(0, "SEQEND"); G(8, layer);
        }
        void LegEntity(string layer, Leg l, double offset)
        {
            var (x0, y0, x1, y1, r) = l.Offset(offset);
            if (!l.IsArc || r <= 0.01) { Line(layer, x0, y0, x1, y1); return; }
            var (cx, cy) = l.Center;
            // Winkel in CAD-Koordinaten (y nach oben), Bögen laufen gegen den Uhrzeigersinn
            double A(double x, double y) => (Math.Atan2(-(y - cy), x - cx) * 180 / Math.PI + 360) % 360;
            double a0 = A(x0, y0), a1 = A(x1, y1);
            var ccw = ((a1 - a0) % 360 + 360) % 360;
            var (start, end) = Math.Abs(ccw - l.Angle) < Math.Abs(360 - ccw - l.Angle) ? (a0, a1) : (a1, a0);
            G(0, "ARC"); G(8, layer); N(10, cx); N(20, -cy); N(40, r); N(50, start); N(51, end);
        }
        void Text(string layer, double x, double y, double size, double angle, string text)
        {
            G(0, "TEXT"); G(8, layer); N(10, x); N(20, -y); N(40, size); G(1, text.Replace("\r", " ").Replace("\n", " ")); N(50, -angle);
            G(72, "1"); N(11, x); N(21, -y); G(73, "2");
        }

        foreach (var o in BoardGeometry.Outlines(plan)) Poly("PLATTE", o, true);
        var g = plan.Gauge.TrackWidth;
        foreach (var p in plan.Pieces)
        {
            var legs = p.WorldLegs();
            foreach (var l in legs)
            {
                LegEntity("GLEISACHSE", l, 0);
                LegEntity("SCHIENEN", l, g / 2);
                LegEntity("SCHIENEN", l, -g / 2);
            }
            if (p.Def.Kind == PieceKind.Buffer)
            {
                var e = legs[0].End;
                double nx = -Math.Sin(e.HeadingRad) * g * 1.1, ny = Math.Cos(e.HeadingRad) * g * 1.1;
                Line("PRELLBOCK", e.X - nx, e.Y - ny, e.X + nx, e.Y + ny);
            }
        }
        foreach (var t in plan.Pieces.Where(p => p.Def.IsTable).Select(p => TableShape.Of(p)!))
        {
            if (t.Round) { G(0, "CIRCLE"); G(8, "GLEISACHSE"); N(10, t.Center.X); N(20, -t.Center.Y); N(40, t.PitRadius); }
            else Poly("GLEISACHSE", t.Pit, true);
        }
        foreach (var s in plan.Scenery)
        {
            switch (s.Kind)
            {
                case SceneryKind.Label:
                    Text("TEXT", s.X, s.Y, s.Height, s.Rotation, s.Name);
                    break;
                case SceneryKind.Dimension when s.Points.Count == 2:
                    var d = Annotation.Dimension(s, s.Height);
                    Line("TEXT", d.P1.X, d.P1.Y, d.A.X, d.A.Y);
                    Line("TEXT", d.P2.X, d.P2.Y, d.B.X, d.B.Y);
                    Line("TEXT", d.A.X, d.A.Y, d.B.X, d.B.Y);
                    Text("TEXT", d.TextAt.X, d.TextAt.Y, d.TextSize, d.TextAngle, d.Text);
                    break;
                case SceneryKind.Room:
                    Poly("RAUM", s.Points, true);
                    if (s.Width > 0) Poly("RAUM", Outset(s.Points, s.Width), true);
                    break;
                case SceneryKind.Obstacle:
                    Poly("RAUM", s.Outline(), true);
                    var c = s.Center();
                    Text("RAUM", c.X, c.Y, Math.Max(20, Math.Min(s.Width, s.Depth) / 4), s.Rotation, s.Name);
                    break;
                case SceneryKind.Tree:
                    G(0, "CIRCLE"); G(8, "GELAENDE"); N(10, s.X); N(20, -s.Y); N(40, s.Width / 2);
                    break;
                default:
                    Poly("GELAENDE", s.Outline(), true);
                    break;
            }
        }
        G(0, "ENDSEC");
        G(0, "EOF");
        return sb.ToString();
    }

    /// <summary>Vieleck um <paramref name="distance"/> nach außen versetzt (Außenkante der Wände).</summary>
    public static IReadOnlyList<(double X, double Y)> Outset(IReadOnlyList<(double X, double Y)> pts, double distance)
    {
        var n = pts.Count;
        if (n < 3) return pts;
        // Bei positiver Fläche (im Uhrzeigersinn, y nach unten) liegt außen links der Kantenrichtung
        var sign = Geometry2D.SignedArea(pts) > 0 ? -1 : 1;
        var result = new List<(double X, double Y)>();
        for (var i = 0; i < n; i++)
        {
            var prev = pts[(i + n - 1) % n];
            var cur = pts[i];
            var next = pts[(i + 1) % n];
            (double X, double Y) Normal((double X, double Y) a, (double X, double Y) b)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
                return (-dy / l * sign, dx / l * sign);
            }
            var n1 = Normal(prev, cur);
            var n2 = Normal(cur, next);
            double mx = n1.X + n2.X, my = n1.Y + n2.Y, ml = Math.Sqrt(mx * mx + my * my);
            if (ml < 1e-9) { result.Add((cur.X + n1.X * distance, cur.Y + n1.Y * distance)); continue; }
            mx /= ml; my /= ml;
            var cos = mx * n1.X + my * n1.Y;
            var len = distance / Math.Max(cos, 0.2);
            result.Add((cur.X + mx * len, cur.Y + my * len));
        }
        return result;
    }
}

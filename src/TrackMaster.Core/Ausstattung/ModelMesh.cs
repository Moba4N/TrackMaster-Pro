namespace TrackMaster.Core;

/// <summary>Ein Dreieck in Modellkoordinaten (Meter; x, y im Grundriss, z nach oben).</summary>
public readonly record struct ModelTriangle((double X, double Y, double Z) A, (double X, double Y, double Z) B, (double X, double Y, double Z) C);

/// <summary>
/// Baut die Körper eines Modells als Dreiecke in Modellkoordinaten (Meter) und liefert die Grundrisse für den Gleisplan.
/// Dieselben Daten nutzen 3D-Ansicht, Gleisplan, Modell-Editor und der Export (OBJ/STL).
/// </summary>
public static class ModelMesh
{
    private const int ArcSegments = 16;
    private const int RoundSegments = 24;

    /// <summary>Farbe und Oberfläche eines Körpers; <paramref name="mainColor"/> färbt das Hauptmaterial um.</summary>
    public static (Rgb Color, SurfaceKind Kind) Surface(ModelDefinition def, ModelBody b, Rgb? mainColor)
    {
        var kind = b.Material == ModelMaterial.Glas ? SurfaceKind.Window : SurfaceKind.Matte;
        if (ModelBody.ParseColor(b.Color) is { } own) return (own, kind);
        if (mainColor is { } main && b.Material == def.EffectiveMainMaterial && kind == SurfaceKind.Matte) return (main, kind);
        return (ModelBody.MaterialColor(b.Material), kind);
    }

    /// <summary>Alle Dreiecke des Modells, je Körper mit seiner Farbe.</summary>
    public static IEnumerable<(Rgb Color, SurfaceKind Kind, ModelTriangle Triangle)> Triangles(ModelDefinition def, Rgb? mainColor = null)
    {
        foreach (var b in def.Bodies)
        {
            var (color, kind) = Surface(def, b, mainColor);
            foreach (var t in Body(b)) yield return (color, kind, t);
        }
    }

    /// <summary>Dreiecke eines Körpers in Modellkoordinaten (gedreht und verschoben).</summary>
    public static List<ModelTriangle> Body(ModelBody b)
    {
        var local = new List<ModelTriangle>();
        void T((double, double, double) p, (double, double, double) q, (double, double, double) r) => local.Add(new ModelTriangle(p, q, r));
        void Q((double, double, double) p, (double, double, double) q, (double, double, double) r, (double, double, double) s) { T(p, q, r); T(p, r, s); }
        double hw = Math.Abs(b.W) / 2, hd = Math.Abs(b.D) / 2, h = b.H;
        switch (b.Shape)
        {
            case BodyShape.Quader:
            case BodyShape.Fenster:
            case BodyShape.Tuer:
            case BodyShape.Tor:
                Frustum(local, [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)], b.Shape == BodyShape.Quader ? b.Top : 1, [], h);
                break;
            case BodyShape.Zylinder:
                Frustum(local, Enumerable.Range(0, RoundSegments).Select(i => (hw * Math.Cos(i * 2 * Math.PI / RoundSegments), hd * Math.Sin(i * 2 * Math.PI / RoundSegments))).ToList(), b.Top, [], h);
                break;
            case BodyShape.Vieleck:
                if (b.Points.Count >= 3) Frustum(local, b.Points, b.Top, b.Heights, h);
                break;
            case BodyShape.Keil:
            case BodyShape.Pultdach:
                Prism(local, [(-hd, 0), (hd, 0), (-hd, h)], hw);
                break;
            case BodyShape.Satteldach:
                Prism(local, [(-hd, 0), (hd, 0), (0, h)], hw);
                break;
            case BodyShape.Walmdach:
            {
                (double, double, double) A = (-hw, -hd, 0), B = (hw, -hd, 0), C = (hw, hd, 0), D = (-hw, hd, 0);
                Q(A, B, C, D);
                if (hw >= hd)
                {
                    var r = hw - hd;
                    (double, double, double) r1 = (-r, 0, h), r2 = (r, 0, h);
                    Q(A, B, r2, r1); Q(D, C, r2, r1); T(A, D, r1); T(B, C, r2);
                }
                else
                {
                    var r = hd - hw;
                    (double, double, double) r1 = (0, -r, h), r2 = (0, r, h);
                    Q(A, D, r2, r1); Q(B, C, r2, r1); T(A, B, r1); T(D, C, r2);
                }
                break;
            }
            case BodyShape.Gewoelbe:
                Vault(local, hw, h, Math.Clamp(b.OpeningW / 2, 0, hw * 0.98), Math.Clamp(b.OpeningH, 0, h * 0.98), hd);
                break;
            case BodyShape.Bogenwand:
                ArchWall(local, hw, hd, h, Math.Clamp(b.OpeningW, 0, hw * 2 * 0.96), Math.Clamp(b.OpeningH, 0, h * 0.97));
                break;
            case BodyShape.Treppe:
            {
                var n = Math.Clamp((int)Math.Round(h / 0.17), 2, 60);
                var sd = 2 * hd / n;
                for (var i = 0; i < n; i++)
                    Frustum(local, [(-hw, -hd), (hw, -hd), (hw, hd - i * sd), (-hw, hd - i * sd)], 1, [], h / n, i * h / n);
                break;
            }
            case BodyShape.Freiform:
                foreach (var f in b.Faces)
                {
                    if (f.Length < 3 || f.Any(i => i < 0 || i >= b.Vertices.Count)) continue;
                    for (var i = 1; i + 1 < f.Length; i++) T(b.Vertices[f[0]], b.Vertices[f[i]], b.Vertices[f[i + 1]]);
                }
                break;
        }
        var rot = b.Rotation * Math.PI / 180;
        double c = Math.Cos(rot), s = Math.Sin(rot);
        (double, double, double) M((double X, double Y, double Z) p) => (b.X + p.X * c - p.Y * s, b.Y + p.X * s + p.Y * c, b.Z + p.Z);
        return local.Select(t => new ModelTriangle(M(t.A), M(t.B), M(t.C))).ToList();
    }

    /// <summary>Grundfläche hochgezogen; Oberseite um den Schwerpunkt auf <paramref name="top"/> verkleinert, Höhe je Ecke möglich.</summary>
    private static void Frustum(List<ModelTriangle> o, IReadOnlyList<(double X, double Y)> poly, double top, IReadOnlyList<double> heights, double h, double z0 = 0)
    {
        var n = poly.Count;
        double cx = poly.Average(p => p.X), cy = poly.Average(p => p.Y);
        var t = Math.Clamp(top, 0, 4);
        double Hi(int i) => i < heights.Count && double.IsFinite(heights[i]) ? heights[i] : h;
        var lo = poly.Select(p => (p.X, p.Y, z0)).ToList();
        var hi = poly.Select((p, i) => (cx + (p.X - cx) * t, cy + (p.Y - cy) * t, z0 + Hi(i))).ToList();
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            o.Add(new ModelTriangle(lo[j], lo[i], hi[i]));
            if (t > 1e-6) o.Add(new ModelTriangle(lo[j], hi[i], hi[j]));
        }
        foreach (var (a, b2, c) in Geometry2D.Triangulate(poly))
        {
            o.Add(new ModelTriangle(lo[a], lo[c], lo[b2]));
            if (t > 1e-6) o.Add(new ModelTriangle(hi[a], hi[b2], hi[c]));
        }
    }

    /// <summary>Querschnitt in der y-z-Ebene (y, z), entlang x von −hw bis hw hochgezogen.</summary>
    private static void Prism(List<ModelTriangle> o, IReadOnlyList<(double Y, double Z)> profile, double hw)
    {
        var n = profile.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (y0, z0) = profile[j]; var (y1, z1) = profile[i];
            o.Add(new ModelTriangle((-hw, y0, z0), (hw, y0, z0), (hw, y1, z1)));
            o.Add(new ModelTriangle((-hw, y0, z0), (hw, y1, z1), (-hw, y1, z1)));
        }
        for (var i = 1; i + 1 < n; i++)
        {
            o.Add(new ModelTriangle((-hw, profile[0].Y, profile[0].Z), (-hw, profile[i].Y, profile[i].Z), (-hw, profile[i + 1].Y, profile[i + 1].Z)));
            o.Add(new ModelTriangle((hw, profile[0].Y, profile[0].Z), (hw, profile[i + 1].Y, profile[i + 1].Z), (hw, profile[i].Y, profile[i].Z)));
        }
    }

    /// <summary>Gewölbe entlang y: Halbellipse außen (hw, h), innen (iw, ih); ohne Öffnung voll.</summary>
    private static void Vault(List<ModelTriangle> o, double hw, double h, double iw, double ih, double hd)
    {
        (double X, double Z) Out(int i) { var a = Math.PI * i / ArcSegments; return (hw * Math.Cos(a), h * Math.Sin(a)); }
        (double X, double Z) In(int i) { var a = Math.PI * i / ArcSegments; return (iw * Math.Cos(a), ih * Math.Sin(a)); }
        var open = iw > 1e-6 && ih > 1e-6;
        for (var i = 0; i < ArcSegments; i++)
        {
            var (a, b) = (Out(i), Out(i + 1));
            o.Add(new ModelTriangle((a.X, -hd, a.Z), (b.X, -hd, b.Z), (b.X, hd, b.Z)));
            o.Add(new ModelTriangle((a.X, -hd, a.Z), (b.X, hd, b.Z), (a.X, hd, a.Z)));
            if (open)
            {
                var (c, d) = (In(i), In(i + 1));
                o.Add(new ModelTriangle((c.X, -hd, c.Z), (d.X, hd, d.Z), (d.X, -hd, d.Z)));
                o.Add(new ModelTriangle((c.X, -hd, c.Z), (c.X, hd, c.Z), (d.X, hd, d.Z)));
                foreach (var y in new[] { -hd, hd })
                {
                    o.Add(new ModelTriangle((a.X, y, a.Z), (b.X, y, b.Z), (d.X, y, d.Z)));
                    o.Add(new ModelTriangle((a.X, y, a.Z), (d.X, y, d.Z), (c.X, y, c.Z)));
                }
            }
            else
                foreach (var y in new[] { -hd, hd })
                    o.Add(new ModelTriangle((0, y, 0), (a.X, y, a.Z), (b.X, y, b.Z)));
        }
        if (open)
            foreach (var sgn in new[] { -1, 1 })
            {
                o.Add(new ModelTriangle((sgn * iw, -hd, 0), (sgn * hw, -hd, 0), (sgn * hw, hd, 0)));
                o.Add(new ModelTriangle((sgn * iw, -hd, 0), (sgn * hw, hd, 0), (sgn * iw, hd, 0)));
            }
        else
        {
            o.Add(new ModelTriangle((-hw, -hd, 0), (hw, -hd, 0), (hw, hd, 0)));
            o.Add(new ModelTriangle((-hw, -hd, 0), (hw, hd, 0), (-hw, hd, 0)));
        }
    }

    /// <summary>Wand (x: Breite, y: Stärke) mit Rundbogen-Öffnung unten in der Mitte.</summary>
    private static void ArchWall(List<ModelTriangle> o, double hw, double hd, double h, double ow, double oh)
    {
        if (ow < 1e-6 || oh < 1e-6)
        {
            Frustum(o, [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)], 1, [], h);
            return;
        }
        var r = ow / 2;
        var rz = Math.Min(r, oh);
        var spring = oh - rz;
        // Öffnungskante von links unten über den Bogen nach rechts unten
        var edge = new List<(double X, double Z)> { (-r, 0) };
        for (var i = 0; i <= ArcSegments; i++)
        {
            var a = Math.PI - Math.PI * i / ArcSegments;
            edge.Add((r * Math.Cos(a), spring + rz * Math.Sin(a)));
        }
        edge.Add((r, 0));
        void Face(double y, bool flip)
        {
            void Q((double X, double Z) p, (double X, double Z) q, (double X, double Z) s, (double X, double Z) t)
            {
                var (a, b, c, d) = ((p.X, y, p.Z), (q.X, y, q.Z), (s.X, y, s.Z), (t.X, y, t.Z));
                if (flip) { o.Add(new ModelTriangle(a, c, b)); o.Add(new ModelTriangle(a, d, c)); }
                else { o.Add(new ModelTriangle(a, b, c)); o.Add(new ModelTriangle(a, c, d)); }
            }
            Q((-hw, 0), (-r, 0), (-r, h), (-hw, h));
            Q((r, 0), (hw, 0), (hw, h), (r, h));
            for (var i = 1; i + 2 < edge.Count; i++)
                Q(edge[i], edge[i + 1], (edge[i + 1].X, h), (edge[i].X, h));
        }
        Face(-hd, false);
        Face(hd, true);
        // Laibung der Öffnung
        for (var i = 0; i + 1 < edge.Count; i++)
        {
            var (a, b) = (edge[i], edge[i + 1]);
            o.Add(new ModelTriangle((a.X, -hd, a.Z), (b.X, hd, b.Z), (b.X, -hd, b.Z)));
            o.Add(new ModelTriangle((a.X, -hd, a.Z), (a.X, hd, a.Z), (b.X, hd, b.Z)));
        }
        // Außenseiten, Krone und Fuß
        void Side((double, double, double) a, (double, double, double) b, (double, double, double) c, (double, double, double) d) { o.Add(new ModelTriangle(a, b, c)); o.Add(new ModelTriangle(a, c, d)); }
        Side((-hw, -hd, 0), (-hw, hd, 0), (-hw, hd, h), (-hw, -hd, h));
        Side((hw, -hd, 0), (hw, -hd, h), (hw, hd, h), (hw, hd, 0));
        Side((-hw, -hd, h), (-hw, hd, h), (hw, hd, h), (hw, -hd, h));
        Side((-hw, -hd, 0), (-r, -hd, 0), (-r, hd, 0), (-hw, hd, 0));
        Side((r, -hd, 0), (hw, -hd, 0), (hw, hd, 0), (r, hd, 0));
    }

    /// <summary>Grundriss eines Körpers in Modellkoordinaten (gedreht und verschoben).</summary>
    public static IReadOnlyList<(double X, double Y)> Footprint(ModelBody b)
    {
        double hw = Math.Abs(b.W) / 2, hd = Math.Abs(b.D) / 2;
        IReadOnlyList<(double X, double Y)> local = b.Shape switch
        {
            BodyShape.Zylinder => Enumerable.Range(0, RoundSegments).Select(i => (hw * Math.Cos(i * 2 * Math.PI / RoundSegments), hd * Math.Sin(i * 2 * Math.PI / RoundSegments))).ToList(),
            BodyShape.Vieleck => b.Points.Count >= 3 ? b.Points : [],
            BodyShape.Freiform => Hull(b.Vertices.Select(v => (v.X, v.Y)).ToList()),
            _ => [(-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)],
        };
        var rot = b.Rotation * Math.PI / 180;
        double c = Math.Cos(rot), s = Math.Sin(rot);
        return local.Select(p => (b.X + p.X * c - p.Y * s, b.Y + p.X * s + p.Y * c)).ToList();
    }

    /// <summary>Höchster Punkt eines Körpers.</summary>
    public static double TopOf(ModelBody b) => b.Shape switch
    {
        BodyShape.Vieleck => b.Z + (b.Heights.Count > 0 ? Math.Max(b.Heights.Max(), b.Heights.Count < b.Points.Count ? b.H : double.MinValue) : b.H),
        BodyShape.Freiform => b.Vertices.Count > 0 ? b.Z + b.Vertices.Max(v => v.Z) : b.Z,
        _ => b.Z + b.H,
    };

    /// <summary>Linien auf der Oberseite für den Gleisplan (First, Grate), in Modellkoordinaten.</summary>
    public static IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> RoofLines(ModelBody b)
    {
        double hw = Math.Abs(b.W) / 2, hd = Math.Abs(b.D) / 2;
        var lines = new List<((double, double), (double, double))>();
        switch (b.Shape)
        {
            case BodyShape.Satteldach:
                lines.Add(((-hw, 0), (hw, 0)));
                break;
            case BodyShape.Walmdach when hw >= hd:
                var r = hw - hd;
                lines.AddRange([((-r, 0), (r, 0)), ((-hw, -hd), (-r, 0)), ((-hw, hd), (-r, 0)), ((hw, -hd), (r, 0)), ((hw, hd), (r, 0))]);
                break;
            case BodyShape.Walmdach:
                var q = hd - hw;
                lines.AddRange([((0, -q), (0, q)), ((-hw, -hd), (0, -q)), ((hw, -hd), (0, -q)), ((-hw, hd), (0, q)), ((hw, hd), (0, q))]);
                break;
            case BodyShape.Gewoelbe:
                lines.Add(((0, -hd), (0, hd)));
                break;
            case BodyShape.Treppe:
                var n = Math.Clamp((int)Math.Round(b.H / 0.17), 2, 60);
                for (var i = 1; i < n; i++) lines.Add(((-hw, hd - i * 2 * hd / n), (hw, hd - i * 2 * hd / n)));
                break;
        }
        var rot = b.Rotation * Math.PI / 180;
        double c = Math.Cos(rot), s = Math.Sin(rot);
        (double, double) M((double X, double Y) p) => (b.X + p.X * c - p.Y * s, b.Y + p.X * s + p.Y * c);
        return lines.Select(l => (M(l.Item1), M(l.Item2))).ToList();
    }

    /// <summary>Konvexe Hülle (Monotone Chain).</summary>
    public static List<(double X, double Y)> Hull(List<(double X, double Y)> pts)
    {
        var p = pts.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new List<(double X, double Y)>();
        foreach (var q in p) { while (h.Count >= 2 && Cross(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
        var lower = h.Count + 1;
        for (var i = p.Count - 2; i >= 0; i--) { var q = p[i]; while (h.Count >= lower && Cross(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
        h.RemoveAt(h.Count - 1);
        return h;
    }
}

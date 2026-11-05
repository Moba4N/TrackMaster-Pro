namespace TrackMaster.Core;

/// <summary>Ansicht im Modell-Editor: Draufsicht (x, y), Vorderansicht (x, −z) und Seitenansicht (y, −z).</summary>
public enum EditorView { Draufsicht, Vorderansicht, Seitenansicht }

/// <summary>
/// Bearbeitungslogik des Modell-Editors (Pro), unabhängig von der Oberfläche: Körper anlegen, wählen, verschieben,
/// Maße ändern, Ecken von Vieleck und Freiform bearbeiten, Rückgängig/Wiederholen. Maße in Metern im Vorbild.
/// </summary>
public sealed class ModelEditor
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private const int MaxUndo = 60;

    public ModelDefinition Model { get; private set; }
    /// <summary>Gewählte Körper (Nummern in <see cref="ModelDefinition.Bodies"/>).</summary>
    public SortedSet<int> Selection { get; } = [];
    /// <summary>Gewählte Ecke des gewählten Vielecks bzw. der Freiform (−1 = keine).</summary>
    public int SelectedVertex { get; set; } = -1;
    /// <summary>Raster zum Einrasten in Metern (0 = aus).</summary>
    public double Grid { get; set; } = 0.1;
    /// <summary>Wurde seit dem letzten Speichern etwas geändert?</summary>
    public bool Dirty { get; set; }

    public event Action? Changed;

    public ModelEditor(ModelDefinition model) => Model = model;

    /// <summary>Neues, leeres eigenes Modell.</summary>
    public static ModelDefinition NewModel(string name) => new()
    {
        Id = ModelFile.NewId(name), Name = name, Category = ModelLibrary.OwnCategory,
        Bodies = [new ModelBody { Shape = BodyShape.Quader, W = 8, D = 6, H = 3, Material = ModelMaterial.Putz }],
    };

    /// <summary>Kopie eines Modells (z. B. eines Grundmodells) als neues eigenes Modell.</summary>
    public static ModelDefinition CopyOf(ModelDefinition source)
    {
        var copy = source.Clone();
        copy.Name = source.IsBuiltin ? Lang.F($"{source.Name} (eigen)") : Lang.F($"{source.Name} (Kopie)");
        copy.Id = ModelFile.NewId(copy.Name);
        if (source.IsBuiltin || string.IsNullOrWhiteSpace(copy.Category)) copy.Category = ModelLibrary.OwnCategory;
        return copy;
    }

    public ModelBody? Selected => Selection.Count > 0 && Selection.Min < Model.Bodies.Count ? Model.Bodies[Selection.Min] : null;
    public int SelectedIndex => Selection.Count > 0 ? Selection.Min : -1;

    public double Snap(double v) => Grid > 0 ? Math.Round(v / Grid) * Grid : v;

    // ---------- Rückgängig ----------

    /// <summary>Stand vor einer Änderung merken.</summary>
    public void Record()
    {
        _undo.Push(ModelFile.ToJson(Model));
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep) _undo.Push(k);
        }
        _redo.Clear();
    }

    /// <summary>Nach einer Änderung: geändert markieren und benachrichtigen.</summary>
    public void Commit()
    {
        Dirty = true;
        Selection.RemoveWhere(i => i >= Model.Bodies.Count);
        Changed?.Invoke();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ModelFile.ToJson(Model));
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ModelFile.ToJson(Model));
        Restore(_redo.Pop());
    }

    private void Restore(string json)
    {
        var id = Model.Id;
        Model = ModelFile.FromJson(json);
        Model.Id = id;
        SelectedVertex = -1;
        Commit();
    }

    /// <summary>Änderung als ein Schritt.</summary>
    public void Edit(Action change)
    {
        Record();
        change();
        Commit();
    }

    // ---------- Körper ----------

    /// <summary>Legt einen Körper an: auf das gewählte Gebäude gesetzt (Dach oben drauf, Fenster und Türen an die Vorderseite) oder in die Mitte.</summary>
    public ModelBody Add(BodyShape shape)
    {
        var host = Selected;
        var b = new ModelBody { Shape = shape };
        switch (shape)
        {
            case BodyShape.Quader: b.W = 6; b.D = 4; b.H = 3; b.Material = ModelMaterial.Putz; break;
            case BodyShape.Zylinder: b.W = 3; b.D = 3; b.H = 6; b.Material = ModelMaterial.Beton; break;
            case BodyShape.Keil: b.W = 4; b.D = 3; b.H = 1.5; b.Material = ModelMaterial.Beton; break;
            case BodyShape.Satteldach or BodyShape.Walmdach or BodyShape.Pultdach: b.W = 6.6; b.D = 4.6; b.H = 2.5; b.Material = shape == BodyShape.Pultdach ? ModelMaterial.Blech : ModelMaterial.Dachziegel; break;
            case BodyShape.Gewoelbe: b.W = 6; b.D = 8; b.H = 4; b.OpeningW = 5; b.OpeningH = 3.4; b.Material = ModelMaterial.Naturstein; break;
            case BodyShape.Bogenwand: b.W = 8; b.D = 1; b.H = 7; b.OpeningW = 4.8; b.OpeningH = 6; b.Material = ModelMaterial.Naturstein; break;
            case BodyShape.Fenster: b.W = 1.1; b.D = 0.1; b.H = 1.4; b.Z = 0.9; b.Material = ModelMaterial.Glas; break;
            case BodyShape.Tuer: b.W = 1.1; b.D = 0.1; b.H = 2.2; b.Material = ModelMaterial.Holz; break;
            case BodyShape.Tor: b.W = 3.5; b.D = 0.1; b.H = 3.8; b.Material = ModelMaterial.Holz; break;
            case BodyShape.Treppe: b.W = 1.5; b.D = 2.4; b.H = 1.2; b.Material = ModelMaterial.Beton; break;
            case BodyShape.Vieleck:
                b.Points = [(-3, -2), (3, -2), (3, 2), (0, 3.5), (-3, 2)]; b.H = 3; b.Material = ModelMaterial.Putz; break;
            case BodyShape.Freiform:
                // Pyramide als Ausgangsform: jede Ecke frei verschiebbar
                b.Vertices = [(-2, -2, 0), (2, -2, 0), (2, 2, 0), (-2, 2, 0), (0, 0, 3)];
                b.Faces = [[0, 3, 2, 1], [0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4]];
                b.Material = ModelMaterial.Schiefer; break;
        }
        if (host is not null && !host.IsDetail)
        {
            b.X = host.X; b.Y = host.Y; b.Rotation = host.Rotation;
            if (b.IsRoof)
            {
                b.Z = ModelMesh.TopOf(host);
                if (host.Shape is BodyShape.Quader) { b.W = host.W + 0.6; b.D = host.D + 0.6; }
            }
            else if (b.IsDetail)
            {
                // an die Vorderseite (+y) des gewählten Körpers
                var r = host.Rotation * Math.PI / 180;
                var off = host.D / 2 + b.D / 2;
                b.X = host.X - Math.Sin(r) * off;
                b.Y = host.Y + Math.Cos(r) * off;
                b.Z = host.Z + (shape == BodyShape.Fenster ? Math.Min(0.9, host.H * 0.3) : 0);
            }
            else b.Z = ModelMesh.TopOf(host);
        }
        Edit(() => Model.Bodies.Add(b));
        Selection.Clear();
        Selection.Add(Model.Bodies.Count - 1);
        SelectedVertex = -1;
        Changed?.Invoke();
        return b;
    }

    /// <summary>Freier Grundriss aus angeklickten Punkten (Draufsicht) als Vieleck mit Höhe <paramref name="height"/>.</summary>
    public ModelBody? AddPolygon(IReadOnlyList<(double X, double Y)> points, double height, double z = 0)
    {
        var pts = points.Select(p => (Snap(p.X), Snap(p.Y))).Distinct().ToList();
        if (pts.Count < 3 || Math.Abs(Geometry2D.SignedArea(pts)) < 1e-4) return null;
        double cx = pts.Average(p => p.Item1), cy = pts.Average(p => p.Item2);
        cx = Snap(cx); cy = Snap(cy);
        var b = new ModelBody
        {
            Shape = BodyShape.Vieleck, X = cx, Y = cy, Z = z, H = height, Material = ModelMaterial.Putz,
            Points = pts.Select(p => (p.Item1 - cx, p.Item2 - cy)).ToList(),
        };
        Edit(() => Model.Bodies.Add(b));
        Selection.Clear();
        Selection.Add(Model.Bodies.Count - 1);
        Changed?.Invoke();
        return b;
    }

    public void Delete()
    {
        if (Selection.Count == 0) return;
        var ids = Selection.ToList();
        Edit(() => { foreach (var i in ids.OrderByDescending(i => i)) Model.Bodies.RemoveAt(i); });
        Selection.Clear();
        SelectedVertex = -1;
        Changed?.Invoke();
    }

    public void Duplicate()
    {
        if (Selection.Count == 0) return;
        var copies = Selection.Select(i => Model.Bodies[i].Clone()).ToList();
        Edit(() =>
        {
            foreach (var c in copies) { c.X += 1; c.Y += 1; Model.Bodies.Add(c); }
        });
        Selection.Clear();
        for (var i = Model.Bodies.Count - copies.Count; i < Model.Bodies.Count; i++) Selection.Add(i);
        Changed?.Invoke();
    }

    /// <summary>Spiegelt die gewählten Körper (oder alle) an der Mittelachse x = 0 (<paramref name="acrossX"/>) bzw. y = 0.</summary>
    public void Mirror(bool acrossX)
    {
        var targets = Selection.Count > 0 ? Selection.ToList() : Enumerable.Range(0, Model.Bodies.Count).ToList();
        Edit(() =>
        {
            foreach (var b in targets.Select(i => Model.Bodies[i]))
            {
                if (acrossX) b.X = -b.X; else b.Y = -b.Y;
                if (b.Shape is BodyShape.Vieleck or BodyShape.Freiform) MirrorLocal(b, acrossX);
                // Die Vorderseite (+y in eigenen Achsen) zeigt nach dem Spiegeln in die gespiegelte Richtung
                else b.Rotation = Angles.Normalize(acrossX ? -b.Rotation : 180 - b.Rotation);
            }
        });
    }

    /// <summary>Vieleck/Freiform exakt spiegeln: Ecken in Modellrichtung drehen, spiegeln, Drehung 0, Umlaufsinn umkehren.</summary>
    private static void MirrorLocal(ModelBody b, bool acrossX)
    {
        var r = b.Rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        (double X, double Y) M(double x, double y)
        {
            double wx = x * c - y * s, wy = x * s + y * c;
            return acrossX ? (-wx, wy) : (wx, -wy);
        }
        b.Rotation = 0;
        b.Points = b.Points.Select(p => M(p.X, p.Y)).Reverse().ToList();
        if (b.Heights.Count > 0)
        {
            while (b.Heights.Count < b.Points.Count) b.Heights.Add(b.H);
            b.Heights = b.Heights.Take(b.Points.Count).Reverse().ToList();
        }
        b.Vertices = b.Vertices.Select(v => { var (x, y) = M(v.X, v.Y); return (x, y, v.Z); }).ToList();
        b.Faces = b.Faces.Select(f => f.Reverse().ToArray()).ToList();
    }

    /// <summary>Verschiebt die gewählten Körper (und alle Körper ihrer Gruppen).</summary>
    public void MoveSelection(double dx, double dy, double dz)
    {
        foreach (var b in SelectedWithGroups())
        {
            b.X += dx; b.Y += dy; b.Z = Math.Max(-50, b.Z + dz);
        }
    }

    /// <summary>Gewählte Körper samt den übrigen Körpern ihrer Gruppen.</summary>
    public IReadOnlyList<ModelBody> SelectedWithGroups()
    {
        var groups = Selection.Select(i => Model.Bodies[i].Group).Where(g => g > 0).ToHashSet();
        return Model.Bodies.Where((b, i) => Selection.Contains(i) || (b.Group > 0 && groups.Contains(b.Group))).ToList();
    }

    /// <summary>Fasst die gewählten Körper zu einer Gruppe zusammen (oder löst sie bei <paramref name="ungroup"/>).</summary>
    public void Group(bool ungroup = false)
    {
        if (Selection.Count == 0) return;
        var next = Model.Bodies.Select(b => b.Group).DefaultIfEmpty(0).Max() + 1;
        Edit(() => { foreach (var b in SelectedWithGroups()) b.Group = ungroup ? 0 : next; });
    }

    /// <summary>Wählt den Körper unter dem Punkt (u, v) einer Ansicht; mit <paramref name="add"/> zur Auswahl hinzu.</summary>
    public int? Pick(EditorView view, double u, double v, bool add = false)
    {
        int? hit = null;
        var best = double.MinValue;
        for (var i = 0; i < Model.Bodies.Count; i++)
            foreach (var t in ModelMesh.Body(Model.Bodies[i]))
            {
                var (a, b, c) = (Project(view, t.A), Project(view, t.B), Project(view, t.C));
                // von der Kante gesehene Flächen (ohne Fläche in dieser Ansicht) zählen nicht
                if (Math.Abs((b.U - a.U) * (c.V - a.V) - (c.U - a.U) * (b.V - a.V)) < 1e-9) continue;
                if (!InTriangle((u, v), (a.U, a.V), (b.U, b.V), (c.U, c.V))) continue;
                var depth = (a.Depth + b.Depth + c.Depth) / 3;
                // kleine Teile (Fenster, Türen) vor großen bevorzugen
                if (Model.Bodies[i].IsDetail) depth += 0.05;
                if (depth > best) { best = depth; hit = i; }
            }
        if (!add) Selection.Clear();
        if (hit is { } h)
        {
            if (add && Selection.Contains(h)) Selection.Remove(h);
            else Selection.Add(h);
        }
        SelectedVertex = -1;
        Changed?.Invoke();
        return hit;
    }

    /// <summary>Modellpunkt in einer Ansicht: (u, v) auf dem Bildschirm (v nach unten) und Tiefe zum Betrachter.</summary>
    public static (double U, double V, double Depth) Project(EditorView view, (double X, double Y, double Z) p) => view switch
    {
        EditorView.Draufsicht => (p.X, p.Y, p.Z),
        EditorView.Vorderansicht => (p.X, -p.Z, p.Y),
        _ => (-p.Y, -p.Z, p.X),
    };

    /// <summary>Bildschirm-Verschiebung (du, dv) einer Ansicht als Verschiebung im Modell.</summary>
    public static (double DX, double DY, double DZ) Unproject(EditorView view, double du, double dv) => view switch
    {
        EditorView.Draufsicht => (du, dv, 0),
        EditorView.Vorderansicht => (du, 0, -dv),
        _ => (0, -du, -dv),
    };

    private static bool InTriangle((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        static double D((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3) => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        double d1 = D(p, a, b), d2 = D(p, b, c), d3 = D(p, c, a);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    // ---------- Ecken (Vieleck, Freiform) ----------

    /// <summary>Ecken des gewählten Körpers in Modellkoordinaten (Vieleck: Grundriss oben, Freiform: alle Eckpunkte).</summary>
    public IReadOnlyList<(double X, double Y, double Z)> Vertices(ModelBody b)
    {
        var r = b.Rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        (double, double, double) M(double x, double y, double z) => (b.X + x * c - y * s, b.Y + x * s + y * c, b.Z + z);
        return b.Shape switch
        {
            BodyShape.Vieleck => b.Points.Select((p, i) => M(p.X, p.Y, i < b.Heights.Count ? b.Heights[i] : b.H)).ToList(),
            BodyShape.Freiform => b.Vertices.Select(v => M(v.X, v.Y, v.Z)).ToList(),
            _ => [],
        };
    }

    /// <summary>Nächste Ecke des gewählten Körpers zu (u, v) im Umkreis <paramref name="radius"/>.</summary>
    public int PickVertex(EditorView view, double u, double v, double radius)
    {
        if (Selected is not { Shape: BodyShape.Vieleck or BodyShape.Freiform } b) return -1;
        var best = -1;
        var bestD = radius;
        var verts = Vertices(b);
        for (var i = 0; i < verts.Count; i++)
        {
            var p = Project(view, verts[i]);
            var d = Math.Sqrt((p.U - u) * (p.U - u) + (p.V - v) * (p.V - v));
            if (d <= bestD) { bestD = d; best = i; }
        }
        SelectedVertex = best;
        Changed?.Invoke();
        return best;
    }

    /// <summary>Verschiebt eine Ecke um (dx, dy, dz) in Modellachsen. Beim Vieleck ändert z die Höhe dieser Ecke.</summary>
    public void MoveVertex(int index, double dx, double dy, double dz)
    {
        if (Selected is not { } b) return;
        var r = -b.Rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        double lx = dx * c - dy * s, ly = dx * s + dy * c;
        switch (b.Shape)
        {
            case BodyShape.Vieleck when index >= 0 && index < b.Points.Count:
                b.Points[index] = (b.Points[index].X + lx, b.Points[index].Y + ly);
                if (Math.Abs(dz) > 1e-12)
                {
                    while (b.Heights.Count < b.Points.Count) b.Heights.Add(b.H);
                    b.Heights[index] = Math.Max(0.01, b.Heights[index] + dz);
                }
                break;
            case BodyShape.Freiform when index >= 0 && index < b.Vertices.Count:
                var v = b.Vertices[index];
                b.Vertices[index] = (v.X + lx, v.Y + ly, v.Z + dz);
                break;
        }
    }

    /// <summary>Fügt hinter der gewählten Ecke eine Ecke ein (Mitte der Kante zur nächsten Ecke).</summary>
    public void InsertVertex()
    {
        if (Selected is not { } b) return;
        var i = Math.Max(0, SelectedVertex);
        switch (b.Shape)
        {
            case BodyShape.Vieleck when b.Points.Count >= 2:
                Edit(() =>
                {
                    var j = (i + 1) % b.Points.Count;
                    b.Points.Insert(i + 1, ((b.Points[i].X + b.Points[j].X) / 2, (b.Points[i].Y + b.Points[j].Y) / 2));
                    if (b.Heights.Count > i) b.Heights.Insert(i + 1, (b.Heights[i] + (j < b.Heights.Count ? b.Heights[j] : b.H)) / 2);
                });
                SelectedVertex = i + 1;
                break;
            case BodyShape.Freiform when b.Vertices.Count > 0:
                // neue Ecke über der gewählten; Flächen, die die Kante zur Nachbarecke enthalten, bekommen sie dazu
                Edit(() => SplitFreeformEdge(b, i));
                SelectedVertex = b.Vertices.Count - 1;
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Teilt in der Freiform die erste Kante, die an Ecke <paramref name="i"/> hängt, in der Mitte.</summary>
    private static void SplitFreeformEdge(ModelBody b, int i)
    {
        int other = -1;
        foreach (var f in b.Faces)
        {
            var k = Array.IndexOf(f, i);
            if (k >= 0) { other = f[(k + 1) % f.Length]; break; }
        }
        if (other < 0) return;
        var (p, q) = (b.Vertices[i], b.Vertices[other]);
        b.Vertices.Add(((p.X + q.X) / 2, (p.Y + q.Y) / 2, (p.Z + q.Z) / 2));
        var n = b.Vertices.Count - 1;
        for (var fi = 0; fi < b.Faces.Count; fi++)
        {
            var f = b.Faces[fi].ToList();
            for (var k = 0; k < f.Count; k++)
            {
                var (a, c) = (f[k], f[(k + 1) % f.Count]);
                if ((a == i && c == other) || (a == other && c == i)) { f.Insert(k + 1, n); break; }
            }
            b.Faces[fi] = [.. f];
        }
    }

    /// <summary>Entfernt die gewählte Ecke (Vieleck mindestens drei Ecken, Freiform mindestens vier).</summary>
    public void RemoveVertex()
    {
        if (Selected is not { } b || SelectedVertex < 0) return;
        var i = SelectedVertex;
        switch (b.Shape)
        {
            case BodyShape.Vieleck when b.Points.Count > 3 && i < b.Points.Count:
                Edit(() => { b.Points.RemoveAt(i); if (i < b.Heights.Count) b.Heights.RemoveAt(i); });
                break;
            case BodyShape.Freiform when b.Vertices.Count > 4 && i < b.Vertices.Count:
                Edit(() =>
                {
                    b.Vertices.RemoveAt(i);
                    b.Faces = b.Faces.Select(f => f.Where(k => k != i).Select(k => k > i ? k - 1 : k).ToArray()).Where(f => f.Length >= 3).ToList();
                });
                break;
            default:
                return;
        }
        SelectedVertex = -1;
        Changed?.Invoke();
    }

    /// <summary>Wandelt den gewählten Körper in eine Freiform um (gleiche Gestalt, jede Ecke frei verschiebbar).</summary>
    public void ToFreeform()
    {
        if (Selected is not { } b || b.Shape == BodyShape.Freiform) return;
        var index = SelectedIndex;
        Edit(() => Model.Bodies[index] = Freeform(b));
        SelectedVertex = -1;
        Changed?.Invoke();
    }

    /// <summary>Freiform aus einem Körper: Dreiecke mit gemeinsamen Ecken, Ursprung am Körper.</summary>
    public static ModelBody Freeform(ModelBody b)
    {
        var shell = b.Clone();
        shell.X = 0; shell.Y = 0; shell.Z = 0; shell.Rotation = 0;
        var verts = new List<(double X, double Y, double Z)>();
        var index = new Dictionary<(long, long, long), int>();
        int V((double X, double Y, double Z) p)
        {
            var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
            if (!index.TryGetValue(key, out var i)) { i = verts.Count; verts.Add(p); index[key] = i; }
            return i;
        }
        var faces = new List<int[]>();
        foreach (var t in ModelMesh.Body(shell))
        {
            var f = new[] { V(t.A), V(t.B), V(t.C) };
            if (f.Distinct().Count() == 3) faces.Add(f);
        }
        return new ModelBody
        {
            Shape = BodyShape.Freiform, X = b.X, Y = b.Y, Z = b.Z, Rotation = b.Rotation, Material = b.Material, Color = b.Color, Group = b.Group,
            Vertices = verts, Faces = faces, W = b.W, D = b.D, H = b.H,
        };
    }

    /// <summary>Prüft das Modell vor dem Speichern; leer = in Ordnung.</summary>
    public IReadOnlyList<string> Problems()
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(Model.Name)) list.Add(Lang.T("Das Modell braucht einen Namen."));
        if (Model.Bodies.Count == 0) list.Add(Lang.T("Das Modell hat noch keinen Körper."));
        if (Model.Bodies.Count > 0 && !ModelMesh.Triangles(Model).Any()) list.Add(Lang.T("Das Modell hat keine sichtbaren Flächen."));
        return list;
    }
}

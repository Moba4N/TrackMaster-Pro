namespace TrackMaster.Core;

/// <summary>
/// Rückmelder automatisch verkabeln: Jeder Rückmelder mit Kontakt (<see cref="Detector.Address"/>) bekommt ein Kabel
/// (<see cref="WireUse.Rueckmelder"/>) zur passenden Klemme seines Rückmeldemoduls. Fehlt ein Modul für den Kontakt,
/// wird eines angelegt (schematisch unter dem Plan, je Modul so viele Kontakte wie das Gerät Eingänge hat, meist 16).
/// Die Kabel gehören über <see cref="SceneryItem.Tracks"/> (= Gleis des Rückmelders) zum Rückmelder und folgen ihm:
/// Kontakt ändern, Gleis verschieben oder Rückmelder löschen passt das Kabel bei jeder Änderung an.
/// </summary>
public static class DetectorWiring
{
    /// <summary>Automatisch verlegtes Rückmelder-Kabel (gehört zum Rückmelder auf diesem Gleis).</summary>
    public static bool IsAuto(SceneryItem s) => s.IsWire && WireLayout.Use(s) == WireUse.Rueckmelder && s.Tracks.Count == 1;

    /// <summary>Gerät für neue Rückmeldemodule: wie die schon gesetzten, sonst s88 für Wechsel- bzw. Gleichstrom.</summary>
    public static DecoderModel DefaultModel(LayoutPlan plan)
        => PowerSupply.Modules(plan).Select(Decoders.ModelOf).OfType<DecoderModel>().Where(m => m.Role == ModuleRole.Melden)
               .GroupBy(m => m.Id).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault()
           ?? Decoders.Find(Electrics.Current(plan) == CurrentKind.Wechselstrom ? "60881" : "60882")!;

    /// <summary>Neuen Baustein dieses Typs anlegen (Größe, Name „Typ n“), noch nicht im Plan.</summary>
    public static SceneryItem NewModule(LayoutPlan plan, DecoderModel m, double x, double y, int? address)
    {
        var item = PowerSupply.Create(plan, PowerSourceType.Baustein, x, y);
        item.Model = m.Id;
        (item.Width, item.Depth) = Decoders.Size(plan.Gauge, m);
        item.Address = address;
        var used = plan.Scenery.Select(s => s.Name).ToHashSet();
        for (var n = 1; ; n++)
            if (!used.Contains($"{m.Short} {n}")) { item.Name = $"{m.Short} {n}"; break; }
        return item;
    }

    /// <summary>Ergebnis eines Abgleichs.</summary>
    public readonly record struct Result(int Modules, int Added, int Moved, int Removed)
    {
        public bool Any => Modules + Added + Moved + Removed > 0;
    }

    /// <summary>
    /// Kabel und Module an die Rückmelder anpassen: fehlende Module anlegen, fehlende Kabel verlegen, verschobene Enden
    /// nachführen und Kabel von Rückmeldern ohne Kontakt (oder gelöschten) entfernen. Ohne <see cref="LayoutPlan.DetectorWiring"/> geschieht nichts.
    /// </summary>
    public static Result Sync(LayoutPlan plan)
    {
        if (!plan.DetectorWiring) return default;
        int modules = 0, added = 0, moved = 0, removed = 0;
        var gauge = plan.Gauge.TrackWidth;
        var pieces = plan.PieceIndex();
        var wanted = new Dictionary<int, Detector>();
        foreach (var d in plan.Detectors)
            if (d.Address is > 0 and <= 4096 && pieces.ContainsKey(d.PieceId)) wanted.TryAdd(d.PieceId, d);

        // Kabel ohne Rückmelder (und doppelte) entfernen
        var wires = new Dictionary<int, SceneryItem>();
        removed = plan.Scenery.RemoveAll(s => IsAuto(s) && (!wanted.ContainsKey(s.Tracks[0]) || !wires.TryAdd(s.Tracks[0], s)));
        if (wanted.Count == 0) return new(0, 0, 0, removed);

        // fehlende Module anlegen: Startadresse auf die Modulgröße ausgerichtet (1, 17, 33 … bei 16 Eingängen)
        var model = DefaultModel(plan);
        foreach (var a in wanted.Values.Select(d => d.Address!.Value).Distinct().Order())
        {
            if (Decoders.ModuleFor(plan, a, ModuleRole.Melden) is not null) continue;
            var n = model.Addresses;
            var start = (a - 1) / n * n + 1;
            foreach (var r in PowerSupply.Modules(plan).Where(s => Decoders.ModelOf(s)?.Role == ModuleRole.Melden).Select(Decoders.Range).OfType<(int First, int Last)>())
                if (r.Last < a && r.Last >= start) start = r.Last + 1;
            var (x, y) = ModuleSpot(plan, model);
            var mod = NewModule(plan, model, x, y, start);
            mod.Id = plan.NextId++;
            mod.Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1);
            plan.Scenery.Add(mod);
            modules++;
        }

        var current = Electrics.Current(plan);
        foreach (var (pieceId, d) in wanted)
        {
            var piece = pieces[pieceId];
            if (Decoders.ModuleFor(plan, d.Address!.Value, ModuleRole.Melden) is not { } mod || Decoders.Range(mod) is not { } r) continue;
            var ports = Decoders.Ports(mod);
            var index = d.Address!.Value - r.First;
            if (index < 0 || index >= ports.Count) continue;
            var from = Electrics.DetectorPoint(piece, gauge);
            var to = ports[index];
            if (wires.TryGetValue(pieceId, out var w))
            {
                if (w.Points.Count >= 2 && Near(w.Points[0], from) && Near(w.Points[^1], to)) continue;
                w.Points = Route(from, to);
                w.X = from.X;
                w.Y = from.Y;
                moved++;
                continue;
            }
            var pts = Route(from, to);
            plan.Scenery.Add(new SceneryItem
            {
                Kind = SceneryKind.Wire, Variant = (int)WireUse.Rueckmelder, Id = plan.NextId++,
                Name = d.Name.Length > 0 ? Lang.F($"Kabel {d.Name}") : Lang.F($"Kabel Rückmelder {d.Address}"),
                Level = Math.Clamp(piece.Level, 0, plan.Levels.Count - 1), Tracks = [pieceId],
                Color = WireLayout.DefaultColor(WireUse.Rueckmelder, current), Depth = WireLayout.DefaultSection(WireUse.Rueckmelder, plan.Gauge),
                Points = pts, X = pts[0].X, Y = pts[0].Y,
            });
            added++;
        }
        return new(modules, added, moved, removed);
    }

    /// <summary>Alle automatisch verlegten Rückmelder-Kabel entfernen; liefert die Anzahl.</summary>
    public static int RemoveAll(LayoutPlan plan) => plan.Scenery.RemoveAll(IsAuto);

    /// <summary>Kabelweg: vom Rückmelder senkrecht zur Höhe der Klemme, dann waagerecht hinüber.</summary>
    private static List<(double X, double Y)> Route((double X, double Y) from, (double X, double Y) to)
        => Math.Abs(from.X - to.X) < 0.5 || Math.Abs(from.Y - to.Y) < 0.5 ? [from, to] : [from, (from.X, to.Y), to];

    private static bool Near((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= 0.5 && Math.Abs(a.Y - b.Y) <= 0.5;

    /// <summary>Platz für ein neues Modul: in einer Reihe unter den Gleisen, rechts neben den schon gesetzten Rückmeldemodulen.</summary>
    private static (double X, double Y) ModuleSpot(LayoutPlan plan, DecoderModel m)
    {
        var (w, depth) = Decoders.Size(plan.Gauge, m);
        var existing = PowerSupply.Modules(plan).Where(s => Decoders.ModelOf(s)?.Role == ModuleRole.Melden).ToList();
        if (existing.Count > 0)
        {
            var last = existing.MaxBy(s => s.X + s.Width / 2)!;
            return (Math.Round(last.X + last.Width / 2 + w * 0.3 + w / 2), last.Y);
        }
        double x0, y1;
        if (plan.Pieces.Count > 0)
        {
            var pts = plan.Pieces.SelectMany(p => p.WorldLegs()).SelectMany(l => new[] { l.At(0), l.At(1) }).ToList();
            x0 = pts.Min(q => q.X);
            y1 = pts.Max(q => q.Y);
        }
        else (x0, y1) = (0, plan.BoardHeight);
        return (Math.Round(x0 + w / 2), Math.Round(y1 + plan.Gauge.TrackWidth * 6 + depth / 2));
    }
}

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
    public static bool IsAuto(SceneryItem s) => s.IsWire && WireLayout.Use(s) == WireUse.Rueckmelder && s.Tracks.Count == 1 && s.Tracks[0] != 0;

    /// <summary>Automatisch verlegtes Kabel von Booster bzw. Zentrale (Bahnstrom oder Masse) zum Besetztmelder.</summary>
    public static bool IsAutoFeed(SceneryItem s) => s.IsWire && WireLayout.Use(s) is WireUse.Fahrstrom or WireUse.Rueckleiter && s.Tracks is [0];

    /// <summary>Automatisch angelegter Besetztmelder (Baustein).</summary>
    public static bool IsAutoModule(SceneryItem s) => s.IsPower && s.Tracks is [0];

    /// <summary>Von „Melder verkabeln“ angelegt (Kabel oder Baustein); wird beim Ausschalten wieder entfernt.</summary>
    public static bool IsGenerated(SceneryItem s) => IsAuto(s) || IsAutoFeed(s) || IsAutoModule(s);

    /// <summary>Gerät für neue Rückmeldemodule: der neutrale Besetztmelder (16 Eingänge, ohne Hersteller).</summary>
    public static DecoderModel DefaultModel(LayoutPlan plan) => Decoders.Find("Besetztmelder")!;

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
        if (wanted.Count == 0) return new(0, 0, 0, removed + plan.Scenery.RemoveAll(IsAutoFeed));

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
            mod.Tracks = [0];
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

        // Einspeisung der Besetztmelder: Bahnstrom (B) und Masse (0) von Booster bzw. Zentrale an die Klemmen oben
        var sources = PowerSupply.Sources(plan).Where(s => PowerSupply.Type(s) is PowerSourceType.Zentrale or PowerSourceType.Booster).ToList();
        var want = new List<((double X, double Y) From, (double X, double Y) To, WireUse Use, string Name, int Level)>();
        foreach (var mod in PowerSupply.Modules(plan).ToList())
        {
            if (Decoders.FeedTerminals(mod) is not { } ft || Decoders.Range(mod) is not { } r) continue;
            var names = plan.Detectors.Where(d => d.Address is { } a && a >= r.First && a <= r.Last && wanted.ContainsKey(d.PieceId) && d.Source is { Length: > 0 })
                .Select(d => d.Source!).Distinct();
            foreach (var src in names.Select(n => sources.FirstOrDefault(s => s.Name == n)).OfType<SceneryItem>())
            {
                want.Add((ft.Track, PowerSupply.Terminal(src), WireUse.Fahrstrom, Lang.F($"Bahnstrom {src.Name} → {mod.Name}"), mod.Level));
                if (PowerSupply.GroundTerminal(src) is { } g)
                    want.Add((ft.Ground, g, WireUse.Rueckleiter, Lang.F($"Masse {src.Name} → {mod.Name}"), mod.Level));
            }
        }
        bool Matches(SceneryItem w, ((double X, double Y) From, (double X, double Y) To, WireUse Use, string, int) x)
            => WireLayout.Use(w) == x.Use && w.Points.Count >= 2 && Near(w.Points[0], x.From) && Near(w.Points[^1], x.To);
        var keep = new HashSet<SceneryItem>();
        var missing = new List<((double X, double Y) From, (double X, double Y) To, WireUse Use, string Name, int Level)>();
        foreach (var x in want)
            if (plan.Scenery.FirstOrDefault(w => IsAutoFeed(w) && !keep.Contains(w) && Matches(w, x)) is { } found) keep.Add(found);
            else missing.Add(x);
        removed += plan.Scenery.RemoveAll(w => IsAutoFeed(w) && !keep.Contains(w));
        var lane = plan.Gauge.TrackWidth * 3;
        foreach (var x in missing)
        {
            // erst ein Stück nach oben (Bahnstrom und Masse auf getrennten Höhen), dann hinüber und auf die Klemme der Quelle
            var y = Math.Min(x.From.Y, x.To.Y) - lane * (x.Use == WireUse.Fahrstrom ? 1 : 2);
            List<(double X, double Y)> pts = Math.Abs(x.From.X - x.To.X) < 0.5 ? [x.From, x.To] : [x.From, (x.From.X, y), (x.To.X, y), x.To];
            plan.Scenery.Add(new SceneryItem
            {
                Kind = SceneryKind.Wire, Variant = (int)x.Use, Id = plan.NextId++, Name = x.Name, Level = x.Level, Tracks = [0],
                Color = WireLayout.DefaultColor(x.Use, current), Depth = WireLayout.DefaultSection(x.Use, plan.Gauge),
                Points = pts, X = pts[0].X, Y = pts[0].Y,
            });
            added++;
        }
        return new(modules, added, moved, removed);
    }

    /// <summary>Alle von „Melder verkabeln“ angelegten Kabel und Besetztmelder entfernen; liefert die Zahl der Kabel und der Bausteine.</summary>
    public static (int Wires, int Modules) RemoveAll(LayoutPlan plan)
    {
        var modules = plan.Scenery.RemoveAll(IsAutoModule);
        return (plan.Scenery.RemoveAll(s => IsAuto(s) || IsAutoFeed(s)), modules);
    }

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

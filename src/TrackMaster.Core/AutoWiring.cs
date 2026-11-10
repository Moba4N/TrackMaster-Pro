namespace TrackMaster.Core;

/// <summary>
/// „Automatisch verkabeln“: aus Gleisplan und Trennstellen die ganze Stromversorgung anlegen.
/// <list type="bullet">
/// <item>Stromquellen: kleine Anlage auf einer Ebene → die Zentrale speist alles. Mehrere Ebenen oder viel Gleis → je Ebene
/// (bzw. je angefangene <see cref="BoosterLength"/>) ein Booster; die Zentrale bleibt dann fürs Schalten.</item>
/// <item>Jeder Abschnitt bekommt Bahnstrom und Masse: mit Rückmelder über den Besetztmelder (siehe <see cref="DetectorWiring"/>),
/// sonst direkt mit Kabeln von der Quelle (etwa alle <see cref="Electrics.FeederSpacingH0"/> m eine Einspeisung, bei Gleichstrom polrichtig).</item>
/// <item>Gleichstrom: Kehrschleifen werden erkannt, bei Bedarf an beiden Enden getrennt und über ein Kehrschleifenmodul (KS1, KS2 …) gespeist.</item>
/// </list>
/// Alles, was dabei entsteht, ist markiert (<see cref="SceneryItem.Tracks"/> = [-1]) und wird beim nächsten Lauf ersetzt; Selbstverlegtes bleibt.
/// </summary>
public static class AutoWiring
{
    /// <summary>Markierung für automatisch angelegte Quellen, Kehrschleifenmodule und Kabel.</summary>
    public const int Tag = -1;

    /// <summary>Gleislänge je Booster in H0 (mm); andere Spuren im Verhältnis der Spurweite.</summary>
    public const double BoosterLength = 25000;

    public static bool IsAuto(SceneryItem s) => s.Tracks is [Tag] && (s.IsWire || s.IsPower);

    public readonly record struct Result(int Sections, int Feeds, int Boosters, bool CentralFeeds, int Loops, int Isolations, int Detectors, int Wires,
        string System = "", bool ThreeRail = false, bool CurrentAdjusted = false);

    /// <summary>Verbautes Gleissystem: das mit der größten Gleislänge im Plan (Name, Mittelleiter ja/nein).</summary>
    public static (string Name, bool ThreeRail) DetectSystem(LayoutPlan plan)
    {
        var top = plan.Pieces.GroupBy(p => (p.Def.GaugeId, p.Def.SystemId)).MaxBy(g => g.Sum(p => Math.Max(1, p.Def.TrackLength)));
        if (top is null) return ("", false);
        var sys = Catalog.GetSystem(top.Key.GaugeId, top.Key.SystemId);
        return (sys?.Name ?? top.Key.SystemId, sys?.ThreeRail == true);
    }

    /// <summary>Verkabelt den Plan neu; vorher automatisch Angelegtes wird ersetzt.</summary>
    public static Result Build(LayoutPlan plan)
    {
        if (plan.Pieces.Count == 0) return default;
        Clear(plan);
        // Gleissystem bestimmt die Verkabelung: Mittelleiter → Wechselstrom-Logik, Zweileiter → Gleichstrom-Logik mit Polung und Kehrschleifen
        var (systemName, threeRail) = DetectSystem(plan);
        var adjusted = false;
        if (plan.Current != CurrentKind.Auto && (plan.Current == CurrentKind.Wechselstrom) != threeRail)
        {
            plan.Current = CurrentKind.Auto;
            adjusted = true;
        }
        var current = threeRail ? CurrentKind.Wechselstrom : CurrentKind.Gleichstrom;
        var digital = plan.Operation == PowerMode.Digital;
        var scale = plan.Gauge.TrackWidth / 16.5;

        // 1. Kehrschleifen (Gleichstrom): ohne Trennstellen suchen, dann bei Bedarf trennen
        var isolationsAdded = 0;
        var loopCores = new List<HashSet<int>>();
        if (!threeRail)
        {
            var open = Clone(plan);
            open.Isolations.Clear();
            foreach (var loop in Electrics.ReverseLoops(open))
            {
                var core = loop.PieceIds.Where(id => plan.Find(id) is { } p && p.Def.Kind != PieceKind.Switch && p.Def.Drives == 0).ToHashSet();
                if (core.Count == 0) continue;
                loopCores.Add(core);
                isolationsAdded += IsolateCore(plan, core);
            }
        }

        // 2. Abschnitte und Quellen
        var conn = Connectivity.Analyze(plan);
        var sections = Electrics.Sections(plan, conn);
        // Ebene eines Abschnitts = Ebene mit der größten Gleislänge darin (eine Rampe zählt nicht als eigene Ebene)
        int LevelOf(PowerSection s) => s.PieceIds.Select(plan.Find).OfType<PlacedPiece>().GroupBy(p => p.Level).MaxBy(g => g.Sum(p => p.Def.TrackLength))?.Key ?? 0;
        var levels = sections.Select(LevelOf).Distinct().Order().ToList();
        var length = plan.Pieces.Sum(p => p.Def.TrackLength);
        var sources = new List<SceneryItem>();
        var central = PowerSupply.Sources(plan).FirstOrDefault(s => PowerSupply.Type(s) == (digital ? PowerSourceType.Zentrale : PowerSourceType.Fahrtrafo))
                      ?? AddSource(plan, digital ? PowerSourceType.Zentrale : PowerSourceType.Fahrtrafo);
        var boosterCount = 0;
        var useBoosters = digital && (levels.Count > 1 || length > BoosterLength * scale);
        if (useBoosters)
        {
            boosterCount = Math.Clamp(Math.Max(levels.Count, (int)Math.Ceiling(length / (BoosterLength * scale))), 1, Math.Max(1, sections.Count));
            var existing = PowerSupply.Sources(plan).Where(s => PowerSupply.Type(s) == PowerSourceType.Booster).ToList();
            for (var i = 0; i < boosterCount; i++) sources.Add(i < existing.Count ? existing[i] : AddSource(plan, PowerSourceType.Booster));
        }
        else sources.Add(central);

        // Abschnitt → Quelle: bei Boostern nach Ebene, auf einer Ebene nach Länge reihum
        SceneryItem SourceFor(PowerSection s, int index)
        {
            if (sources.Count == 1) return sources[0];
            return levels.Count > 1 ? sources[Math.Min(levels.IndexOf(LevelOf(s)), sources.Count - 1)] : sources[index % sources.Count];
        }

        // 3. Abschnitte speisen
        var sides = Electrics.RailSides(plan, conn);
        var spacing = Electrics.FeederSpacingH0 * 1000 * scale;
        int feeds = 0, detectors = 0, wires = 0, loops = 0, ks = 0;
        var ordered = sections.OrderByDescending(s => s.LengthMm).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var src = SourceFor(s, i);
            var loop = loopCores.Any(c => s.PieceIds.Any(c.Contains));
            // Abschnitte mit Rückmelder: Strom und Masse über den Besetztmelder
            var dets = plan.Detectors.Where(d => s.PieceIds.Contains(d.PieceId)).ToList();
            if (digital && !loop && dets.Count > 0 && threeRail)
            {
                // Mittelleiter: Melder über eine isolierte Außenschiene (Kontaktgleis) am Rückmeldemodul, der Abschnitt wird normal gespeist
                foreach (var d in dets)
                {
                    d.Source = "";
                    d.Address ??= plan.Detectors.Select(x => x.Address ?? 0).DefaultIfEmpty(0).Max() + 1;
                    detectors++;
                }
            }
            else if (digital && !loop && dets.Count > 0)
            {
                foreach (var d in dets)
                {
                    if (d.Source != "") d.Source = src.Name;
                    d.Address ??= plan.Detectors.Select(x => x.Address ?? 0).DefaultIfEmpty(0).Max() + 1;
                    detectors++;
                }
                if (dets.Any(d => d.Source is { Length: > 0 }))
                {
                    // Bahnstrom kommt über den Besetztmelder (Melderkabel), die Masse direkt von der Quelle ans Gleis
                    if (PowerSupply.GroundTerminal(src) is { } srcGround)
                        foreach (var piece in dets.Select(d => plan.Find(d.PieceId)).OfType<PlacedPiece>().DistinctBy(p => p.Id))
                        {
                            var left = sides.TryGetValue(piece.Id, out var sd) && sd;
                            var ground = new Feeder { Id = plan.NextId++, PieceId = piece.Id, Circuit = src.Name, Ground = true, Reversed = !left };
                            var w = AddWire(plan, srcGround, Electrics.FeederPoint(plan, piece, ground), WireUse.Rueckleiter, Lang.F($"Masse {src.Name} → Gleis Nr. {piece.Id}"), current);
                            ground.WireId = w.Id;
                            plan.Feeders.Add(ground);
                            wires++;
                        }
                    continue;
                }
            }
            var from = src;
            if (loop)
            {
                // Kehrschleifenmodul zwischen Quelle und Schleife
                loops++;
                var model = Decoders.Find("Kehrschleifenmodul")!;
                var (kx, ky) = PowerSupply.DefaultSpot(plan);
                var module = DetectorWiring.NewModule(plan, model, kx, ky, null);
                module.Name = $"KS{++ks}";
                for (var n = ks; plan.Scenery.Any(x => x.Name == module.Name); n++) module.Name = $"KS{n + 1}";
                module.Id = plan.NextId++;
                module.Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1);
                module.Tracks = [Tag];
                plan.Scenery.Add(module);
                var ft = Decoders.FeedTerminals(module)!.Value;
                wires += Wire(plan, PowerSupply.Terminal(src), ft.Track, WireUse.Fahrstrom, Lang.F($"Bahnstrom {src.Name} → {module.Name}"), current);
                if (PowerSupply.GroundTerminal(src) is { } g) wires += Wire(plan, g, ft.Ground, WireUse.Rueckleiter, Lang.F($"Masse {src.Name} → {module.Name}"), current);
                from = module;
            }
            // Einspeisepunkte: gleichmäßig über die geraden und gebogenen Gleise des Abschnitts
            var candidates = s.PieceIds.Select(plan.Find).OfType<PlacedPiece>().Where(p => p.Def.Kind != PieceKind.Switch && p.Def.Drives == 0).ToList();
            if (candidates.Count == 0) candidates = s.PieceIds.Select(plan.Find).OfType<PlacedPiece>().ToList();
            var count = loop ? 1 : Math.Clamp((int)Math.Ceiling(s.LengthMm / spacing), 1, candidates.Count);
            for (var k = 0; k < count; k++)
            {
                var piece = candidates[(int)((k + 0.5) * candidates.Count / count)];
                var left = sides.TryGetValue(piece.Id, out var sd) && sd;
                (double X, double Y) bOut, gOut;
                string circuit;
                if (from.IsPower && Decoders.ModelOf(from) is { } km && km.Id == "Kehrschleifenmodul")
                {
                    var ports = Decoders.Ports(from);
                    (bOut, gOut, circuit) = (ports[0], ports[1], src.Name);
                }
                else
                {
                    bOut = PowerSupply.Terminal(from);
                    gOut = PowerSupply.GroundTerminal(from) ?? bOut;
                    circuit = from.Name;
                }
                var supply = new Feeder { Id = plan.NextId++, PieceId = piece.Id, Circuit = circuit, Reversed = current == CurrentKind.Wechselstrom ? false : left };
                var ground = new Feeder { Id = plan.NextId++, PieceId = piece.Id, Circuit = circuit, Ground = true, Reversed = !left };
                var at = Electrics.FeederPoint(plan, piece, supply);
                var w1 = AddWire(plan, bOut, at, WireUse.Fahrstrom, Lang.F($"Bahnstrom {from.Name} → Gleis Nr. {piece.Id}"), current);
                supply.WireId = w1.Id;
                plan.Feeders.Add(supply);
                feeds++;
                wires++;
                // Masse: an jeder Einspeisung ein eigenes Kabel zum Gleis
                {
                    var w2 = AddWire(plan, gOut, Electrics.FeederPoint(plan, piece, ground), WireUse.Rueckleiter, Lang.F($"Masse {from.Name} → Gleis Nr. {piece.Id}"), current);
                    ground.WireId = w2.Id;
                    plan.Feeders.Add(ground);
                    wires++;
                }
            }
        }

        // Melder: Besetztmelder und ihre Kabel
        if (detectors > 0) plan.DetectorWiring = true;
        return new(sections.Count, feeds, boosterCount, !useBoosters, loops, isolationsAdded, detectors, wires, systemName, threeRail, adjusted);
    }

    /// <summary>Vorher automatisch Angelegtes entfernen: Kabel samt ihren Einspeisungen, Quellen und Kehrschleifenmodule.</summary>
    public static int Clear(LayoutPlan plan)
    {
        var wireIds = plan.Scenery.Where(s => s.IsWire && IsAuto(s)).Select(s => s.Id).ToHashSet();
        plan.Feeders.RemoveAll(f => f.WireId is { } w && wireIds.Contains(w));
        // automatisch angelegte Quellen: Melder, die auf sie zeigen, wählen beim nächsten Lauf neu
        foreach (var src in plan.Scenery.Where(s => s.IsPower && IsAuto(s)))
            foreach (var d in plan.Detectors.Where(d => d.Source == src.Name)) d.Source = null;
        return plan.Scenery.RemoveAll(IsAuto);
    }

    /// <summary>Kehrschleife abtrennen: an jeder Verbindung von der Schleife nach außen eine Trennstelle (falls noch keine).</summary>
    private static int IsolateCore(LayoutPlan plan, HashSet<int> core)
    {
        var conn = Connectivity.Analyze(plan);
        var n = 0;
        foreach (var c in conn.All)
        {
            if (!core.Contains(c.PieceId) || c.Link is not { } l || core.Contains(l.PieceId)) continue;
            if (plan.Isolations.Any(i => (i.PieceId == c.PieceId && i.Index == c.Index) || (i.PieceId == l.PieceId && i.Index == l.Index))) continue;
            plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = c.PieceId, Index = c.Index });
            n++;
        }
        return n;
    }

    private static SceneryItem AddSource(LayoutPlan plan, PowerSourceType type)
    {
        var (x, y) = PowerSupply.DefaultSpot(plan);
        var item = PowerSupply.Create(plan, type, x, y);
        item.Id = plan.NextId++;
        item.Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1);
        item.Tracks = [Tag];
        plan.Scenery.Add(item);
        return item;
    }

    private static int Wire(LayoutPlan plan, (double X, double Y) a, (double X, double Y) b, WireUse use, string name, CurrentKind current)
    {
        AddWire(plan, a, b, use, name, current);
        return 1;
    }

    private static SceneryItem AddWire(LayoutPlan plan, (double X, double Y) a, (double X, double Y) b, WireUse use, string name, CurrentKind current)
    {
        var pts = WireLayout.Orthogonalize([a, b]);
        var w = new SceneryItem
        {
            Kind = SceneryKind.Wire, Variant = (int)use, Id = plan.NextId++, Name = name, Tracks = [Tag],
            Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1),
            Color = WireLayout.DefaultColor(use, current), Depth = WireLayout.DefaultSection(use, plan.Gauge),
            Points = pts, X = pts[0].X, Y = pts[0].Y,
        };
        plan.Scenery.Add(w);
        return w;
    }

    private static LayoutPlan Clone(LayoutPlan plan) => PlanSerializer.FromJson(PlanSerializer.ToJson(plan, withIds: true), out _);
}

namespace TrackMaster.Core;

/// <summary>Ergebnis von <see cref="SwitchInsert.Build"/>: neue Weiche, Ausgleichs-Flexgleis (falls nötig) und die ersetzten Gleise.</summary>
public sealed record SwitchInsertResult(PlacedPiece Switch, PlacedPiece? Flex, IReadOnlyList<int> Removed, string Message);

/// <summary>
/// Weiche in ein liegendes Gleis einsetzen (z. B. in eine Gleiswendel): das gewählte Gleis wird ersetzt, die Weiche liegt mit
/// ihrem Stammgleis an derselben Stelle, Höhe, Steigung, Ebene und Tunnel bleiben. Ist die Weiche kürzer, schließt ein Flexgleis
/// die Lücke zum nächsten Gleis; ist sie länger, werden die folgenden Gleise mit ersetzt.
/// </summary>
public static class SwitchInsert
{
    /// <summary>Gleise, die sich durch eine Weiche ersetzen lassen.</summary>
    public static bool CanReplace(PlacedPiece p) => p.Def.Kind is PieceKind.Straight or PieceKind.Curve && !p.Def.IsTable && p.WorldConnectors().Count == 2;

    /// <summary>Weichen der Spur (für die Auswahl), Systemweichen zuerst.</summary>
    public static IReadOnlyList<TrackPieceDef> Switches(LayoutPlan plan, string? systemId)
        => plan.Gauge.Systems.SelectMany(s => s.Pieces).Where(d => d.Kind == PieceKind.Switch && TrackGeometry.LocalPaths(d, false).Count == 2)
            .OrderBy(d => d.SystemId == systemId ? 0 : 1).ThenBy(d => d.Number, StringComparer.Ordinal).ToList();

    /// <param name="reverse">In Gegenrichtung einsetzen: die Weichenspitze zeigt zum anderen Ende des gewählten Gleises.</param>
    /// <param name="otherSide">Abzweig auf die andere Seite (sonst die Lage, in der das Stammgleis dem bisherigen Gleis am besten folgt).</param>
    public static SwitchInsertResult Build(LayoutPlan plan, PlacedPiece piece, string switchKey, string flexSystemId, bool reverse = false, bool otherSide = false)
    {
        if (!CanReplace(piece)) throw new InvalidOperationException(Lang.T("Bitte ein einzelnes gerades oder gebogenes Gleis wählen, an dessen Stelle die Weiche kommt."));
        var def = Catalog.Get(switchKey);
        if (def.Kind != PieceKind.Switch) throw new InvalidOperationException(Lang.T("Bitte eine Weiche wählen."));
        if (def.GaugeId != plan.GaugeId) throw new InvalidOperationException(Lang.F($"Diese Weiche ist für Spur {def.GaugeId}, der Plan für Spur {plan.GaugeId}."));
        var conn = Connectivity.Analyze(plan);
        var entryIndex = reverse ? 1 : 0;
        var entry = conn.Find(piece.Id, entryIndex)!;
        // Fahrtrichtung in das Gleis hinein, Höhe und Steigung in dieser Richtung
        var start = entry.Pose with { Heading = entry.Pose.Heading + 180 };
        var z0 = entry.Z;
        var grade = reverse ? -piece.EffectiveGrade : piece.EffectiveGrade;

        // Die folgenden Gleise in Fahrtrichtung, solange sie einfach weiterlaufen: genug für Weiche und Ausgleichsstück
        var reach = def.TrackLength * 3 + plan.Gauge.TrackWidth * 10;
        var run = new List<(PlacedPiece Piece, ConnectorRef Far)>();
        var cur = piece;
        var into = entryIndex;
        var guard = new HashSet<int>();
        while (run.Sum(r => r.Piece.Def.TrackLength) < reach && run.Count < 60 && guard.Add(cur.Id))
        {
            var far = conn.Find(cur.Id, 1 - into)!;
            run.Add((cur, far));
            if (far.Link is not { } next || plan.Find(next.PieceId) is not { } np || !CanReplace(np) || np.Id == piece.Id) break;
            cur = np;
            into = next.Index;
        }
        // Zum Vergleich der Weichenlagen nur das Stück, auf dem die Weiche liegt
        var path = new List<PlacedPiece>();
        foreach (var (rp, _) in run) { path.Add(rp); if (path.Sum(x => x.Def.TrackLength) >= def.TrackLength * 1.5) break; }

        // Lagen der Weiche: Spitze am Anfang, Stammgleis möglichst auf dem bisherigen Gleis; der andere Strang ist der Abzweig
        var options = new List<(bool Flip, int Exit, int Side, double Score)>();
        var (hx, hy) = (Math.Cos(start.HeadingRad), Math.Sin(start.HeadingRad));
        foreach (var flip in new[] { false, true })
        {
            var probe = new PlacedPiece { Key = switchKey, Flipped = flip };
            PlanEditing.PlaceAt(probe, 0, start.X, start.Y, start.Heading);
            var cs = probe.WorldConnectors();
            if (cs.Count != 3) continue;
            for (var exit = 1; exit < cs.Count; exit++)
            {
                var e = cs[exit];
                var b = cs[3 - exit];
                var hit = ModelPlacement.Nearest(path, e.X, e.Y, double.MaxValue);
                var score = hit is null ? double.MaxValue : hit.Distance + Turn(hit.Pose.Heading, e.Heading) * 0.5;
                var side = Math.Sign(hx * (b.Y - e.Y) - hy * (b.X - e.X));
                options.Add((flip, exit, side, score));
            }
        }
        var ordered = options.Where(o => o.Score < double.MaxValue).OrderBy(o => o.Score).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException(Lang.T("Bitte eine Weiche wählen."));
        if (otherSide) ordered = ordered.Where(o => o.Side != ordered[0].Side).ToList();
        // Unter den passenden Lagen die, die am wenigsten Gleise ersetzt (bei Gleichstand die, deren Stammgleis dem alten am besten folgt)
        var fits = ordered.Select(o => Fit(plan, conn, piece, switchKey, flexSystemId, start, z0, grade, run, o.Flip, o.Exit)).OfType<Candidate>().ToList();
        if (fits.Count > 0)
        {
            var c = fits.MinBy(f => f.Removed.Count)!;
            return Commit(plan, c.Switch, c.Flex, c.Removed, def, c.OpenEnd);
        }
        throw new InvalidOperationException(Lang.T("Die Weiche passt hier nicht – das Stammgleis lässt sich nicht ohne zu engen Bogen an das folgende Gleis anschließen. In engen Bögen (z. B. in der Gleiswendel) eine Bogenweiche wählen, sonst die andere Seite oder Gegenrichtung versuchen."));
    }

    private sealed record Candidate(PlacedPiece Switch, PlacedPiece? Flex, List<int> Removed, bool OpenEnd);

    private static Candidate? Fit(LayoutPlan plan, Connectivity conn, PlacedPiece piece, string switchKey, string flexSystemId,
        Pose start, double z0, double grade, List<(PlacedPiece Piece, ConnectorRef Far)> run, bool flip, int exit)
    {
        var sw = new PlacedPiece { Key = switchKey, Flipped = flip };
        PlanEditing.PlaceAt(sw, 0, start.X, start.Y, start.Heading);
        sw.Height = z0;
        sw.Grade = grade;
        sw.Level = piece.Level;
        sw.Tunnel = piece.Tunnel;
        var exitPose = sw.WorldConnectors()[exit];
        var exitZ = sw.ConnectorHeights()[exit];
        var switchLength = PlanEditing.DistanceFromOrigin(sw, exit);

        // Anschluss an das nächste verbleibende Gleis: so wenige Gleise wie möglich ersetzen
        var minFlex = Math.Max(plan.Gauge.TrackWidth * 2, 10);
        var minRadius = MinRadius(plan, piece);
        double along = 0;
        for (var n = 0; n < run.Count; n++)
        {
            along += run[n].Piece.Def.TrackLength;
            if (along + 0.5 < switchLength) continue;
            var last = n == run.Count - 1;
            var far = run[n].Far;
            var removed = run.Take(n + 1).Select(r => r.Piece.Id).ToList();
            // Gleis endet hier: Stammgleis bleibt offen
            if (far.Link is not { } l || conn.Find(l.PieceId, l.Index) is not { } target)
                return new(sw, null, removed, true);
            var gap = Math.Sqrt(Math.Pow(far.Pose.X - exitPose.X, 2) + Math.Pow(far.Pose.Y - exitPose.Y, 2));
            if (gap < 0.5 && Turn(far.Pose.Heading, exitPose.Heading) < 1) return new(sw, null, removed, false);
            if (gap < minFlex && !last) continue;
            var start2 = new Pose(exitPose.X, exitPose.Y, exitPose.Heading);
            var segments = FlexTrack.Connect(start2, new Pose(target.Pose.X, target.Pose.Y, target.Pose.Heading + 180));
            var (key, flipped) = FlexTrack.Key(plan.GaugeId, flexSystemId, segments);
            if (segments.Count == 0 || FlexTrack.SmallestRadius(segments) is { } r && r < minRadius || Catalog.Find(key) is not { } fdef) continue;
            var flex = new PlacedPiece { Key = key, Flipped = flipped };
            PlanEditing.PlaceAt(flex, 0, start2.X, start2.Y, start2.Heading);
            flex.Height = exitZ;
            flex.Grade = fdef.TrackLength > 0 ? (target.Z - exitZ) / fdef.TrackLength : 0;
            flex.Level = piece.Level;
            flex.Tunnel = piece.Tunnel;
            return new(sw, flex, removed, false);
        }
        return null;
    }

    /// <summary>Winkel zwischen zwei Richtungen, ohne Rücksicht auf die Fahrtrichtung (0–90°).</summary>
    private static double Turn(double a, double b)
    {
        var d = Math.Abs(Angles.Normalize(a - b));
        return Math.Min(d, 180 - d);
    }

    /// <summary>Kleinster Radius für das Ausgleichsstück: nicht enger als der engste Katalogbogen bzw. das ersetzte Gleis.</summary>
    private static double MinRadius(LayoutPlan plan, PlacedPiece piece)
    {
        var catalog = plan.Gauge.Systems.SelectMany(s => s.Pieces).Where(p => p.Kind == PieceKind.Curve && p.Radius > 0).Select(p => p.Radius).DefaultIfEmpty(100).Min();
        var own = piece.Def.Kind == PieceKind.Curve && piece.Def.Radius > 0 ? piece.Def.Radius : double.MaxValue;
        return Math.Min(catalog, own) * 0.9;
    }

    private static SwitchInsertResult Commit(LayoutPlan plan, PlacedPiece sw, PlacedPiece? flex, List<int> removed, TrackPieceDef def, bool openEnd)
    {
        plan.RemovePieces(removed.ToHashSet());
        sw.Id = plan.NextId++;
        plan.Pieces.Add(sw);
        if (flex is not null) { flex.Id = plan.NextId++; plan.Pieces.Add(flex); }
        var text = Lang.F($"Weiche {def.Number} eingesetzt ({removed.Count} Gleis(e) ersetzt")
            + (flex is not null ? Lang.F($", Lücke mit {Fmt.Num(flex.Def.TrackLength, 0)} mm Flexgleis geschlossen") : "")
            + "). " + (openEnd ? Lang.T("Das Stammgleis endet offen. ") : "")
            + Lang.T("Höhe, Steigung und Ebene sind übernommen.");
        return new(sw, flex, removed, text);
    }
}

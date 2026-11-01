namespace TrackMaster.Core;

/// <summary>Raum, Hintergrundbild, Sperren, Gruppen, Ausrichten, Parallelgleis, Rückmelder, Preise.</summary>
public sealed partial class PlanSession
{
    // ---------- Sperren und Gruppen ----------

    /// <summary>Sperrt die gewählten Gleise oder hebt die Sperre auf, wenn alle schon gesperrt sind.</summary>
    public void ToggleLock()
    {
        var sel = SelectedPieces;
        if (sel.Count == 0) { Say(Lang.T("Erst Gleise wählen, die gesperrt werden sollen.")); return; }
        var unlock = sel.All(p => p.Locked);
        Record();
        foreach (var p in sel) p.Locked = !unlock;
        Commit();
        Say(unlock ? Lang.F($"{sel.Count} Gleis(e) entsperrt.") : Lang.F($"{sel.Count} Gleis(e) gesperrt: Sie lassen sich nicht mehr verschieben, drehen oder löschen."));
    }

    /// <summary>Fasst die gewählten Gleise zu einer Gruppe zusammen; ein Klick wählt dann immer die ganze Gruppe.</summary>
    public void GroupSelection()
    {
        var sel = SelectedPieces;
        if (sel.Count < 2) { Say(Lang.T("Für eine Gruppe mindestens zwei Gleise wählen.")); return; }
        Record();
        var g = Plan.Pieces.Select(p => p.Group).DefaultIfEmpty(0).Max() + 1;
        foreach (var p in sel) p.Group = g;
        Commit();
        Say(Lang.F($"{sel.Count} Gleise gruppiert. Ein Klick wählt jetzt die ganze Gruppe."));
    }

    public void Ungroup()
    {
        var sel = SelectedPieces.Where(p => p.Group > 0).ToList();
        if (sel.Count == 0) { Say(Lang.T("Die Auswahl enthält keine Gruppe.")); return; }
        Record();
        foreach (var p in sel) p.Group = 0;
        Commit();
        Say(Lang.T("Gruppe aufgelöst."));
    }

    /// <summary>Richtet die gewählten Gleisgruppen (zusammenhängende Gleise bleiben zusammen) aneinander aus.</summary>
    public void Align(AlignMode mode)
    {
        var movable = MovablePieces;
        if (LockedNotice(movable)) return;
        if (Arrange.Clusters(Plan, Connections, movable).Count < 2)
        {
            Say(Lang.T("Ausrichten: mindestens zwei getrennte Gleisgruppen wählen (z. B. mit Strg+Klick oder Rahmen)."));
            return;
        }
        Record();
        var n = Arrange.Align(Plan, Connections, movable, mode);
        if (n == 0) { History.DiscardLast(); Say(Lang.T("Die Gruppen sind schon ausgerichtet.")); return; }
        Commit();
        Say(Lang.F($"{n} Gleisgruppe(n) ausgerichtet."));
    }

    // ---------- Parallelgleis und Höhenprofil ----------

    /// <summary>Legt ein Flexgleis im Abstand <paramref name="spacing"/> neben die gewählten Gleise (in Fahrtrichtung links oder rechts).</summary>
    public bool AddParallel(double spacing, bool left)
    {
        var chain = TrackChain.Order(Plan, Connections, Selection);
        var (pieces, error) = ParallelTrack.Build(Plan, chain, spacing, left, CurrentFlex.SystemId);
        if (error is not null) { Say(error); return false; }
        Record();
        Selection.Clear();
        foreach (var p in pieces)
        {
            p.Id = Plan.NextId++;
            Plan.Pieces.Add(p);
            Selection.Add(p.Id);
        }
        Active = null;
        Commit();
        Say(Lang.F($"Parallelgleis im Abstand {Fmt.Num(spacing, 0)} mm angelegt ({pieces.Count} Flexgleis(e), {Fmt.Num(pieces.Sum(p => p.Def.TrackLength), 0)} mm). Falsche Seite? Rückgängig und andere Seite wählen."));
        return true;
    }

    /// <summary>Höhenprofil der gewählten Gleise (oder null mit Hinweis).</summary>
    public HeightProfile? Profile()
    {
        var profile = Selection.Count > 0 ? HeightProfile.Build(Plan, Selection, Connections) : null;
        if (profile is null) Say(Lang.T("Für das Höhenprofil zusammenhängende Gleise wählen, z. B. eine Rampe oder Strecke."));
        return profile;
    }

    // ---------- Raum ----------

    /// <summary>Rechteckiger Raum; (x, y) ist die Innenecke links oben relativ zur Platte.</summary>
    public SceneryItem AddRoom(double x, double y, double width, double depth, double wall)
    {
        var room = AddScenery(RoomPlan.Rectangle(x, y, width, depth, wall));
        Say(Lang.T("Raum angelegt. Türen, Fenster, Säulen und Heizkörper unter Raum › Raumelemente setzen; die Planprüfung meldet Engstellen."));
        return room;
    }

    // ---------- Hintergrundbild ----------

    public void SetBackground(PlanBackground? background)
    {
        Record();
        Plan.Background = background;
        Commit();
        Say(background is null ? Lang.T("Hintergrundbild entfernt.") : Lang.T("Hintergrundbild geladen. Zum Skalieren eine Maßlinie über eine bekannte Strecke im Bild ziehen und „Bild nach Maß skalieren“ wählen."));
    }

    public void EditBackground(Action<PlanBackground> change)
    {
        if (Plan.Background is not { } bg) return;
        Record();
        change(bg);
        Commit();
    }

    /// <summary>
    /// Skaliert das Hintergrundbild so, dass die gewählte Maßlinie (über eine Strecke im Bild gezogen) <paramref name="realLength"/> mm lang ist.
    /// Der erste Punkt der Maßlinie bleibt dabei an seiner Stelle im Bild.
    /// </summary>
    public bool ScaleBackground(double realLength)
    {
        if (Plan.Background is not { } bg) { Say(Lang.T("Es ist kein Hintergrundbild geladen.")); return false; }
        if (SelectedObject is not { } id || Plan.Scenery.FirstOrDefault(s => s.Id == id) is not { Kind: SceneryKind.Dimension, Points.Count: 2 } dim)
        {
            Say(Lang.T("Erst mit dem Werkzeug „Maß“ eine Linie über eine bekannte Strecke im Bild ziehen und sie wählen."));
            return false;
        }
        var (a, b) = (dim.Points[0], dim.Points[1]);
        var measured = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        if (measured < 1 || realLength <= 0) return false;
        var f = realLength / measured;
        Record();
        bg.X = a.X + (bg.X - a.X) * f;
        bg.Y = a.Y + (bg.Y - a.Y) * f;
        bg.Width *= f;
        dim.Points = [a, (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f)];
        Commit();
        Say(Lang.F($"Hintergrundbild skaliert (Faktor {Fmt.Num(f, 3)}): Es ist jetzt {Fmt.Num(bg.Width, 0)} × {Fmt.Num(bg.Height, 0)} mm groß."));
        return true;
    }

    // ---------- Rückmelder ----------

    public DetectorType DetectorType { get; set; } = DetectorType.Strom;

    public void AddDetector(int pieceId)
    {
        Record();
        var next = Plan.Detectors.Select(d => d.Address ?? 0).DefaultIfEmpty(0).Max() + 1;
        var d = new Detector { Id = Plan.NextId++, PieceId = pieceId, Type = DetectorType, Name = $"RM{Plan.Detectors.Count + 1}", Address = next };
        Plan.Detectors.Add(d);
        SelectedObject = d.Id;
        Selection.Clear();
        Commit();
        Say(Lang.F($"Rückmelder {d.Name} an Gleis Nr. {pieceId} (Kontakt {next})."));
    }

    // ---------- Preise und Bestand ----------

    /// <summary>Übernimmt Preise und Bestand aus der Stückliste (null bzw. 0 löscht den Eintrag).</summary>
    public void SetCosts(IEnumerable<(string Key, double? Price, int Stock)> rows)
    {
        Record();
        foreach (var (key, price, stock) in rows)
        {
            if (price is { } p && double.IsFinite(p) && p >= 0) Plan.Prices[key] = Math.Round(p, 2); else Plan.Prices.Remove(key);
            if (stock > 0) Plan.Stock[key] = stock; else Plan.Stock.Remove(key);
        }
        Commit();
        Say(Lang.T("Preise und Bestand übernommen."));
    }

    public int ImportPrices(string csv)
    {
        Record();
        var n = BillOfMaterials.ImportPrices(Plan, csv);
        if (n == 0) { History.DiscardLast(); Say(Lang.T("In der Preisliste wurden keine Artikel dieses Plans gefunden (erwartet: Artikelnummer; Preis).")); return 0; }
        Commit();
        Say(Lang.F($"{n} Preis(e) aus der Preisliste übernommen."));
        return n;
    }
}

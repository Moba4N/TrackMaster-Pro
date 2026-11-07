namespace TrackMaster.Core;

/// <summary>
/// Alle verfügbaren Ausstattungsmodelle: fest eingebaute Grundmodelle, die eigene Bibliothek (Pro) und die in einem Plan
/// mitgespeicherten Modelle. Gesucht wird zuerst im Plan, dann in den Grundmodellen, dann in der eigenen Bibliothek.
/// </summary>
public static class ModelLibrary
{
    private static readonly List<ModelDefinition> _own = [];

    /// <summary>Eigene Modelle (vom Programm aus dem Modellordner geladen).</summary>
    public static IReadOnlyList<ModelDefinition> Own => _own;

    /// <summary>Wird aufgerufen, wenn sich die eigene Bibliothek ändert.</summary>
    public static event Action? Changed;

    public static void SetOwn(IEnumerable<ModelDefinition> models)
    {
        _own.Clear();
        _own.AddRange(models.Where(m => !m.IsBuiltin && !string.IsNullOrWhiteSpace(m.Id)).GroupBy(m => m.Id).Select(g => g.Last()));
        Changed?.Invoke();
    }

    /// <summary>Fügt ein eigenes Modell hinzu oder ersetzt es (gleiche Kennung).</summary>
    public static void PutOwn(ModelDefinition model)
    {
        _own.RemoveAll(m => m.Id == model.Id);
        _own.Add(model);
        Changed?.Invoke();
    }

    public static void RemoveOwn(string id)
    {
        if (_own.RemoveAll(m => m.Id == id) > 0) Changed?.Invoke();
    }

    /// <summary>Grundmodelle und eigene Modelle.</summary>
    public static IEnumerable<ModelDefinition> All => BuiltinModels.All.Concat(_own);

    public static ModelDefinition? Find(string id)
        => BuiltinModels.All.FirstOrDefault(m => m.Id == id) ?? _own.FirstOrDefault(m => m.Id == id);

    /// <summary>Modell eines Objekts im Plan (mitgespeichert im Plan, Grundmodell oder eigene Bibliothek).</summary>
    public static ModelDefinition? Find(LayoutPlan plan, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return plan.Models.TryGetValue(id, out var own) ? own : Find(id);
    }

    public static ModelDefinition? Of(LayoutPlan plan, SceneryItem s) => s.Kind == SceneryKind.Building ? Find(plan, s.Model) : null;

    /// <summary>Kategorien in fester Reihenfolge, eigene zuletzt.</summary>
    public static IReadOnlyList<string> Categories(bool withOwn = true)
    {
        var cats = new List<string> { BuiltinModels.CatTunnel, BuiltinModels.CatStation, BuiltinModels.CatRailway, BuiltinModels.CatHouses };
        if (withOwn)
            foreach (var c in _own.Select(m => string.IsNullOrWhiteSpace(m.Category) ? OwnCategory : m.Category).Distinct().OrderBy(c => c))
                if (!cats.Contains(c)) cats.Add(c);
        return cats;
    }

    public static string OwnCategory => Lang.T("Eigene Modelle");

    /// <summary>Hauptfarben zur Auswahl in den Eigenschaften (null = Farbe des Modells).</summary>
    public static IReadOnlyList<(string Name, string? Hex)> MainColors =>
    [
        (Lang.T("Original"), null), (Lang.T("Weiß"), "#F2F0EA"), (Lang.T("Creme"), "#E8DCC0"), (Lang.T("Gelb"), "#E6C870"),
        (Lang.T("Ocker"), "#C89A50"), (Lang.T("Rosa"), "#E0B0A0"), (Lang.T("Ziegelrot"), "#A8553C"), (Lang.T("Rot"), "#B03A30"),
        (Lang.T("Hellgrau"), "#C4C4C0"), (Lang.T("Grau"), "#8E8E8C"), (Lang.T("Braun"), "#7A5434"), (Lang.T("Grün"), "#5E8048"),
        (Lang.T("Blau"), "#5A7898"),
    ];

    /// <summary>Name einer Hauptfarbe (oder der Farbwert selbst).</summary>
    public static string ColorName(string? hex)
        => MainColors.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)) is { Name: not null } c ? c.Name : hex ?? Lang.T("Original");

    public static IEnumerable<ModelDefinition> InCategory(string category)
        => All.Where(m => (string.IsNullOrWhiteSpace(m.Category) ? OwnCategory : m.Category) == category);

    /// <summary>Millimeter im Modell je Meter im Vorbild (Maßstab der Spur, z. B. 1:87 → 11,49).</summary>
    public static double MmPerMeter(Gauge gauge)
    {
        var s = gauge.Scale.Replace(',', '.');
        var i = s.IndexOf(':');
        if (i >= 0 && double.TryParse(s[(i + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio) && ratio > 0)
            return 1000 / ratio;
        return 1000 / 87.0;
    }

    /// <summary>
    /// Modell zum Zeichnen. Mit <paramref name="trackHeight"/> (3D) richtet sich die Höhe nach den verknüpften Gleisen;
    /// der Gleisplan braucht das nicht.
    /// </summary>
    public static PlacedModel? Placed(LayoutPlan plan, SceneryItem s, bool trackHeight = false)
        => Of(plan, s) is { } def ? new PlacedModel(def, s, MmPerMeter(plan.Gauge), trackHeight ? ModelPlacement.TrackRise(plan, s, def) : null) : null;

    /// <summary>Behält im Plan nur die mitgespeicherten Modelle, die noch verwendet werden.</summary>
    public static void Prune(LayoutPlan plan)
    {
        var used = plan.Scenery.Where(s => s.Kind == SceneryKind.Building && !string.IsNullOrEmpty(s.Model)).Select(s => s.Model!).ToHashSet();
        foreach (var id in plan.Models.Keys.Where(k => !used.Contains(k)).ToList()) plan.Models.Remove(id);
    }
}

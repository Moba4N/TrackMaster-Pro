using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using TrackMaster.Web.Plan;

namespace TrackMaster.Web.Interop;

/// <summary>
/// Einstiegspunkte für trackmaster.js: Zeiger, Tastatur, Bildaufbau und 3D-Takt. Sie laufen ohne den Umweg über Blazor,
/// damit Mausbewegungen nicht jedes Mal die ganze Oberfläche neu aufbauen.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class JsEntry
{
    /// <summary>Die laufende Oberfläche (Hauptkomponente).</summary>
    public static IAppHost? Host { get; set; }

    private static PlanView? View => Host?.View;

    [JSExport]
    public static void Render(string id)
    {
        try { if (id == PlanView.ElementId) View?.Render(); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void Resize(string id, double width, double height)
    {
        if (id == PlanView.ElementId) View?.Resize((float)width, (float)height);
    }

    [JSExport]
    public static void PointerDown(int id, double x, double y, int button, int mods, bool touch, double time)
    {
        try { View?.PointerDown(id, (float)x, (float)y, button, (PlanView.Mods)mods, touch, time); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void PointerMove(int id, double x, double y, int mods, bool touch)
    {
        try { View?.PointerMove(id, (float)x, (float)y, (PlanView.Mods)mods, touch); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void PointerUp(int id, double x, double y, int mods, bool touch, double time)
    {
        try { View?.PointerUp(id, (float)x, (float)y, (PlanView.Mods)mods, touch, time); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void PointerCancel(int id) => View?.PointerCancel(id);

    [JSExport]
    public static void PointerLeave() => View?.PointerLeave();

    [JSExport]
    public static void Wheel(double x, double y, double deltaY, int mods) => View?.Wheel((float)x, (float)y, deltaY, (PlanView.Mods)mods);

    [JSExport]
    public static void DoubleClick(double x, double y)
    {
        try { View?.DoubleClick((float)x, (float)y); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void Drop(string key, double x, double y)
    {
        try { View?.Drop(key, (float)x, (float)y); }
        catch (Exception e) { Host?.Report(e); }
    }

    [JSExport]
    public static void Space(bool down) => View?.SetSpace(down);

    /// <summary>Taste gedrückt (außerhalb von Eingabefeldern); true = erledigt, der Browser soll nichts weiter tun.</summary>
    [JSExport]
    public static bool KeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        try { return Host?.KeyDown(key, ctrl, shift, alt) ?? false; }
        catch (Exception e) { Host?.Report(e); return false; }
    }

    /// <summary>Takt der 3D-Ansicht (Mitfahren, Zug); <paramref name="dt"/> in Sekunden.</summary>
    [JSExport]
    public static void Tick3D(double dt)
    {
        try { Host?.Tick3D(dt); }
        catch (Exception e) { Host?.Report(e); }
    }

    /// <summary>Vor dem Schließen oder Verlassen der Seite: Plan sichern.</summary>
    [JSExport]
    public static void BeforeUnload() => Host?.SaveNow();
}

/// <summary>Was die Einstiegspunkte von der Oberfläche brauchen.</summary>
public interface IAppHost
{
    PlanView View { get; }
    bool KeyDown(string key, bool ctrl, bool shift, bool alt);
    void Tick3D(double dt);
    void SaveNow();
    void Report(Exception e);
}

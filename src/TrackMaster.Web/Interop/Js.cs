using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using TrackMaster.Web.Graphics;

namespace TrackMaster.Web.Interop;

/// <summary>Aufrufe in wwwroot/js/trackmaster.js (Zeichnen, Dateien, Speicher im Browser, 3D).</summary>
[SupportedOSPlatform("browser")]
public static partial class Js
{
    public const string Module = "tm";

    public static Task Load() => JSHost.ImportAsync(Module, "../js/trackmaster.js");

    [JSImport("requestFrame", Module)]
    public static partial void RequestFrame(string id);

    [JSImport("draw", Module)]
    private static partial void Draw(string id, [JSMarshalAs<JSType.MemoryView>] Span<double> data, [JSMarshalAs<JSType.Array<JSType.String>>] string[] strings);

    public static void Draw(string id, Canvas canvas) => Draw(id, CollectionsMarshal.AsSpan(canvas.Data), canvas.Strings.ToArray());

    [JSImport("attachPlan", Module)]
    public static partial void AttachPlan(string id);

    [JSImport("resetCanvas", Module)]
    public static partial void ResetCanvas(string id);

    [JSImport("setFont", Module)]
    public static partial void SetFont(string family);

    [JSImport("setText", Module)]
    public static partial void SetText(string elementId, string text);

    [JSImport("vibrate", Module)]
    public static partial void Vibrate(int ms);

    [JSImport("download", Module)]
    public static partial void Download(string fileName, string mime, string text);

    [JSImport("downloadBytes", Module)]
    public static partial void DownloadBytes(string fileName, string mime, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("storageGet", Module)]
    public static partial string? StorageGet(string key);

    [JSImport("storageSet", Module)]
    public static partial bool StorageSet(string key, string value);

    [JSImport("storageRemove", Module)]
    public static partial void StorageRemove(string key);

    /// <summary>Speichern unter: Ergebnis ist der gewählte Dateiname oder leer, wenn abgebrochen.</summary>
    [JSImport("saveAs", Module)]
    public static partial Task<string> SaveAs(string suggested, string extension, string description, string promptText, string text);

    [JSImport("saveToHandle", Module)]
    public static partial Task<bool> SaveToHandle(string text);

    [JSImport("forgetSaveHandle", Module)]
    public static partial void ForgetSaveHandle();

    /// <summary>Dateiauswahl; Ergebnis „Name\0Inhalt“ oder leer, wenn abgebrochen.</summary>
    [JSImport("pickFile", Module)]
    public static partial Task<string> PickFile(string accept);

    [JSImport("copyText", Module)]
    public static partial Task<bool> CopyText(string text);

    [JSImport("readClipboard", Module)]
    public static partial Task<string> ReadClipboard();

    [JSImport("print", Module)]
    public static partial void Print(string html);

    [JSImport("focusElement", Module)]
    public static partial void Focus(string elementId);

    [JSImport("reload", Module)]
    public static partial void Reload();

    [JSImport("browserLanguage", Module)]
    public static partial string BrowserLanguage();

    [JSImport("homeUrl", Module)]
    public static partial string HomeUrl();

    [JSImport("activationUrl", Module)]
    public static partial string ActivationUrl();

    [JSImport("browserName", Module)]
    public static partial string BrowserName();

    [JSImport("setImage", Module)]
    public static partial void SetImage(string key, string dataUrl, string canvasId);

    /// <summary>Bild wählen (bei Bedarf verkleinert); Ergebnis „Name\0mime\0Breite\0Höhe\0Base64“ oder leer.</summary>
    [JSImport("pickImage", Module)]
    public static partial Task<string> PickImage(int maxPx);

    [JSImport("downloadPng", Module)]
    public static partial Task<bool> DownloadPng(string fileName, string svg, int width, int height);

    // ---------- 3D ----------

    [JSImport("gl.open", Module)]
    public static partial bool GlOpen(string canvasId);

    [JSImport("gl.close", Module)]
    public static partial void GlClose();

    [JSImport("gl.setMeshes", Module)]
    private static partial void GlSetMeshes([JSMarshalAs<JSType.MemoryView>] Span<double> data, [JSMarshalAs<JSType.MemoryView>] Span<double> meta, bool dynamic);

    public static void GlSetMeshes(List<double> data, List<double> meta, bool dynamic)
        => GlSetMeshes(CollectionsMarshal.AsSpan(data), CollectionsMarshal.AsSpan(meta), dynamic);

    [JSImport("gl.setLight", Module)]
    private static partial void GlSetLight([JSMarshalAs<JSType.MemoryView>] Span<double> values);

    public static void GlSetLight(double[] values) => GlSetLight(values.AsSpan());

    [JSImport("gl.setCamera", Module)]
    private static partial void GlSetCamera([JSMarshalAs<JSType.MemoryView>] Span<double> values);

    public static void GlSetCamera(double[] values) => GlSetCamera(values.AsSpan());

    [JSImport("gl.turn", Module)]
    public static partial void GlTurn(int axis, double degrees);

    [JSImport("gl.render", Module)]
    public static partial void GlRender();

    [JSImport("gl.animate", Module)]
    public static partial void GlAnimate(bool on);
}

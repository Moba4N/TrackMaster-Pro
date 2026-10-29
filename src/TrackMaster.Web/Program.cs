using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TrackMaster.Core;
using TrackMaster.Web;
using TrackMaster.Web.Interop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await Js.Load();

// Sprache: gespeicherte Wahl, sonst die des Browsers
var saved = Js.StorageGet(Settings.LanguageKey);
Lang.Current = Enum.TryParse<Language>(saved, out var language) ? language
    : Js.BrowserLanguage().StartsWith("de", StringComparison.OrdinalIgnoreCase) ? Language.Deutsch : Language.English;
var culture = Lang.IsEnglish ? Fmt.En : Fmt.De;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentCulture = culture;
CultureInfo.CurrentUICulture = culture;

await builder.Build().RunAsync();

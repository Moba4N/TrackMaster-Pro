using Android.App;
using Android.Runtime;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

/// <summary>Startet vor jeder Activity: Der Gleiskatalog liest seine Datendatei (GPL, eigene Datei) aus den App-Assets.</summary>
[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : Application(handle, ownership)
{
    public override void OnCreate()
    {
        base.OnCreate();
        Catalog.OpenData = () => Assets!.Open("gleisdaten/gleisdaten.json");
    }
}

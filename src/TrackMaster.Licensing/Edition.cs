using TrackMaster.Core;

namespace TrackMaster.Licensing;

/// <summary>Funktionspakete. Jede gültige Lizenz ist eine Pro-Lizenz.</summary>
public static class Features
{
    public const string Basis = "basis";
    public const string Pro = "pro";

    public static string DisplayName(string feature) => feature switch
    {
        Basis => Lang.T("Basis"),
        Pro => Lang.T("Pro"),
        _ => feature,
    };
}

/// <summary>Was Basis und Pro enthalten – für Lizenzdialog, Lizenzverwaltung und README.</summary>
public static class EditionInfo
{
    public static readonly string[] Basis =
    [
        Lang.T("Gleisplanung in allen Spuren (Z, N, TT, H0, 0, G) mit allen Gleissystemen"),
        Lang.T("Flexgleis, Weichen, Kreuzungen, Prellböcke, Bearbeiten und Rückgängig"),
        Lang.T("Höhenplanung: Steigung, Gleiswendel rund und oval, ein- und zweigleisig"),
        Lang.T("Gelände, Gewässer, Gebäude, Vegetation und Tunnel"),
        Lang.T("Elektrik: Einspeisungen, Trennstellen, Signale, Decoder-Adressen, Kabelplan"),
        Lang.T("3D-Ansicht mit Mitfahren und Tageszeit, Plan- und Elektrikprüfung"),
        Lang.T("Gleispläne öffnen und Pläne mit bis zu 50 Gleisen speichern"),
    ];

    public static readonly string[] Pro =
    [
        Lang.T("Alles aus Basis"),
        Lang.T("Speichern von Plänen jeder Größe"),
        Lang.T("Stückliste der Gleise, auch als CSV"),
        Lang.T("Holzliste der Gleiswendel mit allen Zuschnitten, Zuschnitt als SVG 1:1"),
        Lang.T("Spantenbau der Anlage mit Aufmaßen, auch für mehrere Ebenen, Schablonen als SVG"),
        Lang.T("Drucken des Gleisplans"),
        Lang.T("Export: Gleisplan als SVG im Maßstab 1:1, Kabelplan als Text"),
    ];
}

/// <summary>Ordner für Lizenz und Lizenzverwaltung.</summary>
public static class AppFolders
{
    public const string Name = "TrackMaster Pro";
    public const string LicenseFileName = "trackmaster.lic";

    /// <summary>%APPDATA%\TrackMaster Pro</summary>
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Dokumente\TrackMaster Pro Lizenzen (Lizenzverwaltung)</summary>
    public static string LicensesInDocuments => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Name + " Lizenzen");
}

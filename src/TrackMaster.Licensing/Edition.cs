namespace TrackMaster.Licensing;

/// <summary>Funktionspakete. Jede gültige Lizenz ist eine Pro-Lizenz.</summary>
public static class Features
{
    public const string Basis = "basis";
    public const string Pro = "pro";

    public static string DisplayName(string feature) => feature switch
    {
        Basis => "Basis",
        Pro => "Pro",
        _ => feature,
    };
}

/// <summary>Was Basis und Pro enthalten – für Lizenzdialog, Lizenzverwaltung und README.</summary>
public static class EditionInfo
{
    public static readonly string[] Basis =
    [
        "Gleisplanung in allen Spuren (Z, N, TT, H0, 0, G) mit allen Gleissystemen",
        "Flexgleis, Weichen, Kreuzungen, Prellböcke, Bearbeiten und Rückgängig",
        "Höhenplanung: Steigung, Gleiswendel rund und oval, ein- und zweigleisig",
        "Gelände, Gewässer, Gebäude, Vegetation und Tunnel",
        "Elektrik: Einspeisungen, Trennstellen, Signale, Decoder-Adressen, Kabelplan",
        "3D-Ansicht mit Mitfahren und Tageszeit, Plan- und Elektrikprüfung",
        "Gleispläne öffnen und Pläne mit bis zu 50 Gleisen speichern",
    ];

    public static readonly string[] Pro =
    [
        "Alles aus Basis",
        "Speichern von Plänen jeder Größe",
        "Stückliste der Gleise, auch als CSV",
        "Holzliste der Gleiswendel mit allen Zuschnitten",
        "Drucken des Gleisplans",
        "Export: Gleisplan als SVG im Maßstab 1:1, Kabelplan als Text",
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

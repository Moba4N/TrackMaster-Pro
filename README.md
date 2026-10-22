# TrackMaster Pro

Windows-Programm zum Planen von Modelleisenbahn-Anlagen in allen gängigen Spuren (Z, N, TT, H0, 0, G).
Oberfläche im Stil von Windows 11 mit Ribbon-Leiste, Gleisbibliothek, Zeichenfläche, Eigenschaften und Statusleiste.

Technik: C# / .NET 8 / WPF (`System.Windows.Controls.Ribbon`), aufgebaut wie GleisMatrix Pro (Moba4N).

## Starten

Das Programm braucht keine Installation.

1. Unter **Actions** den letzten erfolgreichen Lauf von „Build“ öffnen und das Artefakt **TrackMasterPro-win-x64** herunterladen.
2. ZIP entpacken und `TrackMasterPro.exe` starten.

Voraussetzung ist die **.NET 8 Desktop Runtime (x64)**. Fehlt sie, bietet Windows beim ersten Start den Download an
(oder direkt bei Microsoft: „.NET 8.0 Desktop Runtime“).

Eine Plandatei lässt sich direkt öffnen: `TrackMasterPro.exe MeineAnlage.trackmaster.json`.

## Was die erste Version kann

- Spur wählen (Z, N, TT, H0, 0, G) und das Gleissystem des Herstellers. Märklin C-Gleis (H0) ist mit Katalogmaßen geprüft.
  Die anderen Systeme haben vorläufige Maße und sind in der Bibliothek entsprechend markiert.
- Gleise per Klick am aktiven (orangefarbenen) Gleisende ansetzen oder per Ziehen frei platzieren.
  Gleise rasten an freien Enden ein, auch als ganze Gruppe.
- Drehen, Spiegeln, Anschluss wechseln, Duplizieren, Löschen, Verschieben mit Pfeiltasten, Rückgängig und Wiederholen.
- Eigenschaften: Position, Drehung und Höhe je Gleis, Größe der Anlagenplatte, Übersicht mit Gleislänge,
  kleinstem Radius und offenen Gleisenden.
- Stückliste mit CSV-Export (für Excel) und SVG-Export der Anlage im Maßstab 1:1.
- Speichern und Öffnen als `*.trackmaster.json`. Das Format ist dasselbe wie in der Web-Version, Pläne lassen sich austauschen.
- Beispielanlage „Bahnhof Neustadt“ beim Start.

Die Register Gelände, Elektrik und 3D sind als Ausblick angelegt und noch ohne Funktion.

## Aufbau

```
src/
  TrackMaster.Core    Gleiskatalog, Geometrie (Geraden und Bögen), Plan, Verbindungen und Einrasten,
                      Rückgängig/Wiederholen, Dateiformat, Stückliste, SVG-Export
  TrackMaster.App     Hauptfenster (TrackMasterPro.exe): Ribbon, Gleisbibliothek, Zeichenfläche, Eigenschaften
tests/
  TrackMaster.Tests   Tests für Geometrie, Einrasten, Dateiformat und Stückliste
```

## Selbst bauen

Unter Windows mit dem .NET 8 SDK:

```
dotnet build TrackMasterPro.sln -c Release
dotnet test tests/TrackMaster.Tests
dotnet publish src/TrackMaster.App -c Release -r win-x64 --self-contained false -o artifacts/TrackMasterPro
```

GitHub Actions baut bei jedem Push auf `windows-latest`, führt die Tests aus, veröffentlicht das Programm als
Artefakt und erzeugt zur Kontrolle Bildschirmfotos (`TrackMasterPro.exe --screenshot datei.png [--tab Gleise] [--select]`).

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

## Android (APK)

Es gibt eine Android-Version als einzelne APK-Datei zum direkten Installieren, ohne Play Store (ab Android 8.0).

1. Unter **Actions** den letzten erfolgreichen Lauf von „Build“ öffnen und das Artefakt **TrackMasterPro-Android** herunterladen.
2. ZIP entpacken und `TrackMasterPro.apk` aufs Gerät kopieren (USB, Cloud oder E-Mail).
3. Die Datei auf dem Gerät antippen. Beim ersten Mal fragt Android, ob die App (z. B. „Dateien“ oder der Browser)
   **unbekannte Apps installieren** darf; das einmal erlauben und die Installation bestätigen.

Die App kann dasselbe wie das Windows-Programm: Menüband mit Start, Gleise, Gelände, Elektrik, Ansicht, 3D und Datei,
Gleisbibliothek als Leiste unten, Eigenschaften, Planprüfung, Stückliste, 3D-Ansicht mit Mitfahren und Tageszeit.
Pläne (`.trackmaster.json`) sind zwischen Windows und Android austauschbar. Der aktuelle Plan wird automatisch gesichert.

Bedienung:

| Geste | Wirkung |
|---|---|
| Teil in der Bibliothek antippen | am aktiven Gleisende ansetzen |
| Antippen | auswählen, freies Gleisende aktiv setzen |
| Ziehen | Ansicht verschieben; auf einem ausgewählten Teil: Teil verschieben |
| Zwei Finger | zoomen und verschieben |
| Lange drücken | Teil zur Auswahl hinzufügen; auf freier Fläche: Auswahlrahmen |
| Doppelt tippen | Eigenschaften; auf freier Fläche: ganzen Plan zeigen |

Basis und Pro gelten genauso: Unter **Datei › Lizenz** wird die Lizenzdatei gewählt. Dort steht auch die **Gerätekennung**,
falls eine Lizenz an ein Gerät gebunden werden soll (in der Lizenzverwaltung als Rechnerkennung eintragen).

Die APK wird mit `src/TrackMaster.Android/trackmaster.keystore` signiert, damit Updates sich über die installierte
Version installieren lassen. Wer einen eigenen Schlüssel nutzen will, legt die Secrets `ANDROID_KEYSTORE_BASE64`
und `ANDROID_KEYSTORE_PASSWORD` (Alias `trackmaster`) an; dann muss die App einmal neu installiert werden.

## Basis und Pro

Ohne Lizenz läuft die **Basisversion**. Mit einer gültigen, signierten Lizenzdatei wird daraus **Pro**.
Pro-Funktionen sind im Tooltip als „Pro-Funktion“ gekennzeichnet; in der Basisversion erklärt ein Klick darauf, was Pro bietet.
Pläne mit Gelände, Elektrik oder Höhen lassen sich auch in der Basisversion öffnen und speichern, ohne dass etwas verloren geht.

| Basis | Pro (zusätzlich) |
|---|---|
| Gleisplanung in allen Spuren mit allen Gleissystemen | Höhenplanung: Steigung, Gleiswendel, Höhen je Gleis |
| Flexgleis, Weichen, Kreuzungen, Prellböcke | Gelände: Höhenflächen, Gewässer, Gebäude, Vegetation, Tunnel |
| Bearbeiten, Rückgängig, Speichern und Öffnen | Elektrik: Einspeisungen, Trennstellen, Kehrschleifen-Erkennung, Signale, Decoder-Adressen, Kabelplan |
| Kollisions- und Planprüfung | 3D-Ansicht mit Mitfahren und Tageszeit |
| Stückliste und Drucken | Export: Stückliste als CSV, Gleisplan als SVG im Maßstab 1:1 |

Die Lizenz wird unter **Datei › Lizenz** eingespielt und liegt dann in `%APPDATA%\TrackMaster Pro\trackmaster.lic`.
Dort steht auch die **Rechnerkennung**, falls eine Lizenz an einen PC gebunden werden soll.
Debug-Builds laufen als Entwicklerversion mit allen Funktionen.

### Lizenzen ausstellen

Lizenzen sind mit RSA-SHA256 signiert (wie bei Moba4N). Der öffentliche Schlüssel ist im Programm eingebaut
(`src/TrackMaster.Licensing/LicensePublicKey.cs`), der private Schlüssel `trackmaster.private.pem` bleibt beim Herausgeber
und gehört **nie** ins Repository (`*.private.pem` und `*.lic` stehen in `.gitignore`).

Das Artefakt **Lizenzwerkzeuge-win-x64** enthält zwei Programme:

- **TrackMaster.Lizenzverwaltung.exe** – Oberfläche: privaten Schlüssel wählen, Kunde, E-Mail, Ablaufdatum und
  optional Rechnerkennung eintragen, „Pro-Lizenz speichern…“. Ausgestellte Lizenzen werden aufgelistet und lassen sich
  erneut speichern.
- **TrackMaster.LicenseTool.exe** – Kommandozeile:

```
TrackMaster.LicenseTool keygen  <ordner>
TrackMaster.LicenseTool issue   <private.pem> <ausgabe.lic> --name "Max Muster" [--email a@b.de] [--expires 2027-12-31] [--machine <id>]
TrackMaster.LicenseTool verify  <datei.lic> [public.pem]
TrackMaster.LicenseTool machine
```

Wer mit `keygen` ein neues Schlüsselpaar erzeugt, muss den öffentlichen Schlüssel in `LicensePublicKey.cs` ersetzen;
bereits ausgestellte Lizenzen gelten dann nicht mehr.

## Funktionen

**Gleise (Basis)**
- **Neuer Plan** (Datei › Neu): Spur und Form der Anlagenplatte wählen – Rechteck, L-, U-, T-, Z-Form oder Ring
  (rundum mit offener Mitte), mit Außenmaßen, Schenkeltiefe und Ausrichtung in 90°-Schritten; bei der U-Form können
  die beiden Schenkel unterschiedlich lang sein. Eine Vorschau zeigt die Form.
  Form und Maße lassen sich später in den Planeigenschaften ändern. Zeichnung, Raster, 3D-Ansicht, SVG-Export und die
  Planprüfung (Gleise über dem Plattenrand bzw. in der offenen Mitte) folgen der Form.
- Spur wählen (Z, N, TT, H0, H0m, H0e, 0, Spur 1, G) und das Gleissystem des Herstellers:
  Z: Märklin mini-club, Rokuhan · N: Kato Unitrack, Roco/Fleischmann ohne Bettung (222xx) und piccolo mit Bettung (91xx), Minitrix, Arnold, Tomix Fine Track,
  Peco Streamline Code 55 und Code 80, Peco Setrack · TT: Tillig Modellgleis, Tillig Advance, Tillig Bettungsgleis, Kühn, Piko ·
  H0: Märklin C, Trix C, Märklin K, Märklin M, Roco geoLine, Roco Line, Piko A, Fleischmann Profi, Fleischmann Modellgleis,
  Tillig Elite, Peco Code 75 und Code 100, Peco Setrack, Kato Unitrack · H0m: Tillig · H0e: Tillig, Peco ·
  0: Standard, Lenz, Peco Bullhead · Spur 1: Märklin · G: LGB, Piko.
  Zusammen rund 1300 Gleisstücke: Geraden, Bögen, Gegenbögen, Schalt-, Entkupplungs- und Übergangsgleise, einfache, schlanke,
  Dreiwege-, Bogen- und Y-Weichen, Kreuzungen, EKW, DKW, Gleisverbindungen und Doppelgleise.
  Die Maße und Artikelnummern stammen aus der Gleisbibliothek von XTrackCAD (Ordner `params` des Pakets `xtrkcad-common`),
  umgesetzt mit `tools/catalog-import/gen.py` nach `src/TrackMaster.Core/CatalogData.cs`. Drehscheiben und Schiebebühnen
  sind nicht übernommen. Die Teile stehen je System nach Geraden, Bögen, Weichen, Kreuzungen und Sonstigem, darin nach Artikelnummer. Die Peco-Streamline-Systeme enthalten nur Weichen, Kreuzungen und kurze Geraden; verlegt wird dort mit Flexgleis.
  Piko TT und Spur 0 Standard sind von Hand gepflegt, haben vorläufige Maße und sind in der Bibliothek markiert.
  Wo ein Hersteller keinen Prellbock im Datensatz hat, gibt es einen allgemeinen Prellbock „PB“ (ungeprüft).
  Frühere Katalogeinträge bleiben im Hintergrund erhalten, damit ältere Pläne weiter öffnen.
  Jedes Gleissystem hat in der Bibliothek einen Eintrag „Flexgleis…“: Systeme mit eigenem Flexgleis (z. B. Märklin K 2205,
  Roco Line 42400, Tillig 85125, Lenz 45013) rechnen in der Stückliste mit dessen Länge, die anderen mit dem allgemeinen Flexgleis der Spur.
- Gleise per Klick am aktiven (orangefarbenen) Gleisende ansetzen oder frei ziehen; Gleise rasten an freien Enden ein.
- **Flexgleis** mit freier Länge oder freiem Radius und Winkel; in der Stückliste als ganze Stangen mit 5 % Verschnitt.
  Wie in WinTrack frei formbar: Werkzeug **Flexgleis ziehen** (Register Gleise) am offenen Gleisende ansetzen und ziehen –
  das Gleis biegt sich tangential mit und rastet an Gleisenden ein (dann als Doppelbogen, der an beiden Enden passt).
  Verlegte Flexgleise lassen sich am Ende anfassen und nachformen, das andere Ende bleibt liegen.
  **Lücke schließen** verbindet das aktive Gleisende mit dem gewählten oder nächsten Gleisende. Hat ein System mehrere
  Flexgleise (z. B. Roco/Fleischmann N 22200 und 22201), steht jedes einzeln in der Bibliothek.
- Drehen, Spiegeln, Anschluss wechseln, Duplizieren, Löschen, Verschieben mit Pfeiltasten, Rückgängig und Wiederholen.
- **Planprüfung** (F7): zu enge Gleisabstände und Überschneidungen (mit Durchfahrtshöhe je Spur), Gleise über den
  Plattenrand, zu starke Steigungen (Warnung ab 3 %, Fehler ab 5 %), Gleise in Gebäuden, im Wasser oder unter dem Gelände
  ohne Tunnel, offene Gleisenden. Ein Klick auf einen Eintrag zeigt die Stelle.
- Stückliste mit Zubehör (Weichenantriebe, Signale, Einspeisungen, Isolierverbinder, Decoder) und **Drucken** (Strg+P).

**Höhen (Pro)**
- Höhe und Steigung je Gleis, Höhenangaben im Plan, Darstellung nach Ebenen.
- **Steigung**: gleichmäßige Rampe über eine gewählte Gleisfolge mit Start-Höhe und Prozentwert.
- **Gleiswendel**: Windungen aus einem Bogengleis mit Höhe je Windung; übereinanderliegende Windungen verbinden sich nicht.
- **Tunnel** für gewählte Gleise.

**Gelände (Pro)**
- Höhenflächen und Gewässer als Vielecke zeichnen (Klicks setzen Punkte, Enter schließt, Esc bricht ab).
- Gebäude aufziehen, Bäume setzen; Name, Größe, Höhe und Drehung im Eigenschaftenfeld.

**Elektrik (Pro)**
- Einspeisungen und Trennstellen setzen; Stromabschnitte farbig anzeigen.
- Kehrschleifen-Erkennung im Zweileiter-System (beim Märklin-Mittelleiter entfällt sie).
- Signale (Haupt-, Vor-, Ausfahr-, Sperr- und Blocksignal), Decoder-Adressen automatisch vergeben, Konflikte erkennen.
- **Kabelplan** als Text mit Abschnitten, Einspeisungen, Trennstellen und Decoderbelegung.

**3D (Pro)**
- 3D-Ansicht mit Gelände, Gebäuden, Bäumen, Schotter, Schienen, Pfeilern, Tunnelportalen und Signalen.
- **Mitfahren** im Führerstand mit einstellbarer Geschwindigkeit (km/h im Vorbild).
- **Tageszeit** von 5 bis 23 Uhr mit Sonnenstand, Abendhimmel und beleuchteten Fenstern.

**Export (Pro)**: Stückliste als CSV (für Excel), Gleisplan als SVG im Maßstab 1:1.

Speichern und Öffnen als `*.trackmaster.json`. Das Format ist dasselbe wie in der Web-Version; die Web-Version
ignoriert Gelände und Elektrik, Gleise lassen sich weiter austauschen.

### Tastenkürzel

| Taste | Funktion |
|---|---|
| Strg+N / Strg+O / Strg+S | Neu / Öffnen / Speichern |
| Strg+P | Drucken |
| Strg+Z / Strg+Y | Rückgängig / Wiederholen |
| Strg+A / Strg+D | Alles wählen / Duplizieren |
| Entf | Löschen |
| R / Umschalt+R | Drehen rechts / links |
| F | Spiegeln |
| A / E | Anschluss wechseln / nächstes Gleisende |
| Pfeiltasten | Auswahl verschieben |
| + / − / 0 | Vergrößern / Verkleinern / Einpassen |
| F7 | Planprüfung |
| Enter / Esc | Fläche abschließen / Werkzeug beenden |
| F1 | Hilfe |

## Aufbau

```
src/
  TrackMaster.Core          Gleiskatalog mit Flexgleis, Geometrie, Plan mit Höhen, Gelände und Elektrik,
                            Verbindungen, Planprüfung, Stromabschnitte und Kehrschleifen, Dateiformat, Stückliste, SVG
  TrackMaster.Licensing     Lizenzdatei, Signaturprüfung, Basis/Pro, Werkzeuge zum Ausstellen
  TrackMaster.App           Hauptfenster (TrackMasterPro.exe): Ribbon, Zeichenfläche, Eigenschaften, Dialoge,
                            Drucken, 3D-Ansicht
  TrackMaster.Android       Android-App (APK): Zeichenfläche, Menüband, Gesten, Dialoge, 3D mit OpenGL ES
tools/
  TrackMaster.Lizenzverwaltung   Lizenzen ausstellen (Oberfläche)
  TrackMaster.LicenseTool        Lizenzen ausstellen und prüfen (Kommandozeile)
tests/
  TrackMaster.Tests         Tests für Geometrie, Flexgleis, Steigung, Wendel, Planprüfung, Elektrik, Dateiformat, Lizenzen
```

## Selbst bauen

Unter Windows mit dem .NET 8 SDK:

```
dotnet build TrackMasterPro.sln -c Release
dotnet test tests/TrackMaster.Tests
dotnet publish src/TrackMaster.App -c Release -r win-x64 --self-contained false -o artifacts/TrackMasterPro
```

GitHub Actions baut bei jedem Push auf `windows-latest`, führt die Tests aus, veröffentlicht Programm und Lizenzwerkzeuge
als Artefakte und erzeugt zur Kontrolle Bildschirmfotos:
`TrackMasterPro.exe --screenshot datei.png [--pro] [--tab Gleise] [--select] [--3d orbit|ride|evening]`.
`--pro` schaltet nur im Bildschirmfoto-Modus alle Funktionen frei.

Die Android-App braucht das .NET 10 SDK mit `dotnet workload install android` und ist deshalb nicht in der
Projektmappe. Sie wird in GitHub Actions auf `ubuntu-latest` gebaut, signiert, im Emulator gestartet und fotografiert:

```
dotnet publish src/TrackMaster.Android -c Release -f net10.0-android -p:AndroidKeyStore=true ...
```

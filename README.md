# TrackMaster Pro

Windows-Programm zum Planen von Modelleisenbahn-Anlagen in allen gängigen Spuren (Z, N, TT, H0, 0, G).
Oberfläche im Stil von Windows 11 mit Ribbon-Leiste, Gleisbibliothek, Zeichenfläche, Eigenschaften und Statusleiste.

Technik: C# / .NET 8 / WPF (`System.Windows.Controls.Ribbon`), aufgebaut wie GleisMatrix Pro (Moba4N).

## Installieren

Unter **Releases** liegen der Windows-Installer `TrackMasterPro-<Version>.msi`, die Android-App
`TrackMasterPro-<Version>.apk` und das Handbuch `TrackMaster-Pro-Handbuch.pdf`.

Der Installer bringt die .NET-Laufzeit mit, legt das Programm unter `C:\Programme\TrackMaster Pro` ab und richtet
Startmenü- und Desktop-Verknüpfung (mit Handbuch) ein. Neue Versionen installieren sich über die alte;
deinstalliert wird über die Windows-Einstellungen. Ohne Oberfläche: `msiexec /i TrackMasterPro-1.4.4.msi /qn`.

Ohne Installation geht es auch: Unter **Actions** das Artefakt **TrackMasterPro-win-x64** herunterladen, entpacken und
`TrackMasterPro.exe` starten (dafür ist die **.NET 8 Desktop Runtime (x64)** nötig).

Das Handbuch entsteht aus den Bildschirmfotos des Builds (Zweig `handbuch-bilder`, neutrale Gleissystem-Namen)
und liegt unter `docs/TrackMaster-Pro-Handbuch.pdf`; der Installer nimmt es mit.

Eine Plandatei lässt sich direkt öffnen: `TrackMasterPro.exe MeineAnlage.trackmaster.json`.

## Android (APK)

Es gibt eine Android-Version als einzelne APK-Datei zum direkten Installieren, ohne Play Store (ab Android 8.0).

1. Die APK aus den **Releases** laden (oder unter **Actions** das Artefakt **TrackMasterPro-Android**) und aufs Gerät
   kopieren (USB, Cloud oder E-Mail).
2. Die Datei auf dem Gerät antippen. Beim ersten Mal fragt Android, ob die App (z. B. „Dateien“ oder der Browser)
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
Die Basisversion enthält alle Planungsfunktionen – Gleise, Höhen, Gleiswendel, Gelände, Elektrik, Signale und 3D.
Pläne mit bis zu 50 Gleisen lassen sich auch in der Basisversion speichern.
Pro schaltet die Ergebnisse frei: **Speichern ohne Größengrenze**, die **Stückliste der Gleise**, die **Holzliste der Gleiswendel** und
das **Drucken** (dazu die Exporte als CSV, SVG und Text). Diese Befehle sind im Tooltip als „Pro-Funktion“ gekennzeichnet;
in der Basisversion erklärt ein Klick darauf, was Pro bietet. Öffnen funktioniert in beiden Versionen.

| Basis | Pro (zusätzlich) |
|---|---|
| Gleisplanung in allen Spuren mit allen Gleissystemen, Flexgleis | Speichern von Plänen jeder Größe (Basis: bis 50 Gleise) |
| Höhenplanung: Steigung, Gleiswendel rund/oval, ein-/zweigleisig | Stückliste der Gleise, auch als CSV |
| Gelände, Gewässer, Gebäude, Vegetation, Tunnel | Holzliste der Gleiswendel mit allen Zuschnitten |
| Elektrik: Einspeisungen, Trennstellen, Signale, Decoder, Kabelplan | Drucken |
| 3D-Ansicht mit Mitfahren und Tageszeit, Plan- und Elektrikprüfung, Öffnen | Export: SVG 1:1, Kabelplan als Text |

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

**Gleise**
- **Neuer Plan** (Datei › Neu): Spur und Form der Anlagenplatte wählen – Rechteck, L-, U-, T-, Z-Form oder Ring
  (rundum mit offener Mitte), mit Außenmaßen, Schenkeltiefe und Ausrichtung in 90°-Schritten; bei der U-Form können
  die beiden Schenkel unterschiedlich lang sein. Eine Vorschau zeigt die Form.
  Form und Maße lassen sich später in den Planeigenschaften ändern. Zeichnung, Raster, 3D-Ansicht, SVG-Export und die
  Planprüfung (Gleise über dem Plattenrand bzw. in der offenen Mitte) folgen der Form.
- Spur wählen (Z, N, TT, H0, H0m, H0e, 0, Spur 1, G) und das Gleissystem des Herstellers:
  Z: Märklin mini-club · N: Kato Unitrack, Roco/Fleischmann ohne Bettung (222xx) und piccolo mit Bettung (91xx), Minitrix ·
  TT: Tillig Modellgleis, Tillig Advance, Tillig Bettungsgleis, Piko ·
  H0: Märklin C, Trix C, Märklin K, Märklin Drehscheibe und Schiebebühne, Roco geoLine, Roco Line ohne und mit Bettung, Piko A, Fleischmann Modellgleis,
  Tillig Elite, Peco Setrack, Kato Unitrack, Weinert mein Gleis · H0m: Tillig · H0e: Tillig, Kato, Roco ·
  0: Standard · Spur 1: Märklin · G: LGB, Piko.
  Zusammen rund 950 Gleisstücke: Geraden, Bögen, Gegenbögen, Schalt-, Entkupplungs- und Übergangsgleise, einfache, schlanke,
  Dreiwege-, Bogen- und Y-Weichen, Kreuzungen, EKW, DKW, Gleisverbindungen und Doppelgleise.
  Die Gleisdaten stehen im eigenen Katalog `src/TrackMaster.Core/Gleiskatalog.json`, erfasst nach Herstellerangaben
  (Produktseiten, Kataloge, Anleitungen; Quellen je System, Erzeuger in `tools/katalog`). Bisher so erfasst: Märklin C, K und
  mini-club, Trix C, Minitrix, Kato Unitrack N, H0 und H0e, Roco/Fleischmann N (ohne Bettung und piccolo), Weinert mein Gleis, Peco Setrack H0, Roco Line ohne und mit Bettung, Roco geoLine, Roco H0e, Piko A, Piko G, Tillig (TT-Modellgleis, Bettungsgleis, Advance, Elite H0, H0m, H0e), LGB und
  Märklin Spur 1. Die übrigen Systeme kommen noch aus der Datei `gleisdaten/gleisdaten.json` unter der GNU GPL Version 2
  (Lizenztext und Herkunft im selben Ordner; Maße aus der Gleisbibliothek von XTrackCAD); das Programm liest sie nur ein.
  Drehscheiben und Schiebebühnen der Hersteller stehen in einer eigenen Gruppe der Bibliothek. Die Teile stehen je System nach Geraden, Bögen, Weichen, Kreuzungen und Sonstigem, darin nach Artikelnummer. Die Peco-Streamline-Systeme enthalten nur Weichen, Kreuzungen und kurze Geraden; verlegt wird dort mit Flexgleis.
  Piko TT und Spur 0 Standard sind von Hand gepflegt (TrackMaster Pro selbst), haben vorläufige Maße und sind in der Bibliothek markiert.
  Wo ein Hersteller keinen Prellbock im Datensatz hat, gibt es einen allgemeinen Prellbock „PB“ (ungeprüft).
  Frühere Katalogeinträge bleiben im Hintergrund erhalten, damit ältere Pläne weiter öffnen.
  Jedes Gleissystem hat in der Bibliothek einen Eintrag „Flexgleis…“: Systeme mit eigenem Flexgleis (z. B. Märklin K 2205,
  Roco Line 42400, Tillig 85125) rechnen in der Stückliste mit dessen Länge, die anderen mit dem allgemeinen Flexgleis der Spur.
- Gleise per Klick am aktiven (orangefarbenen) Gleisende ansetzen oder frei ziehen; Gleise rasten an freien Enden ein.
- **Flexgleis** mit freier Länge oder freiem Radius und Winkel; in der Stückliste als ganze Stangen mit 5 % Verschnitt.
  Frei formbar: Werkzeug **Flexgleis ziehen** (Register Gleise) am offenen Gleisende ansetzen und ziehen –
  das Gleis biegt sich tangential mit und rastet an Gleisenden ein (dann als Doppelbogen, der an beiden Enden passt).
  Verlegte Flexgleise lassen sich am Ende anfassen und nachformen, das andere Ende bleibt liegen.
  **Lücke schließen** verbindet das aktive Gleisende mit dem gewählten oder nächsten Gleisende. Hat ein System mehrere
  Flexgleise (z. B. Roco/Fleischmann N 22200 und 22201), steht jedes einzeln in der Bibliothek.
- **Kopieren und Einfügen** (Strg+C/Strg+V) der gewählten Gleise samt Einspeisungen, Trennstellen und Signalen, auch zwischen
  zwei Plänen derselben Spur; eingefügt wird an der Mausposition.
- Drehen, Spiegeln, Anschluss wechseln, Duplizieren, Löschen, Verschieben mit Pfeiltasten, Rückgängig und Wiederholen.
- **Planprüfung** (F7): zu enge Gleisabstände und Überschneidungen (mit Durchfahrtshöhe je Spur), Gleise über den
  Plattenrand, zu starke Steigungen (Warnung ab 3 %, Fehler ab 5 %), Gleise in Gebäuden, im Wasser oder unter dem Gelände
  ohne Tunnel, offene Gleisenden. Ein Klick auf einen Eintrag zeigt die Stelle.
  Mit dem **längsten Fahrzeug** (Planeigenschaften, Standard 26,4 m im Vorbild) prüft sie zusätzlich Radien,
  Steigung im Bogen (Warnung über 2 %) und S-Bögen ohne Zwischengerade.
- **Beschriftungen** und **Maßlinien** (Start › Beschriften) direkt im Plan, mit Schrifthöhe und Drehung.
- Stückliste mit Zubehör (Weichenantriebe, Signale, Einspeisungen, Isolierverbinder, Decoder) und **Drucken** (Strg+P) – beides Pro.

**Höhen**
- Höhe und Steigung je Gleis, Höhenangaben im Plan, Darstellung nach Ebenen.
- **Steigung**: gleichmäßige Rampe über eine gewählte Gleisfolge mit Start-Höhe und Prozentwert.
- **Gleiswendel** rund oder oval (Halbkreise mit Geraden an den Längsseiten), ein- oder zweigleisig: Windungen aus einem
  Bogengleis mit Höhe je Windung; das zweite Gleis liegt außen, als Katalogbogen mit passendem Radius oder als Flexgleis
  im gewählten Abstand. Übereinanderliegende Windungen verbinden sich nicht.
  Danach folgt die **Holzliste**: Trassensegmente (Kreisringe) und gerade Bretter aus Sperrholz mit Zuschnittmaßen,
  Randaufmaß und Sägezugabe, Stoßlaschen, Grundplatte, Gewindestangen mit Muttern und Scheiben, Plattenbedarf und lichte Höhe –
  zum Kopieren oder als CSV (Pro).
- **Tunnel** für gewählte Gleise.

**Gelände**
- Höhenflächen und Gewässer als Vielecke zeichnen (Klicks setzen Punkte, Enter schließt, Esc bricht ab).
- Gebäude aufziehen, Bäume setzen; Name, Größe, Höhe und Drehung im Eigenschaftenfeld.

**Elektrik**
- Einspeisungen und Trennstellen setzen; Stromabschnitte farbig anzeigen.
- **Fahrstraßen**: von Signal zu Signal (oder Prellbock) mit Weichenstellungen, Länge und Konflikten; Pro exportiert sie
  zusammen mit den Decoderadressen als Text, CSV oder JSON für Steuerungsprogramme.
- Kehrschleifen-Erkennung im Zweileiter-System (beim Märklin-Mittelleiter entfällt sie).
- Signale (Haupt-, Vor-, Ausfahr-, Sperr- und Blocksignal) als H/V-Lichtsignale der DB mit echtem Lampenbild im Haltbegriff
  (Hp 0, Hp 00, Vr 0, Sh 0) und weiß-rot-weißem Mastschild, im Plan, in der 3D-Ansicht und in der Auswahl.
  Einspeisungen und Signale lassen sich wählen und löschen; mit dem jeweiligen Werkzeug entfernt ein Klick sie wieder.
  Decoder-Adressen automatisch vergeben, Konflikte erkennen.
- **Kabelplan** als Text mit Abschnitten, Einspeisungen, Trennstellen und Decoderbelegung.

**3D**
- 3D-Ansicht mit Gelände, Gebäuden, Bäumen, Schotter, Schienen, Pfeilern, Tunnelportalen und Signalen.
- **Mitfahren** im Führerstand mit einstellbarer Geschwindigkeit (km/h im Vorbild).
- **Tageszeit** von 5 bis 23 Uhr mit Sonnenstand, Abendhimmel und beleuchteten Fenstern.
- **Zug fahren**: ein Zug aus Lok und drei Wagen fährt über die Anlage; **Weiche umstellen** (Taste W) stellt die nächste
  Weiche vor dem Zug um.

**Export (Pro)**: Stückliste als CSV (für Excel), Gleisplan als SVG im Maßstab 1:1.

**Ansicht und Darstellung**
- Ansicht frei drehbar: Strg+Mausrad (5°, mit Umschalt 1°), Ansicht › Drehen (15° links/rechts, genauer Winkel,
  gerade ausrichten); ein Kompass zeigt die Drehung, ein Klick darauf richtet die Ansicht gerade aus. Android: mit zwei Fingern drehen.
- Auswahl frei um einen beliebigen Winkel drehen (Start › Frei drehen…).
- Fünf Designs (Logo-Grün, Klassisch Blau, Blaupause, Hoher Kontrast, Werkstatt), sieben Schriftarten, vier Schriftgrößen
  (Ansicht › Design und Schrift). Logo und Programmbilder erzeugt `tools/branding/make_assets.py` aus `docs/branding/TrackPlan-Logo.png`.

**Sicherheit und Pflege**
- Automatische Sicherung im Hintergrund; nach einem Absturz bietet das Programm beim Start die Wiederherstellung an.
- Drucken auch **1:1** auf mehrere Seiten mit Klebekanten und Passmarken, zum Übertragen auf die Platte.
- **Update-Hinweis**: Beim Start lädt das Programm `version.json` aus dem neuesten GitHub-Release (Adresse `UpdateUrl` in
  `Directory.Build.props`) und meldet neuere Versionen mit Link auf die Release-Seite (Datei › Updates). Die Datei erzeugt der
  Build bei jedem Release; eine leere `UpdateUrl` schaltet die Prüfung ab.

**Sprache**: Deutsch und Englisch (Datei › Sprache / Language). Ohne Wahl gilt die Sprache des Systems.
Die Übersetzungen stehen in `src/TrackMaster.Core/Lang.en.json`; die Texte im Quelltext sind deutsch und laufen über `Lang.T`/`Lang.F`.

Speichern (Basis bis 50 Gleise, Pro unbegrenzt) und Öffnen als `*.trackmaster.json`. Das Format ist dasselbe wie in der Web-Version; die Web-Version
ignoriert Gelände und Elektrik, Gleise lassen sich weiter austauschen.

### Tastenkürzel

| Taste | Funktion |
|---|---|
| Strg+N / Strg+O / Strg+S | Neu / Öffnen / Speichern |
| Strg+P | Drucken |
| Strg+Z / Strg+Y | Rückgängig / Wiederholen |
| Strg+A / Strg+D | Alles wählen / Duplizieren |
| Strg+C / Strg+V | Kopieren / Einfügen |
| Entf | Löschen |
| R / Umschalt+R | Drehen rechts / links |
| F | Spiegeln |
| A / E | Anschluss wechseln / nächstes Gleisende |
| Pfeiltasten | Auswahl verschieben |
| + / − / 0 | Vergrößern / Verkleinern / Einpassen |
| Strg+Mausrad | Ansicht drehen |
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

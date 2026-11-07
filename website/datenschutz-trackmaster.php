<?php
/*
 * Abschnitt "9. Gleisplaner TrackMaster Pro" der Datenschutzerklärung (deutsch/englisch), Stand November 2026.
 *
 * Einbinden: In datenschutz.php den bisherigen Abschnitt 9 (von <h2>9. … bis vor <h2>10. …) löschen und an seiner Stelle
 *     <?php include __DIR__ . '/datenschutz-trackmaster.php'; ?>
 * einsetzen. Diese Datei liegt im selben Ordner wie datenschutz.php.
 *
 * Inhalt: Webversion (Speicher im Browser), Online-Aktivierung der Lizenzen (lizenz/aktivierung.php) mit Speicherfristen,
 * Programme für Windows und Android (Update-Prüfung über GitHub). Keine Rechtsberatung.
 */
$tmLang = isset($lang) && $lang === 'en' ? 'en' : ((isset($_GET['lang']) && $_GET['lang'] === 'en') ? 'en' : 'de');
$tmPlaner = function_exists('url') ? url('planer.php') : 'planer.php';
$tmPlaner = htmlspecialchars((string)$tmPlaner, ENT_QUOTES, 'UTF-8');
if ($tmLang === 'en'): ?>
  <h2 id="trackmaster">9. TrackMaster Pro track planner</h2>
  <p>TrackMaster&nbsp;Pro is available as a web version on the <a href="<?= $tmPlaner ?>">Model railway planner</a> page and as a program for Windows and Android. Your track plans and all other input are <strong>never transmitted</strong> to this website or to third parties. The only data exchanged with the internet is the licence activation described in 9.2 and, in the Windows and Android programs, the update check described in 9.4.</p>

  <h3>9.1 Web version: storage in your browser</h3>
  <p>The web version runs entirely in your browser and does not load any content from external servers. So that your work is not lost, it keeps the following data in your browser's local storage:</p>
  <ul>
    <li>a backup of the plan you last worked on,</li>
    <li>your chosen appearance (light/dark), view and language,</li>
    <li>your own 3D models created in the model editor,</li>
    <li>a randomly generated device ID, used only to tell this browser apart from your other devices for the licence,</li>
    <li>if you install a Pro licence: the contents of the licence file (licence number, licensee name and, where included, e-mail address and validity) and the activation status (date of activation and of the last check).</li>
  </ul>
  <p>You can remove this data at any time in your browser settings (“Clear site data”); an installed licence can also be removed in the planner under “File › Licence › Remove licence”. When you save, open, export or print a plan or a list (e.g. as file, CSV, SVG, PNG, DXF, STL/OBJ, ZIP or PDF), this happens solely as a file on your own device.</p>
  <p>The legal basis for storage in the browser is Section 25(2) No. 2 TDDDG, as it is strictly necessary for the functions you expressly use; any further processing is based on Art. 6(1)(b) GDPR.</p>

  <h3>9.2 Online activation of Pro licences</h3>
  <p>A Pro licence may be used on a limited number of devices (usually two). To enforce this, the web version and the Windows and Android programs register the device with our server (www.jm-jungwirth.de/lizenz/aktivierung.php) when a licence is first used, and check the activation about once a week when an internet connection is available. The following is transmitted:</p>
  <ul>
    <li>the licence file (licence number, licensee name, validity and, where included, e-mail address),</li>
    <li>a device ID (web version: the random browser ID from 9.1; Windows: an ID derived from computer data by a one-way hash; Android: a one-way hash of the device's Android ID),</li>
    <li>the device name (web version: browser and operating system, e.g. “Firefox (Windows)”; Windows: the computer name; Android: manufacturer and model),</li>
    <li>the platform (Web, Windows or Android) and the program version.</li>
  </ul>
  <p>Stored on the server are only the licence number, the licensee name and, for each device, the device ID, device name, platform, program version and the date of the first and last registration. Rejected activations beyond the permitted number of devices are recorded with date, device ID, device name and platform. E-mail and IP addresses are <strong>not</strong> stored by the activation (for the hosting provider's server logfiles see section 3).</p>
  <p>Purpose and legal basis: providing the licence you acquired and complying with its terms of use (Art. 6(1)(b) GDPR) and, for rejected activations, our legitimate interest in preventing misuse of licences (Art. 6(1)(f) GDPR).</p>
  <p>Storage period: a device that has not reported for 12 months is deleted automatically, rejected activations after 90 days. “Release this device” in the licence dialog deletes the device's entry immediately. A licence with no remaining devices is removed together with the licensee name; only a licence blocked because of misuse is kept for as long as the block applies. On request we will tell you what is stored about your licence and delete it (see section 13). The data is stored on our web space at the hosting provider named in section 3 and is not passed on to third parties.</p>

  <h3>9.3 Purchasing a Pro licence</h3>
  <p>When you order a Pro licence (by e-mail or the contact form), I process your name, e-mail address, the order and payment details and, if you request an invoice with address, your address, in order to conclude and perform the contract, issue the invoice and create the licence file in your name (Art. 6(1)(b) GDPR). Invoices and accounting records are kept for the statutory retention periods (Section 147 of the German Fiscal Code, Section 257 of the German Commercial Code: usually 8 or 10 years) (Art. 6(1)(c) GDPR); the remaining order correspondence is deleted three years after the end of the year of purchase. Bank details or payment data appear only as far as needed for the payment you chose; they are not passed on to third parties apart from the payment service or bank involved.</p>

  <h3>9.4 Windows and Android programs</h3>
  <p>The programs store plans, settings, the licence file and the activation status only locally on your device (Windows: in your user folder; Android: in the app's private storage). Apart from the activation in 9.2, they make one further connection: on start-up they check whether a new version exists by downloading a small version file from GitHub (github.com, operated by GitHub Inc., USA). In doing so, GitHub sees, as with any download, your IP address and the identifier “TrackMasterPro-UpdateCheck”; no plans, licence data or device IDs are transmitted. GitHub Inc. is certified under the EU-US Data Privacy Framework. The legal basis is Art. 6(1)(f) GDPR (legitimate interest in informing you about updates and fixes). You can switch off the automatic check: Windows under “File › Update check on start-up…”, Android under “File › Updates… › On start-up…”. The Android app only requests the “Internet” permission, for the activation and the update check.</p>
<?php else: ?>
  <h2 id="trackmaster">9. Gleisplaner TrackMaster Pro</h2>
  <p>TrackMaster&nbsp;Pro steht als Webversion auf der Seite <a href="<?= $tmPlaner ?>">Modellbahn-Planer</a> sowie als Programm für Windows und Android zur Verfügung. Ihre Gleispläne und alle übrigen Eingaben werden <strong>nie an diese Website oder an Dritte übertragen</strong>. Mit dem Internet ausgetauscht werden nur die in 9.2 beschriebene Lizenz-Aktivierung und bei den Programmen für Windows und Android die in 9.4 beschriebene Update-Prüfung.</p>

  <h3>9.1 Webversion: Speicherung in Ihrem Browser</h3>
  <p>Die Webversion läuft vollständig in Ihrem Browser und lädt keine Inhalte von fremden Servern nach. Damit Ihre Arbeit nicht verloren geht, legt sie folgende Daten im lokalen Speicher Ihres Browsers („Local Storage“) ab:</p>
  <ul>
    <li>eine Sicherung des zuletzt bearbeiteten Gleisplans,</li>
    <li>die gewählte Darstellung (hell/dunkel), Ansicht und Sprache,</li>
    <li>eigene 3D-Modelle aus dem Modell-Editor,</li>
    <li>eine zufällig erzeugte Gerätekennung, die nur dazu dient, diesen Browser für die Lizenz von Ihren anderen Geräten zu unterscheiden,</li>
    <li>falls Sie eine Pro-Lizenz einspielen: den Inhalt der Lizenzdatei (Lizenznummer, Name des Lizenznehmers, ggf. E-Mail-Adresse und Gültigkeit) und den Stand der Aktivierung (Datum der Aktivierung und der letzten Prüfung).</li>
  </ul>
  <p>Sie können diese Daten jederzeit über die Browser-Einstellungen („Websitedaten löschen“) entfernen; eine eingespielte Lizenz lässt sich außerdem im Planer unter „Datei&nbsp;›&nbsp;Lizenz&nbsp;›&nbsp;Lizenz entfernen“ löschen. Wenn Sie einen Plan oder eine Liste speichern, öffnen, exportieren oder drucken (z.&nbsp;B. als Plandatei, CSV, SVG, PNG, DXF, STL/OBJ, ZIP oder PDF), geschieht das ausschließlich als Datei auf Ihrem eigenen Gerät.</p>
  <p>Rechtsgrundlage für die Speicherung im Browser ist § 25 Abs.&nbsp;2 Nr.&nbsp;2 TDDDG, da sie für die von Ihnen ausdrücklich genutzten Funktionen unbedingt erforderlich ist; die weitere Verarbeitung erfolgt auf Grundlage von Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;b DSGVO.</p>

  <h3>9.2 Online-Aktivierung der Pro-Lizenzen</h3>
  <p>Eine Pro-Lizenz darf auf einer begrenzten Zahl von Geräten genutzt werden (in der Regel zwei). Damit das eingehalten wird, melden die Webversion und die Programme für Windows und Android das Gerät bei der ersten Nutzung einer Lizenz an unserem Server an (www.jm-jungwirth.de/lizenz/aktivierung.php) und prüfen die Aktivierung danach etwa einmal pro Woche, wenn eine Internetverbindung besteht. Übertragen werden:</p>
  <ul>
    <li>die Lizenzdatei (Lizenznummer, Name des Lizenznehmers, Gültigkeit und ggf. E-Mail-Adresse),</li>
    <li>eine Gerätekennung (Webversion: die zufällige Browserkennung aus 9.1; Windows: eine aus Rechnerdaten per Einweg-Hash gebildete Kennung; Android: ein Einweg-Hash der Android-ID des Geräts),</li>
    <li>der Gerätename (Webversion: Browser und Betriebssystem, z.&nbsp;B. „Firefox (Windows)“; Windows: der Computername; Android: Hersteller und Modell),</li>
    <li>die Plattform (Web, Windows oder Android) und die Programmversion.</li>
  </ul>
  <p>Auf dem Server gespeichert werden nur Lizenznummer, Name des Lizenznehmers und je Gerät Gerätekennung, Gerätename, Plattform, Programmversion sowie Datum der ersten und der letzten Anmeldung. Abgelehnte Aktivierungen über die erlaubte Geräteanzahl hinaus werden mit Datum, Gerätekennung, Gerätename und Plattform festgehalten. E-Mail- und IP-Adressen werden von der Aktivierung <strong>nicht</strong> gespeichert (zu den Server-Logfiles des Hosting-Anbieters siehe Abschnitt 3).</p>
  <p>Zweck und Rechtsgrundlage: Bereitstellung der von Ihnen erworbenen Lizenz und Einhaltung ihrer Nutzungsbedingungen (Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;b DSGVO), bei abgelehnten Aktivierungen unser berechtigtes Interesse, Missbrauch von Lizenzen zu verhindern (Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;f DSGVO).</p>
  <p>Speicherdauer: Ein Gerät, das sich 12 Monate nicht gemeldet hat, wird automatisch gelöscht, abgelehnte Aktivierungen nach 90 Tagen. „Dieses Gerät freigeben“ im Lizenzdialog löscht den Eintrag des Geräts sofort. Eine Lizenz ohne verbleibende Geräte wird samt Name des Lizenznehmers entfernt; nur eine wegen Missbrauchs gesperrte Lizenz bleibt gespeichert, solange die Sperre besteht. Auf Wunsch teilen wir Ihnen mit, was zu Ihrer Lizenz gespeichert ist, und löschen es (siehe Abschnitt 13). Die Daten liegen auf unserem Webspace beim in Abschnitt 3 genannten Hosting-Anbieter und werden nicht an Dritte weitergegeben.</p>

  <h3>9.3 Kauf einer Pro-Lizenz</h3>
  <p>Wenn Sie eine Pro-Lizenz bestellen (per E-Mail oder Kontaktformular), verarbeite ich Ihren Namen, Ihre E-Mail-Adresse, die Bestell- und Zahlungsangaben und – wenn Sie eine Rechnung mit Anschrift wünschen – Ihre Anschrift, um den Vertrag zu schließen und zu erfüllen, die Rechnung auszustellen und die Lizenzdatei auf Ihren Namen zu erstellen (Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;b DSGVO). Rechnungen und Buchungsbelege werden für die gesetzlichen Aufbewahrungsfristen gespeichert (§&nbsp;147 AO, §&nbsp;257 HGB: in der Regel 8 bzw. 10 Jahre; Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;c DSGVO); der übrige Schriftverkehr zur Bestellung wird drei Jahre nach Ende des Kaufjahres gelöscht. Bank- oder Zahlungsdaten fallen nur an, soweit es die gewählte Zahlungsart erfordert; sie werden außer an die beteiligte Bank bzw. den Zahlungsdienst nicht weitergegeben.</p>

  <h3>9.4 Programme für Windows und Android</h3>
  <p>Die Programme speichern Pläne, Einstellungen, Lizenzdatei und Aktivierungsstand nur lokal auf Ihrem Gerät (Windows: in Ihrem Benutzerordner; Android: im privaten Speicher der App). Außer der Aktivierung aus 9.2 bauen sie nur eine weitere Verbindung auf: Beim Start prüfen sie, ob es eine neue Version gibt, und laden dazu eine kleine Versionsdatei von GitHub (github.com, betrieben von GitHub Inc., USA). GitHub sieht dabei wie bei jedem Abruf Ihre IP-Adresse und die Kennung „TrackMasterPro-UpdateCheck“; Pläne, Lizenzdaten oder Gerätekennungen werden nicht übertragen. GitHub Inc. ist nach dem EU-US Data Privacy Framework zertifiziert. Rechtsgrundlage ist Art.&nbsp;6 Abs.&nbsp;1 lit.&nbsp;f DSGVO (berechtigtes Interesse, Sie auf Updates und Fehlerbehebungen hinzuweisen). Die automatische Prüfung lässt sich abschalten: unter Windows über „Datei › Update-Prüfung beim Start…“, unter Android über „Datei › Updates… › Beim Start…“. Die Android-App fordert nur die Berechtigung „Internet“ an, für Aktivierung und Update-Prüfung.</p>
<?php endif; ?>

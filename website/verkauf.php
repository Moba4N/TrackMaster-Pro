<?php
/* =====================================================================
   JörgsMoba4N – Verkaufs- und Lizenzbedingungen TrackMaster Pro mit Widerrufsbelehrung
   Entwurf Stand November 2026 – KEINE Rechtsberatung, vor Verkaufsstart prüfen lassen.
   Anbieter-Daten aus config.php (IMP_NAME, IMP_STREET, IMP_CITY, IMP_EMAIL, IMP_PHONE).
   Optional in config.php:
     define('TM_PREIS', '29,00 €');          // Preis einer Pro-Lizenz (Endpreis)
     define('IMP_VAT', 'DE123456789');       // USt-IdNr., falls vorhanden
     define('IMP_SMALL_BUSINESS', true);     // Kleinunternehmer nach § 19 UStG (keine USt)
   ===================================================================== */
require_once __DIR__ . '/functions.php';

$ic_back = '<svg class="svg-ic" viewBox="0 0 24 24"><path d="M19 12H5M12 5l-7 7 7 7"/></svg>';
$en = (isset($lang) && $lang === 'en') || (isset($_GET['lang']) && $_GET['lang'] === 'en')
    || stripos((string)t('privacy'), 'privacy') !== false;
$page_title = $en ? 'Terms of sale TrackMaster Pro' : 'Verkaufs- und Lizenzbedingungen TrackMaster Pro';
require __DIR__ . '/header.php';

$anbieter = h(IMP_NAME) . ', ' . h(IMP_STREET) . ', ' . h(IMP_CITY) . ', ' . ($en ? 'Germany' : 'Deutschland');
$mail = '<a href="mailto:' . h(IMP_EMAIL) . '">' . h(IMP_EMAIL) . '</a>';
$preis = defined('TM_PREIS') && TM_PREIS ? h(TM_PREIS) : null;
$klein = defined('IMP_SMALL_BUSINESS') && IMP_SMALL_BUSINESS;
?>
<div class="wrap-narrow section legal">
  <a class="pill-link" style="display:inline-flex;align-items:center;gap:6px;margin-bottom:18px;" href="<?= h(url('index.php')) ?>"><?= $ic_back ?> <?= h(t('pub_back_home')) ?></a>
<?php if ($en): ?>
  <h1>Terms of sale and licence TrackMaster Pro</h1>
  <p class="muted">These terms are available in German only; the German version below is legally binding. In short: you buy a Pro licence file for TrackMaster Pro for use on up to two devices, delivered by e-mail after payment. As a consumer you have a 14-day right of withdrawal, which expires once the licence file has been delivered with your express consent.</p>
<?php endif; ?>
  <h1>Verkaufs- und Lizenzbedingungen TrackMaster&nbsp;Pro</h1>
  <p class="muted">Stand: November 2026</p>

  <h2>§ 1 Geltungsbereich, Anbieter</h2>
  <p>Diese Bedingungen gelten für den Kauf von Pro-Lizenzen für den Gleisplaner TrackMaster&nbsp;Pro (Webversion sowie Programme für Windows und Android). Anbieter und Vertragspartner ist <?= $anbieter ?>, E-Mail: <?= $mail ?><?php if (IMP_PHONE): ?>, Telefon: <?= h(IMP_PHONE) ?><?php endif; ?>. Die Basisversion ist kostenlos und ohne Lizenz nutzbar; für sie gelten nur §&nbsp;7 und §&nbsp;8.</p>

  <h2>§ 2 Vertragsgegenstand</h2>
  <p>Gegenstand des Vertrags ist eine Lizenzdatei („trackmaster.lic“), mit der die Pro-Funktionen von TrackMaster&nbsp;Pro freigeschaltet werden. Sie erhalten ein einfaches, nicht übertragbares, zeitlich unbefristetes Recht, die Software in der jeweils bereitgestellten Fassung für eigene Zwecke zu nutzen. Eine Lizenz darf auf bis zu zwei Geräten gleichzeitig genutzt werden; dazu wird jedes Gerät einmal online aktiviert (siehe Datenschutzerklärung, Abschnitt&nbsp;9.2). Ein Gerät lässt sich im Lizenzdialog jederzeit freigeben, um die Lizenz auf ein anderes Gerät zu übertragen. Die Funktionen der Basis- und der Pro-Version sind im Programm unter „Lizenz“ beschrieben.</p>

  <h2>§ 3 Vertragsschluss</h2>
  <p>Die Darstellung der Software auf dieser Website ist kein bindendes Angebot. Sie bestellen eine Lizenz per E-Mail oder über das Kontaktformular unter Angabe Ihres Namens und Ihrer E-Mail-Adresse. Der Vertrag kommt zustande, wenn ich Ihre Bestellung per E-Mail bestätige (Bestellbestätigung mit Rechnung und Zahlungsangaben). Vertragssprache ist Deutsch. Den Vertragstext speichere ich nicht gesondert; diese Bedingungen können Sie jederzeit auf dieser Seite abrufen und speichern.</p>

  <h2>§ 4 Preise und Zahlung</h2>
  <p>Es gilt der zum Zeitpunkt der Bestellung auf dieser Website angegebene Preis<?= $preis ? ' (derzeit ' . $preis . ' je Lizenz)' : '' ?>. Alle Preise sind Endpreise<?= $klein ? '; gemäß §&nbsp;19 UStG wird keine Umsatzsteuer berechnet (Kleinunternehmerregelung)' : ' einschließlich der gesetzlichen Umsatzsteuer' ?>. Versandkosten fallen nicht an. Die Zahlung erfolgt per Vorkasse auf die in der Bestellbestätigung genannte Weise und ist mit der Bestellbestätigung fällig.</p>

  <h2>§ 5 Lieferung</h2>
  <p>Nach Zahlungseingang erhalten Sie die Lizenzdatei per E-Mail, in der Regel innerhalb von drei Werktagen. Die Software selbst laden Sie über diese Website bzw. die dort genannten Download-Seiten herunter.</p>

  <h2>§ 6 Widerrufsrecht für Verbraucher</h2>
  <p>Verbrauchern steht ein Widerrufsrecht nach folgender Maßgabe zu. Verbraucher ist jede natürliche Person, die ein Rechtsgeschäft zu Zwecken abschließt, die überwiegend weder ihrer gewerblichen noch ihrer selbständigen beruflichen Tätigkeit zugerechnet werden können.</p>
  <h3>Widerrufsbelehrung</h3>
  <p><strong>Widerrufsrecht</strong><br>
  Sie haben das Recht, binnen vierzehn Tagen ohne Angabe von Gründen diesen Vertrag zu widerrufen. Die Widerrufsfrist beträgt vierzehn Tage ab dem Tag des Vertragsabschlusses.</p>
  <p>Um Ihr Widerrufsrecht auszuüben, müssen Sie mich (<?= $anbieter ?>, E-Mail: <?= $mail ?><?php if (IMP_PHONE): ?>, Telefon: <?= h(IMP_PHONE) ?><?php endif; ?>) mittels einer eindeutigen Erklärung (z.&nbsp;B. eine E-Mail) über Ihren Entschluss, diesen Vertrag zu widerrufen, informieren. Sie können dafür das beigefügte Muster-Widerrufsformular verwenden, das jedoch nicht vorgeschrieben ist. Zur Wahrung der Widerrufsfrist reicht es aus, dass Sie die Mitteilung über die Ausübung des Widerrufsrechts vor Ablauf der Widerrufsfrist absenden.</p>
  <p><strong>Folgen des Widerrufs</strong><br>
  Wenn Sie diesen Vertrag widerrufen, habe ich Ihnen alle Zahlungen, die ich von Ihnen erhalten habe, unverzüglich und spätestens binnen vierzehn Tagen ab dem Tag zurückzuzahlen, an dem die Mitteilung über Ihren Widerruf dieses Vertrags bei mir eingegangen ist. Für diese Rückzahlung verwende ich dasselbe Zahlungsmittel, das Sie bei der ursprünglichen Transaktion eingesetzt haben, es sei denn, mit Ihnen wurde ausdrücklich etwas anderes vereinbart; in keinem Fall werden Ihnen wegen dieser Rückzahlung Entgelte berechnet. Nach einem Widerruf wird die Lizenz gesperrt.</p>
  <p><strong>Vorzeitiges Erlöschen des Widerrufsrechts</strong><br>
  Das Widerrufsrecht erlischt bei einem Vertrag über die Bereitstellung digitaler Inhalte, die nicht auf einem körperlichen Datenträger geliefert werden, wenn ich mit der Vertragserfüllung (Zusendung der Lizenzdatei) begonnen habe, nachdem Sie ausdrücklich zugestimmt haben, dass ich vor Ablauf der Widerrufsfrist mit der Vertragserfüllung beginne, Sie Ihre Kenntnis davon bestätigt haben, dass Sie durch Ihre Zustimmung mit Beginn der Vertragserfüllung Ihr Widerrufsrecht verlieren, und ich Ihnen eine Bestätigung des Vertrags auf einem dauerhaften Datenträger (z.&nbsp;B. per E-Mail) zur Verfügung gestellt habe.</p>
  <h3>Muster-Widerrufsformular</h3>
  <p>(Wenn Sie den Vertrag widerrufen wollen, dann füllen Sie bitte dieses Formular aus und senden Sie es zurück.)</p>
  <p>– An <?= $anbieter ?>, E-Mail: <?= h(IMP_EMAIL) ?>:<br>
  – Hiermit widerrufe(n) ich/wir (*) den von mir/uns (*) abgeschlossenen Vertrag über den Kauf der folgenden Waren (*)/die Erbringung der folgenden Dienstleistung (*)<br>
  – Bestellt am (*)/erhalten am (*)<br>
  – Name des/der Verbraucher(s)<br>
  – Anschrift des/der Verbraucher(s)<br>
  – Unterschrift des/der Verbraucher(s) (nur bei Mitteilung auf Papier)<br>
  – Datum<br>
  (*) Unzutreffendes streichen.</p>

  <h2>§ 7 Gewährleistung und Aktualisierungen</h2>
  <p>Es gelten die gesetzlichen Vorschriften über Mängel digitaler Produkte (§§&nbsp;327 ff. BGB). Ich stelle Aktualisierungen bereit, die für den Erhalt der Vertragsmäßigkeit erforderlich sind, solange Sie dies vernünftigerweise erwarten können; Hinweise auf neue Versionen erhalten Sie im Programm. Mängel melden Sie bitte per E-Mail an <?= $mail ?>.</p>

  <h2>§ 8 Haftung</h2>
  <p>Ich hafte unbeschränkt bei Vorsatz und grober Fahrlässigkeit, bei Verletzung von Leben, Körper oder Gesundheit sowie nach dem Produkthaftungsgesetz. Bei leicht fahrlässiger Verletzung einer wesentlichen Vertragspflicht ist die Haftung auf den vertragstypischen, vorhersehbaren Schaden begrenzt; im Übrigen ist die Haftung für leichte Fahrlässigkeit ausgeschlossen. Pläne, Stücklisten und Maße aus TrackMaster&nbsp;Pro sind vor dem Bau eigenverantwortlich zu prüfen; für die Datensicherung Ihrer Plandateien sind Sie selbst verantwortlich.</p>

  <h2>§ 9 Nutzungsregeln</h2>
  <p>Die Lizenzdatei ist persönlich. Sie darf nicht weitergegeben, veröffentlicht oder verändert und nicht auf mehr Geräten als erlaubt genutzt werden. Bei einem Verstoß kann die Lizenz nach vorheriger Mitteilung gesperrt werden. Die Software darf nicht dekompiliert oder verändert werden, soweit das Gesetz (§§&nbsp;69d, 69e UrhG) dies nicht ausdrücklich erlaubt.</p>

  <h2>§ 10 Streitbeilegung, Schlussbestimmungen</h2>
  <p>Ich bin nicht bereit und nicht verpflichtet, an Streitbeilegungsverfahren vor einer Verbraucherschlichtungsstelle teilzunehmen. Es gilt das Recht der Bundesrepublik Deutschland unter Ausschluss des UN-Kaufrechts; gegenüber Verbrauchern gilt diese Rechtswahl nur, soweit dadurch nicht der Schutz durch zwingende Vorschriften des Staates entzogen wird, in dem der Verbraucher seinen gewöhnlichen Aufenthalt hat.</p>
</div>
<?php require __DIR__ . '/footer.php'; ?>

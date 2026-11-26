<?php
/* =====================================================================
   JörgsMoba4N – Bestellseite TrackMaster Pro: Kauflizenz und Pro-Abo
   Die Programme verlinken hierher („Pro kaufen“, BuyUrl in Directory.Build.props).
   Einstellungen in config.php:
     define('TM_PREIS', '59,99 €');          // Kauflizenz, einmalig (Endpreis)
     define('TM_PREIS_MONAT', '3,99 €');     // Pro-Abo je Monat; ohne Angabe kein Abo-Angebot
     define('TM_PAYPAL_KAUF', 'https://…');  // Link des PayPal-Knopfs für die einmalige Zahlung (optional)
     define('TM_PAYPAL_ABO', 'https://…');   // Link des PayPal-Abo-Knopfs (…/billing/plans/subscribe?plan_id=…) (optional)
   Ohne PayPal-Links wird per E-Mail bestellt (vorausgefüllte Nachricht an IMP_EMAIL).
   ===================================================================== */
require_once __DIR__ . '/functions.php';

$ic_back = '<svg class="svg-ic" viewBox="0 0 24 24"><path d="M19 12H5M12 5l-7 7 7 7"/></svg>';
$page_title = 'TrackMaster Pro kaufen';
require __DIR__ . '/header.php';

$kauf = defined('TM_PREIS') && TM_PREIS ? (string)TM_PREIS : '';
$monat = defined('TM_PREIS_MONAT') && TM_PREIS_MONAT ? (string)TM_PREIS_MONAT : '';
$paypalKauf = defined('TM_PAYPAL_KAUF') && TM_PAYPAL_KAUF ? (string)TM_PAYPAL_KAUF : '';
$paypalAbo = defined('TM_PAYPAL_ABO') && TM_PAYPAL_ABO ? (string)TM_PAYPAL_ABO : '';
$klein = defined('IMP_SMALL_BUSINESS') ? (bool)IMP_SMALL_BUSINESS : true;

/** mailto-Link mit vorausgefülltem Betreff und Text. */
function tm_bestell_mail(string $betreff, string $text): string
{
    return 'mailto:' . IMP_EMAIL . '?subject=' . rawurlencode($betreff) . '&body=' . rawurlencode($text);
}
$mailText = "Name:\nE-Mail-Adresse für die Lizenzdatei:\n\nIch habe die Verkaufs- und Lizenzbedingungen gelesen und stimme zu, dass die Lizenzdatei vor Ablauf der Widerrufsfrist geliefert wird. Mir ist bekannt, dass ich damit mein Widerrufsrecht verliere.\n";
$mailKauf = tm_bestell_mail('Bestellung TrackMaster Pro – Kauflizenz', "Ich bestelle eine Kauflizenz für TrackMaster Pro" . ($kauf ? " zu $kauf" : '') . ".\n\n" . $mailText);
$mailAbo = tm_bestell_mail('Bestellung TrackMaster Pro – Abo', "Ich bestelle das Pro-Abo für TrackMaster Pro" . ($monat ? " zu $monat je Monat" : '') . ".\n\n" . $mailText);

$pro = [
    'Speichern von Plänen jeder Größe (Basis: bis 50 Gleise)',
    'Stückliste der Gleise mit Preisen und Bestand, auch als CSV',
    'Holzliste der Gleiswendel mit allen Zuschnitten, Zuschnitt als SVG 1:1',
    'Spantenbau der Anlage mit Aufmaßen und Schablonen, auch in der 3D-Ansicht',
    'Trasse aus markierten Gleisen, jedes Teil als eigene SVG-Datei',
    'Drucken des Gleisplans, auch 1:1, und Export als SVG',
    'Eigene 3D-Modelle im Modell-Editor',
];
?>
<style>
  .tm-buy { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 16px; margin: 20px 0; }
  .tm-buy .card { border: 1px solid rgba(0,0,0,.12); border-radius: 10px; padding: 18px; display: flex; flex-direction: column; gap: 8px; }
  .tm-buy .price { font-size: 28px; font-weight: 700; }
  .tm-buy .price small { font-size: 14px; font-weight: 400; opacity: .75; }
  .tm-buy .card ul { margin: 0; padding-left: 18px; }
  .tm-buy .grow { flex: 1; }
  .tm-buy a.kaufen { display: inline-block; text-align: center; padding: 10px 16px; border-radius: 6px; background: #437E1E; color: #fff; font-weight: 600; text-decoration: none; }
  .tm-buy a.kaufen[aria-disabled=true] { opacity: .45; pointer-events: none; }
  .tm-zustimmung { border-radius: 8px; padding: 12px 14px; background: rgba(67,126,30,.08); margin: 8px 0 4px; }
  .tm-zustimmung label { display: flex; gap: 8px; align-items: flex-start; margin: 6px 0; }
</style>
<div class="wrap-narrow section legal">
  <a class="pill-link" style="display:inline-flex;align-items:center;gap:6px;margin-bottom:18px;" href="<?= h(url('index.php')) ?>"><?= $ic_back ?> <?= h(t('pub_back_home')) ?></a>
  <h1>TrackMaster&nbsp;Pro kaufen</h1>
  <p>Die Basisversion von TrackMaster&nbsp;Pro ist kostenlos. Mit einer Pro-Lizenz kommen dazu:</p>
  <ul><?php foreach ($pro as $x): ?><li><?= h($x) ?></li><?php endforeach; ?></ul>
  <p>Eine Lizenz gilt für die Webversion sowie die Programme für Windows und Android, auf bis zu zwei Geräten gleichzeitig.</p>

  <div class="tm-zustimmung">
    <label><input type="checkbox" id="tm-agb"> <span>Ich habe die <a href="<?= h(url('verkauf.php')) ?>" target="_blank">Verkaufs- und Lizenzbedingungen</a> mit der Widerrufsbelehrung gelesen und bin einverstanden.</span></label>
    <label><input type="checkbox" id="tm-widerruf"> <span>Ich stimme ausdrücklich zu, dass die Lizenzdatei vor Ablauf der Widerrufsfrist geliefert wird. Mir ist bekannt, dass ich damit mein Widerrufsrecht verliere.</span></label>
  </div>

  <div class="tm-buy">
    <div class="card">
      <strong>Kauflizenz</strong>
      <div class="price"><?= $kauf ? h($kauf) : 'Preis auf Anfrage' ?> <small>einmalig</small></div>
      <div class="grow">Unbefristet nutzbar, inklusive der Aktualisierungen der jeweils bereitgestellten Fassung.</div>
      <a class="kaufen" aria-disabled="true" href="<?= h($paypalKauf ?: $mailKauf) ?>"<?= $paypalKauf ? ' target="_blank" rel="noopener"' : '' ?>><?= $paypalKauf ? 'Mit PayPal bezahlen' : 'Per E-Mail bestellen' ?></a>
    </div>
    <?php if ($monat): ?>
    <div class="card">
      <strong>Pro-Abo</strong>
      <div class="price"><?= h($monat) ?> <small>je Monat</small></div>
      <div class="grow">Monatlich kündbar zum Monatsende. Pro läuft, solange das Abo bezahlt ist; jedes Gerät meldet sich dazu mindestens alle 30&nbsp;Tage online. Danach läuft die Basisversion, Ihre Pläne bleiben erhalten.</div>
      <a class="kaufen" aria-disabled="true" href="<?= h($paypalAbo ?: $mailAbo) ?>"<?= $paypalAbo ? ' target="_blank" rel="noopener"' : '' ?>><?= $paypalAbo ? 'Abo mit PayPal abschließen' : 'Abo per E-Mail bestellen' ?></a>
    </div>
    <?php endif; ?>
  </div>

  <p class="muted">Alle Preise sind Endpreise<?= $klein ? '; gemäß §&nbsp;19 UStG wird keine Umsatzsteuer berechnet (Kleinunternehmerregelung)' : ' einschließlich Umsatzsteuer' ?>. Zahlung per PayPal, Kreditkarte (über PayPal) oder Überweisung.</p>

  <h2>So geht es weiter</h2>
  <ol>
    <li>Nach Ihrer Bestellung bzw. Zahlung erhalten Sie eine Bestellbestätigung per E-Mail.</li>
    <li>Nach Zahlungseingang schicke ich Ihnen die Lizenzdatei „trackmaster.lic“, in der Regel innerhalb von drei Werktagen. <?php if ($paypalKauf || $paypalAbo): ?>Bitte geben Sie bei PayPal den Namen an, auf den die Lizenz lauten soll.<?php endif; ?></li>
    <li>In TrackMaster&nbsp;Pro unter <em>Datei › Lizenz…</em> die Datei einspielen (Webversion: Text der Datei einfügen). Das Gerät meldet sich dabei einmal online an.</li>
  </ol>
  <?php if ($monat): ?>
  <p>Ein laufendes Abo kündigen Sie jederzeit hier: <a href="<?= h(url('abo-kuendigen.php')) ?>">Verträge hier kündigen</a>.</p>
  <?php endif; ?>
  <p>Fragen? Schreiben Sie mir: <a href="mailto:<?= h(IMP_EMAIL) ?>"><?= h(IMP_EMAIL) ?></a></p>
</div>
<script>
  (function () {
    var agb = document.getElementById('tm-agb'), wid = document.getElementById('tm-widerruf');
    function sync() {
      var ok = agb.checked && wid.checked;
      document.querySelectorAll('.tm-buy a.kaufen').forEach(function (a) { a.setAttribute('aria-disabled', ok ? 'false' : 'true'); });
    }
    agb.addEventListener('change', sync); wid.addEventListener('change', sync); sync();
  })();
</script>
<?php require __DIR__ . '/footer.php'; ?>

<?php
/* =====================================================================
   JörgsMoba4N – Impressum (deutsch/englisch), Stand November 2026
   Name, Anschrift und Kontakt kommen aus config.php:
     IMP_NAME   vollständiger Vor- und Nachname  (PFLICHT – war leer, deshalb fehlte der Name)
     IMP_STREET Straße und Hausnummer
     IMP_CITY   PLZ und Ort
     IMP_EMAIL  E-Mail-Adresse
     IMP_PHONE  Telefon (optional, leer = nicht anzeigen)
   Gliederung: § 5 DDG (Anbieter), Kontakt, § 18 Abs. 2 MStV (Verantwortlicher),
   Software TrackMaster Pro, Verbraucherstreitbeilegung, Haftung, Urheberrecht.
   Keine Rechtsberatung.
   ===================================================================== */
require_once __DIR__ . '/functions.php';

$ic_back = '<svg class="svg-ic" viewBox="0 0 24 24"><path d="M19 12H5M12 5l-7 7 7 7"/></svg>';
// Sprache wie auf den übrigen Seiten: ?lang=en bzw. die in der Sitzung gewählte Sprache
$en = (isset($lang) && $lang === 'en') || (isset($_GET['lang']) && $_GET['lang'] === 'en')
    || stripos((string)t('privacy'), 'privacy') !== false;
$page_title = $en ? 'Imprint' : 'Impressum';
require __DIR__ . '/header.php';

$name = trim((string)IMP_NAME);
$adresse = '<br>' . h(IMP_STREET) . '<br>' . h(IMP_CITY) . '<br>' . ($en ? 'Germany' : 'Deutschland');
?>
<div class="wrap-narrow section legal">
  <a class="pill-link" style="display:inline-flex;align-items:center;gap:6px;margin-bottom:18px;" href="<?= h(url('index.php')) ?>"><?= $ic_back ?> <?= h(t('pub_back_home')) ?></a>
<?php if ($en): ?>
  <h1>Imprint</h1>
  <p class="muted">Information pursuant to Section 5 of the German Digital Services Act (DDG)</p>

  <h2>Provider</h2>
  <p><?= h($name) ?><?= $adresse ?></p>

  <h2>Contact</h2>
  <p>
    E-mail: <a href="mailto:<?= h(IMP_EMAIL) ?>"><?= h(IMP_EMAIL) ?></a>
    <?php if (IMP_PHONE): ?><br>Phone: <?= h(IMP_PHONE) ?><?php endif; ?>
  </p>

  <h2>Responsible for the content</h2>
  <p>Pursuant to Section 18(2) of the German Interstate Media Treaty (MStV):<br><?= h($name) ?><?= $adresse ?></p>

  <h2>TrackMaster Pro software</h2>
  <p>The TrackMaster&nbsp;Pro track planner (web version, Windows and Android programs) and its licences are provided by the provider named above. Questions about licences and activation: <a href="mailto:<?= h(IMP_EMAIL) ?>"><?= h(IMP_EMAIL) ?></a>.</p>

  <h2>Consumer dispute resolution</h2>
  <p>I am neither willing nor obliged to take part in dispute resolution proceedings before a consumer arbitration board.</p>

  <h2>Liability for content</h2>
  <p>The content of these pages was created with great care. However, no guarantee can be given for the accuracy, completeness and timeliness of the content. This website is a private, non-commercial hobby project about a model railway collection in N scale.</p>

  <h2>Liability for links</h2>
  <p>This website contains links to external websites over whose content I have no influence. The respective provider or operator is always responsible for the content of linked pages. The linked pages were checked for possible legal violations at the time of linking; if I become aware of any infringements, I will remove such links immediately.</p>

  <h2>Copyright</h2>
  <p>The texts, photos, graphics and software on this website are subject to German copyright law. Any reproduction, editing or distribution beyond the limits of copyright law requires the prior written consent of the author. Product names and trademarks of model railway manufacturers belong to their respective owners and are used for identification only.</p>
<?php else: ?>
  <h1>Impressum</h1>
  <p class="muted">Angaben gemäß § 5 Digitale-Dienste-Gesetz (DDG)</p>

  <h2>Anbieter</h2>
  <p><?= h($name) ?><?= $adresse ?></p>

  <h2>Kontakt</h2>
  <p>
    E-Mail: <a href="mailto:<?= h(IMP_EMAIL) ?>"><?= h(IMP_EMAIL) ?></a>
    <?php if (IMP_PHONE): ?><br>Telefon: <?= h(IMP_PHONE) ?><?php endif; ?>
  </p>

  <h2>Verantwortlich für den Inhalt</h2>
  <p>nach § 18 Abs.&nbsp;2 Medienstaatsvertrag (MStV):<br><?= h($name) ?><?= $adresse ?></p>

  <h2>Software TrackMaster Pro</h2>
  <p>Der Gleisplaner TrackMaster&nbsp;Pro (Webversion sowie Programme für Windows und Android) und seine Lizenzen werden vom oben genannten Anbieter bereitgestellt. Fragen zu Lizenzen und Aktivierung: <a href="mailto:<?= h(IMP_EMAIL) ?>"><?= h(IMP_EMAIL) ?></a>.</p>

  <h2>Verbraucherstreitbeilegung</h2>
  <p>Ich bin nicht bereit und nicht verpflichtet, an Streitbeilegungsverfahren vor einer Verbraucherschlichtungsstelle teilzunehmen.</p>

  <h2>Haftung für Inhalte</h2>
  <p>Die Inhalte dieser Seiten wurden mit größter Sorgfalt erstellt. Für die Richtigkeit, Vollständigkeit und Aktualität der Inhalte kann ich jedoch keine Gewähr übernehmen. Diese Website ist ein privates, nicht kommerzielles Hobbyprojekt zu einer Modelleisenbahn-Sammlung in Spur&nbsp;N.</p>

  <h2>Haftung für Links</h2>
  <p>Diese Website enthält Links zu externen Websites, auf deren Inhalte ich keinen Einfluss habe. Für die Inhalte der verlinkten Seiten ist stets der jeweilige Anbieter oder Betreiber verantwortlich. Die verlinkten Seiten wurden zum Zeitpunkt der Verlinkung auf mögliche Rechtsverstöße überprüft; werden mir Rechtsverletzungen bekannt, entferne ich derartige Links umgehend.</p>

  <h2>Urheberrecht</h2>
  <p>Die Texte, Fotos, Grafiken und die Software auf dieser Website unterliegen dem deutschen Urheberrecht. Vervielfältigung, Bearbeitung und Verbreitung außerhalb der Grenzen des Urheberrechts bedürfen der vorherigen schriftlichen Zustimmung des Urhebers. Produktnamen und Marken der Modellbahnhersteller gehören ihren jeweiligen Inhabern und dienen nur der Bezeichnung.</p>
<?php endif; ?>
</div>
<?php require __DIR__ . '/footer.php'; ?>

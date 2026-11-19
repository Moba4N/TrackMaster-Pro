<?php
/* =====================================================================
   JörgsMoba4N – Kündigung des TrackMaster-Pro-Abos („Verträge hier kündigen“, § 312k BGB)
   Entwurf – vor Verkaufsstart prüfen lassen. Schritt 1: Formular, Schritt 2: Bestätigungsknopf „jetzt kündigen“,
   danach Bestätigung auf der Seite und per E-Mail an Kunden und Anbieter (IMP_EMAIL).
   ===================================================================== */
require_once __DIR__ . '/functions.php';

$page_title = 'TrackMaster Pro – Abo kündigen';

$felder = ['name' => '', 'email' => '', 'lizenz' => '', 'zeitpunkt' => 'naechstmoeglich', 'datum' => '', 'grund' => ''];
foreach ($felder as $k => $v) $felder[$k] = trim((string)($_POST[$k] ?? $v));
$schritt = $_POST['schritt'] ?? '';
$fehler = [];
if ($schritt !== '') {
    if ($felder['name'] === '') $fehler[] = 'Bitte Ihren Namen angeben.';
    if (!filter_var($felder['email'], FILTER_VALIDATE_EMAIL)) $fehler[] = 'Bitte eine gültige E-Mail-Adresse angeben.';
    if ($felder['zeitpunkt'] === 'datum' && !preg_match('/^\d{4}-\d{2}-\d{2}$/', $felder['datum'])) $fehler[] = 'Bitte das gewünschte Datum angeben.';
    foreach ($felder as $k => $v) $felder[$k] = mb_substr(preg_replace('/[\x00-\x09\x0B-\x1F\x7F]/u', '', $v) ?? '', 0, $k === 'grund' ? 1000 : 120);
}
$gesendet = false;
$zeit = date('d.m.Y H:i');
if ($schritt === 'kuendigen' && !$fehler) {
    $wann = $felder['zeitpunkt'] === 'datum' ? 'zum ' . date('d.m.Y', strtotime($felder['datum'])) : 'zum nächstmöglichen Zeitpunkt (Ende des laufenden Monats)';
    $text = "Kündigung des TrackMaster-Pro-Abos\n\n"
        . "Eingegangen am: $zeit Uhr\n"
        . "Name: {$felder['name']}\n"
        . "E-Mail: {$felder['email']}\n"
        . "Lizenznummer: " . ($felder['lizenz'] !== '' ? $felder['lizenz'] : '–') . "\n"
        . "Kündigung: ordentlich, $wann\n"
        . ($felder['grund'] !== '' ? "Anmerkung: {$felder['grund']}\n" : '')
        . "\nDiese E-Mail bestätigt den Eingang Ihrer Kündigung. Das Abo endet zum genannten Zeitpunkt; bereits bezahlte Monate bleiben nutzbar.\n";
    $kopf = 'From: ' . IMP_EMAIL . "\r\nReply-To: " . IMP_EMAIL . "\r\nContent-Type: text/plain; charset=UTF-8";
    $betreff = '=?UTF-8?B?' . base64_encode('Kündigung TrackMaster-Pro-Abo') . '?=';
    $gesendet = @mail($felder['email'], $betreff, $text, $kopf);
    @mail(IMP_EMAIL, $betreff, $text, "Content-Type: text/plain; charset=UTF-8\r\nReply-To: " . $felder['email']);
}
require __DIR__ . '/header.php';
?>
<div class="wrap-narrow section legal">
  <h1>Verträge hier kündigen</h1>
<?php if ($schritt === 'kuendigen' && !$fehler): ?>
  <p><strong>Ihre Kündigung ist am <?= h($zeit) ?>&nbsp;Uhr eingegangen.</strong></p>
  <p>Das Pro-Abo von <?= h($felder['name']) ?> endet <?= $felder['zeitpunkt'] === 'datum' ? 'zum ' . h(date('d.m.Y', strtotime($felder['datum']))) : 'zum Ende des laufenden Monats' ?>. Bereits bezahlte Monate bleiben nutzbar.
  <?= $gesendet ? 'Eine Bestätigung wurde an ' . h($felder['email']) . ' geschickt.' : 'Bitte speichern oder drucken Sie diese Seite als Bestätigung.' ?></p>
  <p>Wenn Sie per PayPal bezahlen, beenden Sie bitte zusätzlich das Abonnement in Ihrem PayPal-Konto, damit keine weitere Zahlung abgebucht wird.</p>
<?php elseif ($schritt === 'pruefen' && !$fehler): ?>
  <p>Bitte prüfen Sie Ihre Angaben und bestätigen Sie die Kündigung.</p>
  <ul>
    <li>Name: <?= h($felder['name']) ?></li>
    <li>E-Mail: <?= h($felder['email']) ?></li>
    <li>Lizenznummer: <?= $felder['lizenz'] !== '' ? h($felder['lizenz']) : '–' ?></li>
    <li>Kündigung: ordentlich, <?= $felder['zeitpunkt'] === 'datum' ? 'zum ' . h(date('d.m.Y', strtotime($felder['datum']))) : 'zum nächstmöglichen Zeitpunkt' ?></li>
  </ul>
  <form method="post">
    <?php foreach ($felder as $k => $v): ?><input type="hidden" name="<?= h($k) ?>" value="<?= h($v) ?>"><?php endforeach; ?>
    <button type="submit" name="schritt" value="kuendigen" style="padding:10px 18px;font-weight:600;">jetzt kündigen</button>
    <button type="submit" name="schritt" value="" style="margin-left:8px;">Angaben ändern</button>
  </form>
<?php else: ?>
  <p>Hier kündigen Sie Ihr TrackMaster-Pro-Abo. Eine Kauflizenz (einmalige Zahlung) muss nicht gekündigt werden.</p>
  <?php if ($fehler): ?><p style="color:#B3261E"><?= implode('<br>', array_map('h', $fehler)) ?></p><?php endif; ?>
  <form method="post" style="display:grid;gap:10px;max-width:480px;">
    <label>Name *<br><input type="text" name="name" value="<?= h($felder['name']) ?>" required style="width:100%"></label>
    <label>E-Mail-Adresse für die Bestätigung *<br><input type="email" name="email" value="<?= h($felder['email']) ?>" required style="width:100%"></label>
    <label>Lizenznummer (falls zur Hand; steht im Lizenzfenster des Programms)<br><input type="text" name="lizenz" value="<?= h($felder['lizenz']) ?>" style="width:100%"></label>
    <fieldset style="border:0;padding:0;">
      <legend>Kündigungszeitpunkt</legend>
      <label><input type="radio" name="zeitpunkt" value="naechstmoeglich" <?= $felder['zeitpunkt'] !== 'datum' ? 'checked' : '' ?>> zum nächstmöglichen Zeitpunkt</label><br>
      <label><input type="radio" name="zeitpunkt" value="datum" <?= $felder['zeitpunkt'] === 'datum' ? 'checked' : '' ?>> zum Datum <input type="date" name="datum" value="<?= h($felder['datum']) ?>"></label>
    </fieldset>
    <label>Anmerkung (optional)<br><textarea name="grund" rows="3" style="width:100%"><?= h($felder['grund']) ?></textarea></label>
    <div><button type="submit" name="schritt" value="pruefen" style="padding:10px 18px;">Weiter zur Bestätigung</button></div>
  </form>
<?php endif; ?>
</div>
<?php require __DIR__ . '/footer.php'; ?>

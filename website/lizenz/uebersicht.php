<?php
// TrackMaster Pro – Übersicht der Lizenz-Aktivierungen (mit Passwort aus einstellungen.php).
declare(strict_types=1);
require __DIR__ . '/gemeinsam.php';

session_start();
header('Cache-Control: no-store');
header('X-Frame-Options: SAMEORIGIN');

$e = tm_einstellungen();
$passwortOk = is_string($e['passwort']) && $e['passwort'] !== '' && $e['passwort'] !== 'bitte-aendern';
if (empty($_SESSION['tm_csrf'])) $_SESSION['tm_csrf'] = bin2hex(random_bytes(16));
$csrf = $_SESSION['tm_csrf'];
$meldung = '';

if (($_SERVER['REQUEST_METHOD'] ?? '') === 'POST') {
    $aktion = $_POST['aktion'] ?? '';
    if ($aktion === 'anmelden') {
        if ($passwortOk && hash_equals((string)$e['passwort'], (string)($_POST['passwort'] ?? ''))) {
            session_regenerate_id(true);
            $_SESSION['tm_admin'] = true;
        } else {
            sleep(1);
            $meldung = 'Passwort falsch.';
        }
    } elseif (!empty($_SESSION['tm_admin']) && hash_equals($csrf, (string)($_POST['csrf'] ?? ''))) {
        $id = (string)($_POST['lizenz'] ?? '');
        $geraet = (string)($_POST['geraet'] ?? '');
        if ($aktion === 'abmelden') {
            $_SESSION = [];
            session_destroy();
            header('Location: uebersicht.php');
            exit;
        }
        tm_daten_aendern(function (array &$daten) use ($aktion, $id, $geraet, &$meldung) {
            if (!isset($daten[$id])) return;
            switch ($aktion) {
                case 'entfernen':
                    unset($daten[$id]['geraete'][$geraet]);
                    $meldung = "Gerät bei $id entfernt.";
                    break;
                case 'sperren':
                    $daten[$id]['gesperrt'] = empty($daten[$id]['gesperrt']);
                    $meldung = $daten[$id]['gesperrt'] ? "Lizenz $id gesperrt." : "Lizenz $id entsperrt.";
                    break;
                case 'max':
                    $n = (int)($_POST['max'] ?? 0);
                    if ($n > 0) $daten[$id]['max'] = min($n, 50); else unset($daten[$id]['max']);
                    $meldung = "Gerätezahl für $id geändert.";
                    break;
                case 'loeschen':
                    // Auskunfts- oder Löschwunsch des Lizenznehmers (Art. 17 DSGVO): ganzen Eintrag entfernen
                    unset($daten[$id]);
                    $meldung = "Alle Aktivierungsdaten zu $id gelöscht.";
                    break;
                case 'versuche':
                    $daten[$id]['abgelehnt'] = [];
                    $meldung = "Abgelehnte Versuche bei $id gelöscht.";
                    break;
            }
        });
        $_SESSION['tm_meldung'] = $meldung;
        header('Location: uebersicht.php');
        exit;
    }
}
if (isset($_SESSION['tm_meldung'])) { $meldung = $_SESSION['tm_meldung']; unset($_SESSION['tm_meldung']); }

function zeit(?string $iso): string
{
    if (!$iso) return '–';
    $t = strtotime($iso);
    return $t ? date('d.m.Y H:i', $t) : h($iso);
}
?><!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>Lizenz-Aktivierungen · TrackMaster Pro</title>
<style>
  :root { --gruen: #437E1E; --rot: #B3261E; --grau: #666; }
  body { margin: 0; font-family: system-ui, "Segoe UI", Roboto, sans-serif; background: #f4f4f2; color: #1d1d1d; }
  main { max-width: 1000px; margin: 0 auto; padding: 16px; }
  h1 { font-size: 20px; margin: 8px 0 16px; }
  .karte { background: #fff; border: 1px solid #e3e3e3; border-radius: 8px; padding: 12px 14px; margin-bottom: 12px; }
  .warn { border-color: var(--rot); }
  .kopf { display: flex; flex-wrap: wrap; gap: 8px 16px; align-items: baseline; }
  .kopf strong { font-size: 15px; }
  .klein { color: var(--grau); font-size: 13px; }
  .marke { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 12px; font-weight: 600; color: #fff; background: var(--gruen); }
  .marke.rot { background: var(--rot); }
  .marke.grau { background: var(--grau); }
  table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 13px; }
  th, td { text-align: left; padding: 5px 6px; border-top: 1px solid #eee; vertical-align: top; }
  th { color: var(--grau); font-weight: 600; }
  form.inline { display: inline; }
  button { font: inherit; font-size: 12px; padding: 3px 8px; border-radius: 5px; border: 1px solid #ccc; background: #fafafa; cursor: pointer; }
  button:hover { background: #eee; }
  input[type=number] { width: 4em; font: inherit; font-size: 12px; }
  .meldung { background: #E8F3DD; border-radius: 6px; padding: 8px 12px; margin-bottom: 12px; }
  .anmelden { max-width: 340px; margin: 60px auto; }
  .anmelden input { width: 100%; box-sizing: border-box; padding: 8px; margin: 8px 0; font: inherit; }
  .werkzeuge { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
  @media (max-width: 600px) { th:nth-child(4), td:nth-child(4) { display: none; } }
</style>
</head>
<body>
<main>
<?php if (empty($_SESSION['tm_admin'])): ?>
  <div class="karte anmelden">
    <h1>Lizenz-Aktivierungen</h1>
    <?php if (!$passwortOk): ?>
      <p>Bitte zuerst in <code>lizenz/einstellungen.php</code> ein eigenes Passwort eintragen (statt <code>bitte-aendern</code>).</p>
    <?php else: ?>
      <?php if ($meldung): ?><div class="meldung"><?= h($meldung) ?></div><?php endif; ?>
      <form method="post">
        <input type="hidden" name="aktion" value="anmelden">
        <label>Passwort<input type="password" name="passwort" autofocus autocomplete="current-password"></label>
        <button type="submit">Anmelden</button>
      </form>
    <?php endif; ?>
  </div>
<?php else:
    tm_daten_aendern(function (array &$d) {});   // abgelaufene Einträge nach den Speicherfristen löschen
    $daten = tm_daten_lesen();
    uksort($daten, function ($a, $b) use ($daten) {
        $wa = count($daten[$a]['abgelehnt'] ?? []); $wb = count($daten[$b]['abgelehnt'] ?? []);
        return $wb <=> $wa ?: strcmp($a, $b);
    });
    $mehrfach = count(array_filter($daten, fn($x) => !empty($x['abgelehnt'])));
?>
  <div class="kopf">
    <h1>Lizenz-Aktivierungen</h1>
    <span class="klein"><?= count($daten) ?> Lizenzen · Standard <?= (int)$e['max_geraete'] ?> Geräte je Lizenz</span>
    <form method="post" class="inline" style="margin-left:auto"><input type="hidden" name="csrf" value="<?= h($csrf) ?>"><button name="aktion" value="abmelden">Abmelden</button></form>
  </div>
  <?php if ($meldung): ?><div class="meldung"><?= h($meldung) ?></div><?php endif; ?>
  <?php if ($mehrfach): ?><div class="karte warn"><strong><?= $mehrfach ?> Lizenz(en) mit abgelehnten Aktivierungen</strong> – dort wurde versucht, die Lizenz auf mehr Geräten zu nutzen als erlaubt. Sie stehen oben.</div><?php endif; ?>
  <?php if (!$daten): ?><div class="karte">Noch keine Aktivierungen.</div><?php endif; ?>
  <?php foreach ($daten as $id => $x):
      $max = tm_max_geraete((string)$id, $x);
      $geraete = $x['geraete'] ?? [];
      $abgelehnt = $x['abgelehnt'] ?? [];
  ?>
  <div class="karte<?= $abgelehnt ? ' warn' : '' ?>">
    <div class="kopf">
      <strong><?= h($x['inhaber'] ?? '') ?: '–' ?></strong>
      <span class="klein"><?= h((string)$id) ?></span>
      <span class="marke<?= count($geraete) >= $max ? ' grau' : '' ?>"><?= count($geraete) ?> von <?= $max ?> Geräten</span>
      <?php if (!empty($x['gesperrt'])): ?><span class="marke rot">gesperrt</span><?php endif; ?>
      <?php if ($abgelehnt): ?><span class="marke rot"><?= count($abgelehnt) ?> abgelehnt</span><?php endif; ?>
    </div>
    <table>
      <tr><th>Gerät</th><th>Plattform</th><th>Zuletzt</th><th>Seit</th><th></th></tr>
      <?php foreach ($geraete as $gid => $g): ?>
      <tr>
        <td><?= h($g['name'] ?? '') ?: '–' ?><div class="klein"><?= h((string)$gid) ?><?= !empty($g['version']) ? ' · Version ' . h($g['version']) : '' ?></div></td>
        <td><?= h($g['plattform'] ?? '') ?></td>
        <td><?= zeit($g['zuletzt'] ?? null) ?></td>
        <td><?= zeit($g['erst'] ?? null) ?></td>
        <td><form method="post" class="inline" onsubmit="return confirm('Gerät entfernen? Es läuft dann dort die Basisversion, bis es wieder aktiviert wird.')">
          <input type="hidden" name="csrf" value="<?= h($csrf) ?>"><input type="hidden" name="lizenz" value="<?= h((string)$id) ?>"><input type="hidden" name="geraet" value="<?= h((string)$gid) ?>">
          <button name="aktion" value="entfernen">Entfernen</button></form></td>
      </tr>
      <?php endforeach; ?>
      <?php if (!$geraete): ?><tr><td colspan="5" class="klein">Kein Gerät aktiviert.</td></tr><?php endif; ?>
    </table>
    <?php if ($abgelehnt): ?>
      <table>
        <tr><th>Abgelehnt am</th><th>Gerät</th><th>Plattform</th><th></th><th></th></tr>
        <?php foreach (array_slice($abgelehnt, 0, 10) as $v): ?>
        <tr><td><?= zeit($v['zeit'] ?? null) ?></td><td><?= h($v['name'] ?? '') ?><div class="klein"><?= h($v['geraet'] ?? '') ?></div></td><td><?= h($v['plattform'] ?? '') ?></td><td></td><td></td></tr>
        <?php endforeach; ?>
      </table>
    <?php endif; ?>
    <div class="werkzeuge">
      <form method="post" class="inline"><input type="hidden" name="csrf" value="<?= h($csrf) ?>"><input type="hidden" name="lizenz" value="<?= h((string)$id) ?>">
        <button name="aktion" value="sperren"><?= !empty($x['gesperrt']) ? 'Entsperren' : 'Sperren' ?></button></form>
      <form method="post" class="inline"><input type="hidden" name="csrf" value="<?= h($csrf) ?>"><input type="hidden" name="lizenz" value="<?= h((string)$id) ?>">
        Geräte: <input type="number" name="max" min="0" max="50" value="<?= isset($x['max']) ? (int)$x['max'] : '' ?>" placeholder="<?= (int)$e['max_geraete'] ?>">
        <button name="aktion" value="max">Setzen</button></form>
      <?php if ($abgelehnt): ?>
      <form method="post" class="inline"><input type="hidden" name="csrf" value="<?= h($csrf) ?>"><input type="hidden" name="lizenz" value="<?= h((string)$id) ?>">
        <button name="aktion" value="versuche">Abgelehnte Versuche löschen</button></form>
      <?php endif; ?>
      <form method="post" class="inline" onsubmit="return confirm('Alle gespeicherten Aktivierungsdaten dieser Lizenz löschen (z. B. auf Wunsch des Lizenznehmers)? Aktive Geräte melden sich bei der nächsten Prüfung neu an.')">
        <input type="hidden" name="csrf" value="<?= h($csrf) ?>"><input type="hidden" name="lizenz" value="<?= h((string)$id) ?>">
        <button name="aktion" value="loeschen">Daten löschen</button></form>
    </div>
  </div>
  <?php endforeach; ?>
<?php endif; ?>
</main>
</body>
</html>

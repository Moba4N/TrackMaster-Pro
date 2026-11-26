<?php
/* =====================================================================
   JörgsMoba4N – Download TrackMaster Pro (Windows, Android, Handbuch)
   Die Links zeigen immer auf die neueste Version: GitHub stellt unter
   .../releases/latest/download/<Datei> automatisch die Datei aus dem neuesten Release bereit.
   Bei einem Update ist auf der Webseite also nichts zu tun.
   Die Versionsnummer kommt aus version.json des neuesten Releases (eine Stunde zwischengespeichert);
   ist GitHub nicht erreichbar, erscheint die Seite einfach ohne Nummer.
   ===================================================================== */
require_once __DIR__ . '/functions.php';

const TM_REPO = 'Moba4N/TrackMaster-Pro';
const TM_LATEST = 'https://github.com/' . TM_REPO . '/releases/latest/download/';

/** Neueste Version aus version.json, oder null. */
function tm_neueste_version(): ?string
{
    $cache = sys_get_temp_dir() . '/trackmaster-version.json';
    $json = null;
    if (is_file($cache) && filemtime($cache) > time() - 3600) {
        $json = @file_get_contents($cache);
    } else {
        if (function_exists('curl_init')) {
            $c = curl_init(TM_LATEST . 'version.json');
            curl_setopt_array($c, [CURLOPT_RETURNTRANSFER => true, CURLOPT_FOLLOWLOCATION => true, CURLOPT_TIMEOUT => 3,
                CURLOPT_USERAGENT => 'TrackMasterPro-Downloadseite']);
            $r = curl_exec($c);
            if (is_string($r) && curl_getinfo($c, CURLINFO_HTTP_CODE) === 200) $json = $r;
            curl_close($c);
        } elseif (ini_get('allow_url_fopen')) {
            $ctx = stream_context_create(['http' => ['timeout' => 3, 'user_agent' => 'TrackMasterPro-Downloadseite']]);
            $r = @file_get_contents(TM_LATEST . 'version.json', false, $ctx);
            if (is_string($r)) $json = $r;
        }
        if ($json !== null) @file_put_contents($cache, $json);
        elseif (is_file($cache)) $json = @file_get_contents($cache);   // lieber alte Nummer als keine
    }
    $d = is_string($json) ? json_decode($json, true) : null;
    $v = is_array($d) ? (string)($d['version'] ?? '') : '';
    return preg_match('/^\d+\.\d+(\.\d+)?$/', $v) ? $v : null;
}

$en = (isset($lang) && $lang === 'en') || (isset($_GET['lang']) && $_GET['lang'] === 'en');
$page_title = $en ? 'Download TrackMaster Pro' : 'TrackMaster Pro herunterladen';
require __DIR__ . '/header.php';

$version = tm_neueste_version();
$q = $en ? '?lang=en' : '';
$T = $en ? [
    'h1' => 'Download TrackMaster Pro',
    'intro' => 'The basic version is free. A Pro licence unlocks saving large plans and all outputs; the same licence file works in the Windows program, the Android app and the web version.',
    'ver' => 'Current version',
    'win' => 'Windows', 'win_txt' => 'Installer for Windows 10 and 11 (64-bit). An update is installed the same way; your plans and licence are kept.',
    'win_btn' => 'Download for Windows (MSI)',
    'and' => 'Android', 'and_txt' => 'App for Android 8.0 or later, ideally a tablet. On first install Android asks whether to allow installing apps from this source.',
    'and_btn' => 'Download for Android (APK)',
    'web' => 'Web version', 'web_txt' => 'Runs directly in the browser, no installation. Always up to date.',
    'web_btn' => 'Open planner',
    'hb' => 'Manual', 'hb_txt' => 'Complete manual as PDF, in German and English.',
    'hb_btn' => 'To the manual',
    'upd_h' => 'Updates',
    'upd' => 'The Windows program and the Android app check at start-up whether a new version is available and show a notice. Under File › Check for updates you can check yourself; the link leads to this page. Nothing is installed automatically.',
    'all' => 'Older versions and release notes',
] : [
    'h1' => 'TrackMaster Pro herunterladen',
    'intro' => 'Die Basisversion ist kostenlos. Eine Pro-Lizenz schaltet das Speichern großer Pläne und alle Ausgaben frei; dieselbe Lizenzdatei gilt im Windows-Programm, in der Android-App und in der Webversion.',
    'ver' => 'Aktuelle Version',
    'win' => 'Windows', 'win_txt' => 'Installationsprogramm für Windows 10 und 11 (64 Bit). Ein Update installieren Sie genauso; Pläne und Lizenz bleiben erhalten.',
    'win_btn' => 'Für Windows herunterladen (MSI)',
    'and' => 'Android', 'and_txt' => 'App für Android ab 8.0, am besten auf einem Tablet. Beim ersten Mal fragt Android, ob Apps aus dieser Quelle installiert werden dürfen.',
    'and_btn' => 'Für Android herunterladen (APK)',
    'web' => 'Webversion', 'web_txt' => 'Läuft direkt im Browser, ohne Installation. Immer auf dem neuesten Stand.',
    'web_btn' => 'Planer öffnen',
    'hb' => 'Handbuch', 'hb_txt' => 'Vollständiges Handbuch als PDF, auf Deutsch und Englisch.',
    'hb_btn' => 'Zum Handbuch',
    'upd_h' => 'Updates',
    'upd' => 'Das Windows-Programm und die Android-App prüfen beim Start, ob es eine neue Version gibt, und zeigen dann einen Hinweis. Unter Datei › Nach Updates suchen prüfen Sie selbst; der Link führt auf diese Seite. Installiert wird nie von selbst.',
    'all' => 'Ältere Versionen und Änderungen',
];
$karten = [
    ['h' => $T['win'], 'txt' => $T['win_txt'], 'btn' => $T['win_btn'], 'href' => TM_LATEST . 'TrackMasterPro-Setup.msi', 'dl' => true],
    ['h' => $T['and'], 'txt' => $T['and_txt'], 'btn' => $T['and_btn'], 'href' => TM_LATEST . 'TrackMasterPro.apk', 'dl' => true],
    ['h' => $T['web'], 'txt' => $T['web_txt'], 'btn' => $T['web_btn'], 'href' => url('planer.php') . $q, 'dl' => false],
    ['h' => $T['hb'], 'txt' => $T['hb_txt'], 'btn' => $T['hb_btn'], 'href' => url('handbuch.php') . $q, 'dl' => false],
];
?>
<style>
  .tm-dl { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; margin: 24px 0; }
  .tm-dl .karte { border: 1px solid rgba(0,0,0,.14); border-radius: 10px; padding: 18px; display: flex; flex-direction: column; gap: 10px; }
  .tm-dl .karte h2 { margin: 0; font-size: 1.15em; }
  .tm-dl .karte p { margin: 0; flex: 1; }
  .tm-dl .knopf { display: block; text-align: center; padding: 10px 14px; border-radius: 6px; background: #2e6b45; color: #fff; text-decoration: none; }
  .tm-dl .knopf:hover { background: #245638; }
  .tm-ver { display: inline-block; padding: 3px 10px; border-radius: 999px; background: #eef5f0; color: #245638; font-size: .92em; }
</style>
<div class="wrap-narrow section">
  <h1><?= h($T['h1']) ?></h1>
  <?php if ($version): ?><p><span class="tm-ver"><?= h($T['ver']) ?>: <?= h($version) ?></span></p><?php endif; ?>
  <p><?= h($T['intro']) ?></p>
  <div class="tm-dl">
<?php foreach ($karten as $k): ?>
    <div class="karte">
      <h2><?= h($k['h']) ?></h2>
      <p class="muted"><?= h($k['txt']) ?></p>
      <a class="knopf" href="<?= h($k['href']) ?>"<?= $k['dl'] ? ' rel="nofollow"' : '' ?>><?= h($k['btn']) ?></a>
    </div>
<?php endforeach; ?>
  </div>
  <h2><?= h($T['upd_h']) ?></h2>
  <p><?= h($T['upd']) ?></p>
  <p><a href="https://github.com/<?= TM_REPO ?>/releases" rel="noopener"><?= h($T['all']) ?></a></p>
</div>
<?php require __DIR__ . '/footer.php'; ?>

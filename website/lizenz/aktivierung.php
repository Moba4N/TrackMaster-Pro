<?php
// TrackMaster Pro – Online-Aktivierung der Lizenzen.
// Die Programme (Windows, Android, Webversion) melden hier ihr Gerät an. Je Lizenz sind höchstens
// 'max_geraete' Geräte erlaubt (einstellungen.php). Gespeichert wird in daten/aktivierungen.json.
//
// POST action=activate|release, license=<Inhalt der Lizenzdatei>, licenseId, device, platform, name, version
// Antwort (JSON): { "status": "active|released|limit|blocked|invalid", "devices": n, "max": m, "message": "…" }

declare(strict_types=1);
require __DIR__ . '/gemeinsam.php';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');

function antwort(array $a, int $code = 200): void
{
    http_response_code($code);
    echo json_encode($a, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    antwort(['status' => 'invalid', 'message' => 'Nur POST.'], 405);
}

$aktion = $_POST['action'] ?? '';
$lizenzText = (string)($_POST['license'] ?? '');
$geraet = tm_text((string)($_POST['device'] ?? ''), 64);
$plattform = tm_text((string)($_POST['platform'] ?? ''), 20);
$name = tm_text((string)($_POST['name'] ?? ''), 80);
$version = tm_text((string)($_POST['version'] ?? ''), 20);

if (!in_array($aktion, ['activate', 'release'], true) || $geraet === '' || strlen($lizenzText) > 20000) {
    antwort(['status' => 'invalid', 'message' => 'Ungültige Anfrage.'], 400);
}
$lizenz = tm_lizenz_pruefen($lizenzText);
if ($lizenz === null) {
    antwort(['status' => 'invalid', 'message' => 'Die Lizenz wurde vom Aktivierungsserver nicht anerkannt.']);
}
$id = tm_text((string)$lizenz['licenseId'], 80);
$jetzt = gmdate('c');

try {
    $ergebnis = tm_daten_aendern(function (array &$daten) use ($aktion, $id, $lizenz, $geraet, $plattform, $name, $version, $jetzt) {
        $e = $daten[$id] ?? ['inhaber' => '', 'email' => '', 'geraete' => [], 'abgelehnt' => [], 'gesperrt' => false];
        $e['inhaber'] = tm_text((string)($lizenz['licensee'] ?? ''), 120);
        $e['email'] = tm_text((string)($lizenz['email'] ?? ''), 120);
        $max = tm_max_geraete($id, $e);

        if ($aktion === 'release') {
            unset($e['geraete'][$geraet]);
            $daten[$id] = $e;
            return ['status' => 'released', 'devices' => count($e['geraete']), 'max' => $max];
        }

        if (!empty($e['gesperrt'])) {
            $daten[$id] = $e;
            return ['status' => 'blocked', 'devices' => count($e['geraete']), 'max' => $max,
                'message' => 'Diese Lizenz wurde vom Hersteller gesperrt.'];
        }

        if (isset($e['geraete'][$geraet])) {
            $e['geraete'][$geraet] = array_merge($e['geraete'][$geraet], ['plattform' => $plattform, 'name' => $name, 'version' => $version, 'zuletzt' => $jetzt]);
        } elseif (count($e['geraete']) < $max) {
            $e['geraete'][$geraet] = ['plattform' => $plattform, 'name' => $name, 'version' => $version, 'erst' => $jetzt, 'zuletzt' => $jetzt];
        } else {
            // Mehrfachnutzung: festhalten, damit sie in der Übersicht auffällt
            array_unshift($e['abgelehnt'], ['zeit' => $jetzt, 'geraet' => $geraet, 'plattform' => $plattform, 'name' => $name]);
            $e['abgelehnt'] = array_slice($e['abgelehnt'], 0, 30);
            $daten[$id] = $e;
            $belegt = [];
            foreach ($e['geraete'] as $g) $belegt[] = trim(($g['plattform'] ?? '') . ' ' . ($g['name'] ?? ''));
            return ['status' => 'limit', 'devices' => count($e['geraete']), 'max' => $max,
                'message' => "Diese Lizenz ist schon auf $max Geräten aktiviert (" . implode(', ', $belegt) . "). "
                    . 'Auf einem davon im Lizenzdialog „Dieses Gerät freigeben“ wählen, dann hier „Jetzt aktivieren“.'];
        }
        $daten[$id] = $e;
        return ['status' => 'active', 'devices' => count($e['geraete']), 'max' => $max];
    });
} catch (Throwable $t) {
    antwort(['status' => 'error', 'message' => 'Aktivierungsserver: ' . $t->getMessage()], 500);
}
antwort($ergebnis);

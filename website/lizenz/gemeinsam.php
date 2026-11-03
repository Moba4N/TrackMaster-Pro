<?php
// Gemeinsame Funktionen für aktivierung.php und uebersicht.php

date_default_timezone_set('Europe/Berlin');

function tm_einstellungen(): array
{
    static $e = null;
    if ($e === null) {
        $e = require __DIR__ . '/einstellungen.php';
        $e += ['max_geraete' => 2, 'ausnahmen' => [], 'passwort' => ''];
    }
    return $e;
}

function tm_max_geraete(string $lizenz, array $eintrag = []): int
{
    $e = tm_einstellungen();
    if (isset($eintrag['max']) && (int)$eintrag['max'] > 0) return (int)$eintrag['max'];
    if (isset($e['ausnahmen'][$lizenz])) return max(1, (int)$e['ausnahmen'][$lizenz]);
    return max(1, (int)$e['max_geraete']);
}

/** Öffnet die Datendatei gesperrt, ruft $aendern mit den Daten auf und speichert das Ergebnis. */
function tm_daten_aendern(callable $aendern)
{
    $pfad = __DIR__ . '/daten/aktivierungen.json';
    $f = fopen($pfad, 'c+');
    if ($f === false) throw new RuntimeException('Datendatei lässt sich nicht öffnen (Schreibrechte für lizenz/daten/ prüfen).');
    try {
        flock($f, LOCK_EX);
        $inhalt = stream_get_contents($f);
        $daten = $inhalt ? json_decode($inhalt, true) : [];
        if (!is_array($daten)) $daten = [];
        $ergebnis = $aendern($daten);
        rewind($f);
        ftruncate($f, 0);
        fwrite($f, json_encode($daten, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
        fflush($f);
        return $ergebnis;
    } finally {
        flock($f, LOCK_UN);
        fclose($f);
    }
}

function tm_daten_lesen(): array
{
    $pfad = __DIR__ . '/daten/aktivierungen.json';
    if (!is_file($pfad)) return [];
    $daten = json_decode((string)file_get_contents($pfad), true);
    return is_array($daten) ? $daten : [];
}

/** Prüft die Signatur einer Lizenzdatei mit dem öffentlichen Schlüssel; liefert die Lizenzdaten oder null. */
function tm_lizenz_pruefen(string $text): ?array
{
    $datei = json_decode($text, true);
    if (!is_array($datei) || empty($datei['payload']) || empty($datei['signature'])) return null;
    $nutzdaten = base64_decode($datei['payload'], true);
    $signatur = base64_decode($datei['signature'], true);
    if ($nutzdaten === false || $signatur === false) return null;
    $schluessel = @file_get_contents(__DIR__ . '/oeffentlicher-schluessel.pem');
    if (function_exists('openssl_verify') && $schluessel) {
        if (openssl_verify($nutzdaten, $signatur, $schluessel, OPENSSL_ALGO_SHA256) !== 1) return null;
    }
    $daten = json_decode($nutzdaten, true);
    if (!is_array($daten) || empty($daten['licenseId'])) return null;
    if (($daten['product'] ?? '') !== 'TrackMaster Pro') return null;
    return $daten;
}

function tm_text(string $wert, int $laenge = 80): string
{
    $wert = preg_replace('/[\x00-\x1F\x7F]/u', '', $wert) ?? '';
    return mb_substr(trim($wert), 0, $laenge);
}

function h(?string $s): string
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}

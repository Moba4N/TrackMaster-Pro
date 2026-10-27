#!/usr/bin/env bash
# Startet die Android-App im Emulator, macht Bildschirmfotos und bricht ab, wenn die App abstürzt.
# Aufruf: android-smoke.sh <Release-APK> <Debug-APK> <Ausgabeordner>
set -u
RELEASE="$1"; DEBUG="$2"; OUT="$3"
PKG=de.moba4n.trackmaster
MAIN=$PKG/$PKG.MainActivity
mkdir -p "$OUT"
fail=0

crashed() {
  adb logcat -d -b crash > "$OUT/crash-$1.txt" 2>/dev/null || true
  adb logcat -d | grep -E "FATAL EXCEPTION|AndroidRuntime|mono-rt|Unhandled|TrackMaster" > "$OUT/log-$1.txt" || true
  if grep -q "$PKG" "$OUT/crash-$1.txt"; then
    echo "::error::Absturz bei $1"; cat "$OUT/crash-$1.txt"; return 0
  fi
  return 1
}

shot() {
  # $1 = Name, $2 = Wartezeit
  sleep "$2"
  # Hinweise des Emulators selbst (z. B. „System UI isn't responding“) schließen, sie gehören nicht zur App
  adb shell am broadcast -a android.intent.action.CLOSE_SYSTEM_DIALOGS > /dev/null 2>&1 || true
  sleep 1
  adb exec-out screencap -p > "$OUT/android-$1.png"
  if crashed "$1"; then fail=1; fi
}

start() {
  adb shell am force-stop $PKG
  adb logcat -c
  adb shell am start -W -n "$MAIN" "$@" > /dev/null
}

# Freigabeversion (gekürzt, signiert): startet als Basis
adb install -r "$RELEASE" || { echo "::error::Installation der Release-APK fehlgeschlagen"; exit 1; }
start
shot basis 20
adb uninstall $PKG > /dev/null

# Entwicklerversion (alle Funktionen frei) für Pro, Eigenschaften und 3D
adb install -r "$DEBUG" || { echo "::error::Installation der Debug-APK fehlgeschlagen"; exit 1; }
start
shot pro 20
start --es demo "tab:Gelände"
shot gelaende 12
start --es demo props
shot eigenschaften 12
start --es demo 3d
shot 3d 20
start --es demo ride
shot mitfahren 20
start --es demo evening
shot abend 20
exit $fail

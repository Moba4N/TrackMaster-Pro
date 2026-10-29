# Eigener Gleiskatalog

`katalog.py` erzeugt `src/TrackMaster.Core/Gleiskatalog.json` aus den Tabellen in `systeme/`. Der Katalog ist im
Programm eingebettet und gehört zu TrackMaster Pro (keine fremde Gleisbibliothek).

- Jede Datei in `systeme/` beschreibt ein Gleissystem nach den Angaben des Herstellers: Artikelnummer, Länge, Radius,
  Winkel, bei Bogenweichen der Parallel- bzw. Kreisabstand. Die Quellen stehen in `QUELLEN` und als Kommentar am Teil.
- Die Form der Weichen und Kreuzungen entsteht aus diesen Werten über die Hilfsfunktionen in `katalog.py`
  (`W`, `W3`, `Y`, `BW`, `BK`, `X` …).
- Teile, zu denen der Hersteller keine Maße veröffentlicht, fehlen. Annahmen sind als ungeprüft markiert (`ungeprueft`).
- `ALIASE` leitet frühere Artikelnummern gleicher Form auf das heutige Teil um, damit ältere Pläne weiter öffnen.

Das Programm liest nur diesen Katalog. Teile, die es in keinem System mehr gibt, überspringt es beim Öffnen älterer Pläne
und meldet ihre Anzahl.

Aufruf: `python3 tools/katalog/katalog.py`

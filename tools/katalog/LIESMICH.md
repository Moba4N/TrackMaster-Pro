# Eigener Gleiskatalog

`katalog.py` erzeugt `src/TrackMaster.Core/Gleiskatalog.json` aus den Tabellen in `systeme/`. Der Katalog ist im
Programm eingebettet und gehört zu TrackMaster Pro (keine fremde Gleisbibliothek).

- Jede Datei in `systeme/` beschreibt ein Gleissystem nach den Angaben des Herstellers: Artikelnummer, Länge, Radius,
  Winkel, bei Bogenweichen der Parallel- bzw. Kreisabstand. Die Quellen stehen in `QUELLEN` und als Kommentar am Teil.
- Die Form der Weichen und Kreuzungen entsteht aus diesen Werten über die Hilfsfunktionen in `katalog.py`
  (`W`, `W3`, `Y`, `BW`, `BK`, `X` …).
- Teile, zu denen der Hersteller keine Maße veröffentlicht, fehlen. Annahmen sind als ungeprüft markiert (`ungeprueft`).
- `ALIASE` leitet frühere Artikelnummern gleicher Form auf das heutige Teil um, damit ältere Pläne weiter öffnen.

Systeme, die hier noch fehlen, liest das Programm vorerst aus der GPL-Datei `gleisdaten/gleisdaten.json`
(eigene Datei, nur gelesen). Teile der GPL-Datei, die in einem neu erfassten System fehlen, bleiben dort im Hintergrund,
damit ältere Pläne weiter öffnen.

Aufruf: `python3 tools/katalog/katalog.py`

# Gleiskatalog aus XTrackCAD erzeugen

Die Gleissysteme in `src/TrackMaster.Core/CatalogData.cs` werden aus der Gleisbibliothek von XTrackCAD erzeugt
(Paket `xtrkcad-common`, Ordner `usr/share/xtrkcad/params`, Dateien `*.xtp`, Maße in Zoll).

```bash
apt-get download xtrkcad-common
dpkg-deb -x xtrkcad-common_*.deb xtc
python3 -I tools/catalog-import/gen.py xtc/usr/share/xtrkcad/params tools/catalog-import/legacy.json src/TrackMaster.Core/CatalogData.cs
```

- `xtp.py` liest die Dateien und rechnet in TrackMaster-Koordinaten um (mm, y nach unten, Richtung 0 = nach rechts).
- `classify.py` ordnet jedes Teil zu: Gerade, Bogen, Prellbock, Weiche (gemeinsamer Anfang aller Fahrwege),
  Kreuzung/EKW/DKW/Gleisverbindung/Doppelgleis (vier Anschlüsse) oder Flexgleis.
- `gen.py` enthält die Zuordnung Spur → Gleissystem → Datei und schreibt die C#-Datei.
- `legacy.json` sind die früheren, von Hand eingetragenen Teile. Sie bleiben als verborgene Einträge erhalten,
  damit ältere Pläne weiter öffnen; Piko TT und Spur 0 Standard werden daraus weiter gepflegt.

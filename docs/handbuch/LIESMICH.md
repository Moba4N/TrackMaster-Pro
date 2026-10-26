# Handbuch erstellen

`handbuch.html` ist die Quelle des Handbuchs `docs/TrackMaster-Pro-Handbuch.pdf`. Die Bilder kommen aus dem Build
(Zweig `handbuch-bilder`, Ordner `windows/handbuch`, Gleissysteme mit neutralen Namen) und gehören nach `img/`,
dazu `img/icon.png` aus `src/TrackMaster.App/TrackMaster.ico`.

```
git fetch origin handbuch-bilder
git archive FETCH_HEAD windows/handbuch | tar -x && mkdir -p img && cp windows/handbuch/*.png img/
node build.js ../TrackMaster-Pro-Handbuch.pdf     # braucht Playwright mit Chromium und Python mit pypdf
```

Das Handbuch nennt keine Hersteller- oder Fremdproduktnamen.

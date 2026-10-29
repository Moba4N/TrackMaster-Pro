# Handbuch erstellen

`handbuch.html` ist die Quelle des Handbuchs `docs/TrackPlan-Handbuch.pdf`. Die Bilder kommen aus dem Build
(Zweig `handbuch-bilder`, Ordner `windows/handbuch`, Gleissysteme mit neutralen Namen) und gehören nach `img/`,
dazu `img/logo.png` (Kopie von `img-logo.png`, erzeugt von `tools/branding/make_assets.py`).

```
git fetch origin handbuch-bilder
git archive FETCH_HEAD windows/handbuch | tar -x && mkdir -p img && cp windows/handbuch/*.png img/ && cp img-logo.png img/logo.png
node build.js ../TrackPlan-Handbuch.pdf     # braucht Playwright mit Chromium und Python mit pypdf
```

Das englische Handbuch `manual.html` (PDF `docs/TrackPlan-Manual.pdf`) nutzt dieselbe Gestaltung und die Bilder mit
englischer Oberfläche aus `windows/handbuch-en` im Ordner `img-en/`:

```
mkdir -p img-en && cp windows/handbuch-en/*.png img-en/
node build.js ../TrackPlan-Manual.pdf manual.html
```

Fußzeile und Startsatz für die Seitenzählung stehen jeweils in den `<meta>`-Angaben `pdf-footer` und `pdf-start` der Quelle.
Beide Fassungen bitte gemeinsam ändern.

Das Handbuch nennt keine Hersteller- oder Fremdproduktnamen.

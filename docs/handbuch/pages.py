import sys, json, re
from pypdf import PdfReader
r = PdfReader(sys.argv[1]); ids = json.loads(sys.argv[2])
norm = lambda s: re.sub(r'[\W_]+', '', s).lower()
texts = [norm(p.extract_text() or '') for p in r.pages]
# Inhaltsverzeichnis überspringen: der Textteil beginnt mit dem Einleitungssatz von Kapitel 1
start = next(i for i, t in enumerate(texts) if norm('ist ein Planungsprogramm für Modellbahnanlagen') in t)
res = {}
for hid, title in ids:
    t = norm(title)
    for i in range(start, len(texts)):
        if t in texts[i]:
            res[hid] = i + 1
            start = i
            break
print(json.dumps(res))

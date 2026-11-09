import sys, json, re
from pypdf import PdfReader
r = PdfReader(sys.argv[1]); ids = json.loads(sys.argv[2])
norm = lambda s: re.sub(r'[\W_]+', '', s).lower()
raw = [p.extract_text() or '' for p in r.pages]
texts = [norm(t) for t in raw]
lines = [[norm(l) for l in t.splitlines()] for t in raw]
# Überschriften stehen in einer eigenen Zeile (Kapitel mit Nummer davor, Abschnitte evtl. mit PRO dahinter).
# So zählt ein gleichlautender Satz im Fließtext nicht als Treffer.
is_head = lambda l, t: l == t or l == t + 'pro' or (l.endswith(t) and l[:len(l) - len(t)].isdigit())
# Inhaltsverzeichnis überspringen: der Textteil beginnt mit dem Einleitungssatz von Kapitel 1 (<meta name="pdf-start">)
start = next(i for i, t in enumerate(texts) if norm(sys.argv[3]) in t)
res = {}
for hid, title in ids:
    t = norm(title)
    hit = next((i for i in range(start, len(texts)) if any(is_head(l, t) for l in lines[i])), None)
    for i in ([hit] if hit is not None else range(start, len(texts))):
        if t in texts[i]:
            res[hid] = i + 1
            start = i
            break
print(json.dumps(res))

"""Erzeugt src/TrackMaster.Core/CatalogData.cs aus der XTrackCAD-Gleisbibliothek.

Aufruf: python3 -I gen.py <params-Ordner von xtrkcad-common> <legacy.json> <Ausgabedatei>
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xtp import parse
from classify import classify, cs, f

# Spur: (Id, Maßstab, Spurweite, Plattenbreite, Plattenhöhe, Lichte Höhe, Flexgleis allgemein)
GAUGES = [
    ("Z", "1:220", 6.5, 900, 500, 30, 600),
    ("N", "1:160", 9, 1600, 800, 45, 914),
    ("TT", "1:120", 12, 2000, 1000, 60, 890),
    ("H0", "1:87", 16.5, 2000, 1250, 80, 914),
    ("H0m", "1:87", 12, 2000, 1250, 75, 680),
    ("H0e", "1:87", 9, 1600, 1000, 70, 680),
    ("0", "1:45", 32, 4000, 2500, 140, 914),
    ("1", "1:32", 45, 5000, 3000, 190, 1000),
    ("G", "1:22,5", 45, 6000, 4000, 230, 1500),
]

# Länge des allgemeinen Prellbocks je Spur
BUFFER = {"Z": 27, "N": 40, "TT": 50, "H0": 60, "H0m": 50, "H0e": 40, "0": 120, "1": 150, "G": 150}

# Spur, Id, Name, Datei (None = von Hand gepflegt, vorläufig), Zusatz
SYSTEMS = [
    ("Z", "mini", "Märklin mini-club", "mrklnz.xtp", {"flex": (None, 660)}),
    ("Z", "rokuhan", "Rokuhan", "Z-Rokuhan.xtp", {}),
    ("N", "kato", "Kato Unitrack", "N-Kato Track and Structures.xtp", {}),
    # 222xx: Fleischmann-Gleis ohne Schotterbett (Flexgleis 22200, 730 mm); 91xx: piccolo-Gleis mit Bettung
    ("N", "rf-n", "Roco/Fleischmann ohne Bettung", "N-fl.xtp", {"flex": ("22200", 730)}),
    ("N", "picc", "Roco/Fleischmann piccolo mit Bettung", "flnpicco.xtp", {"flex": ("22200", 730)}),
    ("N", "minitrix", "Minitrix", "minitrix.xtp", {"flex": (None, 750)}),
    ("N", "arnold", "Arnold", "arnold.xtp", {}),
    ("N", "tomix", "Tomix Fine Track", "N-Tomix Track.xtp", {}),
    ("N", "peco-n", "Peco Streamline N (Code 55)", "N-PecoCode55Finescale.xtp", {"flex": ("SL-300F", 914)}),
    ("N", "peco-n80", "Peco Streamline N (Code 80)", "N-PecoCode80Streamline.xtp", {"flex": ("SL-300", 914)}),
    ("N", "peco-st-n", "Peco Setrack N", "N-PecoCode80Setrack.xtp", {}),
    ("TT", "tillig", "Tillig Modellgleis", "TilligTTModellGS.xtp", {}),
    ("TT", "tillig-adv", "Tillig Advance", "TilligAdvTT.xtp", {}),
    ("TT", "tillig-bett", "Tillig Bettungsgleis", "TT-TilligAdvBeddingTrack.xtp", {}),
    ("TT", "kuehn", "Kühn TT", "TT-Kuehn.xtp", {}),
    ("TT", "piko-tt", "Piko TT", None, {}),
    ("H0", "mc", "Märklin C-Gleis", "HO-Marklin C-Track.xtp", {"three": True}),
    ("H0", "tc", "Trix C-Gleis", "@trix", {}),
    ("H0", "mk", "Märklin K-Gleis", "HO-Marklin K-track.xtp", {"three": True, "flex": ("2205", 900)}),
    ("H0", "mm", "Märklin M-Gleis", "HO-Marklin M-Track.xtp", {"three": True}),
    ("H0", "geo", "Roco geoLine", "RocoGeoLineHO.xtp", {}),
    ("H0", "rocoline", "Roco Line (ohne Bettung)", "HO-Roco HO Code 83.xtp", {"flex": ("42400", 920)}),
    ("H0", "piko", "Piko A-Gleis", "Piko-A.xtp", {"flex": (None, 940)}),
    ("H0", "profi", "Fleischmann Profi-Gleis", "fl-profi.xtp", {"flex": (None, 750)}),
    ("H0", "fl-model", "Fleischmann Modellgleis", "fl-model.xtp", {}),
    ("H0", "tillig-h0", "Tillig Elite H0", "TilligH0EliteCode83.xtp", {}),
    ("H0", "peco-h0", "Peco Streamline H0 (Code 75)", "HO-Peco-Code75Finescale.xtp", {"flex": ("SL-100F", 914)}),
    ("H0", "peco-h0-100", "Peco Streamline H0 (Code 100)", "HO-Peco-Code100Streamline.xtp", {"flex": ("SL-100", 914)}),
    ("H0", "peco-st-h0", "Peco Setrack H0", "HO-Peco-Code100Setrack.xtp", {}),
    ("H0", "kato-h0", "Kato Unitrack H0", "kato-ho.xtp", {}),
    ("H0m", "tillig-h0m", "Tillig H0m", "TilligH0m.xtp", {}),
    ("H0e", "tillig-h0e", "Tillig H0e", "TilligH0e.xtp", {}),
    ("H0e", "peco-h0e", "Peco H0e / OO-9", "pecohon30.xtp", {}),
    ("0", "std0", "Spur 0 Standard", None, {}),
    ("0", "lenz0", "Lenz Spur 0", "O-Lenz.xtp", {}),
    ("0", "peco-o", "Peco Bullhead Spur 0", "peco-O-Bullhead.xtp", {}),
    ("1", "m1", "Märklin Spur 1", "mrkln1.xtp", {}),
    ("G", "lgb", "LGB", "lgb.xtp", {}),
    ("G", "piko-g", "Piko G", "Piko-g.xtp", {}),
]

# Märklin-Teile, die es nur für den Mittelleiter gibt bzw. bei Trix unter anderer Nummer
TRIX_SKIP = {"24994", "24995", "24194", "24294", "24922", "24951", "24088", "24001", "24997", "24978"}


def side(code):
    return " R" if "rechts" in code else " L" if "links" in code else None


CATEGORY_ORDER = {"Gerade": 0, "Bögen": 1, "Weichen": 2, "Kreuzungen": 3, "Sonstige": 4}


def sort_key(it):
    """Bibliotheksreihenfolge: Geraden, Bögen, Weichen, Kreuzungen, Sonstiges; darin nach Artikelnummer."""
    code = it['code']
    if code.startswith('P('):
        cat = "Sonstige"
    elif code.startswith('G('):
        cat = re.findall(r'"([^"]*)"', code)[3]
    elif code.startswith('S('):
        cat = "Gerade" if code.count('"') == 2 else "Sonstige"
    else:
        cat = "Bögen" if 'gebogen"' not in code else "Sonstige"
    nr = it['number']
    return (CATEGORY_ORDER.get(cat, 4), [int(t) if t.isdigit() else t for t in re.split(r'(\d+)', nr)])


def load(params, fn):
    items, flex, skipped = [], [], []
    for b in parse(os.path.join(params, fn)):
        r, err = classify(b)
        if r is None:
            skipped.append((b['title'].replace('\t', ' | '), err))
        elif r.get('flex'):
            flex.append(r)
        else:
            items.append(r)
    # doppelte Artikelnummern (links/rechts unter einer Nummer) eindeutig machen
    seen = {}
    for it in items:
        seen.setdefault(it['number'], []).append(it)
    for nr, group in seen.items():
        if len(group) < 2:
            continue
        used = set()
        for k, it in enumerate(group):
            new = nr + (side(it['code']) or f"-{k + 1}")
            if new in used:
                new = f"{nr}-{k + 1}"
            used.add(new)
            it['code'] = it['code'].replace(cs(nr), cs(new), 1)
            it['number'] = new
    items.sort(key=lambda it: [str(x).zfill(8) if isinstance(x, int) else x for x in [sort_key(it)[0]] + sort_key(it)[1]])
    return items, flex, skipped


def main(params, legacy_path, out_path):
    legacy = json.load(open(legacy_path, encoding='utf-8'))
    report = []
    data = {}
    for gauge, sid, name, fn, extra in SYSTEMS:
        if fn is None:
            data[sid] = None
            continue
        if fn == "@trix":
            src = data["mc"]
            items = []
            for it in src['items']:
                if it['number'] in TRIX_SKIP or not re.fullmatch(r"24\d{3}", it['number']):
                    continue
                nr = "62" + it['number'][2:]
                items.append({'number': nr, 'code': it['code'].replace(cs(it['number']), cs(nr), 1)})
            data[sid] = {'items': items, 'flex': [], 'skipped': [], 'file': src['file'] + " (Nummern 62… statt 24…)"}
        else:
            items, flex, skipped = load(params, fn)
            data[sid] = {'items': items, 'flex': flex, 'skipped': skipped, 'file': fn}

    lines = [
        "// <auto-generated>",
        "// Erzeugt von tools/catalog-import/gen.py aus der XTrackCAD-Gleisbibliothek (Paket xtrkcad-common, Ordner params).",
        "// Nicht von Hand ändern, sondern das Skript erneut ausführen.",
        "// </auto-generated>",
        "namespace TrackMaster.Core;",
        "",
        "public static partial class Catalog",
        "{",
        "    private static IReadOnlyList<Gauge> BuildGauges() =>",
        "    [",
    ]
    total = 0
    for gid, scale, width, bw, bh, clear, flexlen in GAUGES:
        lines.append(f"        new() {{ Id = {cs(gid)}, ClearanceHeight = {f(clear)}, FlexStockLength = {f(flexlen)}, Scale = {cs(scale)}, TrackWidth = {f(width)}, "
                     f"DefaultBoardWidth = {f(bw)}, DefaultBoardHeight = {f(bh)}, Systems =")
        lines.append("        [")
        for gauge, sid, name, fn, extra in SYSTEMS:
            if gauge != gid:
                continue
            d = data[sid]
            props = [f"Id = {cs(sid)}", f"Name = {cs(name)}"]
            if d is not None:
                props.append("Verified = true")
                props.append(f"Source = {cs('XTrackCAD ' + d['file'])}")
            if extra.get("three"):
                props.append("ThreeRail = true")
            flex = None
            if d and d['flex']:
                fx = max(d['flex'], key=lambda x: x['len'])
                flex = (fx['number'], round(fx['len']))
            elif "flex" in extra:
                flex = extra["flex"]
            if flex:
                if flex[0]:
                    props.append(f"FlexNumber = {cs(flex[0])}")
                props.append(f"FlexLength = {f(flex[1])}")
            lines.append(f"            new() {{ {', '.join(props)}, Pieces =")
            lines.append("            [")
            if d is None:
                lines.append("                " + legacy[sid].strip().rstrip(','))
                count = legacy[sid].count('(')
            else:
                for it in d['items']:
                    lines.append(f"                {it['code']},")
                count = len(d['items'])
                if not any(it['code'].startswith('P(') for it in d['items']):
                    # Prellbock fehlt in den Herstellerdaten: allgemeiner Prellbock, als ungeprüft markiert
                    lines.append(f"                P(\"PB\", {BUFFER[gid]}, uncheckedGeometry: true),")
            total += count
            lines.append("            ],")
            if d is not None and sid in legacy:
                body = legacy[sid].strip().rstrip(',')
                if body.startswith("CGleis("):
                    lines.append(f"            Legacy = {body} }},")
                else:
                    lines.append("            Legacy =")
                    lines.append("            [")
                    lines.append("                " + body)
                    lines.append("            ] },")
            else:
                lines[-1] = "            ] },"
            report.append(f"{gid:4} {sid:12} {count:4} Teile  Flex {flex}  aus {d['file'] if d else 'Handpflege (vorläufig)'}")
            for t, e in (d['skipped'] if d else []):
                report.append(f"        übersprungen: {t} ({e})")
        lines.append("        ] },")
    lines.append("    ];")
    lines.append("}")
    open(out_path, 'w', encoding='utf-8', newline='\n').write("\n".join(lines) + "\n")
    report.append(f"gesamt {total} Teile")
    print("\n".join(report))


if __name__ == "__main__":
    main(*sys.argv[1:4])

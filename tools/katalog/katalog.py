"""Erzeugt den eigenen Gleiskatalog src/TrackMaster.Core/Gleiskatalog.json aus den Tabellen in systeme/.

Jede Datei in systeme/ beschreibt ein Gleissystem nach den Angaben des Herstellers (Katalog, Prospekt, Produktseite);
die Quellen stehen in QUELLEN. Die Form der Weichen und Kreuzungen ergibt sich aus Länge, Radius und Winkel
(siehe die Hilfsfunktionen unten), nicht aus fremden Gleisbibliotheken. Teile ohne veröffentlichte Maße fehlen.

Aufruf: python3 tools/katalog/katalog.py
"""
import importlib.util
import json
import math
import pathlib
import sys

HIER = pathlib.Path(__file__).parent
sys.path.insert(0, str(HIER))
ZIEL = HIER.parent.parent / "src" / "TrackMaster.Core" / "Gleiskatalog.json"

# Spuren: Maßstab, Spurweite (NEM), Vorgaben für neue Pläne, lichte Höhe und Länge eines Flexgleis-Stücks
SPUREN = [
    dict(id="Z", scale="1:220", trackWidth=6.5, boardWidth=900, boardHeight=500, clearance=30, flexStock=600),
    dict(id="N", scale="1:160", trackWidth=9, boardWidth=1600, boardHeight=800, clearance=45, flexStock=914),
    dict(id="TT", scale="1:120", trackWidth=12, boardWidth=2000, boardHeight=1000, clearance=60, flexStock=890),
    dict(id="H0", scale="1:87", trackWidth=16.5, boardWidth=2000, boardHeight=1250, clearance=80, flexStock=914),
    dict(id="H0m", scale="1:87", trackWidth=12, boardWidth=2000, boardHeight=1250, clearance=75, flexStock=680),
    dict(id="H0e", scale="1:87", trackWidth=9, boardWidth=1600, boardHeight=1000, clearance=70, flexStock=680),
    dict(id="0", scale="1:45", trackWidth=32, boardWidth=4000, boardHeight=2500, clearance=140, flexStock=914),
    dict(id="1", scale="1:32", trackWidth=45, boardWidth=5000, boardHeight=3000, clearance=190, flexStock=1000),
    dict(id="G", scale="1:22,5", trackWidth=45, boardWidth=6000, boardHeight=4000, clearance=230, flexStock=1500),
]


def zahl(v, stellen=1):
    """Zahl wie Fmt.Num auf Deutsch: 1.114,6 · 437,5 · 360."""
    t = f"{round(v + 0.0, stellen):,.{stellen}f}" if stellen else f"{round(v):,}"
    if "." in t:
        t = t.rstrip("0").rstrip(".")
    return t.replace(",", "_").replace(".", ",").replace("_", ".")


def en(v, stellen=1):
    return zahl(v, stellen).replace(".", "_").replace(",", ".").replace("_", ",")


def teil(nr, kind, name, spec, cat, name_en=None, **felder):
    p = dict(nr=str(nr), kind=kind, name=name, spec=spec, cat=cat)
    for k, v in felder.items():
        if v not in (None, False, 0, 0.0):
            p[k] = v
    if name_en is not None:
        p["nameEn"] = name_en
    return p


# --- Gleisstücke ------------------------------------------------------------------------------------------------

def S(nr, laenge, name="Gerade", name_en=None, cat=None):
    """Gerade (oder Sondergleis gleicher Form, z. B. Schaltgleis)."""
    return teil(nr, "Straight", name, f"{zahl(laenge)} mm", cat or ("Gerade" if name == "Gerade" else "Sonstige"), name_en, len=laenge)


def C(nr, r, a, rname=None, name="Bogen", name_en_basis="Curve", cat=None):
    """Bogen mit Radius r und Winkel a (Grad); rname z. B. „R1“."""
    zus = f"{rname} · " if rname else ""
    return teil(nr, "Curve", f"{name} {zus}{zahl(a)}°", f"R {zahl(r)} mm", cat or ("Bögen" if name == "Bogen" else "Sonstige"),
                f"{name_en_basis} {zus}{en(a)}°", r=r, a=a)


WEICHE = {False: ("Weiche links", "Left turnout"), True: ("Weiche rechts", "Right turnout")}
BOGENWEICHE = {False: ("Bogenweiche links", "Left curved turnout"), True: ("Bogenweiche rechts", "Right curved turnout")}


def W(nr, rechts, laenge, r, a, zusatz=None, zusatz_en=None, nachlauf=0):
    """Einfache Weiche: gerader Strang `laenge`, Abzweig als Bogen r/a ab Weichenanfang, optional mit gerader Verlängerung."""
    de, e = WEICHE[rechts]
    if zusatz:
        de, e = f"{de} {zusatz}", f"{e}, {zusatz_en or zusatz}"
    paths = [[[laenge, 0, 0, 0]], [[0, r, a, 1 if rechts else -1], [nachlauf, 0, 0, 0]]] if nachlauf else None
    return teil(nr, "Switch", de, f"{zahl(laenge)} mm · R {zahl(r)} · {zahl(a)}°", "Weichen", e if zusatz else None,
                len=laenge, r=r, a=a, right=rechts, paths=paths)


def WW(links, rechts, laenge, r, a, **k):
    return [W(links, False, laenge, r, a, **k), W(rechts, True, laenge, r, a, **k)]


def W3(nr, laenge, r, a, vorlauf=0, ungeprueft=False):
    """
    Dreiwegweiche: gerade und je ein Abzweig r/a nach links und rechts, optional nach einem geraden Vorlauf
    (eine Zahl für beide Seiten oder (links, rechts) bei versetzten Abzweigen).
    """
    vl, vr = vorlauf if isinstance(vorlauf, tuple) else (vorlauf, vorlauf)
    def v(x): return [[x, 0, 0, 0]] if x else []
    return teil(nr, "Switch", "Dreiwegweiche", f"{zahl(laenge)} mm · R {zahl(r)} · 2 × {zahl(a)}°", "Weichen",
                len=laenge, r=r, a=a, unchecked=ungeprueft, paths=[[[laenge, 0, 0, 0]], [*v(vl), [0, r, a, -1]], [*v(vr), [0, r, a, 1]]])


def Y(nr, r, a, nachlauf=0):
    """Y-Weiche: beide Stränge biegen mit r/a nach links und rechts ab."""
    tail = [[nachlauf, 0, 0, 0]] if nachlauf else []
    return teil(nr, "Switch", "Y-Weiche", f"R {zahl(r)} · 2 × {zahl(a)}°", "Weichen", "Y turnout",
                r=r, a=a, paths=[[[0, r, a, -1], *tail], [[0, r, a, 1], *tail]])


def BW(nr, rechts, r, a, abstand):
    """
    Bogenweiche nach Herstellerangabe: Innenbogen r/a (Strang 1); der Außenstrang (Strang 0, Stammstrang) ist derselbe
    Bogen, um den angegebenen Kreisabstand versetzt, also eine Gerade dieser Länge vor dem Bogen.
    """
    t = 1 if rechts else -1
    de, e = BOGENWEICHE[rechts]
    return teil(nr, "Switch", de, f"R {zahl(r)} · {zahl(a)}° · {zahl(abstand)} mm", "Weichen",
                r=r, a=a, right=rechts, paths=[[[abstand, 0, 0, 0], [0, r, a, t]], [[0, r, a, t]]])


def BK(nr, rechts, r_aussen, r_innen, a, a_innen=None):
    """
    Bogenweiche aus zwei Bögen verschiedener Radien, die am Weichenanfang tangential beginnen: Außenstrang r_aussen/a
    (Strang 0), Innenstrang r_innen/a_innen (Strang 1, ohne Angabe gleicher Winkel).
    """
    ai = a_innen or a
    t = 1 if rechts else -1
    de, e = BOGENWEICHE[rechts]
    winkel = f"{zahl(a)}°" if ai == a else f"{zahl(a)}° / {zahl(ai)}°"
    return teil(nr, "Switch", de, f"R {zahl(r_innen)} / {zahl(r_aussen)} · {winkel}", "Weichen",
                r=r_innen, a=ai, right=rechts, paths=[[[0, r_aussen, a, t]], [[0, r_innen, ai, t]]])


def BKK(links, rechts, r_aussen, r_innen, a, a_innen=None):
    return [BK(links, False, r_aussen, r_innen, a, a_innen), BK(rechts, True, r_aussen, r_innen, a, a_innen)]


def BWW(links, rechts, r, a, abstand, **k):
    return [BW(links, False, r, a, abstand, **k), BW(rechts, True, r, a, abstand, **k)]


def X(nr, laenge, a, art="Kreuzung", gespiegelt=False, laenge2=None):
    """
    Kreuzung mit zwei geraden Strängen, die sich in ihrer Mitte unter dem Winkel a kreuzen; art „DKW“ oder „EKW“.
    laenge2: Länge des zweiten Strangs, wenn sie abweicht. gespiegelt: der zweite Strang kreuzt in der Gegenrichtung
    (dasselbe Teil, nur andere Reihenfolge der Anschlüsse).
    """
    namen = {"Kreuzung": "Crossing", "DKW": "Double slip", "EKW": "Single slip"}
    routes = None
    if gespiegelt or laenge2:
        l2 = laenge2 or laenge
        c, c2, rad, f = laenge / 2, l2 / 2, math.radians(a), -1 if gespiegelt else 1
        routes = [dict(x=0, y=0, h=0, s=[[laenge, 0, 0, 0]]),
                  dict(x=round(c - c2 * math.cos(rad), 4), y=round(-f * c2 * math.sin(rad), 4), h=f * a, s=[[l2, 0, 0, 0]])]
    return teil(nr, "Crossing", f"{art} {zahl(a)}°", f"{zahl(laenge)} mm", "Kreuzungen", f"{namen[art]} {en(a)}°",
                len=laenge, a=a, drives=1 if art == "EKW" else None, routes=routes)


def P(nr, laenge, name="Prellbock", name_en=None):
    return teil(nr, "Buffer", name, f"{zahl(laenge)} mm", "Sonstige", name_en, len=laenge)


def PB(laenge):
    """Allgemeiner Prellbock (kein Herstellerteil), für Systeme ohne eigenen Prellbock im Programm; Länge geschätzt."""
    return teil("PB", "Buffer", "Prellbock (allgemein)", f"{zahl(laenge)} mm", "Sonstige", len=laenge, unchecked=True)


# --- Erzeugen ---------------------------------------------------------------------------------------------------

def system(m):
    s = dict(id=m.ID, name=m.NAME, verified=True)
    for k, name in (("threeRail", "THREE_RAIL"), ("flexNumber", "FLEX_NUMBER"), ("flexLength", "FLEX_LENGTH"), ("moreFlex", "MORE_FLEX")):
        v = getattr(m, name, None)
        if v:
            s[k] = v
    if getattr(m, "ALIASE", None):
        s["aliases"] = {str(k): str(v) for k, v in m.ALIASE.items()}
    s["sources"] = m.QUELLEN
    s["pieces"] = m.TEILE
    nummern = [p["nr"] for p in m.TEILE]
    doppelt = {n for n in nummern if nummern.count(n) > 1}
    assert not doppelt, f"{m.ID}: doppelte Nummern {doppelt}"
    return s


def main():
    module = []
    for f in sorted((HIER / "systeme").glob("*.py")):
        spec = importlib.util.spec_from_file_location(f.stem, f)
        m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(m)
        module.append(m)
    spuren = []
    for g in SPUREN:
        systeme = [system(m) for m in sorted((m for m in module if m.SPUR == g["id"]), key=lambda m: m.REIHENFOLGE)]
        spuren.append(dict(g, systems=systeme))
    out = dict(format="gleisdaten-1", gauges=spuren)
    ZIEL.write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    anzahl = sum(len(s["pieces"]) for g in spuren for s in g["systems"])
    print(f"{ZIEL.name}: {sum(len(g['systems']) for g in spuren)} Systeme, {anzahl} Teile")


if __name__ == "__main__":
    main()

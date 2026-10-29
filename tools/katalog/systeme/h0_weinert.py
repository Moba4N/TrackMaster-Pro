"""Weinert »mein Gleis« H0 (Code 75). Maße nach dem Prospekt »mein Gleis« des Herstellers (2021)."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "weinert", "Weinert mein Gleis", 20
QUELLEN = [
    "Weinert Modellbau: Prospekt »mein Gleis« 2021 (Weichen, Bogenweichen, Kreuzungsweichen, Flexgleise mit Länge, Radius und Winkel)",
]
FLEX_NUMBER, FLEX_LENGTH = "74000", 914
MORE_FLEX = [dict(nr="74003", len=914, label="Betonschwellen-Flexgleis B58"), dict(nr="74002", len=914, label="Stahlschwellen-Flexgleis"),
             dict(nr="74004", len=914, label="Holzschwellen-Flexgleis RP 25")]

# Der Prospekt nennt je Weiche Gesamtlänge, Radius und Winkel und die Form des Abzweigs: „ab Herzstück gerade“
# (Bogen ab Weichenanfang, danach gerade) oder „durchgehender Radius“ (gerades Stück am Anfang, Bogen bis zum Ende).
# Das gerade Stück ergibt sich daraus, dass der Abzweig auf der Gesamtlänge endet.
def gerade_nach(laenge, r, a):
    return round((laenge - r * math.sin(math.radians(a))) / math.cos(math.radians(a)), 1)


def gerade_vor(laenge, r, a):
    return round(laenge - r * math.sin(math.radians(a)), 1)


def WN(links, rechts, laenge, r, a, zusatz, form):
    k = dict(nachlauf=gerade_nach(laenge, r, a)) if form == "nach" else dict(vorlauf=gerade_vor(laenge, r, a))
    return WW(links, rechts, laenge, r, a, zusatz=zusatz, **k)


def YN(nr, laenge, r, w, form, zusatz):
    """Außenbogenweiche in Y-Form: beide Stränge mit r und halbem Weichenwinkel w."""
    a = w / 2
    g = gerade_nach(laenge, r, a) if form == "nach" else gerade_vor(laenge, r, a)
    def strang(t):
        return [[0, r, a, t], [g, 0, 0, 0]] if form == "nach" else [[g, 0, 0, 0], [0, r, a, t]]
    return teil(nr, "Switch", f"Y-Weiche {zusatz}", f"{zahl(laenge)} mm · R {zahl(r)} · {zahl(w)}°", "Weichen",
                f"Y turnout, {zusatz}", r=r, a=a, paths=[strang(-1), strang(1)])


# Fertig-, flexible und Bausatz-Weichen haben dieselbe Form; die flexiblen und Bausätze verweisen auf die Fertigweiche.
ALIASE = {"74904": "74901", "74907": "74901", "74905": "74902", "74908": "74902",
          "74664": "74661", "74667": "74661", "74665": "74662", "74668": "74662",
          "74504": "74501", "74505": "74502",
          "74404": "74401", "74407": "74401", "74405": "74402", "74408": "74402",
          "74906": "74903", "74909": "74903", "74666": "74663", "74669": "74663", "74506": "74503",
          "74406": "74403", "74409": "74403"}

# Innenbogenweiche: R 3605 und R 1450, Gesamtlänge 353 mm, Weichenwinkel 8,6°; die Bogenwinkel sind daraus
# abgeschätzt (Außenstrang so lang wie die Weiche, Innenstrang 8,6° mehr).
IB_A = round(math.degrees(353 / 3605), 2)

TEILE = [
    *WN(74901, 74902, 375, 2180, 6.3, "1:9", "nach"),
    *WN(74661, 74662, 355, 2180, 8.6, "1:6,6", "vor"),
    *WN(74501, 74502, 333, 2180, 8.6, "1:6,6 kürzer", "vor"),
    *WN(74401, 74402, 264, 1450, 8.6, "kurz", "nach"),
    YN(74903, 373, 4360, 6.3, "nach", "1:9"),
    YN(74663, 353, 4360, 8.6, "vor", "1:6,6"),
    YN(74503, 333, 4360, 8.6, "vor", "1:6,6 kürzer"),
    YN(74403, 264, 2900, 8.6, "nach", "kurz"),
    {**BK(74601, False, 3605, 1450, IB_A, round(IB_A + 8.6, 2)), "unchecked": True},
    {**BK(74602, True, 3605, 1450, IB_A, round(IB_A + 8.6, 2)), "unchecked": True},
    X(74942, 470, 6.3, "DKW"), X(74641, 341, 8.6, "DKW"), X(74642, 341, 8.6, "DKW"),
    PB(60),
]

"""TILLIG ELITE-Gleis H0 (Code 83). Maße nach den Artikelangaben im TILLIG-Onlineshop."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "tillig-h0", "Tillig Elite H0", 9
FLEX_NUMBER, FLEX_LENGTH = "85125", 890
MORE_FLEX = [dict(nr="85134", len=470, label="Betonschwellen-Flexgleis"), dict(nr="85136", len=470, label="Stahlschwellen-Flexgleis"),
             dict(nr="85025", len=220, label="Flex-Schwellenband")]
QUELLEN = [
    "https://shop.tillig.com/de/h0-modellbahnen/elite-gleissystem/ (Artikel mit Länge, Radius und Abzweigwinkel, Stand 10/2026)",
]

# Die einfachen Weichen enden mit dem Abzweig im Versatz der Kreuzung 15° / 228 mm (114 mm × sin 15° = 29,5 mm), so
# dass Weiche und Kreuzung zusammenpassen. Nach dem Abzweigbogen (Radius und Winkel laut TILLIG) folgt bis dahin gerades Gleis.
VERSATZ = 29.5


def nachlauf(r, a):
    w = math.radians(a)
    return max(0, round((VERSATZ - r * (1 - math.cos(w))) / math.sin(w), 2))


def EW(links, rechts, laenge, r, a):
    return WW(links, rechts, laenge, r, a, nachlauf=nachlauf(r, a))


TEILE = [
    S(85118, 228), S(85124, 114), S(85130, 64), S(85131, 57), S(85128, 53), S(85129, 50),
    S(85139, 114, "Modulausgleichsstück", "Module compensation piece"),
    C(85140, 366, 30, "R11"), C(85141, 425, 30, "R21"), C(85143, 484, 30, "R31"), C(85145, 543, 30, "R41"),
    # Weiche: Abzweig 15°, Radius 484, 178 mm · 15°, R 866, 228 mm · 12°, R 1.350, 284 mm · 9,4°, R 2.200, 361 mm · 6,34°, R 2.200, 388 mm
    *EW(85324, 85323, 178, 484, 15),
    *EW(85344, 85343, 228, 866, 15),
    *EW(85354, 85353, 284, 1350, 12),
    *EW(85327, 85326, 361, 2200, 9.4),
    *EW(85347, 85346, 388, 2200, 6.34),
    # Innenbogenweichen: je Abzweig Winkel und Radius
    *BKK(85314, 85313, 778, 484, 20.7, 32.7),
    *BKK(85334, 85333, 543, 377, 29.3, 41.3),
    *BKK(85364, 85363, 866, 425, 15, 30),
    *BKK(85374, 85373, 934, 543, 17, 29),
    # Außenbogenweichen (symmetrisch): Abzweig 15°, Radius 1.739 · 12°, Radius 2.707
    Y(85381, 1739, 7.5), Y(85383, 2707, 6),
    X(85160, 228, 15), X(85170, 114, 30),
    X(85390, 228, 15, "DKW"), X(85391, 276, 15, "DKW"),
    X(85395, 228, 15, "EKW"), X(85396, 252, 15, "EKW"),
    S(85018, 228, "Schwellenband", "Tie strip"), S(85034, 110, "Betonschwellenband", "Concrete tie strip"),
    S(85036, 54, "Stahlschwellenband", "Steel tie strip"),
    PB(60),
]

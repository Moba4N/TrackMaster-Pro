"""PECO Setrack H0 (Code 100). Maße nach dem PECO-Katalog (deutsche Ausgabe 2002)."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "peco-st-h0", "Peco Setrack H0", 15
QUELLEN = [
    "PECO Gesamtkatalog (deutsche Ausgabe 2002): Setrack Code 100, S. 8–9 (Längen, Radien, Kreisteilung, Herzstückwinkel)",
]

# Parallelgleisabstand 67 mm: R1 371, R2 438, R3 505 mm. Der Katalog druckt für R3 495 mm; das passt nicht zum
# angegebenen Abstand (438 + 67), daher 505.
ABSTAND = 67
R1, R2, R3 = 371, 438, 438 + ABSTAND


def BW_ST(nr, rechts):
    """Bogenweiche: Außenstrang gerade 67 mm, dann R2 22,5°; Innenstrang R2 über 22,5° + Herzstückwinkel 11,25°."""
    t = 1 if rechts else -1
    de, e = BOGENWEICHE[rechts]
    return teil(nr, "Switch", de, f"R {zahl(R2)} · 22,5° / 33,75°", "Weichen", r=R2, a=33.75, right=rechts,
                paths=[[[ABSTAND, 0, 0, 0], [0, R2, 22.5, t]], [[0, R2, 33.75, t]]])


# Y-Weiche: Länge 170 mm, je 11,25° mit dem Bogen R 859,6 (ST-238), danach gerade bis zum Weichenende
Y_NACHLAUF = round((170 - 859.6 * math.sin(math.radians(11.25))) / math.cos(math.radians(11.25)), 2)

TEILE = [
    S("ST-203", 41), S("ST-202", 79), S("ST-200", 168), S("ST-201", 335),
    S("ST-205", 168, "Isoliergleis mit Schalter", "Isolating track with switch"),
    C("ST-220", R1, 22.5, "R1"), C("ST-221", R1, 45, "R1"), C("ST-222", R1, 11.25, "R1"),
    C("ST-225", R2, 22.5, "R2"), C("ST-226", R2, 45, "R2"), C("ST-227", R2, 11.25, "R2"),
    C("ST-230", R3, 22.5, "R3"), C("ST-231", R3, 45, "R3"),
    C("ST-238", 859.6, 11.25, name="Bogen für Y-Weiche", name_en_basis="Curve for Y turnout", cat="Bögen"),
    # Weichen 22,5°: Länge 168 mm, Abzweig R 438 (wie Bogen ST-225)
    W("ST-241", False, 168, R2, 22.5), W("ST-240", True, 168, R2, 22.5),
    BW_ST("ST-245", False), BW_ST("ST-244", True),
    Y("ST-247", 859.6, 11.25, nachlauf=Y_NACHLAUF),
    PB(60),
]

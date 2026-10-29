"""FLEISCHMANN N-Gleis ohne Schotterbett. Maße nach dem FLEISCHMANN-Zubehörkatalog (deutsche Ausgabe)."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "N", "rf-n", "Roco/Fleischmann ohne Bettung", 1
QUELLEN = [
    "FLEISCHMANN Zubehör-Katalog (deutsche Ausgabe): N-Gleissystem ohne Schotterbett, Artikelangaben und Gleisgeometrie (S. 36–41)",
]
FLEX_NUMBER, FLEX_LENGTH = "22200", 730
MORE_FLEX = [dict(nr="22201", len=730, label="Flexgleis, weniger flexibel")]

# Gleisabstand 33,6 mm. Weiche 15°: Abzweig R 362,6 mm (R5); das gerade Stück nach dem Bogen folgt aus dem
# Gleisabstand (zwei Weichen ergeben eine Gleisverbindung). Weiche 24°: Abzweig R1 ohne Verlängerung.
ABSTAND = 33.6
NACHLAUF15 = round((ABSTAND / 2 - 362.6 * (1 - math.cos(math.radians(15)))) / math.sin(math.radians(15)), 2)

TEILE = [
    S(22202, 312.6), S(22203, 104.2), S(22204, 54.2), S(22205, 50), S(22206, 33.6), S(22207, 17.2),
    S(22210, 104.2, "Aufgleisgleis"), S(22212, 104.2, "Entkupplungsgleis"), S(22220, 104.2, "Anschlussgleis"),
    C(22221, 194.6, 30, "R1"), C(22231, 194.6, 24, "R1"), C(22232, 194.6, 6, "R1"),
    C(22222, 228.2, 30, "R2"), C(22233, 228.2, 24, "R2"), C(22234, 228.2, 6, "R2"),
    C(22223, 261.8, 30, "R3"), C(22224, 295.4, 30, "R3a"), C(22235, 295.4, 15, "R3a"),
    C(22225, 329, 15, "R4"), C(22226, 362.6, 15, "R5"), C(22227, 480, 15, "R6"), C(22228, 765, 12, "R7"),
    # 24°: gerade 104,2 mm, Abzweig R1; elektrisch 22249/22251, Hand 22253/22255
    *WW(22249, 22251, 104.2, 194.6, 24), *WW(22253, 22255, 104.2, 194.6, 24),
    # 15°: gerade 112,6 mm, Abzweig R5; elektrisch 22261/22263, Hand 22265/22267
    *WW(22261, 22263, 112.6, 362.6, 15, nachlauf=NACHLAUF15), *WW(22265, 22267, 112.6, 362.6, 15, nachlauf=NACHLAUF15),
    # 10°: gerade 155 mm, Abzweig R7
    *WW(22301, 22303, 155, 765, 10),
    # Dreiwegweiche: gerade 112,6 mm, je 15°; Radius nicht angegeben, wie die Weiche 15° angenommen
    W3(22247, 112.6, 362.6, 15, nachlauf=NACHLAUF15, ungeprueft=True),
    # Bogenweichen: Stammgleis R1, Abzweig R2, 42°
    *BKK(22273, 22275, 228.2, 194.6, 42),
    X(22244, 104.2, 30), X(22245, 112.6, 15), X(22246, 129.8, 15, "DKW"),
    PB(40),
]

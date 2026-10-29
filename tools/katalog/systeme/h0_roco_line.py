"""ROCO LINE ohne Bettung. Maße nach dem ROCO-Zubehörkatalog 2022."""
from katalog import *
from _roco_line import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "rocoline", "Roco Line (ohne Bettung)", 5
FLEX_NUMBER, FLEX_LENGTH = "42400", 920
MORE_FLEX = [dict(nr="42401", len=920, label="Flexgleis Betonschwellen")]

TEILE = [
    S(42406, 920), S(42410, 230), S(42412, 115), S(42413, 57.5), S(42411, 119, "Diagonalgerade DG1", "Diagonal straight DG1", cat="Gerade"),
    S(42419, 115, "Entkupplungsgleis"), S(42421, 115, "Anschlussgleis"),
    S(42414, 57.5, "Übergangsgleis G1/4", "Transition track G1/4", cat="Sonstige"),
    # Distanzstücke D2, D4, D5, D8, D12
    *[S(nr, l, f"Distanzstück D{l}", f"Spacer D{l}", cat="Gerade") for nr, l in ((98000, 2), (98001, 4), (98002, 5), (98003, 8), (98008, 12))],
    bogen(42408, "R2", 7.5, True), bogen(42409, "R3", 7.5, True),
    bogen(42422, "R2", 30), bogen(42423, "R3", 30), bogen(42424, "R4", 30), bogen(42425, "R5", 30), bogen(42426, "R6", 30),
    bogen(42427, "R9", 15), bogen(42428, "R10", 15), bogen(42430, "R20", 5),
    *weichen15(42440, 42441), dreiweg(42454), *weichen10(42488, 42489),
    *bogenweichen(42464, 42465, "R2"), *bogenweichen(42472, 42473, "R3"), *bogenweichen(42470, 42471, "R5"), *bogenweichen(42476, 42477, "R9"),
    X(42497, 230, 15), X(42448, 230, 15, "EKW"), X(42451, 230, 15, "DKW"),
    X(42493, 345, 10, "EKW"), X(42496, 345, 10, "DKW"), X(42498, 119, 30),
    DS(42615, 307, 253, 40, 9),
    PB(60),
]

"""ROCO LINE mit Bettung. Maße nach dem ROCO-Zubehörkatalog 2022."""
from katalog import *
from _roco_line import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "rocoline-b", "Roco Line mit Bettung", 6
FLEX_NUMBER, FLEX_LENGTH = "42400", 920
MORE_FLEX = [dict(nr="42401", len=920, label="Flexgleis Betonschwellen")]

TEILE = [
    S(42506, 920), S(42510, 230), S(42512, 115), S(42513, 57.5), S(42511, 119, "Diagonalgerade DG1", "Diagonal straight DG1", cat="Gerade"),
    S(42518, 115, "Schaltgleis"), S(42519, 115, "Entkupplungsgleis"), S(42521, 115, "Anschlussgleis"),
    S(42517, 115, "Digital-Anschlussgleis", "Digital feeder track"),
    *[S(nr, l, f"Distanzstück D{l}", f"Spacer D{l}", cat="Gerade") for nr, l in ((98004, 2), (98005, 4), (98006, 5), (98007, 8), (98009, 12))],
    bogen(42508, "R2", 7.5, True), bogen(42509, "R3", 7.5, True),
    bogen(42522, "R2", 30), bogen(42523, "R3", 30), bogen(42524, "R4", 30), bogen(42525, "R5", 30), bogen(42526, "R6", 30),
    bogen(42527, "R9", 15), bogen(42528, "R10", 15), bogen(42530, "R20", 5),
    *weichen15(42532, 42533), dreiweg(42543), *weichen10(42580, 42581),
    *bogenweichen(42556, 42557, "R2"), *bogenweichen(42572, 42573, "R3"), *bogenweichen(42570, 42571, "R5"), *bogenweichen(42568, 42569, "R9"),
    X(42597, 230, 15), dgv15(42598), X(42546, 230, 15, "EKW"), X(42594, 230, 15, "DKW"),
    X(42591, 345, 10, "EKW"), X(42549, 345, 10, "DKW"),
    DS(42615, 307, 253, 40, 9),
    PB(60),
]

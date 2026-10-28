"""Minitrix (Spur N). Maße nach den Produktseiten des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "N", "minitrix", "Minitrix", 3
FLEX_NUMBER, FLEX_LENGTH = "14901", 730
QUELLEN = [
    "https://www.maerklinshop.de/en/trix/minitrix/railtracks/ (Produktseiten der Artikel 149xx, Stand 10/2026)",
]

R1, R2, R2A, R3, R4, R5, R6 = 194.6, 228.2, 261.8, 329.0, 362.6, 492.6, 526.2

# frühere Weichen gleicher Form
ALIASE = {"14961": 14951, "14963": 14953, "14936": 14938, "14937": 14939, "14966": 14956, "14967": 14957}

TEILE = [
    S(14903, 17.5), S(14908, 27.9), S(14909, 33.6), S(14907, 50), S(14906, 54.2), S(14905, 76.3), S(14904, 104.2), S(14902, 312.6),
    C(14916, R1, 6, "R1"), C(14914, R1, 24, "R1"), C(14912, R1, 30, "R1"),
    C(14926, R2, 6, "R2"), C(14924, R2, 24, "R2"), C(14922, R2, 30, "R2"),
    C(14910, R2A, 30, "R2a"),
    C(14917, R3, 15, "R3"), C(14927, R4, 15, "R4"), C(14918, R5, 15, "R5"), C(14928, R6, 15, "R6"),
    # Weiche 24°: gerader Strang 104,2 mm, Abzweig R1 − 24°
    *WW(14951, 14953, 104.2, R1, 24),
    # Weichen 15°: gerader Strang 112,6 mm, Abzweig R4 − 15° + 1 × 14903 (17,5 mm)
    *WW(14938, 14939, 112.6, R4, 15, nachlauf=17.5),
    *WW(14954, 14955, 112.6, R4, 15, nachlauf=17.5, zusatz="mit Antrieb", zusatz_en="with motor"),
    # Bogenweiche R1/R2: Innenbogen Radius 1, Außenbogen Radius 2, 42°
    *BKK(14956, 14957, R2, R1, 42),
    X(14960, 129.8, 15, "DKW"),
    X(14968, 104.2, 30, "DKW"),
    X(14973, 129.8, 15),
    X(14958, 104.2, 30),
    S(14969, 76.3, "Entkupplungsgleis", cat="Sonstige"),
    S(14979, 50, "Schaltgleis", cat="Sonstige"),
    S(14974, 104.2, "Aufgleisgleis", cat="Sonstige"),
    P(14991, 50), P(14976, 50),
    PB(40),
]

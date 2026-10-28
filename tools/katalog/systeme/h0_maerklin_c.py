"""Märklin C-Gleis (H0, Mittelleiter). Maße nach den Produktseiten des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "mc", "Märklin C-Gleis", 0
THREE_RAIL = True
QUELLEN = [
    "https://www.maerklinshop.de/en/maerklin/gauge-h0/railtracks/c-gauge/ (Produktseiten der Artikel, Stand 10/2026)",
]

TEILE = [
    S(24064, 64.3), S(24071, 70.8), S(24077, 77.5), S(24094, 94.2), S(24172, 171.7), S(24188, 188.3),
    S(24229, 229.3), S(24236, 236.1), S(24360, 360),
    C(24107, 360, 7.5, "R1"), C(24115, 360, 15, "R1"), C(24130, 360, 30, "R1"),
    C(24206, 437.5, 5.7, "R2"), C(24207, 437.5, 7.5, "R2"), C(24215, 437.5, 15, "R2"), C(24224, 437.5, 24.3, "R2"), C(24230, 437.5, 30, "R2"),
    C(24315, 515, 15, "R3"), C(24330, 515, 30, "R3"),
    C(24430, 579.3, 30, "R4"), C(24530, 643.6, 30, "R5"),
    C(24912, 1114.6, 12.1),
    # Weiche: 188,3 mm, Abzweig R2 = 437,5 mm, 24,3°
    *WW(24611, 24612, 188.3, 437.5, 24.3),
    # schlanke Weiche: 236,1 mm, Abzweig R 1.114,6 mm, 12,1°
    *WW(24711, 24712, 236.1, 1114.6, 12.1, zusatz="schlank", zusatz_en="slim"),
    # Dreiwegweiche: 188,3 mm, 2 × 24,3°, Anschlussmaße wie 24611/24612
    W3(24630, 188.3, 437.5, 24.3),
    # Bogenweichen: Innenbogen R1 360 mm bzw. R3 515 mm, 30°, Parallelabstand 77,5 mm bzw. 64 mm
    *BWW(24671, 24672, 360, 30, 77.5),
    *BWW(24771, 24772, 515, 30, 64),
    X(24624, 188.3, 24.3, "DKW"),
    X(24649, 103.3, 48.6),
    S(24994, 94.2, "Schaltgleis", cat="Sonstige"),
    S(24997, 94.2, "Entkupplungsgleis", cat="Sonstige"),
    S(24951, 180, "Übergangsgleis", cat="Sonstige"),
    S(24922, 180, "Übergangsgleis", cat="Sonstige"),
    # Prellbock: Gleisstück 77,5 mm (+ 5 mm Prellbock)
    P(24977, 77.5), P(24978, 77.5),
]

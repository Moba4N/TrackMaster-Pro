"""Trix C-Gleis (H0, Zweileiter). Maße nach den Produktseiten des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "tc", "Trix C-Gleis", 1
QUELLEN = [
    "https://www.maerklinshop.de/en/trix/trix-h0/railtracks/railtracks/ (Produktseiten der Artikel, Stand 10/2026)",
]

TEILE = [
    S(62064, 64.3), S(62071, 70.8), S(62077, 77.5), S(62094, 94.2), S(62172, 171.7), S(62188, 188.3),
    S(62229, 229.3), S(62236, 236.1), S(62360, 360),
    C(62107, 360, 7.5, "R1"), C(62115, 360, 15, "R1"), C(62130, 360, 30, "R1"),
    C(62206, 437.5, 5.7, "R2"), C(62207, 437.5, 7.5, "R2"), C(62215, 437.5, 15, "R2"), C(62224, 437.5, 24.3, "R2"), C(62230, 437.5, 30, "R2"),
    C(62315, 515, 15, "R3"), C(62330, 515, 30, "R3"),
    C(62430, 579.3, 30, "R4"), C(62530, 643.6, 30, "R5"),
    C(62912, 1114.6, 12.1),
    # Weiche: 188,3 mm, Abzweig R2 = 437,5 mm, 24,3°
    *WW(62611, 62612, 188.3, 437.5, 24.3),
    # schlanke Weiche: 236,1 mm, Abzweig R 1.114,6 mm, 12,1°
    *WW(62711, 62712, 236.1, 1114.6, 12.1, zusatz="schlank", zusatz_en="slim"),
    # Bogenweichen R1 360 mm und R3 515 mm, 30°; Kreisabstand wie beim Märklin C-Gleis (77,5 mm bzw. 64 mm, gleiche Gleisgeometrie)
    *BWW(62671, 62672, 360, 30, 77.5),
    *BWW(62771, 62772, 515, 30, 64),
    X(62624, 188.3, 24.3, "DKW"),
    X(62720, 236.1, 12.1, "DKW"),
    X(62649, 103.3, 48.6),
    S(62997, 94.2, "Entkupplungsgleis", cat="Sonstige"),
    P(62977, 80.5),
    P(62001, 16.5, "Bettungsende", "Roadbed end piece"),
]

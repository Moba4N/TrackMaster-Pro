"""Märklin mini-club (Spur Z). Maße nach den Produktseiten des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "Z", "mini", "Märklin mini-club", 0
FLEX_NUMBER, FLEX_LENGTH = "8594", 660
QUELLEN = [
    "https://www.maerklinshop.de/en/ Spur Z, Gleise (Produktseiten der Artikel 85xx, Stand 10/2026)",
]

# frühere Einträge: Bogen 8591 mit Schnitt links/rechts
ALIASE = {"8591-1": 8591, "8591-2": 8591}

TEILE = [
    S(8504, 25), S(8503, 55), S(8506, 108.6), S(8500, 110), S(8507, 112.8), S(8505, 220),
    S(85051, 220, "Gerade (Betonschwellen)", "Straight (concrete ties)", cat="Gerade"),
    C(8510, 145, 45), C(8520, 195, 45), C(8521, 195, 30), C(8530, 220, 45), C(8531, 220, 30),
    # Gegenbogen zum Abzweig der Weichen 8562/8563/8565/8566
    C(8591, 490, 13),
    # Weichen 110 mm, 13°, Radius 490 mm
    *WW(8562, 8563, 110, 490, 13, zusatz="elektrisch", zusatz_en="electric"),
    *WW(8565, 8566, 110, 490, 13),
    # Bogenweichen R 195 mm, 30°, Stammgleis-Vorlauf 25 mm
    *BWW(8568, 8569, 195, 30, 25),
    X(8560, 112.8, 13, "DKW"),
    X(8559, 112.8, 13),
    S(8587, 55, "Entkupplungsgleis", cat="Sonstige"),
    S(8588, 55, "Trenngleis", cat="Sonstige"),
    S(8589, 55, "Schaltgleis", cat="Sonstige"),
    S(8590, 110, "Anschlussgleis", cat="Sonstige"),
    C(8529, 195, 30, name="Schaltgleis gebogen", name_en_basis="Curved contact track"),
    C(8539, 220, 30, name="Schaltgleis gebogen", name_en_basis="Curved contact track"),
    PB(27),
]

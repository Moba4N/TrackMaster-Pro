"""LGB (Spur G). Maße nach den Produktseiten des Herstellers und dem LGB-Katalog (Gleissystem, Radien)."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "G", "lgb", "LGB", 0
QUELLEN = [
    "https://www.maerklinshop.de/en/ Spur G, LGB-Gleise (Produktseiten, Stand 10/2026)",
    "LGB-Katalog 2009, „Das LGB-Gleissystem“: Kreisdurchmesser R1 1200, R2 1560, R3 2390, R5 4640 mm (Gleismitte)",
]

R1, R2, R3, R5 = 600, 780, 1195, 2320

TEILE = [
    S(10040, 41), S(10050, 52), S(10070, 75), S(10080, 82), S(10150, 150), S(10000, 300), S(10600, 600),
    S(10152, 150, "Trenngleis", cat="Sonstige"),
    S(10153, 150, "Unterbrechergleis", "Interrupter track", cat="Sonstige"),
    C(11020, R1, 15, "R1"), C(11000, R1, 30, "R1"),
    C(15000, R2, 30, "R2"),
    C(16000, R3, 22.5, "R3"),
    C(18000, R5, 15, "R5"),
    # Weichen: gerade Seite 300 mm, Abzweig R1 30° · 440 mm, R3 22,5° · 600 mm, R5 15°
    *WW(12100, 12000, 300, R1, 30),
    *WW(12150, 12050, 300, R1, 30, zusatz="mit Antrieb", zusatz_en="with motor"),
    *WW(16140, 16040, 440, R3, 22.5),
    *WW(16150, 16050, 440, R3, 22.5, zusatz="mit Antrieb", zusatz_en="with motor"),
    *WW(18150, 18050, 600, R5, 15),
    # Dreiwegweiche: gerade 375 mm, Abzweige R1 30°, laut Hersteller „zwei Weichen platzsparend hintereinander“. Wo die
    # Abzweige beginnen, nennt er nicht; angenommen ist der Versatz 375 − 300 = 75 mm, daher als ungeprüft markiert.
    W3(12360, 375, R1, 30, vorlauf=(75, 0), ungeprueft=True),
    # DKW: gerade Seiten 375 mm, Kreuzungswinkel 22,5°
    X(12260, 375, 22.5, "DKW"),
    # Kreuzung 30°: gerade Gleise 341 mm und 300 mm
    X(13000, 341, 30, laenge2=300),
]

"""PIKO G-Gleis. Maße nach den Artikelangaben im PIKO-Shop."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "G", "piko-g", "Piko G", 1
QUELLEN = [
    "https://www.piko-shop.de/en/warengruppe/track-elements-9.html (Artikel mit Länge, Radius und Winkel; Weichen und Kreuzung "
    "mit Angabe der Gleisstücke, aus denen ihre Stränge bestehen; Stand 10/2026)",
]

R1, R3, R5, R7 = 600, 921.54, 1243.08, 1564.6

TEILE = [
    S(35200, 320), S(35201, 280), S(35202, 160), S(35203, 140), S(35204, 95), S(35208, 600), S(35209, 1200),
    C(35211, R1, 30, "R1"), C(35213, R3, 30, "R3"), C(35214, R3, 7.5, "R3"),
    C(35215, R5, 15, "R5"), C(35216, R5, 7.5, "R5"), C(35217, R7, 15, "R7"), C(35218, R7, 7.5, "R7"),
    # Weiche 30°: gerader Strang = G320, Abzweig = R1 (30°)
    *WW(35220, 35221, 320, R1, 30),
    # Weiche 22,5°: gerader Strang = G320 + G160, Abzweig = 3 × R5 (7,5°)
    *WW(35222, 35223, 480, R5, 22.5),
    # Kreuzung 30°: Stränge = G320
    X(35240, 320, 30),
    PB(150),
]

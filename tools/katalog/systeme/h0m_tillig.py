"""TILLIG ELITE-Gleis H0m. Maße nach den Artikelangaben im TILLIG-Onlineshop."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0m", "tillig-h0m", "Tillig H0m", 0
FLEX_NUMBER, FLEX_LENGTH = "85627", 680
QUELLEN = [
    "https://shop.tillig.com/de/schmalspurbahnen/nenngroesse-h0m/gleissystem/h0m-elite-gleissystem/ (Flexgleis 680 mm; Weiche 18°, Radius 490, Länge 155 mm; Stand 10/2026)",
]

# Gleise mit Regel- und Schmalspur (Dreischienen-Weichen, Abzweige, Kreuzungen H0/H0m) fehlen: Das Programm kennt nur eine Spurweite je Gleis.
TEILE = [
    *WW(85632, 85631, 155, 490, 18),
    PB(50),
]

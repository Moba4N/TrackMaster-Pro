"""TILLIG ELITE-Gleis H0e. Maße nach den Artikelangaben im TILLIG-Onlineshop."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0e", "tillig-h0e", "Tillig H0e", 0
FLEX_NUMBER, FLEX_LENGTH = "85626", 680
QUELLEN = [
    "https://shop.tillig.com/de/schmalspurbahnen/nenngroesse-h0e/gleissystem-30/ (Flexgleis 680 mm; Weiche 18°, Radius 409, Länge 128 mm; Stand 10/2026)",
]

# Gleise mit Regel- und Schmalspur (Dreischienen-Weichen, Abzweige, Kreuzungen H0/H0e) fehlen: Das Programm kennt nur eine Spurweite je Gleis.
# Bausatz Weiche 85641 (Länge 128 mm): gleiche Form wie die Fertigweiche
ALIASE = {"85641 L": 85638, "85641 R": 85637}

TEILE = [
    *WW(85638, 85637, 128, 409, 18),
    PB(40),
]

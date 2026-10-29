"""KATO UNITRACK Schmalspur (H0e / 00-9, Spurweite 9 mm). Maße nach dem KATO-UNITRACK-Katalog (deutsche Ausgabe)."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0e", "kato-h0e", "Kato Unitrack H0e", 2
QUELLEN = ["KATO UNITRACK Katalog Spur N / H0e / H0 (deutsche Ausgabe): Schmalspurgleise NS124, NS62, NS62F, R183-45"]

TEILE = [
    S("52-120", 124), S("52-140", 62), S("52-141", 62, "Anschlussgleis"),
    C("52-220", 183, 45),
    PB(40),
]

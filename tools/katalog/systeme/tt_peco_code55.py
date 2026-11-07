"""PECO Streamline TT:120 Code 55 (Weichen und Flexgleis). Maße nach den PECO-Artikelseiten (Stand 11/2026)."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "TT", "peco-55-tt", "Peco Streamline Code 55 (TT:120)", 10
GLEISABSTAND = 43  # PECO nennt keinen Gleisabstand; üblicher TT-Abstand
QUELLEN = [
    "https://peco-uk.com/collections/track/tt-120 und Artikelseiten SLU1291, SLU1295, SLU1296, SLU1294, SL1200 "
    "(Länge, Radius und Winkel, Stand 11/2026)",
    "Setrack TT:120 (ST1250–ST1272) fehlt: PECO gibt Länge, Radius und Winkel noch nicht an („TBC“)",
]
FLEX_NUMBER, FLEX_LENGTH = "SL1200", 914  # Flexgleis Holzschwellen


def nachlauf(laenge, r, a):
    w = math.radians(a)
    v = round((laenge - r * math.sin(w)) / math.cos(w), 2)
    return v if v >= 0.5 else 0


TEILE = [
    # Klein: 135 mm, R 466, 11,25°. Die Artikelnummer der linken Weiche ist bei PECO noch nicht veröffentlicht.
    W("SLU1292", False, 135, 466, 11.25, zusatz="klein", zusatz_en="small radius", nachlauf=nachlauf(135, 466, 11.25), ungeprueft=True),
    W("SLU1291", True, 135, 466, 11.25, zusatz="klein", zusatz_en="small radius", nachlauf=nachlauf(135, 466, 11.25)),
    # Mittel: 180 mm, R 922,6, 11,25° (Unifrog)
    *WW("SLU1296", "SLU1295", 180, 922.6, 11.25, zusatz="mittel", zusatz_en="medium radius", nachlauf=nachlauf(180, 922.6, 11.25)),
    X("SLU1294", 180, 11.25),
    PB(50),
]

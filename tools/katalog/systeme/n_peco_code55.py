"""PECO Streamline N Code 55 (Weichen und Flexgleis). Maße nach den PECO-Artikelseiten (Stand 11/2026)."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "N", "peco-55-n", "Peco Streamline Code 55 (N)", 10
GLEISABSTAND = 33.6  # PECO nennt keinen Gleisabstand; üblicher N-Abstand
QUELLEN = [
    "https://peco-uk.com/products/ Streamline N Code 55: SLE391F/SLE392F, SLU395F/SLU396F, SLE388F/SLE389F, SLE386F/SLE387F, "
    "SLE397F, SLE399F, SLE393F, SLE394F, SL394F, SLE380F, SL380F, SLE390F, SL390F, SL300F, SL302F "
    "(Länge, Radius und Winkel laut Artikelseite, Stand 11/2026)",
]
FLEX_NUMBER, FLEX_LENGTH = "SL300F", 914  # Flexgleis Holzschwellen
MORE_FLEX = [dict(nr="SL302F", len=914, label="Betonschwellen")]


def nachlauf(laenge, r, a):
    """Gerades Stück nach dem Abzweigbogen bis zum Weichenende (Länge in Richtung des Stammgleises)."""
    w = math.radians(a)
    return round((laenge - r * math.sin(w)) / math.cos(w), 2)


# Alle Weichen 10°. Klein: 123 mm, R 305; mittel: 137 mm, R 457 (Unifrog); groß: 164 mm, R 914
TEILE = [
    *WW("SLE392F", "SLE391F", 123, 305, 10, zusatz="klein", zusatz_en="small radius", nachlauf=nachlauf(123, 305, 10)),
    *WW("SLU396F", "SLU395F", 137, 457, 10, zusatz="mittel", zusatz_en="medium radius", nachlauf=nachlauf(137, 457, 10)),
    *WW("SLE389F", "SLE388F", 164, 914, 10, zusatz="groß", zusatz_en="large radius", nachlauf=nachlauf(164, 914, 10)),
    # Y-Weiche: 127 mm, R 762, zusammen 10° (je 5°)
    Y("SLE397F", 762, 5, nachlauf=nachlauf(127, 762, 5)),
    # Bogenweichen: 160 mm, R 457 innen / 914 außen, 10° Unterschied; Winkel der Stränge aus Länge und Radien abgeleitet
    *[dict(b, unchecked=True) for b in BKK("SLE387F", "SLE386F", 914, 457, 10, 20)],
    # Asymmetrische Dreiwegweiche: 153 mm, R 457, 10°; Versatz der Abzweige nicht angegeben, daher beide am Anfang
    W3("SLE399F", 153, 457, 10, nachlauf=nachlauf(153, 457, 10), ungeprueft=True),
    X("SLE394F", 154, 10), X("SL394F", 154, 10), X("SLE393F", 104, 20),
    X("SLE380F", 154, 10, "EKW"), X("SL380F", 154, 10, "EKW"),
    X("SLE390F", 154, 10, "DKW"), X("SL390F", 154, 10, "DKW"),
    PB(40),
]

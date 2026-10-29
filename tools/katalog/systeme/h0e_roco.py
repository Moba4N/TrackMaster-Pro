"""ROCO H0e-Schmalspur- und Feldbahngleis. Maße nach dem ROCO-Zubehörkatalog 2022 (S. 58–59)."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0e", "roco-h0e", "Roco H0e", 3
QUELLEN = ["ROCO Zubehör-Katalog 2022: H0e-Gleise, Schmalspur- und Feldbahngleise (S. 58–59)"]
FLEX_NUMBER, FLEX_LENGTH = "32200", 730
MORE_FLEX = [dict(nr="32201", len=730, label="Feldbahn-Flexgleis")]

# Die Schmalspurweichen 32409/32411 fehlen: der Katalog nennt Länge und Winkel, aber keinen Abzweigradius.
TEILE = [
    S(32202, 134.4), S(32203, 47.9, "Ausgleichsgerade", "Adjustment straight", cat="Gerade"),
    C(32204, 261.8, 30), C(32205, 493, 13.1),
    *WW(32401, 32403, 104.2, 194.6, 24, zusatz="Feldbahn", zusatz_en="field railway"),
    PB(40),
]

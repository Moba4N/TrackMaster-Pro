"""ROCO GEOLINE (H0, Gleis mit Bettung). Maße nach der GEOLINE-Anleitung und den Produktblättern des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "geo", "Roco geoLine", 4
FLEX_NUMBER, FLEX_LENGTH = "61106", 785
QUELLEN = [
    "ROCO GEOLINE-Anleitung 8051250920: Systemraster 385 × 76,5 mm aus Weiche 22,5° (200 mm), Ausgleichsgerade 185 mm und "
    "Gegenbogen mit dem Abzweigradius der Weiche; Radien R2, R3, R4 im Abstand von 76,5 mm, 30°",
    "ROCO Produktblätter 61106, 61110–61113, 61118–61120, 61122–61124, 61128–61130, 61140, 61141, 61154, 61155, 61160, 61164 "
    "(Länge, Radius, Winkel, Stand 10/2026)",
]

# Parallelgleisabstand 76,5 mm. Abzweigradius aller Weichen und Radius des Gegenbogens GB22,5: 502,7 mm. Der Abzweig
# endet ohne gerades Stück: 2 × 502,7 · sin 22,5° = 385 mm, das Systemraster.
ABSTAND, R_WEICHE = 76.5, 502.7

TEILE = [
    S(61110, 200), S(61111, 185), S(61112, 76.5), S(61113, 100),
    S(61118, 100, "Entkupplungsgleis elektrisch", "Electric uncoupling track"), S(61119, 100, "Entkupplungsgleis"),
    S(61120, 100, "Übergangsgleis", "Transition track"),
    C(61122, 358, 30, "R2"), C(61123, 434.5, 30, "R3"), C(61124, 511.1, 30, "R4"),
    C(61129, 358, 7.5, "R2"), C(61130, 434.5, 7.5, "R3"),
    C(61128, R_WEICHE, 22.5),
    *WW(61140, 61141, 200, R_WEICHE, 22.5),
    W3(61160, 200, R_WEICHE, 22.5),
    X(61164, 200, 22.5, "DKW"),
    # Stamm- und Abzweiggleis R3 (434,5 mm), 30°; der Außenstrang beginnt nach 76,5 mm Gerade
    *BWW(61154, 61155, 434.5, 30, ABSTAND, innen_stamm=True),
    PB(60),
]

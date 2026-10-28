"""Märklin K-Gleis (H0, Mittelleiter). Maße nach Märklin-Katalog (K-Gleis-Seiten) und den Produktseiten."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "mk", "Märklin K-Gleis", 2
THREE_RAIL = True
FLEX_NUMBER, FLEX_LENGTH = "2205", 900
QUELLEN = [
    "Märklin-Katalog, Seiten K-Gleis (Übersicht der Artikel 22xx mit Längen, Radien und Winkeln)",
    "https://www.maerklinshop.de/en/maerklin/gauge-h0/railtracks/k-gauge/ (Produktseiten, Stand 10/2026)",
    "https://www.marklin.com/products/details/article/2269 (Bogenweiche: Parallelkreisabstand 64,6 mm)",
]

M = 14 + 26 / 60   # 14° 26'

# frühere Weichen 2272/2273: gleiche Form wie 22715/22716 (Gegenbogen 2274 laut Katalog für beide)
ALIASE = {"2272": 22715, "2273": 22716}

TEILE = [
    S(2204, 22.5), S(2203, 30), S(2208, 35.1), S(2293, 41.3), S(2202, 45), S(2201, 90), S(2207, 156), S(2206, 168.9),
    S(2200, 180), S(2209, 217.9),
    C(2210, 295.4, 45),
    C(2224, 360, 7.5), C(2223, 360, 15), C(2221, 360, 30),
    C(2235, 424.6, 3.75), C(2234, 424.6, 7.5), C(2233, 424.6, 15), C(2232, 424.6, 22.5), C(2231, 424.6, 30),
    C(2241, 553.9, 30), C(2251, 618.5, 30),
    # Gegenbogen zur Weiche 22715/22716: 14° 26', Radius wie deren Abzweig (902,4 mm)
    C(2274, 902.4, M),
    # Weichen 168,9 mm, Abzweig 22° 30' wie Bogen 2232 (R 424,6 mm)
    *WW(2262, 2263, 168.9, 424.6, 22.5),
    *WW(2265, 2266, 168.9, 424.6, 22.5),
    *WW(22715, 22716, 225, 902.4, M, zusatz="schlank", zusatz_en="slim"),
    W3(2270, 168.9, 424.6, 22.5),
    # Bogenweiche: Innenbogen wie 2221 (R 360 mm, 30°), Außenbogen 30° im Parallelkreisabstand 64,6 mm
    *BWW(2268, 2269, 360, 30, 64.6),
    X(2260, 168.9, 22.5, "DKW"),
    X(2275, 225, M, "DKW"),
    X(2259, 168.9, 22.5),
    X(2258, 90, 45),
    X(2257, 225, M),
    S(2290, 180, "Anschlussgleis", cat="Sonstige"),
    S(2292, 180, "Anschlussgleis", cat="Sonstige"),
    S(2291, 180, "Übergangsgleis", cat="Sonstige"),
    S(2297, 90, "Entkupplungsgleis", cat="Sonstige"),
    S(2299, 90, "Schaltgleis", cat="Sonstige"),
    C(2229, 360, 15, name="Schaltgleis gebogen", name_en_basis="Curved contact track"),
    C(2239, 424.6, 15, name="Schaltgleis gebogen", name_en_basis="Curved contact track"),
]

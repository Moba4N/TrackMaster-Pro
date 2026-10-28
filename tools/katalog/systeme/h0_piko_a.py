"""PIKO A-Gleis (H0). Maße nach dem PIKO A-Gleis-Prospekt (99556_2023)."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "piko", "Piko A-Gleis", 6
FLEX_LENGTH = 940
QUELLEN = [
    "PIKO A-Gleis-Prospekt 99556_2023, https://www.piko-shop.de/de/medien/piko-a-gleis-prospekt,-gratis-download-3125/vw_name-download.htm",
    "Bogenweichen 30°: Artikelbezeichnung „Bogenweiche … 30°“ im Handel (PIKO 55222/55223)",
]

TEILE = [
    S(55205, 61.88), S(55204, 107.32), S(55203, 115.46), S(55202, 119.54), S(55201, 230.93), S(55200, 239.07),
    C(55251, 360, 7.5, "R1"), C(55211, 360, 30, "R1"),
    C(55252, 421.88, 7.5, "R2"), C(55212, 421.88, 30, "R2"),
    C(55213, 483.75, 30, "R3"), C(55214, 545.63, 30, "R4"),
    C(55219, 907.97, 15, "R9"),
    # Weiche 15°: gerade G239, Abzweig R9
    *WW(55220, 55221, 239.07, 907.97, 15),
    W3(55225, 239.07, 907.97, 15),
    Y(55226, 907.97, 15),
    # Bogenweichen R2 → R3 bzw. R3 → R4, Parallelabstand 61,88 mm (Gerade G62)
    *BWW(55222, 55223, 421.88, 30, 61.88),
    *BWW(55227, 55228, 483.75, 30, 61.88),
    X(55224, 239.07, 15, "DKW", gespiegelt=True),
    X(55240, 239.07, 15, gespiegelt=True),
    X(55241, 119.54, 30, gespiegelt=True),
    PB(60),
]

"""TILLIG TT-Modellgleis. Maße nach den Artikelangaben im TILLIG-Onlineshop und den Bedienungsanleitungen."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "TT", "tillig", "Tillig Modellgleis", 0
FLEX_NUMBER, FLEX_LENGTH = "83125", 664
MORE_FLEX = [dict(nr="83134", len=520, label="Betonschwellenflexgleis"), dict(nr="83136", len=520, label="Stahlschwellenflexgleis")]
QUELLEN = [
    "https://shop.tillig.com/de/tt-modellbahnen/modellgleissystem/ (Artikel mit Länge, Radius und Abzweigwinkel, Stand 10/2026)",
    "TILLIG Bedienungsanleitungen 368321 (EW 1: Einbaulänge 129,5 mm, Abzweigradius 353 mm, 15°), 368361 (Kreuzung 15°: 166 mm)",
    "TILLIG „TT-Modellgleis – Gleisentwicklung“: Gleisabstand 43 mm",
    "ROCO Zubehör-Katalog 2022, S. 60: TT-Drehscheibe 35900 (Außendurchmesser 257 mm, Bühne 183 mm, Abgänge im Winkel von 15°, bis 24)",
]

# EW 1: Abzweig R 353 mm / 15°, danach gerade bis zum halben Gleisabstand (43 mm / 2), wie bei der Gleisverbindung aus
# zwei EW 1. Die Länge des geraden Stücks nennt TILLIG nicht; sie folgt aus Radius, Winkel und Gleisabstand.
EW1_NACHLAUF = round((43 / 2 - 353 * (1 - math.cos(math.radians(15)))) / math.sin(math.radians(15)), 2)

# frühere Artikelnummern gleicher Form
ALIASE = {"83321": 83328, "83322": 83329, "83160": 83161, "83361": 83363, "83362": 83364}

TEILE = [
    S(83120, 21.3), S(83104, 36.5), S(83103, 41.5), S(83105, 43), S(83102, 83), S(83101, 166),
    C(83115, 267, 7.5, "R04"), C(83116, 267, 30, "R01"),
    C(83113, 310, 7.5, "R14"), C(83110, 310, 15, "R12"), C(83109, 310, 30, "R11"),
    C(83114, 353, 7.5, "R24"), C(83107, 353, 15, "R22"), C(83106, 353, 30, "R21"),
    C(83112, 396, 15, "R32"), C(83111, 396, 30, "R31"),
    *WW(83329, 83328, 129.5, 353, 15, nachlauf=EW1_NACHLAUF),
    # EW 2: Abzweig 15°, Radius 631, Länge 166 mm · EW 3: Abzweig 12°, Radius 984, Länge 207 mm
    *WW(83332, 83331, 166, 631, 15),
    *WW(83334, 83333, 166, 631, 15, zusatz="Betonschwelle", zusatz_en="concrete ties"),
    *WW(83342, 83341, 207, 984, 12),
    *WW(83344, 83343, 207, 984, 12, zusatz="Betonschwelle", zusatz_en="concrete ties"),
    # Innenbogenweiche: Abzweig 15° mit Radius 631, Abzweig 30° mit Radius 310
    *BKK(83364, 83363, 631, 310, 15, 30),
    # Außenbogenweichen (symmetrisch): 15° (2 × 7,5°), Radius 1.273, Länge 167 mm · 12° (2 × 6°), Radius 1.986, Länge 208 mm
    Y(83380, 1273, 7.5), Y(83382, 1986, 6),
    X(83300, 166, 15, "DKW"),
    X(83391, 208.6, 15, "DKW"),
    X(83161, 166, 15),
    S(83132, 57, "Übergangsgleis", cat="Sonstige"),
    S(83143, 166, "Anschlussgleis", cat="Sonstige"), S(83149, 166, "Anschlussgleis", cat="Sonstige"),
    S(83150, 41.5, "Trenngleis", cat="Sonstige"), S(83151, 41.5, "Trenngleis", cat="Sonstige"),
    S(83155, 41.5, "Unterbrechergleis", "Interrupter track", cat="Sonstige"), S(83156, 41.5, "Unterbrechergleis", "Interrupter track", cat="Sonstige"),
    S(83158, 41.5, "Schaltgleis", cat="Sonstige"), S(83159, 41.5, "Schaltgleis", cat="Sonstige"),
    S(83201, 83, "Entkupplungsgleis", cat="Sonstige"),
    P(83100, 41.5),
    # Schwellenbänder (für Flexgleis-Profile) in den Maßen der Gleisstücke
    S(83001, 166, "Schwellenband", "Tie strip", cat="Sonstige"),
    *[C(nr, r, a, name="Schwellenband", name_en_basis="Tie strip", cat="Sonstige") for nr, r, a in
      [(83016, 267, 30), (83003, 285, 15), (83008, 310, 30), (83009, 310, 15), (83006, 353, 30), (83007, 353, 15), (83011, 396, 30), (83012, 396, 15)]],
    DS(35900, 257, 183, 24, 15, "Drehscheibe (Roco)", "Turntable (Roco)"),
]

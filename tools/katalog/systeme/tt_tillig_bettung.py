"""TILLIG TT-Bettungsgleis (grau). Maße nach den Artikelangaben im TILLIG-Onlineshop."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "TT", "tillig-bett", "Tillig Bettungsgleis", 2
QUELLEN = [
    "https://shop.tillig.com/de/tt-modellbahnen/bettungsgleissystem/ (Artikel mit Länge, Radius und Abzweigwinkel, Stand 10/2026)",
]

# Weiche 15°: Abzweig R 353 mm, Länge 129,5 mm, wie die EW 1 des Modellgleises; das gerade Stück nach dem Bogen folgt
# aus dem Gleisabstand 43 mm (Gleisstück G3).
NACHLAUF = round((43 / 2 - 353 * (1 - math.cos(math.radians(15)))) / math.sin(math.radians(15)), 2)

# frühere Nummern gleicher Form
ALIASE = {"83816/818": 83816, "83817/819": 83817, "83361": 83861, "83362": 83862}

TEILE = [
    S(83701, 166, cat="Gerade"), S(83702, 83), S(83703, 41.5), S(83704, 36.5), S(83705, 43),
    # Pass-Stücke: Gleis wie G5 bzw. R22, nur die Bettung ist angepasst
    S(83721, 36.5, "Pass-Stück rechts", "Fitting piece, right", cat="Gerade"),
    S(83722, 36.5, "Pass-Stück links", "Fitting piece, left", cat="Gerade"),
    C(83709, 310, 30, "R11"), C(83710, 310, 15, "R12"),
    C(83706, 353, 30, "R21"), C(83707, 353, 15, "R22"),
    C(83770, 396, 30, "R31"), C(83771, 396, 15, "R32"),
    C(83772, 346, 15, "R3", "Übergangsbogen", "Transition curve", cat="Bögen"),
    C(83723, 353, 15, None, "Pass-Stück rechts", "Fitting piece, right", cat="Bögen"),
    C(83724, 353, 15, None, "Pass-Stück links", "Fitting piece, left", cat="Bögen"),
    C(83773, 396, 30, None, "Pass-Stück rechts", "Fitting piece, right", cat="Bögen"),
    C(83774, 396, 30, None, "Pass-Stück links", "Fitting piece, left", cat="Bögen"),
    C(83775, 353, 15, None, "Pass-Stück rechts", "Fitting piece, right", cat="Bögen"),
    C(83776, 353, 15, None, "Pass-Stück links", "Fitting piece, left", cat="Bögen"),
    *WW(83817, 83816, 129.5, 353, 15, nachlauf=NACHLAUF),
    # Innenbogenweiche: Abzweig 15° R 631 und 30° R 310
    *BKK(83862, 83861, 631, 310, 15, 30),
    X(83760, 166, 15),
    X(83790, 166, 15, "DKW"),
    X(83792, 166, 15, "DKW"),
    S(83758, 83, "Schaltgleis"),
    S(83801, 83, "Entkupplungsgleis"),
    P(83700, 43),
]

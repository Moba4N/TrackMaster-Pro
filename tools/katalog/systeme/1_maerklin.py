"""Märklin Spur 1. Maße nach den Produktseiten des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "1", "m1", "Märklin Spur 1", 0
QUELLEN = [
    "https://www.maerklinshop.de/en/ Spur 1, Gleise (Produktseiten der Artikel 590xx, Stand 10/2026)",
]

TEILE = [
    S(59051, 59.5), S(59052, 79), S(59053, 100), S(59054, 116), S(59055, 150), S(59057, 200), S(59058, 300),
    S(59059, 600), S(59061, 900),
    C(59035, 1020, 22.5), C(59072, 1176, 22.5),
    C(59075, 1394, 15), C(59073, 1394, 22.5),
    C(59074, 1550, 15), C(59076, 1550, 22.5),
    C(59077, 1715, 22.5), C(59078, 2321, 10), C(59071, 2461, 10),
    # Weichenset: Länge 600 mm, Radius 2.321 mm, Weichenwinkel 10°
    *WW(59084, 59083, 600, 2321, 10),
    PB(150),
]

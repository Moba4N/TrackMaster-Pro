"""Gemeinsame Geometrie des ROCO LINE-Gleises (mit und ohne Bettung), nach dem ROCO-Zubehörkatalog 2022."""
import math
from katalog import *

QUELLEN = ["ROCO Zubehör-Katalog 2022: ROCO LINE Geometrie, mit Bettung und ohne Bettung (S. 34–52)"]

# Parallelgleisabstand 61,6 mm; Weichen enden am Rastermaß mit halbem Gleisabstand. Das gerade Stück nach dem
# Abzweigbogen folgt aus Länge, Radius und Winkel (Abzweigende auf der Weichenlänge).
ABSTAND = 61.6
R = {"R2": 358, "R3": 419.6, "R4": 481.2, "R5": 542.8, "R6": 604.4, "R9": 826.4, "R10": 888, "R20": 1962}


def nachlauf(laenge, r, a):
    return round((laenge - r * math.sin(math.radians(a))) / math.cos(math.radians(a)), 2)


N15, N10 = nachlauf(230, 873.5, 15), nachlauf(345, 1946, 10)


def bogen(nr, rname, a, viertel=False):
    return C(nr, R[rname], a, f"{rname} ¼" if viertel else rname)


def weichen15(links, rechts):
    return WW(links, rechts, 230, 873.5, 15, nachlauf=N15)


def weichen10(links, rechts):
    return WW(links, rechts, 345, 1946, 10, nachlauf=N10)


def dreiweg(nr):
    """Asymmetrisch: erste Abzweigung links, zweite rechts nach einer Viertelgeraden (57,5 mm)."""
    return W3(nr, 287.5, 873.5, 15, vorlauf=(0, 57.5), nachlauf=N15)


def bogenweichen(links, rechts, rname):
    """Stamm- und Abzweiggleis mit gleichem Radius, 30°; der Außenstrang beginnt nach einem geraden Stück von 61,6 mm."""
    return BWW(links, rechts, R[rname], 30, ABSTAND)


def dgv15(nr):
    """Mittelstück für doppelte Gleisverbindung: zwei Diagonalen DG1 (119 mm) unter ±15°, Baulänge 115 mm."""
    l, a = 119, 15
    dy = round(l * math.sin(math.radians(a)), 4)
    return teil(nr, "Crossing", "Mittelstück doppelte Gleisverbindung", "115 mm · 2 × 15°", "Kreuzungen",
                "Double crossover centre piece", len=115, a=30,
                routes=[dict(x=0, y=0, h=a, s=[[l, 0, 0, 0]]), dict(x=0, y=dy, h=-a, s=[[l, 0, 0, 0]])])

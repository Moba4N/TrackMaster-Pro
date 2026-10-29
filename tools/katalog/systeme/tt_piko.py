"""
Piko TT: allgemeines Gleissystem aus TrackPlan selbst (keine Herstellerartikel, Kurzzeichen statt Artikelnummern),
unverändert übernommen.
"""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "TT", "piko-tt", "Piko TT", 4
VERIFIED = False
QUELLEN = ["TrackPlan (eigene Maße)"]

TEILE = [
    {"nr": "G166", "kind": "Straight", "name": "Gerade", "spec": "166 mm", "cat": "Gerade", "len": 166},
    {"nr": "G83", "kind": "Straight", "name": "Gerade", "spec": "83 mm", "cat": "Gerade", "len": 83},
    {"nr": "G41,5", "kind": "Straight", "name": "Gerade", "spec": "41,5 mm", "cat": "Gerade", "len": 41.5},
    {"nr": "G43", "kind": "Straight", "name": "Gerade", "spec": "43 mm", "cat": "Gerade", "len": 43},
    {"nr": "R1", "kind": "Curve", "name": "Bogen R1 · 30°", "spec": "R 267 mm", "cat": "Bögen", "r": 267, "a": 30, "nameEn": "Curve R1 · 30°"},
    {"nr": "R2", "kind": "Curve", "name": "Bogen R2 · 30°", "spec": "R 310 mm", "cat": "Bögen", "r": 310, "a": 30, "nameEn": "Curve R2 · 30°"},
    {"nr": "R3", "kind": "Curve", "name": "Bogen R3 · 30°", "spec": "R 353 mm", "cat": "Bögen", "r": 353, "a": 30, "nameEn": "Curve R3 · 30°"},
    {"nr": "R4", "kind": "Curve", "name": "Bogen R4 · 30°", "spec": "R 396 mm", "cat": "Bögen", "r": 396, "a": 30, "nameEn": "Curve R4 · 30°"},
    {"nr": "WL", "kind": "Switch", "name": "Weiche links", "spec": "166 mm · R 396 · 15°", "cat": "Weichen", "len": 166, "r": 396, "a": 15},
    {"nr": "WR", "kind": "Switch", "name": "Weiche rechts", "spec": "166 mm · R 396 · 15°", "cat": "Weichen", "len": 166, "r": 396, "a": 15, "right": True},
    {"nr": "K15", "kind": "Crossing", "name": "Kreuzung 15°", "spec": "166 mm", "cat": "Kreuzungen", "len": 166, "a": 15},
    {"nr": "PB", "kind": "Buffer", "name": "Prellbock", "spec": "41,5 mm", "cat": "Sonstige", "len": 41.5},
]

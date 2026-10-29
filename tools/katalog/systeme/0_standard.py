"""
Spur 0 Standard: allgemeines Gleissystem aus TrackPlan selbst (keine Herstellerartikel, Kurzzeichen statt Artikelnummern),
unverändert übernommen.
"""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "0", "std0", "Spur 0 Standard", 0
VERIFIED = False
QUELLEN = ["TrackPlan (eigene Maße)"]

TEILE = [
    {"nr": "G500", "kind": "Straight", "name": "Gerade", "spec": "500 mm", "cat": "Gerade", "len": 500},
    {"nr": "G250", "kind": "Straight", "name": "Gerade", "spec": "250 mm", "cat": "Gerade", "len": 250},
    {"nr": "G125", "kind": "Straight", "name": "Gerade", "spec": "125 mm", "cat": "Gerade", "len": 125},
    {"nr": "G1000", "kind": "Straight", "name": "Gerade", "spec": "1.000 mm", "cat": "Gerade", "len": 1000},
    {"nr": "R1", "kind": "Curve", "name": "Bogen R1 · 22,5°", "spec": "R 1.000 mm", "cat": "Bögen", "r": 1000, "a": 22.5, "nameEn": "Curve R1 · 22.5°"},
    {"nr": "R2", "kind": "Curve", "name": "Bogen R2 · 22,5°", "spec": "R 1.250 mm", "cat": "Bögen", "r": 1250, "a": 22.5, "nameEn": "Curve R2 · 22.5°"},
    {"nr": "R3", "kind": "Curve", "name": "Bogen R3 · 15°", "spec": "R 1.500 mm", "cat": "Bögen", "r": 1500, "a": 15, "nameEn": "Curve R3 · 15°"},
    {"nr": "R1/11", "kind": "Curve", "name": "Bogen R1 · 11,3°", "spec": "R 1.000 mm", "cat": "Bögen", "r": 1000, "a": 11.25, "nameEn": "Curve R1 · 11.3°"},
    {"nr": "WL", "kind": "Switch", "name": "Weiche links", "spec": "500 mm · R 2.000 · 15°", "cat": "Weichen", "len": 500, "r": 2000, "a": 15},
    {"nr": "WR", "kind": "Switch", "name": "Weiche rechts", "spec": "500 mm · R 2.000 · 15°", "cat": "Weichen", "len": 500, "r": 2000, "a": 15, "right": True},
    {"nr": "WL3", "kind": "Switch", "name": "Weiche links schlank", "spec": "750 mm · R 3.000 · 12°", "cat": "Weichen", "len": 750, "r": 3000, "a": 12, "nameEn": "Left turnout schlank"},
    {"nr": "WR3", "kind": "Switch", "name": "Weiche rechts schlank", "spec": "750 mm · R 3.000 · 12°", "cat": "Weichen", "len": 750, "r": 3000, "a": 12, "right": True, "nameEn": "Right turnout schlank"},
    {"nr": "DWW", "kind": "Switch", "name": "Dreiwegweiche", "spec": "500 mm · R 2.000 · 15°", "cat": "Weichen", "len": 500, "r": 2000, "a": 15, "paths": [[[500, 0, 0, 0]], [[0, 2000, 15, -1]], [[0, 2000, 15, 1]]]},
    {"nr": "BWL", "kind": "Switch", "name": "Bogenweiche links", "spec": "R 1.250 / 1.500 · 22,5°", "cat": "Weichen", "r": 1250, "a": 22.5, "paths": [[[0, 1500, 22.5, -1]], [[0, 1250, 22.5, -1]]], "unchecked": True},
    {"nr": "BWR", "kind": "Switch", "name": "Bogenweiche rechts", "spec": "R 1.250 / 1.500 · 22,5°", "cat": "Weichen", "r": 1250, "a": 22.5, "right": True, "paths": [[[0, 1500, 22.5, 1]], [[0, 1250, 22.5, 1]]], "unchecked": True},
    {"nr": "K15", "kind": "Crossing", "name": "Kreuzung 15°", "spec": "500 mm", "cat": "Kreuzungen", "len": 500, "a": 15},
    {"nr": "DKW15", "kind": "Crossing", "name": "DKW 15°", "spec": "500 mm", "cat": "Kreuzungen", "len": 500, "a": 15},
    {"nr": "PB", "kind": "Buffer", "name": "Prellbock", "spec": "150 mm", "cat": "Sonstige", "len": 150},
]

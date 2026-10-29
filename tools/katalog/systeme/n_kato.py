"""KATO UNITRACK Spur N. Maße nach dem KATO-UNITRACK-Katalog (deutsche Ausgabe) und der Produktübersicht des Herstellers."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "N", "kato", "Kato Unitrack", 0
QUELLEN = [
    "KATO UNITRACK Katalog Spur N / H0e / H0 (deutsche Ausgabe): Artikellisten, Gleisgeometrie und Abmessungen (S. 8–28)",
    "https://unitrack.katomodels.com/products/line_single (Gleise mit Betonschwellen, Stand 10/2026)",
]

# Die Kurzzeichen von KATO nennen die Maße selbst: S248 = gerade 248 mm, R315-45 = Radius 315 mm, 45°,
# EP718-15L = elektrische Weiche links, Abzweig Radius 718 mm, 15°. Gleisabstand 33 mm.
ABSTAND = 33

# frühere Nummern gleicher Form
ALIASE = {"20-430/1/2/3/4": "20-430", "20-435/6/7/8": "20-436", "20-450/1/2/3/4": "20-450", "20-455/6/7/8": "20-455",
          "20-460/1/2/3/4": "20-460", "20-091A": "20-091/S45", "20-091B": "20-091/S29", "20-092A": "20-092/S33",
          "20-092B": "20-092/S38", "20-320-1": "20-320", "S45.5 (incl w/20-320)": "20-091/S45",
          "20-182 L": "20-182", "20-182 R": "20-182", "20-184 L": "20-184", "20-184 R": "20-184",
          "20-186 L": "20-186", "20-186 R": "20-186", "20-545 L": "20-545", "20-545 R": "20-545"}


def VIA(nr, laenge):
    return S(nr, laenge, "Viadukt", "Viaduct", cat="Sonstige")


def BR(nr, laenge):
    return S(nr, laenge, "Brückengleis", cat="Sonstige")


TEILE = [
    S("20-000", 248), S("20-010", 186), S("20-020", 124), S("20-030", 64), S("20-040", 62),
    S("20-875", 248, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    S("20-007", 248, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    S("20-028", 124, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    S("20-054", 62, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    # Ausgleichsstücke: Set 20-091 (S29, S45,5), Set 20-092 (S33, S38)
    S("20-091/S29", 29, cat="Gerade"), S("20-091/S45", 45.5, cat="Gerade"),
    S("20-092/S33", 33, cat="Gerade"), S("20-092/S38", 38, cat="Gerade"),
    C("20-176", 117, 45), C("20-174", 150, 45), C("20-172", 183, 45),
    C("20-170", 216, 45), C("20-171", 216, 15), C("20-100", 249, 45), C("20-101", 249, 15),
    C("20-110", 282, 45), C("20-111", 282, 15), C("20-120", 315, 45), C("20-121", 315, 15),
    C("20-124", 315, 45, name="Bogen Betonschwellen", name_en_basis="Curve, concrete ties", cat="Bögen"),
    C("20-132", 348, 45), C("20-130", 348, 30), C("20-140", 381, 30), C("20-160", 481, 15), C("20-150", 718, 15),
    # Weichen: #6 EP718-15 (186 mm), #4 EP481-15 (126 mm: mit S60 und S62 zusammen 248 mm), Compact EP150-45 (124 mm)
    *WW("20-202", "20-203", 186, 718, 15),
    *WW("20-220", "20-221", 126, 481, 15),
    Y("20-222", 481, 15),
    *WW("20-240", "20-241", 124, 150, 45, zusatz="Compact"),
    # Kreuzungen X15L/X15R 186 mm, X90 124 mm
    X("20-300", 186, 15, gespiegelt=True), X("20-301", 186, 15), X("20-320", 124, 90),
    GW("20-230", 248, ABSTAND, rechts=False), GW("20-231", 248, ABSTAND, rechts=True),
    GW("20-210", 310, ABSTAND, doppelt=True),
    # Doppelgleise mit Betonschwellen (WS…PC) und Plattenfahrbahn (WS…S), gebogen WR…, Abstand 33 mm
    DG("20-004", 248, ABSTAND), DG("20-012", 186, ABSTAND), DG("20-023", 124, ABSTAND), DG("20-042", 62, ABSTAND),
    DG("20-006", 248, ABSTAND, "Doppelgleis Plattenfahrbahn", "Double track, slab"),
    DG("20-014", 186, ABSTAND, "Doppelgleis Plattenfahrbahn", "Double track, slab"),
    DG("20-025", 124, ABSTAND, "Doppelgleis Plattenfahrbahn", "Double track, slab"),
    DG("20-044", 62, ABSTAND, "Doppelgleis Plattenfahrbahn", "Double track, slab"),
    DB("20-185", 480, 447, 45, form="gegen"), DB("20-186", 480, 447, 22.5, form="gegen"),
    DB("20-181", 414, 381, 45), DB("20-182", 414, 381, 22.5),
    DB("20-183", 315, 282, 45, form="gegen"), DB("20-184", 315, 282, 22.5, form="gegen"),
    DB("20-187", 414, 381, 45), DB("20-188", 414, 381, 22.5),
    VIA("20-400", 248), VIA("20-410", 186), VIA("20-420", 124), VIA("20-440", 62), VIA("20-421", 124),
    *[C(nr, r, a, name="Viadukt", name_en_basis="Viaduct", cat="Sonstige") for nr, r, a in
      [("20-505", 249, 45), ("20-510", 282, 45), ("20-520", 315, 45), ("20-530", 348, 45), ("20-531", 348, 30), ("20-540", 381, 30)]],
    DG("20-401", 248, ABSTAND, "Viadukt doppelgleisig", "Double track viaduct"),
    DG("20-411", 186, ABSTAND, "Viadukt doppelgleisig", "Double track viaduct"),
    DG("20-422", 124, ABSTAND, "Viadukt doppelgleisig", "Double track viaduct"),
    DB("20-544", 414, 381, 45, "Viadukt doppelgleisig", "Double track viaduct", form="innen"),
    DB("20-545", 414, 381, 22.5, "Viadukt doppelgleisig", "Double track viaduct"),
    *[BR(nr, 248) for nr in ("20-429", "20-430", "20-431", "20-432", "20-433", "20-434")],
    *[BR(nr, 186) for nr in ("20-450", "20-451", "20-452", "20-453", "20-454")],
    *[BR(nr, 124) for nr in ("20-460", "20-461", "20-462", "20-464")],
    *[DG(nr, 248, ABSTAND, "Brückengleis doppelgleisig", "Double track bridge") for nr in ("20-436", "20-437", "20-438", "20-439")],
    *[DG(nr, 186, ABSTAND, "Brückengleis doppelgleisig", "Double track bridge") for nr in ("20-455", "20-456", "20-457", "20-458")],
    *[C(nr, 448, 15, name="Brückengleis gebogen", name_en_basis="Curved bridge track", cat="Sonstige") for nr in ("20-465", "20-466", "20-467")],
    *[C(nr, 481, 15, name="Brückengleis gebogen", name_en_basis="Curved bridge track", cat="Sonstige") for nr in ("20-470", "20-471", "20-472")],
    S("20-041", 62, "Anschlussgleis"), S("20-055", 62, "Anschlussgleis"), S("20-045", 62, "Verbindungsgleis"),
    S("20-032", 64, "Entkupplungsgleis"), S("20-026", 124, "Aufgleisgleis"),
    S("20-027-1", 124, "Bahnübergang", "Level crossing"),
    S("20-015", 186, "Grubengleis", "Pit track"), S("20-016", 186, "Grubengleis", "Pit track"),
    S("20-050", 78, "Ausgleichsgleis"),
    S("20-605-1", 124, "Signalgleis", "Signal track"),
    # Prellböcke: Länge des Gleisstücks (S62B, S50B, S66B, S35B)
    P("20-046", 62), P("20-047", 62), P("20-048", 50), P("20-060", 35), P("20-063", 66), P("20-064", 66),
    # Elektrische Drehscheibe: Außendurchmesser 217 mm, Brücke 160 mm, Abgänge im Winkel von 10°
    DS("20-283", 217, 160, 36, 10, "Elektrische Drehscheibe", "Electric turntable"),
]

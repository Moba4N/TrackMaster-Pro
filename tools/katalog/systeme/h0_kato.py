"""KATO UNITRACK Spur H0. Maße nach dem KATO-UNITRACK-Katalog (deutsche Ausgabe), S. 31–39."""
import math
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "H0", "kato-h0", "Kato Unitrack H0", 13
QUELLEN = ["KATO UNITRACK Katalog Spur N / H0e / H0 (deutsche Ausgabe): Artikellisten und „Gleisgeometrie und Abmessungen“ (S. 31)"]

# Gleisabstand 60 mm. Weichen R867 10° laut Gleisgeometrie: EP867 + S94 + S149 = 492 mm (EP867: 249 mm), HP867 + S149 = 492 mm
# (HP867: 343 mm); der Abzweig (R 867, 10°, dann gerade) führt mit S97 (nur EP867) und dem Gegenbogen R867-10 in den
# Abstand 60 mm. Weiche R490 22,5°: HP490 + S246 = 492 mm, Abzweig gleich dem Bogen R490-22.5. Weiche R550 19°: 185 mm.
ABSTAND = 60
GERADE_R867 = (ABSTAND - 2 * 867 * (1 - math.cos(math.radians(10)))) / math.sin(math.radians(10))

# frühere Nummern gleicher Form; die Einzelteile der Weichen-Sets (S94, S97, S114, S149, R867-10) gibt es auch einzeln
ALIASE = {"2-242 L": "2-242", "2-242 R": "2-242", "2-252 L": "2-252", "2-252 R": "2-252", "2-850": "2-852", "2-851": "2-853",
          "S94 (incl w/ #6 TO)": "2-111", "S97 (incl w/ #6 TO)": "2-192", "S114 (incl w/#4 TO)": "2-120",
          "S149 (incl w/ #6 TO)": "2-193", "R867-10° (incl w/ #6 TO)": "2-290", "S61L (incl w/#4 TO)": "S61L",
          "S61R (incl w/#4 TO)": "S61R", "R550-3.5° (incl w/#4 TO)": "R550-3.5", "R550-15.5° (incl w/#4 TO)": "R550-15.5"}

TEILE = [
    S("2-180", 369), S("2-150", 246), S("2-160", 227), S("2-130", 174), S("2-193", 149), S("2-140", 123),
    S("2-120", 114), S("2-192", 97), S("2-111", 94), S("2-105", 60),
    S("2-181", 369, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    S("2-152", 246, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    S("2-141", 123, "Gerade Betonschwellen", "Straight, concrete ties", cat="Gerade"),
    C("2-280", 370, 22.5), C("2-260", 430, 22.5), C("2-270", 490, 22.5), C("2-210", 550, 22.5), C("2-220", 610, 22.5),
    C("2-230", 670, 22.5), C("2-240", 730, 22.5), C("2-250", 790, 22.5), C("2-290", 867, 10),
    *[C(nr, r, a, name="Bogen Betonschwellen", name_en_basis="Curve, concrete ties", cat="Bögen") for nr, r, a in
      [("2-241", 730, 22.5), ("2-242", 730, 22.5), ("2-251", 790, 22.5), ("2-252", 790, 22.5), ("2-321", 1546, 11.25), ("2-331", 1606, 11.25)]],
    *WW("2-860", "2-861", 249, 867, 10, nachlauf=round(GERADE_R867 - 97, 2)),
    *WW("2-862", "2-863", 343, 867, 10, nachlauf=round(GERADE_R867, 2)),
    *WW("2-840", "2-841", 246, 490, 22.5),
    *WW("2-852", "2-853", 185, 550, 19),
    X("2-401", 60, 90),
    # Teile der Sets zur Weiche R550 (S61L/S61R, R550-3.5, R550-15.5)
    S("S61L", 61), S("S61R", 61), C("R550-3.5", 550, 3.5), C("R550-15.5", 550, 15.5),
    S("2-151", 246, "Anschlussgleis"), S("2-153", 246, "Anschlussgleis"),
    S("2-142", 123, "Bahnübergang", "Level crossing"), S("2-143", 123, "Entkupplungsgleis"),
    S("2-601", 123, "Signalgleis", "Signal track"), S("2-194", 212, "Ausgleichsgleis"),
    P("2-170", 109),
]

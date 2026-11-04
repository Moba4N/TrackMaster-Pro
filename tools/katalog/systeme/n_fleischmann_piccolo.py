"""FLEISCHMANN piccolo N-Gleis mit Schotterbett. Maße nach dem FLEISCHMANN-Zubehörkatalog (deutsche Ausgabe)."""
from katalog import *

SPUR, ID, NAME, REIHENFOLGE = "N", "picc", "Roco/Fleischmann piccolo mit Bettung", 2
GLEISABSTAND = 33.6  # Parallelgleisabstand Mitte zu Mitte (mm)
QUELLEN = [
    "FLEISCHMANN Zubehör-Katalog (deutsche Ausgabe): N-Gleissystem mit Schotterbett, Artikelangaben und Gleisgeometrie (S. 26–34)",
    "FLEISCHMANN Übersicht „Das N-Piccolo-Gleis mit Schotterbett“: Artikelliste mit Handbetrieb- und elektromagnetischen Weichen",
]
FLEX_NUMBER, FLEX_LENGTH = "9106", 777
MORE_FLEX = [dict(nr="9119", len=222, label="Zahnstangen-Flexgleis")]

# Ausführungen gleicher Form: Doppelte Kreuzungsweichen 9164–9167 wie 9184–9187
ALIASE = {"9164": "9184", "9165": "9185", "9166": "9186", "9167": "9187"}
EL = dict(zusatz="elektromagnetisch", zusatz_en="electromagnetic")
EN = {"Dreiwegweiche": "Three-way turnout", "Bogenweiche links": "Left curved turnout", "Bogenweiche rechts": "Right curved turnout"}


def el(t):
    """Elektromagnetische Ausführung eines Teils (gleiche Form, Name mit Zusatz)."""
    return dict(t, name=f"{t['name']} elektromagnetisch", nameEn=f"{t.get('nameEn') or EN[t['name']]}, electromagnetic")

TEILE = [
    S(9100, 222), S(9101, 111), S(9102, 57.5), S(9103, 55.5), S(9104, 27.75),
    S(9110, 111, "Ausgleichsstück 83–111 mm", "Adjustable track 83–111 mm", cat="Gerade"),
    S(9108, 111, "Anschlussgleis"), S(9112, 111, "Entkupplungsgleis"), S(9114, 111, "Entkupplungsgleis für Handbetrieb"),
    S(9115, 55.5, "Schaltgleis"),
    # Radien R1 192, R2 225,6, R3 396,4, R4 430 mm; Gleisabstand 33,6 mm
    C(9120, 192, 45, "R1"), C(9122, 192, 15, "R1"), C(9123, 192, 7.5, "R1"),
    C(9125, 225.6, 45, "R2"), C(9127, 225.6, 15, "R2"), C(9128, 225.6, 7.5, "R2"),
    C(9130, 396.4, 30, "R3"), C(9131, 396.4, 15, "R3"),
    C(9135, 430, 30, "R4"), C(9136, 430, 15, "R4"),
    # Weichen 15°, 111 mm, Abzweig R 430 (wie Gegenbogen 9136); 9178/9179 mit polarisiertem Herzstück
    # Handbetrieb 9170/9171 und 9178/9179, elektromagnetisch 9172/9173 und 9180/9181
    *WW(9170, 9171, 111, 430, 15), *WW(9178, 9179, 111, 430, 15),
    *WW(9172, 9173, 111, 430, 15, **EL), *WW(9180, 9181, 111, 430, 15, **EL),
    # Dreiwegweiche: Handbetrieb 9157, elektromagnetisch 9158
    W3(9157, 111, 430, 15), el(W3(9158, 111, 430, 15)),
    # Bogenweichen R1/R2, 45°: Handbetrieb 9174/9175 und 9168/9169, elektromagnetisch 9176/9177 und 9182/9183
    *BKK(9174, 9175, 225.6, 192, 45), *BKK(9168, 9169, 225.6, 192, 45),
    *map(el, BKK(9176, 9177, 225.6, 192, 45)), *map(el, BKK(9182, 9183, 225.6, 192, 45)),
    X(9161, 115, 30), X(9162, 111, 15), X(9163, 111, 15),
    # Doppelte Kreuzungsweichen: Handbetrieb 9184/9185, elektromagnetisch 9186/9187
    X(9184, 111, 15, "DKW"), X(9185, 111, 15, "DKW"), el(X(9186, 111, 15, "DKW")), el(X(9187, 111, 15, "DKW")),
    P(9116, 57.5),
]

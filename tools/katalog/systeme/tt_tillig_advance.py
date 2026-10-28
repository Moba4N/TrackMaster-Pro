"""
„Tillig Advance“: frühere Zusammenstellung des TILLIG-Modellgleises mit älteren Artikelnummern. Die Teile sind die des
TT-Modellgleises (tt_tillig.py); die alten Nummern zeigen auf die heutigen Artikel gleicher Form.
"""
import importlib.util
import pathlib
from katalog import *

_spec = importlib.util.spec_from_file_location("tt_tillig", pathlib.Path(__file__).with_name("tt_tillig.py"))
_tillig = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_tillig)

SPUR, ID, NAME, REIHENFOLGE = "TT", "tillig-adv", "Tillig Advance", 1
FLEX_NUMBER, FLEX_LENGTH, MORE_FLEX = _tillig.FLEX_NUMBER, _tillig.FLEX_LENGTH, _tillig.MORE_FLEX
QUELLEN = _tillig.QUELLEN
ALIASE = {**_tillig.ALIASE, "83321/26": 83328, "83322/27": 83329, "83300/391": 83300}
TEILE = _tillig.TEILE

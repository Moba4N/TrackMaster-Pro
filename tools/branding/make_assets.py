"""Erzeugt Programmsymbol, Installer-Bilder und App-Symbole aus docs/branding/TrackMaster-Pro-Logo.png.

Aufruf im Repository-Ordner: python3 tools/branding/make_assets.py  (braucht Pillow)
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
DARK = (0x1F, 0x24, 0x28)
GREEN = (0x76, 0xAD, 0x33)

logo = Image.open(ROOT / "docs/branding/TrackMaster-Pro-Logo.png").convert("RGBA")
logo = logo.crop(logo.split()[3].getbbox())  # durchsichtigen Rand abschneiden


def fit(img, size):
    return img.resize((size, size), Image.LANCZOS)


# Bildteil ohne Schriftzug für kleine Symbole (16–32 px): Gleise und Stift auf dunklem Grund
w, h = logo.size
pic = logo.crop((int(w * 0.17), int(h * 0.08), int(w * 0.89), int(h * 0.70)))
def small(size):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=max(2, size // 5), fill=DARK + (255,))
    inner = int(size * 0.86)
    p = pic.resize((inner, int(inner * pic.height / pic.width)), Image.LANCZOS)
    img.alpha_composite(p, ((size - p.width) // 2, (size - p.height) // 2))
    return img

# Windows-Symbol: große Größen mit dem ganzen Logo, kleine mit dem Bildteil
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [small(s) if s <= 32 else fit(logo, s) for s in sizes]
frames[-1].save(ROOT / "src/TrackMaster.App/TrackMaster.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
fit(logo, 512).save(ROOT / "src/TrackMaster.App/Assets/Logo.png")
small(64).save(ROOT / "src/TrackMaster.App/Assets/LogoSmall.png")

# Installer: Banner oben (493 × 58) und Seitenbild links (493 × 312, sichtbar sind die linken 164 px)
banner = Image.new("RGB", (493, 58), (255, 255, 255))
b = fit(logo, 50)
banner.paste(b, (493 - 50 - 6, 4), b)
banner.save(ROOT / "installer/Banner.bmp")
dialog = Image.new("RGB", (493, 312), (255, 255, 255))
ImageDraw.Draw(dialog).rectangle([0, 0, 163, 311], fill=DARK)
ImageDraw.Draw(dialog).rectangle([160, 0, 163, 311], fill=GREEN)
d = fit(logo, 136)
dialog.paste(d, ((160 - 136) // 2, 80), d)
dialog.save(ROOT / "installer/Dialog.bmp")

# Android: Vordergrund des adaptiven Symbols (432 px, Logo im sicheren Bereich) und Logo für die App
fg = Image.new("RGBA", (432, 432), (0, 0, 0, 0))
l = fit(logo, 300)
fg.alpha_composite(l, (66, 66))
fg.save(ROOT / "src/TrackMaster.Android/Resources/drawable-xxxhdpi/ic_launcher_foreground.png")
fit(logo, 384).save(ROOT / "src/TrackMaster.Android/Resources/drawable-xxxhdpi/logo.png")

# Handbuch
fit(logo, 600).save(ROOT / "docs/handbuch/img-logo.png")
print("ok")

# Startbild (durchsichtig, wird beim Programmstart kurz gezeigt)
fit(logo, 360).save(ROOT / "src/TrackMaster.App/Assets/Splash.png")

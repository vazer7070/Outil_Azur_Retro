"""Optimise les captures brutes : largeur ≤ 1280 px, PNG indexé (256 couleurs) et compression maximale.

Usage : python3 -I optimize.py <dossier des captures brutes> <dossier de sortie>
Chaque PNG brut remplace le fichier du même nom dans le dossier de sortie ; les autres fichiers sont laissés tels quels.
"""
import os
import sys

from PIL import Image

MAX_WIDTH = 1280


def optimize(source, target):
    with Image.open(source) as image:
        image = image.convert("RGB")
        if image.width > MAX_WIDTH:
            height = round(image.height * MAX_WIDTH / image.width)
            image = image.resize((MAX_WIDTH, height), Image.LANCZOS)
        # Palette adaptative : l'interface Retro a peu de teintes, les décors et sprites restent lisibles.
        indexed = image.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG)
        indexed.save(target, format="PNG", optimize=True)


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    raw, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    total = 0
    for name in sorted(os.listdir(raw)):
        if not name.lower().endswith(".png"):
            continue
        target = os.path.join(out, name)
        optimize(os.path.join(raw, name), target)
        size = os.path.getsize(target)
        total += size
        print("%-28s %4d Ko" % (name, size // 1024))
    print("total des captures produites : %d Ko" % (total // 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())

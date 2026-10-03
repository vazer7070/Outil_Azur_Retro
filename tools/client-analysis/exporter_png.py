#!/usr/bin/env python3
"""Convertit en PNG les SVG produits par swfsvg (cairosvg requis : `pip install cairosvg`).

usage : exporter_png.py <dossier-svg> <dossier-png> [échelle]

Les pixels magenta purs (#FF00FF) restants, emplacements remplis à l'exécution par le client
(transformations de couleur, bitmaps techniques), sont rendus transparents si Pillow est installé."""
import glob, os, sys
import cairosvg
try:
    from PIL import Image
except ImportError:
    Image = None


def effacer_magenta(path):
    if Image is None: return
    im = Image.open(path).convert("RGBA"); px = im.load(); touche = False
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a and r > 235 and g < 25 and b > 235: px[x, y] = (0, 0, 0, 0); touche = True
    if touche: im.save(path)

src, dst = sys.argv[1], sys.argv[2]
scale = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
os.makedirs(dst, exist_ok=True)
ok = failed = 0
for path in sorted(glob.glob(os.path.join(src, "*.svg"))):
    name = os.path.basename(path)[:-4]
    try:
        cairosvg.svg2png(url=path, write_to=os.path.join(dst, name + ".png"), scale=scale, background_color=None)
        effacer_magenta(os.path.join(dst, name + ".png"))
        ok += 1
    except Exception as error:  # un symbole illisible ne doit pas arrêter la série
        failed += 1
        print(f"{name} : {error}")
print(f"{ok} PNG écrits, {failed} échecs")

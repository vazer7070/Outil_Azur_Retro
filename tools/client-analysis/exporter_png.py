#!/usr/bin/env python3
"""Convertit en PNG les SVG produits par swfsvg (cairosvg requis : `pip install cairosvg`).

usage : exporter_png.py <dossier-svg> <dossier-png> [échelle]"""
import glob, os, sys
import cairosvg

src, dst = sys.argv[1], sys.argv[2]
scale = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
os.makedirs(dst, exist_ok=True)
ok = failed = 0
for path in sorted(glob.glob(os.path.join(src, "*.svg"))):
    name = os.path.basename(path)[:-4]
    try:
        cairosvg.svg2png(url=path, write_to=os.path.join(dst, name + ".png"), scale=scale, background_color=None)
        ok += 1
    except Exception as error:  # un symbole illisible ne doit pas arrêter la série
        failed += 1
        print(f"{name} : {error}")
print(f"{ok} PNG écrits, {failed} échecs")

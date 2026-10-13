#!/usr/bin/env python3
"""Éléments d'interface des objets interactifs (clavier de code, documents) exportés de core.swf pour le bot.

usage : exporter_ui_interactifs.py <core.swf> <dossier de sortie> --swfsvg <binaire swfsvg> [--travail DOSSIER]

Symboles lus dans `modules/core.swf` du client 1.34 :
  - UI_KeyCodeSymbol0 à 9 : chiffres du clavier `KeyCode` -> code-<n>.png ;
  - UI_DocumentBook, UI_DocumentParchment, UI_DocumentRoadSignLeft : fonds des documents
    -> document-livre.png, document-parchemin.png, document-pancarte.png.

Chaîne : `swfsvg` (symboles nommés) puis `exporter_png.py` à l'échelle 2 (aplats magenta rendus
transparents), puis Pillow : marges transparentes retirées ; pour le parchemin, bitmap JPEG sans
transparence, le fond noir relié aux bords est rendu transparent ; fonds réduits à 380 px de large
et palette de 256 couleurs. Rien n'est exécuté depuis le SWF.
"""

import argparse
import glob
import os
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw

ICI = os.path.dirname(os.path.abspath(__file__))
CHIFFRES = ["UI_KeyCodeSymbol%d" % n for n in range(10)]
FONDS = {
    "UI_DocumentBook": "document-livre",
    "UI_DocumentParchment": "document-parchemin",
    "UI_DocumentRoadSignLeft": "document-pancarte",
}
LARGEUR_FOND = 380


def recadrer(image, marge=2):
    boite = image.getbbox()
    if boite is None:
        return image
    g, h, d, b = boite
    return image.crop((max(0, g - marge), max(0, h - marge), min(image.width, d + marge), min(image.height, b + marge)))


def fond_noir_transparent(image):
    """Le parchemin est un bitmap opaque posé sur du noir : le noir relié aux bords devient transparent."""
    image = image.convert("RGBA")
    marque = (255, 0, 255, 0)
    for point in ((0, 0), (image.width - 1, 0), (0, image.height - 1), (image.width - 1, image.height - 1)):
        r, v, b, a = image.getpixel(point)
        if r < 24 and v < 24 and b < 24:
            ImageDraw.floodfill(image, point, marque, thresh=24)
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x, y] == marque:
                pixels[x, y] = (0, 0, 0, 0)
    return image


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    p.add_argument("core_swf")
    p.add_argument("sortie")
    p.add_argument("--swfsvg", required=True)
    p.add_argument("--travail")
    a = p.parse_args()

    travail = a.travail or tempfile.mkdtemp(prefix="ui-interactifs-")
    svg, png = os.path.join(travail, "svg"), os.path.join(travail, "png")
    os.makedirs(svg, exist_ok=True)
    subprocess.run([a.swfsvg, a.core_swf, svg] + CHIFFRES + sorted(FONDS), check=True)
    subprocess.run([sys.executable, os.path.join(ICI, "exporter_png.py"), svg, png, "2"], check=True)
    os.makedirs(a.sortie, exist_ok=True)

    for n, nom in enumerate(CHIFFRES):
        image = recadrer(Image.open(os.path.join(png, nom + ".png")).convert("RGBA"))
        image.save(os.path.join(a.sortie, "code-%d.png" % n), optimize=True)
    for nom, sortie in sorted(FONDS.items()):
        image = Image.open(os.path.join(png, nom + ".png"))
        image = fond_noir_transparent(image) if image.mode != "RGBA" else image
        image = recadrer(image, 0)
        hauteur = max(1, round(image.height * LARGEUR_FOND / image.width))
        image = image.resize((LARGEUR_FOND, hauteur), Image.LANCZOS)
        image = image.quantize(256, method=Image.Quantize.FASTOCTREE)
        image.save(os.path.join(a.sortie, sortie + ".png"), optimize=True)

    total = 0
    for chemin in sorted(glob.glob(os.path.join(a.sortie, "*.png"))):
        taille = os.path.getsize(chemin)
        total += taille
        with Image.open(chemin) as image:
            print("%-24s %4d x %-4d %-4s %7d octets" % (os.path.basename(chemin), image.width, image.height, image.mode, taille))
    print("Total : %d octets ; fichiers intermédiaires : %s" % (total, travail))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Exporte les bustes des classes du client Dofus 1.34 en PNG (fiche du conjoint du volet Amis).

usage : exporter_artworks.py <client> <Resources/Bot> [options]

  <client>         dossier du client (celui qui contient clips/artworks/faces/10.swf)
  <Resources/Bot>  dossier des ressources du bot : écrit Artworks/Faces/<gfx>.png

options :
  --swfsvg CHEMIN     binaire swfsvg (défaut : $SWFSVG, sinon swfsvg/target/release/swfsvg à côté du script) ;
                      un script .py est lancé par cet interpréteur (tests)
  --travail DOSSIER   garde les SVG intermédiaires dans ce dossier (défaut : dossier temporaire supprimé)
  --sans-palette      garde tous les PNG en RGBA 32 bits

La fiche du conjoint du client (SpouseViewer) affiche le buste de sa classe
(GUILDS_FACES_PATH = clips/artworks/faces/<gfx>.swf, gfx = classe × 10 + sexe). Seuls les 24 gfx des
12 classes jouables sont exportés. Les petites illustrations de la liste d'amis (clips/artworks/mini)
sont déjà exportées par exporter_groupe.py dans Artworks/Mini. Chaîne : swfsvg --scene (timeline
principale, image 1) → cairosvg (échelle 1) → Pillow : magenta pur rendu transparent, marges
transparentes découpées, palette de 256 couleurs quand l'écart avec l'original reste dans la
tolérance stricte de exporter_decor.py, sinon RGBA. Les SWF ne sont jamais copiés.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile

try:
    import cairosvg
    from PIL import Image
except ImportError as erreur:  # cairosvg et Pillow sont indispensables
    sys.exit("exporter_artworks.py : %s (pip install cairosvg pillow)" % erreur)

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ICI)
from exporter_decor import TOLERANCE_STRICTE, ecarts_palette  # noqa: E402

CLASSES = range(1, 13)
SOURCE = os.path.join("clips", "artworks", "faces")
DESTINATION = os.path.join("Artworks", "Faces")
ECHELLE = 1.0


def gfx_des_classes():
    return [classe * 10 + sexe for classe in CLASSES for sexe in (0, 1)]


def lanceur(binaire):
    return [sys.executable, binaire] if binaire.endswith(".py") else [binaire]


def rendre(binaire, swf, travail, nom):
    os.makedirs(travail, exist_ok=True)
    resultat = subprocess.run(lanceur(binaire) + ["--scene", "--name", nom, swf, travail], capture_output=True, text=True)
    svg = os.path.join(travail, nom + ".svg")
    if resultat.returncode != 0 or not os.path.exists(svg):
        raise RuntimeError("swfsvg a échoué : %s" % (resultat.stderr or resultat.stdout).strip()[:300])
    return svg


def effacer_magenta(image):
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if a and r > 235 and g < 25 and b > 235:
                pixels[x, y] = (0, 0, 0, 0)


def finaliser(png, destination, palette):
    with Image.open(png) as source:
        image = source.convert("RGBA")
    effacer_magenta(image)
    cadre = image.getchannel("A").getbbox()
    if cadre is None:
        return None
    image = image.crop(cadre)
    ecrite = image
    if palette:
        couleurs = image.getcolors(256)
        if couleurs is not None:
            ecrite = image.quantize(colors=max(2, len(couleurs)), method=Image.Quantize.FASTOCTREE)
        else:
            essai = image.quantize(colors=256, method=Image.Quantize.FASTOCTREE)
            moyenne, centile99, alpha99 = ecarts_palette(image, essai)
            if moyenne <= TOLERANCE_STRICTE[0] and centile99 <= TOLERANCE_STRICTE[1] and alpha99 <= TOLERANCE_STRICTE[2]:
                ecrite = essai
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    ecrite.save(destination, optimize=True)
    return image.width, image.height, ecrite.mode == "P"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("client")
    parser.add_argument("ressources")
    parser.add_argument("--swfsvg", default=os.environ.get("SWFSVG") or os.path.join(ICI, "swfsvg", "target", "release", "swfsvg"))
    parser.add_argument("--travail")
    parser.add_argument("--sans-palette", action="store_true")
    options = parser.parse_args()
    if not os.path.isfile(options.swfsvg):
        sys.exit("swfsvg introuvable : %s (cargo build --release dans tools/client-analysis/swfsvg)" % options.swfsvg)
    travail = options.travail or tempfile.mkdtemp(prefix="artworks-")
    lignes, absents, octets = [], [], 0
    try:
        for gfx in gfx_des_classes():
            nom = str(gfx)
            relatif = os.path.join(SOURCE, nom + ".swf")
            swf = os.path.join(options.client, relatif)
            if not os.path.isfile(swf):
                absents.append("%s : absent" % relatif)
                continue
            destination = os.path.join(options.ressources, DESTINATION, nom + ".png")
            try:
                svg = rendre(options.swfsvg, swf, travail, nom)
                png = os.path.join(travail, nom + ".png")
                cairosvg.svg2png(url=svg, write_to=png, scale=ECHELLE, background_color=None)
                taille = finaliser(png, destination, not options.sans_palette)
            except (RuntimeError, OSError, ValueError) as erreur:
                absents.append("%s : %s" % (relatif, erreur))
                continue
            if taille is None:
                absents.append("%s : rendu vide" % relatif)
                continue
            octets += os.path.getsize(destination)
            lignes.append("%s.png\t%d × %d\t%s" % (nom, taille[0], taille[1], "palette" if taille[2] else "RGBA"))
    finally:
        if not options.travail:
            shutil.rmtree(travail, ignore_errors=True)
    print("%d PNG écrits dans %s (%.1f Ko), %d absents ou en échec"
          % (len(lignes), os.path.join(options.ressources, DESTINATION), octets / 1024.0, len(absents)))
    for ligne in lignes:
        print("  " + ligne)
    for ligne in absents:
        print("  " + ligne)
    return 0 if lignes else 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Exporte les images du volet Groupe du bot depuis le client Dofus 1.34 fourni.

usage : exporter_groupe.py <client> <Resources/Bot> [--swfsvg CHEMIN] [--travail DOSSIER]

- `<client>/clips/artworks/mini/<n>.swf` (illustration posée sur la scène, aucun symbole exporté) :
  `swfsvg --scene --name <n>`, puis `exporter_png.py` à l'échelle 2 → `<Resources/Bot>/Artworks/Mini/<n>.png`.
- `<client>/modules/core.swf` (ou `<client>/../d/modules/core.swf` avec `--core`) : symboles `UI_PartyItem`
  (couronne du chef et flèche du suivi, séparées par la bande transparente qui les sépare),
  `UI_PartyItemInfo` (épée de l'infobulle d'informations) et `UI_FightOptionBlockJoinerExceptPartyMemberUp`
  (trois personnages) → `<Resources/Bot>/Party/{chef,suivi,infos,groupe}.png`, marges transparentes retirées.

Requiert swfsvg 0.2.1 ou plus (`--swfsvg`, sinon `$SWFSVG`, le PATH ou `swfsvg/target/release`), cairosvg et Pillow.
Les SWF du client ne sont jamais copiés dans le dépôt."""
import argparse
import glob
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
ECHELLE = "2"
SYMBOLES = ["UI_PartyItem", "UI_PartyItemInfo", "UI_FightOptionBlockJoinerExceptPartyMemberUp"]


def trouver_swfsvg(chemin):
    for candidat in (chemin, os.environ.get("SWFSVG"), shutil.which("swfsvg"),
                     os.path.join(ICI, "swfsvg", "target", "release", "swfsvg")):
        if candidat and os.path.isfile(candidat):
            return candidat
    sys.exit("swfsvg introuvable : compilez tools/client-analysis/swfsvg ou passez --swfsvg")


def lancer(commande):
    resultat = subprocess.run(commande, capture_output=True, text=True)
    if resultat.returncode != 0:
        sys.exit("échec de " + " ".join(commande) + " :\n" + resultat.stderr)
    return resultat.stdout


def png(svg, sortie):
    lancer([sys.executable, os.path.join(ICI, "exporter_png.py"), svg, sortie, ECHELLE])


def rogner(image):
    boite = image.getbbox()
    return image.crop(boite) if boite else image


def bandes(image):
    """Découpe verticale aux lignes entièrement transparentes : une image par bloc non vide, de haut en bas."""
    alpha = image.getchannel("A")
    largeur, hauteur = image.size
    pleines = [any(alpha.getpixel((x, y)) for x in range(largeur)) for y in range(hauteur)]
    blocs, debut = [], None
    for y, pleine in enumerate(pleines + [False]):
        if pleine and debut is None:
            debut = y
        elif not pleine and debut is not None:
            blocs.append(rogner(image.crop((0, debut, largeur, y))))
            debut = None
    return blocs


def enregistrer(image, chemin):
    image.save(chemin, optimize=True)
    print(f"{os.path.relpath(chemin)} {image.size[0]}×{image.size[1]}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("client")
    parser.add_argument("ressources")
    parser.add_argument("--swfsvg")
    parser.add_argument("--core", help="core.swf (par défaut <client>/modules/core.swf)")
    parser.add_argument("--travail", help="dossier de travail gardé (SVG intermédiaires) ; sinon temporaire, supprimé à la fin")
    args = parser.parse_args()
    swfsvg = trouver_swfsvg(args.swfsvg)
    core = args.core or os.path.join(args.client, "modules", "core.swf")
    if not os.path.isfile(core):
        sys.exit("core.swf introuvable : " + core + " (option --core)")
    if args.travail:
        exporter(args, swfsvg, core, args.travail)
    else:
        with tempfile.TemporaryDirectory(prefix="groupe-") as travail:
            exporter(args, swfsvg, core, travail)


def exporter(args, swfsvg, core, travail):
    # Petites illustrations : scène de chaque SWF, nommée par son numéro.
    mini_svg = os.path.join(travail, "mini-svg")
    os.makedirs(mini_svg, exist_ok=True)
    sources = sorted(glob.glob(os.path.join(args.client, "clips", "artworks", "mini", "*.swf")))
    if not sources:
        sys.exit("aucun SWF dans clips/artworks/mini")
    for source in sources:
        lancer([swfsvg, "--scene", "--name", os.path.splitext(os.path.basename(source))[0], source, mini_svg])
    mini = os.path.join(args.ressources, "Artworks", "Mini")
    os.makedirs(mini, exist_ok=True)
    for ancien in glob.glob(os.path.join(mini, "*.png")):
        os.remove(ancien)
    png(mini_svg, mini)

    # Éléments du volet Party de core.swf.
    ui_svg, ui_png = os.path.join(travail, "ui-svg"), os.path.join(travail, "ui-png")
    os.makedirs(ui_svg, exist_ok=True)
    lancer([swfsvg, core, ui_svg] + SYMBOLES)
    png(ui_svg, ui_png)
    groupe = os.path.join(args.ressources, "Party")
    os.makedirs(groupe, exist_ok=True)
    item = Image.open(os.path.join(ui_png, "UI_PartyItem.png")).convert("RGBA")
    blocs = bandes(item)
    if len(blocs) != 2:
        sys.exit(f"UI_PartyItem : 2 blocs attendus (couronne, flèche), {len(blocs)} trouvés")
    enregistrer(blocs[0], os.path.join(groupe, "chef.png"))
    enregistrer(blocs[1], os.path.join(groupe, "suivi.png"))
    enregistrer(rogner(Image.open(os.path.join(ui_png, "UI_PartyItemInfo.png")).convert("RGBA")), os.path.join(groupe, "infos.png"))
    enregistrer(rogner(Image.open(os.path.join(ui_png, "UI_FightOptionBlockJoinerExceptPartyMemberUp.png")).convert("RGBA")),
                os.path.join(groupe, "groupe.png"))
    garde = f" ; SVG intermédiaires dans {travail}" if args.travail else ""
    print(f"{len(sources)} illustrations, 4 éléments d'interface{garde}")


if __name__ == "__main__":
    main()

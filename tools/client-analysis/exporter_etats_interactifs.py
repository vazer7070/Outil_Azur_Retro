#!/usr/bin/env python3
"""Exporte l'image 2 des objets interactifs du client Dofus 1.34 (objet en cours d'utilisation), avec ses ancres.

usage : exporter_etats_interactifs.py <client> <decor> [options]

  <client>  dossier du client (celui qui contient clips/gfx/o1.swf)
  <decor>   dossier du décor exporté par exporter_decor.py : le script y écrit objets/<id>_<n>.png et
            ancres-etats.tsv, sans toucher aux autres fichiers

options :
  --interactifs XML  textes interactiveobjects exportés par lang2xml.py (défaut :
                     Outil_Azur_complet/Resources/Bot/BotLang/interactiveobjects.xml du dépôt)
  --swfsvg CHEMIN    binaire swfsvg (défaut : $SWFSVG, sinon swfsvg/target/release/swfsvg à côté du script)
  --travail DOSSIER  garde les SVG et PNG intermédiaires dans ce dossier (défaut : dossier temporaire supprimé)
  --taches N         processus en parallèle (défaut : nombre de cœurs)
  --images-max N     dernière image exportée d'un objet (défaut : 2 ; voir plus bas avant de l'augmenter)
  --sans-palette     garde tous les PNG en RGBA 32 bits

Le client pose chaque objet de la couche 2 à son image 1, puis « GDF|cellule;image » la change
(MapHandler.setObject2Frame : gotoAndStop) ; StarLoco envoie 2 au début d'une récolte ou d'un atelier.
Les gfx retenus sont ceux que interactiveobjects rattache à un objet interactif (« gfx id interactif ») et
dont la timeline compte plusieurs images (swfsvg --list, colonne images_timeline). L'image n est rendue par
« swfsvg --frame n », puis traitée comme les objets d'exporter_decor.py : exporter_png.py, découpe des
marges transparentes, palette de 256 couleurs si l'écart reste faible. Un gfx présent dans plusieurs
bibliothèques est pris dans la première (ordre o1… o11), celle dont exporter_decor.py garde l'image 1.

Seule l'image 2 est exportée par défaut : swfsvg rend une image comme si ses clips imbriqués venaient
d'être créés, sans les actions « onClipEvent(load) » ni la fin de leurs animations. Pour l'image 2, le
client montre bien l'objet tel quel ; pour les images 3 à 5, il joue une animation qui s'achève sur une
autre image (arbre : 3 = chute puis image 4, souche ; 5 = repousse puis image 1), que ce rendu ne donne pas.

ancres-etats.tsv a le format d'ancres.tsv : « objet id xmin ymin largeur hauteur image », image ≥ 2.
exporter_decor.py efface les PNG <id>_<n>.png d'objets/ : relancer ce script après lui.
"""
import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from concurrent.futures import ProcessPoolExecutor

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ICI)
import exporter_decor as decor  # noqa: E402  (convertir, reparer_svg, finaliser, lire_index, svg_de)

BIBLIOTHEQUES = ["o%d" % n for n in range(1, 12)]
ANCRES = "ancres-etats.tsv"
INTERACTIFS = os.path.join(ICI, "..", "..", "Outil_Azur_complet", "Resources", "Bot", "BotLang", "interactiveobjects.xml")
ETAT = re.compile(r"^\d+_\d+\.png$")


def gfx_interactifs(chemin):
    """Gfx rattachés à un objet interactif : {gfx : objet} lu dans les balises « gfx id interactif »."""
    gfx = {}
    for element in ET.parse(chemin).getroot().iter("gfx"):
        try:
            identifiant, objet = int(element.get("id", "")), int(element.get("interactif", ""))
        except ValueError:
            continue
        if identifiant > 0:
            gfx[identifiant] = objet
    return gfx


def commande(binaire):
    """Commande swfsvg ; un script .py (faux swfsvg des tests) est lancé par cet interpréteur."""
    return [sys.executable, binaire] if binaire.endswith(".py") else [binaire]


def lister(binaire, swf):
    """« swfsvg --list » : {nom d'export : images de sa timeline}."""
    resultat = subprocess.run(commande(binaire) + ["--list", swf], capture_output=True, text=True)
    if resultat.returncode != 0:
        raise RuntimeError("swfsvg --list a échoué sur %s : %s" % (swf, resultat.stderr.strip()[:400]))
    images = {}
    for ligne in resultat.stdout.splitlines()[1:]:
        champs = ligne.split("\t")
        if len(champs) >= 5 and champs[2] != "scene":
            try:
                images[champs[0]] = max(1, int(champs[4]))
            except ValueError:
                continue
    return images


def numerique(nom):
    """Nom d'export fait de chiffres ASCII (les noms techniques comme « Link_o1 » sont ignorés)."""
    return nom.isascii() and nom.isdigit()


def choisir(listes, gfx, images_max):
    """Objets à exporter : {gfx : (bibliothèque, dernière image)} pour les gfx interactifs de plus d'une image.

    listes : [(bibliothèque, {nom : images})] dans l'ordre o1… o11. La première bibliothèque qui exporte un gfx
    est la seule utilisée, même si une autre lui donne plus d'images : c'est d'elle que vient l'image 1."""
    vus, retenus = set(), {}
    for bibliotheque, images in listes:
        for nom, total in images.items():
            if not numerique(nom) or int(nom) in vus:
                continue
            identifiant = int(nom)
            vus.add(identifiant)
            if identifiant in gfx and total > 1 and images_max >= 2:
                retenus[identifiant] = (bibliotheque, min(total, images_max))
    return retenus


def lots_par_image(retenus):
    """[(bibliothèque, image, [noms])] : un appel de swfsvg par bibliothèque et par image."""
    lots = {}
    for identifiant, (bibliotheque, derniere) in retenus.items():
        for image in range(2, derniere + 1):
            lots.setdefault((bibliotheque, image), []).append(str(identifiant))
    return [(bibliotheque, image, sorted(noms, key=int)) for (bibliotheque, image), noms in sorted(lots.items())]


def rendre(binaire, swf, dossier, image, noms):
    """« swfsvg <swf> <dossier> --frame <image> <noms…> » : un SVG par symbole et index.tsv."""
    os.makedirs(dossier, exist_ok=True)
    resultat = subprocess.run(commande(binaire) + [swf, dossier, "--frame", str(image)] + list(noms), capture_output=True, text=True)
    if resultat.returncode != 0:
        raise RuntimeError("swfsvg a échoué sur %s : %s" % (swf, resultat.stderr.strip()[:400]))


def ecrire_ancres(chemin, ancres):
    """ancres : [(id, xmin, ymin, largeur, hauteur, image)] ; écrit ancres-etats.tsv trié par gfx puis image."""
    with open(chemin, "w", encoding="utf-8", newline="\n") as fichier:
        fichier.write("# type\tid\txmin\tymin\tlargeur\thauteur\timage\n")
        for identifiant, xmin, ymin, largeur, hauteur, image in sorted(ancres, key=lambda ligne: (ligne[0], ligne[5])):
            fichier.write("objet\t%d\t%d\t%d\t%d\t%d\t%d\n" % (identifiant, xmin, ymin, largeur, hauteur, image))


def main():
    parametres = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parametres.add_argument("client")
    parametres.add_argument("decor")
    parametres.add_argument("--interactifs", default=INTERACTIFS)
    parametres.add_argument("--swfsvg", default=os.environ.get("SWFSVG") or os.path.join(ICI, "swfsvg", "target", "release", "swfsvg"))
    parametres.add_argument("--travail")
    parametres.add_argument("--taches", type=int, default=os.cpu_count() or 2)
    parametres.add_argument("--images-max", type=int, default=2)
    parametres.add_argument("--sans-palette", action="store_true")
    options = parametres.parse_args()
    if decor.Image is None:
        sys.exit("Pillow est nécessaire : pip install pillow")
    gfx_dossier = os.path.join(options.client, "clips", "gfx")
    if not os.path.isdir(gfx_dossier):
        sys.exit("dossier introuvable : %s" % gfx_dossier)
    if not os.path.isfile(options.swfsvg):
        sys.exit("swfsvg introuvable : %s (cargo build --release dans swfsvg/, ou --swfsvg)" % options.swfsvg)
    if not os.path.isfile(options.interactifs):
        sys.exit("textes des objets interactifs introuvables : %s" % options.interactifs)
    gfx = gfx_interactifs(options.interactifs)
    travail = os.path.abspath(options.travail or tempfile.mkdtemp(prefix="etats-"))
    os.makedirs(travail, exist_ok=True)
    racine_svg, racine_png = os.path.join(travail, "svg"), os.path.join(travail, "png")
    debut = time.time()
    journal, reprises, avertissements = [], [], []
    try:
        # 1. Images de chaque symbole, puis gfx interactifs à plusieurs images (première bibliothèque).
        listes = []
        for nom in BIBLIOTHEQUES:
            swf = os.path.join(gfx_dossier, nom + ".swf")
            if os.path.exists(swf):
                listes.append((nom, lister(options.swfsvg, swf)))
            else:
                journal.append("bibliothèque absente : %s" % swf)
        retenus = choisir(listes, gfx, options.images_max)
        absents = sorted(set(gfx) - {int(n) for _, images in listes for n in images if numerique(n)})
        if absents:
            journal.append("gfx interactifs absents des bibliothèques : %s" % " ".join(map(str, absents)))
        # 2. SVG de l'image n des symboles retenus.
        lots = []
        for bibliotheque, image, noms in lots_par_image(retenus):
            dossier = os.path.join(racine_svg, "%s-image%d" % (bibliotheque, image))
            rendre(options.swfsvg, os.path.join(gfx_dossier, bibliotheque + ".swf"), dossier, image, noms)
            lots.append((bibliotheque, image, dossier, decor.lire_index(dossier)))
        svg_fin = time.time()

        # 3. PNG (exporter_png.py), avec la reprise des dégradés minuscules d'exporter_decor.py.
        def png_de(dossier_svg):
            return os.path.join(racine_png, os.path.relpath(dossier_svg, racine_svg))
        with ProcessPoolExecutor(max_workers=max(1, options.taches)) as pool:
            for source, code, fin in pool.map(decor.convertir, [lot[2] for lot in lots], [png_de(lot[2]) for lot in lots]):
                if code != 0:
                    journal.append("exporter_png.py a échoué sur %s : %s" % (source, " ".join(fin)))
        for bibliotheque, image, dossier, index in lots:
            for chemin in sorted(glob.glob(os.path.join(dossier, "*.svg"))):
                symbole = os.path.basename(chemin)[:-4]
                if os.path.exists(os.path.join(png_de(dossier), symbole + ".png")):
                    continue
                for minimum in decor.ECHELLES_DEGRADE_MIN:
                    essai = os.path.join(travail, "svg-repris", "%s-%g" % (os.path.relpath(dossier, racine_svg), minimum))
                    decor.reparer_svg(chemin, os.path.join(essai, symbole + ".svg"), minimum)
                    decor.convertir(essai, png_de(dossier))
                    shutil.rmtree(essai, ignore_errors=True)
                    if os.path.exists(os.path.join(png_de(dossier), symbole + ".png")):
                        reprises.append("%s image %d %s : dégradés d'au moins %g" % (bibliotheque, image, symbole, minimum))
                        break
        png_fin = time.time()
        # 4. Découpe et palette ; les images d'un export précédent sont retirées d'abord (objets/<id>_<n>.png).
        taches, cles = [], []
        for bibliotheque, image, dossier, index in lots:
            for symbole, x0, y0, largeur, hauteur, alerte in index:
                if not numerique(symbole) or (int(symbole), image) in cles:
                    continue
                if alerte:
                    avertissements.append("%s %s image %d : %s" % (bibliotheque, symbole, image, alerte))
                svg = decor.svg_de(dossier, symbole)
                png = os.path.join(png_de(dossier), os.path.basename(svg)[:-4] + ".png") if svg else ""
                if not os.path.exists(png):
                    journal.append("PNG absent : %s %s (image %d)" % (bibliotheque, symbole, image))
                    continue
                destination = os.path.join(options.decor, "objets", "%d_%d.png" % (int(symbole), image))
                taches.append((png, destination, x0, y0, not options.sans_palette, "objet"))
                cles.append((int(symbole), image))
        objets = os.path.join(options.decor, "objets")
        if os.path.isdir(objets):
            for nom in os.listdir(objets):
                if ETAT.match(nom) and os.path.isfile(os.path.join(objets, nom)):
                    os.remove(os.path.join(objets, nom))
        ancres, palettes, vides = [], 0, []
        with ProcessPoolExecutor(max_workers=max(1, options.taches)) as pool:
            for (identifiant, image), (resultat, raison) in zip(cles, pool.map(decor.finaliser, taches, chunksize=8)):
                if raison:
                    vides.append("objet %d (image %d) : %s" % (identifiant, image, raison))
                if resultat is None:
                    continue
                xmin, ymin, largeur, hauteur, en_palette = resultat
                palettes += en_palette
                ancres.append((identifiant, xmin, ymin, largeur, hauteur, image))
        os.makedirs(options.decor, exist_ok=True)
        ecrire_ancres(os.path.join(options.decor, ANCRES), ancres)
        taille = sum(os.path.getsize(os.path.join(objets, "%d_%d.png" % (ligne[0], ligne[5]))) for ligne in ancres)
        print("images d'état des objets interactifs : %d gfx, %d images (2 à %d), %d PNG en palette ; %.2f Mo"
              % (len({ligne[0] for ligne in ancres}), len(ancres), max([ligne[5] for ligne in ancres] or [1]), palettes, taille / 1048576))
        print("durées : SVG %.1f s, PNG %.1f s, découpe et palette %.1f s" % (svg_fin - debut, png_fin - svg_fin, time.time() - png_fin))
        for titre, lignes in (("SVG repris avec des dégradés minuscules agrandis", reprises), ("images vides ou illisibles", vides),
                              ("avertissements de swfsvg", avertissements), ("journal", journal)):
            if lignes:
                print("%s : %d" % (titre, len(lignes)))
                for ligne in lignes:
                    print("  " + ligne)
    finally:
        if not options.travail:
            shutil.rmtree(travail, ignore_errors=True)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Exporte le décor des cartes du client Dofus 1.34 (sols, objets, fonds) en PNG, avec leurs ancres.

usage : exporter_decor.py <client> <sortie> [options]

  <client>  dossier du client (celui qui contient clips/gfx/g1.swf)
  <sortie>  dossier écrit : sols/, objets/, backgrounds/, cellules/ et ancres.tsv

options :
  --swfsvg CHEMIN     binaire swfsvg (défaut : $SWFSVG, sinon swfsvg/target/release/swfsvg à côté du script)
  --travail DOSSIER   garde les SVG et PNG intermédiaires dans ce dossier (défaut : dossier temporaire supprimé)
  --taches N          processus en parallèle (défaut : nombre de cœurs)
  --seuil-fond N      taille en pixels au-delà de laquelle un symbole de g1/g2 est un fond (défaut : 500)
  --sans-palette      garde tous les PNG en RGBA 32 bits (pas de passage en palette)
  --sans-pentes       n'exporte pas les images 2 à 15 des sols (sols en pente)

Chaîne : swfsvg (symboles exportés de clips/gfx/g1, g2, o1…o11 et cell.swf, image 1) → exporter_png.py
(cairosvg, magenta → transparent) → Pillow : découpe des marges transparentes, passage en palette de
256 couleurs quand l'écart reste faible (voir TOLERANCES), écriture de <dossier>/<id>.png et des ancres.

ancres.tsv : « type id xmin ymin largeur hauteur image ». (xmin, ymin) est la position du pixel en haut à
gauche du PNG par rapport au point d'enregistrement du symbole, c'est-à-dire par rapport à la position de
la cellule dans le client (MapHandler.build : x = colonne × 53, + 26,5 une ligne sur deux ; y = ligne ×
13,5 − 20 × (niveau − 7)). Le bot dessine donc le PNG à « position de la cellule + (xmin, ymin) ».
type : sol, objet, fond (symbole de g1/g2 de plus de --seuil-fond pixels, utilisé par backgroundNum et
attaché à l'origine de la carte), selection (s1…s15 de cell.swf, dossier cellules/), interaction
(i1…i15). image : 1, ou n ≥ 2 pour l'image n d'un sol (fichier sols/<id>_<n>.png), que le client affiche
sur une cellule de pente n (gotoAndStop(groundSlope)).

Les identifiants d'objets présents dans deux bibliothèques o* sont dédoublonnés : la première
occurrence (ordre o1, o2… o11) est gardée, les autres sont journalisées. Un symbole entièrement
transparent (le client n'y dessine rien) devient un PNG transparent de 1 px, ancré comme les autres : le bot
ne le confond pas avec un PNG absent, qu'il remplace par une couleur à plat.
"""
import argparse
import glob
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import zlib
from concurrent.futures import ProcessPoolExecutor

try:
    from PIL import Image, ImageChops
except ImportError:  # Pillow est indispensable pour la découpe et les ancres
    Image = ImageChops = None

ICI = os.path.dirname(os.path.abspath(__file__))
BIBLIOTHEQUES = [("g1", "sol"), ("g2", "sol")] + [("o%d" % n, "objet") for n in range(1, 12)] + [("cell", "cellule")]
DOSSIERS = {"sol": "sols", "objet": "objets", "fond": "backgrounds", "selection": "cellules", "interaction": "cellules"}
PENTES = 15  # groundSlope tient sur 4 bits ; le client n'utilise que 1 à 15

# Écarts tolérés entre un PNG RGBA et sa version en palette de 256 couleurs (octree de Pillow) : moyenne et
# 99e centile du plus mauvais canal de couleur sur les pixels opaques, 99e centile de l'alpha. Sols, fonds et
# cellules restent presque sans perte (les grands dégradés des fonds se postérisent vite) ; les objets
# acceptent un écart léger. Au-delà, le PNG reste en RGBA.
TOLERANCES = {"objet": (5.5, 22, 64)}
TOLERANCE_STRICTE = (1.5, 6, 8)

DEGRADE = re.compile(r'gradientTransform="matrix\(([^)]*)\)"')
# Échelles minimales essayées tour à tour pour un SVG que cairo refuse (0,01 × 1 638,4 unités ≈ 16 px).
ECHELLES_DEGRADE_MIN = (0.01, 0.02, 0.05)


def lire_tags(donnees, debut):
    """Parcourt les tags SWF (code, contenu) d'un flux décompressé."""
    position = debut
    while position + 2 <= len(donnees):
        entete = struct.unpack_from("<H", donnees, position)[0]
        position += 2
        code, longueur = entete >> 6, entete & 63
        if longueur == 63:
            longueur = struct.unpack_from("<I", donnees, position)[0]
            position += 4
        yield code, donnees[position:position + longueur]
        position += longueur
        if code == 0:
            break


def images_par_symbole(chemin):
    """Nombre d'images de chaque symbole exporté (DefineSprite) d'un SWF : {nom : images}."""
    with open(chemin, "rb") as fichier:
        brut = fichier.read()
    corps = zlib.decompress(brut[8:]) if brut[:3] == b"CWS" else brut[8:]
    debut = (5 + (corps[0] >> 3) * 4 + 7) // 8 + 4
    sprites, exports = {}, {}
    for code, contenu in lire_tags(corps, debut):
        if code == 39 and len(contenu) >= 4:  # DefineSprite
            identifiant, images = struct.unpack_from("<HH", contenu)
            sprites[identifiant] = images
        elif code == 56 and len(contenu) >= 2:  # ExportAssets
            nombre, position = struct.unpack_from("<H", contenu)[0], 2
            for _ in range(nombre):
                identifiant = struct.unpack_from("<H", contenu, position)[0]
                fin = contenu.index(b"\0", position + 2)
                exports[contenu[position + 2:fin].decode("latin-1")] = identifiant
                position = fin + 1
    return {nom: sprites.get(identifiant, 1) for nom, identifiant in exports.items()}


def lire_index(dossier):
    """index.tsv de swfsvg : nom, identifiant, xmin, ymin, largeur, hauteur, avertissements."""
    lignes = []
    chemin = os.path.join(dossier, "index.tsv")
    if not os.path.exists(chemin):
        return lignes
    with open(chemin, encoding="utf-8") as fichier:
        for ligne in fichier:
            champs = ligne.rstrip("\n").split("\t")
            if len(champs) < 6:
                continue
            try:
                lignes.append((champs[0], float(champs[2]), float(champs[3]), float(champs[4]), float(champs[5]),
                               champs[6] if len(champs) > 6 else ""))
            except ValueError:
                continue
    return lignes


def swfsvg(binaire, swf, sortie, *arguments):
    os.makedirs(sortie, exist_ok=True)
    resultat = subprocess.run([binaire, swf, sortie, *arguments], capture_output=True, text=True)
    if resultat.returncode != 0:
        raise RuntimeError("swfsvg a échoué sur %s : %s" % (swf, resultat.stderr.strip()[:400]))


def svg_de(dossier, nom):
    """SVG d'un symbole dans un dossier de swfsvg (le mode --frame peut suffixer le nom)."""
    exact = os.path.join(dossier, nom + ".svg")
    if os.path.exists(exact):
        return exact
    candidats = sorted(glob.glob(os.path.join(dossier, nom + "_*.svg")))
    return candidats[0] if candidats else None


def sonder_images(binaire, swf, nom, travail):
    """Vrai si ce swfsvg rend l'image N d'un symbole avec « --frame N » (option prévue de swfsvg).

    Un swfsvg sans ce mode prend « --frame » et « 2 » pour des noms d'export et rend l'image 1 : les deux
    SVG sont alors identiques."""
    une, deux = os.path.join(travail, "sonde-1"), os.path.join(travail, "sonde-2")
    try:
        swfsvg(binaire, swf, une, nom)
        swfsvg(binaire, swf, deux, "--frame", "2", nom)
    except RuntimeError:
        return False
    a, b = svg_de(une, nom), svg_de(deux, nom)
    if not a or not b:
        return False
    with open(a, "rb") as fa, open(b, "rb") as fb:
        return fa.read() != fb.read()


def convertir(source, destination):
    """exporter_png.py (inchangé) : cairosvg puis magenta → transparent."""
    resultat = subprocess.run([sys.executable, os.path.join(ICI, "exporter_png.py"), source, destination, "1"],
                              capture_output=True, text=True)
    return source, resultat.returncode, (resultat.stdout + resultat.stderr).strip().splitlines()[-1:]


def reparer_svg(source, destination, minimum):
    """Copie d'un SVG dont les dégradés minuscules sont agrandis, pour les seuls symboles que cairo refuse.

    swfsvg écrit les matrices avec trois décimales : un dégradé de quelques pixels devient presque singulier
    (« matrix(0.003 0 0 0.002 …) ») et cairo échoue (CAIRO_STATUS_NO_MEMORY). Ces dégradés colorent de petits
    détails, toujours découpés par leur forme : on les remplace par un dégradé centré au même point."""
    def remplacer(trouve):
        try:
            a, b, c, d, e, f = (float(valeur) for valeur in trouve.group(1).split())
        except ValueError:
            return trouve.group(0)
        determinant = abs(a * d - b * c)
        if determinant >= minimum ** 2:
            return trouve.group(0)
        echelle = max(math.sqrt(determinant), minimum)
        return 'gradientTransform="matrix(%g 0 0 %g %g %g)"' % (echelle, echelle, e, f)
    with open(source, encoding="utf-8") as fichier:
        texte = fichier.read()
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    with open(destination, "w", encoding="utf-8") as fichier:
        fichier.write(DEGRADE.sub(remplacer, texte))


def centile(histogramme, total, quantile):
    cumul = 0
    for valeur, nombre in enumerate(histogramme):
        cumul += nombre
        if cumul >= quantile * total:
            return valeur
    return 255


def ecarts_palette(original, palette):
    """(moyenne, 99e centile couleur, 99e centile alpha) entre une image RGBA et sa version en palette."""
    difference = ImageChops.difference(original, palette.convert("RGBA"))
    pixels = original.width * original.height
    opaques = original.getchannel("A").point(lambda alpha: 255 if alpha >= 128 else 0)
    nombre = opaques.histogram()[255] or 1
    moyenne = centile99 = 0
    for canal in difference.split()[:3]:
        histogramme = ImageChops.darker(canal, opaques).histogram()
        histogramme[0] -= pixels - nombre
        moyenne = max(moyenne, sum(valeur * compte for valeur, compte in enumerate(histogramme)) / nombre)
        centile99 = max(centile99, centile(histogramme, nombre, 0.99))
    return moyenne, centile99, centile(difference.getchannel("A").histogram(), pixels, 0.99)


def finaliser(tache):
    """Découpe, palette éventuelle et écriture d'un PNG ; renvoie la ligne d'ancres ou une raison d'échec."""
    png, destination, x0, y0, palette, type_ = tache
    try:
        with Image.open(png) as source:
            image = source.convert("RGBA")
    except (OSError, ValueError) as erreur:
        return None, "illisible : %s" % erreur
    cadre = image.getchannel("A").getbbox()
    vide = cadre is None
    if vide:  # le client n'y dessine rien : un pixel transparent ancré évite la couleur à plat de « PNG absent »
        cadre = (0, 0, 1, 1)
    image = image.crop(cadre)
    # Le SVG commence au pixel entier sous (xmin, ymin) du symbole : la découpe s'y ajoute.
    xmin, ymin = int(math.floor(x0)) + cadre[0], int(math.floor(y0)) + cadre[1]
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    ecrite = image
    if palette:
        couleurs = image.getcolors(256)
        if couleurs is not None:  # 256 couleurs RGBA au plus : palette exacte
            ecrite = image.quantize(colors=max(2, len(couleurs)), method=Image.Quantize.FASTOCTREE)
        else:
            essai = image.quantize(colors=256, method=Image.Quantize.FASTOCTREE)
            moyenne, centile99, alpha99 = ecarts_palette(image, essai)
            limite = TOLERANCES.get(type_, TOLERANCE_STRICTE)
            if moyenne <= limite[0] and centile99 <= limite[1] and alpha99 <= limite[2]:
                ecrite = essai
    ecrite.save(destination, optimize=True)
    return (xmin, ymin, image.width, image.height, ecrite.mode == "P"), ("entièrement transparent : PNG de 1 px" if vide else None)


def main():
    parametres = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parametres.add_argument("client")
    parametres.add_argument("sortie")
    parametres.add_argument("--swfsvg", default=os.environ.get("SWFSVG") or os.path.join(ICI, "swfsvg", "target", "release", "swfsvg"))
    parametres.add_argument("--travail")
    parametres.add_argument("--taches", type=int, default=os.cpu_count() or 2)
    parametres.add_argument("--seuil-fond", type=int, default=500)
    parametres.add_argument("--sans-palette", action="store_true")
    parametres.add_argument("--sans-pentes", action="store_true")
    options = parametres.parse_args()
    if Image is None:
        sys.exit("Pillow est nécessaire : pip install pillow")
    gfx = os.path.join(options.client, "clips", "gfx")
    if not os.path.isdir(gfx):
        sys.exit("dossier introuvable : %s" % gfx)
    if not os.path.isfile(options.swfsvg):
        sys.exit("swfsvg introuvable : %s (cargo build --release dans swfsvg/, ou --swfsvg)" % options.swfsvg)
    travail = os.path.abspath(options.travail or tempfile.mkdtemp(prefix="decor-"))
    os.makedirs(travail, exist_ok=True)
    racine_svg, racine_png = os.path.join(travail, "svg"), os.path.join(travail, "png")
    debut = time.time()
    journal = []
    try:
        # 1. SVG de l'image 1 de chaque symbole exporté.
        lots = []  # (bibliothèque, genre, image, dossier svg, index)
        for nom, genre in BIBLIOTHEQUES:
            swf = os.path.join(gfx, nom + ".swf")
            if not os.path.exists(swf):
                journal.append("bibliothèque absente : %s" % swf)
                continue
            dossier = os.path.join(racine_svg, nom)
            swfsvg(options.swfsvg, swf, dossier)
            lots.append((nom, genre, 1, dossier, lire_index(dossier)))
        # 2. Images 2 à 15 des sols (pentes), si swfsvg sait rendre une image donnée.
        if not options.sans_pentes:
            multiples = {}
            for nom, genre in BIBLIOTHEQUES:
                swf = os.path.join(gfx, nom + ".swf")
                if genre == "sol" and os.path.exists(swf):
                    multiples[nom] = {symbole: images for symbole, images in images_par_symbole(swf).items()
                                      if images > 1 and symbole.isdigit()}
            exemple = next(((nom, symbole) for nom, table in multiples.items() for symbole in sorted(table)), None)
            if exemple and sonder_images(options.swfsvg, os.path.join(gfx, exemple[0] + ".swf"), exemple[1], travail):
                for nom, table in multiples.items():
                    for image in range(2, PENTES + 1):
                        voulus = sorted(symbole for symbole, images in table.items() if images >= image)
                        if voulus:
                            dossier = os.path.join(racine_svg, "%s-image%d" % (nom, image))
                            swfsvg(options.swfsvg, os.path.join(gfx, nom + ".swf"), dossier, "--frame", str(image), *voulus)
                            lots.append((nom, "sol", image, dossier, lire_index(dossier)))
            elif exemple:
                journal.append("pentes : ce swfsvg ne rend pas « --frame N » ; seuls les sols à plat (image 1) sont exportés")
        svg_fin = time.time()
        # 3. SVG → PNG avec exporter_png.py, un processus par dossier ; nouvel essai pour les SVG que cairo refuse.
        def png_de(dossier_svg):
            return os.path.join(racine_png, os.path.relpath(dossier_svg, racine_svg))
        with ProcessPoolExecutor(max_workers=max(1, options.taches)) as pool:
            for source, code, fin in pool.map(convertir, [lot[3] for lot in lots], [png_de(lot[3]) for lot in lots]):
                if code != 0:
                    journal.append("exporter_png.py a échoué sur %s : %s" % (source, " ".join(fin)))
        reprises = []
        for nom, genre, image, dossier, index in lots:
            for chemin in sorted(glob.glob(os.path.join(dossier, "*.svg"))):
                symbole = os.path.basename(chemin)[:-4]
                cible = os.path.join(png_de(dossier), symbole + ".png")
                if os.path.exists(cible):
                    continue
                for minimum in ECHELLES_DEGRADE_MIN:
                    essai = os.path.join(travail, "svg-repris", "%s-%g" % (os.path.relpath(dossier, racine_svg), minimum))
                    reparer_svg(chemin, os.path.join(essai, symbole + ".svg"), minimum)
                    convertir(essai, png_de(dossier))
                    shutil.rmtree(essai, ignore_errors=True)
                    if os.path.exists(cible):
                        reprises.append("%s %s : dégradés d'au moins %g" % (os.path.relpath(dossier, racine_svg), symbole, minimum))
                        break
        png_fin = time.time()
        # 4. Destinations, décidées dans l'ordre des bibliothèques (dédoublonnage : première occurrence gardée).
        taches, cles, retenus, vus, doublons, fonds, avertissements = [], [], {}, set(), [], [], []
        for nom, genre, image, dossier, index in lots:
            for symbole, x0, y0, largeur, hauteur, alerte in index:
                if (nom, image, symbole) in vus:
                    continue  # même nom exporté plusieurs fois par le même SWF
                vus.add((nom, image, symbole))
                if alerte:
                    avertissements.append("%s %s : %s" % (nom, symbole, alerte))
                if genre == "cellule":
                    if len(symbole) < 2 or symbole[0] not in "si" or not symbole[1:].isdigit():
                        continue
                    type_, identifiant, fichier = ("selection" if symbole[0] == "s" else "interaction"), int(symbole[1:]), symbole
                else:
                    if not symbole.isdigit():
                        continue  # « [Link_g1-ground] » et autres noms techniques
                    identifiant, type_ = int(symbole), genre
                    if genre == "sol" and image == 1 and max(largeur, hauteur) > options.seuil_fond:
                        type_ = "fond"
                        fonds.append("%s %d : %d × %d px" % (nom, identifiant, round(largeur), round(hauteur)))
                    fichier = str(identifiant) if image == 1 else "%d_%d" % (identifiant, image)
                cle = (type_, identifiant, image)
                if cle in retenus:
                    doublons.append("%s %d : %s et %s, %s gardé" % (type_, identifiant, retenus[cle], nom, retenus[cle]))
                    continue
                svg = svg_de(dossier, symbole)
                png = os.path.join(png_de(dossier), os.path.basename(svg)[:-4] + ".png") if svg else ""
                if not os.path.exists(png):
                    journal.append("PNG absent : %s %s (image %d)" % (nom, symbole, image))
                    continue
                retenus[cle] = nom
                taches.append((png, os.path.join(options.sortie, DOSSIERS[type_], fichier + ".png"), x0, y0,
                               not options.sans_palette, type_))
                cles.append(cle)
        # 5. Découpe, palette et écriture en parallèle, après retrait des PNG d'un export précédent (seulement les
        #    noms que ce script écrit à la racine de chaque dossier : une ancienne bibliothèque rangée en
        #    sous-dossiers, comme sols/Herbe/, reste intacte).
        exporte = re.compile(r"^(\d+(_\d+)?|[si]\d+)\.png$")
        for dossier in sorted(set(DOSSIERS.values())):
            chemin = os.path.join(options.sortie, dossier)
            if os.path.isdir(chemin):
                for nom in os.listdir(chemin):
                    if exporte.match(nom) and os.path.isfile(os.path.join(chemin, nom)):
                        os.remove(os.path.join(chemin, nom))
        ancres, palettes, vides = [], 0, []
        with ProcessPoolExecutor(max_workers=max(1, options.taches)) as pool:
            for cle, (resultat, raison) in zip(cles, pool.map(finaliser, taches, chunksize=16)):
                if raison:
                    vides.append("%s %d (image %d) : %s" % (cle[0], cle[1], cle[2], raison))
                if resultat is None:
                    continue
                xmin, ymin, largeur, hauteur, en_palette = resultat
                palettes += en_palette
                ancres.append((cle[0], cle[1], xmin, ymin, largeur, hauteur, cle[2]))
        ordre = {"sol": 0, "fond": 1, "objet": 2, "selection": 3, "interaction": 4}
        ancres.sort(key=lambda ligne: (ordre[ligne[0]], ligne[1], ligne[6]))
        os.makedirs(options.sortie, exist_ok=True)
        with open(os.path.join(options.sortie, "ancres.tsv"), "w", encoding="utf-8", newline="\n") as fichier:
            fichier.write("# type\tid\txmin\tymin\tlargeur\thauteur\timage\n")
            for ligne in ancres:
                fichier.write("\t".join(str(valeur) for valeur in ligne) + "\n")
        taille = sum(os.path.getsize(os.path.join(racine, nom)) for racine, _, noms in os.walk(options.sortie) for nom in noms)

        def compter(type_, pentes=False):
            return sum(1 for ligne in ancres if ligne[0] == type_ and (ligne[6] > 1) == pentes)
        print("décor : %d sols, %d images de pente, %d fonds, %d objets, %d formes de cellule ; %d PNG en palette ; %.1f Mo"
              % (compter("sol"), compter("sol", True), compter("fond"), compter("objet"),
                 compter("selection") + compter("interaction"), palettes, taille / 1048576))
        print("durées : SVG %.1f s, PNG %.1f s, découpe et palette %.1f s" % (svg_fin - debut, png_fin - svg_fin, time.time() - png_fin))
        for titre, lignes in (("identifiants présents dans deux bibliothèques (première occurrence gardée)", doublons),
                              ("fonds de plus de %d px" % options.seuil_fond, fonds),
                              ("SVG repris avec des dégradés minuscules agrandis", reprises),
                              ("symboles vides ou illisibles", vides), ("avertissements de swfsvg", avertissements),
                              ("journal", journal)):
            if lignes:
                print("%s : %d" % (titre, len(lignes)))
                for ligne in lignes:
                    print("  " + ligne)
    finally:
        if not options.travail:
            shutil.rmtree(travail, ignore_errors=True)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Exporte les effets de sorts du client Dofus 1.34 (clips/spells, clips/extra) en bandes PNG, avec effets.tsv.

usage : exporter_sorts.py <dossier des SWF> <sortie> [--liste sorts_utilises.txt] [--gfx 5,...] [--pas N]
                          [--echelle 1] [--jobs N] [--sans-palette] [--swfsvg CHEMIN]
        exporter_sorts.py --starloco <game.sql> [--java ObjectAction.java] --liste sorts_utilises.txt

Pour chaque <gfx>.swf choisi (cairosvg et Pillow requis, swfsvg 0.2.3 ou plus) :

- <gfx>_scene.png : la timeline principale, ce que le client affiche quand il charge le clip de l'effet
  (`swfsvg --scene --frame all`), une image sur « pas », assemblée en bande comme les sprites
  (exporter_sprites.bande : même cadre pour toutes les images, marges transparentes rognées, une ligne
  jusqu'à 32 767 px puis une grille, palette 8 bits quand l'écart reste invisible) ;
- <gfx>_shoot.png, <gfx>_move.png, <gfx>_duplicate.png : les symboles exportés que le client attache
  lui-même aux projectiles (types 20 à 41 de GA300), toutes leurs images utiles au même pas ;
- <gfx>_rotate.png (lot AN5) : quand la timeline principale nomme une instance « rotate » et que le gfx
  n'est affiché qu'en types 20 et 21 (types lus dans --liste), l'enfant que le client tourne alors vers la
  cible (`--scene --instance rotate`), et la scène est rendue sans lui (`--sans-instance rotate`). Le
  cadre de cette bande est centré sur le point d'origine de l'instance dans la scène (sa translation, lue
  dans le PlaceObject du SWF) : le bot la fait tourner autour du centre de son cadre, à 0,5 px près.
  Sinon (autre type, ou --gfx sans --liste) l'instance reste dans la scène, sans tourner, avec un message.
Les scripts des SWF (hasard, niveau du sort, attachMovie) ne sont pas exécutés : on rend la timeline telle
quelle.

<sortie>/effets.tsv a le format d'ancres.tsv (UTF-8, tabulations, une ligne d'en-tête) :
    gfx  anim  xmin  ymin  largeur  hauteur  images  ips  fin
xmin, ymin : coin haut-gauche de l'image par rapport à l'origine du clip (le point où le client le pose :
lanceur, cellule ou acteur) ; ips : 40 / pas ; fin : colonne fin de `swfsvg --list` (static : le clip se
retire lui-même, arret : stop() sur la dernière image, boucle : rien ne l'arrête avant le retrait à 20 s).

<sortie>/exclusions.tsv (gfx, raison, détail) liste les gfx sans <gfx>_scene.png et pourquoi :
    symboles  scène vide, le dessin est dans shoot, move, duplicate ou rotate (projectiles)
    script    scène vide, dessin fait par script (attachMovie, duplicateMovieClip ou onEnterFrame)
    vide      scène vide, sans script reconnu
    cairo     une image n'a pas pu être rendue (cairo, par exemple NO_MEMORY) : gfx sauté, lot poursuivi
    taille    bande de plus de 16 Mpx ou image de plus de 32 767 px de côté
    absent    SWF absent du dossier
    erreur    swfsvg a échoué sur ce SWF
Avec --gfx, seules les lignes, exclusions et PNG de ces gfx sont remplacés ; sinon le dossier est réécrit.
--gfx et --liste ensemble : les gfx de --gfx, qui doivent figurer dans la liste, avec leurs types.

--starloco écrit la liste des gfx que le serveur StarLoco fait afficher, avec leurs types (champ type de
GA300, GA208 et GA228) et leur source : la table `sorts` (colonne sprite, types de spriteInfos à partir de
10), les fées d'artifice (`objectsactions` de type 5 → table `animations` : id = gfx, area = type) et, avec
--java, les ballons de GA208 lus dans ObjectAction.java. Une ligne : « <gfx> <types> <sources> ».
"""
import argparse, collections, concurrent.futures, os, re, struct, sys, tempfile, zlib

from PIL import Image

import exporter_sprites as sprites

SYMBOLES = ("shoot", "move", "duplicate")
SCRIPTS = (b"attachMovie", b"duplicateMovieClip", b"onEnterFrame")
EN_TETE_EXCLUSIONS = ("gfx", "raison", "detail")
PRODUIT = re.compile(r"^(\d+)_([A-Za-z][A-Za-z0-9]*)\.png$")
TYPE_MIN = 10  # visualEffectHandler.addEffect ignore les types inférieurs
INSTANCE_TOURNEE = "rotate"  # enfant de la scène tourné vers la cible (onLoadInit, types 20 et 21)
TYPES_TOURNES = {20, 21}  # seuls types où onLoadInit tourne l'instance « rotate » vers la cible


# ------------------------------------------------------------------------------- lecture minimale du SWF

class _Bits:
    """Lecture bit à bit (big-endian dans l'octet) des champs MATRIX et CXFORM d'un SWF."""

    def __init__(self, donnees, position):
        self.d, self.octet, self.bit = donnees, position, 0

    def ub(self, n):
        v = 0
        for _ in range(n):
            if self.octet >= len(self.d):
                raise ValueError("champ binaire tronqué")
            v = (v << 1) | ((self.d[self.octet] >> (7 - self.bit)) & 1)
            self.bit += 1
            if self.bit == 8:
                self.bit, self.octet = 0, self.octet + 1
        return v

    def sb(self, n):
        v = self.ub(n)
        return v - (1 << n) if n and v & (1 << (n - 1)) else v

    def aligner(self):
        if self.bit:
            self.bit, self.octet = 0, self.octet + 1
        return self.octet


def _matrice(b):
    """MATRIX du SWF -> (a, b, c, d, tx, ty), translation en pixels (twips / 20)."""
    a = d = 1.0
    rb = rc = 0.0
    if b.ub(1):
        n = b.ub(5); a = b.sb(n) / 65536.0; d = b.sb(n) / 65536.0
    if b.ub(1):
        n = b.ub(5); rb = b.sb(n) / 65536.0; rc = b.sb(n) / 65536.0
    n = b.ub(5)
    tx, ty = b.sb(n) / 20.0, b.sb(n) / 20.0
    b.aligner()
    return (a, rb, rc, d, tx, ty)


def _cxform(b):
    ajout, produit = b.ub(1), b.ub(1)
    n = b.ub(4)
    for _ in range(4 * (ajout + produit)):
        b.sb(n)
    b.aligner()


def placements_principaux(swf):
    """PlaceObject2 et PlaceObject3 de la timeline principale (hors DefineSprite) : liste de
    (image, profondeur, caractère ou None, nom ou None, matrice ou None). [] si le fichier n'est pas un SWF
    lisible (le rendu de swfsvg dira alors pourquoi)."""
    try:
        with open(swf, "rb") as f:
            donnees = f.read()
        if donnees[:3] == b"CWS":
            corps = zlib.decompress(donnees[8:])
        elif donnees[:3] == b"FWS":
            corps = donnees[8:]
        else:
            return []
        b = _Bits(corps, 0)
        n = b.ub(5)
        for _ in range(4):
            b.ub(n)
        position = b.aligner() + 4  # cadre, puis cadence et nombre d'images
        image, liste = 0, []
        while position + 2 <= len(corps):
            code_long = struct.unpack_from("<H", corps, position)[0]
            position += 2
            code, longueur = code_long >> 6, code_long & 0x3F
            if longueur == 0x3F:
                longueur = struct.unpack_from("<I", corps, position)[0]
                position += 4
            balise = corps[position:position + longueur]
            position += longueur
            if code == 0:
                break
            if code == 1:
                image += 1
            elif code in (26, 70) and len(balise) >= 3:
                drapeaux, p = balise[0], 1
                drapeaux2 = 0
                if code == 70:
                    drapeaux2, p = balise[1], 2
                profondeur = struct.unpack_from("<H", balise, p)[0]
                p += 2
                if code == 70 and (drapeaux2 & 0x08 or (drapeaux2 & 0x10 and drapeaux & 0x02)):
                    p = balise.index(b"\0", p) + 1  # nom de classe
                caractere = matrice = nom = None
                if drapeaux & 0x02:
                    caractere = struct.unpack_from("<H", balise, p)[0]
                    p += 2
                if drapeaux & 0x04:
                    lecteur = _Bits(balise, p); matrice = _matrice(lecteur); p = lecteur.octet
                if drapeaux & 0x08:
                    lecteur = _Bits(balise, p); _cxform(lecteur); p = lecteur.octet
                if drapeaux & 0x10:
                    p += 2  # ratio
                if drapeaux & 0x20:
                    fin = balise.index(b"\0", p)
                    nom = balise[p:fin].decode("latin-1")
                liste.append((image, profondeur, caractere, nom, matrice))
        return liste
    except (OSError, ValueError, IndexError, struct.error, zlib.error):
        return []


def placement_nomme(swf, nom):
    """Matrice (a, b, c, d, tx, ty) du premier PlaceObject de la timeline principale qui nomme l'instance
    « nom » (sans tenir compte de la casse, comme swfsvg --instance) ; None si elle n'existe pas."""
    for _, _, _, n, m in placements_principaux(swf):
        if n is not None and n.lower() == nom.lower():
            return m or (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)
    return None


def lister(swfsvg, swf):
    """(images, fin) de la scène et {nom en minuscules: (nom réel, images, fin)} des symboles exportés."""
    lignes = sprites.lancer(swfsvg + ["--list", swf]).splitlines()
    if not lignes or "fin" not in lignes[0].split("\t"):
        raise RuntimeError("swfsvg --list sans colonne fin : swfsvg 0.2.3 ou plus requis")
    scene, symboles = (0, "arret"), {}
    for ligne in lignes[1:]:
        col = ligne.split("\t")
        if len(col) < 6 or not col[3].isdigit():
            continue
        if col[2] == "scene":
            scene = (int(col[3]), col[5])
        else:
            symboles.setdefault(col[0].lower(), (col[0], int(col[3]), col[5]))
    return scene, symboles


def a_des_scripts(swf):
    """Vrai si le SWF appelle attachMovie, duplicateMovieClip ou onEnterFrame (recherche dans son corps)."""
    with open(swf, "rb") as f:
        donnees = f.read()
    if donnees[:3] == b"CWS":
        try:
            donnees = zlib.decompress(donnees[8:])
        except zlib.error:
            return False
    return any(nom in donnees for nom in SCRIPTS)


def images_rendues(index, nom, pas):
    """Fichiers SVG d'une série `--frame all`, dans l'ordre de la colonne image, une image sur « pas »."""
    fichiers = sorted((f for f in index if index[f][0] == nom), key=lambda f: int(index[f][9]))
    return fichiers[::pas]


def exporter_bande(svgs, gfx, anim, sortie, echelle, palette, pas, fin, messages):
    """Ligne d'effets.tsv et taille de la bande ; (None, 0, raison, détail) si rien n'est écrit."""
    try:
        ligne, taille = sprites.bande(svgs, gfx, anim, sortie, echelle, palette)
    except RuntimeError as erreur:
        texte = str(erreur)
        raison = "taille" if "px" in texte else "erreur"
        return None, 0, raison, texte
    except Exception as erreur:  # cairo (NO_MEMORY…), SVG illisible : on saute ce gfx sans arrêter le lot
        return None, 0, "cairo", "%s: %s" % (type(erreur).__name__, str(erreur).strip()[:200])
    if not ligne:
        return None, 0, "vide", ""
    ligne += (sprites.IPS // pas, "arret" if ligne[6] == 1 else sprites.fin_valide(fin, gfx, anim, messages))
    return ligne, taille, None, None


def bande_centree(svgs, gfx, anim, sortie, echelle, palette, pivot):
    """Comme exporter_sprites.bande, mais le cadre commun est symétrique autour de « pivot » (point du repère
    du clip, en pixels du SWF) : le centre du cadre tombe sur ce point à 0,5 px près, quelle que soit
    l'image. Renvoie les sept premières colonnes de la ligne d'effets.tsv et la taille du PNG."""
    images = [sprites.svg_vers_image(s, echelle) for s in svgs]
    if not images:
        return None, 0
    if len({(im.size, x0, y0) for im, x0, y0 in images}) != 1:
        raise RuntimeError("%s : images de %s dans des cadres différents" % (gfx, anim))
    cadres = [c for c in (im.getchannel("A").getbbox() for im, _, _ in images) if c]
    if not cadres:
        return None, 0
    _, x0, y0 = images[0]
    cx, cy = int(round(pivot[0] * echelle)) - x0, int(round(pivot[1] * echelle)) - y0
    demi_l = max(1, max(max(cx - c[0], c[2] - cx) for c in cadres))
    demi_h = max(1, max(max(cy - c[1], c[3] - cy) for c in cadres))
    cadre = (cx - demi_l, cy - demi_h, cx + demi_l, cy + demi_h)  # crop de Pillow : hors de l'image, transparent
    largeur, hauteur = 2 * demi_l, 2 * demi_h
    colonnes, lignes = sprites.disposition(len(images), largeur, hauteur)
    feuille = Image.new("RGBA", (largeur * colonnes, hauteur * lignes), (0, 0, 0, 0))
    for k, (im, _, _) in enumerate(images):
        feuille.paste(im.crop(cadre), (k % colonnes * largeur, k // colonnes * hauteur))
    taille = sprites.enregistrer(feuille, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    return (gfx, anim, x0 + cadre[0], y0 + cadre[1], largeur, hauteur, len(images)), taille


def exporter_bande_centree(svgs, gfx, anim, sortie, echelle, palette, pas, fin, messages, pivot):
    """exporter_bande avec bande_centree."""
    try:
        ligne, taille = bande_centree(svgs, gfx, anim, sortie, echelle, palette, pivot)
    except RuntimeError as erreur:
        return None, 0, "taille" if "px" in str(erreur) else "erreur", str(erreur)
    except Exception as erreur:  # cairo, SVG illisible
        return None, 0, "cairo", "%s: %s" % (type(erreur).__name__, str(erreur).strip()[:200])
    if not ligne:
        return None, 0, "vide", ""
    ligne += (sprites.IPS // pas, "arret" if ligne[6] == 1 else sprites.fin_valide(fin, gfx, anim, messages))
    return ligne, taille, None, None


def instance_tournee(swf, types, gfx, messages):
    """Matrice de l'instance « rotate » de la scène si le bot doit la tourner (types 20 et 21) ; None sinon.
    types : types d'affichage du gfx (sorts_utilises.txt), None s'ils sont inconnus (--gfx sans --liste)."""
    matrice = placement_nomme(swf, INSTANCE_TOURNEE)
    if matrice is None:
        return None
    if not types or not set(types) <= TYPES_TOURNES:
        # Types inconnus (--gfx sans --liste) ou gfx affiché aussi par un autre type, qui montre la scène
        # telle quelle : l'instance reste dans la scène, sans tourner.
        messages.append("%s : instance rotate gardée dans la scène (types %s)"
                        % (gfx, ",".join(str(t) for t in sorted(types)) if types else "inconnus"))
        return None
    a, b, c, d, _, _ = matrice
    if abs(b) > 0.01 or abs(c) > 0.01 or a <= 0 or d <= 0:
        messages.append("%s : instance rotate déjà tournée ou retournée (%.3f %.3f %.3f %.3f) : le bot la tourne "
                        "autour de sa translation sans en tenir compte" % (gfx, a, b, c, d))
    return matrice


def exporter_gfx(gfx, swf, sortie, pas, echelle, palette, swfsvg, temporaire, types=None):
    """Exporte un SWF ; renvoie (lignes d'effets.tsv, exclusion ou None, messages, octets). Ne lève jamais.
    types : types d'affichage du gfx (None : inconnus), pour la bande rotate."""
    lignes, messages, octets = [], [], 0
    if not os.path.isfile(swf):
        return lignes, (gfx, "absent", os.path.basename(swf)), messages, octets
    try:
        scene, symboles = lister(swfsvg, swf)
        tournee = instance_tournee(swf, types, gfx, messages)
        with tempfile.TemporaryDirectory(dir=temporaire) as dossier:
            sous = os.path.join(dossier, "scene")
            filtre = ["--sans-instance", INSTANCE_TOURNEE] if tournee else []
            index = sprites.rendre_svg(swfsvg, ["--scene", "--name", "scene"] + filtre + ["--frame", "all", swf, sous], sous)
            fichiers = [os.path.join(sous, f) for f in images_rendues(index, "scene", pas)]
            ligne, taille, raison, detail = exporter_bande(fichiers, gfx, "scene", sortie, echelle, palette, pas, scene[1], messages)
            if raison in ("cairo", "taille", "erreur"):
                # Un échec de rendu saute tout le gfx (plan : noter et poursuivre le lot).
                return [], (gfx, raison, detail), messages + ["%s : scène non rendue (%s)" % (gfx, detail)], 0
            if ligne:
                lignes.append(ligne); octets += taille
            if tournee:
                # L'enfant tourné, rendu à sa place et dans le temps de la scène ; sa dernière image retire
                # le clip (removeMovieClip de _parent) : même fin que la scène.
                sous = os.path.join(dossier, INSTANCE_TOURNEE)
                index = sprites.rendre_svg(swfsvg, ["--scene", "--name", INSTANCE_TOURNEE, "--instance", INSTANCE_TOURNEE,
                                                    "--frame", "all", swf, sous], sous)
                fichiers = [os.path.join(sous, f) for f in images_rendues(index, INSTANCE_TOURNEE, pas)]
                l, t, r, d = exporter_bande_centree(fichiers, gfx, INSTANCE_TOURNEE, sortie, echelle, palette, pas,
                                                    scene[1], messages, tournee[4:6])
                if r in ("cairo", "taille", "erreur"):
                    return [], (gfx, r, d), messages + ["%s : instance rotate non rendue (%s)" % (gfx, d)], 0
                if l:
                    lignes.append(l); octets += t
            for nom in SYMBOLES:
                s = symboles.get(nom)
                if not s:
                    continue
                sous = os.path.join(dossier, nom)
                index = sprites.rendre_svg(swfsvg, ["--frame", "all", swf, sous, s[0]], sous)
                fichiers = [os.path.join(sous, f) for f in images_rendues(index, s[0], pas)]
                l, t, r, d = exporter_bande(fichiers, gfx, nom, sortie, echelle, palette, pas, s[2], messages)
                if l:
                    lignes.append(l); octets += t
                else:
                    messages.append("%s : symbole %s non exporté (%s %s)" % (gfx, s[0], r, d or ""))
            if ligne:
                return lignes, None, messages, octets
            if any(l[1] != "scene" for l in lignes):
                raison, detail = "symboles", ",".join(l[1] for l in lignes)
            elif a_des_scripts(swf):
                raison, detail = "script", "attachMovie, duplicateMovieClip ou onEnterFrame"
            else:
                raison, detail = "vide", "%d image(s) sans dessin" % scene[0]
            return lignes, (gfx, raison, detail), messages, octets
    except Exception as erreur:  # un SWF illisible ne doit pas arrêter la série
        return [], (gfx, "erreur", str(erreur).strip().replace("\t", " ").replace("\n", " ")[:200]), messages, 0


def lire_liste(chemin):
    """sorts_utilises.txt -> [(gfx, types, sources)] ; # commence un commentaire."""
    liste = []
    with open(chemin, encoding="utf-8") as f:
        for numero, ligne in enumerate(f, 1):
            ligne = ligne.split("#", 1)[0].strip()
            if not ligne:
                continue
            col = ligne.split()
            if not col[0].isdigit() or len(col) > 3:
                raise ValueError("%s ligne %d : « <gfx> <types> <sources> » attendu, « %s » lu" % (chemin, numero, ligne))
            types = [int(t) for t in col[1].split(",")] if len(col) > 1 else []
            liste.append((col[0], types, col[2].split(",") if len(col) > 2 else []))
    return liste


def lire_tsv(chemin, colonnes):
    """Lignes (listes de chaînes) d'un TSV à en-tête, de « colonnes » colonnes ; [] s'il est absent."""
    if not os.path.isfile(chemin):
        return []
    with open(chemin, encoding="utf-8") as f:
        lignes = [l.rstrip("\r\n").split("\t") for l in f]
    return [l for l in lignes[1:] if len(l) == colonnes]


def ecrire_tsv(chemin, en_tete, lignes):
    with open(chemin, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(en_tete) + "\n")
        for l in lignes:
            f.write("\t".join(str(v) for v in l) + "\n")


# ------------------------------------------------------------------------------------------- --starloco

def valeurs_sql(sql, table):
    """Tuples de chaînes des INSERT INTO `table` VALUES (...) d'un dump MySQL (une ligne par INSERT)."""
    motif = re.compile(r"INSERT INTO `%s` VALUES \((.*)\);\s*$" % re.escape(table))
    champ = re.compile(r"'((?:[^'\\]|\\.|'')*)'|(NULL)|(-?[\d.]+)")
    for ligne in sql.splitlines():
        m = motif.match(ligne)
        if m:
            yield tuple(a if a or not (b or c) else (c or "") for a, b, c in champ.findall(m.group(1)))


def liste_starloco(sql, java):
    """{gfx: (types, sources)} des effets que StarLoco fait afficher (voir l'aide du module)."""
    gfx = collections.defaultdict(lambda: (set(), set()))
    for v in valeurs_sql(sql, "sorts"):
        # id, nom, sprite, spriteInfos (type,anim,devant), ...
        if len(v) < 4 or not v[2].lstrip("-").isdigit() or int(v[2]) <= 0:
            continue
        type_ = v[3].split(",")[0]
        if type_.lstrip("-").isdigit() and int(type_) >= TYPE_MIN:
            gfx[int(v[2])][0].add(int(type_)); gfx[int(v[2])][1].add("sorts")
    animations = {v[0]: v for v in valeurs_sql(sql, "animations") if len(v) >= 6}  # guid, id, nom, area, action, size
    for v in valeurs_sql(sql, "objectsactions"):
        # template, type (« 5 » ou « 5;… »), args alignés sur les types (séparés par |) ; type 5 : fée d'artifice
        if len(v) < 3:
            continue
        types, args = v[1].split(";"), v[2].split("|", len(v[1].split(";")) - 1)
        for k, t in enumerate(types):
            a = animations.get(args[k] if k < len(args) else "")
            if t == "5" and a and a[1].isdigit() and a[3].lstrip("-").isdigit() and int(a[3]) >= TYPE_MIN:
                gfx[int(a[1])][0].add(int(a[3])); gfx[int(a[1])][1].add("228")
    if java:
        # ObjectAction.java : "GA;208;" + id + ";" + cellule + ",<gfx>,<type>,<anim>,<niveau>"
        for g, t in re.findall(r'"GA;208;"[^;]*?;"[^"]*?",(\d+),(\d+),\d+,\d+"', java, re.S):
            if int(t) >= TYPE_MIN:
                gfx[int(g)][0].add(int(t)); gfx[int(g)][1].add("208")
    return gfx


def ecrire_liste(chemin, gfx, sources):
    ordre = {"sorts": 0, "228": 1, "208": 2}
    with open(chemin, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Effets que le serveur StarLoco fait afficher : gfx de clips/spells/<gfx>.swf, types d'affichage\n"
                "# (champ type de GA300, GA208 et GA228 ; addEffect ignore les types < 10) et source (sorts : table\n"
                "# sorts ; 228 : fées d'artifice, tables objectsactions et animations ; 208 : ballons, ObjectAction.java).\n"
                "# Généré par tools/client-analysis/exporter_sorts.py --starloco depuis %s ; ne pas éditer.\n"
                "# <gfx> <types> <sources>\n" % sources)
        for g in sorted(gfx):
            types, origines = gfx[g]
            f.write("%d %s %s\n" % (g, ",".join(str(t) for t in sorted(types)), ",".join(sorted(origines, key=lambda o: ordre.get(o, 9)))))


# ------------------------------------------------------------------------------------------- main

def main():
    p = argparse.ArgumentParser(description="Effets de sorts du client Dofus 1.34 -> bandes PNG + effets.tsv")
    p.add_argument("swf", nargs="?", help="dossier des SWF (clips/spells ou clips/extra)")
    p.add_argument("sortie", nargs="?", help="dossier de sortie (Outil_Azur_complet/Resources/Bot/Effets/sorts)")
    p.add_argument("--liste", help="sorts_utilises.txt : gfx à exporter (lu), ou à écrire avec --starloco")
    p.add_argument("--gfx", help="gfx à exporter, séparés par des virgules ; remplace leurs seules lignes "
                                 "(avec --liste : gfx de la liste, dont les types sont lus)")
    p.add_argument("--pas", type=int, default=1, help="une image sur N (diviseur de 40, défaut 1)")
    p.add_argument("--echelle", type=float, default=1.0, help="échelle des PNG (défaut 1)")
    p.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="processus en parallèle")
    p.add_argument("--sans-palette", action="store_true", help="garder tous les PNG en RGBA 32 bits")
    p.add_argument("--swfsvg", help="binaire swfsvg 0.2.3+ (sinon $SWFSVG, le PATH ou swfsvg/target/release)")
    p.add_argument("--starloco", metavar="GAME_SQL", help="écrire --liste depuis le dump game.sql de StarLoco")
    p.add_argument("--java", help="avec --starloco : ObjectAction.java de StarLoco (ballons de GA208)")
    a = p.parse_args()

    if a.starloco:
        if not a.liste or a.swf or a.sortie:
            sys.exit("--starloco <game.sql> [--java ObjectAction.java] --liste <sorts_utilises.txt>, sans autre argument")
        with open(a.starloco, encoding="utf-8", errors="replace") as f:
            sql = f.read()
        java = None
        if a.java:
            with open(a.java, encoding="utf-8", errors="replace") as f:
                java = f.read()
        gfx = liste_starloco(sql, java)
        sources = "game.sql" + (" et ObjectAction.java" if java else "")
        ecrire_liste(a.liste, gfx, sources)
        compte = collections.Counter(o for _, origines in gfx.values() for o in origines)
        print("%d gfx écrits dans %s (%s)" % (len(gfx), a.liste, ", ".join("%s : %d" % kv for kv in sorted(compte.items()))))
        return

    if not a.swf or not a.sortie:
        p.error("dossier des SWF et dossier de sortie attendus")
    if a.pas < 1 or sprites.IPS % a.pas:
        sys.exit("--pas doit diviser %d" % sprites.IPS)
    swfsvg = sprites.trouver_swfsvg(a.swfsvg)
    os.makedirs(a.sortie, exist_ok=True)
    tous = sorted((f[:-4] for f in os.listdir(a.swf) if f.lower().endswith(".swf") and f[:-4].isdigit()), key=int)
    types = {}  # gfx -> types d'affichage, connus seulement avec --liste
    try:
        if a.liste:
            for g, t, _ in lire_liste(a.liste):
                types.setdefault(g, set()).update(t)
        choisis = [g.strip() for g in a.gfx.split(",") if g.strip()] if a.gfx else list(types) if a.liste else tous
    except (OSError, ValueError) as erreur:
        sys.exit(str(erreur))
    if a.liste and a.gfx and any(g not in types for g in choisis):
        sys.exit("--gfx avec --liste : gfx absents de %s : %s" % (a.liste, ",".join(g for g in choisis if g not in types)))
    if not choisis or any(not g.isdigit() for g in choisis):
        sys.exit("gfx invalides ou aucun SWF : " + ",".join(choisis))
    choisis = sorted(set(choisis), key=int)

    lignes, exclusions, messages, octets = [], [], [], 0
    with tempfile.TemporaryDirectory(prefix="sorts-") as temporaire:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            taches = [pool.submit(exporter_gfx, g, os.path.join(a.swf, g + ".swf"), a.sortie, a.pas, a.echelle,
                                  not a.sans_palette, swfsvg, temporaire, types.get(g) if a.liste else None)
                      for g in choisis]
            for t in taches:
                l, e, m, o = t.result()
                lignes += l; messages += m; octets += o
                if e: exclusions.append(e)

    partiel = bool(a.gfx)
    choix = set(choisis)
    produits = {"%s_%s.png" % (l[0], l[1]) for l in lignes}
    for f in os.listdir(a.sortie):
        m = PRODUIT.match(f)
        if m and f not in produits and (not partiel or m.group(1) in choix):
            os.remove(os.path.join(a.sortie, f))
    effets = os.path.join(a.sortie, "effets.tsv")
    refus = os.path.join(a.sortie, "exclusions.tsv")
    if partiel:
        lignes = [tuple(l) for l in lire_tsv(effets, len(sprites.EN_TETE)) if l[0] not in choix] + lignes
        exclusions = [tuple(l) for l in lire_tsv(refus, len(EN_TETE_EXCLUSIONS)) if l[0] not in choix] + exclusions
    lignes.sort(key=lambda l: (int(l[0]), l[1]))
    exclusions.sort(key=lambda l: int(l[0]))
    ecrire_tsv(effets, sprites.EN_TETE, lignes)
    ecrire_tsv(refus, EN_TETE_EXCLUSIONS, exclusions)
    for m in messages:
        print(m)
    raisons = collections.Counter(e[1] for e in exclusions if e[0] in choix)
    print("%d SWF, %d PNG (%.1f Mo), %d scènes, %d exclusions (%s) dans %s" % (
        len(choisis), len(produits), octets / 1e6, sum(1 for l in lignes if l[1] == "scene" and l[0] in choix),
        sum(raisons.values()), ", ".join("%s : %d" % kv for kv in sorted(raisons.items())) or "aucune", a.sortie))


if __name__ == "__main__":
    main()

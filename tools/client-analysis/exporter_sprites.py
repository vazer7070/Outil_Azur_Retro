#!/usr/bin/env python3
"""Exporte les sprites d'acteurs du client Dofus 1.34 en PNG, avec leurs ancres.

usage : exporter_sprites.py <client>/clips/sprites <sortie> [--swfsvg CHEMIN] [--animes FICHIER]
                            [--gfx 10,11,...] [--echelle 1] [--jobs N] [--sans-palette]

Pour chaque <gfx>.swf du dossier (cairosvg et Pillow requis, swfsvg 0.2.1 ou plus) :

- <gfx>_static<O>.png, O dans S, R, L, F, B : le symbole exporté static<O> (la casse du nom
  d'export est ignorée : quelques SWF exportent StaticR). On rend sa dernière image utile
  (`swfsvg --list`) : c'est l'image sur laquelle le client reste une fois l'animation de repos
  jouée (l'épouvantail 1219 sort du sol ; sur une respiration en boucle, c'est une image du cycle).
  Les directions 3, 4 et 7 n'ont pas de fichier : le client retourne R, S et L (`_xscale` -100).
- <gfx>_scene.png quand le SWF n'exporte aucun static<O> (épées de combat 0-5, tombes 13...123,
  quelques monstres) : l'image 1 de la scène, ce que montre le clip chargé sans animation attachée.
- pour les gfx listés dans le fichier --animes (par défaut <sortie>/sprites_animes.txt) :
  <gfx>_walk<O>.png et <gfx>_run<O>.png, bandes horizontales de toutes les images utiles du cycle,
  rendues dans un même cadre (`swfsvg --frame all`), l'image k occupant les colonnes
  [k * largeur, (k + 1) * largeur).

Écrit <sortie>/ancres.tsv (UTF-8, tabulations, une ligne d'en-tête) :
    gfx  anim  xmin  ymin  largeur  hauteur  images
xmin, ymin : position, en pixels du PNG, de son coin haut-gauche par rapport au point d'ancrage du
client (pied du personnage) ; le pixel (-xmin, -ymin) de chaque image est donc le point d'ancrage.
largeur, hauteur : taille d'une image ; images : nombre d'images de la bande (1 pour static et scene).
Pour un sprite retourné (directions 3, 4, 7), l'image retournée se pose à x = ancre - (xmin + largeur).

Les zones entièrement transparentes sont rognées ; les PNG sont enregistrés en palette 8 bits avec
transparence quand l'écart avec l'original reste invisible (--sans-palette pour l'éviter).
Avec --gfx, seules les lignes de ces gfx sont remplacées dans un ancres.tsv existant.
"""
import argparse, concurrent.futures, io, os, re, shutil, subprocess, sys, tempfile

import cairosvg
from PIL import Image, ImageChops, ImageStat

ORIENTATIONS = "SRLFB"
CYCLES = ("walk", "run")
EN_TETE = ("gfx", "anim", "xmin", "ymin", "largeur", "hauteur", "images")
ECART_PALETTE = 1.5  # écart moyen maximal (0-255, par canal) accepté pour la palette 8 bits


def trouver_swfsvg(chemin):
    """Commande swfsvg (liste d'arguments) ; un script .py est lancé par cet interpréteur (tests)."""
    ici = os.path.dirname(os.path.abspath(__file__))
    candidats = [chemin, os.environ.get("SWFSVG"), shutil.which("swfsvg"),
                 os.path.join(ici, "swfsvg", "target", "release", "swfsvg"),
                 os.path.join(ici, "swfsvg", "target", "release", "swfsvg.exe")]
    for c in candidats:
        if c and os.path.isfile(c):
            if c.endswith(".py"):
                return [sys.executable, c]
            if os.access(c, os.X_OK):
                return [c]
    sys.exit("swfsvg introuvable : compilez tools/client-analysis/swfsvg (cargo build --release) ou passez --swfsvg")


def lancer(commande):
    sortie = subprocess.run(commande, capture_output=True, encoding="utf-8", errors="replace")
    if sortie.returncode != 0:
        raise RuntimeError(sortie.stderr.strip() or "swfsvg a échoué : " + " ".join(commande[1:]))
    return sortie.stdout


def lire_liste_animes(chemin):
    """Un gfx par ligne ; tout ce qui suit # est un commentaire."""
    gfx = set()
    if chemin and os.path.isfile(chemin):
        with open(chemin, encoding="utf-8") as f:
            for ligne in f:
                ligne = ligne.split("#", 1)[0].strip()
                if ligne:
                    gfx.add(ligne)
    return gfx


def lister(swfsvg, swf):
    """Symboles exportés du SWF : nom en minuscules -> (nom réel, images utiles)."""
    symboles = {}
    for ligne in lancer(swfsvg + ["--list", swf]).splitlines()[1:]:
        col = ligne.split("\t")
        if len(col) >= 4 and col[2] != "scene" and col[3].isdigit():
            symboles.setdefault(col[0].lower(), (col[0], int(col[3])))
    return symboles


def rendre_svg(swfsvg, args, dossier):
    """Lance swfsvg et renvoie les lignes de son index.tsv par nom de fichier SVG (colonne 12)."""
    lancer(swfsvg + args)
    lignes = {}
    index = os.path.join(dossier, "index.tsv")
    if os.path.isfile(index):
        with open(index, encoding="utf-8") as f:
            for ligne in f:
                col = ligne.rstrip("\n").split("\t")
                if len(col) >= 12:
                    lignes[col[11]] = col
    return lignes


VIEWBOX = re.compile(r'viewBox="(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+)"')


def svg_vers_image(chemin, echelle):
    """PNG RGBA et position de son pixel (0, 0) dans le repère du symbole, en pixels du PNG."""
    with open(chemin, encoding="utf-8") as f:
        debut = f.read(512)
    m = VIEWBOX.search(debut)
    if not m:
        raise RuntimeError("viewBox absent : " + os.path.basename(chemin))
    x0, y0 = round(float(m.group(1)) * echelle), round(float(m.group(2)) * echelle)
    image = Image.open(io.BytesIO(cairosvg.svg2png(url=chemin, scale=echelle, background_color=None))).convert("RGBA")
    return effacer_magenta(image), x0, y0


def seuil(canal, test):
    return canal.point(lambda v: 255 if test(v) else 0)


def effacer_magenta(image):
    """Comme exporter_png.py : les pixels magenta purs (emplacements remplis à l'exécution) deviennent transparents."""
    r, g, b, a = image.split()
    masque = ImageChops.multiply(ImageChops.multiply(seuil(r, lambda v: v > 235), seuil(g, lambda v: v < 25)),
                                 ImageChops.multiply(seuil(b, lambda v: v > 235), seuil(a, lambda v: v > 0)))
    if masque.getbbox():
        image.putalpha(ImageChops.subtract(a, masque))
    return image


def nettoyer(image):
    """Pixels entièrement transparents mis à (0, 0, 0, 0) : la palette ne gaspille pas d'entrées."""
    vide = Image.new("RGBA", image.size, (0, 0, 0, 0))
    return Image.composite(image, vide, seuil(image.getchannel("A"), lambda v: v > 0))


def enregistrer(image, chemin, palette):
    image = nettoyer(image)
    tampon = io.BytesIO()
    image.save(tampon, "PNG", optimize=True)
    meilleur = tampon.getvalue()
    if palette:
        reduite = image.quantize(colors=256, method=Image.Quantize.FASTOCTREE)
        # Écart mesuré sur les seuls pixels visibles : ombres douces et bords en dégradé d'alpha
        # se dégradent vite en palette, ces PNG restent alors en RGBA.
        visibles = seuil(image.getchannel("A"), lambda v: v > 0)
        ecart = ImageStat.Stat(ImageChops.difference(image, reduite.convert("RGBA")), mask=visibles).mean
        if max(ecart) <= ECART_PALETTE:
            tampon = io.BytesIO()
            reduite.save(tampon, "PNG", optimize=True)
            if len(tampon.getvalue()) < len(meilleur):
                meilleur = tampon.getvalue()
    with open(chemin, "wb") as f:
        f.write(meilleur)
    return len(meilleur)


def exporter_gfx(gfx, swf, sortie, anime, echelle, palette, swfsvg, temporaire):
    """Exporte un SWF ; renvoie (lignes d'ancres.tsv, messages, octets écrits). Ne lève jamais."""
    lignes, messages, octets = [], [], 0
    try:
        symboles = lister(swfsvg, swf)
        with tempfile.TemporaryDirectory(dir=temporaire) as dossier:
            # Statiques : une commande par nombre d'images, puisque --frame vaut pour tous les noms.
            statiques = {o: symboles.get("static" + o.lower()) for o in ORIENTATIONS}
            par_image = {}
            for o, s in statiques.items():
                if s:
                    par_image.setdefault(s[1], []).append((o, s[0]))
            for n, liste in sorted(par_image.items()):
                # Dernière image utile, puis l'image 1 si la dernière est vide (clip qui disparaît).
                for image in ([n, 1] if n > 1 else [1]):
                    sous = os.path.join(dossier, "static%d_%d" % (n, image))
                    index = rendre_svg(swfsvg, ["--frame", str(image), swf, sous] + [nom for _, nom in liste], sous)
                    restants = []
                    for o, nom in liste:
                        ligne, taille = None, 0
                        if nom + ".svg" in index:
                            ligne, taille = poser(os.path.join(sous, nom + ".svg"), gfx, "static" + o, sortie, echelle, palette)
                        if ligne:
                            lignes.append(ligne); octets += taille
                            if image != n: messages.append("%s : %s vide à l'image %d, image 1 retenue" % (gfx, nom, n))
                        else:
                            restants.append((o, nom))
                    liste = restants
                    if not liste: break
                for o, nom in liste:
                    messages.append("%s : %s vide, rien d'exporté" % (gfx, nom))
            if not par_image:
                sous = os.path.join(dossier, "scene")
                rendre_svg(swfsvg, ["--scene", "--name", "scene", swf, sous], sous)
                ligne, taille = poser(os.path.join(sous, "scene.svg"), gfx, "scene", sortie, echelle, palette)
                if ligne: lignes.append(ligne); octets += taille
                else: messages.append("%s : aucun static<O> et scène vide, rien d'exporté" % gfx)
            if anime:
                for cycle in CYCLES:
                    for o in ORIENTATIONS:
                        s = symboles.get(cycle + o.lower())
                        if not s: continue
                        sous = os.path.join(dossier, cycle + o)
                        index = rendre_svg(swfsvg, ["--frame", "all", swf, sous, s[0]], sous)
                        # Ordre des images d'après la colonne « image » de l'index, pas le nom de fichier.
                        fichiers = sorted((f for f in index if f.startswith(s[0] + "_f")), key=lambda f: int(index[f][9]))
                        ligne, taille = bande([os.path.join(sous, f) for f in fichiers], gfx, cycle + o, sortie, echelle, palette)
                        if ligne: lignes.append(ligne); octets += taille
                        else: messages.append("%s : %s%s vide" % (gfx, cycle, o))
    except Exception as erreur:  # un SWF illisible ne doit pas arrêter la série
        messages.append("%s : %s" % (gfx, erreur))
    return lignes, messages, octets


def poser(svg, gfx, anim, sortie, echelle, palette):
    image, x0, y0 = svg_vers_image(svg, echelle)
    cadre = image.getchannel("A").getbbox()
    if not cadre:
        return None, 0
    image = image.crop(cadre)
    taille = enregistrer(image, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    return (gfx, anim, x0 + cadre[0], y0 + cadre[1], image.width, image.height, 1), taille


def bande(svgs, gfx, anim, sortie, echelle, palette):
    images = [svg_vers_image(s, echelle) for s in svgs]
    if not images:
        return None, 0
    if len({(im.size, x0, y0) for im, x0, y0 in images}) != 1:
        raise RuntimeError("%s : images de %s dans des cadres différents" % (gfx, anim))
    cadres = [im.getchannel("A").getbbox() for im, _, _ in images]
    cadres = [c for c in cadres if c]
    if not cadres:
        return None, 0
    union = (min(c[0] for c in cadres), min(c[1] for c in cadres), max(c[2] for c in cadres), max(c[3] for c in cadres))
    largeur, hauteur = union[2] - union[0], union[3] - union[1]
    feuille = Image.new("RGBA", (largeur * len(images), hauteur), (0, 0, 0, 0))
    for k, (im, _, _) in enumerate(images):
        feuille.paste(im.crop(union), (k * largeur, 0))
    _, x0, y0 = images[0]
    taille = enregistrer(feuille, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    return (gfx, anim, x0 + union[0], y0 + union[1], largeur, hauteur, len(images)), taille


PRODUIT = re.compile(r"^(\w+)_(?:(?:static|walk|run)[SRLFB]|scene)\.png$")


def retirer_anciens(sortie, gfx, gardes):
    """Supprime les PNG de ce schéma laissés par un export précédent (des gfx cités, ou de tous si
    gfx vaut None) ; jamais les autres fichiers du dossier, comme les anciens <gfx><O>.png."""
    for f in os.listdir(sortie):
        m = PRODUIT.match(f)
        if m and (gfx is None or m.group(1) in gfx) and f not in gardes:
            os.remove(os.path.join(sortie, f))


def cle_gfx(valeur):
    return (0, int(valeur)) if valeur.isdigit() else (1, valeur)


def lire_ancres(chemin):
    lignes = []
    if os.path.isfile(chemin):
        with open(chemin, encoding="utf-8") as f:
            for ligne in f:
                col = ligne.rstrip("\n").split("\t")
                if len(col) == len(EN_TETE) and col[0] != EN_TETE[0]:
                    lignes.append((col[0], col[1]) + tuple(int(v) for v in col[2:]))
    return lignes


def main():
    p = argparse.ArgumentParser(description="Sprites d'acteurs du client Dofus 1.34 -> PNG + ancres.tsv")
    p.add_argument("sprites", help="dossier clips/sprites du client")
    p.add_argument("sortie", help="dossier de sortie (Outil_Azur_complet/Resources/Bot/sprites)")
    p.add_argument("--swfsvg", help="binaire swfsvg 0.2.1+ (sinon $SWFSVG, le PATH ou swfsvg/target/release)")
    p.add_argument("--animes", help="liste des gfx dont on exporte walk/run (défaut : <sortie>/sprites_animes.txt)")
    p.add_argument("--gfx", help="gfx à exporter, séparés par des virgules (défaut : tous les SWF du dossier)")
    p.add_argument("--echelle", type=float, default=1.0, help="échelle des PNG (défaut 1)")
    p.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="processus en parallèle")
    p.add_argument("--sans-palette", action="store_true", help="garder tous les PNG en RGBA 32 bits")
    a = p.parse_args()

    swfsvg = trouver_swfsvg(a.swfsvg)
    os.makedirs(a.sortie, exist_ok=True)
    animes = lire_liste_animes(a.animes or os.path.join(a.sortie, "sprites_animes.txt"))
    tous = sorted((f[:-4] for f in os.listdir(a.sprites) if f.lower().endswith(".swf")), key=cle_gfx)
    choisis = [g.strip() for g in a.gfx.split(",") if g.strip()] if a.gfx else tous
    absents = [g for g in choisis if g not in tous]
    if absents:
        sys.exit("SWF absents : " + ", ".join(absents))
    if not choisis:
        sys.exit("aucun SWF dans " + a.sprites)

    lignes, messages, octets = [], [], 0
    with tempfile.TemporaryDirectory(prefix="sprites-") as temporaire:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            taches = [pool.submit(exporter_gfx, g, os.path.join(a.sprites, g + ".swf"), a.sortie, g in animes,
                                  a.echelle, not a.sans_palette, swfsvg, temporaire) for g in choisis]
            for t in taches:
                l, m, o = t.result()
                lignes += l; messages += m; octets += o

    retirer_anciens(a.sortie, set(choisis) if a.gfx else None, {"%s_%s.png" % (l[0], l[1]) for l in lignes})
    ancres = os.path.join(a.sortie, "ancres.tsv")
    if a.gfx:
        gardees = [l for l in lire_ancres(ancres) if l[0] not in set(choisis)]
        lignes = gardees + lignes
    lignes.sort(key=lambda l: (cle_gfx(l[0]), l[1]))
    with open(ancres, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(EN_TETE) + "\n")
        for l in lignes:
            f.write("\t".join(str(v) for v in l) + "\n")
    for m in messages:
        print(m)
    print("%d SWF, %d PNG (%.1f Mo), %d lignes dans %s, %d messages" % (
        len(choisis), sum(1 for l in lignes if l[0] in set(choisis)), octets / 1e6, len(lignes), ancres, len(messages)))


if __name__ == "__main__":
    main()

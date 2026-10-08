#!/usr/bin/env python3
"""Exporte les sprites d'acteurs du client Dofus 1.34 en PNG, avec leurs ancres.

usage : exporter_sprites.py <client>/clips/sprites <sortie> [--swfsvg CHEMIN] [--animes FICHIER]
                            [--gfx 10,11,...] [--anims hit,die[:pas],...] [--pas N]
                            [--echelle 1] [--jobs N] [--sans-palette]

Pour chaque <gfx>.swf du dossier (cairosvg et Pillow requis, swfsvg 0.2.3 ou plus) :

- famille « static » : <gfx>_static<O>.png, O dans S, R, L, F, B : le symbole exporté static<O> (la
  casse du nom d'export est ignorée : quelques SWF exportent StaticR). On rend sa dernière image
  utile (`swfsvg --list`) : c'est l'image sur laquelle le client reste une fois l'animation de repos
  jouée (l'épouvantail 1219 sort du sol ; sur une respiration en boucle, c'est une image du cycle).
  Les directions 3, 4 et 7 n'ont pas de fichier : le client retourne R, S et L (`_xscale` -100).
  <gfx>_scene.png quand le SWF n'exporte aucun static<O> (épées de combat 0-5, tombes 13...123,
  quelques monstres) : l'image 1 de la scène, ce que montre le clip chargé sans animation attachée.
- familles animées (walk, run, hit, die, anim0, emote1, bonus...) : <gfx>_<famille><O>.png pour
  chaque orientation que le SWF exporte, bande de toutes les images utiles du symbole rendues dans
  un même cadre (`swfsvg --frame all`), une image sur « pas » (pas 2 : images 1, 3, 5...). L'image k
  occupe la case k de la bande, rangée ligne par ligne : une seule ligne tant qu'elle tient en
  32 767 px de large, sinon une grille équilibrée (au-delà, libgdiplus ne décode plus le PNG et le
  bot sous Mono s'arrête). Une bande de plus de 16 Mpx est refusée (message).

Familles exportées : sans --anims, « static » plus celles du fichier --animes (par défaut
<sortie>/sprites_animes.txt) pour ce gfx ; avec --anims, exactement celles-là (ajouter « static »
pour refaire aussi les poses). Format de sprites_animes.txt, une ligne par gfx :
    <gfx> [<famille>[:<pas>],...]     # commentaire
un gfx seul vaut « walk,run » (format historique) ; le pas vaut 1 sans « :<pas> ». --pas donne le
pas des familles de --anims qui n'en précisent pas. Le pas divise 40 (ips = 40 / pas).

Écrit <sortie>/ancres.tsv (UTF-8, tabulations, une ligne d'en-tête) :
    gfx  anim  xmin  ymin  largeur  hauteur  images  ips  fin
xmin, ymin : position, en pixels du PNG, de son coin haut-gauche par rapport au point d'ancrage du
client (pied du personnage) ; le pixel (-xmin, -ymin) de chaque image est donc le point d'ancrage.
largeur, hauteur : taille d'une image ; images : nombre d'images de la bande (1 pour static et scene).
ips : images par seconde de la bande (40, la cadence du client, divisée par le pas).
fin : ce que fait le client à la fin de la bande, d'après la colonne fin de `swfsvg --list` :
boucle, arret (dernière image tenue), static (retour à la pose de repos) ou suite:<anim>. Une
image seule (static, scene) vaut arret.
Pour un sprite retourné (directions 3, 4, 7), l'image retournée se pose à x = ancre - (xmin + largeur).

Les zones entièrement transparentes sont rognées ; les PNG sont enregistrés en palette 8 bits avec
transparence quand l'écart avec l'original reste invisible (--sans-palette pour l'éviter).
Avec --gfx ou --anims, seules les lignes et les PNG de ces gfx et de ces familles sont remplacés dans
un ancres.tsv existant ; les lignes gardées d'un ancien ancres.tsv à 7 colonnes reçoivent ips 40 et
la fin lue par `swfsvg --list`.
"""
import argparse, concurrent.futures, io, os, re, shutil, subprocess, sys, tempfile

import cairosvg
from PIL import Image, ImageChops, ImageStat

ORIENTATIONS = "SRLFB"
CYCLES = ("walk", "run")
EN_TETE = ("gfx", "anim", "xmin", "ymin", "largeur", "hauteur", "images", "ips", "fin")
COLONNES_HISTORIQUES = 7
ECART_PALETTE = 1.5  # écart moyen maximal (0-255, par canal) accepté pour la palette 8 bits
IPS = 40  # cadence des clips de sprites du client (preloader.swf : DOUBLEFRAMERATE)
COTE_MAX = 32767  # au-delà, libgdiplus (Mono) ne crée plus le Bitmap et tue le processus au décodage
PIXELS_MAX = 16 * 1024 * 1024  # surface maximale qu'ActorSprites accepte de décoder
FAMILLE = re.compile(r"^[A-Za-z][A-Za-z0-9]*$")
FIN = re.compile(r"^(?:boucle|arret|static|suite:[A-Za-z0-9]+)$")


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


def lire_familles(texte, pas_defaut=1, source="--anims"):
    """« hit,die:2 » -> [("hit", 1), ("die", 2)] ; ValueError si un nom ou un pas est invalide."""
    familles = []
    for morceau in texte.split(","):
        morceau = morceau.strip()
        if not morceau:
            continue
        nom, _, pas = morceau.partition(":")
        if not FAMILLE.match(nom):
            raise ValueError("%s : famille invalide « %s »" % (source, morceau))
        try:
            pas = int(pas) if pas else pas_defaut
        except ValueError:
            raise ValueError("%s : pas invalide « %s »" % (source, morceau))
        if pas < 1 or IPS % pas:
            raise ValueError("%s : le pas de « %s » doit diviser %d" % (source, morceau, IPS))
        familles.append((nom, pas))
    return familles


def lire_liste_animes(chemin):
    """sprites_animes.txt -> {gfx: [(famille, pas), ...]} ; un gfx seul vaut walk,run (pas 1).

    Une ligne : « <gfx> [<famille>[:<pas>],...] » ; # commence un commentaire. Un gfx présent sur
    plusieurs lignes reçoit l'union de ses familles (le dernier pas l'emporte)."""
    liste = {}
    if chemin and os.path.isfile(chemin):
        with open(chemin, encoding="utf-8") as f:
            for numero, ligne in enumerate(f, 1):
                ligne = ligne.split("#", 1)[0].strip()
                if not ligne:
                    continue
                gfx, _, reste = ligne.partition(" ")
                if not gfx.isdigit():
                    raise ValueError("%s ligne %d : gfx invalide « %s »" % (chemin, numero, gfx))
                familles = lire_familles(reste.replace(" ", ""), 1, "%s ligne %d" % (chemin, numero)) if reste.strip() \
                    else [(c, 1) for c in CYCLES]
                courant = dict(liste.get(gfx, []))
                for nom, pas in familles:
                    courant[nom] = pas
                liste[gfx] = list(courant.items())
    return liste


def lister(swfsvg, swf):
    """Symboles exportés du SWF : nom en minuscules -> (nom réel, images utiles, fin)."""
    lignes = lancer(swfsvg + ["--list", swf]).splitlines()
    if not lignes or "fin" not in lignes[0].split("\t"):
        raise RuntimeError("swfsvg --list sans colonne fin : swfsvg 0.2.3 ou plus requis")
    symboles = {}
    for ligne in lignes[1:]:
        col = ligne.split("\t")
        if len(col) >= 6 and col[2] != "scene" and col[3].isdigit():
            symboles.setdefault(col[0].lower(), (col[0], int(col[3]), col[5]))
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


def disposition(images, largeur, hauteur):
    """(colonnes, lignes) d'une bande : une ligne si elle tient en 32 767 px, sinon une grille
    équilibrée de lignes pleines (la dernière peut être incomplète). RuntimeError si la bande ne tient
    pas sous 32 767 px de côté ou sous 16 Mpx."""
    if largeur > COTE_MAX or hauteur > COTE_MAX:
        raise RuntimeError("image de %d x %d px : plus de %d px de côté" % (largeur, hauteur, COTE_MAX))
    if images * largeur <= COTE_MAX:
        colonnes, lignes = images, 1
    else:
        lignes = -(-images * largeur // COTE_MAX)
        colonnes = -(-images // lignes)
        while colonnes * largeur > COTE_MAX:
            lignes += 1
            colonnes = -(-images // lignes)
        lignes = -(-images // colonnes)
    if lignes * hauteur > COTE_MAX or colonnes * largeur * lignes * hauteur > PIXELS_MAX:
        raise RuntimeError("bande de %d images de %d x %d px : plus de %d px de haut ou de 16 Mpx"
                           % (images, largeur, hauteur, COTE_MAX))
    return colonnes, lignes


def fin_valide(fin, gfx, anim, messages):
    """Fin lue par swfsvg, telle qu'ancres.tsv l'accepte ; une suite au nom illisible par le bot
    (« suite:carring_C ») devient static, avec un message."""
    if FIN.match(fin or ""):
        return fin
    messages.append("%s : %s fin « %s » illisible par le bot, static retenu" % (gfx, anim, fin))
    return "static"


def famille_de(anim):
    """Famille d'un nom de bande : staticR -> static, scene -> static, anim18L -> anim18."""
    if anim == "scene":
        return "static"
    return anim[:-1] if anim and anim[-1] in ORIENTATIONS else anim


def exporter_gfx(gfx, swf, sortie, familles, echelle, palette, swfsvg, temporaire):
    """Exporte un SWF ; renvoie (lignes d'ancres.tsv, messages, octets écrits). Ne lève jamais."""
    lignes, messages, octets = [], [], 0
    try:
        symboles = lister(swfsvg, swf)
        noms = {nom.lower() for nom, _ in familles}
        with tempfile.TemporaryDirectory(dir=temporaire) as dossier:
            if "static" in noms:
                l, m, o = exporter_statiques(gfx, swf, sortie, symboles, echelle, palette, swfsvg, dossier)
                lignes += l; messages += m; octets += o
            for famille, pas in familles:
                if famille.lower() == "static":
                    continue
                trouve = False
                for o in ORIENTATIONS:
                    s = symboles.get(famille.lower() + o.lower())
                    if not s:
                        continue
                    trouve = True
                    anim = famille + o
                    sous = os.path.join(dossier, anim)
                    index = rendre_svg(swfsvg, ["--frame", "all", swf, sous, s[0]], sous)
                    # Ordre des images d'après la colonne « image » de l'index, pas le nom de fichier.
                    fichiers = sorted((f for f in index if f.startswith(s[0] + "_f")), key=lambda f: int(index[f][9]))
                    fichiers = fichiers[::pas]
                    try:
                        ligne, taille = bande([os.path.join(sous, f) for f in fichiers], gfx, anim, sortie, echelle, palette)
                    except RuntimeError as erreur:
                        messages.append("%s : %s refusée (%s)" % (gfx, anim, erreur))
                        continue
                    if ligne:
                        ligne += (IPS // pas, "arret" if ligne[6] == 1 else fin_valide(s[2], gfx, anim, messages))
                        lignes.append(ligne); octets += taille
                    else:
                        messages.append("%s : %s vide" % (gfx, anim))
                if not trouve and famille.lower() not in CYCLES:
                    messages.append("%s : aucun symbole %s<O>" % (gfx, famille))
    except Exception as erreur:  # un SWF illisible ne doit pas arrêter la série
        messages.append("%s : %s" % (gfx, erreur))
    return lignes, messages, octets


def exporter_statiques(gfx, swf, sortie, symboles, echelle, palette, swfsvg, dossier):
    lignes, messages, octets = [], [], 0
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
    return lignes, messages, octets


def poser(svg, gfx, anim, sortie, echelle, palette):
    image, x0, y0 = svg_vers_image(svg, echelle)
    cadre = image.getchannel("A").getbbox()
    if not cadre:
        return None, 0
    image = image.crop(cadre)
    taille = enregistrer(image, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    # Une image seule reste affichée telle quelle : ips de la cadence du client, fin « arret ».
    return (gfx, anim, x0 + cadre[0], y0 + cadre[1], image.width, image.height, 1, IPS, "arret"), taille


def bande(svgs, gfx, anim, sortie, echelle, palette):
    """Assemble les images d'une bande (une ligne, ou une grille au-delà de 32 767 px) ; renvoie les
    sept premières colonnes de sa ligne d'ancres.tsv. RuntimeError si elle est trop grande."""
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
    colonnes, lignes = disposition(len(images), largeur, hauteur)
    feuille = Image.new("RGBA", (largeur * colonnes, hauteur * lignes), (0, 0, 0, 0))
    for k, (im, _, _) in enumerate(images):
        feuille.paste(im.crop(union), (k % colonnes * largeur, k // colonnes * hauteur))
    _, x0, y0 = images[0]
    taille = enregistrer(feuille, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    return (gfx, anim, x0 + union[0], y0 + union[1], largeur, hauteur, len(images)), taille


PRODUIT = re.compile(r"^(\d+)_([A-Za-z][A-Za-z0-9]*)\.png$")


def retirer_anciens(sortie, cible, gardes):
    """Supprime les PNG <gfx>_<anim>.png laissés par un export précédent : de tous les gfx si cible
    vaut None, sinon des seuls (gfx, famille) que cible désigne (cible(gfx, famille) vrai). Jamais
    les autres fichiers du dossier, comme les anciens <gfx><O>.png ou les <gfx>_<anim>.couleurs.png."""
    for f in os.listdir(sortie):
        m = PRODUIT.match(f)
        if m and f not in gardes and (cible is None or cible(m.group(1), famille_de(m.group(2)))):
            os.remove(os.path.join(sortie, f))


def cle_gfx(valeur):
    return (0, int(valeur)) if valeur.isdigit() else (1, valeur)


def lire_ancres(chemin):
    """Lignes d'un ancres.tsv à 7 colonnes (historique : ips et fin à None) ou 9."""
    lignes = []
    if os.path.isfile(chemin):
        with open(chemin, encoding="utf-8") as f:
            for ligne in f:
                col = ligne.rstrip("\r\n").split("\t")
                if col[0] == EN_TETE[0] or len(col) not in (COLONNES_HISTORIQUES, len(EN_TETE)):
                    continue
                valeurs = (col[0], col[1]) + tuple(int(v) for v in col[2:7])
                if len(col) == len(EN_TETE):
                    valeurs += (int(col[7]), col[8])
                else:
                    valeurs += (None, None)
                lignes.append(valeurs)
    return lignes


def completer(lignes, sprites, swfsvg, messages):
    """Lignes gardées d'un ancres.tsv historique : ips 40 (export au pas 1) ; fin « arret » pour une
    image seule, sinon celle que `swfsvg --list` donne au symbole de la bande."""
    a_lister = sorted({l[0] for l in lignes if l[8] is None and l[6] > 1}, key=cle_gfx)
    fins = {}
    for gfx in a_lister:
        swf = os.path.join(sprites, gfx + ".swf")
        try:
            fins[gfx] = lister(swfsvg, swf) if os.path.isfile(swf) else {}
        except RuntimeError as erreur:
            messages.append("%s : %s" % (gfx, erreur))
            fins[gfx] = {}
    resultat = []
    for l in lignes:
        if l[8] is None:
            if l[6] == 1:
                fin = "arret"
            else:
                s = fins.get(l[0], {}).get(l[1].lower())
                fin = fin_valide(s[2], l[0], l[1], messages) if s else "boucle"
                if not s:
                    messages.append("%s : %s absent de swfsvg --list, fin « boucle » gardée" % (l[0], l[1]))
            l = l[:7] + (IPS, fin)
        resultat.append(l)
    return resultat


def main():
    p = argparse.ArgumentParser(description="Sprites d'acteurs du client Dofus 1.34 -> PNG + ancres.tsv")
    p.add_argument("sprites", help="dossier clips/sprites du client")
    p.add_argument("sortie", help="dossier de sortie (Outil_Azur_complet/Resources/Bot/sprites)")
    p.add_argument("--swfsvg", help="binaire swfsvg 0.2.3+ (sinon $SWFSVG, le PATH ou swfsvg/target/release)")
    p.add_argument("--animes", help="familles animées par gfx (défaut : <sortie>/sprites_animes.txt)")
    p.add_argument("--gfx", help="gfx à exporter, séparés par des virgules (défaut : tous les SWF du dossier)")
    p.add_argument("--anims", help="familles à exporter à la place de static + sprites_animes.txt, "
                                   "séparées par des virgules, chacune avec son pas facultatif (hit,die:2)")
    p.add_argument("--pas", type=int, default=1, help="pas des familles de --anims sans « :pas » (diviseur de 40, défaut 1)")
    p.add_argument("--echelle", type=float, default=1.0, help="échelle des PNG (défaut 1)")
    p.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="processus en parallèle")
    p.add_argument("--sans-palette", action="store_true", help="garder tous les PNG en RGBA 32 bits")
    a = p.parse_args()

    if a.pas < 1 or IPS % a.pas:
        sys.exit("--pas doit diviser %d" % IPS)
    try:
        anims = lire_familles(a.anims, a.pas) if a.anims is not None else None
        animes = lire_liste_animes(a.animes or os.path.join(a.sortie, "sprites_animes.txt"))
    except ValueError as erreur:
        sys.exit(str(erreur))
    if anims is not None and not anims:
        sys.exit("--anims : aucune famille")
    swfsvg = trouver_swfsvg(a.swfsvg)
    os.makedirs(a.sortie, exist_ok=True)
    tous = sorted((f[:-4] for f in os.listdir(a.sprites) if f.lower().endswith(".swf")), key=cle_gfx)
    choisis = [g.strip() for g in a.gfx.split(",") if g.strip()] if a.gfx else tous
    absents = [g for g in choisis if g not in tous]
    if absents:
        sys.exit("SWF absents : " + ", ".join(absents))
    if not choisis:
        sys.exit("aucun SWF dans " + a.sprites)

    familles = {g: anims if anims is not None else [("static", 1)] + animes.get(g, []) for g in choisis}
    lignes, messages, octets = [], [], 0
    with tempfile.TemporaryDirectory(prefix="sprites-") as temporaire:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            taches = [pool.submit(exporter_gfx, g, os.path.join(a.sprites, g + ".swf"), a.sortie, familles[g],
                                  a.echelle, not a.sans_palette, swfsvg, temporaire) for g in choisis]
            for t in taches:
                l, m, o = t.result()
                lignes += l; messages += m; octets += o

    partiel = bool(a.gfx) or anims is not None
    choix = set(choisis)
    noms = {g: {f.lower() for f, _ in familles[g]} for g in choisis}
    cible = (lambda gfx, famille: gfx in choix and famille.lower() in noms[gfx]) if partiel else None
    retirer_anciens(a.sortie, cible, {"%s_%s.png" % (l[0], l[1]) for l in lignes})
    ancres = os.path.join(a.sortie, "ancres.tsv")
    if partiel:
        gardees = [l for l in lire_ancres(ancres) if not cible(l[0], famille_de(l[1]))]
        lignes = completer(gardees, a.sprites, swfsvg, messages) + lignes
    lignes.sort(key=lambda l: (cle_gfx(l[0]), l[1]))
    with open(ancres, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(EN_TETE) + "\n")
        for l in lignes:
            f.write("\t".join(str(v) for v in l) + "\n")
    for m in messages:
        print(m)
    print("%d SWF, %d PNG (%.1f Mo), %d lignes dans %s, %d messages" % (
        len(choisis), sum(1 for l in lignes if l[0] in choix and (not partiel or cible(l[0], famille_de(l[1])))),
        octets / 1e6, len(lignes), ancres, len(messages)))


if __name__ == "__main__":
    main()

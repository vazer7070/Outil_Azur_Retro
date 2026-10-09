#!/usr/bin/env python3
"""Exporte les sprites d'acteurs du client Dofus 1.34 en PNG, avec leurs ancres.

usage : exporter_sprites.py <client>/clips/sprites <sortie> [--swfsvg CHEMIN] [--animes FICHIER]
                            [--gfx 10,11,...] [--anims hit,die[:pas],...] [--pas N]
                            [--echelle 1] [--jobs N] [--sans-palette] [--masques]

Pour chaque <gfx>.swf du dossier (cairosvg et Pillow requis, swfsvg 0.2.3 ou plus, 0.2.5 avec
--masques) :

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

--masques : masques de recoloration. Le client recolore certains enfants des sprites d'acteurs par
`GAC.applyColor(<enfant>, <zone>)` : un aplat de la couleur du GM (zones 1 à 3) qui garde l'alpha.
Pour chaque PNG d'un gfx qui a des zones (`swfsvg --zones-list`), <gfx>_<anim>.couleurs.png a la
même taille et la même disposition que lui : sa composante rouge est la part de la zone dominante
du pixel (couverture, 0 à 255 par pas de 17, rendus `swfsvg --zones 123` moins `--zones aucune`),
sa composante verte l'index (1 à 255) de la couleur d'origine du pixel dans cette zone, la bleue 0 ;
un pixel sans zone est noir. Le bot calcule alors pixel = PNG + (couleur du GM - couleur
d'origine) x rouge / 255 (alpha du PNG). Écrit aussi <sortie>/couleurs.tsv (UTF-8, tabulations,
une ligne d'en-tête), une ligne par index de chaque masque :
    gfx  anim  index  zone  couleur
couleur : couleur d'origine du SWF en hexadécimal RRGGBB. Sans --masques, les masques et les lignes
des familles réexportées sont retirés (leur cadre ne correspondrait plus).
"""
import argparse, collections, concurrent.futures, io, os, re, shutil, subprocess, sys, tempfile

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
EN_TETE_COULEURS = ("gfx", "anim", "index", "zone", "couleur")
PAS_COUVERTURE = 17  # couverture des masques sur 16 niveaux (0, 17, ..., 255) : palette exacte
FIABLE = 240  # couverture à partir de laquelle la couleur d'origine d'un pixel sert de référence
ECART_REFERENCE = 8  # deux références plus proches (écart par canal) sont une même couleur
INDEX_MAX = 255  # index de couleur d'origine par masque (composante verte)


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


def zones_de(swfsvg, swf):
    """Zones de couleur (1 à 3) que les scripts du SWF recolorent (`swfsvg --zones-list`)."""
    lignes = lancer(swfsvg + ["--zones-list", swf]).splitlines()
    if not lignes or lignes[0].split("\t") != ["clip", "instance", "zone"]:
        raise RuntimeError("swfsvg --zones-list illisible : swfsvg 0.2.5 ou plus requis")
    return {int(c[2]) for c in (l.split("\t") for l in lignes[1:]) if len(c) == 3 and c[2] in ("1", "2", "3")}


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


def svg_vers_image(chemin, echelle, magenta=True):
    """PNG RGBA et position de son pixel (0, 0) dans le repère du symbole, en pixels du PNG. Le
    magenta pur est effacé, sauf pour les rendus des zones (magenta=False), comparés entre eux."""
    with open(chemin, encoding="utf-8") as f:
        debut = f.read(512)
    m = VIEWBOX.search(debut)
    if not m:
        raise RuntimeError("viewBox absent : " + os.path.basename(chemin))
    x0, y0 = round(float(m.group(1)) * echelle), round(float(m.group(2)) * echelle)
    image = Image.open(io.BytesIO(cairosvg.svg2png(url=chemin, scale=echelle, background_color=None))).convert("RGBA")
    return (effacer_magenta(image) if magenta else image), x0, y0


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


NON_NUL = re.compile(rb"[^\x00]")


def pixels_de_zone(d, w, b):
    """Pixels de zone d'une feuille rendue trois fois à l'identique (d : rendu normal, w : --zones 123,
    b : --zones aucune, en RGBA) : liste de (position, zone, couverture 0-255, couleur d'origine
    estimée, fiable). La couverture est w - b sur la composante de la zone dominante (zones 1, 2, 3 :
    rouge, vert, bleu) ; les alphas des trois rendus sont égaux, donc d - b = couleur d'origine x
    couverture / 255. Un pixel est fiable quand sa zone le couvre presque seule (estimation exacte à
    1 près). Seuls les pixels où w et b diffèrent sont lus (recherche dans les octets bruts)."""
    ecart = ImageChops.difference(w, b)
    r, g, bl, _ = ecart.split()
    diffs = ImageChops.lighter(ImageChops.lighter(r, g), bl).tobytes()
    od, ow, ob = d.tobytes(), w.tobytes(), b.tobytes()
    pixels = []
    for t in NON_NUL.finditer(diffs):
        i = t.start()
        o = 4 * i
        if od[o + 3] == 0:
            continue
        parts = (ow[o] - ob[o], ow[o + 1] - ob[o + 1], ow[o + 2] - ob[o + 2])
        k = 0 if parts[0] >= parts[1] and parts[0] >= parts[2] else (1 if parts[1] >= parts[2] else 2)
        part = parts[k]
        if part <= 0:
            continue
        origine = tuple(min(255, max(0, round((od[o + c] - ob[o + c]) * 255 / part))) for c in range(3))
        fiable = part >= FIABLE and all(parts[c] <= 0 for c in range(3) if c != k)
        pixels.append((i, k + 1, part, origine, fiable))
    return pixels


def regrouper(compte):
    """Couleurs de référence d'une zone, les plus fréquentes d'abord : une couleur à moins de
    ECART_REFERENCE (par canal) d'une référence retenue s'y rattache ; une couleur vue moins de deux
    fois (bord intérieur entre deux aplats) n'en devient une que si la zone n'a rien d'autre."""
    centres = []
    for couleur, n in sorted(compte.items(), key=lambda e: (-e[1], e[0])):
        if centres and n < 2:
            break
        if all(max(abs(a - c) for a, c in zip(couleur, centre)) > ECART_REFERENCE for centre in centres):
            centres.append(couleur)
    return centres


def ecrire_masque(d, w, b, chemin, references):
    """Écrit le masque de recoloration d'une feuille (voir --masques). Renvoie ses lignes de
    couleurs.tsv sans gfx ni anim, [(index, zone, "rrggbb")], et sa taille en octets, ou (None, 0)
    sans pixel de zone. references : {zone: Counter des couleurs d'origine fiables}, cumulé sur les
    feuilles du gfx (poses static d'abord) ; chaque pixel prend la référence la plus proche."""
    pixels = pixels_de_zone(d, w, b)
    for _, zone, _, origine, fiable in pixels:
        if fiable:
            references.setdefault(zone, collections.Counter())[origine] += 1
    centres = {}
    for zone in sorted({p[1] for p in pixels}):
        compte = references.get(zone)
        if not compte:
            # Zone sans pixel fiable (parties fines) : estimations des pixels les plus couverts.
            haut = max(p[2] for p in pixels if p[1] == zone) - PAS_COUVERTURE
            compte = collections.Counter(p[3] for p in pixels if p[1] == zone and p[2] >= haut)
        centres[zone] = regrouper(compte)
    index, table, valeurs = {}, [], {}
    for i, zone, part, origine, _ in pixels:
        niveau = min(255, round(part / PAS_COUVERTURE) * PAS_COUVERTURE)
        if niveau == 0:
            continue
        proche = lambda c: sum((a - o) ** 2 for a, o in zip(c, origine))
        cle = (zone, min(centres[zone], key=proche))
        if cle not in index:
            if len(table) >= INDEX_MAX:
                # Plus de 255 couleurs d'origine : la plus proche déjà indexée dans la même zone.
                cle = min((c for c in table if c[0] == zone), key=lambda c: proche(c[1]), default=None)
                if cle is None:
                    continue
            else:
                table.append(cle)
                index[cle] = len(table)
        valeurs[i] = (niveau, index[cle])
    if not table:
        return None, 0
    return [(index[c], c[0], "%02x%02x%02x" % c[1]) for c in table], enregistrer_masque(valeurs, d.size, chemin)


def enregistrer_masque(valeurs, taille, chemin):
    """Masque {position: (couverture, index)} d'une feuille de taille donnée, les autres pixels noirs :
    en palette exacte (sans transparence) quand il a 256 couleurs au plus, sinon en RGB. La palette
    suit l'ordre des couleurs : le fichier ne dépend que de l'image (export reproductible)."""
    surface = taille[0] * taille[1]
    couleurs = sorted(set(valeurs.values()) | {(0, 0)})
    if len(couleurs) <= 256:
        rang = {c: k for k, c in enumerate(couleurs)}
        donnees = bytearray(surface)  # rang 0 : (0, 0), le noir
        for i, c in valeurs.items():
            donnees[i] = rang[c]
        masque = Image.frombytes("P", taille, bytes(donnees))
        masque.putpalette([v for niveau, k in couleurs for v in (niveau, k, 0)])
    else:
        donnees = bytearray(3 * surface)
        for i, (niveau, k) in valeurs.items():
            donnees[3 * i] = niveau
            donnees[3 * i + 1] = k
        masque = Image.frombytes("RGB", taille, bytes(donnees))
    tampon = io.BytesIO()
    masque.save(tampon, "PNG", optimize=True)
    with open(chemin, "wb") as f:
        f.write(tampon.getvalue())
    return len(tampon.getvalue())


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


class Masques:
    """Masques de recoloration d'un gfx en cours d'export : rendus des zones à faire (--zones 123 et
    --zones aucune) et références de couleurs d'origine cumulées sur ses feuilles."""

    MODES = ("123", "aucune")

    def __init__(self, swfsvg, swf, echelle):
        self.swfsvg, self.swf, self.echelle = swfsvg, swf, echelle
        self.references = {}
        self.lignes = []  # lignes de couleurs.tsv : (gfx, anim, index, zone, couleur)
        self.octets = 0
        self.nombre = 0

    def rendre_noms(self, options, sous, noms):
        """Rend les symboles noms (options : --frame, --scene...) dans les deux modes, dans les
        dossiers <sous>_123 et <sous>_aucune, à côté du rendu normal <sous>."""
        for mode in self.MODES:
            dossier = sous + "_" + mode
            rendre_svg(self.swfsvg, ["--zones", mode] + options + [self.swf, dossier] + noms, dossier)

    def images(self, svg):
        """Rendus des zones du SVG rendu normalement en `svg` (même nom dans <dossier>_<mode>)."""
        dossier, nom = os.path.split(svg)
        return [svg_vers_image(os.path.join(dossier + "_" + mode, nom), self.echelle, magenta=False)[0] for mode in self.MODES]

    def ecrire(self, d, w, b, gfx, anim, sortie):
        table, taille = ecrire_masque(d, w, b, os.path.join(sortie, "%s_%s.couleurs.png" % (gfx, anim)), self.references)
        if table:
            self.lignes += [(gfx, anim) + t for t in table]
            self.octets += taille
            self.nombre += 1


def exporter_gfx(gfx, swf, sortie, familles, echelle, palette, swfsvg, temporaire, masques=False):
    """Exporte un SWF ; renvoie (lignes d'ancres.tsv, messages, octets écrits, lignes de couleurs.tsv,
    nombre et octets des masques). Ne lève jamais."""
    lignes, messages, octets = [], [], 0
    zones = None
    try:
        symboles = lister(swfsvg, swf)
        if masques and zones_de(swfsvg, swf):
            zones = Masques(swfsvg, swf, echelle)
        noms = {nom.lower() for nom, _ in familles}
        with tempfile.TemporaryDirectory(dir=temporaire) as dossier:
            if "static" in noms:
                l, m, o = exporter_statiques(gfx, swf, sortie, symboles, echelle, palette, swfsvg, dossier, zones)
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
                    if zones:
                        zones.rendre_noms(["--frame", "all"], sous, [s[0]])
                    # Ordre des images d'après la colonne « image » de l'index, pas le nom de fichier.
                    fichiers = sorted((f for f in index if f.startswith(s[0] + "_f")), key=lambda f: int(index[f][9]))
                    fichiers = fichiers[::pas]
                    try:
                        ligne, taille = bande([os.path.join(sous, f) for f in fichiers], gfx, anim, sortie, echelle, palette, zones)
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
    if not zones:
        return lignes, messages, octets, [], 0, 0
    return lignes, messages, octets, zones.lignes, zones.nombre, zones.octets


def exporter_statiques(gfx, swf, sortie, symboles, echelle, palette, swfsvg, dossier, zones=None):
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
            if zones:
                zones.rendre_noms(["--frame", str(image)], sous, [nom for _, nom in liste])
            restants = []
            for o, nom in liste:
                ligne, taille = None, 0
                if nom + ".svg" in index:
                    ligne, taille = poser(os.path.join(sous, nom + ".svg"), gfx, "static" + o, sortie, echelle, palette, zones)
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
        if zones:
            zones.rendre_noms(["--scene", "--name", "scene"], sous, [])
        ligne, taille = poser(os.path.join(sous, "scene.svg"), gfx, "scene", sortie, echelle, palette, zones)
        if ligne: lignes.append(ligne); octets += taille
        else: messages.append("%s : aucun static<O> et scène vide, rien d'exporté" % gfx)
    return lignes, messages, octets


def poser(svg, gfx, anim, sortie, echelle, palette, zones=None):
    image, x0, y0 = svg_vers_image(svg, echelle)
    cadre = image.getchannel("A").getbbox()
    if not cadre:
        return None, 0
    if zones:
        w, b = zones.images(svg)
        if w.size != image.size or b.size != image.size:
            raise RuntimeError("%s : rendus des zones de %s dans un autre cadre" % (gfx, anim))
        zones.ecrire(image.crop(cadre), w.crop(cadre), b.crop(cadre), gfx, anim, sortie)
    image = image.crop(cadre)
    taille = enregistrer(image, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    # Une image seule reste affichée telle quelle : ips de la cadence du client, fin « arret ».
    return (gfx, anim, x0 + cadre[0], y0 + cadre[1], image.width, image.height, 1, IPS, "arret"), taille


def bande(svgs, gfx, anim, sortie, echelle, palette, zones=None):
    """Assemble les images d'une bande (une ligne, ou une grille au-delà de 32 767 px) ; renvoie les
    sept premières colonnes de sa ligne d'ancres.tsv. RuntimeError si elle est trop grande. Avec
    zones (--masques), écrit aussi son masque, de même taille et de même disposition."""
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
    taille_feuille = (largeur * colonnes, hauteur * lignes)
    feuille = Image.new("RGBA", taille_feuille, (0, 0, 0, 0))
    for k, (im, _, _) in enumerate(images):
        feuille.paste(im.crop(union), (k % colonnes * largeur, k // colonnes * hauteur))
    if zones:
        w, b = Image.new("RGBA", taille_feuille, (0, 0, 0, 0)), Image.new("RGBA", taille_feuille, (0, 0, 0, 0))
        for k, s in enumerate(svgs):
            iw, ib = zones.images(s)
            if iw.size != images[0][0].size or ib.size != images[0][0].size:
                raise RuntimeError("%s : rendus des zones de %s dans un autre cadre" % (gfx, anim))
            position = (k % colonnes * largeur, k // colonnes * hauteur)
            w.paste(iw.crop(union), position)
            b.paste(ib.crop(union), position)
        zones.ecrire(feuille, w, b, gfx, anim, sortie)
    _, x0, y0 = images[0]
    taille = enregistrer(feuille, os.path.join(sortie, "%s_%s.png" % (gfx, anim)), palette)
    return (gfx, anim, x0 + union[0], y0 + union[1], largeur, hauteur, len(images)), taille


PRODUIT = re.compile(r"^(\d+)_([A-Za-z][A-Za-z0-9]*)(?:\.couleurs)?\.png$")


def retirer_anciens(sortie, cible, gardes):
    """Supprime les PNG <gfx>_<anim>.png et les masques <gfx>_<anim>.couleurs.png laissés par un
    export précédent : de tous les gfx si cible vaut None, sinon des seuls (gfx, famille) que cible
    désigne (cible(gfx, famille) vrai). Un masque suit sa bande : réexportée sans --masques, elle
    perd le sien (son cadre ne correspondrait plus). Jamais les autres fichiers du dossier, comme
    les anciens <gfx><O>.png. Renvoie le nombre de masques retirés."""
    retires = 0
    for f in os.listdir(sortie):
        m = PRODUIT.match(f)
        if m and f not in gardes and (cible is None or cible(m.group(1), famille_de(m.group(2)))):
            os.remove(os.path.join(sortie, f))
            retires += f.endswith(".couleurs.png")
    return retires


def lire_couleurs(chemin):
    """Lignes de couleurs.tsv : [(gfx, anim, index, zone, couleur)], lignes illisibles ignorées."""
    lignes = []
    if os.path.isfile(chemin):
        with open(chemin, encoding="utf-8") as f:
            for ligne in f:
                col = ligne.rstrip("\r\n").split("\t")
                if col[0] == EN_TETE_COULEURS[0] or len(col) != len(EN_TETE_COULEURS):
                    continue
                if col[2].isdigit() and col[3] in ("1", "2", "3") and re.match(r"^[0-9a-f]{6}$", col[4]):
                    lignes.append((col[0], col[1], int(col[2]), int(col[3]), col[4]))
    return lignes


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
    p.add_argument("--masques", action="store_true", help="écrire aussi les masques de recoloration "
                                                          "<gfx>_<anim>.couleurs.png et couleurs.tsv (swfsvg 0.2.5+)")
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
    if a.masques:
        # Un swfsvg sans --zones-list ferait échouer chaque gfx, et retirer ses anciens PNG.
        try:
            zones_de(swfsvg, os.path.join(a.sprites, choisis[0] + ".swf"))
        except RuntimeError as erreur:
            if "option inconnue" in str(erreur) or "0.2.5" in str(erreur):
                sys.exit("--masques : swfsvg 0.2.5 ou plus requis (%s)" % erreur)

    familles = {g: anims if anims is not None else [("static", 1)] + animes.get(g, []) for g in choisis}
    lignes, messages, octets = [], [], 0
    couleurs, masques, octets_masques = [], 0, 0
    with tempfile.TemporaryDirectory(prefix="sprites-") as temporaire:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            taches = [pool.submit(exporter_gfx, g, os.path.join(a.sprites, g + ".swf"), a.sortie, familles[g],
                                  a.echelle, not a.sans_palette, swfsvg, temporaire, a.masques) for g in choisis]
            for t in taches:
                l, m, o, c, n, om = t.result()
                lignes += l; messages += m; octets += o
                couleurs += c; masques += n; octets_masques += om

    partiel = bool(a.gfx) or anims is not None
    choix = set(choisis)
    noms = {g: {f.lower() for f, _ in familles[g]} for g in choisis}
    cible = (lambda gfx, famille: gfx in choix and famille.lower() in noms[gfx]) if partiel else None
    gardes = {"%s_%s.png" % (l[0], l[1]) for l in lignes} | {"%s_%s.couleurs.png" % (c[0], c[1]) for c in couleurs}
    retires = retirer_anciens(a.sortie, cible, gardes)
    if retires and not a.masques:
        messages.append("%d masques de recoloration retirés avec leurs bandes réexportées (relancer avec --masques)" % retires)
    ancres = os.path.join(a.sortie, "ancres.tsv")
    if partiel:
        gardees = [l for l in lire_ancres(ancres) if not cible(l[0], famille_de(l[1]))]
        lignes = completer(gardees, a.sprites, swfsvg, messages) + lignes
    lignes.sort(key=lambda l: (cle_gfx(l[0]), l[1]))
    with open(ancres, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(EN_TETE) + "\n")
        for l in lignes:
            f.write("\t".join(str(v) for v in l) + "\n")
    chemin_couleurs = os.path.join(a.sortie, "couleurs.tsv")
    anciennes = lire_couleurs(chemin_couleurs)
    if couleurs or anciennes:
        produits = {(c[0], c[1]) for c in couleurs}
        gardees = [c for c in anciennes if (c[0], c[1]) not in produits and partiel and not cible(c[0], famille_de(c[1]))]
        couleurs = sorted(gardees + couleurs, key=lambda c: (cle_gfx(c[0]), c[1], c[2]))
        with open(chemin_couleurs, "w", encoding="utf-8", newline="\n") as f:
            f.write("\t".join(EN_TETE_COULEURS) + "\n")
            for c in couleurs:
                f.write("\t".join(str(v) for v in c) + "\n")
    for m in messages:
        print(m)
    print("%d SWF, %d PNG (%.1f Mo), %d lignes dans %s, %d messages" % (
        len(choisis), sum(1 for l in lignes if l[0] in choix and (not partiel or cible(l[0], famille_de(l[1])))),
        octets / 1e6, len(lignes), ancres, len(messages)))
    if a.masques:
        print("%d masques (%.1f Mo), %d lignes dans %s" % (masques, octets_masques / 1e6, len(couleurs), chemin_couleurs))


if __name__ == "__main__":
    main()

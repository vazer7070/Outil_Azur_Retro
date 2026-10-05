#!/usr/bin/env python3
"""Exporte les icônes et images du client Dofus 1.34 pour le bot (PNG optimisés + PROVENANCE.md).

usage :
  exporter_icons.py --client <dossier du client> --sortie <Outil_Azur_complet/Resources/Bot>
                    [--swfsvg <binaire>] [--familles Smileys,Emotes,...] [--travail <dossier>]
                    [--processus N] [--limite N] [--remplacer]

Familles (dossier écrit sous --sortie, voir README.md de ce dossier) :
  Smileys     clips/smileys/<n>.swf              -> Smileys/<n>.png
  Emotes      clips/emotes/<n>.swf               -> Emotes/<n>.png
  Jobs        clips/jobs/<n>.swf                 -> Jobs/<n>.png
  Alignments  clips/alignments/{,mini,orders,feats}/<n>.swf -> Alignments/{,mini/,orders/,feats/}<n>.png
  Emblems     clips/emblems/back/<n>.swf         -> Emblems/back/<n>.png (calque « back » à teinter)
                                                    + Emblems/back/<n>_contour.png (le reste)
              clips/emblems/up/<n>.swf           -> Emblems/up/<n>.png (à teinter en entier)
  Portraits   clips/artworks/big/<n>.swf         -> Portraits/<n>.png
  Items       clips/items/<type>/<gfx>.swf       -> Items/<type>/<gfx>.png
  Spells      clips/spells/icons/<id>.swf        -> sorts/<id>.png (seulement les absents, sauf --remplacer)
  WorldMap    clips/maps/<zone>.swf (exports x_y) -> WorldMap/<zone>/<x_y>.png + WorldMap/<zone>/tuiles.tsv
              (exports subarea_<id>)            -> WorldMap/<zone>/sous-zones/<id>.png + sous-zones.tsv
              clips/maps/hints.swf               -> WorldMap/hints/<id>.png ; dungeon.swf -> WorldMap/dungeon.png
  UI          modules/core.swf (symboles listés dans SYMBOLES_UI) -> Client/<Symbole>.png
              clips/flag.swf (scènes de SCENES_UI, à une image donnée) -> Client/FlagCell.png

Chaque SWF passe par `swfsvg` (mode --scene ou nom d'export), puis cairosvg à l'échelle de la
famille ; les pixels magenta purs (emplacements remplis à l'exécution) deviennent transparents
comme dans exporter_png.py, puis le PNG est réduit à une palette de 256 couleurs quand l'écart
moyen reste invisible (voir `optimiser`). Le client affiche ces SWF dans un `Loader` qui ajuste
le contenu à son cadre (getBounds) : le recadrage sur le contenu fait par swfsvg est donc celui
du client.

Calques : le client recolore une partie des clips à l'exécution (`Color.setRGB`). Pour que le bot
puisse le refaire, certains rendus sont aussi produits sans une instance nommée, ou avec elle
seule, en réécrivant une copie du SWF (les placements de cette profondeur sont retirés) ; tous les
calques d'un même rendu sont posés dans le même cadre (union des cadres)."""
import argparse
import base64
import concurrent.futures
import copy
import datetime
import io
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zlib

try:
    import cairosvg
    from PIL import Image, ImageChops, ImageStat
except ImportError as error:  # pragma: no cover - message d'installation
    sys.exit("exporter_icons.py : %s (pip install cairosvg Pillow)" % error)

# ---------------------------------------------------------------------------------------------
# Lecture et réécriture minimale d'un SWF (en-tête, balises, DefineSprite).

PLACE_OBJECT2, PLACE_OBJECT3, REMOVE_OBJECT, REMOVE_OBJECT2, DEFINE_SPRITE, EXPORT_ASSETS = 26, 70, 5, 28, 39, 56


class SwfInvalide(Exception):
    pass


class SymboleVide(Exception):
    """Le symbole ou la scène ne contient aucun dessin (DefineSprite sans placement, cadre nul)."""


def lire_swf(donnees):
    """Renvoie (version, octets rect+cadence+images, balises) ; balise = [code, octets]."""
    if len(donnees) < 8 or donnees[:3] not in (b"FWS", b"CWS"):
        raise SwfInvalide("en-tête SWF absent")
    version = donnees[3]
    corps = donnees[8:]
    if donnees[:3] == b"CWS":
        try:
            corps = zlib.decompress(corps)
        except zlib.error as error:
            raise SwfInvalide("compression illisible (%s)" % error)
    if not corps:
        raise SwfInvalide("SWF vide")
    nbits = corps[0] >> 3
    debut = (5 + 4 * nbits + 7) // 8 + 4
    if len(corps) < debut:
        raise SwfInvalide("en-tête tronqué")
    return version, corps[:debut], lire_balises(corps, debut, len(corps))


def lire_balises(octets, pos, fin):
    balises = []
    while pos + 2 <= fin:
        entete = struct.unpack_from("<H", octets, pos)[0]
        code, longueur = entete >> 6, entete & 0x3F
        pos += 2
        if longueur == 0x3F:
            if pos + 4 > fin:
                raise SwfInvalide("balise tronquée")
            longueur = struct.unpack_from("<I", octets, pos)[0]
            pos += 4
        if pos + longueur > fin:
            raise SwfInvalide("balise %d tronquée" % code)
        balises.append([code, octets[pos:pos + longueur]])
        pos += longueur
        if code == 0:
            break
    return balises


def ecrire_balises(balises):
    sortie = bytearray()
    for code, octets in balises:
        # PlaceObject et DefineBits* gardent l'en-tête long s'ils l'avaient ; l'en-tête long est
        # toujours accepté, on l'utilise dès 63 octets.
        if len(octets) < 0x3F:
            sortie += struct.pack("<H", (code << 6) | len(octets))
        else:
            sortie += struct.pack("<HI", (code << 6) | 0x3F, len(octets))
        sortie += octets
    return bytes(sortie)


def ecrire_swf(version, entete, balises):
    corps = entete + ecrire_balises(balises)
    return b"FWS" + bytes([version]) + struct.pack("<I", 8 + len(corps)) + corps


def placement(octets, code):
    """(profondeur, nom) d'un PlaceObject2/3, ou None si la balise est illisible."""
    try:
        drapeaux = octets[0]
        p = 1
        drapeaux3 = 0
        if code == PLACE_OBJECT3:
            drapeaux3 = octets[1]
            p = 2
        profondeur = struct.unpack_from("<H", octets, p)[0]
        p += 2
        if code == PLACE_OBJECT3 and (drapeaux3 & 0x08 or (drapeaux3 & 0x10 and drapeaux & 0x02)):
            p = octets.index(0, p) + 1  # nom de classe
        if drapeaux & 0x02:
            p += 2
        bits = p * 8

        def lire(n):
            nonlocal bits
            valeur = 0
            for _ in range(n):
                valeur = (valeur << 1) | ((octets[bits // 8] >> (7 - bits % 8)) & 1)
                bits += 1
            return valeur
        if drapeaux & 0x04:  # matrice
            if lire(1):
                n = lire(5); lire(n); lire(n)
            if lire(1):
                n = lire(5); lire(n); lire(n)
            n = lire(5); lire(n); lire(n)
            bits = (bits + 7) // 8 * 8
        if drapeaux & 0x08:  # transformation de couleur avec alpha
            ajout, mult, n = lire(1), lire(1), None
            n = lire(4)
            lire(n * 4 * (ajout + mult))
            bits = (bits + 7) // 8 * 8
        p = bits // 8
        if drapeaux & 0x10:
            p += 2
        nom = None
        if drapeaux & 0x20:
            fin = octets.index(0, p)
            nom = octets[p:fin].decode("latin-1")
        return profondeur, nom
    except (IndexError, ValueError, struct.error):
        return None


def profondeur_retiree(octets, code):
    if code == REMOVE_OBJECT2 and len(octets) >= 2:
        return struct.unpack_from("<H", octets, 0)[0]
    if code == REMOVE_OBJECT and len(octets) >= 4:
        return struct.unpack_from("<H", octets, 2)[0]
    return None


def filtrer_timeline(balises, noms, garder):
    """Retire (garder=False) ou garde seules (garder=True) les profondeurs des instances `noms`
    (nom d'instance, ou « #n » pour la profondeur n d'un placement sans nom).
    Renvoie (balises filtrées, nombre d'instances trouvées)."""
    profondeurs = set()
    for code, octets in balises:
        if code in (PLACE_OBJECT2, PLACE_OBJECT3):
            info = placement(octets, code)
            if info and (info[1] in noms or "#%d" % info[0] in noms):
                profondeurs.add(info[0])
    resultat = []
    for code, octets in balises:
        profondeur = None
        if code in (PLACE_OBJECT2, PLACE_OBJECT3):
            info = placement(octets, code)
            profondeur = info[0] if info else None
        elif code in (REMOVE_OBJECT, REMOVE_OBJECT2):
            profondeur = profondeur_retiree(octets, code)
        if profondeur is not None and (profondeur in profondeurs) != garder:
            continue
        resultat.append([code, octets])
    return resultat, len(profondeurs)


def variante_calque(donnees, noms, garder, sprite=None):
    """Copie du SWF dont la timeline principale (sprite=None) ou le DefineSprite `sprite` ne garde
    que les instances `noms` (garder=True) ou toutes sauf elles. Lève SwfInvalide si l'instance
    n'existe pas."""
    version, entete, balises = lire_swf(donnees)
    if sprite is None:
        balises, trouves = filtrer_timeline(balises, noms, garder)
    else:
        trouves = 0
        for balise in balises:
            if balise[0] == DEFINE_SPRITE and len(balise[1]) >= 4 and struct.unpack_from("<H", balise[1], 0)[0] == sprite:
                interne = lire_balises(balise[1], 4, len(balise[1]))
                interne, trouves = filtrer_timeline(interne, noms, garder)
                balise[1] = balise[1][:4] + ecrire_balises(interne)
    if not trouves:
        raise SwfInvalide("instance %s absente" % ", ".join(sorted(noms)))
    return ecrire_swf(version, entete, balises)


def exports(donnees):
    """{nom: id} des symboles d'ExportAssets."""
    _, _, balises = lire_swf(donnees)
    resultat = {}
    for code, octets in balises:
        if code != EXPORT_ASSETS or len(octets) < 2:
            continue
        try:
            nombre, p = struct.unpack_from("<H", octets, 0)[0], 2
            for _ in range(nombre):
                ident = struct.unpack_from("<H", octets, p)[0]
                fin = octets.index(0, p + 2)
                resultat[octets[p + 2:fin].decode("latin-1")] = ident
                p = fin + 1
        except (struct.error, ValueError):
            raise SwfInvalide("ExportAssets tronqué")
    return resultat


def exports_ou_rien(chemin):
    """Exports d'un SWF du client, ou {} (avec un message) s'il est illisible : la série continue."""
    try:
        with open(chemin, "rb") as f:
            return exports(f.read())
    except (OSError, SwfInvalide) as error:
        print("illisible : %s (%s)" % (chemin, error), flush=True)
        return {}


# ---------------------------------------------------------------------------------------------
# Rendu : swfsvg -> SVG -> PNG (cairosvg), magenta effacé, calques dans un même cadre, palette.

class Rendu:
    """Un PNG RGBA et l'origine de son pixel (0, 0) dans le repère du symbole, à l'échelle 1."""

    def __init__(self, image, x0, y0, echelle, avertissements):
        self.image, self.x0, self.y0, self.echelle, self.avertissements = image, x0, y0, echelle, avertissements


def lire_index(dossier):
    lignes = []
    chemin = os.path.join(dossier, "index.tsv")
    if not os.path.exists(chemin):
        return lignes
    with open(chemin, encoding="utf-8") as f:
        for ligne in f:
            c = ligne.rstrip("\n").split("\t")
            if len(c) >= 12:
                lignes.append({"nom": c[0], "xmin": float(c[2]), "ymin": float(c[3]), "largeur": float(c[4]),
                               "hauteur": float(c[5]), "avertissements": c[6], "xmax": float(c[7]), "ymax": float(c[8]),
                               "fichier": c[11]})
    return lignes


def swfsvg(binaire, swf, dossier, scene=False, noms=(), image=None):
    os.makedirs(dossier, exist_ok=True)
    # Un swfsvg écrit en Python (faux swfsvg des tests) est lancé par cet interpréteur.
    lanceur = [sys.executable, binaire] if binaire.endswith(".py") else [binaire]
    commande = lanceur + (["--scene", "--name", "scene"] if scene else []) + (["--frame", str(image)] if image else []) + [swf, dossier] + list(noms)
    sortie = subprocess.run(commande, capture_output=True, text=True, timeout=300)
    if sortie.returncode != 0:
        raise RuntimeError("swfsvg %s : %s" % (os.path.basename(swf), (sortie.stderr or "").strip()[:300]))
    return lire_index(dossier), (sortie.stderr or "").strip()


def effacer_magenta(image):
    """Rend transparents les pixels magenta purs restants (même seuil qu'exporter_png.py)."""
    r, g, b, a = image.split()
    masque = ImageChops.multiply(ImageChops.multiply(r.point(lambda v: 255 if v > 235 else 0),
                                                     g.point(lambda v: 255 if v < 25 else 0)),
                                 b.point(lambda v: 255 if v > 235 else 0))
    masque = ImageChops.multiply(masque, a.point(lambda v: 255 if v else 0))
    if masque.getbbox() is None:
        return image
    image.putalpha(ImageChops.subtract(a, masque))
    return image


# ---------------------------------------------------------------------------------------------
# Transformations de couleur : swfsvg les écrit en filtres `feColorMatrix`, que cairosvg ignore.
# On les applique directement aux couleurs (aplats, traits, dégradés, bitmaps) avant le rendu.

SVG_NS = "http://www.w3.org/2000/svg"
ET.register_namespace("", SVG_NS)
_URL = re.compile(r"^url\(#([^)]+)\)$")


def _matrice(filtre):
    """Valeurs (20) du feColorMatrix d'un filtre, ou None si le filtre n'est pas une matrice simple."""
    if filtre is None or len(filtre) != 1 or not filtre[0].tag.endswith("feColorMatrix") or filtre[0].get("type", "matrix") != "matrix":
        return None
    try:
        valeurs = [float(v) for v in filtre[0].get("values", "").replace(",", " ").split()]
    except ValueError:
        return None
    return valeurs if len(valeurs) == 20 else None


def _appliquer(pile, r, g, b, a):
    """Applique les matrices de la plus intérieure à la plus extérieure ; composantes 0..1."""
    for m in pile:
        r, g, b, a = (min(1.0, max(0.0, m[5 * i] * r + m[5 * i + 1] * g + m[5 * i + 2] * b + m[5 * i + 3] * a + m[5 * i + 4])) for i in range(4))
    return r, g, b, a


def _couleur(hexa, opacite, pile):
    hexa = hexa.lstrip("#")
    if len(hexa) == 3:
        hexa = "".join(c * 2 for c in hexa)
    r, g, b = (int(hexa[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    r, g, b, a = _appliquer(pile, r, g, b, opacite)
    return "#%02x%02x%02x" % tuple(int(round(v * 255)) for v in (r, g, b)), a


def _image(href, pile):
    if not href.startswith("data:image/png;base64,"):
        return href
    image = Image.open(io.BytesIO(base64.b64decode(href.split(",", 1)[1]))).convert("RGBA")
    for m in pile:
        diagonale = all(m[5 * i + j] == 0 for i in range(4) for j in range(4) if i != j)
        if diagonale:
            bandes = [bande.point(lambda v, mul=m[5 * i + i], add=m[5 * i + 4]: int(round(min(1.0, max(0.0, mul * v / 255.0 + add)) * 255)))
                      for i, bande in enumerate(image.split())]
            image = Image.merge("RGBA", bandes)
        else:  # matrice pleine : swfsvg n'en écrit pas, repli pixel par pixel
            image.putdata([tuple(int(round(v * 255)) for v in _appliquer([m], *(c / 255.0 for c in px))) for px in image.getdata()])
    sortie = io.BytesIO()
    image.save(sortie, format="PNG")
    return "data:image/png;base64," + base64.b64encode(sortie.getvalue()).decode("ascii")


def appliquer_transformations(svg):
    """Renvoie le SVG dont les filtres feColorMatrix de swfsvg sont appliqués aux couleurs mêmes."""
    if "feColorMatrix" not in svg:
        return svg
    racine = ET.fromstring(svg)
    defs = racine.find("{%s}defs" % SVG_NS)
    if defs is None:
        return svg
    par_id = {e.get("id"): e for e in racine.iter() if e.get("id")}
    copies = {}

    def reference(valeur, pile):
        m = _URL.match(valeur or "")
        if not m or m.group(1) not in par_id:
            return valeur
        cle = (m.group(1), tuple(tuple(x) for x in pile))
        if cle not in copies:
            copie = copy.deepcopy(par_id[m.group(1)])
            copie.set("id", "%s_c%d" % (m.group(1), len(copies) + 1))
            for e in copie.iter():
                if e.get("stop-color"):
                    couleur, a = _couleur(e.get("stop-color"), float(e.get("stop-opacity", "1")), pile)
                    e.set("stop-color", couleur)
                    e.set("stop-opacity", "%g" % round(a, 4))
                if e.tag.endswith("image") and e.get("href"):
                    e.set("href", _image(e.get("href"), pile))
            defs.append(copie)
            copies[cle] = copie.get("id")
        return "url(#%s)" % copies[cle]

    def parcourir(element, pile):
        filtre = element.get("filter")
        m = _URL.match(filtre or "")
        if m:
            matrice = _matrice(par_id.get(m.group(1)))
            if matrice is not None:
                pile = [matrice] + pile
                del element.attrib["filter"]
        if pile:
            for attribut, opacite in (("fill", "fill-opacity"), ("stroke", "stroke-opacity")):
                valeur = element.get(attribut)
                if valeur and valeur.startswith("#"):
                    couleur, a = _couleur(valeur, float(element.get(opacite, "1")), pile)
                    element.set(attribut, couleur)
                    element.set(opacite, "%g" % round(a, 4))
                elif valeur and valeur.startswith("url("):
                    element.set(attribut, reference(valeur, pile))
            if element.tag.endswith("image") and element.get("href"):
                element.set("href", _image(element.get("href"), pile))
        for enfant in list(element):
            if enfant is not defs:
                parcourir(enfant, pile)

    parcourir(racine, [])
    return ET.tostring(racine, encoding="unicode")


def svg_en_png(chemin, echelle):
    with open(chemin, encoding="utf-8") as f:
        svg = appliquer_transformations(f.read()).encode("utf-8")
    try:
        image = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, scale=echelle, background_color=None)))
        image.load()
        image = image.convert("RGBA")
    except MemoryError:
        # cairo refuse (CAIRO_STATUS_NO_MEMORY) un dégradé devenu presque ponctuel une fois réduit
        # (portrait 8033) : rendu à l'échelle 1, puis réduction par Pillow en alpha prémultiplié.
        if echelle >= 1:
            raise
        grand = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, scale=1, background_color=None)))
        grand.load()
        taille = (max(1, int(round(grand.width * echelle))), max(1, int(round(grand.height * echelle))))
        image = grand.convert("RGBA").convert("RGBa").resize(taille, Image.Resampling.LANCZOS).convert("RGBA")
    return effacer_magenta(image)


def echelle_pour(ligne, echelle, maximum):
    if maximum:
        cote = max(ligne["largeur"], ligne["hauteur"])
        if cote * echelle > maximum:
            return maximum / cote
    return echelle


def rendre(binaire, swf, travail, echelle, maximum=None, symbole=None, donnees=None, etiquette="r", image=None):
    """Rend la scène (symbole=None, à l'image `image` si elle est donnée) ou un symbole exporté ; renvoie un Rendu,
    None si le rendu est entièrement transparent, et lève SymboleVide si le SWF n'y dessine rien."""
    dossier = tempfile.mkdtemp(prefix=etiquette + "-", dir=travail)
    try:
        source = swf
        if donnees is not None:
            source = os.path.join(dossier, "variante.swf")
            with open(source, "wb") as f:
                f.write(donnees)
        lignes, _ = swfsvg(binaire, source, os.path.join(dossier, "svg"), scene=symbole is None,
                           noms=() if symbole is None else (symbole,), image=image)
        if not lignes:
            raise SwfInvalide("symbole %s absent" % symbole if symbole else "scène illisible")
        ligne = lignes[0]
        if ligne["largeur"] <= 0 or ligne["hauteur"] <= 0:
            raise SymboleVide()
        e = echelle_pour(ligne, echelle, maximum)
        image = svg_en_png(os.path.join(dossier, "svg", ligne["fichier"]), e)
        if image.getchannel("A").getbbox() is None:
            return None  # rien de visible (composant dessiné par le code du client, Loader vide…)
        return Rendu(image, int(ligne["xmin"] // 1), int(ligne["ymin"] // 1), e, ligne["avertissements"])
    finally:
        shutil.rmtree(dossier, ignore_errors=True)


def rendre_calque(*args, **kwargs):
    """Comme rendre, mais un calque sans dessin donne None (image vide dans le cadre commun)."""
    try:
        return rendre(*args, **kwargs)
    except SymboleVide:
        return None


def meme_cadre(rendus):
    """Pose des rendus de même échelle dans le cadre union ; renvoie (images, x0, y0)."""
    presents = [r for r in rendus if r is not None]
    e = presents[0].echelle
    x0 = min(r.x0 for r in presents)
    y0 = min(r.y0 for r in presents)
    x1 = max(r.x0 * e + r.image.width for r in presents)
    y1 = max(r.y0 * e + r.image.height for r in presents)
    largeur, hauteur = int(round(x1 - x0 * e)), int(round(y1 - y0 * e))
    images = []
    for r in rendus:
        toile = Image.new("RGBA", (largeur, hauteur), (0, 0, 0, 0))
        if r is not None:
            toile.paste(r.image, (int(round((r.x0 - x0) * e)), int(round((r.y0 - y0) * e))))
        images.append(toile)
    return images, x0, y0


def optimiser(image, seuil=4.5):
    """PNG le plus léger sans écart visible : RGB si opaque, palette de 256 couleurs si l'écart
    moyen par canal, mesuré sur les pixels visibles, reste sous `seuil` (sur 255)."""
    image = image.convert("RGBA")
    alpha = image.getchannel("A")
    if alpha.getbbox() is None:
        return image
    # Les pixels totalement transparents n'ont pas de couleur utile : noir transparent partout.
    vide = alpha.point(lambda v: 255 if v == 0 else 0)
    if vide.getbbox() is not None:
        image = Image.composite(Image.new("RGBA", image.size, (0, 0, 0, 0)), image, vide)
    opaque = alpha.getextrema()[0] == 255
    candidat = image.convert("RGB") if opaque else image
    # Médiane pour les images opaques ; octree, seule méthode de Pillow qui garde l'alpha, sinon.
    methode = Image.Quantize.MEDIANCUT if opaque else Image.Quantize.FASTOCTREE
    palette = candidat.quantize(colors=256, method=methode, dither=Image.Dither.NONE)
    ecart = ImageChops.difference(image, palette.convert("RGBA"))
    visibles = alpha.point(lambda v: 255 if v else 0)
    if max(ImageStat.Stat(ecart, mask=visibles).mean) <= seuil:
        return palette
    return candidat


def enregistrer(image, chemin):
    os.makedirs(os.path.dirname(chemin), exist_ok=True)
    temporaire = chemin + ".tmp"
    optimiser(image).save(temporaire, format="PNG", optimize=True)
    os.replace(temporaire, chemin)
    return os.path.getsize(chemin)


# ---------------------------------------------------------------------------------------------
# Familles.

# `CircleChrono`, `Clock`, `Compass` et `Emblem` ne contiennent qu'un cadre invisible : le client les
# dessine par le code à partir des symboles ci-dessous (aiguilles, fonds) ou des SWF d'emblèmes.
# `CircleChrono` y attache deux `CircleChronoHalfDefault`, demi-disques magenta (aplat recoloré par la
# couleur de style `bgcolor`) que le code masque et tourne : swfsvg omet le magenta, il n'y a rien à
# exporter, le bot dessine un secteur de la couleur voulue.
# `UI_MainMenu` et `UI_WaypointItemLocate` sont déjà dans Client/ (onglet-menu.png, zaap.png).
SYMBOLES_UI = [
    # Bandeau (jauges, boutons ronds, œil des combats, menu principal, canaux)
    "Heart", "PointsViewerAP", "PointsViewerMP", "ButtonBannerRoundUp", "ButtonBannerRoundDown",
    "Eye", "Eye2", "NoEye", "ButtonMainMenuUp", "ButtonMainMenuDown", "UI_BannerChatCommandAll",
    "UI_BannerClockBack", "UI_BannerClockArrowHours", "UI_BannerClockArrowMinutes",
    "UI_BannerCompassBack", "UI_BannerCompassArrow", "UI_BannerCompassNoArrow",
    "UI_MainMenuSubscribe", "UI_MainMenuOptions", "UI_MainMenuHelp", "UI_MainMenuCross", "UI_MainMenuBugs",
    # Chat
    "FilterIcon0", "FilterIcon1", "FilterIcon2", "FilterIcon3", "FilterIcon4", "FilterIcon5", "FilterIcon6", "FilterIcon7",
    "ButtonChatUp", "ButtonChatDown", "ButtonSitUp", "ButtonSitDown", "ButtonEmoteUp", "ButtonEmoteDown", "SmileysHighlight",
    # Carte, acteurs, combat
    "Star", "StarBorder", "UI_Party", "UI_Timeline", "TimelineItem", "TimelinePointer", "TimelineItemSummonedBg",
    # Combat (lot F12b) : options d'équipe, drapeau, menu prêt / annuler, ligne d'un résultat
    "UI_FightOptionBlockJoinerUp", "UI_FightOptionBlockJoinerDown",
    "UI_FightOptionBlockJoinerExceptPartyMemberUp", "UI_FightOptionBlockJoinerExceptPartyMemberDown",
    "UI_FightOptionBlockSpectatorUp", "UI_FightOptionBlockSpectatorDown", "UI_FightOptionNeedHelpUp", "UI_FightOptionNeedHelpDown",
    "UI_FightOptionButtonCell", "UI_FightOptionTacticModeUp", "UI_FightOptionTacticModeDown", "UI_ChallengeMenu", "UI_GameResultPlayer",
]
# Scènes de clips/ rendues à une image donnée (symbole, SWF relatif au client, image, échelle) : le drapeau de combat
# (`Gf`, `flag.swf` joué par `spriteLaunchVisualEffect`) est pris à l'image 30, flèche posée au-dessus du losange.
SCENES_UI = [
    ("FlagCell", "clips/flag.swf", 30, 1),
]
# Calques : (symbole, suffixe, instance, garder) ; le rendu complet garde le nom du symbole.
CALQUES_UI = [
    ("StarBorder", "_fill", "fill", True),        # étoile du groupe de monstres : partie teintée (STARS_COLORS)
    ("StarBorder", "_contour", "fill", False),    # bordure seule
    ("Heart", "_vide", "_mcRectangle", False),    # cœur sans le rectangle rouge des points de vie
    ("TimelineItem", "_fond", "_mcHealth", False),  # case de la ligne de temps sans la barre de vie (teintée TEAMS_COLOR)
    ("TimelineItem", "_vie", "_mcHealth", True),    # la barre de vie seule, dans le même cadre
    ("UI_ChallengeMenu", "_fond", "_mcTick", False),  # menu prêt / annuler sans la coche
    ("UI_ChallengeMenu", "_coche", "_mcTick", True),  # la coche seule
    ("UI_GameResultPlayer", "_mort", "_mcDeadHead", True),  # tête de mort d'un combattant vaincu
]


def fichiers(dossier, motif=r"^(-?\d+)\.swf$"):
    if not os.path.isdir(dossier):
        return []
    trouves = []
    for nom in os.listdir(dossier):
        m = re.match(motif, nom)
        if m:
            trouves.append((int(m.group(1)), os.path.join(dossier, nom)))
    return [chemin for _, chemin in sorted(trouves)]


def swf_ignores(dossier, motif=r"^(-?\d+)\.swf$", sauf=()):
    """SWF d'un dossier dont le nom ne suit pas `motif` : le client compose ses chemins avec un
    identifiant numérique et ne les demande pas ainsi ; ils sont listés dans PROVENANCE.md."""
    if not os.path.isdir(dossier):
        return []
    return sorted(os.path.join(dossier, nom) for nom in os.listdir(dossier)
                  if nom.lower().endswith(".swf") and not re.match(motif, nom) and nom not in sauf)


def noter_ignores(ignores, famille, dossier, **options):
    if ignores is not None:
        ignores.setdefault(famille, []).extend(swf_ignores(dossier, **options))


def taches_simples(client, famille, sous_dossier, sortie, echelle, maximum, ignores=None):
    taches = []
    noter_ignores(ignores, famille, os.path.join(client, sous_dossier))
    for chemin in fichiers(os.path.join(client, sous_dossier)):
        nom = os.path.splitext(os.path.basename(chemin))[0]
        taches.append({"famille": famille, "type": "scene", "swf": chemin, "sortie": sortie + nom,
                       "echelle": echelle, "max": maximum})
    return taches


def lister_taches(client, familles, sortie_racine, remplacer, ignores=None):
    """Tâches de rendu des familles demandées ; `ignores` (dict facultatif) reçoit par famille les SWF
    au nom non numérique que la série laisse de côté."""
    clips = os.path.join(client, "clips")
    taches = []
    if "Smileys" in familles:
        taches += taches_simples(clips, "Smileys", "smileys", "Smileys/", 2, 48, ignores)
    if "Emotes" in familles:
        taches += taches_simples(clips, "Emotes", "emotes", "Emotes/", 2, 48, ignores)
    if "Jobs" in familles:
        taches += taches_simples(clips, "Jobs", "jobs", "Jobs/", 2, 64, ignores)
    if "Alignments" in familles:
        for sous in ("", "mini/", "orders/", "feats/"):
            taches += taches_simples(clips, "Alignments", "alignments/" + sous, "Alignments/" + sous, 2, 64, ignores)
    if "Emblems" in familles:
        noter_ignores(ignores, "Emblems", os.path.join(clips, "emblems", "back"))
        noter_ignores(ignores, "Emblems", os.path.join(clips, "emblems", "up"))
        for chemin in fichiers(os.path.join(clips, "emblems", "back")):
            nom = os.path.splitext(os.path.basename(chemin))[0]
            taches.append({"famille": "Emblems", "type": "embleme-fond", "swf": chemin, "sortie": "Emblems/back/" + nom, "echelle": 2})
        for chemin in fichiers(os.path.join(clips, "emblems", "up")):
            nom = os.path.splitext(os.path.basename(chemin))[0]
            taches.append({"famille": "Emblems", "type": "scene", "swf": chemin, "sortie": "Emblems/up/" + nom, "echelle": 2, "max": 100})
    if "Portraits" in familles:
        taches += taches_simples(clips, "Portraits", os.path.join("artworks", "big"), "Portraits/", 1, 320, ignores)
    if "Items" in familles:
        racine = os.path.join(clips, "items")
        types = sorted((int(n), n) for n in os.listdir(racine) if n.isdigit()) if os.path.isdir(racine) else []
        for _, type_objet in types:
            taches += taches_simples(racine, "Items", type_objet, "Items/%s/" % type_objet, 2, 80, ignores)
    if "Spells" in familles:
        noter_ignores(ignores, "Spells", os.path.join(clips, "spells", "icons"))
        for chemin in fichiers(os.path.join(clips, "spells", "icons")):
            nom = os.path.splitext(os.path.basename(chemin))[0]
            if not remplacer and os.path.exists(os.path.join(sortie_racine, "sorts", nom + ".png")):
                continue
            taches.append({"famille": "Spells", "type": "scene", "swf": chemin, "sortie": "sorts/" + nom, "echelle": 2, "max": 80})
    if "WorldMap" in familles:
        noter_ignores(ignores, "WorldMap", os.path.join(clips, "maps"), sauf=("hints.swf", "dungeon.swf"))
        for chemin in fichiers(os.path.join(clips, "maps")):
            zone = os.path.splitext(os.path.basename(chemin))[0]
            noms = exports_ou_rien(chemin)
            for nom in sorted(n for n in noms if re.match(r"^-?\d+_-?\d+$", n)):
                taches.append({"famille": "WorldMap", "type": "symbole", "swf": chemin, "symbole": nom,
                               "sortie": "WorldMap/%s/%s" % (zone, nom), "echelle": 1, "tuile": (zone, "tuiles.tsv")})
            # Contours des sous-zones (`subarea_<id>`), posés par le client à l'origine de la carte et
            # recolorés par Color.setRGB (MapNavigator.addSubareaClip) : masques à teinter.
            for nom in sorted((n for n in noms if re.match(r"^subarea_\d+$", n)), key=lambda n: int(n[8:])):
                taches.append({"famille": "WorldMap", "type": "symbole", "swf": chemin, "symbole": nom,
                               "sortie": "WorldMap/%s/sous-zones/%s" % (zone, nom[8:]), "echelle": 1, "tuile": (zone, "sous-zones.tsv")})
        indices = os.path.join(clips, "maps", "hints.swf")
        if os.path.exists(indices):
            for nom in sorted(exports_ou_rien(indices), key=lambda n: (len(n), n)):
                taches.append({"famille": "WorldMap", "type": "symbole", "swf": indices, "symbole": nom,
                               "sortie": "WorldMap/hints/" + nom, "echelle": 2, "max": 48})
        donjon = os.path.join(clips, "maps", "dungeon.swf")
        if os.path.exists(donjon):
            taches.append({"famille": "WorldMap", "type": "scene", "swf": donjon, "sortie": "WorldMap/dungeon", "echelle": 1})
    if "UI" in familles:
        core = os.path.join(client, "modules", "core.swf")
        if os.path.exists(core):
            for symbole in SYMBOLES_UI:
                taches.append({"famille": "UI", "type": "symbole-calques" if any(c[0] == symbole for c in CALQUES_UI) else "symbole",
                               "swf": core, "symbole": symbole, "sortie": "Client/" + symbole, "echelle": 2})
        for nom, relatif, image, echelle in SCENES_UI:
            chemin = os.path.join(client, *relatif.split("/"))
            if os.path.exists(chemin):
                taches.append({"famille": "UI", "type": "scene", "swf": chemin, "sortie": "Client/" + nom, "echelle": echelle, "image": image})
    return taches


def executer(tache, binaire, sortie_racine, travail):
    """Rend une tâche ; renvoie un dict (fichiers écrits, tailles, cadre, avertissement) sans lever."""
    resultat = {"tache": tache, "ecrits": [], "octets": 0, "erreur": None, "vide": False, "avertissements": "", "cadre": None}
    try:
        cible = lambda suffixe="": os.path.join(sortie_racine, *(tache["sortie"] + suffixe + ".png").split("/"))
        if tache["type"] == "scene":
            rendu = rendre(binaire, tache["swf"], travail, tache["echelle"], tache.get("max"), etiquette=tache["famille"], image=tache.get("image"))
            if rendu is None:
                resultat["erreur"] = "rendu vide"
                return resultat
            resultat["octets"] += enregistrer(rendu.image, cible())
            resultat["ecrits"].append(tache["sortie"])
            resultat["avertissements"] = rendu.avertissements
        elif tache["type"] == "symbole":
            rendu = rendre(binaire, tache["swf"], travail, tache["echelle"], tache.get("max"), symbole=tache["symbole"], etiquette=tache["famille"])
            if rendu is None:
                resultat["erreur"] = "rendu vide"
                return resultat
            resultat["octets"] += enregistrer(rendu.image, cible())
            resultat["ecrits"].append(tache["sortie"])
            resultat["avertissements"] = rendu.avertissements
            resultat["cadre"] = (rendu.x0 * rendu.echelle, rendu.y0 * rendu.echelle, rendu.image.width, rendu.image.height)
        elif tache["type"] == "embleme-fond":
            with open(tache["swf"], "rb") as f:
                donnees = f.read()
            complet = rendre(binaire, tache["swf"], travail, tache["echelle"], etiquette="embleme")
            if complet is None:
                resultat["erreur"] = "rendu vide"
                return resultat
            fond = rendre_calque(binaire, tache["swf"], travail, tache["echelle"], donnees=variante_calque(donnees, {"back"}, True), etiquette="embleme")
            contour = rendre_calque(binaire, tache["swf"], travail, tache["echelle"], donnees=variante_calque(donnees, {"back"}, False), etiquette="embleme")
            images, _, _ = meme_cadre([complet, fond, contour])
            resultat["octets"] += enregistrer(images[1], cible())
            resultat["octets"] += enregistrer(images[2], cible("_contour"))
            resultat["ecrits"] += [tache["sortie"], tache["sortie"] + "_contour"]
        elif tache["type"] == "symbole-calques":
            with open(tache["swf"], "rb") as f:
                donnees = f.read()
            ident = exports(donnees)[tache["symbole"]]
            calques = [c for c in CALQUES_UI if c[0] == tache["symbole"]]
            rendus = [rendre(binaire, tache["swf"], travail, tache["echelle"], symbole=tache["symbole"], etiquette="ui")]
            if rendus[0] is None:
                resultat["erreur"] = "rendu vide"
                return resultat
            for _, _, instance, garder in calques:
                rendus.append(rendre_calque(binaire, tache["swf"], travail, tache["echelle"], symbole=tache["symbole"],
                                     donnees=variante_calque(donnees, {instance}, garder, sprite=ident), etiquette="ui"))
            images, _, _ = meme_cadre(rendus)
            resultat["octets"] += enregistrer(images[0], cible())
            resultat["ecrits"].append(tache["sortie"])
            for image, (_, suffixe, _, _) in zip(images[1:], calques):
                resultat["octets"] += enregistrer(image, cible(suffixe))
                resultat["ecrits"].append(tache["sortie"] + suffixe)
            resultat["avertissements"] = rendus[0].avertissements if rendus[0] else ""
    except SymboleVide:  # sprite sans dessin dans le SWF même (ex. 191 `subarea_*` de 0.swf)
        resultat["vide"] = True
    except Exception as error:  # un SWF illisible ne doit pas arrêter la série
        resultat["erreur"] = "%s: %s" % (type(error).__name__, error)
    return resultat


# ---------------------------------------------------------------------------------------------
# PROVENANCE.md de chaque famille.

SOURCES = {
    "Smileys": ("clips/smileys/<n>.swf", "Smileys/<n>.png", "Smileys du chat (`BS<n>`, `cS<id>|<n>`) : 1 à 15 dans le panneau « Smileys » (`SMILEYS_ICONS_PATH + n`), 91 à 99 pour les jauges de la monture. "
                "`all.swf`, qui regroupe les mêmes smileys pour le mode « streaming » du client (`SMILEYS_ICONS_PATH + \"all.swf\"`), n'est pas exporté.",
                "--scene, échelle 2, côté le plus long limité à 48 px"),
    "Emotes": ("clips/emotes/<n>.swf", "Emotes/<n>.png", "Icônes des attitudes (`eU<n>`, onglet attitudes du panneau « Smileys », `EMOTES_ICONS_PATH + n`).",
               "--scene, échelle 2, côté le plus long limité à 48 px"),
    "Jobs": ("clips/jobs/<g>.swf", "Jobs/<g>.png", "Icônes des métiers (`JOBS_ICONS_PATH + J[id].g`).", "--scene, échelle 2, côté le plus long limité à 64 px"),
    "Alignments": ("clips/alignments/{,mini/,orders/,feats/}<n>.swf", "Alignments/{,mini/,orders/,feats/}<n>.png",
                   "Alignements (`ALIGNMENTS_PATH`), petites icônes à côté des noms (`ALIGNMENTS_MINI_PATH + alignement`), ordres et dons (`ORDERS_PATH`, `FEATS_PATH`).",
                   "--scene, échelle 2, côté le plus long limité à 64 px"),
    "Emblems": ("clips/emblems/back/<n>.swf, clips/emblems/up/<n>.swf", "Emblems/back/<n>.png + Emblems/back/<n>_contour.png, Emblems/up/<n>.png",
                "Emblèmes de guilde (`Emblem` du client) : le fond `back/<n>` et le motif `up/<n>` sont recolorés à l'exécution par `Color.setRGB`. "
                "`back/<n>.png` est l'instance `back` seule (la partie recolorée par la couleur du fond), `back/<n>_contour.png` tout le reste du fond, dans le même cadre ; "
                "`up/<n>.png` est recoloré en entier par la couleur du motif. `ClientAssets.Emblem` compose les trois comme le composant `Emblem` de `core.swf` "
                "(fond ajusté à 78 × 78 en (1, 1), motif à 50 × 50 en (15, 15), cadre de 80 × 80).",
                "--scene ; calques obtenus en retirant ou en gardant seule l'instance `back` d'une copie du SWF ; échelle 2"),
    "Portraits": ("clips/artworks/big/<gfx>.swf", "Portraits/<gfx>.png", "Portraits des PNJ et des monstres dans la fenêtre de dialogue (`ARTWORKS_BIG_PATH + gfx` ou `customArtwork`).",
                  "--scene, échelle 1, côté le plus long limité à 320 px"),
    "Items": ("clips/items/<type>/<gfx>.swf", "Items/<type>/<gfx>.png", "Icônes d'objets (inventaire, boutique, échanges) : chemin `ITEMS_PATH + type + \"/\" + gfx` du client ; "
              "`type` et `gfx` d'un modèle d'objet viennent de `items_fr` (`I.u[id].t`, `I.u[id].g`).", "--scene, échelle 2, côté le plus long limité à 80 px"),
    "WorldMap": ("clips/maps/<zone>.swf (exports x_y et subarea_<id>), clips/maps/hints.swf, clips/maps/dungeon.swf",
                 "WorldMap/<zone>/<x_y>.png + tuiles.tsv, WorldMap/<zone>/sous-zones/<id>.png + sous-zones.tsv, WorldMap/hints/<id>.png, WorldMap/dungeon.png",
                 "Carte du monde (`MapExplorer`, zones 0, 2 et 3 du client) : la tuile `x_y` couvre 15 × 15 cartes de 40 × 23 pixels et se pose en "
                 "(x × 600, y × 345) au zoom 100 ; `tuiles.tsv` donne pour chaque tuile non vide le décalage de son PNG dans cette case et sa taille "
                 "(`nom`, `x`, `y`, `largeur`, `hauteur`). Les sous-zones (`subarea_<id>`) sont les contours que le client pose à l'origine de la carte "
                 "et recolore par `Color.setRGB` pour montrer la sous-zone choisie : `sous-zones.tsv` donne leur position dans le même repère. "
                 "Les icônes d'indices de `hints.swf` sont nommées par leur gfx (`HI[id].g` de `hints_fr`).",
                 "symboles exportés, échelle 1 pour les tuiles et le parchemin, 2 pour les indices"),
    "Spells": ("clips/spells/icons/<id>.swf", "sorts/<id>.png",
               "Icônes de sorts (barre de sorts, fiche de sort). Le dossier contenait déjà 512 icônes de 80 × 80, versionnées sans provenance ; "
               "l'outil n'exporte que les icônes absentes (sauf `--remplacer`), au même format : un rendu de ces mêmes SWF par l'outil "
               "diffère des 512 existantes de moins de 1,3/255 en moyenne par canal (vérifié sur 0, 1, 100 et 161).",
               "--scene, échelle 2, côté le plus long limité à 80 px"),
}


def ecrire_provenance(sortie_racine, famille, resultats, commande, ignores=()):
    source, cible, usage, mode = SOURCES[famille]
    dossier = os.path.join(sortie_racine, cible.split("/")[0])
    ecrits = [r for r in resultats if r["tache"]["famille"] == famille and r["ecrits"]]
    erreurs = [r for r in resultats if r["tache"]["famille"] == famille and r["erreur"]]
    vides = [r for r in resultats if r["tache"]["famille"] == famille and r["vide"]]
    octets = sum(r["octets"] for r in ecrits)
    lignes = [
        "# %s : images du client utilisées par le bot" % famille, "",
        "Source : `%s` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). "
        "Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont." % source, "",
        "Usage : %s" % usage, "",
        "Fichiers : `%s` — %d PNG (%d fichiers écrits), %.1f Mo. Copiés à côté de l'exécutable dans `ressources/Bot/%s` par la cible "
        "`CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get(\"%s\", nom)`, nom relatif sans `.png`), "
        "qui renvoie `null` si un fichier manque ou est illisible."
        % (cible, len(ecrits), sum(len(r["ecrits"]) for r in ecrits), octets / 1e6, cible.split("/")[0], famille), "",
        "Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` %s, cairosvg, Pillow) : rendu SVG par `swfsvg` (%s), PNG par cairosvg, "
        "magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles." % (VERSION_SWFSVG, mode), "",
        "Commande exacte (depuis la racine du dépôt) :", "", "```sh", commande, "```", "",
        "Date : %s." % datetime.date.today().isoformat(),
    ]
    if erreurs:
        lignes += ["", "Non exportés (%d) :" % len(erreurs), ""]
        lignes += ["- `%s` : %s" % (os.path.relpath(r["tache"]["swf"], CLIENT_RACINE).replace(os.sep, "/") + (" " + r["tache"]["symbole"] if r["tache"].get("symbole") else ""), r["erreur"]) for r in erreurs[:60]]
        if len(erreurs) > 60:
            lignes.append("- … et %d autres" % (len(erreurs) - 60))
    if vides:
        # Groupés par SWF : « clips/maps/0.swf : subarea_0, subarea_7… » ou le SWF seul (scène vide).
        par_swf = {}
        for r in vides:
            par_swf.setdefault(os.path.relpath(r["tache"]["swf"], CLIENT_RACINE).replace(os.sep, "/"), []).append(r["tache"].get("symbole"))
        naturel = lambda n: [int(x) if x.isdigit() else x for x in re.split(r"(\d+)", n or "")]
        lignes += ["", "Sans dessin dans le SWF même (sprite ou scène vide), donc sans PNG (%d) :" % len(vides), ""]
        for swf in sorted(par_swf, key=naturel):
            symboles = sorted((n for n in par_swf[swf] if n), key=naturel)
            lignes.append("- `%s`%s" % (swf, " : " + ", ".join(symboles) if symboles else ""))
    if ignores:
        relatifs = sorted((os.path.relpath(c, CLIENT_RACINE).replace(os.sep, "/") for c in ignores),
                          key=lambda n: [int(x) if x.isdigit() else x for x in re.split(r"(\d+)", n)])
        lignes += ["", "SWF ignorés : nom non numérique, hors des chemins `<dossier>/<n>.swf` que compose le client (%d) :" % len(relatifs), ""]
        lignes += ["- `%s`" % n for n in relatifs]
    lignes += ["", "Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`."]
    if famille == "Spells" and ecrits:
        noms = sorted((os.path.basename(e) for r in ecrits for e in r["ecrits"]), key=lambda n: int(n) if n.lstrip("-").isdigit() else 0)
        lignes[lignes.index("Commande exacte (depuis la racine du dépôt) :"):0] = [
            "Icônes écrites par l'outil (%d) : %s." % (len(noms), ", ".join("`%s.png`" % n for n in noms)), ""]
    os.makedirs(dossier, exist_ok=True)
    chemin = os.path.join(dossier, "PROVENANCE.md")
    if famille == "Spells" and not ecrits and os.path.exists(chemin):
        return  # rien d'ajouté : la provenance des icônes déjà ajoutées reste valable
    with open(chemin, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lignes) + "\n")


def ecrire_tuiles(sortie_racine, resultats):
    """Écrit WorldMap/<zone>/tuiles.tsv et sous-zones.tsv : nom, coin haut-gauche du PNG dans le
    repère de la carte du monde (pixels, zoom 100) et taille."""
    par_fichier = {}
    for r in resultats:
        tache = r["tache"]
        if tache.get("tuile") is not None and r["cadre"]:
            nom = tache["symbole"][8:] if tache["symbole"].startswith("subarea_") else tache["symbole"]
            par_fichier.setdefault(tache["tuile"], []).append((nom, r["cadre"]))
    for (zone, fichier), lignes in par_fichier.items():
        chemin = os.path.join(sortie_racine, "WorldMap", zone, fichier)
        with open(chemin, "w", encoding="utf-8", newline="\n") as f:
            f.write("nom\tx\ty\tlargeur\thauteur\n")
            for nom, (x, y, largeur, hauteur) in sorted(lignes, key=lambda l: [int(v) for v in l[0].split("_")]):
                f.write("%s\t%d\t%d\t%d\t%d\n" % (nom, round(x), round(y), largeur, hauteur))


VERSION_SWFSVG = "?"
CLIENT_RACINE = "."
FAMILLES = ["Smileys", "Emotes", "Jobs", "Alignments", "Emblems", "Portraits", "Items", "Spells", "WorldMap", "UI"]


def main(argv=None):
    global VERSION_SWFSVG, CLIENT_RACINE
    parser = argparse.ArgumentParser(description="Exporte les icônes du client Dofus 1.34 pour le bot.")
    parser.add_argument("--client", required=True, help="dossier du client (contient clips/ et modules/)")
    parser.add_argument("--sortie", required=True, help="dossier Outil_Azur_complet/Resources/Bot")
    parser.add_argument("--swfsvg", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "swfsvg", "target", "release", "swfsvg"))
    parser.add_argument("--familles", default=",".join(FAMILLES))
    parser.add_argument("--travail", default=None, help="dossier temporaire (défaut : dossier système)")
    parser.add_argument("--processus", type=int, default=os.cpu_count() or 2)
    parser.add_argument("--limite", type=int, default=0, help="nombre maximal de SWF par famille (essais)")
    parser.add_argument("--remplacer", action="store_true", help="réécrit aussi les icônes de sorts déjà présentes")
    options = parser.parse_args(argv)
    familles = [f.strip() for f in options.familles.split(",") if f.strip()]
    inconnues = [f for f in familles if f not in FAMILLES]
    if inconnues:
        parser.error("familles inconnues : %s (connues : %s)" % (", ".join(inconnues), ", ".join(FAMILLES)))
    if not os.path.isfile(options.swfsvg):
        parser.error("binaire swfsvg introuvable : %s (cargo build --release dans tools/client-analysis/swfsvg)" % options.swfsvg)
    CLIENT_RACINE = os.path.abspath(options.client)
    try:
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "swfsvg", "Cargo.toml"), encoding="utf-8") as f:
            VERSION_SWFSVG = re.search(r'^version\s*=\s*"([^"]+)"', f.read(), re.M).group(1)
    except (OSError, AttributeError):
        pass
    ignores = {}
    taches = lister_taches(CLIENT_RACINE, familles, options.sortie, options.remplacer, ignores)
    if options.limite:
        compte, retenues = {}, []
        for t in taches:
            compte[t["famille"]] = compte.get(t["famille"], 0) + 1
            if compte[t["famille"]] <= options.limite:
                retenues.append(t)
        taches = retenues
    travail = tempfile.mkdtemp(prefix="icones-", dir=options.travail)
    debut = datetime.datetime.now()
    resultats = []
    try:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, options.processus)) as pool:
            futurs = [pool.submit(executer, t, os.path.abspath(options.swfsvg), options.sortie, travail) for t in taches]
            for i, futur in enumerate(concurrent.futures.as_completed(futurs), 1):
                resultats.append(futur.result())
                if i % 500 == 0:
                    print("%d/%d" % (i, len(taches)), flush=True)
    finally:
        shutil.rmtree(travail, ignore_errors=True)
    commande = "python3 tools/client-analysis/exporter_icons.py --client \"<client 1.34>\" --sortie Outil_Azur_complet/Resources/Bot --familles " + ",".join(familles)
    if options.remplacer:
        commande += " --remplacer"
    for famille in familles:
        if famille in SOURCES:
            ecrire_provenance(options.sortie, famille, resultats, commande.replace("--familles " + ",".join(familles), "--familles " + famille),
                              ignores.get(famille, ()))
    if "WorldMap" in familles:
        ecrire_tuiles(options.sortie, resultats)
    for famille in familles:
        mine = [r for r in resultats if r["tache"]["famille"] == famille]
        print("%-10s %5d SWF/symboles, %5d PNG, %6.1f Mo, %d sans dessin, %d non exportés, %d SWF ignorés" % (
            famille, len(mine), sum(len(r["ecrits"]) for r in mine), sum(r["octets"] for r in mine) / 1e6,
            sum(1 for r in mine if r["vide"]), sum(1 for r in mine if r["erreur"]), len(ignores.get(famille, ()))))
    for r in resultats:
        if r["erreur"]:
            t = r["tache"]
            print("non exporté : %s%s : %s" % (os.path.relpath(t["swf"], CLIENT_RACINE), " " + t["symbole"] if t.get("symbole") else "", r["erreur"]))
    print("durée : %.1f s" % (datetime.datetime.now() - debut).total_seconds())
    return 0


if __name__ == "__main__":
    sys.exit(main())

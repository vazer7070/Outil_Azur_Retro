#!/usr/bin/env python3
"""Exporte les accessoires des personnages du client Dofus 1.34 en PNG, avec leurs ancres (lot AN7).

usage : exporter_accessoires.py <client>/clips/sprites/accessories <sortie> [--swfsvg CHEMIN]
                                [--fichiers a1,a2,a3,a4,a5] [--types 16,17,18] [--symboles 16_2,18_61]
                                [--pas N] [--echelle 1] [--jobs N] [--sans-palette]

Le client ne charge que accessories.swf, qui importe a1.swf à a5.swf (coiffes, capes, familiers,
armes et boucliers) ; chaque accessoire est un symbole exporté « <type>_<gfx> » (type et gfx de
l'objet dans items.xml) dont les images portent des étiquettes : S, R, L, F et B (coiffe), L, R, RR
et RL (cape), L, R, S, F, B et WL, WR, WS, WF, WB (familier, à l'arrêt et en marche), R et L
(arme, bouclier), R0 à R19... (obvijevans : étiquette + image du GM). Le client attache l'accessoire
dans le clip réservé du sprite et l'arrête sur l'étiquette (`gotoAndStop`) ; les clips que contient
cette image continuent de jouer (familier qui marche).

Pour chaque symbole « <type>_<gfx> » de chaque SWF (cairosvg et Pillow requis, swfsvg 0.2.6 ou plus) :
`swfsvg --etiquettes-list` donne ses étiquettes et la durée de l'animation tenue ; `swfsvg
--etiquettes` rend chaque étiquette image par image dans un même cadre ; les images sont rognées à
leur cadre commun et assemblées en bande (une image sur « pas », comme exporter_sprites.py), dans
<sortie>/<type>_<gfx>_<etiquette>.png. Les zones transparentes sont rognées, le magenta pur effacé,
la palette 8 bits gardée quand elle ne perd rien.

Écrit <sortie>/ancres.tsv au format d'exporter_sprites.py (UTF-8, tabulations, en-tête) :
    gfx  anim  xmin  ymin  largeur  hauteur  images  ips  fin
gfx : « <type>_<gfx> » ; anim : l'étiquette ; xmin, ymin : coin haut-gauche du PNG par rapport à
l'origine du symbole d'accessoire (le point que le client pose sur l'origine du clip réservé),
en pixels du PNG ; images : images de la bande ; ips : 40 / pas ; fin : boucle ou arret.
Le bot pose l'image k de la bande par la matrice du clip réservé (emplacements de
exporter_sprites.py --emplacements) : (x, y) du PNG -> (a (xmin + x) + c (ymin + y) + tx, ...).

--fichiers, --types et --symboles limitent l'export ; seules les lignes d'ancres.tsv et les PNG
des symboles exportés sont alors remplacés. Sans filtre, tout le dossier est refait.
"""
import argparse, concurrent.futures, os, re, sys, tempfile

import exporter_sprites as es

FICHIERS = ("a1", "a2", "a3", "a4", "a5")
SYMBOLE = re.compile(r"^\d+_\d+$")
ETIQUETTE = re.compile(r"^[A-Za-z][A-Za-z0-9]*$")
LOT = 40  # symboles par appel de swfsvg --etiquettes (un processus par lot)


def etiquettes_de(swfsvg, swf, noms):
    """Étiquettes des symboles (`swfsvg --etiquettes-list`) : [(nom, étiquette, image, images, fin)]."""
    lignes = es.lancer(swfsvg + ["--etiquettes-list", swf] + list(noms)).splitlines()
    if not lignes or lignes[0].split("\t") != ["nom", "etiquette", "image", "images", "fin"]:
        raise RuntimeError("swfsvg --etiquettes-list illisible : swfsvg 0.2.6 ou plus requis")
    resultat = []
    for ligne in lignes[1:]:
        col = ligne.split("\t")
        if len(col) == 5 and col[2].isdigit() and col[3].isdigit():
            resultat.append((col[0], col[1], int(col[2]), int(col[3]), col[4]))
    return resultat


def symboles_de(swfsvg, swf):
    """Noms d'export « <type>_<gfx> » du SWF, dans l'ordre de --list."""
    noms = []
    for ligne in es.lancer(swfsvg + ["--list", swf]).splitlines()[1:]:
        col = ligne.split("\t")
        if len(col) >= 3 and SYMBOLE.match(col[0]) and col[0] not in noms:
            noms.append(col[0])
    return noms


def exporter_lot(swf, sortie, noms, pas, echelle, palette, swfsvg, temporaire):
    """Exporte les symboles noms d'un SWF ; renvoie (lignes d'ancres.tsv, messages, octets). Ne lève jamais."""
    lignes, messages, octets = [], [], 0
    try:
        etiquettes = etiquettes_de(swfsvg, swf, noms)
        with tempfile.TemporaryDirectory(dir=temporaire) as dossier:
            index = es.rendre_svg(swfsvg, ["--etiquettes", swf, dossier] + list(noms), dossier)
            for nom, etiquette, _, images, fin in etiquettes:
                if not ETIQUETTE.match(etiquette):
                    messages.append("%s : étiquette « %s » illisible, ignorée" % (nom, etiquette))
                    continue
                prefixe = "%s_%s_a" % (nom, etiquette)
                fichiers = sorted((f for f in index if f.startswith(prefixe)), key=lambda f: int(index[f][9]))
                fichiers = fichiers[::pas]
                if not fichiers:
                    messages.append("%s : %s sans rendu" % (nom, etiquette))
                    continue
                try:
                    ligne, taille = es.bande([os.path.join(dossier, f) for f in fichiers], nom, etiquette, sortie, echelle, palette)
                except Exception as erreur:  # une étiquette illisible ne doit pas perdre les autres
                    messages.append("%s : %s refusée (%s)" % (nom, etiquette, erreur))
                    continue
                if not ligne:
                    messages.append("%s : %s vide" % (nom, etiquette))
                    continue
                ligne += (es.IPS // pas, "arret" if ligne[6] == 1 else ("boucle" if fin == "boucle" else "arret"))
                lignes.append(ligne); octets += taille
    except Exception as erreur:  # un SWF illisible ne doit pas arrêter la série
        messages.append("%s : %s" % (os.path.basename(swf), erreur))
    return lignes, messages, octets


def cle(valeur):
    """Tri des gfx « <type>_<gfx> » par type puis gfx numériques."""
    return tuple(int(v) for v in valeur.split("_")) if SYMBOLE.match(valeur) else (1 << 30, valeur)


def main():
    p = argparse.ArgumentParser(description="Accessoires des personnages du client Dofus 1.34 -> PNG + ancres.tsv")
    p.add_argument("accessoires", help="dossier clips/sprites/accessories du client")
    p.add_argument("sortie", help="dossier de sortie (Outil_Azur_complet/Resources/Bot/Accessoires)")
    p.add_argument("--swfsvg", help="binaire swfsvg 0.2.6+ (sinon $SWFSVG, le PATH ou swfsvg/target/release)")
    p.add_argument("--fichiers", default=",".join(FICHIERS), help="SWF à lire, sans extension (défaut : a1,a2,a3,a4,a5)")
    p.add_argument("--types", help="types d'objets à exporter (16 coiffes, 17 capes, 18 familiers...), séparés par des virgules")
    p.add_argument("--symboles", help="symboles à exporter (16_2,18_61), séparés par des virgules")
    p.add_argument("--pas", type=int, default=1, help="une image sur N des animations tenues (diviseur de 40, défaut 1)")
    p.add_argument("--echelle", type=float, default=1.0, help="échelle des PNG (défaut 1)")
    p.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="processus en parallèle")
    p.add_argument("--sans-palette", action="store_true", help="garder tous les PNG en RGBA 32 bits")
    a = p.parse_args()

    if a.pas < 1 or es.IPS % a.pas:
        sys.exit("--pas doit diviser %d" % es.IPS)
    swfsvg = es.trouver_swfsvg(a.swfsvg)
    fichiers = [f.strip() for f in a.fichiers.split(",") if f.strip()]
    swfs = {f: os.path.join(a.accessoires, f + ".swf") for f in fichiers}
    absents = [f for f, chemin in swfs.items() if not os.path.isfile(chemin)]
    if absents:
        sys.exit("SWF absents : " + ", ".join(absents))
    types = {t.strip() for t in a.types.split(",") if t.strip()} if a.types else None
    voulus = {s.strip() for s in a.symboles.split(",") if s.strip()} if a.symboles else None
    os.makedirs(a.sortie, exist_ok=True)

    lots, messages = [], []
    for f in fichiers:
        try:
            noms = symboles_de(swfsvg, swfs[f])
        except RuntimeError as erreur:
            if "option inconnue" in str(erreur):
                sys.exit("swfsvg 0.2.6 ou plus requis (%s)" % erreur)
            messages.append("%s : %s" % (f, erreur))
            continue
        noms = [n for n in noms if (types is None or n.split("_")[0] in types) and (voulus is None or n in voulus)]
        for k in range(0, len(noms), LOT):
            lots.append((swfs[f], noms[k:k + LOT]))
    if not lots:
        sys.exit("aucun symbole à exporter")
    try:
        etiquettes_de(swfsvg, lots[0][0], lots[0][1][:1])
    except RuntimeError as erreur:
        sys.exit("swfsvg 0.2.6 ou plus requis (%s)" % erreur)

    lignes, octets = [], 0
    with tempfile.TemporaryDirectory(prefix="accessoires-") as temporaire:
        with concurrent.futures.ProcessPoolExecutor(max_workers=max(1, a.jobs)) as pool:
            taches = [pool.submit(exporter_lot, swf, a.sortie, noms, a.pas, a.echelle, not a.sans_palette, swfsvg, temporaire)
                      for swf, noms in lots]
            for t in taches:
                l, m, o = t.result()
                lignes += l; messages += m; octets += o

    partiel = types is not None or voulus is not None or set(fichiers) != set(FICHIERS)
    exportes = {n for _, noms in lots for n in noms}
    produits = {"%s_%s.png" % (l[0], l[1]) for l in lignes}
    for f in os.listdir(a.sortie):
        m = re.match(r"^(\d+_\d+)_([A-Za-z][A-Za-z0-9]*)\.png$", f)
        if m and f not in produits and (not partiel or m.group(1) in exportes):
            os.remove(os.path.join(a.sortie, f))
    ancres = os.path.join(a.sortie, "ancres.tsv")
    if partiel:
        lignes = [l for l in es.lire_ancres(ancres) if l[0] not in exportes and l[8] is not None] + lignes
    lignes.sort(key=lambda l: (cle(l[0]), l[1]))
    with open(ancres, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(es.EN_TETE) + "\n")
        for l in lignes:
            f.write("\t".join(str(v) for v in l) + "\n")
    for m in messages:
        print(m)
    print("%d symboles, %d PNG (%.1f Mo), %d lignes dans %s, %d messages" % (
        len(exportes), len(produits), octets / 1e6, len(lignes), ancres, len(messages)))


if __name__ == "__main__":
    main()

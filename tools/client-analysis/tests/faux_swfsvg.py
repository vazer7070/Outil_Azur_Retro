#!/usr/bin/env python3
"""Faux swfsvg pour tester exporter_sprites.py sans fichier du client.

Le « SWF » est un JSON : {"symboles": {"staticR": 3, "walkR": 4, ...}, "scene": true, "vides": ["staticB"],
"vides_images": {"staticF": [2]}, "fins": {"hitR": "static"}, "larges": {"dieR": 9000}} (nom d'export ->
images utiles ; symboles vides partout ou à certaines images ; colonne fin de --list, « boucle » par
défaut ; largeur d'image d'un symbole très large). L'image N d'un symbole est un rectangle brun de
4 x 25 pixels dont le bord gauche est en x = N - 6 (le pied, point d'ancrage, est en y = 0), plus un
carré magenta (couleur technique du client) qui doit disparaître. Toutes les images partagent le cadre
-10 -30 20 32, comme celles d'une série `--frame all`. Un symbole de « larges » remplit tout son cadre
(-10 -30 <largeur> 32), plus un repère d'un pixel en x = N - 10. Mêmes options, mêmes fichiers et même
index.tsv (12 colonnes) que swfsvg 0.2.3 ; --list a sa colonne fin.

Zones de couleur (swfsvg 0.2.5) : "zones": [1, 3] fait du rectangle brun la zone 1 et ajoute en haut
de chaque image un chapeau bleu (#2040c0, 4 x 3 px) qui est la zone 3 ; --zones-list les énumère.
--zones <mode> (« 123 », « aucune », « 13 »...) peint tout en noir, sauf les zones du mode dans leur
couleur unité (1 rouge, 2 vert, 3 bleu), alpha gardé, comme swfsvg.

Accessoires (swfsvg 0.2.6) : "emplacements": {"walkR": [[2, "L", 0, 0, "1", "0", "0", "1", "-5", "-20", "5",
"-20", 0, 1]]} donne les clips réservés d'un symbole (emplacement, étiquette, miroir, tête, a, b, c, d, tx,
ty, tx_miroir, ty_miroir, rang, formes) ; --emplacements les écrit pour chaque image demandée, tx avancé
d'un pixel par image (comme le rectangle). "etiquettes": {"16_2": [["R", 1, 1, "arret"], ["L", 4, 3,
"boucle"]]} donne les étiquettes d'images d'un symbole d'accessoire (étiquette, image, images tenues,
fin) ; --etiquettes-list les écrit, --etiquettes rend <nom>_<étiquette>_a<k>.svg (image k comme ci-dessus)
avec un index.tsv dont la colonne 10 est k et la colonne 11 le nombre d'images."""
import json, os, sys

CADRE = (-10, -30, 20, 32)


UNITES = {1: "#ff0000", 2: "#00ff00", 3: "#0000ff"}


def peinture(couleur, zone, mode):
    """Couleur d'un aplat : la sienne sans --zones, sinon sa couleur unité (zone du mode) ou noir."""
    if mode is None:
        return couleur
    return UNITES[zone] if zone and str(zone) in mode else "#000000"


def svg(image, vide, cadre=CADRE, zones=(), mode=None):
    if vide:
        corps = ""
    elif cadre != CADRE:
        corps = ('<rect x="%d" y="-25" width="%d" height="25" fill="%s"/>'
                 '<rect x="%d" y="-28" width="1" height="2" fill="%s"/>'
                 % (cadre[0], cadre[2], peinture("#804020", 1 if 1 in zones else 0, mode), image - 10,
                    peinture("#00ff00", 0, mode)))
    else:
        corps = ('<rect x="%d" y="-25" width="4" height="25" fill="%s"/>'
                 '<rect x="7" y="-3" width="2" height="2" fill="%s"/>'
                 % (image - 6, peinture("#804020", 1 if 1 in zones else 0, mode), peinture("#ff00ff", 0, mode)))
        if 3 in zones:
            corps += '<rect x="%d" y="-25" width="4" height="3" fill="%s"/>' % (image - 6, peinture("#2040c0", 3, mode))
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="%d %d %d %d">%s</svg>'
            % ((cadre[2], cadre[3]) + cadre + (corps,)))


def main(args):
    liste = scene = ajout = liste_zones = emplacements = etiquettes_liste = etiquettes = False
    nom = image = mode = None
    reste = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--list": liste = True
        elif a == "--emplacements": emplacements = True
        elif a == "--etiquettes-list": etiquettes_liste = True
        elif a == "--etiquettes": etiquettes = True
        elif a == "--scene": scene = True
        elif a == "--append-index": ajout = True
        elif a == "--name": i += 1; nom = args[i]
        elif a == "--frame": i += 1; image = args[i]
        elif a == "--zones": i += 1; mode = "" if args[i] == "aucune" else args[i]
        elif a == "--zones-list": liste_zones = True
        else: reste.append(a)
        i += 1
    try:
        with open(reste[0], encoding="utf-8") as f:
            desc = json.load(f)
    except (OSError, ValueError, IndexError) as erreur:
        sys.stderr.write("swfsvg : SWF illisible (%s)\n" % erreur)
        return 1
    symboles = desc.get("symboles", {})
    zones = desc.get("zones", [])
    if liste_zones:
        print("clip\tinstance\tzone")
        for z in zones:
            print("1\tz%d\t%d" % (z, z))
        return 0
    if emplacements:
        print("\t".join(("nom", "image", "emplacement", "etiquette", "miroir", "tete", "a", "b", "c", "d", "tx", "ty",
                         "tx_miroir", "ty_miroir", "rang", "formes")))
        for n in reste[1:]:
            if n not in symboles:
                sys.stderr.write("swfsvg : %s absent\n" % n)
                continue
            images = range(1, symboles[n] + 1) if image == "all" else [int(image or 1)]
            for k in images:
                for c in desc.get("emplacements", {}).get(n, []):
                    c = list(c)
                    c[8] = str(float(c[8]) + k - 1).rstrip("0").rstrip(".")
                    print("\t".join([n, str(k)] + [str(v) for v in c]))
        return 0
    if etiquettes_liste or etiquettes:
        if etiquettes:
            dossier = reste[1]
            os.makedirs(dossier, exist_ok=True)
            noms = reste[2:]
        else:
            noms = reste[1:]
            print("nom\tetiquette\timage\timages\tfin")
        lignes = []
        for n in noms:
            if n not in symboles:
                sys.stderr.write("swfsvg : %s absent\n" % n)
                continue
            for etiquette, premiere, total, fin in desc.get("etiquettes", {}).get(n, []):
                if not etiquettes:
                    print("%s\t%s\t%d\t%d\t%s" % (n, etiquette, premiere, total, fin))
                    continue
                for k in range(1, total + 1):
                    fichier = "%s_%s_a%03d.svg" % (n, etiquette, k)
                    with open(os.path.join(dossier, fichier), "w", encoding="utf-8") as f:
                        f.write(svg(k, n in desc.get("vides", [])))
                    x0, y0, w, h = CADRE
                    lignes.append(["%s_%s" % (n, etiquette), "1", str(x0), str(y0), str(w), str(h), "", str(x0 + w), str(y0 + h),
                                   str(k), str(total), fichier])
        if etiquettes:
            with open(os.path.join(dossier, "index.tsv"), "a" if ajout else "w", encoding="utf-8") as f:
                for l in lignes:
                    f.write("\t".join(l) + "\n")
        return 0
    if liste:
        print("nom\tid\ttype\timages\timages_timeline\tfin")
        print("scene\t0\tscene\t1\t1\tarret")
        for k, (n, total) in enumerate(symboles.items()):
            print("%s\t%d\tclip\t%d\t1\t%s" % (n, k + 1, total, desc.get("fins", {}).get(n, "boucle")))
        return 0
    dossier = reste[1]
    os.makedirs(dossier, exist_ok=True)
    lignes = []

    def ecrire(fichier, nom_symbole, n, total, vide):
        cadre = CADRE
        if nom_symbole in desc.get("larges", {}):
            cadre = (CADRE[0], CADRE[1], desc["larges"][nom_symbole], CADRE[3])
        with open(os.path.join(dossier, fichier), "w", encoding="utf-8") as f:
            f.write(svg(n, vide, cadre, zones, mode))
        x0, y0, w, h = cadre
        lignes.append([nom_symbole, "1", str(x0), str(y0), str(w), str(h), "", str(x0 + w), str(y0 + h), str(n), str(total), fichier])

    if scene:
        nom = nom or os.path.splitext(os.path.basename(reste[0]))[0]
        ecrire(nom + ".svg", nom, 1, 1, not desc.get("scene"))
    else:
        for n in reste[2:]:
            if n not in symboles:
                sys.stderr.write("swfsvg : %s absent\n" % n)
                continue
            total = symboles[n]
            vide = lambda k: n in desc.get("vides", []) or k in desc.get("vides_images", {}).get(n, [])
            if image == "all":
                for k in range(1, total + 1):
                    ecrire("%s_f%03d.svg" % (n, k), n, k, total, vide(k))
            else:
                ecrire(n + ".svg", n, int(image or 1), total, vide(int(image or 1)))
    with open(os.path.join(dossier, "index.tsv"), "a" if ajout else "w", encoding="utf-8") as f:
        for l in lignes:
            f.write("\t".join(l) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

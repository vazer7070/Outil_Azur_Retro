#!/usr/bin/env python3
"""Faux swfsvg pour tester exporter_sorts.py sans fichier du client.

Le « SWF » est un JSON : {"scene": 4, "fin": "static", "symboles": {"shoot": 3}, "vides": ["move"],
"illisible": [2], "script": "attachMovie"} : images de la scène (0 : scène vide), colonne fin de la scène,
symboles exportés et leurs images (fin « static »), symboles vides, images de la scène dont le SVG est
illisible (comme un échec de cairo), texte présent dans le corps du SWF (lu par la recherche de scripts).
L'image N de la scène ou d'un symbole est un rectangle de 6 x 4 pixels dont le bord gauche est en
x = N - 8, couleur #c04000 pour la scène et #0040c0 pour un symbole ; toutes partagent le cadre
-10 -10 20 20, comme une série `--frame all`. Options et index.tsv (12 colonnes) de swfsvg 0.2.4.

Un vrai SWF binaire (pour la lecture des PlaceObject par l'exporteur) est décrit par le JSON « <swf>.json »
posé à côté. {"rotate": N, "pivot": [x, y]} y ajoute à la scène une instance « rotate » de N images :
l'image K est un rectangle #00c040 de K + 1 x 2 pixels dont le coin haut-gauche est en (x + 1, y - 2) ;
--instance rotate ne rend qu'elle, --sans-instance rotate tout sauf elle (comme swfsvg 0.2.4)."""
import json, os, sys

CADRE = (-10, -10, 20, 20)


def svg(image, vide, couleur, illisible=False):
    if illisible:  # cadre lisible, XML tronqué : cairosvg lève une exception pendant le rendu
        return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="%d %d %d %d"><rect x='
                % ((CADRE[2], CADRE[3]) + CADRE))
    corps = "" if vide else '<rect x="%d" y="-4" width="6" height="4" fill="%s"/>' % (image - 8, couleur)
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="%d %d %d %d">%s</svg>'
            % ((CADRE[2], CADRE[3]) + CADRE + (corps,)))


def tournee(image, pivot):
    x, y = pivot
    return '<rect x="%d" y="%d" width="%d" height="2" fill="#00c040"/>' % (x + 1, y - 2, image + 1)


def main(args):
    liste = scene = False
    nom = image = instance = sans = None
    reste = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--list": liste = True
        elif a == "--scene": scene = True
        elif a == "--name": i += 1; nom = args[i]
        elif a == "--frame": i += 1; image = args[i]
        elif a == "--instance": i += 1; instance = args[i]
        elif a == "--sans-instance": i += 1; sans = args[i]
        else: reste.append(a)
        i += 1
    try:
        description = reste[0] + ".json" if reste and os.path.isfile(reste[0] + ".json") else reste[0]
        with open(description, encoding="utf-8") as f:
            desc = json.load(f)
    except (OSError, ValueError, IndexError) as erreur:
        sys.stderr.write("swfsvg : SWF illisible (%s)\n" % erreur)
        return 1
    symboles = desc.get("symboles", {})
    images_scene = desc.get("scene", 0)
    if liste:
        print("nom\tid\ttype\timages\timages_timeline\tfin")
        print("scene\t0\tscene\t%d\t1\t%s" % (max(1, images_scene, desc.get("rotate", 0)), desc.get("fin", "arret")))
        for k, (n, total) in enumerate(symboles.items()):
            print("%s\t%d\tclip\t%d\t%d\tstatic" % (n, k + 1, total, total))
        return 0
    if image != "all":
        sys.stderr.write("swfsvg : ce faux swfsvg ne connaît que --frame all\n")
        return 2
    dossier = reste[1]
    os.makedirs(dossier, exist_ok=True)
    lignes = []

    rotate, pivot = desc.get("rotate", 0), desc.get("pivot", [0, 0])

    def ecrire(nom_rendu, total, vide, couleur, illisibles=(), avec_rotate=False):
        for k in range(1, total + 1):
            fichier = "%s_f%03d.svg" % (nom_rendu, k)
            texte = svg(k, vide, couleur, k in illisibles)
            if avec_rotate and k <= rotate:
                texte = texte.replace("</svg>", tournee(k, pivot) + "</svg>")
            with open(os.path.join(dossier, fichier), "w", encoding="utf-8") as f:
                f.write(texte)
            x0, y0, w, h = CADRE
            lignes.append([nom_rendu, "0", str(x0), str(y0), str(w), str(h), "", str(x0 + w), str(y0 + h), str(k), str(total), fichier])

    if scene:
        nom_rendu = nom or os.path.splitext(os.path.basename(reste[0]))[0]
        total = max(1, images_scene, rotate)
        if instance is not None:
            ecrire(nom_rendu, total, True, "", avec_rotate=instance.lower() == "rotate")
        else:
            ecrire(nom_rendu, total, images_scene == 0, "#c04000", desc.get("illisible", []),
                   avec_rotate=(sans or "").lower() != "rotate")
    else:
        for n in reste[2:]:
            if n in symboles:
                ecrire(n, symboles[n], n in desc.get("vides", []), "#0040c0")
            else:
                sys.stderr.write("swfsvg : %s absent\n" % n)
    with open(os.path.join(dossier, "index.tsv"), "w", encoding="utf-8") as f:
        for l in lignes:
            f.write("\t".join(l) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

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
index.tsv (12 colonnes) que swfsvg 0.2.3 ; --list a sa colonne fin."""
import json, os, sys

CADRE = (-10, -30, 20, 32)


def svg(image, vide, cadre=CADRE):
    if vide:
        corps = ""
    elif cadre != CADRE:
        corps = ('<rect x="%d" y="-25" width="%d" height="25" fill="#804020"/>'
                 '<rect x="%d" y="-28" width="1" height="2" fill="#00ff00"/>' % (cadre[0], cadre[2], image - 10))
    else:
        corps = ('<rect x="%d" y="-25" width="4" height="25" fill="#804020"/>'
                 '<rect x="7" y="-3" width="2" height="2" fill="#ff00ff"/>' % (image - 6))
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="%d %d %d %d">%s</svg>'
            % ((cadre[2], cadre[3]) + cadre + (corps,)))


def main(args):
    liste = scene = ajout = False
    nom = image = None
    reste = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--list": liste = True
        elif a == "--scene": scene = True
        elif a == "--append-index": ajout = True
        elif a == "--name": i += 1; nom = args[i]
        elif a == "--frame": i += 1; image = args[i]
        else: reste.append(a)
        i += 1
    try:
        with open(reste[0], encoding="utf-8") as f:
            desc = json.load(f)
    except (OSError, ValueError, IndexError) as erreur:
        sys.stderr.write("swfsvg : SWF illisible (%s)\n" % erreur)
        return 1
    symboles = desc.get("symboles", {})
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
            f.write(svg(n, vide, cadre))
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

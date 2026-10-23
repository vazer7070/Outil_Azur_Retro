#!/usr/bin/env python3
"""Exporte les pièces de l'inventaire et de la fiche d'objet du client Dofus 1.34 (lot F13b).

usage :
  exporter_inventaire.py --client <dossier du client> --sortie <Outil_Azur_complet/Resources/Bot>
                         [--swfsvg <binaire>] [--travail <dossier>]

Écrit dans <sortie>/Client/ (copié tel quel par la cible BotClientAsset du projet) :
  inventaire-silhouette.png   profondeur 16 (sans nom) de `UI_Inventory` : la silhouette grise
                              posée derrière les emplacements d'équipement
  inventaire-croix.png        instance `_mcTwoHandedCrossLeft` de `UI_Inventory` : la croix montrée
                              sur le bouclier quand l'arme tient à deux mains
  ItemViewerDestroy.png, ItemViewerTarget.png, ItemViewerTwoHand.png, ItemViewerUseHand.png,
  UI_InventoryMountIcon.png, ItemSetViewerItemBorder.png
                              symboles exportés du même nom (boutons de la fiche d'objet, icône de la
                              monture, bordure d'un objet de panoplie non porté)

Le rendu réutilise les fonctions d'exporter_icons.py (swfsvg, cairosvg à l'échelle 2, magenta pur
effacé, palette de 256 couleurs quand l'écart reste invisible). Les calques sont rendus dans le cadre
de leur instance (recadrage de swfsvg), pas dans celui d'`UI_Inventory`."""
import argparse
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exporter_icons as icones  # noqa: E402  (même dossier)

# Identifiant du DefineSprite exporté sous le nom `UI_Inventory` : lu dans le SWF, jamais supposé.
INTERFACE = "UI_Inventory"

# (fichier de sortie, symbole exporté, calque à garder seul dans `UI_Inventory` ou None)
PIECES = [
    ("inventaire-silhouette", INTERFACE, "#16"),
    ("inventaire-croix", INTERFACE, "_mcTwoHandedCrossLeft"),
    ("ItemViewerDestroy", "ItemViewerDestroy", None),
    ("ItemViewerTarget", "ItemViewerTarget", None),
    ("ItemViewerTwoHand", "ItemViewerTwoHand", None),
    ("ItemViewerUseHand", "ItemViewerUseHand", None),
    ("UI_InventoryMountIcon", "UI_InventoryMountIcon", None),
    ("ItemSetViewerItemBorder", "ItemSetViewerItemBorder", None),
]


def exporter(core, binaire, sortie, travail, echelle=2):
    with open(core, "rb") as f:
        donnees = f.read()
    symboles = icones.exports(donnees)
    if INTERFACE not in symboles:
        raise icones.SwfInvalide("symbole %s absent de %s" % (INTERFACE, core))
    resultats = []
    for fichier, symbole, calque in PIECES:
        variante = None
        if calque is not None:
            variante = icones.variante_calque(donnees, {calque}, True, sprite=symboles[INTERFACE])
        rendu = icones.rendre(binaire, core, travail, echelle, symbole=symbole, donnees=variante, etiquette="inv")
        if rendu is None:
            raise icones.SwfInvalide("rendu vide pour %s" % fichier)
        image = rendu.image
        cadre = image.getchannel("A").getbbox()
        if cadre is not None:
            image = image.crop(cadre)
        chemin = os.path.join(sortie, "Client", fichier + ".png")
        taille = icones.enregistrer(image, chemin)
        resultats.append((fichier, image.width, image.height, taille))
        print("%s.png %dx%d %d octets" % (fichier, image.width, image.height, taille), flush=True)
    return resultats


def main(argv=None):
    parser = argparse.ArgumentParser(description="Exporte les pièces de l'inventaire du client Dofus 1.34.")
    parser.add_argument("--client", required=True, help="dossier du client (contient modules/core.swf)")
    parser.add_argument("--sortie", required=True, help="dossier Outil_Azur_complet/Resources/Bot")
    parser.add_argument("--swfsvg", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "swfsvg", "target", "release", "swfsvg"))
    parser.add_argument("--travail", default=None, help="dossier temporaire (défaut : dossier système)")
    options = parser.parse_args(argv)
    core = os.path.join(options.client, "modules", "core.swf")
    if not os.path.exists(core):
        sys.exit("exporter_inventaire.py : %s introuvable" % core)
    if not os.path.exists(options.swfsvg):
        sys.exit("exporter_inventaire.py : swfsvg introuvable (%s)" % options.swfsvg)
    travail = tempfile.mkdtemp(prefix="inventaire-", dir=options.travail)
    try:
        exporter(core, options.swfsvg, options.sortie, travail)
    finally:
        shutil.rmtree(travail, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

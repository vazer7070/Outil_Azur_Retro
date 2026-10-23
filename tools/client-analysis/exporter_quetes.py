#!/usr/bin/env python3
"""Exporte les pièces de la fenêtre des quêtes du client Dofus 1.34 (lot F10).

usage :
  exporter_quetes.py --client <dossier du client> --sortie <Outil_Azur_complet/Resources/Bot>
                     [--swfsvg <binaire>] [--travail <dossier>]

Écrit dans <sortie>/Client/ (copié tel quel vers ressources/Bot/UI/Client par le projet) :
  UI_QuestXP.png             symbole exporté du même nom : icône de la récompense en expérience
                             (la récompense en kamas, `UI_QuestKamaSymbol`, est déjà `kamas.png`)
  quete-terminee.png         instance `_mcCheckFinished` de `UI_QuestsQuestItem` : coche d'une quête terminée
  quete-en-cours.png         instance `_mcCurrent` de `UI_QuestsQuestItem` : marque d'une quête en cours
  etape-courante.png         instance `_mcArrow` de `UI_QuestsStepItem` : flèche de l'étape courante
  objectif-boussole.png      instance `_mcCompass` de `UI_QuestsObjectivetItem` : boussole d'un objectif localisé

Le rendu réutilise les fonctions d'exporter_icons.py (swfsvg, cairosvg à l'échelle 2, magenta pur
effacé, palette de 256 couleurs quand l'écart reste invisible). Les calques sont rendus seuls dans le
DefineSprite de leur ligne, puis recadrés sur leurs pixels visibles."""
import argparse
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import exporter_icons as icones  # noqa: E402  (même dossier)

# (fichier de sortie, symbole exporté, instance à garder seule dans ce symbole ou None)
PIECES = [
    ("UI_QuestXP", "UI_QuestXP", None),
    ("quete-terminee", "UI_QuestsQuestItem", "_mcCheckFinished"),
    ("quete-en-cours", "UI_QuestsQuestItem", "_mcCurrent"),
    ("etape-courante", "UI_QuestsStepItem", "_mcArrow"),
    ("objectif-boussole", "UI_QuestsObjectivetItem", "_mcCompass"),
]


def exporter(core, binaire, sortie, travail, echelle=2):
    with open(core, "rb") as f:
        donnees = f.read()
    symboles = icones.exports(donnees)
    resultats = []
    for fichier, symbole, calque in PIECES:
        if symbole not in symboles:
            raise icones.SwfInvalide("symbole %s absent de %s" % (symbole, core))
        variante = None
        if calque is not None:
            # Identifiant du DefineSprite lu dans ExportAssets, jamais supposé.
            variante = icones.variante_calque(donnees, {calque}, True, sprite=symboles[symbole])
        rendu = icones.rendre(binaire, core, travail, echelle, symbole=symbole, donnees=variante, etiquette="quete")
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
    parser = argparse.ArgumentParser(description="Exporte les pièces de la fenêtre des quêtes du client Dofus 1.34.")
    parser.add_argument("--client", required=True, help="dossier du client (contient modules/core.swf)")
    parser.add_argument("--sortie", required=True, help="dossier Outil_Azur_complet/Resources/Bot")
    parser.add_argument("--swfsvg", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "swfsvg", "target", "release", "swfsvg"))
    parser.add_argument("--travail", default=None, help="dossier temporaire (défaut : dossier système)")
    options = parser.parse_args(argv)
    core = os.path.join(options.client, "modules", "core.swf")
    if not os.path.exists(core):
        sys.exit("exporter_quetes.py : %s introuvable" % core)
    if not os.path.exists(options.swfsvg):
        sys.exit("exporter_quetes.py : swfsvg introuvable (%s)" % options.swfsvg)
    travail = tempfile.mkdtemp(prefix="quetes-", dir=options.travail)
    try:
        exporter(core, options.swfsvg, options.sortie, travail)
    finally:
        shutil.rmtree(travail, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

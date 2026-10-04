#!/usr/bin/env python3
"""Documents du client Dofus 1.34 (livres, parchemins, pancartes) -> XML du bot (ressources/Bot/BotDocs).

Un document (`data/docs/<langue>_<id>_<date>.swf`) est un SWF d'une image dont l'unique DoAction
affecte des littéraux à `this` : `type` (book, parchment, roadsignleft, roadsignright), `title`,
`subtitle`, `author`, `style` (feuille `styles/<n>.css`), `pages[n]` (HTML simplifié) et
`chapters[n] = [titre, page, page de droite, titre visible]`. Le serveur ouvre le document par
`dCK<id>_<date>` ; le bot lit alors `BotDocs/<id>_<date>.xml`.

Chaîne : `avm1dump` puis `as2lite.py` (comme lang2xml.py), puis ce script, qui relit les
affectations littérales et écrit un XML par document. Rien n'est exécuté depuis le SWF ; les
affectations qui ne sont pas des littéraux sont ignorées et comptées.

Usage :
  python3 docs2xml.py <dossier data/docs> <dossier de sortie> [--langue fr] [--avm1dump CHEMIN] [--travail DOSSIER]
  python3 docs2xml.py --as <fichier.as.txt> --id 139 --date 0612131303 --sortie 139_0612131303.xml
"""

import argparse
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ICI)

from lang2xml import desassembler, lire_affectations, texte_xml, trouver_avm1dump  # noqa: E402

TYPES = ("book", "parchment", "roadsignleft", "roadsignright")
NOM_SWF = re.compile(r"(?P<langue>[a-z]{2})_(?P<id>\d{1,9})_(?P<date>\d{1,14})\.swf\Z")


def indexes(noeud):
    """Tableau AS2 (`new Array()` puis `t[0] = …`) -> liste ordonnée par indice, trous ignorés."""
    if isinstance(noeud, list):
        return list(noeud)
    if not isinstance(noeud, dict):
        return []
    cles = sorted((int(c), c) for c in noeud if re.fullmatch(r"\d+", c))
    return [noeud[c] for _, c in cles]


def texte(v):
    return texte_xml(v) if isinstance(v, str) else ""


def booleen(v):
    return "true" if v is True or v == 1 or v == "true" else "false"


def document_xml(lignes, ident, date, source):
    """Construit l'élément <BotDoc> ; renvoie (élément, lignes ignorées, avertissements)."""
    racines, ignorees, _ = lire_affectations(lignes)
    doc = racines.get("this", {})
    if not isinstance(doc, dict):
        doc = {}
    avertissements = []
    type_doc = doc.get("type") if doc.get("type") in TYPES else "book"
    if doc.get("type") not in TYPES:
        avertissements.append("type inconnu %r, « book » utilisé" % (doc.get("type"),))
    style = doc.get("style")
    racine = ET.Element("BotDoc", {"id": str(ident), "date": date, "type": type_doc,
                                   "style": str(style) if isinstance(style, int) else "1", "source": source})
    racine.text = "\n"

    def ajouter(element):
        element.tail = "\n"
        racine.append(element)

    for nom, cle in (("titre", "title"), ("soustitre", "subtitle"), ("auteur", "author")):
        e = ET.Element(nom)
        e.text = texte(doc.get(cle))
        ajouter(e)
    pages = indexes(doc.get("pages"))
    for page in pages:
        e = ET.Element("page")
        e.text = texte(page)
        ajouter(e)
    for chapitre in indexes(doc.get("chapters")):
        if not isinstance(chapitre, list) or len(chapitre) < 2 or not isinstance(chapitre[1], int):
            avertissements.append("chapitre illisible ignoré")
            continue
        if not 0 <= chapitre[1] < len(pages):
            avertissements.append("chapitre « %s » vers une page absente (%d)" % (chapitre[0], chapitre[1]))
        ajouter(ET.Element("chapitre", {
            "titre": texte(chapitre[0]), "page": str(chapitre[1]),
            "droite": booleen(chapitre[2] if len(chapitre) > 2 else False),
            "titreVisible": booleen(chapitre[3] if len(chapitre) > 3 else False)}))
    if not pages:
        avertissements.append("aucune page")
    return racine, ignorees, avertissements


def ecrire(racine, chemin):
    os.makedirs(os.path.dirname(os.path.abspath(chemin)), exist_ok=True)
    temporaire = chemin + ".tmp"
    with open(temporaire, "wb") as f:
        ET.ElementTree(racine).write(f, encoding="utf-8", xml_declaration=True)
        f.write(b"\n")
    os.replace(temporaire, chemin)


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    p.add_argument("dossier_docs", nargs="?", help="dossier data/docs du client")
    p.add_argument("sortie", nargs="?", help="dossier de sortie (Resources/Bot/BotDocs)")
    p.add_argument("--langue", default="fr")
    p.add_argument("--avm1dump")
    p.add_argument("--travail", help="dossier des fichiers intermédiaires (conservés)")
    p.add_argument("--as", dest="fichier_as", help="convertir une pseudo-décompilation existante")
    p.add_argument("--id", type=int)
    p.add_argument("--date", default="")
    p.add_argument("--sortie", dest="sortie_fichier")
    a = p.parse_args()

    if a.fichier_as:
        if a.id is None or not a.sortie_fichier:
            p.error("--as demande --id et --sortie")
        with open(a.fichier_as, encoding="utf-8") as f:
            racine, ignorees, avert = document_xml(f.read().split("\n"), a.id, a.date, os.path.basename(a.fichier_as))
        ecrire(racine, a.sortie_fichier)
        print("%s : %d page(s) ; %d ligne(s) ignorée(s)%s" % (a.sortie_fichier, len(racine.findall("page")), ignorees,
                                                              "".join(" ; " + x for x in avert)))
        return

    if not (a.dossier_docs and a.sortie):
        p.error("indiquez le dossier data/docs et le dossier de sortie")
    avm1dump = trouver_avm1dump(a.avm1dump)
    travail = a.travail or tempfile.mkdtemp(prefix="docs2xml-")
    os.makedirs(travail, exist_ok=True)
    os.makedirs(a.sortie, exist_ok=True)
    nombre, total, erreurs = 0, 0, 0
    for nom in sorted(os.listdir(a.dossier_docs)):
        m = NOM_SWF.match(nom)
        if not m or m.group("langue") != a.langue:
            continue
        try:
            lignes = desassembler(os.path.join(a.dossier_docs, nom), avm1dump, travail)
        except Exception as erreur:  # SWF illisible : signalé, les autres documents continuent.
            print("%s : illisible (%s)" % (nom, erreur))
            erreurs += 1
            continue
        racine, ignorees, avert = document_xml(lignes, int(m.group("id")), m.group("date"), nom)
        sortie = os.path.join(a.sortie, "%s_%s.xml" % (m.group("id"), m.group("date")))
        ecrire(racine, sortie)
        nombre += 1
        total += os.path.getsize(sortie)
        if ignorees or avert:
            print("%s : %d ligne(s) ignorée(s)%s" % (nom, ignorees, "".join(" ; " + x for x in avert)))
    print("%d document(s) écrit(s), %d octets%s" % (nombre, total, " ; %d erreur(s)" % erreurs if erreurs else ""))
    if not a.travail:
        print("Fichiers intermédiaires : %s" % travail)
    sys.exit(1 if erreurs or nombre == 0 else 0)


if __name__ == "__main__":
    main()

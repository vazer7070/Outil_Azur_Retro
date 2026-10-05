#!/usr/bin/env python3
"""Tests de docs2xml.py sur des affectations AS2 écrites ici (aucun SWF ni texte du client).

    python3 tools/client-analysis/test_docs2xml.py
"""

import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ICI)
import docs2xml  # noqa: E402


# Sortie d'as2lite.py pour un document fictif (même forme que data/docs, contenu inventé).
LIVRE = r'''//// DoAction
    this.type = "book";
    this.title = "Carnet d'essai";
    this.subtitle = "Tome unique";
    this.author = "Personne";
    this.style = 2;
    this.pages = new Array();
    this.pages[0] = "<p class='n'>Première page & <b>gras</b></p>";
    this.pages[1] = "<p class='n'>Deuxième page</p>";
    this.chapters = new Array();
    this.chapters[0] = ["Début", 0, false, true];
    this.chapters[1] = ["Hors du livre", 7, true, false];
    r2 = (r2 + 1);
'''

PANCARTE_SANS_PAGE = r'''    this.type = "roadsigndown";
    this.title = "Vers nulle part";
    this.pages = new Array();
    this.chapters = new Array();
    this.chapters[0] = "pas un tableau";
'''


class DocumentXml(unittest.TestCase):
    def test_livre_complet(self):
        racine, ignorees, avert = docs2xml.document_xml(LIVRE.split("\n"), 42, "0102030405", "fr_42_0102030405.swf")
        self.assertEqual(racine.tag, "BotDoc")
        self.assertEqual(racine.get("id"), "42")
        self.assertEqual(racine.get("date"), "0102030405")
        self.assertEqual(racine.get("type"), "book")
        self.assertEqual(racine.get("style"), "2")
        self.assertEqual(racine.get("source"), "fr_42_0102030405.swf")
        self.assertEqual(racine.findtext("titre"), "Carnet d'essai")
        self.assertEqual(racine.findtext("soustitre"), "Tome unique")
        self.assertEqual(racine.findtext("auteur"), "Personne")
        pages = [p.text for p in racine.findall("page")]
        self.assertEqual(pages, ["<p class='n'>Première page & <b>gras</b></p>", "<p class='n'>Deuxième page</p>"])
        chapitres = racine.findall("chapitre")
        self.assertEqual(len(chapitres), 2)
        self.assertEqual((chapitres[0].get("titre"), chapitres[0].get("page"), chapitres[0].get("droite"),
                          chapitres[0].get("titreVisible")), ("Début", "0", "false", "true"))
        self.assertEqual(chapitres[1].get("droite"), "true")
        self.assertEqual(ignorees, 1, "la ligne qui n'est pas une affectation littérale est ignorée")
        self.assertTrue(any("page absente (7)" in a for a in avert), avert)

    def test_type_inconnu_et_aucune_page(self):
        racine, _, avert = docs2xml.document_xml(PANCARTE_SANS_PAGE.split("\n"), 5, "", "x.swf")
        self.assertEqual(racine.get("type"), "book")
        self.assertEqual(racine.get("style"), "1")
        self.assertEqual(racine.findall("page"), [])
        self.assertEqual(racine.findall("chapitre"), [])
        self.assertEqual(racine.findtext("auteur"), "")
        self.assertTrue(any("type inconnu" in a for a in avert), avert)
        self.assertTrue(any("chapitre illisible" in a for a in avert), avert)
        self.assertTrue(any("aucune page" in a for a in avert), avert)

    def test_entree_vide(self):
        racine, ignorees, avert = docs2xml.document_xml([], 1, "", "vide.swf")
        self.assertEqual(racine.findtext("titre"), "")
        self.assertEqual(ignorees, 0)
        self.assertIn("aucune page", avert)

    def test_nom_de_fichier(self):
        m = docs2xml.NOM_SWF.match("fr_139_0612131303.swf")
        self.assertEqual((m.group("langue"), m.group("id"), m.group("date")), ("fr", "139", "0612131303"))
        self.assertIsNone(docs2xml.NOM_SWF.match("fr_139.swf"))
        self.assertIsNone(docs2xml.NOM_SWF.match("fr_139_0612131303.swf.bak"))

    def test_ligne_de_commande_as(self):
        with tempfile.TemporaryDirectory() as d:
            source = os.path.join(d, "livre.as.txt")
            sortie = os.path.join(d, "sous", "42_0102030405.xml")
            with open(source, "w", encoding="utf-8") as f:
                f.write(LIVRE)
            r = subprocess.run([sys.executable, os.path.join(ICI, "docs2xml.py"), "--as", source, "--id", "42",
                                "--date", "0102030405", "--sortie", sortie],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn("2 page(s)", r.stdout)
            relu = ET.parse(sortie).getroot()
            self.assertEqual(relu.get("id"), "42")
            self.assertEqual(len(relu.findall("page")), 2)
            self.assertFalse(os.path.exists(sortie + ".tmp"))
            with open(sortie, "rb") as f:
                self.assertTrue(f.read().startswith(b"<?xml version='1.0' encoding='utf-8'?>"))

    def test_dossier_sans_document(self):
        with tempfile.TemporaryDirectory() as d:
            faux = os.path.join(d, "avm1dump")
            with open(faux, "w") as f:
                f.write("")
            os.makedirs(os.path.join(d, "docs"))
            with open(os.path.join(d, "docs", "notes.txt"), "w") as f:
                f.write("pas un document")
            r = subprocess.run([sys.executable, os.path.join(ICI, "docs2xml.py"), os.path.join(d, "docs"),
                                os.path.join(d, "sortie"), "--avm1dump", faux, "--travail", os.path.join(d, "t")],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)
            self.assertEqual(r.returncode, 1, "aucun document : code de sortie 1")
            self.assertIn("0 document(s)", r.stdout)


if __name__ == "__main__":
    unittest.main()

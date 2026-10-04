#!/usr/bin/env python3
"""Tests de lang2xml.py sur des affectations AS2 écrites ici (aucun SWF ni texte du client).

    python3 tools/client-analysis/test_lang2xml.py
"""

import os
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lang2xml  # noqa: E402


# Sortie d'as2lite.py pour des fichiers de langue fictifs (même forme que le client, contenu inventé).
DIALOGUE = r'''//// DoAction
    FILE_BEGIN = true;
    System.security.allowDomain(_parent._url);
    VERSION = 12;
    D = new Object();
    D.q = new Object();
    D.a = new Object();
    D.q[10] = "Bonjour #1, il te faut #2 kamas.\nAu revoir.";
    D.q[11] = "Texte \"cité\" et barre \\ oblique";
    D.q[11] = "Remplacé : la dernière affectation gagne";
    D.a[20] = "Oui";
    D.a[-3] = "Non";
    r2 = (r2 + 1);
    if (!ip) goto @12;
    FILE_END = true;
'''

PNJ = r'''    VERSION = 7;
    N = new Object();
    N.d = new Object();
    N.a = new Object();
    N.a[1] = "Acheter";
    N.a[3] = "Parler";
    N.d[5] = {"a": [1, 3], "n": "Marchand fictif"};
    N.d[6] = {"n": "Sans action"};
'''

MONSTRES = r'''    M = new Object();
    M[9] = {"g2": {"r": [2, 3, 4, 5, 6, 7, 8], "l": 4}, "g1": {"r": [1, -2, 3, 4, 5, 6, 7], "l": 2}, "k": false, "a": -1, "b": 1, "g": 1500, "n": "Bestiole"};
    MR = new Object();
    MR[1] = {"s": "1", "n": "Race fictive"};
'''

LANGUE = r'''    VERSION = "3";
    ACCEPT = "Accepter";
    INFOS_54 = "Nouvelle quête : <b>%1</b>";
    C = new Object();
    C.LINK = String("http://exemple.invalid");
    C.AUTO = Boolean(false);
    CSR = new Object();
    CSR[4] = {"l": 0, "c": "ok"};
'''

DIVERS = r'''    SRVC = new Object();
    SRVC["1|2"] = "Texte du serveur 2";
    HI = [{"m": 5, "g": 7, "n": "Repère"}, {"m": 6, "g": 8, "n": "Autre"}];
    Q = new Object();
    Q.o = new Object();
    Q.o[1] = {"t": 4, "p": ["Lieu, avec virgule"]};
    Q.o[2] = {"t": 3, "p": [5, 6, 1], "x": null};
    S = new Object();
    S[1] = {"l1": [[[100, 2, 6, null, 0, 0, "1d5+1"]], 4, "PaPa"], "d": "Description", "n": "Sort"};
    T = new Object();
    T.v = 1.5;
    T.e = 1e-05;
    T.c = "\x01contrôle";
'''


def convertir(texte, famille):
    dossier = tempfile.mkdtemp(prefix="test-lang2xml-")
    sortie = os.path.join(dossier, famille + ".xml")
    resultat = lang2xml.convertir_as(texte.split("\n"), famille, "fr", 42, famille + "_fr_42.swf", sortie)
    return ET.parse(sortie).getroot(), resultat


class Lecture(unittest.TestCase):
    def test_affectations_et_lignes_ignorees(self):
        racines, ignorees, version = lang2xml.lire_affectations(DIALOGUE.split("\n"))
        self.assertEqual(version, 12)
        self.assertEqual(racines["D"]["q"]["10"], "Bonjour #1, il te faut #2 kamas.\nAu revoir.")
        self.assertEqual(racines["D"]["q"]["11"], "Remplacé : la dernière affectation gagne")
        self.assertEqual(racines["D"]["a"]["-3"], "Non")
        self.assertNotIn("FILE_BEGIN", racines)
        # allowDomain, r2 = (r2 + 1), if … goto : rien n'est exécuté ni retenu
        self.assertEqual(ignorees, 3)
        self.assertNotIn("r2", racines)

    def test_echappements(self):
        racines, _, _ = lang2xml.lire_affectations(['D.q[1] = "a \\"b\\" \\\\ c\\td\\x41";'])
        self.assertEqual(racines["D"]["q"]["1"], 'a "b" \\ c\tdA')

    def test_valeurs_imbriquees(self):
        racines, ignorees, _ = lang2xml.lire_affectations(DIVERS.split("\n"))
        self.assertEqual(ignorees, 0)
        self.assertEqual(racines["SRVC"]["1|2"], "Texte du serveur 2")
        self.assertEqual(racines["HI"][1]["n"], "Autre")
        self.assertEqual(racines["S"]["1"]["l1"][0][0][3], None)
        self.assertEqual(racines["T"]["v"], 1.5)

    def test_non_litteraux(self):
        for ligne in ("api = _root.mcModules.x;", "var ip = 5;", "X = f(1);", 'X = "non terminé;', "X = [1, 2;"):
            racines, ignorees, _ = lang2xml.lire_affectations([ligne])
            self.assertEqual((racines, ignorees), ({}, 1), ligne)


class Export(unittest.TestCase):
    def test_dialogue(self):
        racine, r = convertir(DIALOGUE, "dialog")
        self.assertEqual((racine.tag, racine.get("famille"), racine.get("version"), racine.get("source")),
                         ("BotLang", "dialog", "42", "dialog_fr_42.swf"))
        questions = racine.findall("question")
        self.assertEqual([q.get("id") for q in questions], ["10", "11"])
        self.assertEqual(questions[0].get("texte"), "Bonjour #1, il te faut #2 kamas.\nAu revoir.")
        self.assertEqual([a.get("id") for a in racine.findall("reponse")], ["-3", "20"])
        self.assertEqual(r["comptes"], {"D.q": 2, "D.a": 2})
        self.assertEqual(r["version_interne"], 12)

    def test_pnj(self):
        racine, _ = convertir(PNJ, "npc")
        self.assertEqual(racine.find("action[@id='3']").get("nom"), "Parler")
        pnj = racine.find("pnj[@id='5']")
        self.assertEqual((pnj.get("nom"), pnj.get("actions")), ("Marchand fictif", "1,3"))
        self.assertIsNone(racine.find("pnj[@id='6']").get("actions"))

    def test_monstres_grades(self):
        racine, _ = convertir(MONSTRES, "monsters")
        m = racine.find("monstre[@id='9']")
        self.assertEqual((m.get("nom"), m.get("gfx"), m.get("race"), m.get("expulsable")), ("Bestiole", "1500", "1", "false"))
        grades = {g.get("n"): g for g in m.findall("grade")}
        self.assertEqual(sorted(grades), ["1", "2"])
        self.assertEqual((grades["1"].get("niveau"), grades["1"].get("resistances")), ("2", "1,-2,3,4,5,6,7"))
        self.assertEqual(racine.find("race[@id='1']").get("superRace"), "1")

    def test_langue(self):
        racine, r = convertir(LANGUE, "lang")
        textes = {t.get("cle"): t.get("valeur") for t in racine.findall("texte")}
        self.assertEqual(textes, {"ACCEPT": "Accepter", "INFOS_54": "Nouvelle quête : <b>%1</b>"})
        configs = {c.get("cle"): c.get("valeur") for c in racine.findall("config")}
        self.assertEqual(configs, {"LINK": "http://exemple.invalid", "AUTO": "false"})
        self.assertEqual(racine.find("entree[@table='CSR']").get("c"), "ok")
        self.assertIn("table COM absente", r["avertissements"])

    def test_quetes_parametres(self):
        racine, _ = convertir(DIVERS, "quests")
        self.assertEqual(racine.find("objectif[@id='1']").get("parametres"), '["Lieu, avec virgule"]')
        o2 = racine.find("objectif[@id='2']")
        self.assertEqual((o2.get("type"), o2.get("parametres"), o2.get("x")), ("3", "5,6,1", None))

    def test_sorts_sans_niveaux(self):
        racine, _ = convertir(DIVERS, "spells")
        s = racine.find("sort[@id='1']")
        self.assertEqual(dict(s.attrib), {"id": "1", "nom": "Sort", "description": "Description"})

    def test_famille_generique_et_caracteres_invalides(self):
        lang2xml.FAMILLES["essai"] = ["HI", "T", "SRVC"]
        try:
            racine, r = convertir(DIVERS, "essai")
        finally:
            del lang2xml.FAMILLES["essai"]
        self.assertEqual([e.get("id") for e in racine.findall("entree[@table='HI']")], ["0", "1"])
        self.assertEqual(racine.find("entree[@table='T'][@id='v']").get("valeur"), "1.5")
        self.assertEqual(racine.find("entree[@table='T'][@id='e']").get("valeur"), "1e-05")
        self.assertEqual(racine.find("entree[@table='T'][@id='c']").get("valeur"), "\uFFFDcontrôle")
        self.assertEqual(r["remplaces"], 1)
        self.assertEqual(racine.find("entree[@table='SRVC']").get("id"), "1|2")


class Fichiers(unittest.TestCase):
    def test_choix_de_version(self):
        dossier = tempfile.mkdtemp(prefix="test-lang2xml-swf-")
        swf = os.path.join(dossier, "swf")
        os.makedirs(swf)
        for nom in ("npc_fr_3.swf", "npc_fr_12.swf", "npc_fr_undefined.swf", "dialog_fr_4.swf"):
            open(os.path.join(swf, nom), "wb").close()
        self.assertEqual(lang2xml.choisir_swf(swf, "npc", "fr", {})[1], 12)
        with open(os.path.join(dossier, "versions_fr.txt"), "w", encoding="utf-8") as f:
            f.write("&f=npc,fr,3|dialog,fr,9|")
        versions, _ = lang2xml.lire_versions(swf, "fr")
        self.assertEqual(versions, {"npc": 3, "dialog": 9})
        self.assertEqual(lang2xml.choisir_swf(swf, "npc", "fr", versions)[1], 3)
        # version annoncée absente : la plus haute présente
        self.assertEqual(lang2xml.choisir_swf(swf, "dialog", "fr", versions)[1], 4)
        self.assertEqual(lang2xml.choisir_swf(swf, "items", "fr", versions), (None, None))


if __name__ == "__main__":
    unittest.main(verbosity=2)

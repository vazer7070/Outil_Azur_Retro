#!/usr/bin/env python3
"""Tests de choisir_gfx_animes.py sur un mini dump SQL de démonstration (données inventées) et un faux
swfsvg (faux_swfsvg.py) : aucun fichier du client ni du serveur.

Lancement : python3 tools/client-analysis/tests/test_choisir_gfx_animes.py"""
import json, os, shutil, subprocess, sys, tempfile, unittest

ICI = os.path.dirname(os.path.abspath(__file__))
OUTIL = os.path.join(ICI, "..", "choisir_gfx_animes.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")
sys.path.insert(0, os.path.join(ICI, ".."))
import choisir_gfx_animes as cga  # noqa: E402

# Dump de démonstration au format de StarLoco (colonnes dans un ordre quelconque, comme le vrai).
# Monstres : 1 (gfx 500, niveaux 1 et 2 ; le « 3@ » final n'a pas de résistances), 2 (gfx 600,
# niveau 5), 3 (gfx 500, niveau 7), 4 (gfx 700, niveaux 1 et 2, statistiques du seul premier rang :
# le niveau 2 est ignoré),
# 5 (gfx 800, niveaux 1 et 2, sorts au premier rang seulement : seul le niveau 1 est chargé),
# 6 (gfx 900, niveau 4, nom avec apostrophes échappées), 7 (gfx 450, niveau 9).
DUMP = r"""-- Dump de démonstration
CREATE TABLE `maps` (
  `id` int(11) NOT NULL,
  `date` varchar(50) NOT NULL,
  `monsters` text NOT NULL,
  `numgroup` int(11) NOT NULL DEFAULT 3,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8;
INSERT INTO `maps` VALUES ('10', '0101', '1,1|1,2|2,5', '3');
INSERT INTO `maps` VALUES ('11', '0101', '1,1|3,7|2,9|99,1|0,1|1,0|', '2'), ('12', '0101', '2,5|2,5|5,1|5,2|4,2', '1');
INSERT INTO `maps` VALUES ('13', '0101', '1,1|1,1|1,1|1,1', '0');
INSERT INTO `maps` VALUES ('14', 'bad', 'abc,1|2,x|2', '1');
INSERT INTO `maps` VALUES ('15', '0101', '6,4|7,9', '1');
INSERT INTO `maps` VALUES ('16', '0101', '');
CREATE TABLE `monsters` (
  `id` int(11) NOT NULL,
  `name` varchar(100) NOT NULL,
  `gfxID` int(11) NOT NULL,
  `grades` text NOT NULL,
  `stats` text NOT NULL,
  `spells` text NOT NULL
) ENGINE=MyISAM DEFAULT CHARSET=utf8;
INSERT INTO `monsters` VALUES ('1', 'Bestiole', '500', '1@5;5|2@6;6|3@', '1,1|2,2|3,3', '||||');
INSERT INTO `monsters` VALUES ('2', 'Chose', '600', '5@1;1', '1,1', '-1');
INSERT INTO `monsters` VALUES ('3', 'Bestiole rouge', '500', '7@1;1', '1,1', '');
INSERT INTO `monsters` VALUES ('4', 'Stats courtes', '700', '1@1;1|2@2;2', '1,1', '||||');
INSERT INTO `monsters` VALUES ('5', 'Deux grades', '800', '1@1;1|2@2;2', '1,1|2,2', '10@1');
INSERT INTO `monsters` VALUES ('6', 'L\'écureuil d''or (, ; |)', '900', '4@1;1', '1,1', '||||');
INSERT INTO `monsters` VALUES ('7', 'Seul', '450', '9@1;1', '1,1', '||||');
CREATE TABLE `mobgroups_fix` (
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `groupData` varchar(200) NOT NULL
) ENGINE=MyISAM DEFAULT CHARSET=utf8;
INSERT INTO `mobgroups_fix` VALUES ('15', '100', '2,1,9;2,1,9;7,1,3;99,1,9;1,1,1;');
INSERT INTO `mobgroups_fix` VALUES ('404', '100', '2,1,9;');
"""
# Présences attendues : 500 = 2 (carte 10) + 2 (carte 11 : 1,1 et 3,7) + 1 (fixe 15 : 1,1,1) = 5
# 600 = 1 (10) + 2 (12) + 2 (fixe 15) = 5 ; 800 = 1 (12 : 5,1) ; 900 = 1 (15) ; 450 = 1 (15 : 7,9 ; le fixe 7,1,3 n'a pas de grade).
ATTENDU = {500: 5, 600: 5, 800: 1, 900: 1, 450: 1}
ATTENDU_SANS_FIXES = {500: 4, 600: 3, 800: 1, 900: 1, 450: 1}


class ChoisirGfxAnimes(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="choisir-gfx-")
        self.dump = os.path.join(self.dossier, "game.sql")
        with open(self.dump, "w", encoding="utf-8") as f:
            f.write(DUMP)
        self.sprites = os.path.join(self.dossier, "sprites")
        os.makedirs(self.sprites)
        # 500 : tout ; 600 : sans run, die d'une image ; 800 : hit de 2 images (1 au pas 2) seulement ; 900 : pas de SWF ;
        # 450 : walk en R seulement.
        self.swf("500", {"staticR": 1, "walkR": 10, "walkL": 10, "runR": 6, "hitR": 4, "hitL": 4, "dieR": 9, "anim0R": 3})
        self.swf("600", {"staticR": 1, "walkR": 8, "hitR": 5, "dieR": 1, "anim0L": 4})
        self.swf("800", {"staticR": 1, "hitR": 2})
        self.swf("450", {"staticR": 1, "walkR": 5})

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def swf(self, gfx, symboles):
        with open(os.path.join(self.sprites, gfx + ".swf"), "w", encoding="utf-8") as f:
            json.dump({"symboles": symboles}, f)

    def lancer(self, *options, ok=True):
        r = subprocess.run([sys.executable, OUTIL, self.dump] + list(options), capture_output=True, encoding="utf-8")
        if ok:
            self.assertEqual(r.returncode, 0, r.stderr)
        return r

    @staticmethod
    def lignes(sortie):
        return [l for l in sortie.splitlines() if l and not l.startswith("#")]

    def test_java_split_et_valeurs(self):
        self.assertEqual(cga.java_split("a|b||", "|"), ["a", "b"])
        self.assertEqual(cga.java_split("|", "|"), [])
        self.assertEqual(cga.java_split("", "|"), [""])
        self.assertEqual(cga.java_split("3@", "@"), ["3"])
        ligne = "INSERT INTO `t` VALUES ('1', 'a\\'b''c', NULL, 7), ('2', '(,;)', '', -3);"
        self.assertEqual(cga.valeurs_sql(ligne, ligne.index("(")),
                         [["1", "a'b'c", None, "7"], ["2", "(,;)", "", "-3"]])
        for mauvaise in ("('1', 'a'", "('1' 'a');", "('1');x", "'1');", "('1')"):
            with self.assertRaises(ValueError):
                cga.valeurs_sql(mauvaise, 0)

    def test_niveaux_comme_starloco(self):
        tables = cga.lire_tables(self.dump, ("maps", "monsters", "mobgroups_fix"))
        par_id = {m["id"]: m for m in tables["monsters"]}
        self.assertEqual(cga.niveaux(par_id["1"]), {1, 2})
        self.assertEqual(cga.niveaux(par_id["4"]), {1})
        self.assertEqual(cga.niveaux(par_id["5"]), {1})
        self.assertEqual(par_id["6"]["name"], "L'écureuil d'or (, ; |)")
        self.assertEqual(len(tables["maps"]), 6)

    def test_presences(self):
        avertissements = []
        tables = cga.lire_tables(self.dump, ("maps", "monsters", "mobgroups_fix"), avertissements.append)
        compte, cartes = cga.presences(tables)
        self.assertEqual(dict(compte), ATTENDU)
        self.assertEqual(cartes[500], 3)
        self.assertEqual(cartes[600], 3)
        compte, _ = cga.presences(tables, fixes=False)
        self.assertEqual(dict(compte), ATTENDU_SANS_FIXES)
        # La carte 16 n'a pas toutes ses colonnes : signalée, ignorée.
        self.assertTrue(any("3 valeurs pour 4 colonnes" in a for a in avertissements), avertissements)

    def test_sortie_sans_client(self):
        r = self.lancer("--nombre", "2")
        sortie = r.stdout
        # Égalité 500/600 à 5 présences : gfx croissant ; lignes triées par gfx ; pas 2 par défaut.
        self.assertEqual(self.lignes(sortie), ["500 walk:2,run:2,hit:2,die:2,anim0:2", "600 walk:2,run:2,hit:2,die:2,anim0:2"])
        self.assertIn("2 gfx de monstres sur 5 présents, 76.9 % des 13 présences", sortie)
        self.assertIn("arrêt : 2 gfx atteints", sortie)
        self.assertIn("3 valeurs pour 4 colonnes", r.stderr)
        r = self.lancer("--pas", "1", "--familles", "walk,hit", "--nombre", "1", "--sans-fixes")
        self.assertEqual(self.lignes(r.stdout), ["500 walk,hit"])
        self.assertIn("1 gfx de monstres sur 5 présents, 40.0 % des 10 présences (entrées de maps.monsters)", r.stdout)

    def test_familles_reelles_et_budget(self):
        r = self.lancer("--sprites", self.sprites, "--swfsvg", FAUX, "--tableau", os.path.join(self.dossier, "t.tsv"))
        sortie = r.stdout
        # 900 n'a pas de SWF ; 800 n'a qu'un hit d'une image au pas 2 ; 600 perd run et die (une image).
        self.assertEqual(self.lignes(sortie), ["450 walk:2", "500 walk:2,run:2,hit:2,die:2,anim0:2", "600 walk:2,hit:2,anim0:2"])
        self.assertIn("# écarté 900 : SWF absent ou illisible", sortie)
        self.assertIn("# écarté 800 : aucune des familles walk,run,hit,die,anim0", sortie)
        # Images exportées au pas 2 : 500 = 5+5+3+2+2+5+2 = 24 ; 600 = 4+3+2 = 9 ; 450 = 3 → 36 × 2,5 Kio.
        self.assertIn("taille estimée : 0.1 Mio", sortie)
        with open(os.path.join(self.dossier, "t.tsv"), encoding="utf-8") as f:
            tableau = f.read().splitlines()
        self.assertEqual(tableau[0], "rang\tgfx\tpresences\tcartes\tpart\tcumul")
        self.assertEqual(tableau[1].split("\t")[:4], ["1", "500", "5", "3"])
        self.assertEqual(tableau[-1].split("\t")[5], "1.0000")
        # Budget : 24 images de 500 = 60 Kio passent sous 0,07 Mio, pas les 22,5 Kio de 600 en plus.
        r = self.lancer("--sprites", self.sprites, "--swfsvg", FAUX, "--budget-mo", "0.07")
        self.assertEqual(self.lignes(r.stdout), ["500 walk:2,run:2,hit:2,die:2,anim0:2"])
        self.assertIn("budget de 0.07 Mio atteint (gfx 600", r.stdout)

    def test_exclure(self):
        animes = os.path.join(self.dossier, "sprites_animes.txt")
        with open(animes, "w", encoding="utf-8") as f:
            f.write("# commentaire\n500 walk,run   # classe\n10\n")
        r = self.lancer("--exclure", animes, "--nombre", "1")
        self.assertEqual(self.lignes(r.stdout), ["600 walk:2,run:2,hit:2,die:2,anim0:2"])
        self.assertIn("# écarté 500 : déjà dans sprites_animes.txt", r.stdout)

    def test_erreurs(self):
        self.assertNotEqual(self.lancer("--budget-mo", "5", ok=False).returncode, 0)
        self.assertNotEqual(self.lancer("--pas", "3", ok=False).returncode, 0)
        self.assertNotEqual(self.lancer("--familles", "walk,static", ok=False).returncode, 0)
        vide = os.path.join(self.dossier, "vide.sql")
        with open(vide, "w", encoding="utf-8") as f:
            f.write("-- rien\n")
        r = subprocess.run([sys.executable, OUTIL, vide], capture_output=True, encoding="utf-8")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("tables absentes", r.stderr)


if __name__ == "__main__":
    unittest.main()

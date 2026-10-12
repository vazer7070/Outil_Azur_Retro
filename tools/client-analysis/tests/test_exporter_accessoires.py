#!/usr/bin/env python3
"""Tests de exporter_accessoires.py avec un faux swfsvg (faux_swfsvg.py) : aucun fichier du client.

Lancement : python3 -I tools/client-analysis/tests/test_exporter_accessoires.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
EXPORTEUR = os.path.join(ICI, "..", "exporter_accessoires.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")
EN_TETE = "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin"


class ExporterAccessoires(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-accessoires-")
        self.accessoires = os.path.join(self.dossier, "accessories")
        self.sortie = os.path.join(self.dossier, "Accessoires")
        os.makedirs(self.accessoires)
        # a1 : deux coiffes et un symbole du client qui n'est pas un accessoire ; a2 : une cape à deux images ;
        # a3 : un familier qui marche (WL, 4 images) et son arrêt ; a4 et a5 : vides.
        self.swf("a1", {"16_2": 1, "16_7": 1, "CapRunR-133": 1},
                 {"16_2": [["R", 1, 1, "arret"], ["L", 4, 3, "boucle"]], "16_7": [["S", 1, 1, "arret"]],
                  "CapRunR-133": [["R", 1, 1, "arret"]]})
        self.swf("a2", {"17_5": 1}, {"17_5": [["R", 1, 2, "boucle"]]})
        self.swf("a3", {"18_61": 1}, {"18_61": [["WL", 1, 4, "boucle"], ["S", 5, 1, "arret"]]})
        self.swf("a4", {}, {})
        self.swf("a5", {}, {})

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def swf(self, nom, symboles, etiquettes):
        with open(os.path.join(self.accessoires, nom + ".swf"), "w", encoding="utf-8") as f:
            json.dump({"symboles": symboles, "etiquettes": etiquettes}, f)

    def exporter(self, *options, ok=True):
        r = subprocess.run([sys.executable, EXPORTEUR, self.accessoires, self.sortie, "--swfsvg", FAUX, "--jobs", "2"]
                           + list(options), capture_output=True, encoding="utf-8")
        if ok:
            self.assertEqual(r.returncode, 0, r.stderr)
            return r.stdout
        return r

    def ancres(self):
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        self.assertEqual(lignes[0], EN_TETE)
        return lignes[1:]

    def pngs(self):
        return sorted(f for f in os.listdir(self.sortie) if f.endswith(".png"))

    def test_export_complet(self):
        sortie = self.exporter("--pas", "2")
        self.assertEqual(self.pngs(), ["16_2_L.png", "16_2_R.png", "16_7_S.png", "17_5_R.png", "18_61_S.png", "18_61_WL.png"])
        # Triées par type, gfx puis étiquette ; une image sur deux des animations tenues ; une seule image -> arret.
        self.assertEqual(self.ancres(), [
            "16_2\tL\t-5\t-25\t6\t25\t2\t20\tboucle",
            "16_2\tR\t-5\t-25\t4\t25\t1\t20\tarret",
            "16_7\tS\t-5\t-25\t4\t25\t1\t20\tarret",
            "17_5\tR\t-5\t-25\t4\t25\t1\t20\tarret",
            "18_61\tS\t-5\t-25\t4\t25\t1\t20\tarret",
            "18_61\tWL\t-5\t-25\t6\t25\t2\t20\tboucle"])
        with Image.open(os.path.join(self.sortie, "16_2_L.png")) as im:
            self.assertEqual(im.size, (12, 25))
            im = im.convert("RGBA")
            self.assertEqual(im.getpixel((0, 0)), (0x80, 0x40, 0x20, 255))   # image 1 : bord gauche en x = -5
            self.assertEqual(im.getpixel((5, 0))[3], 0)                       # image 1 : rien au-delà de x = -1
            self.assertEqual(im.getpixel((6 + 2, 0)), (0x80, 0x40, 0x20, 255))  # image 3 : bord gauche en x = -3
        with Image.open(os.path.join(self.sortie, "16_2_R.png")) as im:
            self.assertEqual(im.size, (4, 25))
            self.assertNotIn((255, 0, 255, 255), {c for _, c in im.convert("RGBA").getcolors(1 << 16)})  # magenta effacé
        self.assertIn("4 symboles, 6 PNG", sortie)
        self.assertIn("6 lignes", sortie)

    def test_export_partiel(self):
        self.exporter()
        self.assertEqual(len(self.ancres()), 6)
        with open(os.path.join(self.sortie, "16_9_R.png"), "wb") as f:
            f.write(b"vieux")
        # 16_2 perd son étiquette L : --symboles ne touche qu'à 16_2, les autres lignes et PNG restent.
        self.swf("a1", {"16_2": 1, "16_7": 1}, {"16_2": [["R", 1, 1, "arret"]], "16_7": [["F", 1, 1, "arret"]]})
        self.exporter("--symboles", "16_2")
        self.assertEqual(self.pngs(), ["16_2_R.png", "16_7_S.png", "16_9_R.png", "17_5_R.png", "18_61_S.png", "18_61_WL.png"])
        self.assertEqual([l.split("\t")[:2] for l in self.ancres()],
                         [["16_2", "R"], ["16_7", "S"], ["17_5", "R"], ["18_61", "S"], ["18_61", "WL"]])
        self.assertEqual(self.ancres()[4], "18_61\tWL\t-5\t-25\t7\t25\t4\t40\tboucle")
        # --types 16 refait les coiffes (16_7 passe de S à F) sans toucher aux autres familles.
        self.exporter("--types", "16")
        self.assertEqual(self.pngs(), ["16_2_R.png", "16_7_F.png", "16_9_R.png", "17_5_R.png", "18_61_S.png", "18_61_WL.png"])
        # --fichiers limite aussi l'export : partiel, les PNG orphelins des autres SWF restent.
        self.exporter("--fichiers", "a3", "--pas", "4")
        self.assertEqual(self.ancres()[4], "18_61\tWL\t-5\t-25\t4\t25\t1\t10\tarret")
        self.assertIn("16_9_R.png", self.pngs())
        # Un export complet retire les PNG orphelins et les symboles disparus.
        self.exporter()
        self.assertEqual(self.pngs(), ["16_2_R.png", "16_7_F.png", "17_5_R.png", "18_61_S.png", "18_61_WL.png"])

    def test_erreurs(self):
        os.remove(os.path.join(self.accessoires, "a5.swf"))
        r = self.exporter(ok=False)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("a5", r.stderr)
        r = self.exporter("--fichiers", "a1", "--pas", "3", ok=False)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("--pas", r.stderr)
        r = self.exporter("--fichiers", "a4", ok=False)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("aucun symbole", r.stderr)
        # Un SWF illisible dans la série est signalé, les autres sont exportés.
        with open(os.path.join(self.accessoires, "a1.swf"), "w") as f:
            f.write("pas un SWF")
        sortie = self.exporter("--fichiers", "a1,a2")
        self.assertIn("a1", sortie)
        self.assertEqual(self.pngs(), ["17_5_R.png"])


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Tests de exporter_sprites.py avec un faux swfsvg (faux_swfsvg.py) : aucun fichier du client.

Lancement : python3 tools/client-analysis/tests/test_exporter_sprites.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
EXPORTEUR = os.path.join(ICI, "..", "exporter_sprites.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")
EN_TETE = "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages"


class ExporterSprites(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-sprites-")
        self.sprites = os.path.join(self.dossier, "sprites")
        self.sortie = os.path.join(self.dossier, "sortie")
        os.makedirs(self.sprites)
        os.makedirs(self.sortie)
        # 10 : classe animée ; 11 : mêmes cycles mais non listé ; 7 : épée (scène seule) ;
        # 8 : ni static ni scène ; 9 : static vide ; 12 : SWF illisible.
        self.swf("10", {"staticR": 3, "StaticL": 1, "staticF": 2, "walkR": 4, "runR": 2, "hitR": 5}, vides_images={"staticF": [2]})
        self.swf("11", {"staticR": 1, "walkR": 4})
        self.swf("7", {"circle": 1}, scene=True)
        self.swf("8", {"circle": 1}, scene=False)
        self.swf("9", {"staticB": 3}, vides=["staticB"])
        with open(os.path.join(self.sprites, "12.swf"), "w") as f:
            f.write("pas un SWF")
        with open(os.path.join(self.sortie, "sprites_animes.txt"), "w", encoding="utf-8") as f:
            f.write("# commentaire\n10  # classe\n")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def swf(self, gfx, symboles, scene=False, vides=(), vides_images=None):
        with open(os.path.join(self.sprites, gfx + ".swf"), "w", encoding="utf-8") as f:
            json.dump({"symboles": symboles, "scene": scene, "vides": list(vides), "vides_images": vides_images or {}}, f)

    def exporter(self, *options):
        r = subprocess.run([sys.executable, EXPORTEUR, self.sprites, self.sortie, "--swfsvg", FAUX, "--jobs", "2"] + list(options),
                           capture_output=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stderr)
        return r.stdout

    def ancres(self):
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        self.assertEqual(lignes[0], EN_TETE)
        return {(c[0], c[1]): tuple(int(v) for v in c[2:]) for c in (l.split("\t") for l in lignes[1:])}

    def png(self, nom):
        return Image.open(os.path.join(self.sortie, nom)).convert("RGBA")

    def test_export_complet(self):
        sortie = self.exporter()
        a = self.ancres()
        self.assertEqual(set(a), {("7", "scene"), ("10", "runR"), ("10", "staticF"), ("10", "staticL"), ("10", "staticR"), ("10", "walkR"), ("11", "staticR")})
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            ordre = [l.split("\t")[0] for l in f.read().splitlines()[1:]]
        self.assertEqual(ordre, ["7", "10", "10", "10", "10", "10", "11"], "tri numérique des gfx")
        # Statique : dernière image utile (3) ; rognage au rectangle, magenta effacé, pied en (0, 0).
        self.assertEqual(a[("10", "staticR")], (-3, -25, 4, 25, 1))
        self.assertEqual(a[("10", "staticL")], (-5, -25, 4, 25, 1), "StaticL (casse différente) exporté en staticL")
        self.assertEqual(a[("10", "staticF")], (-5, -25, 4, 25, 1), "dernière image vide : image 1")
        self.assertIn("staticF vide à l'image 2, image 1 retenue", sortie)
        self.assertIn("9 : staticB vide, rien d'exporté", sortie)
        im = self.png("10_staticR.png")
        self.assertEqual(im.size, (4, 25))
        px = im.tobytes()
        self.assertFalse(any(px[i + 3] and px[i] > 235 and px[i + 1] < 25 and px[i + 2] > 235 for i in range(0, len(px), 4)), "magenta restant")
        # Bande : 4 images de 7 pixels (union des cadres), le rectangle avance d'un pixel par image.
        self.assertEqual(a[("10", "walkR")], (-5, -25, 7, 25, 4))
        bande = self.png("10_walkR.png")
        self.assertEqual(bande.size, (28, 25))
        for k in range(4):
            colonnes = [x for x in range(7) if bande.getpixel((k * 7 + x, 12))[3] > 0]
            self.assertEqual(colonnes, [k, k + 1, k + 2, k + 3], "image %d" % (k + 1))
        self.assertEqual(a[("10", "runR")][4], 2)
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "11_walkR.png")), "11 n'est pas dans sprites_animes.txt")
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "10_hitR.png")))
        # Scène quand aucun static<O> : l'épée ; rien pour 8, 9 et 12, avec un message chacun.
        self.assertEqual(a[("7", "scene")], (-5, -25, 4, 25, 1))
        for gfx in ("8 :", "9 :", "12 :"):
            self.assertIn(gfx, sortie)

    def test_reexport_partiel(self):
        self.exporter()
        avant = self.ancres()
        # Anciennes images : <gfx><O>.png reste, un ancien <gfx>_static<O>.png sans source disparaît.
        Image.new("RGBA", (2, 2)).save(os.path.join(self.sortie, "7R.png"))
        Image.new("RGBA", (2, 2)).save(os.path.join(self.sortie, "7_staticR.png"))
        Image.new("RGBA", (2, 2)).save(os.path.join(self.sortie, "11_staticB.png"))
        self.swf("7", {"circle": 1}, scene=True)
        self.exporter("--gfx", "7")
        apres = self.ancres()
        self.assertEqual(apres, avant, "les lignes des autres gfx sont gardées")
        self.assertTrue(os.path.exists(os.path.join(self.sortie, "7R.png")))
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "7_staticR.png")))
        self.assertTrue(os.path.exists(os.path.join(self.sortie, "11_staticB.png")), "un gfx non réexporté n'est pas nettoyé")
        r = subprocess.run([sys.executable, EXPORTEUR, self.sprites, self.sortie, "--swfsvg", FAUX, "--gfx", "404"],
                           capture_output=True, encoding="utf-8")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("404", r.stderr)

    def test_echelle_2(self):
        self.exporter("--echelle", "2", "--gfx", "10")
        self.assertEqual(self.ancres()[("10", "staticR")], (-6, -50, 8, 50, 1))
        self.assertEqual(self.png("10_walkR.png").size, (56, 50))


if __name__ == "__main__":
    unittest.main()

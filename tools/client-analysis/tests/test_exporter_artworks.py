#!/usr/bin/env python3
"""Tests de exporter_artworks.py avec un faux swfsvg (faux_swfsvg.py) : aucun fichier du client.

Lancement : python3 tools/client-analysis/tests/test_exporter_artworks.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
EXPORTEUR = os.path.join(ICI, "..", "exporter_artworks.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")


class ExporterArtworks(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-artworks-")
        self.client = os.path.join(self.dossier, "client")
        self.sortie = os.path.join(self.dossier, "Bot")
        faces = os.path.join(self.client, "clips", "artworks", "faces")
        os.makedirs(faces)
        # 10 : buste dessiné ; 11 : scène vide ; 20 : SWF illisible ; 21 absent ; 7 (hors classes) ignoré.
        for gfx, scene in (("10", True), ("11", False), ("7", True)):
            with open(os.path.join(faces, gfx + ".swf"), "w", encoding="utf-8") as f:
                json.dump({"symboles": {}, "scene": scene}, f)
        with open(os.path.join(faces, "20.swf"), "w") as f:
            f.write("pas un SWF")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def exporter(self, *options):
        return subprocess.run([sys.executable, EXPORTEUR, self.client, self.sortie, "--swfsvg", FAUX] + list(options),
                              capture_output=True, encoding="utf-8")

    def test_bustes_des_classes(self):
        r = self.exporter()
        self.assertEqual(r.returncode, 0, r.stderr)
        dossier = os.path.join(self.sortie, "Artworks", "Faces")
        self.assertEqual(sorted(os.listdir(dossier)), ["10.png"])
        self.assertIn("1 PNG écrits", r.stdout)
        self.assertIn("faces/11.swf : rendu vide", r.stdout.replace(os.sep, "/"))
        self.assertIn("faces/20.swf : swfsvg a échoué", r.stdout.replace(os.sep, "/"))
        self.assertIn("faces/21.swf : absent", r.stdout.replace(os.sep, "/"))
        self.assertNotIn("/7.swf", r.stdout.replace(os.sep, "/"))
        with Image.open(os.path.join(dossier, "10.png")) as source:
            image = source.convert("RGBA")
        # Rectangle brun de 4 × 25 du faux swfsvg, marges et carré magenta retirés.
        self.assertEqual(image.size, (4, 25))
        pixels = [image.getpixel((x, y)) for y in range(image.height) for x in range(image.width)]
        self.assertTrue(all(pixel[3] == 255 for pixel in pixels))
        self.assertFalse(any(pixel[:3] == (255, 0, 255) for pixel in pixels))

    def test_sans_palette_et_travail(self):
        travail = os.path.join(self.dossier, "travail")
        r = self.exporter("--sans-palette", "--travail", travail)
        self.assertEqual(r.returncode, 0, r.stderr)
        with Image.open(os.path.join(self.sortie, "Artworks", "Faces", "10.png")) as image:
            self.assertEqual(image.mode, "RGBA")
        self.assertTrue(os.path.isfile(os.path.join(travail, "10.svg")))

    def test_aucun_buste(self):
        shutil.rmtree(os.path.join(self.client, "clips"))
        r = self.exporter()
        self.assertEqual(r.returncode, 1)
        self.assertIn("0 PNG écrits", r.stdout)

    def test_swfsvg_introuvable(self):
        r = subprocess.run([sys.executable, EXPORTEUR, self.client, self.sortie, "--swfsvg", os.path.join(self.dossier, "absent")],
                           capture_output=True, encoding="utf-8")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("swfsvg introuvable", r.stderr)


if __name__ == "__main__":
    unittest.main()

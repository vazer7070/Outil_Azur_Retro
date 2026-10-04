#!/usr/bin/env python3
"""Tests de exporter_etats_interactifs.py avec un faux swfsvg écrit ici : aucun fichier du client.

Lancement : python3 tools/client-analysis/tests/test_exporter_etats_interactifs.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(ICI, ".."))
import exporter_etats_interactifs as etats  # noqa: E402

EXPORTEUR = os.path.join(ICI, "..", "exporter_etats_interactifs.py")

# Faux swfsvg : le « SWF » est un JSON {nom d'export : images de sa timeline}. L'image N d'un symbole est un
# rectangle de 6 × 4 pixels dont la couleur dépend de N, posé en (-3, -4) dans un cadre (-5, -6, 10, 8), plus un
# pixel magenta (couleur technique du client) qui doit devenir transparent.
FAUX = r'''import json, os, sys
args = sys.argv[1:]
if args[0] == "--list":
    symboles = json.load(open(args[1], encoding="utf-8"))
    print("nom\tid\ttype\timages\timages_timeline")
    print("scene\t0\tscene\t1\t1")
    for k, (nom, images) in enumerate(symboles.items()):
        print("%s\t%d\tclip\t%d\t%d" % (nom, k + 1, images * 9, images))
    sys.exit(0)
swf, dossier, reste = args[0], args[1], args[2:]
image = int(reste[reste.index("--frame") + 1]) if "--frame" in reste else 1
noms = [n for i, n in enumerate(reste) if n != "--frame" and (i == 0 or reste[i - 1] != "--frame")]
symboles = json.load(open(swf, encoding="utf-8"))
os.makedirs(dossier, exist_ok=True)
with open(os.path.join(dossier, "index.tsv"), "w", encoding="utf-8") as index:
    for nom in noms:
        if nom not in symboles:
            continue
        couleur = "#%02x2040" % (40 * image)
        with open(os.path.join(dossier, nom + ".svg"), "w", encoding="utf-8") as f:
            f.write('<svg xmlns="http://www.w3.org/2000/svg" width="10" height="8" viewBox="-5 -6 10 8">'
                    '<rect x="-3" y="-4" width="6" height="4" fill="%s"/><rect x="2" y="-4" width="1" height="1" fill="#ff00ff"/></svg>' % couleur)
        index.write("%s\t1\t-5\t-6\t10\t8\t\n" % nom)
'''


class ExporterEtats(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-etats-")
        self.client = os.path.join(self.dossier, "client")
        self.decor = os.path.join(self.dossier, "decor")
        gfx = os.path.join(self.client, "clips", "gfx")
        os.makedirs(gfx)
        os.makedirs(os.path.join(self.decor, "objets"))
        # 10 : interactif, 3 images dans o1 (5 dans o2, ignorées) ; 11 : interactif d'une image ; 12 : décor non
        # interactif ; 13 : interactif seulement dans o2 ; 14 : interactif absent des bibliothèques.
        self.swf(gfx, "o1", {"10": 3, "11": 1, "12": 4, "Link_o1": 2})
        self.swf(gfx, "o2", {"10": 5, "13": 2})
        self.interactifs = os.path.join(self.dossier, "interactiveobjects.xml")
        with open(self.interactifs, "w", encoding="utf-8") as f:
            f.write('<?xml version="1.0" encoding="utf-8"?>\n<BotLang famille="interactiveobjects">\n'
                    '<interactif id="1" nom="Objet fictif" type="1" competences="6" />\n'
                    '<gfx id="10" interactif="1" />\n<gfx id="11" interactif="1" />\n<gfx id="13" interactif="1" />\n'
                    '<gfx id="14" interactif="1" />\n<gfx id="x" interactif="1" />\n</BotLang>\n')
        self.faux = os.path.join(self.dossier, "faux_swfsvg.py")
        with open(self.faux, "w", encoding="utf-8") as f:
            f.write(FAUX)
        # Export précédent : l'image 1 et ancres.tsv restent, une ancienne image d'état disparaît.
        Image.new("RGBA", (2, 2), (1, 2, 3, 255)).save(os.path.join(self.decor, "objets", "10.png"))
        Image.new("RGBA", (2, 2), (1, 2, 3, 255)).save(os.path.join(self.decor, "objets", "99_2.png"))
        with open(os.path.join(self.decor, "ancres.tsv"), "w", encoding="utf-8") as f:
            f.write("# type\tid\txmin\tymin\tlargeur\thauteur\timage\nobjet\t10\t0\t0\t2\t2\t1\n")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    @staticmethod
    def swf(dossier, nom, symboles):
        with open(os.path.join(dossier, nom + ".swf"), "w", encoding="utf-8") as f:
            json.dump(symboles, f)

    def exporter(self, *options):
        r = subprocess.run([sys.executable, EXPORTEUR, self.client, self.decor, "--swfsvg", self.faux,
                            "--interactifs", self.interactifs, "--taches", "2"] + list(options), capture_output=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return r.stdout

    def ancres(self):
        with open(os.path.join(self.decor, etats.ANCRES), encoding="utf-8") as f:
            return f.read().splitlines()

    def test_choix_des_symboles(self):
        gfx = {10: 1, 11: 1, 13: 1}
        retenus = etats.choisir([("o1", {"10": 3, "11": 1, "12": 4}), ("o2", {"10": 5, "13": 2})], gfx, 15)
        self.assertEqual(retenus, {10: ("o1", 3), 13: ("o2", 2)})
        self.assertEqual(etats.choisir([("o1", {"10": 3})], gfx, 1), {})
        self.assertEqual(etats.lots_par_image({10: ("o1", 3), 13: ("o2", 2)}),
                         [("o1", 2, ["10"]), ("o1", 3, ["10"]), ("o2", 2, ["13"])])
        self.assertEqual(etats.gfx_interactifs(self.interactifs), {10: 1, 11: 1, 13: 1, 14: 1})

    def test_image_2_par_defaut(self):
        sortie = self.exporter()
        objets = sorted(os.listdir(os.path.join(self.decor, "objets")))
        self.assertEqual(objets, ["10.png", "10_2.png", "13_2.png"], sortie)
        self.assertEqual(self.ancres(), ["# type\tid\txmin\tymin\tlargeur\thauteur\timage",
                                         "objet\t10\t-3\t-4\t6\t4\t2", "objet\t13\t-3\t-4\t6\t4\t2"])
        with open(os.path.join(self.decor, "ancres.tsv"), encoding="utf-8") as f:
            self.assertIn("objet\t10\t0\t0\t2\t2\t1", f.read())
        with Image.open(os.path.join(self.decor, "objets", "10_2.png")) as image:
            pixels = image.convert("RGBA")
            self.assertEqual(pixels.size, (6, 4))
            self.assertEqual(pixels.getpixel((0, 1))[:3], (80, 32, 64))
            self.assertEqual(pixels.getpixel((5, 0))[3], 0, "le magenta doit devenir transparent")
        self.assertIn("2 gfx, 2 images", sortie)
        self.assertIn("gfx interactifs absents des bibliothèques : 14", sortie)

    def test_images_suivantes_sur_demande(self):
        self.exporter("--images-max", "5")
        self.assertEqual(sorted(os.listdir(os.path.join(self.decor, "objets"))), ["10.png", "10_2.png", "10_3.png", "13_2.png"])
        self.assertEqual(self.ancres()[1:], ["objet\t10\t-3\t-4\t6\t4\t2", "objet\t10\t-3\t-4\t6\t4\t3", "objet\t13\t-3\t-4\t6\t4\t2"])
        with Image.open(os.path.join(self.decor, "objets", "10_3.png")) as image:
            self.assertEqual(image.convert("RGBA").getpixel((0, 1))[:3], (120, 32, 64))


if __name__ == "__main__":
    unittest.main()

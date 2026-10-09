#!/usr/bin/env python3
"""Tests de exporter_sorts.py avec un faux swfsvg (faux_swfsvg_sorts.py) : aucun fichier du client ni de StarLoco.

Lancement : python3 tools/client-analysis/tests/test_exporter_sorts.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest, zlib

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
EXPORTEUR = os.path.join(ICI, "..", "exporter_sorts.py")
FAUX = os.path.join(ICI, "faux_swfsvg_sorts.py")
EN_TETE = "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin"


class ExporterSorts(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-sorts-")
        self.swfs = os.path.join(self.dossier, "spells")
        self.sortie = os.path.join(self.dossier, "sortie")
        os.makedirs(self.swfs)
        # 101 : scène de 6 images ; 103 : projectile (scène vide, shoot de 3 images, move vide) ;
        # 105 : scène vide dessinée par script ; 106 : scène vide sans script ; 704 : image 3 illisible ;
        # 900 : SWF illisible.
        self.swf("101", {"scene": 6, "fin": "static"})
        self.swf("103", {"scene": 0, "symboles": {"shoot": 3, "move": 2, "baton": 1}, "vides": ["move"]})
        self.swf("105", {"scene": 0, "script": "this.attachMovie(\"x\")"})
        self.swf("106", {"scene": 0})
        self.swf("704", {"scene": 3, "illisible": [3]})
        with open(os.path.join(self.swfs, "900.swf"), "w") as f:
            f.write("pas un SWF")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def swf(self, gfx, desc):
        with open(os.path.join(self.swfs, gfx + ".swf"), "w", encoding="utf-8") as f:
            json.dump(desc, f)

    def exporter(self, *options, ok=True):
        r = subprocess.run([sys.executable, EXPORTEUR, self.swfs, self.sortie, "--swfsvg", FAUX, "--jobs", "2"] + list(options),
                           capture_output=True, encoding="utf-8")
        if ok:
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return r

    def tsv(self, nom, colonnes):
        with open(os.path.join(self.sortie, nom), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        for l in lignes:
            self.assertEqual(len(l.split("\t")), colonnes, l)
        return lignes[0], [l.split("\t") for l in lignes[1:]]

    def effets(self):
        en_tete, lignes = self.tsv("effets.tsv", 9)
        self.assertEqual(en_tete, EN_TETE)
        return {(l[0], l[1]): tuple(int(v) for v in l[2:8]) + (l[8],) for l in lignes}

    def exclusions(self):
        en_tete, lignes = self.tsv("exclusions.tsv", 3)
        self.assertEqual(en_tete, "gfx\traison\tdetail")
        return {l[0]: l[1] for l in lignes}

    def test_scenes_symboles_et_exclusions(self):
        sortie = self.exporter("--pas", "2").stdout
        e = self.effets()
        # Scène de 6 images au pas 2 : images 1, 3 et 5, rectangle de 6 x 4 px dont le bord gauche est en x = N - 8.
        self.assertEqual(e[("101", "scene")], (-7, -4, 10, 4, 3, 20, "static"))
        bande = Image.open(os.path.join(self.sortie, "101_scene.png")).convert("RGBA")
        self.assertEqual(bande.size, (30, 4))
        for k, n in enumerate((1, 3, 5)):
            x = k * 10 + (n - 8) - (-7)
            self.assertEqual(bande.getpixel((x, 1))[:3], (0xc0, 0x40, 0x00), "image %d" % n)
            self.assertEqual(bande.getpixel((x + 6, 1))[3] if x + 6 < (k + 1) * 10 else 0, 0)
        # Projectile : shoot exporté (3 images -> 2 au pas 2), move vide ignoré, symbole non lu par le client ignoré.
        self.assertEqual(e[("103", "shoot")][4:], (2, 20, "static"))
        self.assertNotIn(("103", "move"), e)
        self.assertNotIn(("103", "baton"), e)
        self.assertIn("103 : symbole move non exporté", sortie)
        x = self.exclusions()
        self.assertEqual(x, {"103": "symboles", "105": "script", "106": "vide", "704": "cairo", "900": "erreur"})
        # Un gfx en échec de rendu est sauté entier, sans PNG, et le lot continue.
        self.assertFalse(any(f.startswith("704_") for f in os.listdir(self.sortie)))
        self.assertIn("704 : scène non rendue", sortie)
        self.assertIn("6 SWF, 2 PNG", sortie)

    def test_liste_gfx_partiel_et_nettoyage(self):
        liste = os.path.join(self.dossier, "sorts_utilises.txt")
        with open(liste, "w", encoding="utf-8") as f:
            f.write("# commentaire\n101 10 sorts\n103 30 sorts\n999 11 228\n")
        self.exporter("--liste", liste)
        self.assertEqual(set(self.effets()), {("101", "scene"), ("103", "shoot")})
        self.assertEqual(self.effets()[("101", "scene")][4:], (6, 40, "static"))
        self.assertEqual(self.exclusions(), {"103": "symboles", "999": "absent"})
        # --gfx ne remplace que ses gfx ; un PNG qui n'est plus produit disparaît.
        self.swf("101", {"scene": 2, "fin": "boucle"})
        open(os.path.join(self.sortie, "101_vieux.png"), "wb").close()
        open(os.path.join(self.sortie, "LISEZMOI.md"), "w").close()
        self.exporter("--gfx", "101,106")
        e = self.effets()
        self.assertEqual(e[("101", "scene")][4:], (2, 40, "boucle"))
        self.assertIn(("103", "shoot"), e)
        self.assertEqual(self.exclusions(), {"103": "symboles", "106": "vide", "999": "absent"})
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "101_vieux.png")))
        self.assertTrue(os.path.exists(os.path.join(self.sortie, "LISEZMOI.md")))
        # Une image seule vaut « arret ».
        self.swf("106", {"scene": 1, "fin": "static"})
        self.exporter("--gfx", "106")
        self.assertEqual(self.effets()[("106", "scene")][4:], (1, 40, "arret"))

    def test_options_refusees(self):
        liste = os.path.join(self.dossier, "liste.txt")
        with open(liste, "w", encoding="utf-8") as f:
            f.write("103 30 sorts\n")
        # --gfx avec --liste : seulement des gfx de la liste.
        for options in (["--pas", "3"], ["--gfx", "101", "--liste", liste], ["--gfx", "101", "--liste", "x.txt"], ["--gfx", "abc"]):
            r = self.exporter(*options, ok=False)
            self.assertNotEqual(r.returncode, 0, options)

    def test_scripts_dans_un_swf_compresse(self):
        sys.path.insert(0, os.path.join(ICI, ".."))
        import exporter_sorts
        chemin = os.path.join(self.dossier, "c.swf")
        corps = b"\x00" * 10 + b"duplicateMovieClip" + b"\x00" * 10
        with open(chemin, "wb") as f:
            f.write(b"CWS\x08" + (len(corps) + 8).to_bytes(4, "little") + zlib.compress(corps))
        self.assertTrue(exporter_sorts.a_des_scripts(chemin))
        with open(chemin, "wb") as f:
            f.write(b"CWS\x08" + (18).to_bytes(4, "little") + zlib.compress(b"\x00" * 10))
        self.assertFalse(exporter_sorts.a_des_scripts(chemin))

    def test_liste_starloco(self):
        sql = os.path.join(self.dossier, "game.sql")
        with open(sql, "w", encoding="utf-8") as f:
            f.write("INSERT INTO `sorts` VALUES ('1', 'Sort d\\'essai', '101', '10,0,1', '1');\n"
                    "INSERT INTO `sorts` VALUES ('2', 'Autre', '101', '11,1,0', '1');\n"
                    "INSERT INTO `sorts` VALUES ('3', 'Sans effet', '102', '0,0,0', '1');\n"
                    "INSERT INTO `sorts` VALUES ('4', 'Projectile', '103', '30,0,1', '1');\n"
                    "INSERT INTO `sorts` VALUES ('5', 'Sans gfx', '-1', '11,0,1', '1');\n"
                    "INSERT INTO `animations` VALUES ('7', '2900', 'Fée', '11', '8', '1');\n"
                    "INSERT INTO `animations` VALUES ('8', '2914', 'Fée', '11', '8', '2');\n"
                    "INSERT INTO `objectsactions` VALUES ('100', '5', '7');\n"
                    "INSERT INTO `objectsactions` VALUES ('101', '3;5', '139|8');\n"
                    "INSERT INTO `objectsactions` VALUES ('102', '5', 'all');\n")
        java = os.path.join(self.dossier, "ObjectAction.java")
        with open(java, "w", encoding="utf-8") as f:
            f.write('SocketManager.sendPacketToMap(map, "GA;208;"\n    + player.getId() + ";" + cell.getId()\n'
                    '    + ",2906,11,8,1");\n')
        liste = os.path.join(self.dossier, "liste.txt")
        r = subprocess.run([sys.executable, EXPORTEUR, "--starloco", sql, "--java", java, "--liste", liste],
                           capture_output=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stderr)
        with open(liste, encoding="utf-8") as f:
            lignes = [l for l in f.read().splitlines() if not l.startswith("#")]
        self.assertEqual(lignes, ["101 10,11 sorts", "103 30 sorts", "2900 11 228", "2906 11 208", "2914 11 228"])
        r = subprocess.run([sys.executable, EXPORTEUR, "--starloco", sql], capture_output=True, encoding="utf-8")
        self.assertNotEqual(r.returncode, 0)

    # ------------------------------------------------------------------ instance « rotate » (lot AN5)

    @staticmethod
    def bits(*champs):
        """Champs (valeur, nombre de bits) écrits bit à bit, poids fort d'abord, complétés à l'octet."""
        texte = "".join(format(v & ((1 << n) - 1), "0%db" % n) for v, n in champs)
        texte += "0" * (-len(texte) % 8)
        return bytes(int(texte[i:i + 8], 2) for i in range(0, len(texte), 8))

    def swf_binaire(self, chemin, tx, ty, compresse=False):
        """Vrai SWF d'une image : un PlaceObject2 sans nom (profondeur 2), puis l'instance « rotate »
        (profondeur 1, caractère 1) translatée de (tx, ty) twips."""
        def balise(code, corps):
            return ((code << 6) | len(corps)).to_bytes(2, "little") + corps
        matrice = self.bits((0, 1), (0, 1), (8, 5), (tx, 8), (ty, 8))
        sans_nom = balise(26, bytes([0x06]) + (2).to_bytes(2, "little") + (2).to_bytes(2, "little") + self.bits((0, 1), (0, 1), (0, 5)))
        nomme = balise(26, bytes([0x26]) + (1).to_bytes(2, "little") + (1).to_bytes(2, "little") + matrice + b"rotate\0")
        corps = b"\x00" + b"\x00\x18" + (1).to_bytes(2, "little") + sans_nom + nomme + balise(1, b"") + balise(0, b"")
        with open(chemin, "wb") as f:
            if compresse:
                f.write(b"CWS\x08" + (len(corps) + 8).to_bytes(4, "little") + zlib.compress(corps))
            else:
                f.write(b"FWS\x08" + (len(corps) + 8).to_bytes(4, "little") + corps)

    def test_lecture_des_placements(self):
        sys.path.insert(0, os.path.join(ICI, ".."))
        import exporter_sorts
        for compresse in (False, True):
            chemin = os.path.join(self.dossier, "p.swf")
            self.swf_binaire(chemin, 69, -41, compresse)
            self.assertEqual(exporter_sorts.placements_principaux(chemin),
                             [(0, 2, 2, None, (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)), (0, 1, 1, "rotate", (1.0, 0.0, 0.0, 1.0, 3.45, -2.05))])
            self.assertEqual(exporter_sorts.placement_nomme(chemin, "ROTATE"), (1.0, 0.0, 0.0, 1.0, 3.45, -2.05))
            self.assertIsNone(exporter_sorts.placement_nomme(chemin, "shoot"))
        # Un fichier qui n'est pas un SWF, ou un SWF tronqué, ne lève pas.
        self.assertEqual(exporter_sorts.placements_principaux(os.path.join(self.swfs, "900.swf")), [])
        with open(chemin, "rb") as f:
            debut = f.read(30)
        with open(chemin, "wb") as f:
            f.write(b"FWS" + debut[3:])
        self.assertIsInstance(exporter_sorts.placements_principaux(chemin), list)

    def test_bande_rotate_centree_sur_l_instance(self):
        # 120 : scène de 4 images et instance rotate de 4 images en (3,45 ; -2,05), affichée en type 21 ;
        # 121 : même SWF affiché aussi en type 10 (rotate reste dans la scène) ; 123 : scène faite de rotate seul.
        for gfx, scene in (("120", 4), ("121", 4), ("123", 0)):
            self.swf_binaire(os.path.join(self.swfs, gfx + ".swf"), 69, -41)
            with open(os.path.join(self.swfs, gfx + ".swf.json"), "w", encoding="utf-8") as f:
                json.dump({"scene": scene, "rotate": 4, "pivot": [3, -2], "fin": "static"}, f)
        liste = os.path.join(self.dossier, "sorts_utilises.txt")
        with open(liste, "w", encoding="utf-8") as f:
            f.write("120 21 sorts\n121 10,21 sorts\n123 20 sorts\n")
        sortie = self.exporter("--liste", liste).stdout
        e = self.effets()
        # Cadre symétrique autour du pivot arrondi (3, -2) : xmin + largeur / 2 = 3, ymin + hauteur / 2 = -2.
        self.assertEqual(e[("120", "rotate")], (-3, -4, 12, 4, 4, 40, "static"))
        vert = (0x00, 0xc0, 0x40)

        def pixels(nom):
            image = Image.open(os.path.join(self.sortie, nom)).convert("RGBA")
            return image, [image.getpixel((x, y)) for y in range(image.height) for x in range(image.width)]

        bande, tous = pixels("120_rotate.png")
        self.assertEqual(bande.size, (48, 4))
        for j in range(4):
            k = j + 1  # rectangle de k + 1 px de large, de x = 4 (colonne 7 du cadre) à x = 4 + k
            self.assertEqual(bande.getpixel((j * 12 + 7, 0))[:3], vert, "image %d" % k)
            self.assertEqual(bande.getpixel((j * 12 + 7 + k, 1))[:3], vert, "image %d" % k)
            self.assertEqual(bande.getpixel((j * 12 + 6, 0))[3], 0, "image %d" % k)
            self.assertEqual(bande.getpixel((j * 12 + 7, 2))[3], 0, "image %d" % k)
            if 8 + k < 12:
                self.assertEqual(bande.getpixel((j * 12 + 8 + k, 0))[3], 0, "image %d" % k)
        # Scène rendue sans l'instance ; gardée entière pour un gfx affiché aussi en type 10.
        _, tous = pixels("120_scene.png")
        self.assertFalse(any(p[3] and p[:3] == vert for p in tous))
        self.assertNotIn(("121", "rotate"), e)
        _, tous = pixels("121_scene.png")
        self.assertTrue(any(p[3] and p[:3] == vert for p in tous))
        self.assertIn("121 : instance rotate gardée dans la scène (types 10,21)", sortie)
        # Scène vide sans l'instance : exclusion « symboles » qui nomme rotate.
        self.assertEqual(e[("123", "rotate")][:4], (-3, -4, 12, 4))
        self.assertNotIn(("123", "scene"), e)
        _, lignes = self.tsv("exclusions.tsv", 3)
        self.assertIn(["123", "symboles", "rotate"], lignes)
        # --gfx sans --liste : types inconnus, l'instance reste dans la scène et l'ancienne bande disparaît.
        sortie = self.exporter("--gfx", "120").stdout
        self.assertIn("120 : instance rotate gardée dans la scène (types inconnus)", sortie)
        self.assertNotIn(("120", "rotate"), self.effets())
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "120_rotate.png")))
        # --gfx avec --liste : types lus dans la liste, seules les lignes de 120 changent.
        self.exporter("--gfx", "120", "--liste", liste)
        self.assertEqual(self.effets(), e)


if __name__ == "__main__":
    unittest.main()

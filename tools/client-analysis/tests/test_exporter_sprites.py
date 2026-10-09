#!/usr/bin/env python3
"""Tests de exporter_sprites.py avec un faux swfsvg (faux_swfsvg.py) : aucun fichier du client.

Lancement : python3 tools/client-analysis/tests/test_exporter_sprites.py (cairosvg et Pillow requis)."""
import json, os, shutil, subprocess, sys, tempfile, unittest

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
EXPORTEUR = os.path.join(ICI, "..", "exporter_sprites.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")
EN_TETE = "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin"
sys.path.insert(0, os.path.join(ICI, ".."))
import exporter_sprites  # noqa: E402


class ExporterSprites(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-sprites-")
        self.sprites = os.path.join(self.dossier, "sprites")
        self.sortie = os.path.join(self.dossier, "sortie")
        os.makedirs(self.sprites)
        os.makedirs(self.sortie)
        # 10 : classe animée ; 11 : mêmes cycles mais non listé ; 7 : épée (scène seule) ;
        # 8 : ni static ni scène ; 9 : static vide ; 12 : SWF illisible.
        self.swf("10", {"staticR": 3, "StaticL": 1, "staticF": 2, "walkR": 4, "runR": 2, "hitR": 5, "hitL": 4, "dieR": 6},
                 vides_images={"staticF": [2]}, fins={"hitR": "static", "hitL": "static", "dieR": "arret"})
        self.swf("11", {"staticR": 1, "walkR": 4})
        self.swf("7", {"circle": 1}, scene=True)
        self.swf("8", {"circle": 1}, scene=False)
        self.swf("9", {"staticB": 3}, vides=["staticB"])
        with open(os.path.join(self.sprites, "12.swf"), "w") as f:
            f.write("pas un SWF")
        self.animes("# commentaire\n10  # classe\n")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def swf(self, gfx, symboles, scene=False, vides=(), vides_images=None, fins=None, larges=None, zones=()):
        with open(os.path.join(self.sprites, gfx + ".swf"), "w", encoding="utf-8") as f:
            json.dump({"symboles": symboles, "scene": scene, "vides": list(vides), "vides_images": vides_images or {},
                       "fins": fins or {}, "larges": larges or {}, "zones": list(zones)}, f)

    def animes(self, texte):
        with open(os.path.join(self.sortie, "sprites_animes.txt"), "w", encoding="utf-8") as f:
            f.write(texte)

    def exporter(self, *options, ok=True):
        r = subprocess.run([sys.executable, EXPORTEUR, self.sprites, self.sortie, "--swfsvg", FAUX, "--jobs", "2"] + list(options),
                           capture_output=True, encoding="utf-8")
        if ok:
            self.assertEqual(r.returncode, 0, r.stderr)
            return r.stdout
        return r

    def ancres(self):
        """{(gfx, anim): (xmin, ymin, largeur, hauteur, images, ips, fin)} ; neuf colonnes partout."""
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        self.assertEqual(lignes[0], EN_TETE)
        resultat = {}
        for l in lignes[1:]:
            c = l.split("\t")
            self.assertEqual(len(c), 9, l)
            resultat[(c[0], c[1])] = tuple(int(v) for v in c[2:8]) + (c[8],)
        return resultat

    def png(self, nom):
        return Image.open(os.path.join(self.sortie, nom)).convert("RGBA")

    def existe(self, nom):
        return os.path.exists(os.path.join(self.sortie, nom))

    def octets(self, nom):
        with open(os.path.join(self.sortie, nom), "rb") as f:
            return f.read()

    def couleurs(self):
        """couleurs.tsv : {(gfx, anim): {index: (zone, (r, g, b))}}."""
        with open(os.path.join(self.sortie, "couleurs.tsv"), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        self.assertEqual(lignes[0], "gfx\tanim\tindex\tzone\tcouleur")
        resultat = {}
        for l in lignes[1:]:
            c = l.split("\t")
            self.assertEqual(len(c), 5, l)
            resultat.setdefault((c[0], c[1]), {})[int(c[2])] = (int(c[3]), tuple(int(c[4][i:i + 2], 16) for i in (0, 2, 4)))
        return resultat

    def recolorer(self, gfx, anim, cibles):
        """Bande recolorée par la formule du bot : PNG + (couleur du GM - couleur d'origine) x couverture / 255."""
        d = self.png("%s_%s.png" % (gfx, anim))
        m = Image.open(os.path.join(self.sortie, "%s_%s.couleurs.png" % (gfx, anim))).convert("RGB")
        self.assertEqual(m.size, d.size, "masque de même taille que sa bande")
        table = self.couleurs()[(gfx, anim)]
        r = d.copy()
        for y in range(d.height):
            for x in range(d.width):
                couverture, index, bleu = m.getpixel((x, y))
                self.assertEqual(bleu, 0)
                if couverture == 0:
                    self.assertEqual(index, 0, "pixel sans zone : noir")
                    continue
                zone, origine = table[index]
                p = d.getpixel((x, y))
                r.putpixel((x, y), tuple(max(0, min(255, round(p[k] + (cibles[zone][k] - origine[k]) * couverture / 255)))
                                         for k in range(3)) + (p[3],))
        return r

    def test_export_complet(self):
        sortie = self.exporter()
        a = self.ancres()
        self.assertEqual(set(a), {("7", "scene"), ("10", "runR"), ("10", "staticF"), ("10", "staticL"), ("10", "staticR"), ("10", "walkR"), ("11", "staticR")})
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            ordre = [l.split("\t")[0] for l in f.read().splitlines()[1:]]
        self.assertEqual(ordre, ["7", "10", "10", "10", "10", "10", "11"], "tri numérique des gfx")
        # Statique : dernière image utile (3) ; rognage au rectangle, magenta effacé, pied en (0, 0).
        # Une image seule : 40 ips, fin « arret ».
        self.assertEqual(a[("10", "staticR")], (-3, -25, 4, 25, 1, 40, "arret"))
        self.assertEqual(a[("10", "staticL")][:5], (-5, -25, 4, 25, 1), "StaticL (casse différente) exporté en staticL")
        self.assertEqual(a[("10", "staticF")][:5], (-5, -25, 4, 25, 1), "dernière image vide : image 1")
        self.assertIn("staticF vide à l'image 2, image 1 retenue", sortie)
        self.assertIn("9 : staticB vide, rien d'exporté", sortie)
        im = self.png("10_staticR.png")
        self.assertEqual(im.size, (4, 25))
        px = im.tobytes()
        self.assertFalse(any(px[i + 3] and px[i] > 235 and px[i + 1] < 25 and px[i + 2] > 235 for i in range(0, len(px), 4)), "magenta restant")
        # Bande : 4 images de 7 pixels (union des cadres), le rectangle avance d'un pixel par image ; fin de --list.
        self.assertEqual(a[("10", "walkR")], (-5, -25, 7, 25, 4, 40, "boucle"))
        bande = self.png("10_walkR.png")
        self.assertEqual(bande.size, (28, 25))
        for k in range(4):
            colonnes = [x for x in range(7) if bande.getpixel((k * 7 + x, 12))[3] > 0]
            self.assertEqual(colonnes, [k, k + 1, k + 2, k + 3], "image %d" % (k + 1))
        self.assertEqual(a[("10", "runR")][4], 2)
        self.assertFalse(self.existe("11_walkR.png"), "11 n'est pas dans sprites_animes.txt")
        self.assertFalse(self.existe("10_hitR.png"), "un gfx seul dans sprites_animes.txt vaut walk,run")
        # Scène quand aucun static<O> : l'épée ; rien pour 8, 9 et 12, avec un message chacun.
        self.assertEqual(a[("7", "scene")], (-5, -25, 4, 25, 1, 40, "arret"))
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
        self.assertTrue(self.existe("7R.png"))
        self.assertFalse(self.existe("7_staticR.png"))
        self.assertTrue(self.existe("11_staticB.png"), "un gfx non réexporté n'est pas nettoyé")
        r = self.exporter("--gfx", "404", ok=False)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("404", r.stderr)

    def test_echelle_2(self):
        self.exporter("--echelle", "2", "--gfx", "10")
        self.assertEqual(self.ancres()[("10", "staticR")][:5], (-6, -50, 8, 50, 1))
        self.assertEqual(self.png("10_walkR.png").size, (56, 50))

    def test_familles_du_fichier_et_pas(self):
        # Format « gfx familles » : hit au pas 2 (images 1, 3, 5 : 20 ips), die au pas 1 ; fin de --list.
        self.animes("10 walk,run,hit:2,die\n10 runR\n11\n")
        sortie = self.exporter()
        a = self.ancres()
        self.assertEqual(a[("10", "hitR")][4:], (3, 20, "static"))
        self.assertEqual(a[("10", "hitL")][4:], (2, 20, "static"), "4 images au pas 2")
        self.assertEqual(a[("10", "dieR")][4:], (6, 40, "arret"))
        self.assertIn(("11", "walkR"), a, "un gfx seul vaut walk,run")
        self.assertIn("aucun symbole runR<O>", sortie, "famille absente signalée")
        bande = self.png("10_hitR.png")
        largeur = a[("10", "hitR")][2]
        self.assertEqual(bande.size, (largeur * 3, 25))
        # Image k de la bande = image 2k + 1 du symbole : bord gauche du rectangle en x = 2k + 1 - 6.
        gauche = [min(x for x in range(largeur) if bande.getpixel((k * largeur + x, 12))[3] > 0) for k in range(3)]
        self.assertEqual([g - gauche[0] for g in gauche], [0, 2, 4])

    def test_anims_ne_remplace_que_ses_familles(self):
        self.exporter()
        avant = self.ancres()
        walk = self.octets("10_walkR.png")
        Image.new("RGBA", (2, 2)).save(os.path.join(self.sortie, "10_hitB.png"))  # reste d'un export précédent
        sortie = self.exporter("--gfx", "10", "--anims", "hit,die")
        a = self.ancres()
        for cle, valeur in avant.items():
            self.assertEqual(a[cle], valeur, "ligne gardée : %s" % (cle,))
        self.assertEqual(a[("10", "hitR")][4:], (5, 40, "static"))
        self.assertEqual(a[("10", "dieR")][4:], (6, 40, "arret"))
        self.assertEqual(self.octets("10_walkR.png"), walk, "walkR non réécrit")
        self.assertFalse(self.existe("10_hitB.png"), "ancien PNG de la famille réexportée retiré")
        self.assertTrue(self.existe("10_staticR.png") and self.existe("7_scene.png"))
        self.assertIn("3 PNG", sortie)
        # --pas pour les familles de --anims sans pas ; un pas qui ne divise pas 40 est refusé.
        self.exporter("--gfx", "10", "--anims", "hit", "--pas", "2")
        self.assertEqual(self.ancres()[("10", "hitR")][4:], (3, 20, "static"))
        self.assertNotEqual(self.exporter("--anims", "hit:3", ok=False).returncode, 0)
        self.assertNotEqual(self.exporter("--anims", "hit-R", ok=False).returncode, 0)

    def test_ancien_ancres_a_sept_colonnes(self):
        # Ancres historiques (lot D2) : ips 40 et fin ajoutées aux lignes gardées.
        self.exporter("--gfx", "10,11")
        with open(os.path.join(self.sortie, "ancres.tsv"), encoding="utf-8") as f:
            lignes = f.read().splitlines()
        with open(os.path.join(self.sortie, "ancres.tsv"), "w", encoding="utf-8") as f:
            f.write("gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\n")
            for l in lignes[1:]:
                f.write("\t".join(l.split("\t")[:7]) + "\n")
        self.swf("10", {"staticR": 3, "walkR": 4, "runR": 2, "hitR": 5}, fins={"hitR": "static", "walkR": "boucle", "runR": "suite:carring_C"})
        sortie = self.exporter("--gfx", "10", "--anims", "hit")
        a = self.ancres()
        self.assertEqual(a[("10", "walkR")][4:], (4, 40, "boucle"))
        self.assertEqual(a[("11", "staticR")][4:], (1, 40, "arret"))
        self.assertEqual(a[("10", "runR")][6], "static", "nom de suite illisible par le bot")
        self.assertIn("carring_C", sortie)
        self.assertEqual(a[("10", "hitR")][4:], (5, 40, "static"))

    def test_grille_au_dela_de_32767_px(self):
        # 7 images de 9 000 px : une ligne ferait 63 000 px ; grille de 3 colonnes sur 3 lignes.
        self.swf("13", {"staticR": 1, "dieR": 7}, larges={"dieR": 9000}, fins={"dieR": "arret"})
        self.exporter("--gfx", "13", "--anims", "die")
        a = self.ancres()
        largeur, hauteur = a[("13", "dieR")][2], a[("13", "dieR")][3]
        self.assertEqual((largeur, a[("13", "dieR")][4]), (9000, 7))
        grille = self.png("13_dieR.png")
        self.assertEqual(grille.size, (3 * 9000, 3 * hauteur))
        for k in range(7):
            # Repère vert de l'image k (x = k + 1 dans sa case), ligne par ligne.
            x0, y0 = k % 3 * largeur, k // 3 * hauteur
            vert = [x for x in range(12) if grille.getpixel((x0 + x, y0))[1] > 200 and grille.getpixel((x0 + x, y0))[0] < 50]
            self.assertEqual(vert, [k + 1], "image %d" % (k + 1))
        self.assertEqual(grille.getpixel((2 * largeur + 5, 2 * hauteur + 5))[3], 0, "case vide après la dernière image")

    def test_masques_de_recoloration(self):
        # 10 : corps brun (zone 1) et chapeau bleu (zone 3, 3 px du haut) ; 11 et 7 sans zone : pas de masque.
        self.swf("10", {"staticR": 3, "walkR": 4, "hitR": 2}, zones=[1, 3], fins={"hitR": "static"})
        self.animes("10 walk,hit\n11\n")
        sortie = self.exporter("--masques")
        self.assertIn("3 masques", sortie)
        for nom in ("11_staticR", "11_walkR", "7_scene"):
            self.assertTrue(self.existe(nom + ".png"))
            self.assertFalse(self.existe(nom + ".couleurs.png"), nom)
        table = self.couleurs()
        self.assertEqual(set(table), {("10", "staticR"), ("10", "walkR"), ("10", "hitR")})
        self.assertEqual(sorted(table[("10", "staticR")].values()), [(1, (0x80, 0x40, 0x20)), (3, (0x20, 0x40, 0xc0))])
        # Pose : chapeau sur les lignes 0 à 2, corps en dessous ; couverture pleine (255).
        m = Image.open(os.path.join(self.sortie, "10_staticR.couleurs.png")).convert("RGB")
        self.assertEqual(table[("10", "staticR")][m.getpixel((1, 1))[1]][0], 3)
        self.assertEqual(table[("10", "staticR")][m.getpixel((1, 10))[1]][0], 1)
        self.assertEqual(m.getpixel((1, 10))[0], 255)
        cibles = {1: (10, 200, 30), 2: (0, 0, 0), 3: (250, 250, 0)}
        r = self.recolorer("10", "staticR", cibles)
        self.assertEqual(r.getpixel((1, 1)), (250, 250, 0, 255))
        self.assertEqual(r.getpixel((2, 20)), (10, 200, 30, 255))
        # Bande : même disposition que walkR (image k : colonnes k à k + 3 de sa case de 7 px).
        r = self.recolorer("10", "walkR", cibles)
        self.assertEqual(r.size, (28, 25))
        for k in range(4):
            vertes = [x for x in range(7) if r.getpixel((k * 7 + x, 12)) == (10, 200, 30, 255)]
            self.assertEqual(vertes, [k, k + 1, k + 2, k + 3], "image %d" % (k + 1))
        masque_walk, tsv = self.octets("10_walkR.couleurs.png"), self.octets("couleurs.tsv")
        # Réexport de walk sans --masques : son masque et ses lignes partent, les autres restent.
        sortie = self.exporter("--gfx", "10", "--anims", "walk")
        self.assertIn("1 masques de recoloration retirés", sortie)
        self.assertFalse(self.existe("10_walkR.couleurs.png"))
        self.assertTrue(self.existe("10_staticR.couleurs.png") and self.existe("10_hitR.couleurs.png"))
        self.assertEqual(set(self.couleurs()), {("10", "staticR"), ("10", "hitR")})
        # Puis avec --masques : mêmes octets (export reproductible), lignes des autres bandes gardées.
        self.exporter("--gfx", "10", "--anims", "walk", "--masques")
        self.assertEqual(self.octets("10_walkR.couleurs.png"), masque_walk)
        self.assertEqual(self.octets("couleurs.tsv"), tsv)
        # Export complet sans --masques : plus aucun masque, couleurs.tsv réduit à son en-tête.
        self.exporter()
        self.assertFalse([f for f in os.listdir(self.sortie) if f.endswith(".couleurs.png")])
        self.assertEqual(self.couleurs(), {})

    def test_disposition(self):
        self.assertEqual(exporter_sprites.disposition(10, 100, 50), (10, 1))
        self.assertEqual(exporter_sprites.disposition(100, 400, 300), (50, 2))
        self.assertEqual(exporter_sprites.disposition(7, 9000, 28), (3, 3))
        colonnes, lignes = exporter_sprites.disposition(240, 150, 120)
        self.assertLessEqual(colonnes * 150, 32767)
        self.assertEqual(lignes, -(-240 // colonnes))
        with self.assertRaises(RuntimeError):
            exporter_sprites.disposition(2, 30000, 600)  # 36 Mpx
        with self.assertRaises(RuntimeError):
            exporter_sprites.disposition(1, 40000, 10)

    def test_liste_animes(self):
        chemin = os.path.join(self.dossier, "liste.txt")
        with open(chemin, "w", encoding="utf-8") as f:
            f.write("# commentaire\n10\n1001 walk:2,run:2, hit,die  # monstre\n1001 anim0\n")
        self.assertEqual(exporter_sprites.lire_liste_animes(chemin),
                         {"10": [("walk", 1), ("run", 1)], "1001": [("walk", 2), ("run", 2), ("hit", 1), ("die", 1), ("anim0", 1)]})
        for faux in ("abc walk\n", "10 hit:3\n", "10 hit:0\n", "10 h!t\n"):
            with open(chemin, "w", encoding="utf-8") as f:
                f.write(faux)
            with self.assertRaises(ValueError, msg=faux):
                exporter_sprites.lire_liste_animes(chemin)


if __name__ == "__main__":
    unittest.main()

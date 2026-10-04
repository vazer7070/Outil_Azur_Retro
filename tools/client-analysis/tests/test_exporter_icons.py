#!/usr/bin/env python3
"""Tests de exporter_icons.py : SWF fabriqués dans le test et faux swfsvg (faux_swfsvg.py), aucun fichier du client.

Lancement : python3 tools/client-analysis/tests/test_exporter_icons.py (cairosvg et Pillow requis)."""
import io, json, os, shutil, struct, subprocess, sys, tempfile, unittest, zlib
import xml.etree.ElementTree as ET

from PIL import Image

ICI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(ICI, ".."))
import exporter_icons as E  # noqa: E402

EXPORTEUR = os.path.join(ICI, "..", "exporter_icons.py")
FAUX = os.path.join(ICI, "faux_swfsvg.py")
SVG = "{http://www.w3.org/2000/svg}"


# --- Construction de SWF minimaux (balises brutes, sans dessin) ---------------------------------

def place(profondeur, caractere, nom=None):
    """PlaceObject2 : caractère à une profondeur, nom d'instance facultatif."""
    drapeaux = 0x02 | (0x20 if nom else 0)
    octets = bytes([drapeaux]) + struct.pack("<HH", profondeur, caractere)
    return [E.PLACE_OBJECT2, octets + (nom.encode("latin-1") + b"\0" if nom else b"")]


def retire(profondeur):
    return [E.REMOVE_OBJECT2, struct.pack("<H", profondeur)]


def sprite(ident, balises):
    return [E.DEFINE_SPRITE, struct.pack("<HH", ident, 1) + E.ecrire_balises(balises + [[1, b""], [0, b""]])]


def exporte(noms):
    corps = struct.pack("<H", len(noms)) + b"".join(struct.pack("<H", i) + n.encode("latin-1") + b"\0" for n, i in noms.items())
    return [E.EXPORT_ASSETS, corps]


def swf(balises, compresse=False):
    entete = bytes([0]) + struct.pack("<HH", 12 << 8, 1)  # RECT vide (nbits = 0), cadence, 1 image
    brut = E.ecrire_swf(8, entete, balises + [[1, b""], [0, b""]])
    if not compresse:
        return brut
    return b"CWS" + brut[3:8] + zlib.compress(brut[8:])


def placements(balises):
    return [E.placement(o, c) for c, o in balises if c in (E.PLACE_OBJECT2, E.PLACE_OBJECT3)]


def interne(donnees, ident):
    for code, octets in E.lire_swf(donnees)[2]:
        if code == E.DEFINE_SPRITE and struct.unpack_from("<H", octets, 0)[0] == ident:
            return E.lire_balises(octets, 4, len(octets))
    raise AssertionError("sprite %d absent" % ident)


class Swf(unittest.TestCase):
    def setUp(self):
        self.donnees = swf([
            sprite(5, [place(1, 2, "back"), place(2, 3, "contour"), place(4, 3), retire(2)]),
            exporte({"Embleme": 5, "Autre": 9}),
            place(1, 5, "back"), place(3, 2),
        ])

    def test_lecture(self):
        self.assertEqual(E.exports(self.donnees), {"Embleme": 5, "Autre": 9})
        self.assertEqual(E.exports(swf([exporte({"x_y": 3})], compresse=True)), {"x_y": 3}, "SWF compressé (CWS)")
        self.assertEqual(placements(interne(self.donnees, 5)), [(1, "back"), (2, "contour"), (4, None)])
        for invalide in (b"", b"GIF89a", b"CWS\x08\0\0\0\0pas du zlib", b"FWS\x08\x20\0\0\0\0\0\0"):
            with self.assertRaises(E.SwfInvalide):
                E.lire_swf(invalide)
        with self.assertRaises(E.SwfInvalide):  # balise annoncée plus longue que le fichier
            E.lire_balises(struct.pack("<H", (2 << 6) | 10) + b"abc", 0, 5)
        with self.assertRaises(E.SwfInvalide):  # ExportAssets qui annonce deux noms et n'en contient qu'un
            E.exports(swf([[E.EXPORT_ASSETS, struct.pack("<HH", 2, 3) + b"x\0"]]))

    def test_calques_dans_un_sprite(self):
        seul = E.variante_calque(self.donnees, {"back"}, True, sprite=5)
        self.assertEqual(placements(interne(seul, 5)), [(1, "back")])
        sans = E.variante_calque(self.donnees, {"contour"}, False, sprite=5)
        balises = interne(sans, 5)
        self.assertEqual(placements(balises), [(1, "back"), (4, None)])
        self.assertNotIn(E.REMOVE_OBJECT2, [c for c, _ in balises], "le retrait de la profondeur 2 part avec elle")
        self.assertEqual(E.exports(sans), {"Embleme": 5, "Autre": 9}, "le reste du SWF est intact")
        with self.assertRaises(E.SwfInvalide):
            E.variante_calque(self.donnees, {"absent"}, True, sprite=5)

    def test_calques_de_la_scene(self):
        principale = lambda d: placements([b for b in E.lire_swf(d)[2] if b[0] != E.DEFINE_SPRITE])
        self.assertEqual(principale(E.variante_calque(self.donnees, {"#3"}, True)), [(3, None)], "profondeur sans nom")
        self.assertEqual(principale(E.variante_calque(self.donnees, {"back"}, False)), [(3, None)])
        self.assertEqual(placements(interne(E.variante_calque(self.donnees, {"back"}, False), 5))[0], (1, "back"),
                         "la scène filtrée ne touche pas aux sprites")


class Images(unittest.TestCase):
    def test_transformations_de_couleur(self):
        source = ('<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><defs>'
                  '<filter id="f1"><feColorMatrix type="matrix" values="0 0 0 0 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0.5 0"/></filter>'
                  '<linearGradient id="g1"><stop offset="0" stop-color="#00ff00"/></linearGradient></defs>'
                  '<g filter="url(#f1)"><rect fill="#123456" width="5" height="5"/><rect fill="url(#g1)" width="5" height="5"/></g>'
                  '<rect fill="#123456" width="1" height="1"/></svg>')
        racine = ET.fromstring(E.appliquer_transformations(source))
        groupe = racine.find(SVG + "g")
        self.assertIsNone(groupe.get("filter"), "le filtre appliqué est retiré")
        plein, degrade = groupe.findall(SVG + "rect")
        self.assertEqual((plein.get("fill"), plein.get("fill-opacity")), ("#ff0000", "0.5"))
        copie = degrade.get("fill")[5:-1]
        self.assertNotEqual(copie, "g1")
        stops = {g.get("id"): g.find(SVG + "stop").get("stop-color") for g in racine.iter(SVG + "linearGradient")}
        self.assertEqual(stops, {"g1": "#00ff00", copie: "#ff0000"}, "le dégradé d'origine reste intact")
        self.assertEqual(racine.findall(SVG + "rect")[0].get("fill"), "#123456", "hors du groupe filtré : inchangé")
        self.assertEqual(E.appliquer_transformations("<svg/>"), "<svg/>")

    def test_magenta_et_palette(self):
        image = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
        for x in range(8):
            image.putpixel((x, 1), (255, 0, 255, 255))
            image.putpixel((x, 2), (240, 20, 250, 255))
            image.putpixel((x, 3), (200, 40, 10, 255))
            image.putpixel((x, 4), (10, 20, 250, 128))
        image = E.effacer_magenta(image)
        self.assertEqual({image.getpixel((x, y))[3] for x in range(8) for y in (1, 2)}, {0}, "magenta du client effacé")
        self.assertEqual(image.getpixel((3, 3)), (200, 40, 10, 255))
        palette = E.optimiser(image)
        self.assertEqual(palette.mode, "P", "quatre couleurs : palette")
        rendu = palette.convert("RGBA")
        self.assertEqual(rendu.getpixel((3, 3)), (200, 40, 10, 255))
        self.assertEqual(rendu.getpixel((5, 4))[3], 128, "alpha partiel conservé (tRNS)")
        self.assertEqual(rendu.getpixel((0, 0))[3], 0)
        self.assertEqual(E.optimiser(Image.new("RGBA", (3, 3), (9, 9, 9, 255))).convert("RGBA").getpixel((1, 1)), (9, 9, 9, 255))

    def test_repli_quand_cairo_refuse(self):
        dossier = tempfile.mkdtemp(prefix="exporter-icons-")
        chemin = os.path.join(dossier, "a.svg")
        with open(chemin, "w", encoding="utf-8") as f:
            f.write('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="50"><rect width="100" height="50" fill="#804020"/></svg>')
        original = E.cairosvg.svg2png

        def capricieux(**kwargs):
            if kwargs.get("scale", 1) < 1:
                raise MemoryError("cairo returned CAIRO_STATUS_NO_MEMORY")
            return original(**kwargs)
        try:
            self.assertEqual(E.svg_en_png(chemin, 2).size, (200, 100))
            E.cairosvg.svg2png = capricieux
            image = E.svg_en_png(chemin, 0.5)
            self.assertEqual(image.size, (50, 25), "rendu à l'échelle 1 puis réduit")
            self.assertEqual(image.getpixel((25, 12)), (128, 64, 32, 255))
            E.cairosvg.svg2png = lambda **kwargs: (_ for _ in ()).throw(MemoryError("toujours"))
            with self.assertRaises(MemoryError):
                E.svg_en_png(chemin, 1)
        finally:
            E.cairosvg.svg2png = original
            shutil.rmtree(dossier, ignore_errors=True)


class Export(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.mkdtemp(prefix="exporter-icons-")
        self.client = os.path.join(self.dossier, "client")
        self.sortie = os.path.join(self.dossier, "sortie")
        self.faux("clips/smileys/1.swf", {"scene": True})
        self.faux("clips/smileys/2.swf", {"scene": False})
        self.faux("clips/smileys/lisezmoi.swf", {"scene": True})
        self.faux("clips/items/9/12.swf", {"scene": True})
        self.faux("clips/items/9/3.swf", {"scene": True})
        for chemin in ("clips/artworks/big/7.swf", "clips/maps/0.swf", "clips/maps/hints.swf"):
            chemin = os.path.join(self.client, *chemin.split("/"))
            os.makedirs(os.path.dirname(chemin), exist_ok=True)
            with open(chemin, "w") as f:
                f.write("pas un SWF")

    def tearDown(self):
        shutil.rmtree(self.dossier, ignore_errors=True)

    def faux(self, chemin, description):
        chemin = os.path.join(self.client, *chemin.split("/"))
        os.makedirs(os.path.dirname(chemin), exist_ok=True)
        with open(chemin, "w", encoding="utf-8") as f:
            json.dump(description, f)

    def lire(self, *chemin):
        with open(os.path.join(self.sortie, *chemin), encoding="utf-8") as f:
            return f.read()

    def test_familles_scene(self):
        r = subprocess.run([sys.executable, EXPORTEUR, "--client", self.client, "--sortie", self.sortie, "--swfsvg", FAUX,
                            "--familles", "Smileys,Items,Portraits,Emotes,WorldMap", "--processus", "2", "--travail", self.dossier],
                           capture_output=True, encoding="utf-8")
        self.assertEqual(r.returncode, 0, r.stderr)
        # Cadre 20 x 32 à l'échelle 2, limité à 48 px : échelle 1,5.
        smiley = Image.open(os.path.join(self.sortie, "Smileys", "1.png")).convert("RGBA")
        self.assertEqual(smiley.size, (30, 48))
        pixels = [smiley.getpixel((x, y)) for y in range(smiley.height) for x in range(smiley.width)]
        self.assertFalse(any(a and r_ > 235 and g < 25 and b > 235 for r_, g, b, a in pixels), "magenta restant")
        self.assertIn((128, 64, 32, 255), pixels)
        self.assertTrue(os.path.exists(os.path.join(self.sortie, "Items", "9", "12.png")))
        self.assertTrue(os.path.exists(os.path.join(self.sortie, "Items", "9", "3.png")))
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "Smileys", "2.png")), "rendu vide : pas de PNG")
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "Smileys", "lisezmoi.png")), "seuls les <n>.swf sont lus")
        self.assertFalse(os.path.exists(os.path.join(self.sortie, "Portraits", "7.png")))
        self.assertIn("clips/artworks/big/7.swf", r.stdout, "un SWF illisible est signalé sans arrêter la série")
        self.assertIn("illisible : ", r.stdout, "carte du monde illisible : signalée, la série continue")
        self.assertIn("0 PNG", self.lire("WorldMap", "PROVENANCE.md"))
        # Provenance : source, outil, commande de la famille, fichiers non exportés.
        smileys = self.lire("Smileys", "PROVENANCE.md")
        self.assertIn("`clips/smileys/<n>.swf`", smileys)
        self.assertIn("1 PNG", smileys)
        self.assertIn("exporter_icons.py", smileys)
        self.assertIn("--familles Smileys\n", smileys)
        self.assertIn("`clips/smileys/2.swf` : rendu vide", smileys)
        self.assertIn("`clips/artworks/big/7.swf` : RuntimeError", self.lire("Portraits", "PROVENANCE.md"))
        self.assertIn("2 PNG", self.lire("Items", "PROVENANCE.md"))
        self.assertIn("0 PNG", self.lire("Emotes", "PROVENANCE.md"), "famille absente du client : provenance vide, pas d'erreur")

    def test_options_invalides(self):
        for options in (["--familles", "Inconnue"], ["--swfsvg", os.path.join(self.dossier, "absent")]):
            r = subprocess.run([sys.executable, EXPORTEUR, "--client", self.client, "--sortie", self.sortie] + options,
                               capture_output=True, encoding="utf-8")
            self.assertNotEqual(r.returncode, 0)
        self.assertFalse(os.path.exists(self.sortie), "rien n'est écrit sur une option invalide")


if __name__ == "__main__":
    unittest.main()

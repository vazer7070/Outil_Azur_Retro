#!/usr/bin/env python3
"""Fichiers de langue du client Dofus 1.34 -> XML du bot (ressources/Bot/BotLang).

Un fichier de langue (`lang/swf/<famille>_<langue>_<version>.swf`) est un SWF d'une
image dont l'unique DoAction affecte des objets AS2 (`D.q[693] = "..."`,
`N.d[63] = {"a": [1, 3], "n": "..."}`). La chaîne est celle du README :
`avm1dump` (désassemblage) puis `as2lite.py` (pseudo-décompilation, une affectation
par ligne), puis ce script, qui relit les affectations littérales, reconstruit
l'arbre des objets et écrit un XML par famille : un élément par entrée, des
attributs typés, encodage UTF-8, `version` = numéro du SWF.

Usage :
  python3 lang2xml.py <dossier lang/swf> <dossier de sortie> [--familles dialog,npc|bot|toutes]
                      [--langue fr] [--avm1dump CHEMIN] [--travail DOSSIER]
  python3 lang2xml.py --as <fichier.as.txt> --famille npc --version 508 --sortie npc.xml

Les affectations qui ne sont pas des littéraux (le code de `states_fr`, par exemple)
sont ignorées et comptées ; rien n'est exécuté.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ICI = os.path.dirname(os.path.abspath(__file__))


# ---------------------------------------------------------------------------
# Lecture des affectations produites par as2lite.py
# ---------------------------------------------------------------------------

class NonLitteral(Exception):
    """L'expression n'est pas un littéral (code, appel, variable) : la ligne est ignorée."""


NOMBRE = re.compile(r"-?(?:\d+(?:\.\d*)?(?:[eE][+-]?\d+)?|NaN|inf)")
IDENT = re.compile(r"[A-Za-z_$][\w$]*")
ECHAPPEMENTS = {"n": "\n", "r": "\r", "t": "\t", '"': '"', "\\": "\\", "'": "'"}


class Lecteur:
    """Analyse descendante des littéraux tels qu'avm1dump/as2lite les écrivent."""

    def __init__(self, texte):
        self.t = texte
        self.i = 0

    def fin(self):
        self.espaces()
        return self.i >= len(self.t)

    def espaces(self):
        while self.i < len(self.t) and self.t[self.i] in " \t":
            self.i += 1

    def regarde(self, s):
        self.espaces()
        return self.t.startswith(s, self.i)

    def attendre(self, s):
        if not self.regarde(s):
            raise NonLitteral("« %s » attendu en %d" % (s, self.i))
        self.i += len(s)

    def mot(self, m):
        self.espaces()
        j = self.i + len(m)
        if self.t.startswith(m, self.i) and (j >= len(self.t) or not (self.t[j].isalnum() or self.t[j] in "_$")):
            self.i = j
            return True
        return False

    def chaine(self):
        self.attendre('"')
        sortie = []
        while True:
            if self.i >= len(self.t):
                raise NonLitteral("chaîne non terminée")
            c = self.t[self.i]
            if c == '"':
                self.i += 1
                return "".join(sortie)
            if c == "\\":
                if self.i + 1 >= len(self.t):
                    raise NonLitteral("échappement final")
                e = self.t[self.i + 1]
                if e == "x" and re.match(r"[0-9a-fA-F]{2}", self.t[self.i + 2:self.i + 4]):
                    sortie.append(chr(int(self.t[self.i + 2:self.i + 4], 16)))
                    self.i += 4
                    continue
                sortie.append(ECHAPPEMENTS.get(e, e))
                self.i += 2
                continue
            sortie.append(c)
            self.i += 1

    def valeur(self):
        self.espaces()
        if self.i >= len(self.t):
            raise NonLitteral("valeur attendue")
        c = self.t[self.i]
        if c == '"':
            return self.chaine()
        if c == "{":
            self.i += 1
            objet = {}
            if self.regarde("}"):
                self.i += 1
                return objet
            while True:
                cle = cle_texte(self.valeur())
                self.attendre(":")
                objet[cle] = self.valeur()
                if self.regarde(","):
                    self.i += 1
                    continue
                self.attendre("}")
                return objet
        if c == "[":
            self.i += 1
            liste = []
            if self.regarde("]"):
                self.i += 1
                return liste
            while True:
                liste.append(self.valeur())
                if self.regarde(","):
                    self.i += 1
                    continue
                self.attendre("]")
                return liste
        m = NOMBRE.match(self.t, self.i)
        if m and (m.end() >= len(self.t) or not (self.t[m.end()].isalnum() or self.t[m.end()] in "_$")):
            self.i = m.end()
            return nombre(m.group(0))
        for mot, val in (("true", True), ("false", False), ("null", None), ("undefined", None)):
            if self.mot(mot):
                return val
        for constructeur in ("new Object()", "new Array()"):
            if self.regarde(constructeur):
                self.i += len(constructeur)
                return {}
        for conversion in ("String", "Number", "Boolean"):
            if self.regarde(conversion + "("):
                self.i += len(conversion) + 1
                v = self.valeur()
                self.attendre(")")
                return v
        raise NonLitteral("expression non littérale en %d" % self.i)

    def chemin(self):
        """`N.d[63]` -> ["N", "d", "63"]."""
        self.espaces()
        m = IDENT.match(self.t, self.i)
        if not m:
            raise NonLitteral("nom attendu")
        self.i = m.end()
        parties = [m.group(0)]
        while True:
            if self.t.startswith(".", self.i):
                m = IDENT.match(self.t, self.i + 1)
                if not m:
                    raise NonLitteral("membre attendu")
                self.i = m.end()
                parties.append(m.group(0))
            elif self.t.startswith("[", self.i):
                self.i += 1
                parties.append(cle_texte(self.valeur()))
                self.attendre("]")
            else:
                return parties


def nombre(texte):
    if texte in ("NaN", "inf", "-inf"):
        return float(texte)
    if re.fullmatch(r"-?\d+", texte):
        return int(texte)
    v = float(texte)
    return int(v) if v.is_integer() and abs(v) < 2 ** 53 else v


def cle_texte(v):
    """Clé d'objet AS2 : toujours une chaîne (`5`, `-1`, `1|2`)."""
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    if isinstance(v, (int, float)):
        return str(v)
    if isinstance(v, str):
        return v
    raise NonLitteral("clé non scalaire")


def affecter(racines, chemin, valeur):
    noeud = racines
    for cle in chemin[:-1]:
        suivant = noeud.get(cle)
        if isinstance(suivant, list):
            suivant = {str(i): v for i, v in enumerate(suivant)}
            noeud[cle] = suivant
        elif not isinstance(suivant, dict):
            suivant = {}
            noeud[cle] = suivant
        noeud = suivant
    noeud[chemin[-1]] = valeur


def lire_affectations(lignes):
    """Rejoue les affectations littérales. Renvoie (racines, nombre de lignes ignorées, VERSION interne)."""
    racines = {}
    ignorees = 0
    version = None
    for brute in lignes:
        ligne = brute.strip()
        if not ligne or ligne.startswith("////") or ligne.startswith("/*") or ligne.startswith("@"):
            continue
        if not ligne.endswith(";"):
            ignorees += 1
            continue
        lecteur = Lecteur(ligne[:-1])
        try:
            chemin = lecteur.chemin()
            lecteur.attendre("=")
            valeur = lecteur.valeur()
            if not lecteur.fin():
                raise NonLitteral("texte après la valeur")
        except NonLitteral:
            ignorees += 1
            continue
        if len(chemin) == 1 and chemin[0] in ("FILE_BEGIN", "FILE_END"):
            continue
        if chemin == ["VERSION"]:
            version = valeur
            continue
        affecter(racines, chemin, valeur)
    return racines, ignorees, version


# ---------------------------------------------------------------------------
# Description des familles : quelles tables, quels noms d'éléments et d'attributs
# ---------------------------------------------------------------------------

class Table:
    """Une table AS2 (`D.q`, `MA.m`…) exportée en éléments `<element id="…">`.

    `valeur` : nom de l'attribut quand l'entrée est un scalaire (`D.q[1] = "…"`).
    `noms` : renommage des champs connus ; les autres gardent leur clé AS2.
    `garder` : si fourni, seuls ces champs sont exportés.
    """

    def __init__(self, chemin, element, noms=None, valeur=None, garder=None, enfants=None):
        self.chemin = chemin
        self.element = element
        self.noms = noms or {}
        self.valeur = valeur
        self.garder = garder
        self.enfants = enfants


def grades_monstre(cle, v):
    """`g1…g10` d'un monstre -> `<grade n="1" niveau="2" resistances="…"/>`."""
    m = re.fullmatch(r"g(\d+)", cle)
    if not m or not isinstance(v, dict):
        return None
    e = ET.Element("grade", {"n": m.group(1)})
    for k in sorted(v):
        nom = {"l": "niveau", "r": "resistances"}.get(k, k)
        poser(e, nom, v[k])
    return e


FAMILLES = {
    "dialog": [Table("D.q", "question", valeur="texte"), Table("D.a", "reponse", valeur="texte")],
    "npc": [Table("N.a", "action", valeur="nom"), Table("N.d", "pnj", {"n": "nom", "a": "actions"})],
    "maps": [
        Table("MA.m", "carte", {"x": "x", "y": "y", "sa": "sousZone", "ep": "episode", "p1": "placementEquipe1",
                                "p2": "placementEquipe2", "c": "maxDefi", "t": "maxEquipe", "d": "donjon"}),
        Table("MA.sa", "sousZone", {"n": "nom", "a": "zone", "v": "voisines", "m": "musiques"}),
        Table("MA.a", "zone", {"n": "nom", "sua": "superZone"}),
        Table("MA.sua", "superZone", valeur="nom"),
    ],
    "monsters": [
        Table("M", "monstre", {"n": "nom", "g": "gfx", "b": "race", "a": "alignement", "k": "expulsable"},
              enfants=grades_monstre),
        Table("MR", "race", {"n": "nom", "s": "superRace"}),
        Table("MSR", "superRace", {"n": "nom"}),
    ],
    "items": [
        Table("I.u", "objet", {"n": "nom", "t": "type", "g": "gfx", "l": "niveau", "w": "pods", "p": "prix",
                               "d": "description", "c": "conditions", "s": "panoplie", "fm": "forgemageable",
                               "tw": "deuxMains", "et": "ethere", "h": "cache", "m": "maudit", "u": "utilisable",
                               "ut": "ciblable", "e": "arme", "ep": "episode"}),
        Table("I.t", "type", {"n": "nom", "t": "superType", "z": "zone"}),
        Table("I.st", "superType", valeur="valeur"),
        Table("I.ss", "emplacements", valeur="emplacements"),
        Table("I.us", "texteUnique", valeur="texte"),
    ],
    "spells": [Table("S", "sort", {"n": "nom", "d": "description"}, garder=("n", "d"))],
    "emotes": [Table("EM", "emote", {"n": "nom", "s": "commande"})],
    "interactiveobjects": [
        Table("IO.d", "interactif", {"n": "nom", "t": "type", "sk": "competences"}),
        Table("IO.g", "gfx", valeur="interactif"),
    ],
    "skills": [Table("SK", "competence", {"d": "nom", "j": "metier", "io": "interactif", "c": "condition",
                                          "f": "forgemagie"})],
    "jobs": [Table("J", "metier", {"n": "nom", "s": "specialisation", "g": "icone"})],
    "titles": [Table("PT", "titre", {"t": "texte", "c": "couleur", "pt": "typeParametre"})],
    "quests": [
        Table("Q.q", "quete", valeur="nom"),
        Table("Q.s", "etape", {"n": "nom", "d": "description", "r": "recompenses"}),
        Table("Q.o", "objectif", {"t": "type", "p": "parametres"}),
        Table("Q.t", "modele", valeur="texte"),
    ],
    # Familles exportées telles quelles (`<entree table="…" id="…">`, clés AS2 d'origine),
    # pour les lots qui en auront besoin (alignement, maisons, montures, panoplies…).
    "alignment": ["A.a", "A.o", "A.s", "A.f", "A.fe", "A.b", "A.jo", "A.at", "A.g"],
    "classes": ["G"],
    "effects": ["E", "EDMG", "EHEL"],
    "itemsets": ["IS"],
    "crafts": ["CR"],
    "hints": ["HI", "HIC"],
    "houses": ["H.h", "H.m", "H.d", "H.ids"],
    "rides": ["RI", "RIA"],
    "fightChallenge": ["FC"],
    "states": ["ST"],
    "guilds": ["GU"],
    "ranks": ["R"],
    "pvp": ["PP"],
    "servers": ["SR", "SRC", "SRP", "SRPW", "SRVT", "SRVC"],
    "dungeons": ["DU"],
    # Raccourcis clavier (`shortcuts_fr`) : jeux de touches `SST`, catégories `SSC`, raccourcis `SH`,
    # touches `SSK["<jeu>|<NOM>"]` (`k` code de touche, `c` modificateurs 0/1 Ctrl/2 Maj/3 Ctrl+Maj, `s` libellé).
    "shortcuts": ["SST", "SSC", "SH", "SSK"],
}

# Familles lues par LangData (le lot D4) : celles du tableau de la fiche, plus `lang`.
FAMILLES_BOT = ["dialog", "npc", "maps", "monsters", "items", "spells", "emotes", "lang",
                "interactiveobjects", "skills", "jobs", "titles", "quests"]
FAMILLES_TOUTES = FAMILLES_BOT + [f for f in FAMILLES if f not in FAMILLES_BOT]


# ---------------------------------------------------------------------------
# Écriture XML
# ---------------------------------------------------------------------------

INVALIDE_XML = re.compile("[^\x09\x0A\x0D\x20-\uD7FF\uE000-\uFFFD\U00010000-\U0010FFFF]")
NOM_XML = re.compile(r"[A-Za-z_][\w.-]*\Z")


class Stats:
    def __init__(self):
        self.remplaces = 0


STATS = Stats()


def texte_xml(s):
    propre, n = INVALIDE_XML.subn("\uFFFD", s)
    STATS.remplaces += n
    return propre


def encoder(v):
    """Valeur AS2 -> texte d'attribut ; None = attribut absent."""
    if v is None:
        return None
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        return repr(v) if not v.is_integer() else str(int(v))
    if isinstance(v, str):
        return texte_xml(v)
    if isinstance(v, list) and all(isinstance(x, (int, float, bool)) for x in v):
        return ",".join(encoder(x) for x in v)
    # Structure imbriquée (tableau de chaînes, objets, null…) : JSON compact.
    return texte_xml(json.dumps(v, ensure_ascii=False, separators=(",", ":")))


def poser(element, nom, v):
    texte = encoder(v)
    if texte is None:
        return
    if not NOM_XML.match(nom):
        raise ValueError("nom d'attribut invalide : %r" % nom)
    element.set(nom, texte)


def cle_tri(cle):
    return (0, int(cle), "") if re.fullmatch(r"-?\d+", cle) else (1, 0, cle)


def entrees(noeud):
    if isinstance(noeud, dict):
        return sorted(noeud.items(), key=lambda kv: cle_tri(kv[0]))
    if isinstance(noeud, list):
        return [(str(i), v) for i, v in enumerate(noeud)]
    return None


def trouver(racines, chemin):
    noeud = racines
    for partie in chemin.split("."):
        if not isinstance(noeud, dict) or partie not in noeud:
            return None
        noeud = noeud[partie]
    return noeud


def element_entree(table, cle, v):
    e = ET.Element(table.element, {"id": texte_xml(cle)})
    if table.valeur and not isinstance(v, dict):
        poser(e, table.valeur, v)
        return e
    if not isinstance(v, dict):
        poser(e, "valeur", v)
        return e
    noms = list(table.noms)
    for k in noms + sorted(k for k in v if k not in table.noms):
        if k not in v or (table.garder is not None and k not in table.garder):
            continue
        if table.enfants:
            enfant = table.enfants(k, v[k])
            if enfant is not None:
                e.append(enfant)
                continue
        nom = table.noms.get(k, k)
        if NOM_XML.match(nom) and nom != "id":
            poser(e, nom, v[k])
        else:
            e.append(element_brut(k, v[k]))
    return e


def element_brut(cle, v):
    """Champ dont la clé n'est pas un nom d'attribut XML : `<champ cle="…" valeur="…"/>`."""
    e = ET.Element("champ", {"cle": texte_xml(cle)})
    poser(e, "valeur", v)
    return e


def element_generique(table, cle, v):
    e = ET.Element("entree", {"table": table, "id": texte_xml(cle)})
    if isinstance(v, dict):
        for k in sorted(v):
            if NOM_XML.match(k) and k not in ("id", "table"):
                poser(e, k, v[k])
            else:
                e.append(element_brut(k, v[k]))
    else:
        poser(e, "valeur", v)
    return e


def exporter_famille(famille, racines):
    """Renvoie (liste d'éléments, {table: nombre}, [avertissements])."""
    elements, comptes, avertissements = [], {}, []
    spec = FAMILLES.get(famille)
    if famille == "lang":
        return exporter_lang(racines)
    if spec is None:
        raise SystemExit("Famille inconnue : %s (connues : %s)" % (famille, ", ".join(FAMILLES_TOUTES)))
    for table in spec:
        chemin = table.chemin if isinstance(table, Table) else table
        lignes = entrees(trouver(racines, chemin))
        if lignes is None:
            avertissements.append("table %s absente" % chemin)
            continue
        for cle, v in lignes:
            if isinstance(table, Table):
                elements.append(element_entree(table, cle, v))
            else:
                elements.append(element_generique(chemin, cle, v))
        comptes[chemin] = len(lignes)
    return elements, comptes, avertissements


def exporter_lang(racines):
    """`lang_fr` : textes d'interface (`ACCEPT = "…"`, `INFOS_54 = "…"`) + `C.*` + petites tables."""
    elements, comptes, avertissements = [], {"texte": 0, "C": 0}, []
    for cle in sorted(racines):
        v = racines[cle]
        if isinstance(v, dict):
            continue
        e = ET.Element("texte", {"cle": cle})
        poser(e, "valeur", v)
        elements.append(e)
        comptes["texte"] += 1
    for cle, v in entrees(racines.get("C", {})) or []:
        e = ET.Element("config", {"cle": cle})
        poser(e, "valeur", v)
        elements.append(e)
        comptes["C"] += 1
    for table in ("CSR", "COM", "CNS", "ABR"):
        lignes = entrees(racines.get(table))
        if lignes is None:
            avertissements.append("table %s absente" % table)
            continue
        for cle, v in lignes:
            elements.append(element_generique(table, cle, v))
        comptes[table] = len(lignes)
    return elements, comptes, avertissements


def ecrire_xml(chemin_sortie, famille, langue, version, source, elements):
    racine = ET.Element("BotLang", {"famille": famille, "langue": langue, "version": str(version), "source": source})
    racine.text = "\n"
    for e in elements:
        e.tail = "\n"
        racine.append(e)
    arbre = ET.ElementTree(racine)
    os.makedirs(os.path.dirname(os.path.abspath(chemin_sortie)), exist_ok=True)
    temporaire = chemin_sortie + ".tmp"
    with open(temporaire, "wb") as f:
        arbre.write(f, encoding="utf-8", xml_declaration=True)
        f.write(b"\n")
    os.replace(temporaire, chemin_sortie)


def convertir_as(lignes, famille, langue, version, source, chemin_sortie):
    STATS.remplaces = 0
    racines, ignorees, version_interne = lire_affectations(lignes)
    elements, comptes, avertissements = exporter_famille(famille, racines)
    ecrire_xml(chemin_sortie, famille, langue, version, source, elements)
    return {"comptes": comptes, "ignorees": ignorees, "avertissements": avertissements,
            "version_interne": version_interne, "remplaces": STATS.remplaces}


# ---------------------------------------------------------------------------
# Désassemblage des SWF
# ---------------------------------------------------------------------------

def lire_versions(dossier_swf, langue):
    """`versions_<langue>.txt` (`&f=dialog,fr,520|npc,fr,508|…`) du dossier `lang/`, s'il existe."""
    for dossier in (os.path.dirname(os.path.abspath(dossier_swf)), dossier_swf):
        chemin = os.path.join(dossier, "versions_%s.txt" % langue)
        if os.path.isfile(chemin):
            with open(chemin, encoding="utf-8", errors="replace") as f:
                texte = f.read()
            versions = {}
            for m in re.finditer(r"([A-Za-z]+),%s,(\d+)" % re.escape(langue), texte):
                versions[m.group(1)] = int(m.group(2))
            return versions, chemin
    return {}, None


def choisir_swf(dossier_swf, famille, langue, versions):
    """Version annoncée par versions_<langue>.txt si son SWF existe, sinon la plus haute présente."""
    voulue = versions.get(famille)
    if voulue is not None:
        chemin = os.path.join(dossier_swf, "%s_%s_%d.swf" % (famille, langue, voulue))
        if os.path.isfile(chemin):
            return chemin, voulue
    trouvees = []
    motif = re.compile(r"%s_%s_(\d+)\.swf\Z" % (re.escape(famille), re.escape(langue)))
    for nom in os.listdir(dossier_swf):
        m = motif.match(nom)
        if m:
            trouvees.append(int(m.group(1)))
    if not trouvees:
        return None, None
    v = max(trouvees)
    return os.path.join(dossier_swf, "%s_%s_%d.swf" % (famille, langue, v)), v


def trouver_avm1dump(chemin):
    candidats = [chemin, os.environ.get("AVM1DUMP"),
                 os.path.join(ICI, "avm1dump", "target", "release", "avm1dump"),
                 os.path.join(ICI, "avm1dump", "target", "release", "avm1dump.exe")]
    for c in candidats:
        if c and os.path.isfile(c):
            return c
    raise SystemExit("avm1dump introuvable : compilez-le (cargo build --release --manifest-path "
                     "tools/client-analysis/avm1dump/Cargo.toml) ou indiquez --avm1dump / AVM1DUMP.")


def desassembler(swf, avm1dump, travail):
    base = os.path.join(travail, os.path.splitext(os.path.basename(swf))[0])
    subprocess.run([avm1dump, swf, base + ".avm1.txt"], check=True)
    subprocess.run([sys.executable, os.path.join(ICI, "as2lite.py"), base + ".avm1.txt", base + ".as.txt"], check=True)
    with open(base + ".as.txt", encoding="utf-8") as f:
        return f.read().split("\n")


def main():
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    p.add_argument("dossier_swf", nargs="?", help="dossier lang/swf du client ou du pack Lang")
    p.add_argument("sortie", nargs="?", help="dossier de sortie (ressources/Bot/BotLang)")
    p.add_argument("--familles", default="bot", help="liste séparée par des virgules, « bot » (défaut) ou « toutes »")
    p.add_argument("--langue", default="fr")
    p.add_argument("--avm1dump")
    p.add_argument("--travail", help="dossier des fichiers intermédiaires (conservés)")
    p.add_argument("--as", dest="fichier_as", help="convertir une pseudo-décompilation existante")
    p.add_argument("--famille")
    p.add_argument("--version", type=int)
    p.add_argument("--sortie", dest="sortie_fichier")
    a = p.parse_args()

    if a.fichier_as:
        if not (a.famille and a.sortie_fichier):
            p.error("--as demande --famille et --sortie")
        with open(a.fichier_as, encoding="utf-8") as f:
            r = convertir_as(f.read().split("\n"), a.famille, a.langue, a.version if a.version is not None else 0,
                             os.path.basename(a.fichier_as), a.sortie_fichier)
        print("%s : %s ; %d ligne(s) ignorée(s)" % (a.famille, r["comptes"], r["ignorees"]))
        return

    if not (a.dossier_swf and a.sortie):
        p.error("indiquez le dossier lang/swf et le dossier de sortie")
    if a.familles == "bot":
        familles = FAMILLES_BOT
    elif a.familles == "toutes":
        familles = FAMILLES_TOUTES
    else:
        familles = [f.strip() for f in a.familles.split(",") if f.strip()]
    avm1dump = trouver_avm1dump(a.avm1dump)
    versions, fichier_versions = lire_versions(a.dossier_swf, a.langue)
    if fichier_versions:
        print("Versions : %s" % fichier_versions)
    travail = a.travail or tempfile.mkdtemp(prefix="lang2xml-")
    os.makedirs(travail, exist_ok=True)
    erreurs = 0
    for famille in familles:
        swf, version = choisir_swf(a.dossier_swf, famille, a.langue, versions)
        if swf is None:
            print("%-18s absent du dossier" % famille)
            erreurs += 1
            continue
        lignes = desassembler(swf, avm1dump, travail)
        sortie = os.path.join(a.sortie, famille + ".xml")
        r = convertir_as(lignes, famille, a.langue, version, os.path.basename(swf), sortie)
        details = ", ".join("%s %d" % kv for kv in r["comptes"].items())
        print("%-18s %s -> %s (%d octets) : %s ; %d ligne(s) ignorée(s)%s%s" % (
            famille, os.path.basename(swf), os.path.basename(sortie), os.path.getsize(sortie), details, r["ignorees"],
            "".join(" ; " + x for x in r["avertissements"]),
            " ; %d caractère(s) invalide(s) remplacé(s)" % r["remplaces"] if r["remplaces"] else ""))
    if not a.travail:
        print("Fichiers intermédiaires : %s" % travail)
    sys.exit(1 if erreurs else 0)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Choisit les gfx de monstres à animer d'après leur présence sur les cartes d'un dump StarLoco.

usage : choisir_gfx_animes.py <game.sql> [--sprites <client>/clips/sprites] [--swfsvg CHEMIN]
                              [--familles walk,run,hit,die,anim0] [--pas 2] [--budget-mo 50]
                              [--nombre 100] [--ko-par-image 2.5] [--exclure sprites_animes.txt]
                              [--sans-fixes] [--tableau FICHIER]

Écrit sur la sortie standard des lignes au format de sprites_animes.txt (« <gfx> walk:2,run:2,... »),
triées par gfx, précédées de commentaires (#) qui décrivent la sélection : on les ajoute au fichier
avec >> puis on lance exporter_sprites.py (voir Resources/Bot/sprites/PROVENANCE.md).

Métrique : les présences d'un gfx. Le dump est lu comme StarLoco le charge (GameMap, Monster.MobGroup) :
- carte : chaque entrée « <monstre>,<niveau> » de maps.monsters (séparées par |) compte 1 pour le gfx
  du monstre (monsters.gfxID), si la carte fait apparaître des groupes (numgroup > 0), si le monstre
  existe et si l'un de ses grades a ce niveau. Quand StarLoco compose un groupe, il tire ses membres
  dans cette liste d'entrées : un monstre listé à trois niveaux sort trois fois plus souvent ;
- groupe fixe (mobgroups_fix, donjons) : chaque monstre « <monstre>,<min>,<max> » de groupData compte
  1, si la carte existe et si le monstre a un grade entre min et max (--sans-fixes pour les ignorer).
La part d'un gfx est ses présences divisées par le total des présences de tous les gfx. Non comptés :
les monstres ajoutés au hasard par extra_monster, les substitutions d'Halloween ou de Noël, les
invocations et les monstres de quêtes, que le dump ne place pas sur une carte.

Classement : présences décroissantes, puis gfx croissant. Avec --sprites, seules les familles que le
SWF exporte (symbole <famille><O> d'au moins deux images au pas choisi, R, L, S, F ou B) sont gardées,
un gfx sans SWF ou sans aucune de ces familles est écarté (commentaire), et la taille estimée vaut
images exportées (une sur « pas ») x --ko-par-image (2,5 Kio : export réel des monstres, 1,9 à
3,2 Kio par image). Sans --sprites, toutes les familles sont écrites et la taille n'est pas estimée.
La sélection s'arrête au --nombre-ième gfx ou au premier gfx qui ferait dépasser --budget-mo.
"""
import argparse, collections, os, re, subprocess, sys

FAMILLES = "walk,run,hit,die,anim0"
ORIENTATIONS = "SRLFB"
IPS = 40
FAMILLE = re.compile(r"^[A-Za-z][A-Za-z0-9]*$")
INSERT = re.compile(r"^INSERT INTO `(\w+)` VALUES ")
CREATE = re.compile(r"^CREATE TABLE `(\w+)` \(")
COLONNE = re.compile(r"^\s*`(\w+)` ")
ECHAPPES = {"0": "\0", "b": "\b", "n": "\n", "r": "\r", "t": "\t", "Z": "\x1a"}


# ------------------------------------------------------------------ lecture du dump SQL

def valeurs_sql(texte, debut):
    """Tuples d'un INSERT ... VALUES (...),(...); à partir de texte[debut] : liste de listes de chaînes
    (None pour NULL). ValueError si la ligne est mal formée."""
    tuples, i, n = [], debut, len(texte)
    while True:
        while i < n and texte[i] in " \t": i += 1
        if i >= n or texte[i] != "(":
            raise ValueError("« ( » attendu en colonne %d" % (i + 1))
        i += 1
        courant = []
        while True:
            while i < n and texte[i] in " \t": i += 1
            if i >= n:
                raise ValueError("tuple non fermé")
            if texte[i] == "'":
                i += 1
                morceaux = []
                while True:
                    if i >= n:
                        raise ValueError("chaîne non fermée")
                    c = texte[i]
                    if c == "\\" and i + 1 < n:
                        morceaux.append(ECHAPPES.get(texte[i + 1], texte[i + 1])); i += 2
                    elif c == "'" and i + 1 < n and texte[i + 1] == "'":
                        morceaux.append("'"); i += 2
                    elif c == "'":
                        i += 1; break
                    else:
                        morceaux.append(c); i += 1
                courant.append("".join(morceaux))
            else:
                j = i
                while j < n and texte[j] not in ",)": j += 1
                brut = texte[i:j].strip()
                courant.append(None if brut.upper() == "NULL" else brut)
                i = j
            while i < n and texte[i] in " \t": i += 1
            if i < n and texte[i] == ",":
                i += 1; continue
            if i < n and texte[i] == ")":
                i += 1; break
            raise ValueError("« , » ou « ) » attendu en colonne %d" % (i + 1))
        tuples.append(courant)
        while i < n and texte[i] in " \t": i += 1
        if i < n and texte[i] == ",":
            i += 1; continue
        if i < n and texte[i] == ";" and not texte[i + 1:].strip():
            return tuples
        raise ValueError("« ; » attendu en fin d'INSERT")


def lire_tables(chemin, voulues, avertir=None):
    """{table: [dict colonne -> valeur]} des tables voulues d'un dump MySQL (une instruction INSERT par
    ligne, éventuellement à plusieurs tuples). Les colonnes viennent du CREATE TABLE du dump ; une ligne
    illisible est signalée à avertir(message) et ignorée."""
    colonnes, lignes = {}, {t: [] for t in voulues}
    en_cours = None
    with open(chemin, encoding="utf-8", errors="replace") as f:
        for numero, ligne in enumerate(f, 1):
            ligne = ligne.rstrip("\r\n")
            m = CREATE.match(ligne)
            if m:
                en_cours = m.group(1) if m.group(1) in voulues else None
                if en_cours: colonnes[en_cours] = []
                continue
            if en_cours:
                c = COLONNE.match(ligne)
                if c: colonnes[en_cours].append(c.group(1))
                elif ligne.startswith(")"): en_cours = None
                continue
            m = INSERT.match(ligne)
            if not m or m.group(1) not in voulues:
                continue
            table = m.group(1)
            if table not in colonnes:
                if avertir: avertir("ligne %d : INSERT dans %s avant son CREATE TABLE, ignoré" % (numero, table))
                continue
            try:
                for t in valeurs_sql(ligne, m.end()):
                    if len(t) != len(colonnes[table]):
                        raise ValueError("%d valeurs pour %d colonnes" % (len(t), len(colonnes[table])))
                    lignes[table].append(dict(zip(colonnes[table], t)))
            except ValueError as erreur:
                if avertir: avertir("ligne %d (%s) illisible : %s" % (numero, table, erreur))
    manquantes = [t for t in voulues if t not in colonnes]
    if manquantes:
        raise ValueError("tables absentes du dump : " + ", ".join(manquantes))
    return lignes


# ------------------------------------------------------------------ règles de StarLoco

def java_split(texte, separateur):
    """String.split de Java avec un séparateur littéral : les chaînes vides de la fin sont retirées."""
    morceaux = texte.split(separateur)
    while morceaux and morceaux[-1] == "":
        morceaux.pop()
    return morceaux if texte != "" else [""]


def entier(texte):
    """Integer.parseInt de Java (signe facultatif, chiffres seulement) ; None si illisible."""
    return int(texte) if texte is not None and re.fullmatch(r"[+-]?\d+", texte) else None


def niveaux(monstre):
    """Niveaux des grades que StarLoco charge (Monster) : les 12 premiers morceaux de grades
    « <niveau>@<résistances> » dont le niveau se lit, les résistances existent, et qui ont leurs
    statistiques (et leurs sorts, sauf liste vide, « |||| » ou « -1 ») au même rang. Approximation : la
    lecture détaillée des résistances et des statistiques (MobGrade) n'est pas refaite."""
    grades = java_split(monstre.get("grades") or "", "|")
    stats = java_split(monstre.get("stats") or "", "|")
    sorts = monstre.get("spells") or ""
    sans_sorts = sorts.lower() in ("||||", "", "-1")
    sorts = java_split(sorts, "|")
    resultat = set()
    for n in range(12):
        if n >= len(grades) or n >= len(stats) or (not sans_sorts and n >= len(sorts)):
            continue
        infos = java_split(grades[n], "@")
        niveau = entier(infos[0]) if infos else None
        if niveau is None or len(infos) < 2:
            continue
        resultat.add(niveau)
    return resultat


def presences(tables, fixes=True, avertir=None):
    """(Counter gfx -> présences, Counter gfx -> cartes distinctes) d'après maps, monsters et
    mobgroups_fix (voir la docstring du module)."""
    monstres = {}
    for m in tables["monsters"]:
        ident, gfx = entier(m.get("id")), entier(m.get("gfxID"))
        if ident is None or gfx is None:
            if avertir: avertir("monstre illisible : id %r, gfxID %r" % (m.get("id"), m.get("gfxID")))
            continue
        monstres[ident] = (gfx, niveaux(m))
    compte, cartes = collections.Counter(), collections.defaultdict(set)
    connues = set()
    for carte in tables["maps"]:
        ident = entier(carte.get("id"))
        if ident is None:
            continue
        connues.add(ident)
        if (entier(carte.get("numgroup")) or 0) <= 0:
            continue
        for entree in (carte.get("monsters") or "").split("|"):
            if entree == "":
                continue
            morceaux = entree.split(",")
            monstre, niveau = entier(morceaux[0]), entier(morceaux[1]) if len(morceaux) > 1 else None
            if not monstre or not niveau or monstre not in monstres or niveau not in monstres[monstre][1]:
                continue
            gfx = monstres[monstre][0]
            compte[gfx] += 1
            cartes[gfx].add(ident)
    if fixes:
        for groupe in tables.get("mobgroups_fix", []):
            carte = entier(groupe.get("mapid"))
            if carte not in connues:
                continue
            for membre in (groupe.get("groupData") or "").split(";"):
                if membre == "":
                    continue
                morceaux = membre.split(",")
                if len(morceaux) < 3:
                    continue
                monstre, bas, haut = entier(morceaux[0]), entier(morceaux[1]), entier(morceaux[2])
                if monstre not in monstres or bas is None or haut is None:
                    continue
                if not any(bas <= v <= haut for v in monstres[monstre][1]):
                    continue
                gfx = monstres[monstre][0]
                compte[gfx] += 1
                cartes[gfx].add(carte)
    return compte, collections.Counter({g: len(c) for g, c in cartes.items()})


# ------------------------------------------------------------------ SWF du client

def trouver_swfsvg(chemin):
    ici = os.path.dirname(os.path.abspath(__file__))
    for c in [chemin, os.environ.get("SWFSVG"), os.path.join(ici, "swfsvg", "target", "release", "swfsvg"),
              os.path.join(ici, "swfsvg", "target", "release", "swfsvg.exe")]:
        if c and os.path.isfile(c):
            return [sys.executable, c] if c.endswith(".py") else [c]
    sys.exit("swfsvg introuvable : compilez tools/client-analysis/swfsvg (cargo build --release) ou passez --swfsvg")


def symboles(swfsvg, swf):
    """Nom en minuscules -> images utiles, d'après `swfsvg --list` ; None si le SWF est illisible."""
    sortie = subprocess.run(swfsvg + ["--list", swf], capture_output=True, encoding="utf-8", errors="replace")
    if sortie.returncode != 0:
        return None
    resultat = {}
    for ligne in sortie.stdout.splitlines()[1:]:
        col = ligne.split("\t")
        if len(col) >= 4 and col[2] != "scene" and col[3].isdigit():
            resultat.setdefault(col[0].lower(), int(col[3]))
    return resultat


def familles_du_swf(liste, familles, pas):
    """[(famille, images exportées)] des familles dont au moins une orientation garde deux images."""
    gardees = []
    for famille in familles:
        images = [-(-liste[famille.lower() + o.lower()] // pas) for o in ORIENTATIONS if famille.lower() + o.lower() in liste]
        images = [n for n in images if n >= 2]
        if images:
            gardees.append((famille, sum(images)))
    return gardees


# ------------------------------------------------------------------ sélection

def lire_exclus(chemins):
    exclus = set()
    for chemin in chemins:
        with open(chemin, encoding="utf-8") as f:
            for ligne in f:
                mot = ligne.split("#", 1)[0].split()
                if mot and mot[0].isdigit():
                    exclus.add(int(mot[0]))
    return exclus


def choisir(compte, cartes, familles, pas, nombre, budget_ko, ko_par_image, liste_swf=None, exclus=()):
    """Sélection : [(gfx, présences, cartes, [(famille, images)], ko estimés)], écartés [(gfx, raison)]
    et raison de l'arrêt. liste_swf(gfx) rend les symboles du SWF (dict) ou None ; sans elle, toutes les
    familles sont gardées sans estimation de taille."""
    choix, ecartes, total, arret = [], [], 0.0, "plus aucun gfx présent"
    for gfx, n in sorted(compte.items(), key=lambda e: (-e[1], e[0])):
        if gfx in exclus:
            ecartes.append((gfx, "déjà dans sprites_animes.txt"))
            continue
        if len(choix) >= nombre:
            arret = "%d gfx atteints" % nombre
            break
        if liste_swf is None:
            choix.append((gfx, n, cartes[gfx], [(f, None) for f in familles], None))
            continue
        liste = liste_swf(gfx)
        if liste is None:
            ecartes.append((gfx, "SWF absent ou illisible"))
            continue
        gardees = familles_du_swf(liste, familles, pas)
        if not gardees:
            ecartes.append((gfx, "aucune des familles " + ",".join(familles)))
            continue
        ko = sum(images for _, images in gardees) * ko_par_image
        if budget_ko is not None and total + ko > budget_ko:
            arret = "budget de %g Mio atteint (gfx %d : %.1f Mio de plus)" % (budget_ko / 1024, gfx, ko / 1024)
            break
        total += ko
        choix.append((gfx, n, cartes[gfx], gardees, ko))
    return choix, ecartes, arret


def main():
    p = argparse.ArgumentParser(description="gfx de monstres les plus présents sur les cartes StarLoco -> lignes de sprites_animes.txt")
    p.add_argument("dump", help="game.sql de StarLoco (tables maps, monsters, mobgroups_fix)")
    p.add_argument("--sprites", help="dossier clips/sprites du client : familles réellement exportées et taille estimée")
    p.add_argument("--swfsvg", help="binaire swfsvg 0.2.3+ (sinon $SWFSVG ou swfsvg/target/release)")
    p.add_argument("--familles", default=FAMILLES, help="familles à animer (défaut " + FAMILLES + ")")
    p.add_argument("--pas", type=int, default=2, help="pas d'export : une image sur N (diviseur de 40, défaut 2)")
    p.add_argument("--nombre", type=int, default=100, help="nombre maximal de gfx (défaut 100)")
    p.add_argument("--budget-mo", type=float, help="taille estimée maximale en Mio (exige --sprites)")
    p.add_argument("--ko-par-image", type=float, default=2.5, help="Kio par image exportée (défaut 2,5)")
    p.add_argument("--exclure", action="append", default=[], help="sprites_animes.txt existant : ses gfx sont sautés")
    p.add_argument("--sans-fixes", action="store_true", help="ignorer les groupes fixes (mobgroups_fix)")
    p.add_argument("--tableau", help="écrit ici le classement complet (gfx, présences, cartes, part, cumul) en TSV")
    a = p.parse_args()

    if a.pas < 1 or IPS % a.pas:
        sys.exit("--pas doit diviser %d" % IPS)
    familles = [f.strip() for f in a.familles.split(",") if f.strip()]
    if not familles or not all(FAMILLE.match(f) for f in familles) or "static" in [f.lower() for f in familles]:
        sys.exit("--familles : noms de familles animées séparés par des virgules (static est toujours exporté)")
    if a.budget_mo is not None and not a.sprites:
        sys.exit("--budget-mo exige --sprites (la taille dépend des images de chaque SWF)")
    messages = []
    try:
        tables = lire_tables(a.dump, ("maps", "monsters", "mobgroups_fix"), messages.append)
    except (OSError, ValueError) as erreur:
        sys.exit("dump illisible : %s" % erreur)
    compte, cartes = presences(tables, not a.sans_fixes, messages.append)
    total = sum(compte.values())
    if not total:
        sys.exit("aucun monstre placé sur une carte dans " + a.dump)

    liste_swf = None
    if a.sprites:
        swfsvg = trouver_swfsvg(a.swfsvg)
        cache = {}

        def liste_swf(gfx):
            if gfx not in cache:
                swf = os.path.join(a.sprites, "%d.swf" % gfx)
                cache[gfx] = symboles(swfsvg, swf) if os.path.isfile(swf) else None
            return cache[gfx]

    budget = a.budget_mo * 1024 if a.budget_mo is not None else None
    choix, ecartes, arret = choisir(compte, cartes, familles, a.pas, max(0, a.nombre), budget, a.ko_par_image,
                                    liste_swf, lire_exclus(a.exclure))

    if a.tableau:
        with open(a.tableau, "w", encoding="utf-8", newline="\n") as f:
            f.write("rang\tgfx\tpresences\tcartes\tpart\tcumul\n")
            cumul = 0
            for rang, (gfx, n) in enumerate(sorted(compte.items(), key=lambda e: (-e[1], e[0])), 1):
                cumul += n
                f.write("%d\t%d\t%d\t%d\t%.4f\t%.4f\n" % (rang, gfx, n, cartes[gfx], n / total, cumul / total))

    pris = sum(n for _, n, _, _, _ in choix)
    print("# choisir_gfx_animes.py : %d gfx de monstres sur %d présents, %.1f %% des %d présences "
          "(entrées de maps.monsters%s)" % (len(choix), len(compte), 100.0 * pris / total, total,
                                            "" if a.sans_fixes else " et monstres de mobgroups_fix"))
    if any(ko is not None for *_, ko in choix):
        print("# taille estimée : %.1f Mio (%.1f Kio par image, pas %d) ; arrêt : %s"
              % (sum(ko for *_, ko in choix) / 1024, a.ko_par_image, a.pas, arret))
    else:
        print("# arrêt : %s" % arret)
    for gfx, raison in ecartes:
        print("# écarté %d : %s" % (gfx, raison))
    suffixe = "" if a.pas == 1 else ":%d" % a.pas
    for gfx, n, c, gardees, _ in sorted(choix):
        print("%d %s" % (gfx, ",".join(f + suffixe for f, _ in gardees)))
    for m in messages:
        sys.stderr.write(m + "\n")


if __name__ == "__main__":
    main()

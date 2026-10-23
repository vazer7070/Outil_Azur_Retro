#!/usr/bin/env python3
"""Relève le protocole du client Dofus 1.x à partir de la pseudo-décompilation d'as2lite.py.

usage : relever_protocole.py <loader.as.txt> <sortie.md> [dossier-tables]

Produit la référence Markdown (routes serveur → client, envois client → serveur,
lecture des réponses par domaine) et, si un dossier est indiqué, les tables
`routes.tsv` et `envois.tsv`. Le code décompilé lui-même n'est pas à versionner."""
import collections, os, re, sys

DOMAINS = collections.OrderedDict([
    ("Account", "Compte, serveurs et personnages"), ("Basics", "Base : date, commandes, messages"), ("Chat", "Discussion"),
    ("Game", "Carte, déplacements et combat"), ("GameActions", "Actions de jeu (GA)"), ("Items", "Objets et inventaire"),
    ("Spells", "Sorts"), ("Job", "Métiers"), ("Dialog", "Dialogues PNJ"), ("Exchange", "Échanges, boutique, HDV, artisanat"),
    ("Party", "Groupe"), ("Friends", "Amis"), ("Enemies", "Ennemis"), ("Guild", "Guilde"), ("Mount", "Montures"),
    ("Houses", "Maisons"), ("Waypoints", "Zaaps"), ("Subway", "Zaapis et prismes"), ("Quests", "Quêtes"),
    ("Emotes", "Émotes et direction"), ("Conquest", "Conquête"), ("Fights", "Liste des combats"), ("Infos", "Informations"),
    ("Storages", "Coffres et banque"), ("Key", "Codes de coffre"), ("Documents", "Documents"), ("Tutorial", "Tutoriel"),
    ("Specialization", "Spécialisation"), ("Subareas", "Sous-zones"), ("Handler", "Base des gestionnaires"),
])
KEEP = re.compile(r'split\(|\[\d+\]|charAt\(|substr\(|substring\(|parseInt|Number\(|=== "|== "|\.push\(|new Array|showMessage|getText\("')
SKIP = re.compile(r"unloadUIComponent|loadUIComponent|getUIComponent|^\s*@\d+:$|^\s*goto @|^\s*/\*|Logger|\.err\(|\.info\(|\.warn\(")
METHOD = re.compile(r"^(\s+)r1\.(\w+) = function \(([^)]*)\) \{\n(.*?)^\1\};", re.M | re.S)


def classes(text):
    """Blocs `//// __Packages.dofus.aks.<Classe>` de la sortie d'as2lite."""
    for block in re.split(r"^(?=//// )", text, flags=re.M):
        if block.startswith("//// __Packages.dofus.aks."):
            name = block.split("\n", 1)[0].replace("//// __Packages.dofus.aks.", "").split(" ")[0]
            yield name, block


def routes_of(dispatcher):
    """Reconstruit préfixe → gestionnaire à partir des `if ((r0 === "x")) goto` du répartiteur."""
    conds, current, var, rows = {}, [], None, []
    for line in dispatcher.split("\n"):
        s = line.strip()
        m = re.match(r"@(\d+):$", s)
        if m:
            current = conds.get(int(m.group(1)), current)
            continue
        m = re.match(r"r0 = (p\d|p4\.charAt\(\d\));$", s)
        if m:
            var = m.group(1)
            continue
        m = re.match(r'if \(\(r0 === "((?:[^"\\]|\\.)*)"\)\) goto @(\d+);$', s)
        if m and var:
            conds[int(m.group(2))] = current + [(var, m.group(1))]
            continue
        m = re.search(r"this\.aks\.([A-Za-z]+)\.(\w+)\((.*)\);", s)
        if m:
            rows.append((current[:], m.group(1), m.group(2), m.group(3)))
    table = collections.OrderedDict()
    for cond, cls, meth, args in rows:
        chars = {"p1": "", "p2": "", "p4.charAt(2)": ""}
        for v, value in cond:
            chars[v] = value
        table.setdefault((chars["p1"] + chars["p2"] + chars["p4.charAt(2)"], cls, meth), args)
    return [(p, c, m, a) for (p, c, m), a in table.items()]


def sends_of(blocks):
    rows = []
    for cls, block in blocks:
        for m in METHOD.finditer(block):
            for sm in re.finditer(r"this\.aks\.send\((.*?)\);", m.group(4)):
                arg = sm.group(1)
                lit = re.match(r'\(*"((?:[^"\\]|\\.)*)"', arg)
                rows.append((cls, m.group(2), m.group(3), lit.group(1) if lit else "?", arg[:110]))
    return rows


def digest(body, limit=28):
    out = []
    for line in body.split("\n"):
        s = line.strip()
        if not s or SKIP.search(s):
            continue
        if KEEP.search(s):
            out.append(re.sub(r'ank\["[^"]*"\]\["[^"]*"\]', "ank.util", s))
        if len(out) >= limit:
            out.append("…")
            break
    return out


def main():
    src, dst = sys.argv[1], sys.argv[2]
    tables = sys.argv[3] if len(sys.argv) > 3 else None
    text = open(src, encoding="utf-8").read()
    blocks = list(classes(text))
    dispatcher = max((b for _, b in blocks), key=lambda b: len(re.findall(r"this\.aks\.[A-Za-z]+\.on\w+\(", b)))
    routes = routes_of(dispatcher)
    sends = sends_of(blocks)
    handlers = {}
    for cls, block in blocks:
        for m in METHOD.finditer(block):
            handlers[(cls, m.group(2))] = (m.group(3), m.group(4))
    if tables:
        os.makedirs(tables, exist_ok=True)
        with open(os.path.join(tables, "routes.tsv"), "w", encoding="utf-8") as f:
            for r in sorted(routes):
                f.write("\t".join(r) + "\n")
        with open(os.path.join(tables, "envois.tsv"), "w", encoding="utf-8") as f:
            for r in sorted(sends, key=lambda r: (r[3], r[0], r[1])):
                f.write("\t".join(r) + "\n")
    out = ["# Protocole du client Dofus 1.34.1 relevé dans `loader.swf`\n",
           "Référence générée à partir du code ActionScript 2 du client fourni (`modules/loader.swf`, classes `dofus.aks.*`), "
           "désassemblé avec `tools/client-analysis` (suivi de flux, prédicats opaques repliés, pseudo-décompilation par simulation de pile). "
           "Elle décrit ce que le **client** envoie et comment il **lit** chaque réponse : c'est la structure que le bot doit respecter. "
           "Le serveur StarLoco fourni accepte la version `1.34.1`.\n",
           f"- {len(routes)} routes serveur → client (préfixe de paquet → gestionnaire du client).\n- {len(sends)} envois client → serveur.\n",
           "Conventions : `p4` est le paquet complet, `!p3` vaut vrai quand le troisième caractère n'est pas `E` (erreur). "
           "`substr(n)` retire le préfixe. Les séparateurs usuels sont `|` entre champs, `;` dans un enregistrement, `*` entre enregistrements, "
           "`~` dans un objet et `,` dans un chemin.\n",
           "Les noms de paramètres `p1`, `p2`… remplacent les noms obfusqués du client ; les méthodes et les préfixes sont ceux du client.\n",
           "\n## Routage serveur → client\n", "| Préfixe | Gestionnaire | Arguments |\n|---|---|---|"]
    for p, cls, meth, args in sorted(routes, key=lambda r: (r[0], r[1])):
        out.append(f"| `{p}` | `{cls}.{meth}` | `{args}` |")
    out += ["\n## Envois client → serveur\n", "| Préfixe | Méthode du client | Paquet construit |\n|---|---|---|"]
    for cls, meth, params, lit, arg in sorted(sends, key=lambda r: (r[3], r[0], r[1])):
        packet = arg.replace("|", "\\|")
        out.append(f"| `{lit}` | `{cls}.{meth}({params})` | `{packet}` |")
    out += ["\n## Lecture des réponses par domaine\n",
            "Pour chaque gestionnaire, les lignes de découpage du paquet telles que le client les exécute (champs, séparateurs, conversions). "
            "Les corps complets sont dans la sortie de l'outil, non versionnée.\n"]
    by_cls = collections.defaultdict(list)
    for p, cls, meth, args in routes:
        by_cls[cls].append((p, meth, args))
    for cls, title in DOMAINS.items():
        if cls not in by_cls:
            continue
        out.append(f"\n### {cls} : {title}\n")
        seen = set()
        for p, meth, args in sorted(by_cls[cls]):
            if (cls, meth) in seen:
                continue
            seen.add((cls, meth))
            prefixes = sorted({pp for pp, mm, _ in by_cls[cls] if mm == meth})
            h = handlers.get((cls, meth))
            if not h:
                out.append(f"- `{'`, `'.join(prefixes)}` → `{meth}` (corps non retrouvé)")
                continue
            params, body = h
            out.append(f"- `{'`, `'.join(prefixes)}` → `{meth}({params})`")
            lines = digest(body)
            if lines:
                out.append("  ```")
                out.extend("  " + l for l in lines)
                out.append("  ```")
    open(dst, "w", encoding="utf-8").write("\n".join(out) + "\n")
    print(f"{len(routes)} routes, {len(sends)} envois, {len(out)} lignes")


if __name__ == "__main__":
    main()

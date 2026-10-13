# Captures d'écran de la fenêtre de jeu du bot

Outil qui photographie les écrans du bot (connexion, serveurs, personnages, fenêtre de jeu, volets du bandeau, hôtel de
vente, atelier, monture, combat) pour [docs/captures](../../docs/captures/README.md). Il tourne sous Linux avec Mono et un
écran virtuel Xvfb ; chaque vue est ouverte contre un **serveur fictif local** (boucle locale, port libre) qui reçoit les
paquets du bot sans y répondre, tandis que l'outil injecte les paquets du « serveur » (repris des tests `tests/Bot*Smoke.cs`).
Les comptes, personnages, guildes et montures sont **inventés** (`compte-demo`, `Aventuriere-Demo`, `Sentinelle-Demo`…) ;
aucun compte ni personnage réel n'est lu.

## Commande

Depuis la racine du dépôt, après une compilation Debug de la solution (`Outil_Azur_complet/bin/Debug`) :

```bash
tools/captures/run-captures.sh                      # toutes les vues, dans docs/captures
tools/captures/run-captures.sh /tmp/essai 04-jeu 12-guilde   # quelques vues, ailleurs
```

Pour la vraie carte d'Astrub montrée dans `docs/captures` (sinon : prairie synthétique composée avec les décors versionnés) :

```bash
AZUR_CAPTURE_SQL="/chemin/StarLoco/02 - BDD/game.sql" \
AZUR_CAPTURE_CLIENT="/chemin/Dofus 1.34" \
tools/captures/run-captures.sh
```

Le script compile `BotCaptures.exe` dans `tools/captures/bin` (ignoré par git) avec le compilateur Roslyn du SDK .NET et les
assemblys de référence .NET Framework 4.8, le lance sous `xvfb-run` (écran 1280 × 1024), photographie chaque vue avec
`import -window root` (ImageMagick) découpé à la zone cliente de la fenêtre (Xvfb n'a pas de gestionnaire de fenêtres : ni
barre de titre ni bordure), puis `optimize.py` (Pillow) ramène chaque PNG à 1 280 px de large au plus en couleurs indexées.
Il affiche `captures=N absentes=N erreurs-interface=N` et se termine par `exit=0` quand toutes les vues demandées sont produites
(3 si une vue manque, avec sa raison sur une ligne `non capturée :`).

Prérequis : `mono` (6.8 ou plus), `xvfb-run`, ImageMagick (`import`), `python3` avec Pillow, un SDK .NET (`csc.dll` trouvé
sous `/usr/lib/dotnet/sdk/*/Roslyn/bincore`, ou `AZUR_CSC`) et le paquet NuGet
`microsoft.netframework.referenceassemblies.net48` (ou `AZUR_REFERENCE_ASSEMBLIES`).

| Variable | Rôle |
| --- | --- |
| `AZUR_CAPTURE_SQL` | export SQL du serveur de jeu StarLoco : la ligne `maps` de la carte (dimensions, cellules, coordonnées) |
| `AZUR_CAPTURE_MAP` | identifiant de cette carte, 7411 (Astrub) par défaut |
| `AZUR_CAPTURE_CLIENT` | dossier du client 1.34 : le fond de la carte est lu dans `data/maps/<id>_*.swf` |
| `AZUR_CAPTURE_DEBUG=1` | numéros des cellules sur la carte (pour choisir les cellules des acteurs) |

## Données lues, jamais versionnées

- `AZUR_CAPTURE_SQL` et `AZUR_CAPTURE_CLIENT` ne servent qu'au moment de la capture : seule la carte demandée est lue
  (ses cellules et le numéro de son fond), rien n'en est copié dans le dépôt. Aucune autre table de l'export n'est lue.
- Les décors, icônes, textes du client (`lang`) et sorts sont ceux que l'application lit déjà à côté de l'exécutable
  (`Outil_Azur_complet/bin/Debug/ressources`).
- Les fichiers de compte et de configuration créés par le bot vont dans un dossier temporaire supprimé à la fin.

## Fichiers

- `BotCaptures.cs` : point d'entrée, attente de l'interface, prise de vue, serveur fictif.
- `Scenes.cs` : chargement des données, connexion, serveurs, personnages.
- `GameScenes.cs` : fenêtre de jeu (carte, acteurs, chat, bandeau, menu d'un PNJ).
- `PanelScenes.cs` : volets du bandeau, hôtel de vente, atelier, monture, combat.
- `CaptureMap.cs` : carte lue dans l'export SQL ou prairie synthétique.
- `run-captures.sh`, `optimize.py` : compilation, Xvfb, optimisation des PNG.

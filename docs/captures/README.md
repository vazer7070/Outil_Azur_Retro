# Captures de l'interface du bot

Fenêtres du bot (Azur, interface du client Retro 1.34) photographiées sous Linux (Mono, écran virtuel Xvfb 1280 × 1024)
contre un serveur fictif local : les paquets du « serveur » sont synthétiques, les comptes, personnages, guilde et monture
sont inventés. Fenêtre de jeu en 1200 × 800 ; PNG en 256 couleurs, 1 280 px de large au plus.

La carte est **Astrub [4,-19] (7411)** : ses cellules ont été lues dans l'export SQL du serveur StarLoco et le numéro de son
fond dans le client 1.34, au moment de la capture seulement (rien de ces fichiers n'est versionné). Les décors, icônes et
textes sont ceux que le bot lit déjà dans `Outil_Azur_complet/bin/Debug/ressources`.

## Régénérer

Depuis la racine du dépôt, après une compilation Debug :

```bash
AZUR_CAPTURE_SQL="/chemin/StarLoco/02 - BDD/game.sql" \
AZUR_CAPTURE_CLIENT="/chemin/Dofus 1.34" \
tools/captures/run-captures.sh                 # toutes les vues, ici même
tools/captures/run-captures.sh docs/captures 12-guilde 18-combat   # seulement certaines vues
```

Sans les deux variables, la carte est une prairie synthétique composée avec les décors versionnés. Prérequis, variables et
fonctionnement : [tools/captures/README.md](../../tools/captures/README.md).

## Vues

| Fichier | Vue | Contenu montré |
| --- | --- | --- |
| [01-connexion.png](01-connexion.png) | Connexion | deux comptes enregistrés fictifs, champs du compte sélectionné, bandeau des œufs |
| [02-serveurs.png](02-serveurs.png) | Choix du serveur | `AxK` et `AH` : quatre serveurs (en ligne, hors ligne, sauvegarde) et nombre de personnages |
| [03-personnages.png](03-personnages.png) | Choix du personnage | `ALK` : trois personnages (Iop, Sadida, Crâ) et deux emplacements libres |
| [04-jeu.png](04-jeu.png) | Fenêtre de jeu | carte d'Astrub, trois joueurs, deux PNJ, un groupe de Bouftous, bandeau bas |
| [05-chat.png](05-chat.png) | Chat déplié | canaux général, guilde, groupe, commerce, recrutement, privé, alignement, emote, message `Im` |
| [06-bandeau.png](06-bandeau.png) | Bandeau bas | chat replié, cœur, PA / PM, illustration, boutons des volets, raccourcis de sorts |
| [07-menu-pnj.png](07-menu-pnj.png) | Menu d'un PNJ | clic droit sur la marchande : Parler, Vendre, Acheter |
| [08-caracteristiques.png](08-caracteristiques.png) | Caractéristiques | `As` complet : vie, énergie, PA / PM, initiative, prospection, six caractéristiques et capital |
| [09-sorts.png](09-sorts.png) | Sorts | `SL` : sorts du Iop, fiche du sort Intimidation niveau 3 (effets, autres caractéristiques) |
| [10-inventaire.png](10-inventaire.png) | Inventaire | panoplie portée, sac (ressources, potions), fiche de la Fiole de Soin sélectionnée |
| [11-quetes.png](11-quetes.png) | Quêtes | `QL` / `QS` : trois quêtes en cours, étape « L'epyss s'enlise », objectifs et récompense |
| [12-guilde.png](12-guilde.png) | Guilde | `gS` / `gIG` / `gIM` : emblème, niveau, cinq membres (rang, niveau, xp, membre en combat) |
| [13-carte-monde.png](13-carte-monde.png) | Carte du monde | Astrub et ses environs, repère de la position, légendes des indices |
| [14-hotel-de-vente.png](14-hotel-de-vente.png) | Hôtel de vente | `ECK11`, `EHL`, `EHP`, `EHl` : céréales, lots de Blé par 1, 10 et 100, prix moyen |
| [15-atelier.png](15-atelier.png) | Atelier | `JS` / `JX` / `ECK3` / `EMKO` : boulanger, farine et eau posées, recette du pain reconnue |
| [16-monture.png](16-monture.png) | Monture | `Re+` : dragodinde équipée, jauges (xp, énergie, fatigue, maturité, endurance, amour, sérénité) |
| [17-combat-placement.png](17-combat-placement.png) | Combat, placement | `GJK`, `GP`, `GM`, `GIC` : options du combat, boutons Prêt / Annuler |
| [18-combat.png](18-combat.png) | Combat, tour du joueur | `GS`, `GTM`, `GTL`, `GTS` : frise des combattants, Passer / Abandonner, raccourcis actifs |

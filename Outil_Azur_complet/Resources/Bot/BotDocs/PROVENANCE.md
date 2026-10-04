# Documents du client 1.34 pour le bot (`BotDocs`)

Ces XML reprennent les 110 documents français du client Dofus 1.34 : livres, parchemins (avis de recherche, affiches) et pancartes. Le serveur n'envoie que leur clé : StarLoco ouvre un document par `dCK<id>_<date>` quand le personnage marche sur la cellule d'une pancarte ou d'un livre (`InteractiveObject.getSignIO`), et le client charge alors `data/docs/<langue>_<id>_<date>.swf`. Le volet Document du bot lit `ressources/Bot/BotDocs/<id>_<date>.xml` à la place (`Tool_BotProtocol.Game.Interactions.DocumentDialog`).

## Source

Fichiers `data/docs/fr_<id>_<date>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF eux-mêmes ne sont pas versionnés. Les autres langues (`de`, `en`, `es`, `it`, `nl`, `pt`) ne sont pas exportées ; `--langue` le permet.

## Outil et commande

`tools/client-analysis/docs2xml.py` enchaîne, pour chaque document, `avm1dump` (désassemblage du `DoAction` unique du SWF), `as2lite.py` puis la lecture des affectations littérales de `lang2xml.py` (`this.type`, `this.title`, `this.subtitle`, `this.author`, `this.style`, `this.pages[n]`, `this.chapters[n]`). Rien n'est exécuté depuis le SWF.

Commande exacte, depuis la racine du dépôt (`avm1dump` compilé depuis `tools/client-analysis/avm1dump`) :

```sh
python3 tools/client-analysis/docs2xml.py "<client 1.34>/data/docs" Outil_Azur_complet/Resources/Bot/BotDocs \
    --avm1dump tools/client-analysis/avm1dump/target/release/avm1dump
```

Durée : environ 5 secondes. Taille : 585 Ko pour 110 fichiers.

## Contenu

Racine `<BotDoc id="…" date="…" type="book|parchment|roadsignleft|roadsignright" style="1|2" source="<nom du SWF>">`, puis `titre`, `soustitre`, `auteur`, un élément `page` par page (HTML simplifié du client, texte échappé) et un élément `chapitre` par chapitre (`titre`, `page` = indice de la page, `droite` = le chapitre commence sur une page de droite, `titreVisible` = le titre est répété en tête de la page).

Limites, reprises telles quelles du client : deux parchemins (126, 127) n'ont aucune page ; treize livres ont un chapitre qui pointe après leur dernière page (le bot ne le propose pas dans le sommaire). Les illustrations (`<img src='##swf,<n>##'>`, `data/docs/swf/<n>.swf` et `data/docs/jpg`) ne sont pas exportées : le bot affiche « [illustration] » à leur place. Les liens `asfunction:onHref,…` sont affichés, jamais exécutés. Les textes conservent les droits de leurs titulaires d'origine.

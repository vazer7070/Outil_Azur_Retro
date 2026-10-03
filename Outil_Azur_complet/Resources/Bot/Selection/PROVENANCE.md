# Ressources de sélection du client fourni

Ces PNG proviennent du client remis par l'utilisateur, dans `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`. Ils ne sont pas générés et ne nécessitent pas ce chemin à l'exécution.

Source de l'interface : `modules/core.swf` (SHA-256 `917D4CF5A399AFB3AFBB617015C8E72933799C591ECD387A382AAEC3E6D91DCE`).

| PNG | Symbole original |
| --- | --- |
| scene.png | UI_ChooseCharacter, DefineSprite 797 ; les cinq instances dynamiques ChooseCharacterSprite 455 ont été retirées d'une copie temporaire avant export |
| podium.png | DefineSprite 449, sol du personnage |
| selected-podium.png | DefineSprite 451, anneau de sélection |
| cartouche.png | DefineShape 445, cartouche nom/niveau |
| unknown.png | DefineSprite 453, emplacement sans personnage |
| play-up.png / play-down.png | ChooseCharacterBtnPlayUp / Down, DefineSprite 1322 / 1323 |
| artwork-10.png à artwork-121.png | `clips/artworks/big/{gfx}.swf`, première image, 12 classes et leurs deux sexes |

Les illustrations de classe ont été exportées à une échelle 2 depuis une copie temporaire dont le rectangle de scène inclut les coordonnées négatives, puis recadrées sur l'alpha. Elles représentent les illustrations du client ; elles ne reproduisent pas les couleurs personnalisées envoyées lors de la création.

Extraction locale : JPEXS Free Flash Decompiler 26.3.0, [version officielle](https://github.com/jindrapetrik/jpexs-decompiler/releases/tag/version26.3.0), Java 8. Le décompilateur et les SWF ne sont pas nécessaires à Azur et ne sont pas embarqués. La licence GPL du décompilateur ne s'applique pas aux illustrations du client : les ressources conservent les droits de leurs titulaires d'origine. Aucune licence de redistribution distincte n'a été constatée dans le kit.

Le projet copie ce dossier vers `ressources/Bot/UI/Selection` à côté de l'exécutable, dans les compilations Debug et Release ainsi que dans le paquet portable. L'affichage revient aux sprites existants ou à une indication d'apparence indisponible si une illustration manque.

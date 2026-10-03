# Inventaire de reprise — Azur

État de référence : **2 octobre 2026**. Le dépôt principal est `G:\RECOVERY FOLDER\Outil_Azur_complet`. La copie détaillée de travail est aussi dans `C:\Users\kishi\Documents\Codex\2026-09-14\explique-moi-ce-projet-en-quoi\INVENTAIRE_REPRISE_AZUR.md`.

## Lecture prioritaire

Lire dans cet ordre :

1. `docs/BRIEF_REDESIGN_UI_BOT.md`
2. `docs/BOT_STARLOCO.md` et `docs/STARLOCO_SOURCES_ANALYSE.md`
3. `docs/ETAT_PROJET.md`
4. `docs/KAUTH_COMPATIBILITE.md`, `docs/CAPTURE_RESEAU.md` et `tests/README.md`
5. ce fichier, puis `git status --short` et `git diff --check`

Le dépôt est volontairement sale : les modifications et nouveaux fichiers sont le travail en cours. Ne pas utiliser `git reset`, `git checkout` ou un nettoyage global.

## Structure

| Chemin | Rôle |
|---|---|
| `Outil_Azur_complet.sln` | Solution Visual Studio. |
| `Outil_Azur_complet/` | Application WinForms et éditeurs. |
| `tools/Tools_protocol.csproj` | JSON, configuration, SQL et exports. |
| `tools/tools_editor/Tool_Editor.csproj` | Cartes, tuiles et codecs AME/SWF. |
| `Tool_BotProtocol/` | Transport TCP, frames auth/jeu et bot ; référence `Tools_protocol`, .NET Framework 4.8. |
| `tests/` | Tests sans base, UI et MySQL isolé. |
| `docs/` | Guides, état et formats. |
| `kworldsave.sql` | Dump world Kryone ; ne pas importer automatiquement. |

## Corrections déjà réalisées

### SQL et éditeurs

- `tools/Tools_protocol.Json/JsonManager.cs` : correspondances kauth corrigées.
- `tools/Tools_protocol.Kryone.Database/AccountList.cs` : création selon les colonnes réelles, champs obligatoires inconnus refusés, limites, InnoDB, SQL strict, transaction, relecture et cache après commit.
- `Outil_Azur_complet/editeur_compte/editeurcompte.cs` : originaux, conflits, hors-ligne et rollback groupé.
- `tools/Tools_protocol.Kryone.Database/CharacterList.cs` et `Outil_Azur_complet/editeur_perso/editeur-perso.cs` : état `NULL`, couleurs, `savepos`, renommage, compte cible et conflits.
- `Outil_Azur_complet/ServerSql.cs` : contrôles SQL stricts, métadonnées et état hors-ligne.
- 24 familles de ressources : `ServerDataForm.cs`, `ServerDataService.cs`, `ResourceRelations.cs`, `Editors/ResourceFieldCatalog.cs`.
- UI commune : `Editors/EditorUi.cs`, `Editors/BoundEditorLayout.cs` et les layouts dédiés des comptes, personnages, inventaires, objets, sorts, métiers, placements et cartes.

### Cartes et SWF

- `tools/tools_editor/Tool_Editor.maps.data/Map.cs` : `Map.CellCount`; une carte 15×17 a **479 cellules**.
- `MapSwfSerializer.cs` : FWS/CWS à affectations AVM1 littérales, sans exécution de scripts.
- `MapProjectSerializer.cs` : AME Azur v1/v2/v3 et métadonnées.
- `Outil_Azur_complet/Parser/ResourceMapConversion.cs` : déchiffrement, conversion sur copie, conservation AME et échec sans mutation.
- `MapForm.cs`, `MapEditorLayout.cs`, `MapServerPlacementLayer.cs` : placement souris/tuiles et repères serveur.

### Bot et capture

- `Tool_BotProtocol/Network/TcpClient.cs` : réassemblage UTF-8/NUL, fragments, envois complets, sessions séparées, reconnexion et fermeture sûre.
- `Tool_BotProtocol/Game/Accounts/Accounts.cs` : état réel et notifications hors verrou.
- `tools/Network/NullTerminatedPacketDecoder.cs` : décodeur borné.
- `tools/Network/PacketCaptureProxy.cs` : relais TCP manuel sur loopback, 16 connexions, demi-fermeture, arrêt/restart et masquage heuristique.
- `Outil_Azur_complet/Parser/NetworkCaptureLayout.cs` : UI, filtres, détail, export et arrêt à la fermeture.
- Limites : pas de TLS, injection ou redirection automatique. Voir `docs/CAPTURE_RESEAU.md`.


La refonte du bot du 2 octobre utilise les designs historiques et le client fourni : `BotUi.cs`, `GameClientFullform.cs`, `SelectPlayerPerso.cs`, `CreateCharacter.cs`, `Interfaces/MapControl.cs`, `Controls/UserMapControl.cs` et `Controls/BotMapArtwork.cs`. Les décors viennent des PNG portables, indexés une fois puis détenus par carte. Les tests spécifiques ajoutés sont `BotConfigSmoke`, `BotHandshakeSmoke`, `BotGameplaySmoke`, `BotSpellsSmoke`, `BotMapViewSmoke` et `BotUiSmoke`. Ne pas réintroduire un tableau de bord à cartes blanches ni une petite prévisualisation de navigation.

## Validation prouvée

La suite contient **29 tests : 21 sans base et 8 d’intégration**, réussis en Debug et Release le **2 octobre 2026**, après la dernière refonte du client.

```powershell
.\tests\Run-Tests.ps1 -Integration
.\tests\Run-Tests.ps1 -Configuration Release -Integration
```

Les intégrations utilisent une instance MySQL 8.4 temporaire sur `127.0.0.1:43306`, dans un dossier neuf, puis l’arrêtent. Aucun serveur du kit ni connexion de production n’est utilisé.

- Debug final : `tests/bin/run-9c3edbaeec054cf58eb26cd74605b078`.
- Release final : `tests/bin/run-e803fd98abeb46c3a767f2879f36a05e`.
- Journaux dans l’espace de travail : `outputs/Verification_Azur_Debug_Client_2026-10-02.log` et `outputs/Verification_Azur_Release_Client_2026-10-02.log`, terminés par `29 tests réussis`.
- Captures Release du client : `outputs/bot-client-2026-10-02`, avec Nowel, Astrub, zoom, fiches, portraits et discussion privée. Les données de compte/personnage sont fictives.
- Les tests de carte vérifient les couches raster et la précision des cellules après zoom/pan/redimensionnement ; les tests de jeu couvrent aussi l’annulation GDM et les accusés obsolètes.

## Kit serveur fourni

Le kit `F:\kit` n’a pas été modifié ni lancé.

| Chemin | Contenu |
|---|---|
| `F:\kit\03 - Emulateur StarLoco\03 - Login\login.jar` | Serveur Login Java. |
| `F:\kit\03 - Emulateur StarLoco\03 - Login\config.properties` | Configuration Login ; ne pas afficher les secrets. |
| `F:\kit\03 - Emulateur StarLoco\04 - Game\game.jar` | Serveur Game Java. |
| `F:\kit\03 - Emulateur StarLoco\04 - Game\config.txt` | Configuration Game et profils possibles. |
| `F:\kit\03 - Emulateur StarLoco\02 - BDD\login.sql` | Dump Login, environ 6,3 Mo. |
| `F:\kit\03 - Emulateur StarLoco\02 - BDD\game.sql` | Dump Game, environ 51,9 Mo. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\Dofus.exe` | Client Dofus 1.34. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\data\maps` | Ressources maps du client. |

Java 8 et Java 17 sont présents. Les JAR sont du bytecode Java 8. Le README StarLoco est un modèle vide ; les `.bat` lancent directement les JAR originaux.

StarLoco et Kryone sont des cibles différentes : Azur écrit dans Kryone/kauth/kworld ; le parcours réseau à tester est StarLoco Login/Game. Ne pas mapper automatiquement les tables ni importer les dumps StarLoco dans les fixtures.

Constats réseau : les sources sont sous `03 - Login/src` et `04 - Game/src`; les binds doivent être forcés à `127.0.0.1` dans une copie isolée avant tout lancement. Le parcours attendu comprend Login `HC`, version, identifiant/hash, `Af`, `AxK/AYK`, puis Game `HG`, `AT...`, `ATK0/ATK`, `AV0`, `ALK`, `ASK`. La connexion réelle bot → StarLoco n’est pas encore validée.

## Reprise prioritaire

1. Lire `docs/BRIEF_REDESIGN_UI_BOT.md`, `docs/BOT_STARLOCO.md` et `docs/STARLOCO_SOURCES_ANALYSE.md`. Préserver la direction client : grande carte, HUD inférieur, icônes et fiches refermables.
2. Réaliser le parcours réel Bot → Login → Game dans une copie StarLoco isolée : loopback, ports libres, bases séparées et comptes synthétiques. Le parcours fictif est testé ; la validation réelle reste à faire.
3. Contrôler les décors, personnages, mouvements, chat, question secrète et augmentations en jeu. Les captures natives et tests locaux constituent déjà une référence vérifiée.
4. Compléter les fonctions désactivées, interactions/récolte, combat puis automatisation/admin/Lua/plugins, en suivant les sources Game. Les bases StarLoco et Kryone restent distinctes.
5. Compléter si nécessaire l’extraction des SWF (noms d’export GFX, origines, pentes, timelines, masques, recoloration/animations). Le rendu PNG actuel ne constitue pas un moteur Flash complet.
6. Vérifier les exports de cartes/objets SWF et les placements dans le vrai client 1.34 ; la compatibilité AME d’Astria reste distincte.
7. Après tout changement de code, exécuter la suite appropriée, inspecter les vrais formulaires, mettre à jour l’état puis reconstruire le portable avec `tools/Build-PortableRelease.ps1` et vérifier `tests/verify_portable.py`.

## Livrables et règles

Les artefacts précédents sont dans `C:\Users\kishi\Documents\Codex\2026-09-14\explique-moi-ce-projet-en-quoi\outputs`. La nouvelle archive est `outputs/Azur_portable_2026-10-02.zip`, avec son fichier `.sha256` et le journal `outputs/Verification_portable_2026-10-02.log`. Les archives datées antérieures conservent les versions historiques.

Ne jamais afficher mots de passe, tokens, comptes initiaux ou configurations du kit. Ne pas lancer les JAR originaux avant isolation complète. Ne pas annoncer le projet terminé avant un vrai parcours Login → Game, l’ouverture d’un SWF 1.34 et la validation des placements.

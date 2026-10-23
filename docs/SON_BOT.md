# Son du bot : évaluation

État au 8 octobre 2026 (lot AN9 du plan des animations). Le bot ne joue aucun son. Cette note dit
ce que le client 1.34 fait entendre, ce que cela coûterait en taille, ce que WinForms et Mono savent
lire, et s’il faut un lot son. Elle ne livre ni code, ni données, ni test.

Les chiffres viennent des fichiers du client fourni (`audio/effects.swf`, `audio/musics.swf`,
`clips/spells`, `clips/sprites`, langue `audio_fr_56.swf`) et d’essais sous Mono 6.8 dans le
conteneur de développement. La méthode est décrite à la fin. Rien de ce qui est mesuré ici n’a été
versionné.

## En bref

- Le client a **904 bruitages** (MP3, 10,7 Mo de données) et **36 musiques** (MP3, 72,1 Mo).
- Le jeu en fait entendre **793** par les animations et l’interface : **2,64 Mo** en MP3, **32,4 Mo**
  une fois convertis en WAV, le seul format que lisent `SoundPlayer` et Mono (× 12,3).
- StarLoco n’envoie rien de propre au son : tout se déduit de paquets que le bot lit déjà (`GA`, `GS`,
  `GTS`, `GE`, chat) et des animations jouées.
- Sous Windows, `SoundPlayer` ne joue qu’un son à la fois (comportement documenté, non essayé ici).
  Sous Mono (essayé), il ne lit que le WAV PCM, se tait sans erreur sur un MP3 et **ne rend jamais la
  main** sur une machine sans carte son.
- **Recommandation : pas de lot son pour l’instant.** Les bruitages des animations n’ont de sens
  qu’après les lots AN2 à AN4. Si un lot est ouvert ensuite, il versionne les MP3 tels quels et les
  joue sous Windows seulement (détails au dernier paragraphe).

## Ce que contient le client

### Fichiers sonores

| Fichier | Taille | Sons | Format | Durée totale | En PCM 16 bits |
|---|---|---|---|---|---|
| `audio/effects.swf` | 8 535 805 octets (SWF compressé) | 904, tous exportés | MP3 ; 563 à 11 025 Hz, 176 à 22 050 Hz, 165 à 44 100 Hz ; 868 mono, 36 stéréo | 1 470 s | 86,8 Mo |
| `audio/musics.swf` | 71 963 489 octets | 36, tous exportés | MP3 44 100 Hz stéréo, 160 kbit/s | 3 608 s | 636,4 Mo |

Les données MP3 des sons pèsent 10,68 Mo pour `effects.swf` (le SWF est compressé par zlib) et
72,15 Mo pour `musics.swf`. Les noms exportés d’`effects.swf` sont de trois sortes :

- 754 sons `fx_<n>.mp3`, désignés par la langue audio ;
- 138 sons au nom d’un monstre et d’une animation (`<Monstre>_Anim<n>`, `_Hit`, `_Die`, 35 monstres),
  joués par leur nom depuis les sprites ;
- 12 autres : trois fonds sonores d’Otomaï, huit sons des objets vivants, une mort de tentacule.

### Langue audio (`audio_fr_56.swf`)

| Table | Entrées | Contenu |
|---|---|---|
| `AUE` | 765 | bruitage : `{f: fichier, v: volume 0–100, l: boucle, s: lecture en flux, o: décalage}` ; 754 pointent vers `fx_<id>.mp3`, 11 vers les sons d’Otomaï et des objets vivants ; aucun ne boucle, aucun n’est en flux, aucun n’a de décalage ; 692 ont le volume 100 |
| `AUEC` | 764 | nom → identifiant de bruitage (`TURN_START`, `BIP`, `COUP_CRITIQUE`, `ARTY_101`…) |
| `AUM`, `AUMC` | 36, 36 | musiques (`cls_*`, `fig_*`, `job_*`, `loc_*`), toutes en boucle ; noms `GUILD_*`, `FIGHT_*`, `JOB_*`, `PLACE_*` |
| `AUA`, `AUAC` | 20, 20 | ambiances : fonds en boucle (`bg`) et bruits tirés au hasard (`n`) toutes les `mind` + hasard(`maxd`) secondes (15 et 30 le plus souvent) |

### Quand le client joue un son

Un gestionnaire unique classe chaque son en bruitage, ambiance ou musique, avec un volume et un
silence par classe. Valeurs par défaut des options : musique 60, bruitages 100, ambiance 60, rien en
silence ; `StartTurnSound` vrai, `GuildMessageSound` faux. Un bruitage est ignoré quand la fenêtre n’a
pas le focus, sauf ceux de la liste ci-dessous marqués « toujours ».

**Événements de l’interface et du combat** (clé de `AUEC`) :

| Événement | Son | Sans le focus |
|---|---|---|
| début de son propre tour (`GTS`), si `StartTurnSound` | `TURN_START` | toujours |
| coup critique (`GA301`, `GA304`) | `BIP` | ignoré |
| échec critique (`GA302`, `GA305`) | `COUP_CRITIQUE` | ignoré |
| message privé reçu, invitation | `BIP` | toujours |
| erreur affichée | `ERROR` | toujours |
| attaque d’un percepteur | `CLANG` | toujours |
| minuterie du tour dans le bandeau | `TAK` | toujours |
| boutons du bandeau, de la discussion, des sorts, des fiches, de l’inventaire, de la carte, de la guilde | `CLICK`, `CLICK2`, `CLICK3` | ignoré |
| drapeau posé sur la carte | `POSE2` | ignoré |

Ces dix sons pèsent 0,01 Mo en MP3 et 0,14 Mo en WAV.

**Animations.** Les SWF de sorts et de sprites appellent le gestionnaire depuis une image de leur
animation, avec un nom. Le client cherche ce nom, mis en majuscules, parmi les clés de `AUEC`, et
joue aussi le son exporté d’`effects.swf` sous ce nom s’il existe ; aucun nom relevé ne se trouve
dans les deux. Un nom qui ne se trouve dans aucun des deux ne joue rien.

| Dossier | SWF qui appellent un son | Appels | Noms | Résolus par la langue / par le nom / introuvables | Sons distincts | MP3 | Durée |
|---|---|---|---|---|---|---|---|
| `clips/spells` | 219 des 271 SWF numérotés, plus `2905save.swf` et `transition.swf` | 372 | 155 | 360 / 4 / 8 | 147 | 0,60 Mo | 194 s |
| `clips/sprites` | 715 sur 933 | 9 706 | 764 | 8 992 / 538 / 176 | 696 | 2,15 Mo | 795 s |
| `clips/gfx` | 3 sur 14 | 18 | 4 | 18 / 0 / 0 | 4 | 0,01 Mo | 2 s |
| `clips/cinematics` | 1 sur 9 | 1 | 1 | 1 / 0 / 0 | 1 | < 0,01 Mo | 1 s |

Les autres dossiers de `clips` (auras, émotes, défis, extras, métiers, smileys, alignements, cadeaux,
cartes) et les clips à la racine n’appellent aucun son. Dans les sprites, les appels se trouvent dans
les animations `anim<n>` (585 gfx), `hit` (687 gfx), `die` (529 gfx), `walk` (189 gfx), `run`
(179 gfx), `emote<n>` (46 gfx) et `static` (46 gfx). Beaucoup d’appels ne sont pas sur la
première image de leur clip (182 sur 372 pour les sorts, 6 469 sur 9 706 pour les sprites), et la
plupart sont dans des clips imbriqués : pour savoir quand jouer un son, il faut résoudre la timeline
comme `swfsvg` le fait pour les images. Un son trouvé par la langue suit la règle du focus ; un son
trouvé par son nom exporté (sons au nom d’un monstre, ou `fx_<n>.mp3` appelé directement) est joué
même sans le focus.

Avec les dix sons de l’interface, l’ensemble des sons réellement appelés compte **793 sons,
2,64 Mo de MP3 et 949 s**.

**Ambiances et musiques.** À l’arrivée sur une carte, le client lance l’ambiance et la musique dont
les numéros sont dans le SWF de la carte (`ambianceId`, `musicId`). Au début d’un combat (`GS`), il
tire au hasard une musique de la sous-zone ; quand il quitte le combat, il reprend celle de la
carte. Les musiques des sous-zones sont déjà dans `BotLang/maps.xml` (attribut `musiques`) ;
`ambianceId` et `musicId` ne sont pas exportés dans `BotMaps`. Les 20 ambiances utilisent 49 sons
(7,74 Mo de MP3, 458 s).

## Taille selon le format

Conversion réelle par `ffmpeg` en WAV PCM 16 bits à la fréquence d’origine, en-tête canonique
de 44 octets ; « zlib » est la taille compressée au niveau 9, proche de ce que git stockerait.

| Ensemble | Sons | MP3 | WAV | WAV après zlib | Rapport WAV / MP3 |
|---|---|---|---|---|---|
| sons appelés (animations et interface) | 793 | 2,64 Mo | 32,40 Mo | 23,41 Mo | × 12,3 |
| tout `effects.swf` | 904 | 10,68 Mo | 87,62 Mo | 63,06 Mo | × 8,2 |
| musiques (calcul, non converties) | 36 | 72,15 Mo | 636,4 Mo | — | × 8,8 |

Des WAV 8 bits, que Mono accepte aussi, diviseraient ces tailles par deux, au prix de la qualité.
Le budget des animations (150 Mo, dont ≈ 135 Mo prévus par AN1 à AN8) n’a pas la place des WAV ; il
a celle des MP3 des sons appelés.

## Lire un son depuis le bot

### Sous Mono (Linux), mesuré

Essais avec Mono 6.8.0.105 dans le conteneur, qui n’a pas de carte son :

- `System.Media.SoundPlayer` passe par `Mono.Audio` : il lit les données comme WAV (`RIFF`/`WAVE`,
  PCM 8 ou 16 bits, bloc `data` ou `fact` juste après `fmt `) ou AU, et les envoie à ALSA
  (`libasound` → `libasound.so.2` dans `/etc/mono/config`), périphérique `default`.
- Un MP3 est refusé (« incorrect format ») ; un WAV de `ffmpeg` qui garde son bloc `LIST` aussi
  (« incorrect format (data/fact chunck) »). `PlaySync` avale l’exception : aucun son, aucune erreur.
- Sans périphérique ALSA, Mono se rabat sur un périphérique muet qui n’accepte aucune image : avec
  un WAV valide, `PlaySync` ne rend **jamais** la main (arrêté au bout de 30 s par `timeout`).
  D’après le code désassemblé, `Play` fait la même chose sur un fil du pool, qui tournerait sans
  fin. C’est la situation des tests sous Xvfb dans le conteneur.
- Avec un périphérique ALSA (essai sur un fichier, par `ALSA_CONFIG_PATH`), les données arrivent,
  sauf le dernier bloc incomplet : 16 536 octets reçus sur 20 736 pour un son de 0,235 s, 41 340 sur
  41 472 pour un son de 0,940 s.
- Décodage MP3 en mémoire avec `NLayer` 1.16.0 (paquet NuGet, licence MIT, `netstandard2.0`) : les
  793 sons appelés sont décodés en 1,5 s, mais seuls 113 des 781 décodés ont la bonne durée (presque
  tous à 44 100 Hz). Les 668 sons à 11 025 ou 22 050 Hz sortent à la moitié de leur durée ou moins, et
  12 lèvent `IndexOutOfRangeException`. Inutilisable tel quel pour `effects.swf`.
- Lecteurs externes : `aplay` (alsa-utils, WAV), `paplay` (PulseAudio), `ffplay` ou `mpg123` (MP3).
  Aucun n’est garanti sur la machine de l’utilisateur ; dans le conteneur, seul `ffplay` est présent.

### Sous Windows, non essayé ici

Le bot cible .NET Framework 4.8 sous Windows. Aucun essai n’a pu être fait dans le conteneur ; ce qui
suit est le comportement documenté des API, à vérifier avant d’écrire du code :

- `SoundPlayer` lit le WAV seulement, n’a pas de réglage de volume et ne joue qu’un son à la fois :
  un nouveau son coupe le précédent. Un sort, le coup reçu et la mort qui s’enchaînent se couperaient
  l’un l’autre.
- L’interface MCI de `winmm.dll` (`mciSendString`, type `mpegvideo`) lit le MP3 avec les décodeurs
  de Windows, plusieurs alias à la fois, avec un volume par alias ; c’est une simple déclaration
  `DllImport`, sans dépendance. Chaque alias ouvert doit être refermé.
- `System.Windows.Media.MediaPlayer` (WPF) lit aussi le MP3, mais demande les références
  `PresentationCore` et `WindowsBase`, absentes de Mono.
- `NAudio` (NuGet) lit le MP3 et mixe plusieurs sons, au prix d’une dépendance de plus.

## Options pour un lot son

| Option | Taille versionnée | Windows | Mono | Risques |
|---|---|---|---|---|
| A. WAV PCM 16 bits, `SoundPlayer` | 32,4 Mo (23,4 Mo dans git) pour 793 sons | un son à la fois, sans volume | WAV seulement ; blocage sans carte son | dépasse le budget ; sons coupés en combat |
| B. MP3 tels quels, MCI sous Windows | 2,64 Mo (10,7 Mo pour tout `effects.swf`) | plusieurs sons, volume par son | rien (silence voulu) | à valider sous Windows ; pas de son sous Mono |
| C. MP3, décodage géré en mémoire puis `SoundPlayer` | 2,64 Mo + une bibliothèque | un son à la fois | blocage sans carte son | `NLayer` 1.16.0 décode mal 668 sons sur 781 |
| D. MP3, lecteur externe (`ffplay`, `mpg123`…) | 2,64 Mo | rien de fourni par Windows | selon l’installation | un processus par son ; dépendance non garantie |
| E. Musiques et ambiances en plus | + 72,1 Mo et + 7,7 Mo de MP3 | comme B | — | la moitié du budget ; `musicId` et `ambianceId` à ajouter à l’export `BotMaps` |

## Recommandation

**Ne pas ouvrir de lot son maintenant**, comme le prévoit déjà le plan des animations. Les
bruitages suivent les animations image par image : sans les bandes `hit`, `die` et `anim<n>` des
lots AN2 à AN4, il n’y a rien sur quoi les caler. Sous Mono, le seul lecteur intégré bloque sans
carte son, et les tests tournent sans carte son.

Si un lot son est décidé après les animations, il devrait :

1. versionner les **MP3 tels quels** des 793 sons appelés (2,64 Mo), avec leur provenance, et jamais
   de WAV ni de musique ;
2. ajouter à `tools/client-analysis` un relevé `sons.tsv` (SWF, animation, image, nom, fichier
   résolu) qui résout les clips imbriqués comme `swfsvg`, et lire la langue audio avec `lang2xml.py` ;
3. jouer les sons sous **Windows seulement**, par MCI après un essai sur une vraie machine
   (plusieurs sons à la fois, volume, alias refermés), sur un fil dédié et jamais sur le fil de
   l’interface ; sous Mono, ne rien jouer et ne jamais appeler `SoundPlayer` ;
4. commencer par les dix sons de l’interface et du combat, puis les 147 sons des sorts (avec AN4),
   puis les 696 sons des sprites (avec AN2 à AN4) ;
5. reprendre les options du client (`AudioEffectVol`, `AudioEffectMute`, `StartTurnSound`) et sa
   règle du focus ;
6. vérifier la chronologie par un test avec une sortie audio fictive (quel son, à quel moment), qui
   passe sans carte son.

Comme les images exportées, les sons du client restent la propriété de leurs titulaires.

## Méthode

- Sons des SWF : lecture des balises `DefineSound` (format, fréquence, canaux, nombre d’échantillons,
  octets) et `ExportAssets` (noms). La taille PCM vaut échantillons × canaux × 2.
- Langue audio : `avm1dump` puis `as2lite.py` de `tools/client-analysis`, puis décompte des
  affectations `AUE`, `AUEC`, `AUM`, `AUMC`, `AUA`, `AUAC`.
- Appels : dans chaque `DoAction` (racine et clips), la suite « nom, `SOMA`, `playSound` » ; image =
  numéro d’image du clip qui contient l’appel. Rattachement aux animations : clips exportés qui
  contiennent l’appel directement ou par leurs enfants (`PlaceObject`).
- Résolution d’un nom : comme le client, espaces et tirets changés en `_`, `é` en `e`, `à` en `a`,
  majuscules, puis recherche dans `AUEC` ; sinon nom exporté d’`effects.swf`.
- Conversion : `ffmpeg -f mp3 -i <son>.mp3 -map_metadata -1 -fflags +bitexact -flags:a +bitexact
  -c:a pcm_s16le <son>.wav`, puis zlib niveau 9 en Python.
- Mono : petits programmes C# hors dépôt (`SoundPlayer` sur fichier et sur flux mémoire, création
  directe d’`AlsaDevice` et de `WavData` par réflexion, `NLayer` 1.16.0 sur les 793 sons) ;
  désassemblage de `System.dll` par `monodis` pour la boucle de lecture.

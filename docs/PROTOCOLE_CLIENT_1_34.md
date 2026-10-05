# Protocole du client Dofus 1.34.1 relevé dans `loader.swf`

Référence générée à partir du code ActionScript 2 du client fourni (`modules/loader.swf`, classes `dofus.aks.*`), désassemblé avec `tools/client-analysis` (suivi de flux, prédicats opaques repliés, pseudo-décompilation par simulation de pile). Elle décrit ce que le **client** envoie et comment il **lit** chaque réponse : c'est la structure que le bot doit respecter. Le serveur StarLoco fourni accepte la version `1.34.1`.

- 244 routes serveur → client (préfixe de paquet → gestionnaire du client).
- 202 envois client → serveur.

Conventions : `p4` est le paquet complet, `!p3` vaut vrai quand le troisième caractère n'est pas `E` (erreur). `substr(n)` retire le préfixe. Les séparateurs usuels sont `|` entre champs, `;` dans un enregistrement, `*` entre enregistrements, `~` dans un objet et `,` dans un chemin.

Les noms de paramètres `p1`, `p2`… remplacent les noms obfusqués du client ; les méthodes et les préfixes sont ceux du client.


## Routage serveur → client

| Préfixe | Gestionnaire | Arguments |
|---|---|---|
| `AA` | `Account.onCharacterAdd` | `!p3, p4.substr(3)` |
| `AD` | `Account.onCharacterDelete` | `!p3, p4.substr(3)` |
| `AF` | `Account.onFriendServerList` | `p4.substr(2)` |
| `AG` | `Account.onGiftStored` | `!p3` |
| `AH` | `Account.onHosts` | `p4.substr(2)` |
| `AK` | `Account.onKey` | `p4.substr(2)` |
| `AL` | `Account.onCharactersList` | `!p3, p4.substr(3)` |
| `AM` | `Account.onCharactersList` | `!p3, p4.substr(3), true` |
| `AM?` | `Account.onCharactersMigrationAskConfirm` | `p4.substr(3)` |
| `AN` | `Account.onNewLevel` | `p4.substr(2)` |
| `AP` | `Account.onCharacterNameGenerated` | `!p3, p4.substr(3)` |
| `AQ` | `Account.onSecretQuestion` | `p4.substr(2)` |
| `AR` | `Account.onRestrictions` | `p4.substr(2)` |
| `AS` | `Account.onCharacterSelected` | `!p3, p4.substr(4)` |
| `AT` | `Account.onTicketResponse` | `!p3, p4.substr(3)` |
| `AV` | `Account.onRegionalVersion` | `p4.substr(2)` |
| `AX` | `Account.onSelectServer` | `!p3, true, p4.substr(3)` |
| `AY` | `Account.onSelectServer` | `!p3, false, p4.substr(3)` |
| `AZ` | `Account.onSelectServerMinimal` | `p4.substr(3)` |
| `Ac` | `Account.onCommunity` | `p4.substr(2)` |
| `Ad` | `Account.onDofusPseudo` | `p4.substr(2)` |
| `Af` | `Account.onNewQueue` | `p4.substr(2)` |
| `Ag` | `Account.onGiftsList` | `p4.substr(2)` |
| `Al` | `Account.onLogin` | `!p3, p4.substr(3)` |
| `Am` | `Account.onMiniClipInfo` | `` |
| `Aq` | `Account.onQueue` | `p4.substr(2)` |
| `Ar` | `Account.onRescue` | `!p3` |
| `As` | `Account.onStats` | `p4.substr(2)` |
| `Ax` | `Account.onServersList` | `!p3, p4.substr(3)` |
| `BAC` | `Basics.onAuthorizedCommandClear` | `` |
| `BAE` | `Basics.onAuthorizedCommand` | `false` |
| `BAI` | `Basics.onAuthorizedInterfaceClose` | `p4.substr(4)` |
| `BAI` | `Basics.onAuthorizedInterfaceOpen` | `p4.substr(4)` |
| `BAL` | `Basics.onAuthorizedLine` | `p4.substr(3)` |
| `BAP` | `Basics.onAuthorizedCommandPrompt` | `p4.substr(3)` |
| `BAT` | `Basics.onAuthorizedCommand` | `true, p4.substr(3)` |
| `BC` | `Basics.onFileCheck` | `p4.substr(2)` |
| `BD` | `Basics.onDate` | `p4.substr(2)` |
| `BM` | `Basics.onPopupMessage` | `p4.substr(2)` |
| `BP` | `Basics.onSubscriberRestriction` | `p4.substr(2)` |
| `BT` | `Basics.onReferenceTime` | `p4.substr(2)` |
| `BW` | `Basics.onWhoIs` | `!p3, p4.substr(3)` |
| `Bp` | `Basics.onAveragePing` | `p4.substr(2)` |
| `Br` | `Basics.onReportInfos` | `p4.substr(2)` |
| `CA` | `Conquest.onPrismAttacked` | `p4.substr(2)` |
| `CB` | `Conquest.onConquestBonus` | `p4.substr(2)` |
| `CD` | `Conquest.onPrismDead` | `p4.substr(2)` |
| `CIJ` | `Conquest.onPrismInfosJoined` | `p4.substr(3)` |
| `CIV` | `Conquest.onPrismInfosClosing` | `p4.substr(3)` |
| `CP` | `Conquest.onPrismFightAddPlayer` | `p4.substr(2)` |
| `CS` | `Conquest.onPrismSurvived` | `p4.substr(2)` |
| `CW` | `Conquest.onWorldData` | `p4.substr(2)` |
| `Cb` | `Conquest.onConquestBalance` | `p4.substr(2)` |
| `Cp` | `Conquest.onPrismFightAddEnemy` | `p4.substr(2)` |
| `DA` | `Dialog.onCustomAction` | `p4.substr(2)` |
| `DC` | `Dialog.onCreate` | `!p3, p4.substr(3)` |
| `DP` | `Dialog.onPause` | `` |
| `DQ` | `Dialog.onQuestion` | `p4.substr(2)` |
| `DV` | `Dialog.onLeave` | `` |
| `EA` | `Exchange.onCraftLoop` | `p4.substr(2)` |
| `EB` | `Exchange.onBuy` | `!p3` |
| `EC` | `Exchange.onCreate` | `!p3, p4.substr(3)` |
| `EHL` | `Exchange.onBigStoreTypeItemsList` | `p4.substr(3)` |
| `EHM` | `Exchange.onBigStoreTypeItemsMovement` | `p4.substr(3)` |
| `EHP` | `Exchange.onItemMiddlePriceInBigStore` | `p4.substr(3)` |
| `EHS` | `Exchange.onSearch` | `p4.substr(3)` |
| `EHl` | `Exchange.onBigStoreItemsList` | `p4.substr(3)` |
| `EHm` | `Exchange.onBigStoreItemsMovement` | `p4.substr(3)` |
| `EJ` | `Exchange.onCrafterListChanged` | `p4.substr(2)` |
| `EK` | `Exchange.onReady` | `p4.substr(2)` |
| `EL` | `Exchange.onList` | `p4.substr(2)` |
| `EM` | `Exchange.onLocalMovement` | `!p3, p4.substr(3)` |
| `ER` | `Exchange.onRequest` | `!p3, p4.substr(3)` |
| `ES` | `Exchange.onSell` | `!p3` |
| `EV` | `Exchange.onLeave` | `!p3, p4.substr(2)` |
| `EW` | `Exchange.onCraftPublicMode` | `p4.substr(2)` |
| `Ea` | `Exchange.onCraftLoopEnd` | `p4.substr(2)` |
| `Ec` | `Exchange.onCraft` | `!p3, p4.substr(3)` |
| `Ee` | `Exchange.onMountStorage` | `p4.substr(2)` |
| `Ef` | `Exchange.onMountPark` | `p4.substr(2)` |
| `Ei` | `Exchange.onPlayerShopMovement` | `!p3, p4.substr(3)` |
| `Ej` | `Exchange.onCrafterReference` | `p4.substr(2)` |
| `Em` | `Exchange.onDistantMovement` | `!p3, p4.substr(3)` |
| `Ep` | `Exchange.onPayMovement` | `!p3, p4.substr(2)` |
| `Eq` | `Exchange.onAskOfflineExchange` | `p4.substr(2)` |
| `Er` | `Exchange.onCoopMovement` | `!p3, p4.substr(3)` |
| `Es` | `Exchange.onStorageMovement` | `!p3, p4.substr(3)` |
| `Ew` | `Exchange.onMountPods` | `p4.substr(2)` |
| `FA` | `Friends.onAddFriend` | `!p3, p4.substr(3)` |
| `FD` | `Friends.onRemoveFriend` | `!p3, p4.substr(3)` |
| `FL` | `Friends.onFriendsList` | `p4.substr(3)` |
| `FO` | `Friends.onNotifyChange` | `p4.substr(2)` |
| `FS` | `Friends.onSpouse` | `p4.substr(2)` |
| `GA` | `GameActions.onActions` | `p4.substr(2)` |
| `GAF` | `GameActions.onActionsFinish` | `p4.substr(3)` |
| `GAS` | `GameActions.onActionsStart` | `p4.substr(3)` |
| `GC` | `Game.onCreate` | `!p3, p4.substr(4)` |
| `GDC` | `Game.onCellData` | `p4.substr(3)` |
| `GDE` | `Game.onFrameObjectExternal` | `p4.substring(4)` |
| `GDF` | `Game.onFrameObject2` | `p4.substring(4)` |
| `GDK` | `Game.onMapLoaded` | `` |
| `GDM` | `Game.onMapData` | `p4.substr(4)` |
| `GDO` | `Game.onCellObject` | `p4.substring(3)` |
| `GDZ` | `Game.onZoneData` | `p4.substring(3)` |
| `GE` | `Game.onEnd` | `p4.substr(2)` |
| `GIC` | `Game.onPlayersCoordinates` | `p4.substr(4)` |
| `GIE` | `Game.onEffect` | `p4.substr(3)` |
| `GIP` | `Game.onPVP` | `p4.substr(3), false` |
| `GIe` | `Game.onClearAllEffect` | `p4.substr(3)` |
| `GJ` | `Game.onJoin` | `p4.substr(3)` |
| `GM` | `Game.onMovement` | `p4.substr(3)` |
| `GO` | `Game.onGameOver` | `` |
| `GP` | `Game.onPositionStart` | `p4.substr(2)` |
| `GR` | `Game.onReady` | `p4.substr(2)` |
| `GS` | `Game.onStartToPlay` | `` |
| `GTF` | `Game.onTurnFinish` | `p4.substr(3)` |
| `GTL` | `Game.onTurnlist` | `p4.substr(4)` |
| `GTM` | `Game.onTurnMiddle` | `p4.substr(4)` |
| `GTR` | `Game.onTurnReady` | `p4.substr(3)` |
| `GTS` | `Game.onTurnStart` | `p4.substr(3)` |
| `GV` | `Game.onLeave` | `true, p4.substr(2)` |
| `GX` | `Game.onExtraClip` | `p4.substr(2)` |
| `Gc` | `Game.onChallenge` | `p4.substr(2)` |
| `Gd` | `Game.onFightChallenge` | `p4.substr(2)` |
| `Gd` | `Game.onFightChallengeUpdate` | `p4.substr(4), false` |
| `Gf` | `Game.onFlag` | `p4.substr(2)` |
| `Go` | `Game.onFightOption` | `p4.substr(2)` |
| `Gt` | `Game.onTeam` | `p4.substr(2)` |
| `IC` | `Infos.onInfoCompass` | `p4.substr(2)` |
| `IH` | `Infos.onInfoCoordinatespHighlight` | `p4.substr(2)` |
| `ILF` | `Infos.onLifeRestoreTimerFinish` | `p4.substr(3)` |
| `ILS` | `Infos.onLifeRestoreTimerStart` | `p4.substr(3)` |
| `IM` | `Infos.onInfoMaps` | `p4.substr(2)` |
| `IO` | `Infos.onObject` | `p4.substr(2)` |
| `IQ` | `Infos.onQuantity` | `p4.substr(2)` |
| `Im` | `Infos.onMessage` | `p4.substr(2)` |
| `JN` | `Job.onLevel` | `p4.substr(2)` |
| `JO` | `Job.onOptions` | `p4.substr(2)` |
| `JR` | `Job.onRemove` | `p4.substr(2)` |
| `JS` | `Job.onSkills` | `p4.substr(3)` |
| `JX` | `Job.onXP` | `p4.substr(3)` |
| `KC` | `Key.onCreate` | `p4.substr(3)` |
| `KK` | `Key.onKey` | `!p3` |
| `KV` | `Key.onLeave` | `` |
| `OA` | `Items.onAdd` | `!p3, p4.substr(3)` |
| `OC` | `Items.onChange` | `p4.substr(3)` |
| `OD` | `Items.onDrop` | `!p3, p4.substr(3)` |
| `OF` | `Items.onItemFound` | `p4.substr(2)` |
| `OK` | `Items.onItemUseCondition` | `p4.substr(2)` |
| `OM` | `Items.onMovement` | `p4.substr(2)` |
| `OQ` | `Items.onQuantity` | `p4.substr(2)` |
| `OR` | `Items.onRemove` | `p4.substr(2)` |
| `OS` | `Items.onItemSet` | `p4.substr(2)` |
| `OT` | `Items.onTool` | `p4.substr(2)` |
| `Oa` | `Items.onAccessories` | `p4.substr(2)` |
| `Ow` | `Items.onWeight` | `p4.substr(2)` |
| `PA` | `Party.onAccept` | `p4.substr(2)` |
| `PC` | `Party.onCreate` | `!p3, p4.substr(3)` |
| `PF` | `Party.onFollow` | `!p3, p4.substr(3)` |
| `PI` | `Party.onInvite` | `!p3, p4.substr(3)` |
| `PL` | `Party.onLeader` | `p4.substr(2)` |
| `PM` | `Party.onMovement` | `p4.substr(2)` |
| `PR` | `Party.onRefuse` | `p4.substr(2)` |
| `PV` | `Party.onLeave` | `p4.substr(2)` |
| `QL` | `Quests.onList` | `p4.substr(3)` |
| `QS` | `Quests.onStep` | `p4.substr(2)` |
| `RD` | `Mount.onMountParkBuy` | `p4.substr(2)` |
| `Rd` | `Mount.onData` | `p4.substr(2)` |
| `Re` | `Mount.onEquip` | `p4.substr(2)` |
| `Rn` | `Mount.onName` | `p4.substr(2)` |
| `Rp` | `Mount.onMountPark` | `p4.substr(2)` |
| `Rr` | `Mount.onRidingState` | `p4.substr(2)` |
| `Rv` | `Mount.onLeave` | `p4.substr(2)` |
| `Rx` | `Mount.onXP` | `p4.substr(2)` |
| `SB` | `Spells.onSpellBoost` | `p4.substr(2)` |
| `SF` | `Spells.onSpellForget` | `p4.substr(2)` |
| `SL` | `Spells.onList` | `p4.substr(2)` |
| `SLo` | `Spells.onChangeOption` | `p4.substr(3)` |
| `SU` | `Spells.onUpgradeSpell` | `!p3, p4.substr(3)` |
| `TB` | `Tutorial.onGameBegin` | `` |
| `TC` | `Tutorial.onCreate` | `p4.substr(2)` |
| `TT` | `Tutorial.onShowTip` | `p4.substr(2)` |
| `WC` | `Waypoints.onCreate` | `p4.substr(2)` |
| `WU` | `Waypoints.onUseError` | `` |
| `WV` | `Waypoints.onLeave` | `` |
| `Wc` | `Subway.onCreate` | `p4.substr(2)` |
| `Wp` | `Subway.onPrismCreate` | `p4.substr(2)` |
| `Wu` | `Subway.onUseError` | `` |
| `Wv` | `Subway.onLeave` | `` |
| `Ww` | `Subway.onPrismLeave` | `` |
| `ZC` | `Specialization.onChange` | `p4.substr(2)` |
| `ZS` | `Specialization.onSet` | `p4.substr(2)` |
| `aM` | `Conquest.onAreaAlignmentChanged` | `p4.substr(2)` |
| `al` | `Subareas.onList` | `p4.substr(3)` |
| `am` | `Subareas.onAlignmentModification` | `p4.substr(2)` |
| `cC` | `Chat.onSubscribeChannel` | `p4.substr(2)` |
| `cM` | `Chat.onMessage` | `!p3, p4.substr(3)` |
| `cS` | `Chat.onSmiley` | `p4.substr(2)` |
| `cs` | `Chat.onServerMessage` | `p4.substr(2)` |
| `dC` | `Documents.onCreate` | `!p3, p4.substr(3)` |
| `dV` | `Documents.onLeave` | `` |
| `eA` | `Emotes.onAdd` | `p4.substr(2)` |
| `eD` | `Emotes.onDirection` | `p4.substr(2)` |
| `eL` | `Emotes.onList` | `p4.substr(2)` |
| `eR` | `Emotes.onRemove` | `p4.substr(2)` |
| `eU` | `Emotes.onUse` | `!p3, p4.substr(3)` |
| `fC` | `Fights.onCount` | `p4.substr(2)` |
| `fD` | `Fights.onDetails` | `p4.substr(2)` |
| `fL` | `Fights.onList` | `p4.substr(2)` |
| `gA` | `Guild.onTaxCollectorAttacked` | `p4.substr(2)` |
| `gC` | `Guild.onCreate` | `!p3, p4.substr(3)` |
| `gH` | `Guild.onHireTaxCollector` | `!p3, p4.substr(3)` |
| `gIB` | `Guild.onInfosBoosts` | `p4.substr(3)` |
| `gIF` | `Guild.onInfosMountPark` | `p4.substr(3)` |
| `gIG` | `Guild.onInfosGeneral` | `p4.substr(3)` |
| `gIH` | `Guild.onInfosHouses` | `p4.substr(3)` |
| `gIM` | `Guild.onInfosMembers` | `p4.substr(3)` |
| `gIT` | `Guild.onInfosTaxCollectorsAttackers` | `p4.substr(4)` |
| `gIT` | `Guild.onInfosTaxCollectorsPlayers` | `p4.substr(4)` |
| `gIT` | `Guild.onInfosTaxCollectorsMovement` | `p4.substr(4)` |
| `gJC` | `Guild.onJoinDistantOk` | `` |
| `gJE` | `Guild.onJoinError` | `p4.substr(3)` |
| `gJK` | `Guild.onJoinOk` | `p4.substr(3)` |
| `gJR` | `Guild.onRequestLocal` | `p4.substr(3)` |
| `gJr` | `Guild.onRequestDistant` | `p4.substr(3)` |
| `gK` | `Guild.onBann` | `!p3, p4.substr(3)` |
| `gS` | `Guild.onStats` | `p4.substr(2)` |
| `gT` | `Guild.onTaxCollectorInfo` | `p4.substr(2)` |
| `gU` | `Guild.onUserInterfaceOpen` | `p4.substr(2)` |
| `gV` | `Guild.onLeave` | `` |
| `gn` | `Guild.onNew` | `` |
| `hB` | `Houses.onBuy` | `!p3, p4.substr(3)` |
| `hC` | `Houses.onCreate` | `p4.substr(3)` |
| `hG` | `Houses.onGuildInfos` | `p4.substr(2)` |
| `hL` | `Houses.onList` | `p4.substr(2)` |
| `hP` | `Houses.onProperties` | `p4.substr(2)` |
| `hS` | `Houses.onSell` | `!p3, p4.substr(3)` |
| `hV` | `Houses.onLeave` | `` |
| `hX` | `Houses.onLockedProperty` | `p4.substr(2)` |
| `iA` | `Enemies.onAddEnemy` | `!p3, p4.substr(3)` |
| `iD` | `Enemies.onRemoveEnemy` | `!p3, p4.substr(3)` |
| `iL` | `Enemies.onEnemiesList` | `p4.substr(3)` |
| `sL` | `Storages.onList` | `p4.substr(2)` |
| `sX` | `Storages.onLockedProperty` | `p4.substr(2)` |

## Envois client → serveur

| Préfixe | Méthode du client | Paquet construit |
|---|---|---|
| `?` | `Account.logon(p1, p2, p3)` | `(((((((dofus.Constants.VERSION + ".") + dofus.Constants.SUBVERSION) + ".") + dofus.Constants.SUBSUBVERSION) + ` |
| `?` | `Account.logon(p1, p2, p3)` | `((p1 + "\n") + p2)` |
| `?` | `Account.logon(p1, p2, p3)` | `((p1 + "\n") + r6)` |
| `?` | `Account.logon(p1, p2, p3)` | `((p1 + "\n") + ank["\x1e\n\x07"]["\x11\x17"].cryptPassword(p2, this.api.datacenter.Basics.connexionKey))` |
| `?` | `Account.setNickName(p1)` | `p1, true, this.api.lang.getText("WAITING_MSG_LOADING")` |
| `AA` | `Account.addCharacter(p1, p2, p3, p4, p5, p6)` | `((((((((((("AA" + p1) + "\|") + p2) + "\|") + p6) + "\|") + p3) + "\|") + p4) + "\|") + p5), true, this.api.lang.ge` |
| `AB` | `Account.boost(p1)` | `("AB" + p1)` |
| `AD` | `Account.deleteCharacter(p1, p2)` | `((("AD" + p1) + "\|") + r4.replace(["\|", "\r", "\n", String.fromCharCode(0)], ["", "", "", ""])), true, this.ap` |
| `AEc` | `Account.editCharacterColors(p1, p2, p3)` | `((((("AEc" + p1) + "\|") + p2) + "\|") + p3), true` |
| `AEi0|` | `Items.destroyMimibiote(p1)` | `("AEi0\|" + p1)` |
| `AEi1|` | `Items.associateMimibiote(p1, p2)` | `((("AEi1\|" + p1) + "\|") + p2)` |
| `AEn` | `Account.editCharacterName(p1)` | `("AEn" + p1), true` |
| `AF` | `Account.searchForFriend(p1)` | `("AF" + p1)` |
| `AG` | `Account.attributeGiftToCharacter(p1, p2)` | `((("AG" + p1) + "\|") + p2)` |
| `AL` | `Account.getCharacters()` | `"AL", true, this.api.lang.getText("CONNECTING")` |
| `ALf` | `Account.getCharactersForced()` | `"ALf", true, this.api.lang.getText("CONNECTING")` |
| `AM` | `Account.validCharacterMigration(p1, p2)` | `((("AM" + p1) + ";") + p2), false` |
| `AM-` | `Account.deleteCharacterMigration(p1)` | `("AM-" + p1), false` |
| `AM?` | `Account.askCharacterMigration(p1, p2)` | `((("AM?" + p1) + ";") + p2), false` |
| `AP` | `Account.getRandomCharacterName()` | `"AP", false` |
| `AR` | `Account.resetCharacter(p1)` | `("AR" + p1)` |
| `AS` | `Account.setCharacter(p1)` | `("AS" + p1), true, this.api.lang.getText("WAITING_MSG_LOADING")` |
| `AT` | `Account.sendTicket(p1)` | `("AT" + p1)` |
| `AV` | `Account.requestRegionalVersion()` | `"AV", true, this.api.lang.getText("WAITING_MSG_LOADING")` |
| `AX` | `Account.setServer(p1)` | `("AX" + p1), true, this.api.lang.getText("WAITING_MSG_LOADING")` |
| `Af` | `Account.getQueuePosition()` | `"Af", false` |
| `Ag` | `Account.getGifts()` | `("Ag" + this.api.config.language)` |
| `Ai` | `Account.sendIdentity()` | `("Ai" + this.api.datacenter.Basics.aks_identity), false` |
| `Ak` | `Account.useKey(p1)` | `("Ak" + dofus.aks["\x1e\x0f"].HEX_CHARS[p1]), false` |
| `Ap` | `Account.sendConfiguredPort()` | `("Ap" + this.api.datacenter.Basics.aks_connection_server_port), false` |
| `Ar` | `Account.rescue(p1)` | `(("Ar" + p1) + r3)` |
| `Ax` | `Account.getServersList()` | `"Ax", true, this.api.lang.getText("WAITING_MSG_LOADING")` |
| `BA` | `Basics.autorisedCommand(p1)` | `("BA" + p1), false, undefined, true` |
| `BC` | `Basics.fileCheckAnswer(p1, p2)` | `((("BC" + p1) + ";") + p2), false` |
| `BD` | `Basics.getDate()` | `"BD", false` |
| `BK` | `Basics.sanctionMe(p1, p2)` | `((("BK" + p1) + "\|") + p2), false` |
| `BM` | `Chat.send(p1, p2, p3)` | `((((("BM" + p2) + "\|") + p1) + "\|") + r16), true, undefined, true` |
| `BQ` | `Basics.kick(p1)` | `("BQ" + p1), false` |
| `BR` | `Chat.reportMessage(p1, p2, p3, p4)` | `((((((("BR" + p1) + "\|") + p3) + "\|") + p2) + "\|") + p4), false` |
| `BS` | `Chat.useSmiley(p1)` | `("BS" + p1), true` |
| `BW` | `Basics.whoIs(p1)` | `("BW" + p1)` |
| `BYA` | `Basics.away()` | `"BYA", false` |
| `BYI` | `Basics.invisible()` | `"BYI", false` |
| `BaK` | `Basics.autorisedKickCommand(p1, p2, p3)` | `((((("BaK" + p1) + "\|") + p2) + "\|") + p3), false` |
| `BaM` | `Basics.autorisedMoveCommand(nX, nY)` | `((("BaM" + nX) + ",") + nY), false` |
| `Bp` | `Basics.averagePing()` | `((((("Bp" + this.api.network.getAveragePing()) + "\|") + this.api.network.getAveragePingPacketsCount()) + "\|") ` |
| `Br` | `Basics.askReportInfos(p1, p2, p3)` | `((((("Br" + p1) + "\|") + p2) + "\|") + "0")` |
| `CB` | `Conquest.getAlignedBonus()` | `"CB", true` |
| `CFJ` | `Conquest.prismFightJoin()` | `"CFJ", true` |
| `CFS` | `Conquest.switchPlaces(p1)` | `("CFS" + p1), true` |
| `CFV` | `Conquest.prismFightLeave()` | `"CFV", false` |
| `CIJ` | `Conquest.prismInfosJoin()` | `"CIJ", true` |
| `CIV` | `Conquest.prismInfosLeave()` | `"CIV", false` |
| `CWJ` | `Conquest.worldInfosJoin()` | `"CWJ", false` |
| `CWV` | `Conquest.worldInfosLeave()` | `"CWV", false` |
| `Cb` | `Conquest.requestBalance()` | `"Cb", true` |
| `DB` | `Dialog.begining(p1)` | `("DB" + p1), true` |
| `DC` | `Dialog.create(p1)` | `("DC" + p1), true` |
| `DR` | `Dialog.response(p1, p2)` | `((("DR" + p1) + "\|") + p2), true` |
| `DV` | `Dialog.leave()` | `"DV", true` |
| `EA` | `Exchange.accept()` | `"EA", false` |
| `EB` | `Exchange.buy(p1, p2)` | `((("EB" + p1) + "\|") + p2), true` |
| `EHB` | `Exchange.bigStoreBuy(p1, p2, p3)` | `((((("EHB" + p1) + "\|") + p2) + "\|") + p3), true` |
| `EHP` | `Exchange.getItemMiddlePriceInBigStore(p1)` | `("EHP" + p1), false` |
| `EHS` | `Exchange.bigStoreSearch(p1, p2)` | `((("EHS" + p1) + "\|") + p2)` |
| `EHT` | `Exchange.bigStoreType(p1)` | `("EHT" + p1)` |
| `EHl` | `Exchange.bigStoreItemList(p1)` | `("EHl" + p1)` |
| `EJF` | `Exchange.getCrafterForJob(p1)` | `("EJF" + p1), true` |
| `EK` | `Exchange.ready()` | `"EK", true` |
| `EL` | `Exchange.replayCraft()` | `"EL", false` |
| `EMG` | `Exchange.movementKama(p1)` | `("EMG" + p1), true` |
| `EMO` | `Exchange.movementItem(p1, p2, p3, p4)` | `((((("EMO" + "-") + p2.ID) + "\|") + p3) + ("\|" + p4)), true` |
| `EMO` | `Exchange.movementItems(p1)` | `("EMO" + r3), true, undefined, true` |
| `EMR` | `Exchange.repeatCraft(p1)` | `("EMR" + p1), false` |
| `EMr` | `Exchange.stopRepeatCraft()` | `"EMr", false` |
| `EP` | `Exchange.movementPayItem(p1, p2, p3, p4, p5)` | `((((((("EP" + p1) + "O") + "-") + p3) + "\|") + p4) + ("\|" + p5)), true` |
| `EP` | `Exchange.movementPayKama(p1, p2)` | `((("EP" + p1) + "G") + p2), true` |
| `EQ` | `Exchange.offlineExchange()` | `"EQ", true` |
| `ER` | `Exchange.request(p1, p2, p3)` | `(((("ER" + p1) + "\|") + p2) + ("\|" + p3)), true` |
| `ES` | `Exchange.sell(p1, p2)` | `((("ES" + p1) + "\|") + p2), true` |
| `EW` | `Exchange.setPublicMode(p1)` | `("EW" + "-"), false` |
| `Eff` | `Exchange.killMountInPark(p1)` | `("Eff" + p1), false` |
| `Efg` | `Exchange.putInShedFromMountPark(p1)` | `("Efg" + p1), true` |
| `Efp` | `Exchange.putInMountParkFromShed(p1)` | `("Efp" + p1), true` |
| `Eq` | `Exchange.askOfflineExchange()` | `"Eq", true` |
| `ErC` | `Exchange.putInShedFromCertificate(p1)` | `("ErC" + p1), true` |
| `Erc` | `Exchange.putInCertificateFromShed(p1)` | `("Erc" + p1), true` |
| `Erf` | `Exchange.killMount(p1)` | `("Erf" + p1), false` |
| `Erg` | `Exchange.putInInventoryFromShed(p1)` | `("Erg" + p1), true` |
| `Erp` | `Exchange.putInShedFromInventory(p1)` | `("Erp" + p1), true` |
| `Es` | `Exchange.shop(p1)` | `("Es" + p1)` |
| `FA` | `Friends.addFriend(p1)` | `("FA" + p1)` |
| `FD` | `Friends.removeFriend(p1)` | `("FD" + p1)` |
| `FJ` | `Friends.join(p1)` | `("FJ" + p1)` |
| `FJC` | `Friends.compass(p1)` | `("FJC" + "+")` |
| `FJF` | `Friends.joinFriend(p1)` | `("FJF" + p1)` |
| `FL` | `Friends.getFriendsList()` | `"FL", true` |
| `FO` | `Friends.setNotifyWhenConnect(p1)` | `("FO" + "-")` |
| `GA` | `GameActions.sendActions(p1, p2)` | `(("GA" + new ank["\x1e\n\x07"]["\x0e\x1c"](p1).addLeftChar("0", 3)) + p2.join(";"))` |
| `GC` | `Game.create()` | `("GC" + dofus.aks.Game.TYPE_SOLO)` |
| `GD` | `Game.getMapData(p1)` | `("GD" + "")` |
| `GF` | `Game.freeMySoul()` | `"GF", false` |
| `GI` | `Game.getExtraInformations()` | `"GI"` |
| `GKE` | `GameActions.actionCancel(p1, p2)` | `((("GKE" + p1) + "\|") + p2), false` |
| `GKK` | `GameActions.actionAck(p1)` | `("GKK" + p1), false` |
| `GP` | `Game.enabledPVPMode(p1)` | `("GP" + "-"), false` |
| `GP*` | `Game.askDisablePVPMode()` | `"GP*", false` |
| `GQ` | `Game.leave(p1)` | `("GQ" + p1)` |
| `GR` | `Game.ready(p1)` | `("GR" + "0")` |
| `GT` | `Game.turnOk(p1)` | `("GT" + ""), false` |
| `GT` | `Game.turnOk2(p1)` | `("GT" + ""), false` |
| `Gdi` | `Game.showFightChallengeTarget(p1)` | `("Gdi" + p1), false` |
| `Gf` | `Game.setFlag(p1)` | `("Gf" + p1), false` |
| `Gp` | `Game.setPlayerPosition(p1)` | `("Gp" + p1), true` |
| `Gt` | `Game.turnEnd()` | `"Gt", false` |
| `IM` | `Infos.getMaps()` | `"IM"` |
| `Ir` | `Infos.sendScreenInfo()` | `((((("Ir" + Stage.width) + ";") + Stage.height) + ";") + r3)` |
| `JO` | `Job.changeJobStats(p1, p2, minSlots)` | `((((("JO" + p1) + "\|") + p2) + "\|") + minSlots)` |
| `KK` | `Key.sendKey(p1, p2)` | `((("KK" + p1) + "\|") + p2)` |
| `KV` | `Key.leave()` | `"KV", false` |
| `O` | `Items.use(p1, p2, p3, p4)` | `(((("O" + "U") + p1) + "\|") + ""), true` |
| `OD` | `Items.drop(p1, p2)` | `((("OD" + p1) + "\|") + p2), false` |
| `OM` | `Items.movement(p1, p2, p3)` | `(((("OM" + p1) + "\|") + p2) + ("\|" + p3)), true` |
| `Od` | `Items.destroy(p1, p2)` | `((("Od" + p1) + "\|") + p2), false` |
| `Of` | `Items.feed(p1, p2, p3)` | `((((("Of" + p1) + "\|") + p2) + "\|") + p3), false` |
| `Os` | `Items.setSkin(p1, p2, p3)` | `((((("Os" + p1) + "\|") + p2) + "\|") + p3), false` |
| `Ox` | `Items.dissociate(p1, p2)` | `((("Ox" + p1) + "\|") + p2), false` |
| `PA` | `Party.acceptInvitation()` | `"PA"` |
| `PF` | `Party.follow(p1, p2)` | `(("PF" + "+") + p2)` |
| `PG` | `Party.followAll(p1, p2)` | `(("PG" + "+") + p2)` |
| `PI` | `Party.invite(p1)` | `("PI" + p1)` |
| `PR` | `Party.refuseInvitation()` | `"PR", false` |
| `PV` | `Party.leave(p1)` | `("PV" + "")` |
| `PW` | `Party.where()` | `"PW"` |
| `QL` | `Quests.getList()` | `"QL"` |
| `QS` | `Quests.getStep(p1, p2)` | `(("QS" + p1) + "")` |
| `Rb` | `Mount.mountParkBuy(p1)` | `("Rb" + p1), true` |
| `Rc` | `Mount.castrate()` | `"Rc"` |
| `Rd` | `Mount.data(p1, p2)` | `((("Rd" + p1) + "\|") + p2), true` |
| `Rf` | `Mount.kill()` | `"Rf"` |
| `Rn` | `Mount.rename(p1)` | `("Rn" + p1), true` |
| `Ro` | `Mount.removeObjectInPark(p1)` | `("Ro" + p1), true` |
| `Rp` | `Mount.parkMountData(p1)` | `("Rp" + p1), true` |
| `Rr` | `Mount.ride()` | `"Rr", false` |
| `Rs` | `Mount.mountParkSell(p1)` | `("Rs" + p1), true` |
| `Rv` | `Mount.leave()` | `"Rv"` |
| `Rx` | `Mount.setXP(p1)` | `("Rx" + p1), true` |
| `SB` | `Spells.boost(p1)` | `("SB" + p1)` |
| `SF` | `Spells.spellForget(p1)` | `("SF" + p1)` |
| `SM` | `Spells.moveToUsed(p1, p2)` | `((("SM" + p1) + "\|") + p2), false` |
| `TV` | `Tutorial.end(p1, p2, p3)` | `("TV" + String(p1)), false` |
| `TV` | `Tutorial.end(p1, p2, p3)` | `((((("TV" + String(p1)) + "\|") + String(p2)) + "\|") + String(p3)), false` |
| `WU` | `Waypoints.use(p1)` | `("WU" + p1), true` |
| `WV` | `Waypoints.leave()` | `"WV", true` |
| `Wp` | `Subway.prismUse(p1)` | `("Wp" + p1)` |
| `Wu` | `Subway.use(p1)` | `("Wu" + p1)` |
| `Wv` | `Subway.leave()` | `"Wv"` |
| `Ww` | `Subway.prismLeave()` | `"Ww"` |
| `cC` | `Chat.subscribeChannels(p1, p2)` | `(("cC" + "-") + r4), true` |
| `dV` | `Documents.leave()` | `"dV"` |
| `eD` | `Emotes.setDirection(p1)` | `("eD" + p1), true` |
| `eU` | `Emotes.useEmote(p1)` | `("eU" + p1), true` |
| `fD` | `Fights.getDetails(p1)` | `("fD" + p1), false` |
| `fH` | `Fights.needHelp()` | `"fH"` |
| `fL` | `Fights.getList()` | `"fL"` |
| `fN` | `Fights.blockJoiner()` | `"fN"` |
| `fP` | `Fights.blockJoinerExceptParty()` | `"fP"` |
| `fS` | `Fights.blockSpectators()` | `"fS"` |
| `gB` | `Guild.boostCharacteristic(p1)` | `("gB" + r3), true` |
| `gC` | `Guild.create(p1, p2, p3, p4, p5)` | `((((((((("gC" + p1) + "\|") + p2) + "\|") + p3) + "\|") + p4) + "\|") + p5)` |
| `gF` | `Guild.removeTaxCollector(p1)` | `("gF" + p1), false` |
| `gH` | `Guild.hireTaxCollector()` | `"gH"` |
| `gIB` | `Guild.getInfosBoosts()` | `"gIB", true` |
| `gIF` | `Guild.getInfosMountPark()` | `"gIF", false` |
| `gIG` | `Guild.getInfosGeneral()` | `"gIG", true` |
| `gIH` | `Guild.getInfosGuildHouses()` | `"gIH", false` |
| `gIM` | `Guild.getInfosMembers()` | `"gIM", true` |
| `gIT` | `Guild.getInfosTaxCollector()` | `"gIT", false` |
| `gITV` | `Guild.leaveTaxInterface()` | `"gITV", false` |
| `gJE` | `Guild.refuseInvitation(p1)` | `("gJE" + p1), false` |
| `gJK` | `Guild.acceptInvitation(p1)` | `("gJK" + p1)` |
| `gJR` | `Guild.invite(p1)` | `("gJR" + p1)` |
| `gK` | `Guild.bann(p1)` | `("gK" + p1)` |
| `gP` | `Guild.changeMemberProfil(p1)` | `((((((("gP" + p1.id) + "\|") + p1.rank) + "\|") + p1.percentxp) + "\|") + p1.rights.value), true` |
| `gTJ` | `Guild.joinTaxCollector(p1)` | `("gTJ" + p1), false` |
| `gTV` | `Guild.leaveTaxCollector(p1, p2)` | `(("gTV" + p1) + ""), false` |
| `gV` | `Guild.leave()` | `"gV"` |
| `gb` | `Guild.boostSpell(p1)` | `("gb" + p1), true` |
| `gf` | `Guild.teleportToGuildFarm(p1)` | `("gf" + p1), false` |
| `gh` | `Guild.teleportToGuildHouse(p1)` | `("gh" + p1), false` |
| `hB` | `Houses.buy(p1)` | `("hB" + p1), true` |
| `hG` | `Houses.rights(p1)` | `("hG" + p1), true` |
| `hG` | `Houses.state()` | `"hG", true` |
| `hG+` | `Houses.share()` | `"hG+", true` |
| `hG-` | `Houses.unshare()` | `"hG-", true` |
| `hQ` | `Houses.kick(p1)` | `("hQ" + p1)` |
| `hS` | `Houses.sell(p1)` | `("hS" + p1), true` |
| `hV` | `Houses.leave()` | `"hV"` |
| `iA` | `Enemies.addEnemy(p1)` | `("iA" + p1)` |
| `iD` | `Enemies.removeEnemy(p1)` | `("iD" + p1)` |
| `iL` | `Enemies.getEnemiesList()` | `"iL", true` |
| `rpong` | `\x11\x0c.postProcess(p1, p2, p3, p4)` | `("rpong" + p4.substr(5)), false` |

## Lecture des réponses par domaine

Pour chaque gestionnaire, les lignes de découpage du paquet telles que le client les exécute (champs, séparateurs, conversions). Les corps complets sont dans la sortie de l'outil, non versionnée.


### Account : Compte, serveurs et personnages

- `AA` → `onCharacterAdd(p1, p2)`
  ```
  if ((r0 === "s")) goto @107;
  if ((r0 === "f")) goto @457;
  if ((r0 === "a")) goto @408;
  if ((r0 === "n")) goto @231;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CREATE_CHARACTER_ERROR"), "ERROR_BOX", {"name": "CreateNameExists"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CREATE_CHARACTER_BAD_NAME"), "ERROR_BOX", {"name": "CreateNameExists"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NAME_ALEREADY_EXISTS"), "ERROR_BOX", {"name": "CreateNameExists"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CREATE_CHARACTER_FULL"), "ERROR_BOX", {"name": "CreateNameExists"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("SUBSCRIPTION_OUT"), "ERROR_BOX", {"name": "CreateNameExists"});
  ```
- `AD` → `onCharacterDelete(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CHARACTER_DELETION_FAILED"), "ERROR_BOX");
  ```
- `AF` → `onFriendServerList(p1)`
  ```
  r3 = p1.split(";");
  r4 = new Array();
  r6 = r3[r5].split(",");
  r4.push({"server": r6[0], "count": r6[1]});
  ```
- `AG` → `onGiftStored(p1)`
- `AH` → `onHosts(p1)`
  ```
  r4 = new Array();
  r5 = p1.split("|");
  r7 = r5[r6].split(";");
  r8 = Number(r7[0]);
  r9 = Number(r7[1]);
  r10 = Number(r7[2]);
  r11 = (r7[3] == "1");
  r4.push(r12);
  ```
- `AK` → `onKey(p1)`
  ```
  r3 = _global.parseInt(p1.substr(0, 1), 16);
  r4 = p1.substr(1);
  ```
- `AL`, `AM` → `onCharactersList(p1, p2, p3)`
  ```
  r5 = new Array();
  r6 = p2.split("|");
  r7 = Number(r6[0]);
  r8 = Number(r6[1]);
  r9 = new Array();
  r11 = r6[r10].split(";");
  r13 = r11[0];
  r14 = r11[1];
  r12.level = r11[2];
  r12.gfxID = r11[3];
  r12.color1 = r11[4];
  r12.color2 = r11[5];
  r12.color3 = r11[6];
  r12.accessories = r11[7];
  r12.merchant = r11[8];
  r12.serverID = r11[9];
  r12.isDead = r11[10];
  r12.deathCount = r11[11];
  r12.lvlMax = r11[12];
  r15.sortID = Number(r13);
  r5.push(r15);
  r9.push(Number(r13));
  ```
- `AM?` → `onCharactersMigrationAskConfirm(p1)`
  ```
  r3 = p1.split(";");
  r4 = _global.parseInt(r3[0], 10);
  r5 = r3[1];
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CONFIRM_MIGRATION", [r5]), "CAUTION_YESNO", r6);
  ```
- `AN` → `onNewLevel(p1)`
  ```
  r3 = Number(p1);
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("NEW_LEVEL", [r3]), "ERROR_BOX", {"name": "NewLevel"});
  ```
- `AP` → `onCharacterNameGenerated(p1, p2)`
  ```
  if ((r0 === "1")) goto @111;
  if ((r0 === "2")) goto @116;
  ```
- `AQ` → `onSecretQuestion(p1)`
- `AR` → `onRestrictions(p1)`
  ```
  this.api.datacenter.Player.restrictions = _global.parseInt(p1, 36);
  ```
- `AS` → `onCharacterSelected(p1, p2)`
  ```
  r4 = p2.split("|");
  r6 = Number(r4[0]);
  r7 = r4[1];
  r5.level = r4[2];
  r5.guild = r4[3];
  r5.sex = r4[4];
  r5.gfxID = r4[5];
  r5.color1 = r4[6];
  r5.color2 = r4[7];
  r5.color3 = r4[8];
  r5.items = r4[9];
  ```
- `AT` → `onTicketResponse(p1, p2)`
  ```
  r4 = _global.parseInt(p2.substr(0, 1), 16);
  this.aks.addKeyToCollection(r4, p2.substr(1));
  ```
- `AV` → `onRegionalVersion(p1)`
  ```
  r4 = Number(p1);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("SWITCH_TO_ENGLISH"), "CAUTION_YESNO", r5);
  ```
- `AX`, `AY` → `onSelectServer(p1, p2, p3)`
  ```
  r8 = p3.substr(0, 8);
  r9 = p3.substr(8, 3);
  r7 = p3.substr(11);
  r10 = new Array();
  r10.push((((r12 & 15) << 4) | (r13 & 15)));
  r6 = ((((ank.util.decode64(r9.charAt(0)) & 63) << 12) | ((ank.util.decode64(r9.charAt(1)) & 63) << 6)) | (ank.util.decode64(r9.charAt(2)) & 63));
  r14 = p3.split(";");
  r15 = r14[0].split(":");
  r5 = r15[0];
  r6 = r15[1];
  r7 = r14[1];
  r0 = p3.charAt(0);
  if ((r0 === "d")) goto @1822;
  if ((r0 === "f")) goto @1616;
  if ((r0 === "F")) goto @2343;
  if ((r0 === "s")) goto @2195;
  if ((r0 === "r")) goto @2035;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_SELECT_THIS_SERVER"), "ERROR_BOX");
  r21 = this.api.lang.getServerInfos(Number(p3.substr(1))).n;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_CHOOSE_CHARACTER_SHOP_OTHER_SERVER", [r21]), "ERROR_BOX");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("SERVER_FULL"), "ERROR_BOX");
  r17 = this.api.lang.getText("CANT_CHOOSE_CHARACTER_SERVER_FULL");
  if (!(p3.substr(1).length > 0)) goto @2307;
  r18 = p3.substr(1).split("|");
  r17 = (r17 + (this.api.lang.getText("SERVERS_ACCESSIBLES") + " : <br/>"));
  this.api.kernel.showMessage(undefined, r17, "ERROR_BOX");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_CHOOSE_CHARACTER_SERVER_DOWN"), "ERROR_BOX");
  ```
- `AZ` → `onSelectServerMinimal(p1)`
  ```
  r3 = Number(p1);
  ```
- `Ac` → `onCommunity(p1)`
  ```
  r3 = Number(p1);
  ```
- `Ad` → `onDofusPseudo(p1)`
- `Af` → `onNewQueue(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = Number(r3[2]);
  r0 = r3[3];
  if ((r0 === "0")) goto @231;
  if ((r0 === "1")) goto @246;
  r8 = Number(r3[4]);
  ```
- `Ag` → `onGiftsList(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = r3[2];
  r7 = r3[3];
  r8 = r3[4];
  r9 = r3[5];
  r14 = new Array();
  r15 = r9.split(";");
  if (!!(r15[r16] == "")) goto @220;
  r14.push(r17);
  this.api.datacenter.Basics.aks_gifts_stack.push(r18);
  ```
- `Al` → `onLogin(p1, p2)`
  ```
  this.api.datacenter.Player.isAuthorized = (p2 == "1");
  r4 = p2.charAt(0);
  if ((r0 === "n")) goto @351;
  if ((r0 === "a")) goto @482;
  if ((r0 === "c")) goto @519;
  if ((r0 === "v")) goto @1256;
  if ((r0 === "p")) goto @1086;
  if ((r0 === "b")) goto @929;
  if ((r0 === "d")) goto @966;
  if ((r0 === "k")) goto @802;
  if ((r0 === "w")) goto @1568;
  if ((r0 === "o")) goto @1605;
  if ((r0 === "e")) goto @1795;
  if ((r0 === "m")) goto @1873;
  if ((r0 === "r")) goto @1636;
  if ((r0 === "s")) goto @1671;
  if ((r0 === "i")) goto @1428;
  if ((r0 === "f")) goto @1721;
  r5 = this.api.lang.getText("ACCESS_DENIED");
  r5 = this.api.lang.getText("ACCESS_DENIED_MINICLIP");
  r5 = this.api.lang.getText("LOGIN_ERROR_ANONYMOUS_IP");
  r5 = this.api.lang.getText("MAINTAIN_ACCOUNT");
  r5 = this.api.lang.getText("OLD_ACCOUNT_USE_NEW", [this.api.datacenter.Basics.login]);
  r5 = this.api.lang.getText("OLD_ACCOUNT", [this.api.datacenter.Basics.login]);
  r5 = this.api.lang.getText("SERVER_FULL");
  r7 = p2.substr(1).split("|");
  r5 = ank.util.getDescription(this.api.lang.getText("KICKED"), r7);
  r5 = this.api.lang.getText("U_DISCONNECT_ACCOUNT");
  …
  ```
- `Am` → `onMiniClipInfo()`
- `Aq` → `onQueue(p1)`
  ```
  r3 = Number(p1);
  ```
- `Ar` → `onRescue(p1)`
- `As` → `onStats(p1)`
  ```
  r3 = p1.split("|");
  r5 = r3[0].split(",");
  r4.XP = r5[0];
  r4.XPlow = r5[1];
  r4.XPhigh = r5[2];
  r4.Kama = r3[1];
  r4.BonusPoints = r3[2];
  r4.BonusPointsSpell = r3[3];
  r5 = r3[4].split(",");
  if (!r5[0].indexOf("~")) goto @668;
  r7 = r5[0].split("~");
  r4.haveFakeAlignment = !(r7[0] == r7[1]);
  r5[0] = r7[0];
  r6 = Number(r7[1]);
  r8 = Number(r5[0]);
  r9 = Number(r5[1]);
  r10 = Number(r5[2]);
  r11 = Number(r5[3]);
  r12 = Number(r5[4]);
  if ((r5[5] == "1")) goto @158;
  r5 = r3[5].split(",");
  r4.LP = r5[0];
  r4.data.LP = r5[0];
  r4.LPmax = r5[1];
  r4.data.LPmax = r5[1];
  r5 = r3[6].split(",");
  r4.EnergyMax = r5[1];
  r4.Energy = r5[0];
  …
  ```
- `Ax` → `onServersList(p1, p2)`
  ```
  r5 = p2.split("|");
  r6 = Number(r5[0]);
  r10 = r5[r9].split(",");
  r11 = Number(r10[0]);
  r12 = Number(r10[1]);
  ```

### Basics : Base : date, commandes, messages

- `BAC` → `onAuthorizedCommandClear()`
- `BAE`, `BAT` → `onAuthorizedCommand(p1, p2)`
  ```
  r4 = Number(p2.charAt(0));
  r6 = p2.substr(1);
  this.api.kernel.showMessage(undefined, r6, r5);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("UNKNOW_COMMAND", ["/a"]), "ERROR_CHAT");
  ```
- `BAI` → `onAuthorizedInterfaceClose(p1)`
  ```
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("A_REMOVE_U_RIGHTS", [p1]), "ERROR_BOX");
  ```
- `BAI` → `onAuthorizedInterfaceOpen(p1)`
  ```
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("A_GIVE_U_RIGHTS", [p1]), "ERROR_BOX");
  ```
- `BAL` → `onAuthorizedLine(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = r3[2];
  r7 = this.api.datacenter.Basics.aks_a_logs.split("<br/>");
  ```
- `BAP` → `onAuthorizedCommandPrompt(p1)`
- `BC` → `onFileCheck(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r5 = r3[1];
  p1 = r5.substr(10);
  ```
- `BD` → `onDate(p1)`
  ```
  r3 = p1.split("|");
  this.api.kernel.NightManager.setReferenceDate(Number(r3[0]), Number(r3[1]), Number(r3[2]));
  ```
- `BM` → `onPopupMessage(p1)`
  ```
  this.api.kernel.showMessage(undefined, r3, "WAIT_BOX");
  ```
- `BP` → `onSubscriberRestriction` (corps non retrouvé)
- `BT` → `onReferenceTime(p1)`
  ```
  r3 = Number(p1);
  ```
- `BW` → `onWhoIs(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = r4[0];
  r6 = r4[1];
  r7 = r4[2];
  if (!(Number(r4[3]) == -1)) goto @361;
  r8 = this.api.lang.getText("UNKNOWN_AREA");
  if ((r0 === "1")) goto @73;
  if ((r0 === "2")) goto @496;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("I_AM_IN_GAME", [r7, r5, r8]), "COMMANDS_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("I_AM_IN_SINGLE_GAME", [r7, r5, r8]), "COMMANDS_CHAT");
  if ((r0 === "1")) goto @279;
  if ((r0 === "2")) goto @163;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("IS_IN_GAME", [r7, r5, r8]), "COMMANDS_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("IS_IN_SINGLE_GAME", [r7, r5, r8]), "COMMANDS_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_FIND_ACCOUNT_OR_CHARACTER", [p2]), "ERROR_CHAT");
  ```
- `Bp` → `onAveragePing` (corps non retrouvé)
- `Br` → `onReportInfos(p1)`
  ```
  r4 = p1.charAt(0);
  r5 = p1.substring(1);
  if ((r0 === "t")) goto @363;
  if ((r0 === "s")) goto @78;
  if ((r0 === "p")) goto @303;
  if ((r0 === "f")) goto @318;
  if ((r0 === "#")) goto @333;
  r6 = r5.split("|");
  r3.targetAccountPseudo = r6[0];
  r3.targetAccountId = r6[1];
  ```

### Chat : Discussion

- `cC` → `onSubscribeChannel(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("");
  if ((r0 === "i")) goto @313;
  if ((r0 === "*")) goto @331;
  if ((r0 === "#")) goto @349;
  if ((r0 === "$")) goto @168;
  if ((r0 === "p")) goto @186;
  if ((r0 === "%")) goto @204;
  if ((r0 === "!")) goto @450;
  if ((r0 === "?")) goto @468;
  if ((r0 === ":")) goto @486;
  if ((r0 === "^")) goto @504;
  if ((r0 === "@")) goto @598;
  ```
- `cM` → `onMessage(p1, p2)`
  ```
  r0 = p2.charAt(0);
  if ((r0 === "S")) goto @658;
  if ((r0 === "f")) goto @547;
  if ((r0 === "e")) goto @713;
  if ((r0 === "n")) goto @278;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("USER_NOT_CONNECTED_EXTERNAL_NACK", [p2.substr(1)]), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("USER_NOT_CONNECTED_BUT_TRY_SEND_EXTERNAL", [p2.substr(1)]), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("USER_NOT_CONNECTED", [p2.substr(1)]), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("SYNTAX_ERROR", [((((" /w <" + this.api.lang.getText("NAME")) + "> <") + this.api.lang.getText("MSG")) + ">")]), "ERROR_CHAT");
  r4 = p2.charAt(0);
  if ((r4 == "|")) goto @172;
  p2 = p2.substr(2);
  r5 = p2.split("|");
  r6 = r5[2];
  r7 = r5[1];
  r8 = r5[0];
  r9 = r5[3];
  if (((r5[4].length > 0) && !(r5[4] == ""))) goto @906;
  r12 = r9.split("!");
  if ((r0 === "F")) goto @842;
  if ((r0 === "T")) goto @1783;
  if ((r0 === "#")) goto @1458;
  if ((r0 === "%")) goto @2204;
  if ((r0 === "$")) goto @2521;
  if ((r0 === "!")) goto @2125;
  if ((r0 === "?")) goto @2704;
  if ((r0 === ":")) goto @2646;
  if ((r0 === "^")) goto @3023;
  …
  ```
- `cS` → `onSmiley(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = Number(r3[1]);
  ```
- `cs` → `onServerMessage(p1)`
  ```
  this.api.kernel.showMessage(undefined, p1, "INFO_CHAT");
  ```

### Game : Carte, déplacements et combat

- `GC` → `onCreate(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = Number(r4[0]);
  ```
- `GDC` → `onCellData(p1)`
  ```
  r3 = p1.split("|");
  r5 = r3[r4].split(";");
  r6 = Number(r5[0]);
  r7 = r5[1].substring(0, 10);
  r8 = r5[1].substr(10);
  if ((r5[2] == "0")) goto @110;
  ```
- `GDE` → `onFrameObjectExternal(p1)`
  ```
  r3 = p1.split("|");
  r5 = r3[r4].split(";");
  r6 = Number(r5[0]);
  r7 = Number(r5[1]);
  ```
- `GDF` → `onFrameObject2(p1)`
  ```
  r3 = p1.split("|");
  r5 = r3[r4].split(";");
  r6 = Number(r5[0]);
  r7 = r5[1];
  r8 = !(r5[2] == undefined);
  if ((r5[2] == "1")) goto @213;
  ```
- `GDK` → `onMapLoaded()`
- `GDM` → `onMapData(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1];
  r6 = r3[2];
  if (!(Number(r4) == this.api.datacenter.Map.id)) goto @110;
  this.nLastMapIdReceived = _global.parseInt(r4, 10);
  ```
- `GDO` → `onCellObject(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r6 = r4[r5].split(";");
  r7 = Number(r6[0]);
  r8 = _global.parseInt(r6[1]);
  r10 = Number(r6[2]);
  r9.durability = Number(r6[3]);
  r9.durabilityMax = Number(r6[4]);
  ```
- `GDZ` → `onZoneData(p1)`
  ```
  r3 = p1.split("|");
  if ((r5.charAt(0) == "+")) goto @155;
  r7 = r5.substr(1).split(";");
  r8 = Number(r7[0]);
  r9 = Number(r7[1]);
  r10 = r7[2];
  ```
- `GE` → `onEnd(p1)`
  ```
  r5 = p1.split("|");
  if (!!_global.isNaN(Number(r5[0]))) goto @541;
  r4.duration = Number(r5[0]);
  r8 = Number(r5[1]);
  r9 = Number(r5[2]);
  r7 = r5[0].split(";");
  r4.duration = Number(r7[0]);
  r6 = Number(r7[1]);
  ```
- `GIC` → `onPlayersCoordinates(p1)`
  ```
  if (!!(p1 == "e")) goto @207;
  r3 = p1.split("|");
  r5 = r3[r4].split(";");
  r6 = r5[0];
  r7 = Number(r5[1]);
  ```
- `GIE` → `onEffect(p1)`
  ```
  r3 = p1.split(";");
  r4 = r3[0];
  r5 = r3[1].split(",");
  r6 = r3[2];
  r7 = r3[3];
  r8 = r3[4];
  r9 = r3[5];
  r10 = Number(r3[6]);
  r11 = r3[7];
  r12 = r3[8];
  r15 = new dofus.datacenter["\x0f\r"](r12, Number(r4), Number(r6), Number(r7), Number(r8), r9, Number(r10), Number(r11));
  ```
- `GIP` → `onPVP(p1, p2)`
  ```
  r4 = Number(p1);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ASK_DISABLE_PVP", [r4]), "CAUTION_YESNO", {"name": "DisabledPVP", "listener": this});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ASK_ENABLED_PVP"), "CAUTION_YESNO", {"name": "EnabledPVP", "listener": this});
  ```
- `GIe` → `onClearAllEffect(p1)`
- `GJ` → `onJoin(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  if ((r3[1] == "0")) goto @66;
  if ((r3[2] == "0")) goto @108;
  if ((r3[3] == "0")) goto @382;
  r8 = Number(r3[4]);
  r9 = Number(r3[5]);
  ```
- `GM` → `onMovement(p1, p2)`
  ```
  r4 = p1.split("|");
  r9 = r6.charAt(0);
  if (!(r9 == "+")) goto @227;
  r10 = r6.substr(1).split(";");
  r11 = r10[0];
  if (!(r11 == "-1")) goto @184;
  r12 = r10[1];
  r13 = Number(r10[2]);
  r14 = r10[3];
  r15 = r10[4];
  r16 = r10[5];
  r17 = r10[6];
  if (!(r17.charAt((r17.length - 1)) == "*")) goto @98;
  r17 = r17.substr(0, (r17.length - 1));
  if (!(r17.charAt(0) == "*")) goto @1339;
  r17 = r17.substr(1);
  r20 = r17.split("^");
  r22 = r16.split(",");
  r23 = r22[0];
  r24 = r22[1];
  r26 = r24.split("*");
  r25 = new dofus.datacenter["\x1e\x0b\x01"](_global.parseInt(r26[0]), r26[1]);
  r29 = r20[1];
  if (!_global.isNaN(Number(r29))) goto @735;
  r30 = r29.split("x");
  if ((r0 === "-1")) goto @1609;
  if ((r0 === "-2")) goto @1609;
  if ((r0 === "-3")) goto @2413;
  …
  ```
- `GO` → `onGameOver()`
- `GP` → `onPositionStart(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1];
  r6 = Number(r3[2]);
  this.api.datacenter.Basics.aks_team1_starts = new Array();
  this.api.datacenter.Basics.aks_team2_starts = new Array();
  r8 = (ank.util.decode64(r4.charAt(r7)) << 6);
  r8 = (r8 + ank.util.decode64(r4.charAt((r7 + 1))));
  this.api.datacenter.Basics.aks_team1_starts.push(r8);
  r10 = (ank.util.decode64(r5.charAt(r9)) << 6);
  r10 = (r10 + ank.util.decode64(r5.charAt((r9 + 1))));
  this.api.datacenter.Basics.aks_team2_starts.push(r10);
  ```
- `GR` → `onReady(p1)`
  ```
  r3 = (p1.charAt(0) == "1");
  r4 = p1.substr(1);
  ```
- `GS` → `onStartToPlay()`
- `GTF` → `onTurnFinish(p1)`
- `GTL` → `onTurnlist(p1)`
  ```
  r3 = p1.split("|");
  ```
- `GTM` → `onTurnMiddle(p1)`
  ```
  r3 = p1.split("|");
  r6 = r3[r5].split(";");
  r7 = r6[0];
  if ((r6[1] == "1")) goto @527;
  r9 = Number(r6[2]);
  r10 = Number(r6[3]);
  r11 = Number(r6[4]);
  r12 = Number(r6[5]);
  r13 = Number(r6[6]);
  r14 = Number(r6[7]);
  ```
- `GTR` → `onTurnReady(p1)`
- `GTS` → `onTurnStart(p1)`
  ```
  r4 = p1.split("|");
  r5 = r4[0];
  r6 = (Number(r4[1]) / 1000);
  r7 = Number(r4[2]);
  this.api.electron.makeNotification(this.api.lang.getText("PLAYER_TURN", [this.api.datacenter.Player.Name]));
  r9 = new Array();
  r9[1] = r8.color1;
  r9[2] = r8.color2;
  r9[3] = r8.color3;
  ```
- `GV` → `onLeave()`
- `GX` → `onExtraClip(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1].split(";");
  r7 = (r4 == "-");
  ```
- `Gc` → `onChallenge(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = r4.shift().split(";");
  r6 = Number(r5[0]);
  r7 = Number(r5[1]);
  r11 = r4[r10].split(";");
  r12 = r11[0];
  r13 = Number(r11[1]);
  r14 = Number(r11[2]);
  r15 = Number(r11[3]);
  ```
- `Gd` → `onFightChallenge(p1)`
  ```
  r3 = p1.split(";");
  r4 = new dofus.datacenter["\x0e\x0f"](_global.parseInt(r3[0]), (r3[1] == "1"), _global.parseInt(r3[2]), _global.parseInt(r3[3]), _global.parseInt(r3[4]), _global.parseInt(r3[5]), _global.parseInt(r3[6]));
  ```
- `Gd` → `onFightChallengeUpdate(p1, p2)`
  ```
  r4 = _global.parseInt(p1);
  r5 = this.api.lang.getText("FIGHT_CHALLENGE_FAILED");
  this.api.kernel.showMessage(undefined, r5, "INFO_CHAT");
  ```
- `Gf` → `onFlag(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = Number(r3[1]);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PLAYER_SET_FLAG", [r6.name, r5]), "INFO_CHAT");
  ```
- `Go` → `onFightOption(p1)`
  ```
  r3 = p1.substr(2);
  r5 = (p1.charAt(0) == "+");
  r6 = p1.charAt(1);
  if ((r0 === "H")) goto @343;
  if ((r0 === "S")) goto @91;
  if ((r0 === "A")) goto @449;
  if ((r0 === "P")) goto @283;
  ```
- `Gt` → `onTeam(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3.shift());
  r7 = r3[r6].split(";");
  r8 = (r7[0].charAt(0) == "+");
  r9 = r7[0].substr(1);
  r10 = r7[1];
  r11 = r7[2];
  r12 = r10.split(",");
  r13 = Number(r10);
  ```

### GameActions : Actions de jeu (GA)

- `GA` → `onActions(p1)`
  ```
  r4 = Number(p1.substring(0, r3));
  if (!(p1 == ";0")) goto @283;
  p1 = p1.substring((r3 + 1));
  r5 = Number(p1.substring(0, r3));
  p1 = p1.substring((r3 + 1));
  r6 = p1.substring(0, r3);
  r7 = p1.substring((r3 + 1));
  r238 = p1.split(",");
  r239 = r238[0];
  r240 = r238[0];
  r241 = r238[2];
  r242 = r238[3];
  r243 = r238[4];
  r244 = r238[6];
  r245 = r238[7];
  r246 = new dofus.datacenter["\x0f\r"](undefined, Number(r240), Number(r241), Number(r242), Number(r243), "", Number(r244), Number(r245));
  r232 = r7.split(",");
  r233 = r232[0];
  r235 = Number(r232[1]);
  if ((Number(r232[2]) == 1)) goto @29166;
  r237 = this.api.lang.getText("EXIT_STATE", [r234.name, this.api.lang.getStateText(r235)]);
  r11.addAction(115, false, this.api.kernel, this.api.kernel.showMessage, [undefined, r237, "INFO_FIGHT_CHAT"]);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("A_ATTACK_B", [r230.name, r231.name]), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("A_ATTACK_B", [this.api.kernel.ChatManager.getLinkName(r227), this.api.kernel.ChatManager.getLinkName(r228)]), "INFO_CHAT");
  this.api.electron.makeNotification(this.api.lang.getText("A_ATTACK_B", [r227, r228]));
  if ((r0 === "c")) goto @26504;
  if ((r0 === "t")) goto @26029;
  if ((r0 === "a")) goto @26541;
  …
  ```
- `GAF` → `onActionsFinish(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1];
  ```
- `GAS` → `onActionsStart(p1)`

### Items : Objets et inventaire

- `OA` → `onAdd(p1, p2)`
  ```
  if ((r0 === "F")) goto @675;
  if ((r0 === "L")) goto @457;
  if ((r0 === "A")) goto @78;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ALREADY_EQUIPED"), "ERROR_BOX", {"name": "Already"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TOO_LOW_LEVEL_FOR_ITEM"), "ERROR_BOX", {"name": "LowLevel"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("INVENTORY_FULL"), "ERROR_BOX", {"name": "Full"});
  r4 = p2.split("*");
  r7 = r6.charAt(0);
  r6 = r6.substr(1);
  if ((r0 === "G")) goto @204;
  if ((r0 === "O")) goto @209;
  r8 = r6.split(";");
  ```
- `OC` → `onChange(p1)`
  ```
  r3 = p1.split("*");
  r6 = r5.split(";");
  ```
- `OD` → `onDrop(p1, p2)`
  ```
  if ((r0 === "F")) goto @50;
  if ((r0 === "E")) goto @76;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_DROP_ITEM"), "ERROR_BOX");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("DROP_FULL"), "ERROR_BOX", {"name": "DropFull"});
  ```
- `OF` → `onItemFound(p1)`
  ```
  r3 = p1.split("|");
  if (_global.isNaN(Number(r3[0]))) goto @409;
  r4 = Number(r3[0]);
  if (_global.isNaN(Number(r3[2]))) goto @200;
  r5 = Number(r3[2]);
  r6 = r3[1].split("~");
  if (_global.isNaN(Number(r6[0]))) goto @117;
  r7 = Number(r6[0]);
  if (_global.isNaN(Number(r6[1]))) goto @473;
  r8 = Number(r6[1]);
  ```
- `OK` → `onItemUseCondition(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "G")) goto @671;
  if ((r0 === "U")) goto @112;
  r10 = p1.substr(1).split("|");
  if (_global.isNaN(Number(r10[0]))) goto @442;
  r11 = Number(r10[0]);
  if (_global.isNaN(Number(r10[1]))) goto @307;
  r12 = Number(r10[1]);
  if (_global.isNaN(Number(r10[2]))) goto @943;
  r13 = Number(r10[2]);
  if (_global.isNaN(Number(r10[3]))) goto @853;
  r14 = Number(r10[3]);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ITEM_USE_CONFIRMATION", [r16.name]), "CAUTION_YESNO", r15);
  r4 = p1.substr(1).split("|");
  if (_global.isNaN(Number(r4[0]))) goto @564;
  r5 = Number(r4[0]);
  if (_global.isNaN(Number(r4[1]))) goto @514;
  r6 = Number(r4[1]);
  if (_global.isNaN(Number(r4[2]))) goto @392;
  r7 = Number(r4[2]);
  if (_global.isNaN(Number(r4[3]))) goto @652;
  r8 = Number(r4[3]);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ITEM_USE_CONDITION_GOLD", [r8]), "CAUTION_YESNO", r9);
  ```
- `OM` → `onMovement(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  if (_global.isNaN(Number(r3[1]))) goto @101;
  r5 = Number(r3[1]);
  ```
- `OQ` → `onQuantity(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```
- `OR` → `onRemove(p1)`
  ```
  r3 = Number(p1);
  ```
- `OS` → `onItemSet(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = Number(r4[0]);
  r6 = String(r4[1]).split(";");
  r7 = r4[2];
  ```
- `OT` → `onTool(p1)`
  ```
  r3 = Number(p1);
  ```
- `Oa` → `onAccessories(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1].split(",");
  r6 = new Array();
  r11 = r5[r7].split("~");
  r8 = _global.parseInt(r11[0], 16);
  r10 = _global.parseInt(r11[1]);
  r9 = (_global.parseInt(r11[2]) - 1);
  r8 = _global.parseInt(r5[r7], 16);
  ```
- `Ow` → `onWeight(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```

### Spells : Sorts

- `SB` → `onSpellBoost(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = Number(r3[2]);
  ```
- `SF` → `onSpellForget(p1)`
  ```
  if (!(p1 == "+")) goto @75;
  if (!(p1 == "-")) goto @117;
  ```
- `SL` → `onList(p1)`
  ```
  r3 = p1.split(";");
  r5 = new Array();
  r5.push(r8);
  ```
- `SLo` → `onChangeOption(p1)`
  ```
  this.api.datacenter.Basics.canUseSeeAllSpell = (p1.charAt(0) == "+");
  ```
- `SU` → `onUpgradeSpell(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_BOOST_SPELL"), "ERROR_BOX");
  ```

### Job : Métiers

- `JN` → `onLevel(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("NEW_JOB_LEVEL", [this.api.lang.getJobText(r4).n, r5]), "ERROR_BOX", {"name": "NewJobLevel"});
  ```
- `JO` → `onOptions` (corps non retrouvé)
- `JR` → `onRemove(p1)`
  ```
  r3 = Number(p1);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("REMOVE_JOB", [r5.item.name]), "INFO_CHAT");
  ```
- `JS` → `onSkills(p1)`
  ```
  r3 = p1.split("|");
  r6 = r3[r5].split(";");
  r7 = Number(r6[0]);
  r9 = r6[1].split(",");
  r11 = r9[r10].split("~");
  r8.push(new dofus.datacenter["\x1e\x10\x1d"](r11[0], r11[1], r11[2], r11[3], r11[4]));
  r4.push(r12);
  ```
- `JX` → `onXP(p1)`
  ```
  r3 = p1.split("|");
  r6 = r3[r5].split(";");
  r7 = Number(r6[0]);
  r8 = Number(r6[1]);
  r9 = Number(r6[2]);
  r10 = Number(r6[3]);
  r11 = Number(r6[4]);
  ```

### Dialog : Dialogues PNJ

- `DA` → `onCustomAction(p1)`
  ```
  if ((r0 === "1")) goto @25;
  ```
- `DC` → `onCreate(p1, p2)`
  ```
  r4 = Number(p2);
  r6 = new Array();
  r6[1] = r5.color1;
  r6[2] = r5.color2;
  r6[3] = r5.color3;
  ```
- `DP` → `onPause()`
- `DQ` → `onQuestion(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0].split(";");
  r5 = Number(r4[0]);
  r6 = r4[1].split(",");
  r7 = r3[1].split(";");
  ```
- `DV` → `onLeave()`

### Exchange : Échanges, boutique, HDV, artisanat

- `EA` → `onCraftLoop(p1)`
  ```
  r3 = Number(p1);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CRAFT_LOOP_PROCESS", [((this._nItemsToCraft - r3) + 1), (this._nItemsToCraft + 1)]), "INFO_CHAT");
  ```
- `EB` → `onBuy(p1)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("BUY_DONE"), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("EXCHANGE"), this.api.lang.getText("CANT_BUY"), "ERROR_BOX", {"name": "Buy"});
  ```
- `EC` → `onCreate(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = Number(r4[0]);
  r6 = r4[1];
  r4 = r6.split("~");
  r23 = r4[0].split(";");
  r24 = r4[1].split(";");
  if (!!(r23[r25] == "")) goto @3967;
  r21.push(this.api.network.Mount.createMount(r23[r25]));
  if (!!(r24[r26] == "")) goto @4074;
  r22.push(this.api.network.Mount.createMount(r24[r26]));
  r18 = r6.split(";");
  r20 = Number(r18[r19]);
  r17.push({"label": this.api.lang.getJobText(r20).n, "id": r20});
  r4 = r6.split(";");
  r15 = Number(r4[0]);
  r16 = Number(r4[1]);
  r4 = r6.split(";");
  r14 = r4[0].split(",");
  r7.Shop.quantity1 = Number(r14[0]);
  r7.Shop.quantity2 = Number(r14[1]);
  r7.Shop.quantity3 = Number(r14[2]);
  r7.Shop.types = r4[1].split(",");
  r7.Shop.tax = Number(r4[2]);
  r7.Shop.maxLevel = Number(r4[3]);
  r7.Shop.maxItemCount = Number(r4[4]);
  r7.Shop.npcID = Number(r4[5]);
  r7.Shop.maxSellTime = Number(r4[6]);
  r4 = r6.split(";");
  …
  ```
- `EHL` → `onBigStoreTypeItemsList(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1].split(";");
  if (!!(r3[1].length == 0)) goto @96;
  r8 = Number(r5[r7]);
  r6.push(r9);
  ```
- `EHM` → `onBigStoreTypeItemsMovement(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = Number(p1.substr(1));
  r5.inventory.push(r7);
  ```
- `EHP` → `onItemMiddlePriceInBigStore(p1)`
  ```
  r3 = p1.split("|");
  ```
- `EHS` → `onSearch(p1)`
- `EHl` → `onBigStoreItemsList(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r6 = r3[k].split(";");
  r7 = Number(r6[0]);
  r8 = r6[1];
  r9 = Number(r6[2]);
  r10 = Number(r6[3]);
  r11 = Number(r6[4]);
  r5.push(r13);
  ```
- `EHm` → `onBigStoreItemsMovement(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = Number(r4[0]);
  r6 = Number(r4[1]);
  r7 = r4[2];
  r8 = Number(r4[3]);
  r9 = Number(r4[4]);
  r10 = Number(r4[5]);
  r11.inventory2.push(r14);
  ```
- `EJ` → `onCrafterListChanged(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split(";");
  r6 = Number(r4[0]);
  r7 = r4[1];
  r9 = r4[2];
  r10 = Number(r4[3]);
  r11 = Number(r4[4]);
  r12 = !!Number(r4[5]);
  r13 = Number(r4[6]);
  r14 = Number(r4[7]);
  r15 = r4[8].split(",");
  r16 = r4[9];
  r17 = r4[10].split(",");
  r18.job = new dofus.datacenter.Job(r6, new ank.util(), new dofus.datacenter["\x0c\x07"](Number(r17[0]), Number(r17[1])));
  r18.color1 = r15[0];
  r18.color2 = r15[1];
  r18.color3 = r15[2];
  r5.push(r18);
  ```
- `EK` → `onReady(p1)`
  ```
  r3 = (p1.charAt(0) == "1");
  r4 = Number(p1.substr(1));
  ```
- `EL` → `onList(p1)`
  ```
  r24 = p1.split("|");
  r26 = r24[k].split(";");
  r27 = Number(r26[0]);
  r28 = Number(r26[1]);
  r29 = Number(r26[2]);
  r30 = r26[3];
  r31 = Number(r26[4]);
  r32 = Number(r26[5]);
  r25.push(r33);
  r15 = p1.split("|");
  r17 = r15[k].split(";");
  r18 = Number(r17[0]);
  r19 = Number(r17[1]);
  r20 = Number(r17[2]);
  r21 = r17[3];
  r22 = Number(r17[4]);
  r16.push(r23);
  r9 = p1.split(";");
  r12 = r11.charAt(0);
  r13 = r11.substr(1);
  if ((r0 === "O")) goto @144;
  if ((r0 === "G")) goto @1318;
  r10.push(r14);
  r3 = p1.split("|");
  r5 = r3[k].split(";");
  r6 = Number(r5[0]);
  r7 = r5[1];
  r4.push(r8);
  …
  ```
- `EM` → `onLocalMovement(p1, p2)`
- `ER` → `onRequest(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = r4[0];
  r6 = r4[1];
  r7 = Number(r4[2]);
  this.api.kernel.showMessage(this.api.lang.getText("EXCHANGE"), this.api.lang.getText(r11, [r10.name]), "INFO_CANCEL", {"name": "Exchange", "listener": this});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CHAT_A_WANT_EXCHANGE", [this.api.kernel.ChatManager.getLinkName(r12.name)]), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("EXCHANGE"), this.api.lang.getText(r13, [r12.name]), "CAUTION_YESNOIGNORE", {"name": "Exchange", "player": r12.name, "listener": this, "params": {"player": r12.name}});
  r14 = p2.charAt(0);
  if ((r0 === "O")) goto @1727;
  if ((r0 === "T")) goto @1424;
  if ((r0 === "J")) goto @1549;
  if ((r0 === "o")) goto @1689;
  if ((r0 === "S")) goto @1627;
  if ((r0 === "I")) goto @1788;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_EXCHANGE"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ERROR_62"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ERROR_70"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ERROR_85"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NOT_NEAR_CRAFT_TABLE"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ALREADY_EXCHANGE"), "ERROR_CHAT");
  ```
- `ES` → `onSell(p1)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("SELL_DONE"), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("EXCHANGE"), this.api.lang.getText("CANT_SELL"), "ERROR_BOX", {"name": "Sell"});
  ```
- `EV` → `onLeave(p1, p2)`
  ```
  if (!(p2 == "a")) goto @235;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("EXCHANGE_OK"), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("EXCHANGE_CANCEL"), "INFO_CHAT");
  ```
- `EW` → `onCraftPublicMode(p1)`
  ```
  if ((r3 == "+")) goto @302;
  r4 = p1.charAt(0);
  r5 = p1.substr(1).split("|");
  r6 = r5[0];
  if (!((r4 == "+") && (r5[1].length > 0))) goto @349;
  r8 = r5[1].split(";");
  ```
- `Ea` → `onCraftLoopEnd(p1)`
  ```
  r3 = Number(p1);
  this.api.kernel.showMessage(undefined, r4, "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("CRAFT"), r4, "ERROR_BOX");
  r4 = this.api.lang.getText("CRAFT_LOOP_END_INVALID");
  r4 = this.api.lang.getText("CRAFT_LOOP_END_FAIL");
  r4 = this.api.lang.getText("CRAFT_LOOP_END_INTERRUPT");
  this.api.electron.makeNotification(this.api.lang.getText("CRAFT_LOOP_END_OK"));
  r4 = this.api.lang.getText("CRAFT_LOOP_END_OK");
  ```
- `Ec` → `onCraft(p1, p2)`
  ```
  r0 = p2.substr(0, 1);
  if ((r0 === "I")) goto @84;
  if ((r0 === "F")) goto @684;
  if ((r0 === ";")) goto @1102;
  r7 = p2.substr(1).split(";");
  r8 = new dofus.datacenter["\x0c\x0c"](0, Number(r7[0]), undefined, undefined, undefined);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CRAFT_SUCCESS_SELF", [r8.name]), "INFO_CHAT");
  r9 = r7[1].substr(0, 1);
  r10 = r7[1].substr(1);
  r11 = Number(r7[0]);
  r12 = r7[2];
  r13 = new Array();
  r13.push(r11);
  r13.push(r12);
  if ((r0 === "T")) goto @1351;
  if ((r0 === "B")) goto @1491;
  this.api.kernel.showMessage(undefined, this.api.kernel.ChatManager.parseInlineItems(this.api.lang.getText("CRAFT_SUCCESS_OTHER", [r10]), r13), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.kernel.ChatManager.parseInlineItems(this.api.lang.getText("CRAFT_SUCCESS_TARGET", [r10]), r13), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("CRAFT"), this.api.lang.getText("CRAFT_FAILED"), "ERROR_BOX", {"name": "CraftFailed"});
  this.api.kernel.showMessage(this.api.lang.getText("CRAFT"), this.api.lang.getText("NO_CRAFT_RESULT"), "ERROR_BOX", {"name": "Impossible"});
  ```
- `Ee` → `onMountStorage(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "~")) goto @291;
  if ((r0 === "+")) goto @301;
  if ((r0 === "-")) goto @359;
  if ((r0 === "E")) goto @416;
  r5 = Number(p1.substr(1));
  r6.removeItems(Number(a), 1);
  ```
- `Ef` → `onMountPark(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "+")) goto @309;
  if ((r0 === "-")) goto @226;
  if ((r0 === "E")) goto @385;
  r4 = Number(p1.substr(1));
  r5.removeItems(Number(a), 1);
  ```
- `Ei` → `onPlayerShopMovement(p1, p2)`
  ```
  r4 = (p2.charAt(0) == "+");
  r5 = p2.substr(1).split("|");
  r6 = Number(r5[0]);
  r7 = Number(r5[1]);
  r8 = Number(r5[2]);
  r9 = r5[3];
  r10 = Number(r5[4]);
  r11.inventory.push(r13);
  ```
- `Ej` → `onCrafterReference(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = Number(p1.substr(1));
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CRAFTER_REFERENCE_REMOVE", [this.api.lang.getJobText(r4).n]), "INFO_CHAT");
  ```
- `Em` → `onDistantMovement(p1, p2)`
  ```
  r4 = (p2.charAt(0) == "+");
  r5 = p2.substr(1).split("|");
  r6 = Number(r5[0]);
  r7 = Number(r5[1]);
  r8 = Number(r5[2]);
  r9 = r5[3];
  r10 = Number(r5[4]);
  r11 = Number(r5[5]);
  r12.inventory.push(r14);
  ```
- `Ep` → `onPayMovement(p1, p2)`
  ```
  r4 = Number(p2.charAt(0));
  this.modifyLocal(p2.substr(2), r5, r6);
  this.modifyDistant(p2.substr(2), r5, r6, false);
  ```
- `Eq` → `onAskOfflineExchange(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = (Number(r3[1]) / 10);
  r6 = Number(r3[2]);
  ```
- `Er` → `onCoopMovement(p1, p2)`
- `Es` → `onStorageMovement(p1, p2)`
  ```
  r4 = p2.charAt(0);
  if ((r0 === "O")) goto @618;
  if ((r0 === "G")) goto @218;
  r15 = Number(p2.substr(1));
  r7 = (p2.charAt(1) == "+");
  r8 = p2.substr(2).split("|");
  r9 = Number(r8[0]);
  r10 = Number(r8[1]);
  r11 = Number(r8[2]);
  r12 = r8[3];
  r6.push(r14);
  ```
- `Ew` → `onMountPods(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```

### Party : Groupe

- `PA` → `onAccept(p1)`
- `PC` → `onCreate(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("U_ARE_IN_GROUP", [r4]), "INFO_CHAT");
  r5 = p2.charAt(0);
  if ((r0 === "a")) goto @215;
  if ((r0 === "f")) goto @87;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PARTY_FULL"), "ERROR_CHAT", {"name": "PartyError"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PARTY_ALREADY_IN_GROUP"), "ERROR_CHAT", {"name": "PartyError"});
  ```
- `PF` → `onFollow(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PARTY_NOT_IN_IN_GROUP"), "ERROR_BOX", {"name": "PartyError"});
  ```
- `PI` → `onInvite(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = r4[0];
  r6 = r4[1];
  this.api.kernel.showMessage(this.api.lang.getText("PARTY"), this.api.lang.getText("YOU_INVITE_B_IN_PARTY", [r6]), "INFO_CANCEL", {"name": "Party", "listener": this});
  this.api.electron.makeNotification(this.api.lang.getText("A_INVITE_YOU_IN_PARTY", [r5]));
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CHAT_A_INVITE_YOU_IN_PARTY", [this.api.kernel.ChatManager.getLinkName(r5)]), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("PARTY"), this.api.lang.getText("A_INVITE_YOU_IN_PARTY", [r5]), "CAUTION_YESNOIGNORE", {"name": "Party", "player": r5, "listener": this, "params": {"player": r5}});
  r7 = p2.charAt(0);
  if ((r0 === "a")) goto @1153;
  if ((r0 === "f")) goto @803;
  if ((r0 === "n")) goto @960;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_FIND_ACCOUNT_OR_CHARACTER", [p2.substr(1)]), "ERROR_CHAT", {"name": "PartyError"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PARTY_FULL"), "ERROR_CHAT", {"name": "PartyError"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PARTY_ALREADY_IN_GROUP"), "ERROR_CHAT", {"name": "PartyError"});
  ```
- `PL` → `onLeader(p1)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NEW_GROUP_LEADER", [r5]), "INFO_CHAT");
  ```
- `PM` → `onMovement(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r5 = p1.substr(1).split("|");
  r7 = String(r5[r6]).split(";");
  r8 = r7[0];
  r0 = p1.charAt(0);
  if ((r0 === "+")) goto @677;
  if ((r0 === "-")) goto @1112;
  if ((r0 === "~")) goto @1135;
  r21 = r7[1];
  r22 = r7[2];
  r23 = Number(r7[3]);
  r24 = Number(r7[4]);
  r25 = Number(r7[5]);
  r26 = r7[6];
  r27 = r7[7];
  r28 = Number(r7[8]);
  r29 = Number(r7[9]);
  r30 = Number(r7[10]);
  r31 = Number(r7[11]);
  r9 = r7[1];
  r10 = r7[2];
  r11 = Number(r7[3]);
  r12 = Number(r7[4]);
  r13 = Number(r7[5]);
  r14 = r7[6];
  r15 = r7[7];
  r16 = Number(r7[8]);
  r17 = Number(r7[9]);
  …
  ```
- `PR` → `onRefuse(p1)`
- `PV` → `onLeave(p1)`
  ```
  this.api.kernel.GameManager.updateCompass(this.api.datacenter.Basics.banner_targetCoords[0], this.api.datacenter.Basics.banner_targetCoords[1]);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("A_KICK_FROM_PARTY", [r4]), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("LEAVE_GROUP"), "INFO_CHAT");
  ```

### Friends : Amis

- `FA` → `onAddFriend(p1, p2)`
  ```
  this.api.datacenter.Player.Friends.push(r4);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ADD_TO_FRIEND_LIST", [r4.name]), "INFO_CHAT");
  if ((r0 === "f")) goto @376;
  if ((r0 === "y")) goto @72;
  if ((r0 === "a")) goto @133;
  if ((r0 === "m")) goto @167;
  this.api.kernel.showMessage(this.api.lang.getText("FRIENDS"), this.api.lang.getText("FRIENDS_LIST_FULL"), "ERROR_BOX", {"name": "FriendsListFull"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ALREADY_YOUR_FRIEND"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_YOU"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_FRIEND_NOT_FOUND"), "ERROR_CHAT");
  ```
- `FD` → `onRemoveFriend(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("REMOVE_FRIEND_OK"), "INFO_CHAT");
  if ((r0 === "f")) goto @124;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_FRIEND_NOT_FOUND"), "ERROR_CHAT");
  ```
- `FL` → `onFriendsList(p1)`
  ```
  r3 = p1.split("|");
  this.api.datacenter.Player.Friends = new Array();
  this.api.datacenter.Player.Friends.push(r5);
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("YOUR_FRIEND_LIST")) + " :</b>"), "INFO_CHAT");
  if (!!(r7[r9].state == "DISCONNECT")) goto @768;
  r8 = (r8 + (((((((" (" + r7[r9].name) + ") ") + this.api.lang.getText("LEVEL")) + ":") + r7[r9].level) + ", ") + this.api.lang.getText(r7[r9].state)));
  this.api.kernel.showMessage(undefined, r8, "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("EMPTY_FRIEND_LIST"), "INFO_CHAT");
  ```
- `FO` → `onNotifyChange(p1)`
  ```
  this.api.datacenter.Basics.aks_notify_on_friend_connexion = (p1 == "+");
  r3.notifyStateChanged((p1 == "+"));
  ```
- `FS` → `onSpouse(p1)`
  ```
  r3 = p1.split("|");
  r4.name = r3[0];
  r4.gfx = r3[1];
  r4.color1 = Number(r3[2]);
  r4.color2 = Number(r3[3]);
  r4.color3 = Number(r3[4]);
  r4.mapID = Number(r3[5]);
  r4.level = Number(r3[6]);
  if ((r3[7] == "1")) goto @87;
  if ((r3[8] == "1")) goto @305;
  ```

### Enemies : Ennemis

- `iA` → `onAddEnemy(p1, p2)`
  ```
  this.api.datacenter.Player.Enemies.push(r4);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ADD_TO_ENEMY_LIST", [r4.name]), "INFO_CHAT");
  if ((r0 === "f")) goto @390;
  if ((r0 === "y")) goto @117;
  if ((r0 === "a")) goto @424;
  if ((r0 === "m")) goto @222;
  this.api.kernel.showMessage(this.api.lang.getText("ENEMIES"), this.api.lang.getText("ENEMIES_LIST_FULL"), "ERROR_BOX", {"name": "EnemiesListFull"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ALREADY_YOUR_ENEMY"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_YOU_AS_ENEMY"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_FRIEND_NOT_FOUND"), "ERROR_CHAT");
  ```
- `iD` → `onRemoveEnemy(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("REMOVE_ENEMY_OK"), "INFO_CHAT");
  if ((r0 === "f")) goto @73;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_ADD_FRIEND_NOT_FOUND"), "ERROR_CHAT");
  ```
- `iL` → `onEnemiesList(p1)`
  ```
  r3 = p1.split("|");
  this.api.datacenter.Player.Enemies = new Array();
  this.api.datacenter.Player.Enemies.push(r5);
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("YOUR_ENEMY_LIST")) + " :</b>"), "INFO_CHAT");
  if (!!(r7[r9].state == "DISCONNECT")) goto @792;
  r8 = (r8 + (((((((" (" + r7[r9].name) + ") ") + this.api.lang.getText("LEVEL")) + ":") + r7[r9].level) + ", ") + this.api.lang.getText(r7[r9].state)));
  this.api.kernel.showMessage(undefined, r8, "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("EMPTY_ENEMY_LIST"), "INFO_CHAT");
  ```

### Guild : Guilde

- `gA` → `onTaxCollectorAttacked(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0].charAt(0);
  r5 = this.api.lang.getFullNameText(r3[0].substr(1).split(","));
  r6 = Number(r3[1]);
  r7 = r3[2];
  r8 = r3[3];
  if ((r0 === "A")) goto @308;
  if ((r0 === "S")) goto @651;
  if ((r0 === "D")) goto @440;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TAX_ATTACKED_DIED", [r5, r9]), "GUILD_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TAX_ATTACKED_SUVIVED", [r5, r9]), "GUILD_CHAT");
  this.api.electron.makeNotification(this.api.lang.getText("TAX_ATTACKED", [r5, r9]));
  this.api.kernel.showMessage(undefined, (("<img src=\"CautionIcon\" hspace='0' vspace='0' width='13' height='13' /><a href='asfunction:onHref,OpenGuildTaxCollectors'>" + this.api.lang.getText("TAX_ATTACKED", [r5, r9])) + "</a>"), "GUILD_CHAT");
  ```
- `gC` → `onCreate(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_CREATED"), "INFO_CHAT");
  if ((r0 === "an")) goto @133;
  if ((r0 === "ae")) goto @273;
  if ((r0 === "a")) goto @67;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_CREATE_ALLREADY_IN_GUILD"), "ERROR_BOX");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_CREATE_ALLREADY_USE_EMBLEM"), "ERROR_BOX");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_CREATE_ALLREADY_USE_NAME"), "ERROR_BOX");
  ```
- `gH` → `onHireTaxCollector(p1, p2)`
  ```
  r4 = p2.charAt(0);
  if ((r0 === "d")) goto @92;
  if ((r0 === "a")) goto @416;
  if ((r0 === "k")) goto @627;
  if ((r0 === "m")) goto @220;
  if ((r0 === "b")) goto @152;
  if ((r0 === "y")) goto @293;
  if ((r0 === "h")) goto @469;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_HIRE_TAXCOLLECTORS_HERE"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_HIRE_TAXCOLLECTORS_TOO_TIRED"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NOT_YOUR_TAXCOLLECTORS"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_HIRE_MAX_TAXCOLLECTORS"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NOT_ENOUGTH_RICH_TO_HIRE_TAX"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ALREADY_TAXCOLLECTOR_ON_MAP"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NOT_ENOUGHT_RIGHTS_FROM_GUILD"), "ERROR_CHAT");
  ```
- `gIB` → `onInfosBoosts(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = Number(r3[2]);
  r7 = Number(r3[3]);
  r8 = Number(r3[4]);
  r9 = Number(r3[5]);
  r10 = Number(r3[6]);
  r11 = Number(r3[7]);
  r12 = Number(r3[8]);
  r13 = Number(r3[9]);
  r3[r14] = r3[r14].split(";");
  r17 = Number(r3[r16][0]);
  r18 = Number(r3[r16][1]);
  r15.push(new dofus.datacenter["\x1e\x0e\x1c"](r17, r18));
  ```
- `gIF` → `onInfosMountPark(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r7 = r3[r6].split(";");
  r8 = Number(r7[0]);
  r9 = Number(r7[1]);
  r10 = Number(r7[2]);
  if (!!(r7[3] == "")) goto @142;
  r12 = r7[3].split(",");
  r14 = new dofus.datacenter.Mount(Number(r12[r13]));
  if ((r12[(r13 + 1)] == "")) goto @326;
  r11.mounts.push(r14);
  r5.push(r11);
  ```
- `gIG` → `onInfosGeneral(p1)`
  ```
  r3 = p1.split("|");
  r4 = (r3[0] == "1");
  r5 = Number(r3[1]);
  r6 = Number(r3[2]);
  r7 = Number(r3[3]);
  r8 = Number(r3[4]);
  ```
- `gIH` → `onInfosHouses(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r7 = r4[r6].split(";");
  r8 = Number(r7[0]);
  r9 = r7[1];
  r10 = r7[2].split(",");
  r11 = new com.ankamagames.types["\x1e\x16\r"](Number(r10[0]), Number(r10[1]));
  r12 = new Array();
  r13 = r7[3].split(",");
  r12.push(Number(r13[r14]));
  r15 = r7[4];
  r5.push(r16);
  ```
- `gIM` → `onInfosMembers(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r7 = r4[r6].split(";");
  r8.id = Number(r7[0]);
  r8.name = r7[1];
  r8.level = Number(r7[2]);
  r8.gfx = Number(r7[3]);
  r8.rank = Number(r7[4]);
  r8.winxp = Number(r7[5]);
  r8.percentxp = Number(r7[6]);
  r8.rights = new dofus.datacenter["\r\x0b"](Number(r7[7]));
  r8.state = Number(r7[8]);
  r8.alignement = Number(r7[9]);
  r8.lastConnection = Number(r7[10]);
  r5.members.push(r8);
  r5.members.push(r8);
  ```
- `gIT` → `onInfosTaxCollectorsAttackers(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = _global.parseInt(r4[0], 36);
  r10 = r4[r9].split(";");
  r11.id = _global.parseInt(r10[0], 36);
  r11.name = r10[1];
  r11.level = Number(r10[2]);
  r8.attackers.push(r11);
  r13 = _global.parseInt(r10[0], 36);
  ```
- `gIT` → `onInfosTaxCollectorsMovement(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r7 = r4[r6].split(";");
  r8.id = _global.parseInt(r7[0], 36);
  r10 = _global.parseInt(r7[2], 36);
  r8.name = this.api.lang.getFullNameText(r7[1].split(","));
  r8.state = Number(r7[3]);
  r8.timer = Number(r7[4]);
  r8.maxTimer = Number(r7[5]);
  r8.maxPlayerCount = Number(r7[6]);
  r13 = r7[1].split(",");
  if ((r13[2] == "")) goto @1149;
  r8.callerName = r13[2];
  r8.startDate = _global.parseInt(r13[3], 10);
  if ((r13[4] == "")) goto @701;
  r8.lastHarvesterName = r13[4];
  r8.lastHarvestDate = _global.parseInt(r13[5], 10);
  r8.nextHarvestDate = _global.parseInt(r13[6], 10);
  r5.taxCollectors.push(r8);
  r5.taxCollectors.push(r8);
  ```
- `gIT` → `onInfosTaxCollectorsPlayers(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = _global.parseInt(r4[0], 36);
  r10 = r4[r9].split(";");
  r11.id = _global.parseInt(r10[0], 36);
  r11.name = r10[1];
  r11.gfxFile = ((dofus.Constants.CLIPS_PERSOS_PATH + r10[2]) + ".swf");
  r11.level = Number(r10[3]);
  r11.color1 = _global.parseInt(r10[4], 36);
  r11.color2 = _global.parseInt(r10[5], 36);
  r11.color3 = _global.parseInt(r10[6], 36);
  r8.players.push(r11);
  r13 = _global.parseInt(r10[0], 36);
  ```
- `gJC` → `onJoinDistantOk()`
- `gJE` → `onJoinError(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "a")) goto @342;
  if ((r0 === "d")) goto @128;
  if ((r0 === "u")) goto @486;
  if ((r0 === "o")) goto @282;
  if ((r0 === "r")) goto @207;
  if ((r0 === "c")) goto @429;
  r4 = p1.substr(1);
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_JOIN_REFUSED", [r4]), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_JOIN_OCCUPED"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_JOIN_UNKNOW"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_JOIN_NO_RIGHTS"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("GUILD_JOIN_ALREADY_IN_GUILD"), "ERROR_CHAT");
  ```
- `gJK` → `onJoinOk(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "a")) goto @140;
  if ((r0 === "j")) goto @118;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("YOUR_R_NEW_IN_GUILD", [this.api.datacenter.Player.guildInfos.name]), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("A_JOIN_YOUR_GUILD", [p1.substr(1)]), "INFO_CHAT");
  ```
- `gJR` → `onRequestLocal(p1)`
  ```
  this.api.kernel.showMessage(this.api.lang.getText("GUILD"), this.api.lang.getText("YOU_INVIT_B_IN_GUILD", [p1]), "INFO_CANCEL", {"name": "Guild", "listener": this, "params": {"spriteID": this.api.datacenter.Player.ID}});
  ```
- `gJr` → `onRequestDistant(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1];
  r6 = r3[2];
  this.refuseInvitation(Number(r4));
  this.api.electron.makeNotification(this.api.lang.getText("A_INVIT_YOU_IN_GUILD", [r5, r6]));
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CHAT_A_INVIT_YOU_IN_GUILD", [this.api.kernel.ChatManager.getLinkName(r5), r6]), "INFO_CHAT");
  this.api.kernel.showMessage(this.api.lang.getText("GUILD"), this.api.lang.getText("A_INVIT_YOU_IN_GUILD", [r5, r6]), "CAUTION_YESNOIGNORE", {"name": "Guild", "player": r5, "listener": this, "params": {"spriteID": r4, "player": r5}});
  ```
- `gK` → `onBann(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = r4[0];
  r6 = r4[1];
  this.api.kernel.showMessage(undefined, this.api.lang.getText("YOU_BANN_A_FROM_GUILD", [r6]), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("YOU_BANN_YOU_FROM_GUILD"), "INFO_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("YOU_ARE_BANN_BY_A_FROM_GUILD", [r5]), "INFO_CHAT");
  r8 = p2.charAt(0);
  if ((r0 === "d")) goto @290;
  if ((r0 === "a")) goto @102;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_BANN_FROM_GUILD_NOT_MEMBER"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NOT_ENOUGHT_RIGHTS_FROM_GUILD"), "ERROR_CHAT");
  ```
- `gS` → `onStats(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = _global.parseInt(r3[1], 36);
  r6 = _global.parseInt(r3[2], 36);
  r7 = _global.parseInt(r3[3], 36);
  r8 = _global.parseInt(r3[4], 36);
  r9 = _global.parseInt(r3[5], 36);
  ```
- `gT` → `onTaxCollectorInfo(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0].charAt(0);
  r5 = this.api.lang.getFullNameText(r3[0].substr(1).split(","));
  r6 = Number(r3[1]);
  r7 = r3[2];
  r8 = r3[3];
  r10 = r3[4];
  if ((r0 === "S")) goto @87;
  if ((r0 === "R")) goto @523;
  if ((r0 === "G")) goto @309;
  r11 = r3[5].split(";");
  r12 = Number(r11[0]);
  r13 = ((r12 + " ") + this.api.lang.getText("EXPERIENCE_POINT"));
  r15 = r11[r14].split(",");
  r16 = r15[0];
  r17 = r15[1];
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TAXCOLLECTOR_RECOLTED", [r5, r9, r10, r13]), "GUILD_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TAXCOLLECTOR_REMOVED", [r5, r9, r10]), "GUILD_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("TAXCOLLECTOR_ADDED", [r5, r9, r10]), "GUILD_CHAT");
  ```
- `gU` → `onUserInterfaceOpen(p1)`
  ```
  if ((r0 === "T")) goto @40;
  if ((r0 === "F")) goto @354;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ITEM_NEED_GUILD"), "ERROR_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("ITEM_NEED_GUILD"), "ERROR_CHAT");
  ```
- `gV` → `onLeave()`
- `gn` → `onNew()`

### Mount : Montures

- `RD` → `onMountParkBuy(p1)`
  ```
  r3 = p1.split("|");
  ```
- `Rd` → `onData(p1)`
- `Re` → `onEquip(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "+")) goto @157;
  if ((r0 === "-")) goto @83;
  if ((r0 === "E")) goto @102;
  this.equipError(p1.charAt(1));
  this.api.datacenter.Player.mount = this.createMount(p1.substr(1));
  ```
- `Rn` → `onName(p1)`
- `Rp` → `onMountPark(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = Number(r3[2]);
  r7 = Number(r3[3]);
  r8 = r3[4];
  r9 = r3[5];
  ```
- `Rr` → `onRidingState(p1)`
  ```
  r3 = p1.charAt(0);
  if ((r0 === "+")) goto @73;
  if ((r0 === "-")) goto @106;
  ```
- `Rv` → `onLeave()`
- `Rx` → `onXP(p1)`
  ```
  r3 = Number(p1);
  ```

### Houses : Maisons

- `hB` → `onBuy(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = Number(r4[0]);
  r6 = Number(r4[1]);
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("HOUSE_BUY", [r7.name, r7.price]), "ERROR_BOX", {"name": "BuyHouse"});
  r0 = p2.charAt(0);
  if ((r0 === "C")) goto @255;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_BUY_HOUSE", [p2.substr(1)]), "ERROR_BOX", {"name": "BuyHouse"});
  ```
- `hC` → `onCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```
- `hG` → `onGuildInfos(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r6 = r3[1];
  r7 = this.api.kernel.CharactersManager.createGuildEmblem(r3[2]);
  r8 = Number(r3[3]);
  ```
- `hL` → `onList(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r6 = r4[r5].split(";");
  r7 = r6[0];
  r8 = (r6[1] == "1");
  r9 = (r6[2] == "1");
  r10 = (r4[3] == "1");
  ```
- `hP` → `onProperties(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1].split(";");
  r6 = r5[0];
  r7 = (r5[1] == "1");
  r8 = r5[2];
  r9 = this.api.kernel.CharactersManager.createGuildEmblem(r5[3]);
  ```
- `hS` → `onSell(p1, p2)`
  ```
  r4 = p2.split("|");
  r5 = Number(r4[0]);
  r6 = Number(r4[1]);
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("HOUSE_SELL", [r7.name, r7.price]), "ERROR_BOX", {"name": "SellHouse"});
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_SELL_HOUSE"), "ERROR_BOX", {"name": "SellHouse"});
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText("HOUSE_NOSELL", [r7.name]), "ERROR_BOX", {"name": "NoSellHouse"});
  ```
- `hV` → `onLeave()`
- `hX` → `onLockedProperty` (corps non retrouvé)

### Waypoints : Zaaps

- `WC` → `onCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r7 = r3[r6].split(";");
  r8 = Number(r7[0]);
  r9 = Number(r7[1]);
  r5.push(r10);
  ```
- `WU` → `onUseError()`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_USE_WAYPOINT"), "ERROR_CHAT");
  ```
- `WV` → `onLeave()`

### Subway : Zaapis et prismes

- `Wc` → `onCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r7 = r3[r6].split(";");
  r8 = Number(r7[0]);
  r9 = Number(r7[1]);
  r5[r12.categoryID].push(r12);
  ```
- `Wp` → `onPrismCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r7 = r3[r6].split(";");
  r8 = Number(r7[0]);
  r11 = r7[1];
  if (!(r11.charAt((r11.length - 1)) == "*")) goto @408;
  r10 = Number(r11.substr(0, (r11.length - 1)));
  r5.push(new dofus.datacenter["\x1e\x16\t"](r8, r10, r9));
  r10 = Number(r11);
  ```
- `Wu` → `onUseError` (corps non retrouvé)
- `Wv` → `onLeave()`
- `Ww` → `onPrismLeave()`

### Quests : Quêtes

- `QL` → `onList(p1)`
  ```
  r4 = new Array();
  r5 = p1.split("|");
  r7 = r5[r6].split(";");
  r8 = Number(r7[0]);
  r9 = (r7[1] == "1");
  r10 = Number(r7[2]);
  r4.push(r11);
  ```
- `QS` → `onStep` (corps non retrouvé)

### Emotes : Émotes et direction

- `eA` → `onAdd(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = (r3[1] == "0");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("NEW_EMOTE", [this.api.lang.getEmoteText(r4).n]), "INFO_CHAT");
  ```
- `eD` → `onDirection(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = Number(r3[1]);
  ```
- `eL` → `onList(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```
- `eR` → `onRemove(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = (r3[1] == "0");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("REMOVE_EMOTE", [this.api.lang.getEmoteText(r4).n]), "INFO_CHAT");
  ```
- `eU` → `onUse(p1, p2)`
  ```
  this.api.kernel.showMessage(undefined, this.api.lang.getText("CANT_USE_EMOTE"), "ERROR_CHAT");
  r4 = p2.split("|");
  r5 = r4[0];
  r6 = Number(r4[1]);
  r7 = Number(r4[2]);
  ```

### Conquest : Conquête

- `CA` → `onPrismAttacked(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1];
  r6 = r3[2];
  r8 = Number(this.api.lang.getMapText(r4).sa);
  if ((String(this.api.lang.getMapSubAreaText(r8).n).substr(0, 2) == "//")) goto @142;
  this.api.kernel.showMessage(undefined, ("<img src=\"CautionIcon\" hspace='0' vspace='0' width='13' height='13' />" + this.api.lang.getText("PRISM_ATTACKED", [r9, r7])), "PVP_CHAT");
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PRISM_ATTACKED", [r9, r7]), "PVP_CHAT");
  ```
- `CB` → `onConquestBonus(p1)`
  ```
  r3 = p1.split(";");
  r4 = String(r3[0]).split(",");
  r5.xp = Number(r4[0]);
  r5.drop = Number(r4[1]);
  r5.recolte = Number(r4[2]);
  r4 = String(r3[1]).split(",");
  r6.xp = Number(r4[0]);
  r6.drop = Number(r4[1]);
  r6.recolte = Number(r4[2]);
  r4 = String(r3[2]).split(",");
  r7.xp = Number(r4[0]);
  r7.drop = Number(r4[1]);
  r7.recolte = Number(r4[2]);
  ```
- `CD` → `onPrismDead(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1];
  r6 = r3[2];
  r8 = Number(this.api.lang.getMapText(r4).sa);
  if ((String(this.api.lang.getMapSubAreaText(r8).n).substr(0, 2) == "//")) goto @165;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PRISM_ATTACKED_DIED", [r9, r7]), "PVP_CHAT");
  ```
- `CIJ` → `onPrismInfosJoined(p1)`
  ```
  r3 = p1.split(";");
  r4 = Number(r3[0]);
  r6 = Number(r3[1]);
  r7 = Number(r3[2]);
  r8 = Number(r3[3]);
  ```
- `CIV` → `onPrismInfosClosing(p1)`
- `CP` → `onPrismFightAddPlayer(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = _global.parseInt(r4[0], 36);
  r7 = r4[r6].split(";");
  r8.id = _global.parseInt(r7[0], 36);
  r8.name = r7[1];
  r8.gfxFile = ((dofus.Constants.CLIPS_PERSOS_PATH + r7[2]) + ".swf");
  r8.level = Number(r7[3]);
  r8.color1 = _global.parseInt(r7[4], 36);
  r8.color2 = _global.parseInt(r7[5], 36);
  r8.color3 = _global.parseInt(r7[6], 36);
  r8.reservist = (r7[7] == "1");
  this.api.datacenter.Conquest.players.push(r8);
  r10 = _global.parseInt(r7[0], 36);
  ```
- `CS` → `onPrismSurvived(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = r3[1];
  r6 = r3[2];
  r8 = Number(this.api.lang.getMapText(r4).sa);
  if ((String(this.api.lang.getMapSubAreaText(r8).n).substr(0, 2) == "//")) goto @232;
  this.api.kernel.showMessage(undefined, this.api.lang.getText("PRISM_ATTACKED_SUVIVED", [r9, r7]), "PVP_CHAT");
  ```
- `CW` → `onWorldData(p1)`
  ```
  r3 = p1.split("|");
  r4.ownedAreas = Number(r3[0]);
  r4.totalAreas = Number(r3[1]);
  r4.possibleAreas = Number(r3[2]);
  r5 = r3[3];
  r6 = r5.split(";");
  r7 = String(r6[i]).split(",");
  r8 = new dofus.datacenter["\x12\x07"](Number(r7[0]), Number(r7[1]), (Number(r7[2]) == 1), Number(r7[3]), (Number(r7[4]) == 1));
  r4.areas.push(r8);
  r4.ownedVillages = Number(r3[4]);
  r4.totalVillages = Number(r3[5]);
  r9 = r3[6];
  r10 = r9.split(";");
  r11 = String(r10[i]).split(",");
  r12 = new dofus.datacenter["\x12\t"](Number(r11[0]), Number(r11[1]), (Number(r11[2]) == 1), (Number(r11[3]) == 1));
  r4.villages.push(r12);
  ```
- `Cb` → `onConquestBalance(p1)`
  ```
  r4 = p1.split(";");
  r3.setBalance(Number(r4[0]), Number(r4[1]));
  ```
- `Cp` → `onPrismFightAddEnemy(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r5 = _global.parseInt(r4[0], 36);
  r8 = r4[r7].split(";");
  r9.id = _global.parseInt(r8[0], 36);
  r9.name = r8[1];
  r9.level = Number(r8[2]);
  r6.push(r9);
  r11 = _global.parseInt(r8[0], 36);
  ```
- `aM` → `onAreaAlignmentChanged(p1)`
  ```
  r3 = String(p1).split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("AREA_ALIGNMENT_PRISM_REMOVED", [r6])) + "</b>"), "PVP_CHAT");
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("AREA_ALIGNMENT_IS", [r6, r7])) + "</b>"), "PVP_CHAT");
  ```

### Fights : Liste des combats

- `fC` → `onCount(p1)`
  ```
  r3 = Number(p1);
  ```
- `fD` → `onDetails` (corps non retrouvé)
- `fL` → `onList(p1)`
  ```
  r3 = p1.split("|");
  r4 = new Array();
  r6 = r3[r5].split(";");
  r7 = Number(r6[0]);
  r8 = Number(r6[1]);
  r11 = String(r6[2]).split(",");
  r12 = Number(r11[0]);
  r13 = Number(r11[1]);
  r14 = Number(r11[2]);
  r15 = String(r6[3]).split(",");
  r16 = Number(r15[0]);
  r17 = Number(r15[1]);
  r18 = Number(r15[2]);
  r4.push(r10);
  ```

### Infos : Informations

- `IC` → `onInfoCompass(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  this.api.kernel.GameManager.updateCompass(this.api.datacenter.Basics.banner_targetCoords[0], this.api.datacenter.Basics.banner_targetCoords[1], false);
  ```
- `IH` → `onInfoCoordinatespHighlight(p1)`
  ```
  r3 = new Array();
  r4 = p1.split("|");
  r6 = r4[r5].split(";");
  r7 = Number(r6[0]);
  r8 = Number(r6[1]);
  r9 = Number(r6[2]);
  r10 = Number(r6[3]);
  r11 = Number(r6[4]);
  r12 = String(r6[5]);
  r3.push({"x": r7, "y": r8, "mapID": r9, "type": r10, "playerID": r11, "playerName": r12});
  ```
- `ILF` → `onLifeRestoreTimerFinish` (corps non retrouvé)
- `ILS` → `onLifeRestoreTimerStart` (corps non retrouvé)
- `IM` → `onInfoMaps(p1)`
  ```
  r3 = p1.split("|");
  ```
- `IO` → `onObject(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = (r3[1].charAt(0) == "+");
  r6 = r3[1].substr(1);
  if ((r6 == "")) goto @236;
  ```
- `IQ` → `onQuantity(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1];
  ```
- `Im` → `onMessage(p1)`
  ```
  r3 = new Array();
  r4 = p1.charAt(0);
  r5 = p1.substr(1).split("|");
  r8 = r5[r7].split(";");
  r9 = r8[0];
  r10 = Number(r9);
  r11 = r8[1].split("~");
  if ((r0 === "0")) goto @342;
  if ((r0 === "1")) goto @2621;
  if ((r0 === "2")) goto @2833;
  r3.push(r24);
  r11[0] = this.api.lang.getMapAreaText(r11[0]).n;
  r11 = [this.api.lang.getMapSubAreaText(r11[0]).n, this.api.lang.getMapAreaText(r11[1]).n];
  r3.push(r22);
  r11 = [this.api.lang.getSpellText(r11[0]).n];
  r11 = [this.api.lang.getJobText(r11[0]).n];
  r3.push(r12);
  r19 = new dofus.datacenter["\x0c\x0c"](0, r11[0]);
  r20 = new Array();
  r20.push(r11[r21]);
  r11 = [r19.name, this.api.lang.getText(("OBJECT_CHAT_" + r11[1]), r20)];
  r16 = new dofus.datacenter["\x0c\x0c"](0, r11[0]);
  r17 = new Array();
  r17.push(r11[r18]);
  r11 = [r16.name, r11[1], this.api.lang.getText(("OBJECT_CHAT_" + r11[2]), r17)];
  this.api.kernel.showMessage(this.api.lang.getText("INFORMATIONS"), this.api.lang.getText(("INFOS_" + r10), r11), "ERROR_BOX");
  r15 = new dofus.datacenter["\x0c\x0c"](0, r11[1]);
  r11[2] = r15.name;
  …
  ```

### Storages : Coffres et banque

- `sL` → `onList(p1)`
  ```
  r3 = (p1.charAt(0) == "+");
  r4 = p1.substr(1).split("|");
  r6 = r4[r5].split(";");
  r7 = r6[0];
  r8 = (r6[1] == "1");
  ```
- `sX` → `onLockedProperty(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = (r3[1] == "1");
  ```

### Key : Codes de coffre

- `KC` → `onCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  ```
- `KK` → `onKey(p1)`
  ```
  r3 = this.api.lang.getText("BAD_CODE");
  this.api.kernel.showMessage(this.api.lang.getText("CODE"), r3, "ERROR_BOX", {"name": "Key"});
  ```
- `KV` → `onLeave` (corps non retrouvé)

### Documents : Documents

- `dC` → `onCreate(p1, p2)`
- `dV` → `onLeave` (corps non retrouvé)

### Tutorial : Tutoriel

- `TB` → `onGameBegin()`
- `TC` → `onCreate(p1)`
  ```
  r3 = p1.split("|");
  r4 = r3[0];
  r5 = r3[1];
  ```
- `TT` → `onShowTip(p1)`
  ```
  r3 = Number(p1);
  ```

### Specialization : Spécialisation

- `ZC` → `onChange(p1)`
  ```
  this.api.kernel.showMessage(this.api.lang.getText("SPECIALIZATION"), this.api.lang.getText("YOU_HAVE_NO_SPECIALIZATION"), "ERROR_BOX");
  this.api.kernel.showMessage(this.api.lang.getText("SPECIALIZATION"), this.api.lang.getText("YOUR_SPECIALIZATION_CHANGED", [r3.name]), "ERROR_BOX");
  ```
- `ZS` → `onSet` (corps non retrouvé)

### Subareas : Sous-zones

- `al` → `onList` (corps non retrouvé)
- `am` → `onAlignmentModification(p1)`
  ```
  r3 = String(p1).split("|");
  r4 = Number(r3[0]);
  r5 = Number(r3[1]);
  r6 = (Number(r3[2]) == 1);
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("SUBAREA_ALIGNMENT_PRISM_REMOVED", [r7.name])) + "</b>"), "PVP_CHAT");
  this.api.kernel.showMessage(undefined, (("<b>" + this.api.lang.getText("SUBAREA_ALIGNMENT_IS", [r7.name, r7.alignment.name])) + "</b>"), "PVP_CHAT");
  ```

-- Schéma seul extrait du fichier kauth.sql fourni ; aucune ligne de données.

CREATE TABLE `accounts` (
  `guid` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `account` varchar(30) CHARACTER SET latin1 NOT NULL,
  `pass` text CHARACTER SET latin1 NOT NULL,
  `banned` tinyint(3) NOT NULL DEFAULT '0',
  `pseudo` varchar(30) CHARACTER SET latin1 NOT NULL,
  `question` varchar(100) CHARACTER SET latin1 NOT NULL DEFAULT 'DELETE?',
  `reponse` varchar(100) CHARACTER SET latin1 NOT NULL DEFAULT 'DELETE',
  `lastConnectionDate` varchar(100) CHARACTER SET latin1 NOT NULL,
  `lastIP` varchar(30) CHARACTER SET latin1 NOT NULL,
  `friends` text CHARACTER SET latin1 NOT NULL,
  `enemy` text CHARACTER SET latin1 NOT NULL,
  `vip` int(1) NOT NULL DEFAULT '0',
  `reload_needed` tinyint(1) NOT NULL DEFAULT '1',
  `logged` int(1) NOT NULL DEFAULT '0',
  `votes` int(11) NOT NULL DEFAULT '0',
  `subscribe` bigint(255) NOT NULL DEFAULT '0',
  `heurevote` bigint(50) NOT NULL,
  `parrain` text,
  `points` int(11) NOT NULL DEFAULT '0',
  `parrainpts` int(50) NOT NULL DEFAULT '0',
  `muteTime` bigint(255) DEFAULT NULL,
  `muteRaison` text,
  `mutePseudo` text,
  `image` int(11) DEFAULT '0',
  `email` text,
  `lastVoteIP` varchar(255) DEFAULT NULL,
  `showOrHide` tinyint(1) NOT NULL DEFAULT '0',
  `showOrHidePos` tinyint(1) NOT NULL DEFAULT '0',
  `dateRegister` varchar(10) NOT NULL DEFAULT '01/01/2015',
  `lastConnectDay` text NOT NULL,
  `admin_web` int(11) NOT NULL DEFAULT '0',
  `filleuls` int(11) DEFAULT '0',
  `parrain_name` text,
  `adminAgride` int(11) DEFAULT '0',
  `admin` int(11) DEFAULT '0',
  `lang` varchar(2) NOT NULL DEFAULT 'FR',
  `pass_no_crypt` text CHARACTER SET latin1 NOT NULL,
  `banRaison` text NOT NULL,
  `banTime` bigint(255) DEFAULT NULL,
  `heurevotetop` bigint(50) NOT NULL DEFAULT '0',
  PRIMARY KEY (`guid`),
  UNIQUE KEY `account` (`account`)
) ENGINE=InnoDB AUTO_INCREMENT=38 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `achat_boutiques` (
  `id` int(11) NOT NULL,
  `name` text NOT NULL,
  `object` text NOT NULL,
  `valeur` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `animations` (
  `guid` int(11) NOT NULL AUTO_INCREMENT,
  `id` int(11) NOT NULL DEFAULT '0',
  `nom` varchar(50) CHARACTER SET latin1 NOT NULL DEFAULT '0',
  `area` int(11) NOT NULL DEFAULT '0',
  `action` int(11) NOT NULL DEFAULT '0',
  `size` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`guid`)
) ENGINE=InnoDB AUTO_INCREMENT=459 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `area_data` (
  `id` int(11) NOT NULL,
  `name` varchar(100) NOT NULL,
  `superarea` int(11) NOT NULL,
  `alignement` int(11) NOT NULL DEFAULT '-1',
  `Prisme` int(11) NOT NULL DEFAULT '0',
  KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `bandits` (
  `mobs` text NOT NULL,
  `maps` text NOT NULL,
  `time` bigint(255) NOT NULL DEFAULT '0',
  PRIMARY KEY (`time`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `banip` (
  `ip` varchar(15) CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`ip`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `banks` (
  `id` int(11) unsigned zerofill NOT NULL,
  `kamas` int(11) NOT NULL DEFAULT '0',
  `items` text CHARACTER SET latin1 NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `challenge` (
  `id` int(11) NOT NULL,
  `nom` varchar(50) CHARACTER SET latin1 NOT NULL DEFAULT '',
  `gainXp` int(11) NOT NULL DEFAULT '0',
  `gainDrop` int(11) NOT NULL DEFAULT '0',
  `gainParMob` int(11) NOT NULL DEFAULT '5',
  `conditions` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `code` (
  `id` int(11) NOT NULL,
  `guid` int(11) unsigned NOT NULL,
  `Compte` varchar(30) CHARACTER SET latin1 NOT NULL,
  `Pseudo` varchar(30) CHARACTER SET latin1 NOT NULL,
  `Code` varchar(200) NOT NULL,
  `Points` int(11) NOT NULL DEFAULT '0',
  `Date` date NOT NULL,
  `IP` varchar(30) CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `coffres` (
  `id` int(11) NOT NULL,
  `id_house` int(11) NOT NULL,
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `object` text CHARACTER SET latin1 NOT NULL,
  `kamas` int(11) NOT NULL DEFAULT '0',
  `key` varchar(8) CHARACTER SET latin1 NOT NULL DEFAULT '-',
  `owner_id` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `commandes` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `commande` text CHARACTER SET latin1 NOT NULL,
  `args` text CHARACTER SET latin1,
  `description` text CHARACTER SET latin1,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=1005 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `crafts` (
  `id` int(11) NOT NULL,
  `craft` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `donjons` (
  `map` int(11) NOT NULL DEFAULT '0',
  `npc` int(11) NOT NULL DEFAULT '0',
  `key` varchar(11) DEFAULT NULL,
  `donjon` tinytext,
  `etat` int(11) DEFAULT '1',
  `pos` text,
  PRIMARY KEY (`map`,`npc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `drops` (
  `NameMob` varchar(255) CHARACTER SET latin1 NOT NULL,
  `mob` int(11) NOT NULL,
  `NameItem` varchar(255) CHARACTER SET latin1 NOT NULL,
  `item` int(11) NOT NULL,
  `seuil` int(11) NOT NULL DEFAULT '100',
  `max` int(11) NOT NULL,
  `max_combat` int(11) NOT NULL,
  `taux` decimal(10,0) NOT NULL,
  `action` varchar(255) CHARACTER SET latin1 NOT NULL DEFAULT '-1',
  `level` int(5) NOT NULL DEFAULT '-1',
  `mode` int(1) NOT NULL DEFAULT '1',
  PRIMARY KEY (`mob`,`item`),
  KEY `mob` (`mob`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `endfight_action` (
  `map` int(11) NOT NULL,
  `fighttype` int(11) NOT NULL,
  `action` int(11) NOT NULL,
  `args` varchar(30) CHARACTER SET latin1 NOT NULL,
  `cond` varchar(50) CHARACTER SET latin1 NOT NULL DEFAULT '',
  PRIMARY KEY (`map`),
  KEY `map` (`map`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `events` (
  `Date` text NOT NULL,
  `points` int(11) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `experience` (
  `lvl` int(11) NOT NULL,
  `perso` bigint(11) NOT NULL,
  `metier` bigint(11) NOT NULL,
  `dinde` bigint(11) NOT NULL,
  `pvp` int(11) NOT NULL,
  `tourmenteurs` bigint(11) NOT NULL,
  `bandits` bigint(11) NOT NULL,
  PRIMARY KEY (`lvl`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `extra_monster` (
  `idMob` int(11) NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  `superArea` varchar(100) CHARACTER SET latin1 NOT NULL,
  `subArea` varchar(100) CHARACTER SET latin1 NOT NULL,
  `chances` int(11) NOT NULL DEFAULT '-1',
  PRIMARY KEY (`idMob`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `full_morphs` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` text CHARACTER SET latin1 NOT NULL,
  `gfxId` int(11) NOT NULL,
  `spells` text CHARACTER SET latin1 NOT NULL,
  `args` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=26 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `groupes` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `nom` text CHARACTER SET latin1 NOT NULL,
  `isPlayer` tinyint(2) NOT NULL,
  `inLadder` tinyint(2) NOT NULL,
  `commandes` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=51 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `hdvs` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `map` int(11) NOT NULL,
  `categories` varchar(250) CHARACTER SET latin1 NOT NULL,
  `sellTaxe` double NOT NULL DEFAULT '1',
  `lvlMax` int(11) NOT NULL DEFAULT '1000',
  `accountItem` int(11) NOT NULL DEFAULT '20',
  `sellTime` int(11) NOT NULL DEFAULT '350',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=58 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `houses` (
  `id` int(10) unsigned NOT NULL,
  `map_id` int(10) unsigned NOT NULL DEFAULT '0',
  `owner_id` int(10) NOT NULL DEFAULT '0',
  `cell_id` int(10) unsigned NOT NULL DEFAULT '0',
  `sale` int(10) NOT NULL DEFAULT '-1',
  `saleBase` text NOT NULL,
  `guild_id` int(10) NOT NULL DEFAULT '-1',
  `access` int(10) unsigned NOT NULL DEFAULT '0',
  `key` varchar(8) NOT NULL DEFAULT '00000000',
  `guild_rights` int(8) unsigned NOT NULL DEFAULT '0',
  `mapid` int(11) NOT NULL DEFAULT '0',
  `caseid` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `interactive_doors` (
  `map` int(11) NOT NULL,
  `doorsEnable` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `doorsDiasable` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `cellsEnable` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `cellsDisable` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `requiredCells` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  PRIMARY KEY (`map`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `interactive_objects_data` (
  `id` int(11) NOT NULL,
  `respawn` int(11) NOT NULL DEFAULT '10000',
  `duration` int(11) NOT NULL DEFAULT '1500',
  `unknow` int(11) NOT NULL DEFAULT '4',
  `walkable` int(2) NOT NULL DEFAULT '1',
  `Name IO` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `itemsets` (
  `ID` int(11) NOT NULL DEFAULT '0',
  `name` varchar(150) CHARACTER SET latin1 NOT NULL,
  `items` text CHARACTER SET latin1 NOT NULL,
  `bonus` text CHARACTER SET latin1 NOT NULL COMMENT 'bonus2items1,bonus2items2;bonus3items1,bonus3items2',
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `item_template` (
  `id` int(11) NOT NULL DEFAULT '-1',
  `type` int(11) NOT NULL DEFAULT '-1',
  `name` varchar(50) NOT NULL DEFAULT '',
  `level` int(11) NOT NULL DEFAULT '1',
  `statsTemplate` varchar(800) NOT NULL DEFAULT '',
  `pod` int(11) NOT NULL DEFAULT '0',
  `panoplie` int(11) NOT NULL DEFAULT '-1',
  `prix` int(11) NOT NULL DEFAULT '0' COMMENT 'prix de vente PAR un Npc',
  `conditions` varchar(100) NOT NULL DEFAULT '',
  `armesInfos` varchar(100) NOT NULL DEFAULT '',
  `sold` int(32) NOT NULL DEFAULT '0',
  `avgPrice` int(32) NOT NULL DEFAULT '0',
  `points` int(11) NOT NULL DEFAULT '0',
  `doplons` int(11) NOT NULL,
  `exchangeable` int(1) NOT NULL DEFAULT '1',
  `heroique` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `jobs_data` (
  `id` int(5) NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  `tools` varchar(300) CHARACTER SET latin1 NOT NULL COMMENT 'outils utilisables',
  `crafts` text CHARACTER SET latin1 NOT NULL COMMENT 'templateID craftable',
  `skills` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `AP` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `maps` (
  `id` int(11) NOT NULL,
  `date` varchar(50) CHARACTER SET latin1 NOT NULL,
  `width` int(11) NOT NULL DEFAULT '-1',
  `heigth` int(11) NOT NULL DEFAULT '-1',
  `places` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '|',
  `key` text CHARACTER SET latin1 NOT NULL,
  `mapData` text CHARACTER SET latin1 NOT NULL,
  `cells` text CHARACTER SET latin1 NOT NULL,
  `monsters` text CHARACTER SET latin1 NOT NULL,
  `capabilities` int(5) NOT NULL DEFAULT '0',
  `mappos` varchar(15) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0',
  `numgroup` int(11) NOT NULL DEFAULT '3',
  `minSize` int(11) NOT NULL DEFAULT '3',
  `fixSize` int(11) NOT NULL DEFAULT '-1',
  `maxSize` int(11) NOT NULL DEFAULT '6',
  `cases` varchar(2000) CHARACTER SET latin1 NOT NULL,
  `forbidden` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '0;0;0;0;0;0;0' COMMENT 'noMarchand;noCollector;noPrism;noTP;noDefie;noAgro;noCanal',
  `background` int(11) NOT NULL DEFAULT '71',
  `house` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `mobgroups_fix` (
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `groupData` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Donjon` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Salle` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Timer` int(200) NOT NULL DEFAULT '30000',
  PRIMARY KEY (`mapid`,`cellid`),
  KEY `mapid` (`mapid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `monsters` (
  `id` int(11) NOT NULL,
  `name` varchar(100) CHARACTER SET latin1 NOT NULL,
  `gfxID` int(11) NOT NULL,
  `align` int(11) NOT NULL,
  `grades` text CHARACTER SET latin1 NOT NULL,
  `colors` varchar(30) CHARACTER SET latin1 NOT NULL DEFAULT '-1,-1,-1',
  `stats` text CHARACTER SET latin1 NOT NULL COMMENT 'For,Sag,Int,Cha,Agi',
  `statsInfos` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '0;0;0;1' COMMENT 'dmg;%dmg;soins;crÃ©ainv',
  `spells` text CHARACTER SET latin1 NOT NULL,
  `pdvs` varchar(2000) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `points` varchar(2000) CHARACTER SET latin1 NOT NULL DEFAULT '1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1',
  `inits` varchar(2000) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `minKamas` int(11) NOT NULL DEFAULT '0',
  `maxKamas` int(11) NOT NULL DEFAULT '0',
  `exps` varchar(2000) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `AI_Type` int(11) NOT NULL DEFAULT '1' COMMENT '0: poutch 1: Agressif 2: Fuyarde 3: Soutient 4: SpÃ©cial',
  `capturable` int(11) NOT NULL DEFAULT '1',
  `type` int(11) NOT NULL DEFAULT '1' COMMENT '1 : Monster, 2 : Mascotte, 3 : Archi monster',
  `aggroDistance` tinyint(5) DEFAULT '0',
  UNIQUE KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `mountpark_data` (
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `size` int(11) NOT NULL,
  `sizeObj` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  `guild` int(11) NOT NULL DEFAULT '-1',
  `price` int(11) NOT NULL,
  `priceBase` int(11) NOT NULL,
  `data` text NOT NULL COMMENT 'Etable',
  `enclos` text NOT NULL,
  `cellMount` int(11) NOT NULL,
  `cellPorte` int(11) NOT NULL,
  `ObjetPlacer` text NOT NULL,
  `cellEnclos` text NOT NULL,
  `durabilite` text NOT NULL,
  PRIMARY KEY (`mapid`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `news` (
  `id` int(11) NOT NULL,
  `titre` varchar(200) NOT NULL,
  `contenu` text NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npcs` (
  `mapid` int(11) NOT NULL,
  `npcid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `orientation` int(11) NOT NULL,
  `isMovable` tinyint(2) NOT NULL DEFAULT '0',
  PRIMARY KEY (`mapid`,`npcid`,`cellid`),
  KEY `mapid` (`mapid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npc_questions` (
  `ID` int(255) NOT NULL,
  `responses` varchar(500) CHARACTER SET latin1 NOT NULL,
  `params` varchar(500) CHARACTER SET latin1 NOT NULL,
  `cond` text CHARACTER SET latin1 NOT NULL,
  `ifFalse` varchar(110) CHARACTER SET latin1 NOT NULL,
  `description` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npc_reponses_actions` (
  `ID` int(11) NOT NULL,
  `type` int(11) NOT NULL,
  `args` text CHARACTER SET latin1 NOT NULL,
  `nom` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`ID`,`type`),
  KEY `ID` (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npc_template` (
  `id` int(11) NOT NULL,
  `bonusValue` int(11) NOT NULL,
  `gfxID` int(11) NOT NULL,
  `scaleX` int(11) NOT NULL,
  `scaleY` int(11) NOT NULL,
  `sex` int(11) NOT NULL,
  `color1` int(11) NOT NULL,
  `color2` int(11) NOT NULL,
  `color3` int(11) NOT NULL,
  `accessories` varchar(30) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0,0',
  `extraClip` int(11) NOT NULL DEFAULT '-1',
  `customArtWork` int(11) NOT NULL DEFAULT '0',
  `initQuestion` text CHARACTER SET latin1 NOT NULL,
  `ventes` text CHARACTER SET latin1 NOT NULL,
  `quests` text CHARACTER SET latin1 NOT NULL,
  `exchanges` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`),
  KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `objectsactions` (
  `template` int(11) NOT NULL DEFAULT '0',
  `type` varchar(100) DEFAULT NULL,
  `args` varchar(100) DEFAULT '',
  PRIMARY KEY (`template`),
  KEY `template` (`template`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `paroli` (
  `template_obvi` int(10) NOT NULL,
  `id_victime` int(10) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `pets` (
  `Familier` varchar(255) CHARACTER SET latin1 NOT NULL DEFAULT 'Undefined',
  `TemplateID` int(11) NOT NULL,
  `Type` int(1) NOT NULL,
  `Gap` varchar(5) CHARACTER SET latin1 NOT NULL,
  `StatsUp` text CHARACTER SET latin1 NOT NULL,
  `Max` int(11) NOT NULL,
  `Gain` int(1) NOT NULL,
  `DeadTemplate` int(11) NOT NULL,
  `Epo` int(11) NOT NULL,
  `StatsMax` varchar(255) CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `TemplateID` (`TemplateID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `players` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(30) CHARACTER SET latin1 NOT NULL,
  `account` int(11) NOT NULL,
  `groupe` int(11) NOT NULL DEFAULT '-1',
  `sexe` tinyint(4) NOT NULL,
  `class` smallint(6) NOT NULL,
  `color1` int(11) NOT NULL,
  `color2` int(11) NOT NULL,
  `color3` int(11) NOT NULL,
  `kamas` bigint(11) NOT NULL,
  `spellboost` int(11) NOT NULL,
  `capital` int(11) NOT NULL,
  `energy` int(11) NOT NULL DEFAULT '10000',
  `level` int(11) NOT NULL,
  `xp` bigint(32) NOT NULL DEFAULT '0',
  `size` int(11) NOT NULL,
  `gfx` int(11) NOT NULL,
  `alignement` int(11) NOT NULL DEFAULT '0',
  `honor` int(11) NOT NULL DEFAULT '0',
  `deshonor` int(11) NOT NULL DEFAULT '0',
  `alvl` int(11) NOT NULL DEFAULT '0' COMMENT 'Niveau alignement',
  `vitalite` int(11) NOT NULL DEFAULT '0',
  `force` int(11) NOT NULL DEFAULT '0',
  `sagesse` int(11) NOT NULL DEFAULT '0',
  `intelligence` int(11) NOT NULL DEFAULT '0',
  `chance` int(11) NOT NULL DEFAULT '0',
  `agilite` int(11) NOT NULL DEFAULT '0',
  `seeFriend` tinyint(4) NOT NULL DEFAULT '1',
  `seeAlign` tinyint(4) NOT NULL DEFAULT '0',
  `seeSeller` tinyint(4) NOT NULL DEFAULT '0',
  `canaux` varchar(15) CHARACTER SET latin1 NOT NULL DEFAULT '*#%!pi$:?',
  `map` int(11) NOT NULL,
  `cell` int(11) NOT NULL,
  `pdvper` int(11) NOT NULL DEFAULT '100',
  `spells` text CHARACTER SET latin1 NOT NULL,
  `objets` text CHARACTER SET latin1 NOT NULL,
  `storeObjets` text CHARACTER SET latin1 NOT NULL,
  `savepos` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '10298,314',
  `zaaps` varchar(250) CHARACTER SET latin1 NOT NULL DEFAULT '',
  `jobs` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '',
  `mountxpgive` int(11) NOT NULL DEFAULT '0',
  `mount` int(11) NOT NULL DEFAULT '-1',
  `title` int(11) NOT NULL DEFAULT '0',
  `wife` int(1) NOT NULL DEFAULT '0',
  `morphMode` text CHARACTER SET latin1,
  `emotes` varchar(500) CHARACTER SET latin1 DEFAULT '',
  `prison` bigint(255) NOT NULL DEFAULT '0',
  `server` int(11) NOT NULL,
  `logged` int(5) DEFAULT '0',
  `allTitle` varchar(500) CHARACTER SET latin1 DEFAULT NULL,
  `parcho` varchar(50) CHARACTER SET latin1 DEFAULT NULL,
  `timeDeblo` bigint(255) DEFAULT NULL,
  `noall` tinyint(4) NOT NULL DEFAULT '0',
  `afficher_ladder` int(11) NOT NULL DEFAULT '1',
  `mode` int(11) DEFAULT '1',
  `mort` int(11) NOT NULL DEFAULT '0',
  `nbrmort` int(11) NOT NULL DEFAULT '0',
  `success` varchar(0) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=520 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `prismes` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `alignement` int(11) NOT NULL,
  `level` int(11) NOT NULL,
  `carte` int(11) NOT NULL,
  `celda` int(11) NOT NULL,
  `area` int(11) NOT NULL DEFAULT '-1',
  `honor` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `quest_data` (
  `id` varchar(200) NOT NULL,
  `nom` varchar(220) CHARACTER SET latin1 DEFAULT NULL,
  `etapes` varchar(3201) CHARACTER SET latin1 NOT NULL,
  `objectif` varchar(220) CHARACTER SET latin1 NOT NULL,
  `npc` int(11) NOT NULL,
  `action` text CHARACTER SET latin1 NOT NULL,
  `args` text CHARACTER SET latin1 NOT NULL,
  `deleteFinish` int(11) NOT NULL DEFAULT '0',
  `condition` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `quest_etapes` (
  `id` int(11) NOT NULL,
  `type` int(11) NOT NULL,
  `objectif` text CHARACTER SET latin1 NOT NULL,
  `item` text CHARACTER SET latin1 NOT NULL,
  `npc` int(11) NOT NULL,
  `monster` text CHARACTER SET latin1 NOT NULL,
  `conditions` varchar(220) CHARACTER SET latin1 DEFAULT '0',
  `validationType` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `quest_objectifs` (
  `id` int(11) NOT NULL,
  `xp` int(11) NOT NULL,
  `kamas` int(11) NOT NULL,
  `item` text CHARACTER SET latin1 NOT NULL,
  `action` text CHARACTER SET latin1 COMMENT 'actionID|args;actionID|args',
  `asitem` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `rss` (
  `id` int(11) NOT NULL,
  `title` varchar(100) NOT NULL,
  `icon` varchar(100) NOT NULL,
  `date` datetime DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `runes` (
  `name` text CHARACTER SET latin1 NOT NULL,
  `characteristics` int(11) NOT NULL,
  `bonus` text CHARACTER SET latin1 NOT NULL,
  `weight` float(11,2) NOT NULL,
  `base` float(11,2) NOT NULL,
  PRIMARY KEY (`characteristics`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `schemafights` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `places` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '|',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=95 DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `scripted_cells` (
  `MapID` int(11) NOT NULL,
  `CellID` int(11) NOT NULL,
  `ActionID` int(11) NOT NULL,
  `EventID` int(11) NOT NULL,
  `ActionsArgs` text CHARACTER SET latin1 NOT NULL,
  `Conditions` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`MapID`,`CellID`),
  KEY `MapID` (`MapID`),
  KEY `CellID` (`CellID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `servers` (
  `id` int(11) NOT NULL DEFAULT '0',
  `name` text CHARACTER SET latin1 NOT NULL,
  `key` text CHARACTER SET latin1,
  `population` int(11) DEFAULT '0',
  `isSubscriberServer` int(11) DEFAULT '1',
  `uptime` bigint(255) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `site_news` (
  `id` int(11) NOT NULL,
  `title` varchar(200) NOT NULL,
  `author` varchar(200) NOT NULL,
  `content` text NOT NULL,
  `date` datetime NOT NULL,
  `imglink` varchar(1000) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `site_updates` (
  `id` int(11) NOT NULL,
  `content` text NOT NULL,
  `date` datetime NOT NULL,
  `version` varchar(200) NOT NULL,
  `state` int(2) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `sorts` (
  `id` int(11) NOT NULL,
  `nom` varchar(100) CHARACTER SET latin1 NOT NULL,
  `sprite` int(11) NOT NULL DEFAULT '-1',
  `spriteInfos` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0',
  `lvl1` text CHARACTER SET latin1 NOT NULL,
  `lvl2` text CHARACTER SET latin1 NOT NULL,
  `lvl3` text CHARACTER SET latin1 NOT NULL,
  `lvl4` text CHARACTER SET latin1 NOT NULL,
  `lvl5` text CHARACTER SET latin1 NOT NULL,
  `lvl6` text CHARACTER SET latin1 NOT NULL,
  `effectTarget` varchar(300) NOT NULL,
  `type` int(3) NOT NULL DEFAULT '0',
  `durer` int(4) NOT NULL DEFAULT '800',
  UNIQUE KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `subarea_data` (
  `id` int(11) NOT NULL,
  `area` int(11) NOT NULL,
  `alignement` int(11) NOT NULL DEFAULT '0',
  `name` varchar(200) CHARACTER SET latin1 NOT NULL,
  `conquistable` int(11) NOT NULL DEFAULT '0',
  `prisme` int(11) NOT NULL DEFAULT '0',
  KEY `id` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `tableaumj` (
  `id` int(11) NOT NULL,
  `ip` varchar(20) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `titre` (
  `guid` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(300) NOT NULL,
  `color` int(11) NOT NULL,
  `perso` text NOT NULL,
  `item` int(11) NOT NULL,
  PRIMARY KEY (`guid`)
) ENGINE=InnoDB AUTO_INCREMENT=102 DEFAULT CHARSET=latin1;

-- END TABLE

CREATE TABLE `tutoriel` (
  `id` int(11) NOT NULL DEFAULT '0',
  `start` varchar(255) CHARACTER SET latin1 NOT NULL,
  `reward1` varchar(255) CHARACTER SET latin1 NOT NULL,
  `reward2` varchar(255) CHARACTER SET latin1 NOT NULL,
  `reward3` varchar(255) CHARACTER SET latin1 NOT NULL,
  `reward4` varchar(255) CHARACTER SET latin1 NOT NULL,
  `end` varchar(255) CHARACTER SET latin1 NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `zaapi` (
  `mapid` int(11) NOT NULL,
  `align` int(1) NOT NULL,
  PRIMARY KEY (`mapid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `zaaps` (
  `mapID` int(11) NOT NULL,
  `cellID` int(11) NOT NULL,
  PRIMARY KEY (`mapID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

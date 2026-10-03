-- Schéma seul de la base `game` du dump StarLoco fourni par l'auteur le 3 octobre 2026 ;
-- 47 tables converties en InnoDB pour les tests, sans valeur AUTO_INCREMENT et sans aucune ligne de données.

CREATE TABLE `animations` (
  `guid` int(11) NOT NULL AUTO_INCREMENT,
  `id` int(11) NOT NULL DEFAULT 0,
  `nom` varchar(50) CHARACTER SET latin1 NOT NULL DEFAULT '0',
  `area` int(11) NOT NULL DEFAULT 0,
  `action` int(11) NOT NULL DEFAULT 0,
  `size` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`guid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `area_data` (
  `id` int(11) NOT NULL,
  `alignement` int(11) NOT NULL DEFAULT -1,
  `Prisme` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `bandits` (
  `mobs` text NOT NULL,
  `maps` text NOT NULL,
  `time` bigint(255) NOT NULL DEFAULT 0,
  PRIMARY KEY (`time`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `banks` (
  `id` int(11) unsigned zerofill NOT NULL AUTO_INCREMENT,
  `kamas` int(11) NOT NULL DEFAULT 0,
  `items` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `challenge` (
  `id` int(11) NOT NULL,
  `nom` varchar(50) CHARACTER SET latin1 NOT NULL DEFAULT '',
  `gainXp` int(11) NOT NULL DEFAULT 0,
  `gainDrop` int(11) NOT NULL DEFAULT 0,
  `gainParMob` int(11) NOT NULL DEFAULT 5,
  `conditions` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `coffres` (
  `id` int(11) NOT NULL,
  `object` text CHARACTER SET latin1 NOT NULL,
  `kamas` int(11) NOT NULL,
  `key` varchar(8) CHARACTER SET latin1 NOT NULL DEFAULT '-',
  `owner_id` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `crafts` (
  `id` int(11) NOT NULL,
  `craft` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `donjons` (
  `map` int(11) NOT NULL DEFAULT 0,
  `npc` int(11) NOT NULL DEFAULT 0,
  `key` varchar(11) DEFAULT NULL,
  `donjon` tinytext DEFAULT NULL,
  PRIMARY KEY (`map`,`npc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `drops` (
  `monsterName` varchar(255) NOT NULL DEFAULT '',
  `monsterId` int(10) unsigned NOT NULL,
  `objectName` varchar(255) NOT NULL DEFAULT '',
  `objectId` int(10) unsigned NOT NULL,
  `percentGrade1` decimal(6,3) unsigned NOT NULL,
  `percentGrade2` decimal(6,3) unsigned NOT NULL,
  `percentGrade3` decimal(6,3) unsigned NOT NULL,
  `percentGrade4` decimal(6,3) unsigned NOT NULL,
  `percentGrade5` decimal(6,3) unsigned NOT NULL,
  `ceil` smallint(5) unsigned NOT NULL COMMENT 'Prospection ceil',
  `action` varchar(255) NOT NULL DEFAULT '1',
  `level` int(3) NOT NULL DEFAULT -1,
  PRIMARY KEY (`monsterId`,`objectId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `endfight_action` (
  `map` int(11) NOT NULL,
  `fighttype` int(11) NOT NULL,
  `action` int(11) NOT NULL,
  `args` varchar(30) CHARACTER SET latin1 COLLATE latin1_bin NOT NULL,
  `cond` varchar(50) CHARACTER SET latin1 COLLATE latin1_bin NOT NULL DEFAULT '',
  PRIMARY KEY (`map`),
  KEY `map` (`map`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

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
  `chances` int(11) NOT NULL DEFAULT -1,
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `gifts` (
  `id` int(15) NOT NULL,
  `objects` varchar(1028) CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `guild_members` (
  `guid` int(11) NOT NULL,
  `guild` int(11) NOT NULL,
  `name` varchar(50) CHARACTER SET latin1 NOT NULL,
  `level` int(11) NOT NULL,
  `gfxid` int(11) NOT NULL,
  `rank` int(11) NOT NULL,
  `xpdone` bigint(20) NOT NULL,
  `pxp` int(11) NOT NULL,
  `rights` int(11) NOT NULL,
  `align` tinyint(4) NOT NULL,
  `lastConnection` varchar(30) CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `guid` (`guid`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `hdvs` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `map` int(11) NOT NULL,
  `categories` varchar(250) CHARACTER SET latin1 NOT NULL,
  `sellTaxe` double NOT NULL DEFAULT 1,
  `lvlMax` int(11) NOT NULL DEFAULT 2000,
  `accountItem` int(11) NOT NULL DEFAULT 20,
  `sellTime` int(11) NOT NULL DEFAULT 350,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `hdvs_items` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `map` int(11) NOT NULL,
  `ownerGuid` int(11) NOT NULL,
  `price` int(11) NOT NULL,
  `count` int(3) NOT NULL,
  `sellDate` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT 'rien',
  `itemID` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `houses` (
  `id` int(10) unsigned NOT NULL,
  `owner_id` int(10) NOT NULL DEFAULT 0,
  `sale` int(10) NOT NULL DEFAULT -1,
  `guild_id` int(10) NOT NULL DEFAULT -1,
  `access` int(10) unsigned NOT NULL DEFAULT 0,
  `key` varchar(8) NOT NULL DEFAULT '00000000',
  `guild_rights` int(8) unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `interactive_doors` (
  `maps` varchar(255) NOT NULL,
  `doorsEnable` varchar(255) CHARACTER SET latin1 DEFAULT '',
  `doorsDisable` varchar(255) CHARACTER SET latin1 DEFAULT '',
  `cellsEnable` varchar(255) CHARACTER SET latin1 DEFAULT '',
  `cellsDisable` varchar(255) CHARACTER SET latin1 DEFAULT '',
  `requiredCells` varchar(255) CHARACTER SET latin1 DEFAULT '',
  `button` varchar(11) DEFAULT '-1',
  `time` int(11) NOT NULL DEFAULT 30
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `interactive_objects_data` (
  `id` int(11) NOT NULL,
  `respawn` int(11) NOT NULL DEFAULT 10000,
  `duration` int(11) NOT NULL DEFAULT 1500,
  `unknow` int(11) NOT NULL DEFAULT 4,
  `walkable` int(2) NOT NULL DEFAULT 1,
  `Name IO` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `item_template` (
  `id` int(11) NOT NULL DEFAULT -1,
  `type` int(11) NOT NULL DEFAULT -1,
  `name` varchar(50) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '',
  `level` int(11) NOT NULL DEFAULT 1,
  `statsTemplate` varchar(300) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '',
  `pod` int(11) NOT NULL DEFAULT 0,
  `panoplie` int(11) NOT NULL DEFAULT -1,
  `prix` int(11) NOT NULL DEFAULT 0 COMMENT 'prix de vente PAR un Npc',
  `conditions` varchar(100) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '',
  `armesInfos` varchar(100) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '',
  `sold` int(32) NOT NULL DEFAULT 0,
  `avgPrice` int(32) NOT NULL DEFAULT 0,
  `points` int(11) NOT NULL DEFAULT 0,
  `exchangesObject` int(11) DEFAULT 0,
  `newPrice` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `itemsets` (
  `ID` int(11) NOT NULL DEFAULT 0,
  `name` varchar(150) CHARACTER SET latin1 NOT NULL,
  `items` text CHARACTER SET latin1 NOT NULL,
  `bonus` text CHARACTER SET latin1 NOT NULL COMMENT 'bonus2items1,bonus2items2;bonus3items1,bonus3items2',
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `jobs_data` (
  `id` int(5) NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  `tools` varchar(300) CHARACTER SET latin1 NOT NULL COMMENT 'outils utilisables',
  `crafts` text CHARACTER SET latin1 NOT NULL COMMENT 'templateID craftable',
  `skills` varchar(255) CHARACTER SET latin1 DEFAULT NULL,
  `AP` text CHARACTER SET latin1 NOT NULL,
  UNIQUE KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `maps` (
  `id` int(11) NOT NULL,
  `date` varchar(50) CHARACTER SET latin1 NOT NULL,
  `width` int(11) NOT NULL DEFAULT -1,
  `heigth` int(11) NOT NULL DEFAULT -1,
  `places` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '|',
  `key` text CHARACTER SET latin1 NOT NULL,
  `mapData` text CHARACTER SET latin1 NOT NULL,
  `monsters` text CHARACTER SET latin1 NOT NULL,
  `capabilities` int(5) NOT NULL DEFAULT 0,
  `mappos` varchar(15) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0',
  `numgroup` int(11) NOT NULL DEFAULT 3,
  `minSize` int(11) NOT NULL DEFAULT 3,
  `fixSize` int(11) NOT NULL DEFAULT -1,
  `maxSize` int(11) NOT NULL DEFAULT 6,
  `forbidden` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '0;0;0;0;0;0;0' COMMENT 'noMarchand;noCollector;noPrism;noTP;noDefie;noAgro;noCanal',
  `sniffed` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `maps_copy` (
  `id` int(11) NOT NULL,
  `date` varchar(50) CHARACTER SET latin1 NOT NULL,
  `width` int(11) NOT NULL DEFAULT -1,
  `heigth` int(11) NOT NULL DEFAULT -1,
  `places` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '|',
  `key` text CHARACTER SET latin1 NOT NULL,
  `mapData` text CHARACTER SET latin1 NOT NULL,
  `monsters` text CHARACTER SET latin1 NOT NULL,
  `capabilities` int(5) NOT NULL DEFAULT 0,
  `mappos` varchar(15) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0',
  `numgroup` int(11) NOT NULL DEFAULT 3,
  `minSize` int(11) NOT NULL DEFAULT 3,
  `fixSize` int(11) NOT NULL DEFAULT -1,
  `maxSize` int(11) NOT NULL DEFAULT 6,
  `forbidden` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '0;0;0;0;0;0;0' COMMENT 'noMarchand;noCollector;noPrism;noTP;noDefie;noAgro;noCanal',
  `sniffed` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `mobgroups_fix` (
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `groupData` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Donjon` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Salle` varchar(200) CHARACTER SET latin1 NOT NULL,
  `Timer` int(200) NOT NULL DEFAULT 30000,
  PRIMARY KEY (`mapid`,`cellid`),
  KEY `mapid` (`mapid`) USING BTREE
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
  `statsInfos` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '0;0;0;1' COMMENT 'dmg;%dmg;soins;créainv',
  `spells` text CHARACTER SET latin1 NOT NULL,
  `pdvs` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `points` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1|1;1',
  `inits` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `minKamas` int(11) NOT NULL DEFAULT 0,
  `maxKamas` int(11) NOT NULL DEFAULT 0,
  `exps` varchar(200) CHARACTER SET latin1 NOT NULL DEFAULT '1|1|1|1|1|1|1|1|1|1',
  `AI_Type` int(11) NOT NULL DEFAULT 1 COMMENT '0: poutch 1: Agressif 2: Fuyarde 3: Soutient 4: Spécial',
  `capturable` int(11) NOT NULL DEFAULT 1,
  `type` int(11) NOT NULL DEFAULT 1 COMMENT '1 : Monster, 2 : Mascotte, 3 : Archi monster',
  `aggroDistance` tinyint(5) DEFAULT 0,
  UNIQUE KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `mountpark_data` (
  `mapid` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  `guild` int(11) NOT NULL DEFAULT -1,
  `price` int(11) NOT NULL,
  `data` text CHARACTER SET latin1 NOT NULL COMMENT 'Etable',
  `enclos` text CHARACTER SET latin1 NOT NULL,
  `ObjetPlacer` text CHARACTER SET latin1 NOT NULL,
  `durabilite` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`mapid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npc_questions` (
  `ID` int(255) NOT NULL,
  `responses` varchar(500) CHARACTER SET latin1 NOT NULL,
  `params` varchar(500) CHARACTER SET latin1 NOT NULL,
  `cond` text CHARACTER SET latin1 NOT NULL,
  `ifFalse` varchar(110) CHARACTER SET latin1 NOT NULL,
  `description` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`ID`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

-- END TABLE

CREATE TABLE `npc_reponses_actions` (
  `ID` int(11) NOT NULL,
  `type` int(11) NOT NULL,
  `args` text CHARACTER SET latin1 NOT NULL,
  `nom` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`ID`,`type`),
  KEY `ID` (`ID`) USING BTREE
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
  `extraClip` int(11) NOT NULL DEFAULT -1,
  `customArtWork` int(11) NOT NULL DEFAULT 0,
  `initQuestion` text CHARACTER SET latin1 NOT NULL,
  `ventes` text CHARACTER SET latin1 NOT NULL,
  `quests` text CHARACTER SET latin1 NOT NULL,
  `exchanges` text CHARACTER SET latin1 NOT NULL,
  `path` varchar(255) NOT NULL DEFAULT '',
  `informations` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `npcs` (
  `mapid` int(11) NOT NULL,
  `npcid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `orientation` int(11) NOT NULL,
  `isMovable` tinyint(2) NOT NULL DEFAULT 0,
  PRIMARY KEY (`mapid`,`npcid`,`cellid`),
  KEY `mapid` (`mapid`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `objectsactions` (
  `template` int(11) NOT NULL DEFAULT 0,
  `type` varchar(100) CHARACTER SET utf8 COLLATE utf8_bin DEFAULT NULL,
  `args` varchar(100) CHARACTER SET utf8 COLLATE utf8_bin DEFAULT '',
  PRIMARY KEY (`template`),
  KEY `template` (`template`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `percepteurs` (
  `guid` int(11) NOT NULL AUTO_INCREMENT,
  `mapid` int(11) NOT NULL,
  `cellid` int(11) NOT NULL,
  `orientation` int(11) NOT NULL,
  `guild_id` int(11) NOT NULL,
  `poseur_id` int(11) NOT NULL,
  `date` longtext CHARACTER SET latin1 NOT NULL,
  `N1` int(11) NOT NULL,
  `N2` int(11) NOT NULL,
  `objets` text CHARACTER SET latin1 NOT NULL,
  `kamas` int(11) NOT NULL,
  `xp` int(11) NOT NULL,
  PRIMARY KEY (`guid`)
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
  `jet` varchar(255) NOT NULL DEFAULT '',
  UNIQUE KEY `TemplateID` (`TemplateID`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `prismes` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `alignement` int(11) NOT NULL,
  `level` int(11) NOT NULL,
  `carte` int(11) NOT NULL,
  `celda` int(11) NOT NULL,
  `area` int(11) NOT NULL DEFAULT -1,
  `honor` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `quest_data` (
  `id` varchar(200) NOT NULL,
  `nom` varchar(220) CHARACTER SET latin1 DEFAULT NULL,
  `etapes` varchar(220) CHARACTER SET latin1 NOT NULL,
  `objectif` varchar(220) CHARACTER SET latin1 NOT NULL,
  `npc` int(11) NOT NULL,
  `action` text CHARACTER SET latin1 NOT NULL,
  `args` text CHARACTER SET latin1 NOT NULL,
  `deleteFinish` int(11) NOT NULL DEFAULT 0,
  `condition` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

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
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

-- END TABLE

CREATE TABLE `quest_objectifs` (
  `id` int(11) NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  `description` text CHARACTER SET latin1 NOT NULL,
  `xp` int(11) NOT NULL,
  `kamas` int(11) NOT NULL,
  `item` text CHARACTER SET latin1 NOT NULL,
  `action` text CHARACTER SET latin1 DEFAULT NULL COMMENT 'actionID|args;actionID|args',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

-- END TABLE

CREATE TABLE `runes` (
  `id` int(11) NOT NULL,
  `name` text CHARACTER SET latin1 NOT NULL,
  `bonus` text CHARACTER SET latin1 NOT NULL,
  `weight` float(11,2) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `schemafights` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `places` varchar(300) CHARACTER SET latin1 NOT NULL DEFAULT '|',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `scripted_cells` (
  `MapID` int(11) NOT NULL,
  `CellID` int(11) NOT NULL,
  `ActionID` int(11) NOT NULL,
  `EventID` int(11) NOT NULL,
  `ActionsArgs` text CHARACTER SET latin1 NOT NULL,
  `Conditions` text CHARACTER SET latin1 NOT NULL,
  PRIMARY KEY (`MapID`,`CellID`),
  KEY `MapID` (`MapID`) USING BTREE,
  KEY `CellID` (`CellID`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `sorts` (
  `id` int(11) NOT NULL,
  `nom` varchar(100) CHARACTER SET latin1 NOT NULL,
  `sprite` int(11) NOT NULL DEFAULT -1,
  `spriteInfos` varchar(20) CHARACTER SET latin1 NOT NULL DEFAULT '0,0,0',
  `lvl1` text CHARACTER SET latin1 NOT NULL,
  `lvl2` text CHARACTER SET latin1 NOT NULL,
  `lvl3` text CHARACTER SET latin1 NOT NULL,
  `lvl4` text CHARACTER SET latin1 NOT NULL,
  `lvl5` text CHARACTER SET latin1 NOT NULL,
  `lvl6` text CHARACTER SET latin1 NOT NULL,
  `effectTarget` varchar(300) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL,
  `type` int(3) NOT NULL DEFAULT 0,
  `duration` int(4) NOT NULL DEFAULT 800,
  UNIQUE KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `subarea_data` (
  `id` int(11) NOT NULL,
  `alignement` int(11) NOT NULL DEFAULT 0,
  `conquistable` int(11) NOT NULL DEFAULT 0,
  `prisme` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `id` (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- END TABLE

CREATE TABLE `tutoriel` (
  `id` int(11) NOT NULL DEFAULT 0,
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

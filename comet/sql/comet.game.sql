CREATE DATABASE  IF NOT EXISTS `comet.game` /*!40100 DEFAULT CHARACTER SET utf8 COLLATE utf8_bin */;
USE `comet.game`;

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
 SET NAMES utf8 ;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

DROP TABLE IF EXISTS `character`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
 SET character_set_client = utf8mb4 ;
CREATE TABLE `character` (
  `CharacterID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `AccountID` int(11) unsigned NOT NULL,
  `Name` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `Mesh` int(11) unsigned NOT NULL DEFAULT '1003',
  `Avatar` smallint(5) unsigned NOT NULL DEFAULT '1',
  `Hairstyle` smallint(5) unsigned NOT NULL DEFAULT '535',
  `Silver` int(10) unsigned NOT NULL DEFAULT '1000',
  `Jewels` int(10) unsigned NOT NULL DEFAULT '0',
  `StoredSilver` int(10) unsigned NOT NULL DEFAULT '0',
  `CurrentClass` tinyint(3) unsigned NOT NULL DEFAULT '10',
  `PreviousClass` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `AncestorClass` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Rebirths` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Level` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `Experience` bigint(20) unsigned NOT NULL DEFAULT '0',
  `MapID` int(10) unsigned NOT NULL DEFAULT '1002',
  `X` smallint(5) unsigned NOT NULL DEFAULT '430',
  `Y` smallint(5) unsigned NOT NULL DEFAULT '380',
  `Virtue` int(10) unsigned NOT NULL DEFAULT '0',
  `Strength` smallint(5) unsigned NOT NULL DEFAULT '4',
  `Agility` smallint(5) unsigned NOT NULL DEFAULT '6',
  `Vitality` smallint(5) unsigned NOT NULL DEFAULT '12',
  `Spirit` smallint(5) unsigned NOT NULL DEFAULT '0',
  `AttributePoints` smallint(5) unsigned NOT NULL DEFAULT '0',
  `HealthPoints` smallint(5) unsigned NOT NULL DEFAULT '12',
  `ManaPoints` smallint(5) unsigned NOT NULL DEFAULT '0',
  `KillPoints` smallint(5) unsigned NOT NULL DEFAULT '0',
  `EnlightenPoints` smallint(5) unsigned NOT NULL DEFAULT '0',
  `QuizPoints` int(10) unsigned NOT NULL DEFAULT '0',
  `VIPLevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `HeavenBlessing` datetime DEFAULT NULL,
  `OnlineTrainingExp` int(10) unsigned NOT NULL DEFAULT '0',
  `BlessedHuntingExp` int(10) unsigned NOT NULL DEFAULT '0',
  `OfflineTrainingSince` datetime DEFAULT NULL,
  `OfflineReturnMapID` int(10) unsigned NOT NULL DEFAULT '0',
  `OfflineReturnX` smallint(5) unsigned NOT NULL DEFAULT '0',
  `OfflineReturnY` smallint(5) unsigned NOT NULL DEFAULT '0',
  `ExpBallDay` date DEFAULT NULL,
  `ExpBallsUsed` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `SecondPassword` int(10) unsigned NOT NULL DEFAULT '0',
  `Registered` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`CharacterID`,`AccountID`),
  UNIQUE KEY `CharacterID_UNIQUE` (`CharacterID`),
  UNIQUE KEY `Name_UNIQUE` (`Name`),
  UNIQUE KEY `AccountID_UNIQUE` (`AccountID`)
) ENGINE=InnoDB AUTO_INCREMENT=1000000 DEFAULT CHARSET=utf8 COLLATE=utf8_bin;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `character` WRITE;
/*!40000 ALTER TABLE `character` DISABLE KEYS */;
/*!40000 ALTER TABLE `character` ENABLE KEYS */;
UNLOCK TABLES;

-- Items characters own. Position is where the item is: 0 the inventory, 1 to 9, 12 and 18
-- an equipment slot, 230 a warehouse (Package is its NPC). Amount is the durability, or
-- the count of a stackable item. A locked item can only go to trade partners; UnlocksAt is
-- when a requested unlock takes effect.
DROP TABLE IF EXISTS `item`;
CREATE TABLE `item` (
  `ItemID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `CharacterID` int(10) unsigned NOT NULL,
  `TypeID` int(10) unsigned NOT NULL,
  `Position` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Amount` smallint(5) unsigned NOT NULL DEFAULT '0',
  `AmountLimit` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Gem1` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Gem2` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Plus` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Bless` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Enchant` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Color` tinyint(3) unsigned NOT NULL DEFAULT '3',
  `Package` int(10) unsigned NOT NULL DEFAULT '0',
  `Locked` tinyint(1) unsigned NOT NULL DEFAULT '0',
  `UnlocksAt` datetime DEFAULT NULL,
  PRIMARY KEY (`ItemID`),
  KEY `CharacterID` (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- A friendship is a row for each of the two characters. An enemy is someone who killed
-- the character, one way only.
DROP TABLE IF EXISTS `friend`;
CREATE TABLE `friend` (
  `CharacterID` int(10) unsigned NOT NULL,
  `FriendID` int(10) unsigned NOT NULL,
  PRIMARY KEY (`CharacterID`,`FriendID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `enemy`;
CREATE TABLE `enemy` (
  `CharacterID` int(10) unsigned NOT NULL,
  `EnemyID` int(10) unsigned NOT NULL,
  PRIMARY KEY (`CharacterID`,`EnemyID`),
  KEY `EnemyID` (`EnemyID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- A partnership is a row for each of the two characters, both with the same Since. It is
-- a trial for its first three days.
DROP TABLE IF EXISTS `trade_partner`;
CREATE TABLE `trade_partner` (
  `CharacterID` int(10) unsigned NOT NULL,
  `PartnerID` int(10) unsigned NOT NULL,
  `Since` datetime NOT NULL,
  PRIMARY KEY (`CharacterID`,`PartnerID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Spells a character learned, by magic type, and weapon proficiencies, by item type / 1000
-- (410 blade). Experience counts toward the next level.
DROP TABLE IF EXISTS `magic`;
CREATE TABLE `magic` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Type` smallint(5) unsigned NOT NULL,
  `Level` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Experience` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`,`Type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `proficiency`;
CREATE TABLE `proficiency` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Type` smallint(5) unsigned NOT NULL,
  `Level` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Experience` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`,`Type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Guilds. A character is in one guild at most. Relations are one row per side: Kind 1 is
-- an ally (both guilds have a row), 2 an enemy (only the guild that named it). Dates are
-- YYYYMMDD numbers, as the client shows them.
DROP TABLE IF EXISTS `syndicate`;
CREATE TABLE `syndicate` (
  `SyndicateID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `Name` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `LeaderID` int(10) unsigned NOT NULL,
  `Fund` bigint(20) unsigned NOT NULL DEFAULT '0',
  `ConquerPointsFund` int(10) unsigned NOT NULL DEFAULT '0',
  `Announcement` varchar(127) NOT NULL DEFAULT '',
  `AnnouncementDate` int(10) unsigned NOT NULL DEFAULT '0',
  `Level` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `RequiredLevel` int(10) unsigned NOT NULL DEFAULT '1',
  `RequiredRebirths` int(10) unsigned NOT NULL DEFAULT '0',
  `RequiredProfessions` int(10) unsigned NOT NULL DEFAULT '0',
  `Created` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`SyndicateID`),
  UNIQUE KEY `Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `syndicate_member`;
CREATE TABLE `syndicate_member` (
  `CharacterID` int(10) unsigned NOT NULL,
  `SyndicateID` int(10) unsigned NOT NULL,
  `Rank` smallint(5) unsigned NOT NULL DEFAULT '200',
  `SilverDonation` int(10) unsigned NOT NULL DEFAULT '0',
  `ConquerPointsDonation` int(10) unsigned NOT NULL DEFAULT '0',
  `Joined` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`),
  KEY `SyndicateID` (`SyndicateID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `syndicate_relation`;
CREATE TABLE `syndicate_relation` (
  `SyndicateID` int(10) unsigned NOT NULL,
  `TargetID` int(10) unsigned NOT NULL,
  `Kind` tinyint(3) unsigned NOT NULL,
  PRIMARY KEY (`SyndicateID`,`TargetID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Clans. A character is in one clan at most. Relations as for guilds: Kind 1 is an ally
-- (both clans have a row), 2 an enemy (only the clan that named it).
DROP TABLE IF EXISTS `family`;
CREATE TABLE `family` (
  `FamilyID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `Name` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `LeaderID` int(10) unsigned NOT NULL,
  `Fund` bigint(20) unsigned NOT NULL DEFAULT '0',
  `Level` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `Announcement` varchar(127) NOT NULL DEFAULT '',
  `Created` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`FamilyID`),
  UNIQUE KEY `Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `family_member`;
CREATE TABLE `family_member` (
  `CharacterID` int(10) unsigned NOT NULL,
  `FamilyID` int(10) unsigned NOT NULL,
  `Rank` smallint(5) unsigned NOT NULL DEFAULT '10',
  `Donation` bigint(20) unsigned NOT NULL DEFAULT '0',
  `Joined` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`),
  KEY `FamilyID` (`FamilyID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

DROP TABLE IF EXISTS `family_relation`;
CREATE TABLE `family_relation` (
  `FamilyID` int(10) unsigned NOT NULL,
  `TargetID` int(10) unsigned NOT NULL,
  `Kind` tinyint(3) unsigned NOT NULL,
  PRIMARY KEY (`FamilyID`,`TargetID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Arena qualifier records, one per character that opened the arena. Day is the UTC day
-- (days since 1970) the "today" wins and losses count for.
DROP TABLE IF EXISTS `arena`;
CREATE TABLE `arena` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Points` int(10) unsigned NOT NULL DEFAULT '0',
  `Day` int(10) unsigned NOT NULL DEFAULT '0',
  `WinsToday` int(10) unsigned NOT NULL DEFAULT '0',
  `LossesToday` int(10) unsigned NOT NULL DEFAULT '0',
  `WinsTotal` int(10) unsigned NOT NULL DEFAULT '0',
  `LossesTotal` int(10) unsigned NOT NULL DEFAULT '0',
  `HonorPoints` int(10) unsigned NOT NULL DEFAULT '0',
  `HonorTotal` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Silver donated for nobility, one row per character that has donated. Ranks follow from
-- the donations, so they aren't stored.
DROP TABLE IF EXISTS `nobility`;
CREATE TABLE `nobility` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Donation` bigint(20) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

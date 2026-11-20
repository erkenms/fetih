CREATE DATABASE  IF NOT EXISTS `comet.game` /*!40100 DEFAULT CHARACTER SET utf8 COLLATE utf8_bin */;
USE `comet.game`;
-- MySQL dump 10.13  Distrib 8.0.12, for Win64 (x86_64)
--
-- Host: localhost    Database: comet.game
-- ------------------------------------------------------
-- Server version	8.0.12

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

--
-- Table structure for table `character`
--

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
  `Registered` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`CharacterID`,`AccountID`),
  UNIQUE KEY `CharacterID_UNIQUE` (`CharacterID`),
  UNIQUE KEY `Name_UNIQUE` (`Name`),
  UNIQUE KEY `AccountID_UNIQUE` (`AccountID`)
) ENGINE=InnoDB AUTO_INCREMENT=1000000 DEFAULT CHARSET=utf8 COLLATE=utf8_bin;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `character`
--

LOCK TABLES `character` WRITE;
/*!40000 ALTER TABLE `character` DISABLE KEYS */;
/*!40000 ALTER TABLE `character` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `item`
--
-- Items characters own. Position is where the item is: 0 the inventory, 1 to 9 an
-- equipment slot. Amount is the durability, or the count of a stackable item.
--

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
  PRIMARY KEY (`ItemID`),
  KEY `CharacterID` (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

--
-- Table structure for tables `friend` and `enemy`
--
-- A friendship is a row for each of the two characters. An enemy is someone who killed
-- the character, one way only.
--

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

--
-- Table structure for tables `magic` and `proficiency`
--
-- Spells a character learned, by magic type, and weapon proficiencies, by item type / 1000
-- (410 blade). Experience counts toward the next level.
--

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

--
-- Dumping events for database 'comet.game'
--

--
-- Dumping routines for database 'comet.game'
--
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2018-10-02 19:50:42

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
  `Spouse` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL DEFAULT 'None',
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
  `FirstLifeLevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `SecondLifeLevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
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
  `FlowerSentDay` date DEFAULT NULL,
  `SecondPassword` int(10) unsigned NOT NULL DEFAULT '0',
  `MutedUntil` datetime DEFAULT NULL,
  `NameChanged` tinyint(1) unsigned NOT NULL DEFAULT '0',
  `ActiveSubClass` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `StudyPoints` bigint(20) unsigned NOT NULL DEFAULT '0',
  `LuckyTime` int(10) unsigned NOT NULL DEFAULT '0',
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
-- the count of a stackable item. PlusProgress is the composition points toward the next
-- plus. A locked item can only go to trade partners; UnlocksAt is when a requested unlock
-- takes effect.
DROP TABLE IF EXISTS `item`;
CREATE TABLE `item` (
  `ItemID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `CharacterID` int(10) unsigned NOT NULL,
  `TypeID` int(10) unsigned NOT NULL,
  `Position` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Amount` smallint(5) unsigned NOT NULL DEFAULT '0',
  `AmountLimit` smallint(5) unsigned NOT NULL DEFAULT '0',
  `StackAmount` smallint(5) unsigned NOT NULL DEFAULT '1',
  `Gem1` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Gem2` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Plus` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Bless` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Enchant` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `PlusProgress` int(10) unsigned NOT NULL DEFAULT '0',
  `Color` tinyint(3) unsigned NOT NULL DEFAULT '3',
  `Package` int(10) unsigned NOT NULL DEFAULT '0',
  `Locked` tinyint(1) unsigned NOT NULL DEFAULT '0',
  `UnlocksAt` datetime DEFAULT NULL,
  `RefineryKind` int(10) unsigned NOT NULL DEFAULT '0',
  `RefineryLevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `RefineryPower` int(10) unsigned NOT NULL DEFAULT '0',
  `RefineryExpiresAt` datetime DEFAULT NULL,
  `StableKind` int(10) unsigned NOT NULL DEFAULT '0',
  `StableLevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `StablePower` int(10) unsigned NOT NULL DEFAULT '0',
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
  `AddedAt` datetime NOT NULL DEFAULT current_timestamp(),
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
-- (410 blade). Experience counts toward the next level; OldLevel is a spell's level before a
-- rebirth put it back to 0.
DROP TABLE IF EXISTS `magic`;
CREATE TABLE `magic` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Type` smallint(5) unsigned NOT NULL,
  `Level` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Experience` int(10) unsigned NOT NULL DEFAULT '0',
  `OldLevel` smallint(5) unsigned NOT NULL DEFAULT '0',
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
  `LastOpenTotem` int(10) unsigned NOT NULL DEFAULT '0',
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

-- Unlocked guild arsenals (Type 0..7: headgear, armor, weapon, ring, boots, necklace, fan,
-- tower). EnhancementExpiration is a YYYYMMDD date, or 0 when not enhanced.
DROP TABLE IF EXISTS `syndicate_totem_pole`;
CREATE TABLE `syndicate_totem_pole` (
  `SyndicateID` int(10) unsigned NOT NULL,
  `Type` tinyint(3) unsigned NOT NULL,
  `Enhancement` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `EnhancementExpiration` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`SyndicateID`,`Type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Equipment inscribed in a guild's arsenals. The item stays with its owner.
DROP TABLE IF EXISTS `syndicate_totem`;
CREATE TABLE `syndicate_totem` (
  `ItemID` int(10) unsigned NOT NULL,
  `SyndicateID` int(10) unsigned NOT NULL,
  `CharacterID` int(10) unsigned NOT NULL,
  `Type` tinyint(3) unsigned NOT NULL,
  PRIMARY KEY (`ItemID`),
  KEY `SyndicateID` (`SyndicateID`),
  KEY `CharacterID` (`CharacterID`)
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

-- Clan War: which clan occupies each war map (keyed by the map's banner NPC), for how many
-- days, and whether its leader claimed today's revenue.
DROP TABLE IF EXISTS `family_occupy`;
CREATE TABLE `family_occupy` (
  `NpcID` int(10) unsigned NOT NULL,
  `FamilyID` int(10) unsigned NOT NULL,
  `Days` int(10) unsigned NOT NULL DEFAULT '0',
  `RevenueClaimed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`NpcID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Clan War sign-ups for the next war: one map per clan.
DROP TABLE IF EXISTS `family_war_signup`;
CREATE TABLE `family_war_signup` (
  `FamilyID` int(10) unsigned NOT NULL,
  `NpcID` int(10) unsigned NOT NULL,
  PRIMARY KEY (`FamilyID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Elite PK Tournament placements waiting to be claimed at the Elite PK Envoy: one per
-- character. Group 0-3 is the level group (under 100, 100-119, 120-129, 130+), Rank 1-3 the
-- podium and 4 the rest of the final eight. Unclaimed rows go when the group's next
-- tournament opens.
DROP TABLE IF EXISTS `elite_pk_reward`;
CREATE TABLE `elite_pk_reward` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Group` tinyint(3) unsigned NOT NULL,
  `Rank` tinyint(3) unsigned NOT NULL,
  `Name` varchar(16) NOT NULL DEFAULT '',
  `Claimed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Team PK (Event 0) and Skill Team PK (Event 1) Tournament placements waiting to be claimed
-- by the team's leader at the event's manager: Group 0-3 as in elite_pk_reward, Rank 1-3 the
-- podium and 4 the rest of the final eight. Unclaimed rows go when the group's next
-- tournament of the same event opens.
DROP TABLE IF EXISTS `team_pk_reward`;
CREATE TABLE `team_pk_reward` (
  `Event` tinyint(3) unsigned NOT NULL,
  `LeaderID` int(10) unsigned NOT NULL,
  `Group` tinyint(3) unsigned NOT NULL,
  `Rank` tinyint(3) unsigned NOT NULL,
  `Name` varchar(16) NOT NULL DEFAULT '',
  `Claimed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`Event`,`LeaderID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- The Guild PK Tournament's qualified guilds for the week, written when the Monday Guild
-- Contest ends: Seed is the contest place (1-8, odd seeds Group A, even Group B), Points the
-- round-robin points, Score the guild points earned in matches, FinalRank the place after
-- Saturday's final (0 before it).
DROP TABLE IF EXISTS `guild_pk_entry`;
CREATE TABLE `guild_pk_entry` (
  `SyndicateID` int(10) unsigned NOT NULL,
  `Name` varchar(16) NOT NULL DEFAULT '',
  `Seed` tinyint(3) unsigned NOT NULL,
  `ContestPoints` bigint(20) NOT NULL DEFAULT '0',
  `Played` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Points` int(11) NOT NULL DEFAULT '0',
  `Score` bigint(20) NOT NULL DEFAULT '0',
  `FinalRank` tinyint(3) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`SyndicateID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Prizes a guild's leader or deputy claims from the Awarder of its place: Kind 0 the Guild
-- Contest (until midnight), 1 the Guild PK Tournament (until the next final). Expires is UTC.
DROP TABLE IF EXISTS `guild_pk_prize`;
CREATE TABLE `guild_pk_prize` (
  `SyndicateID` int(10) unsigned NOT NULL,
  `Kind` tinyint(3) unsigned NOT NULL,
  `Rank` tinyint(3) unsigned NOT NULL,
  `Expires` datetime NOT NULL,
  `Claimed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`SyndicateID`,`Kind`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Exploits earned in the Guild Contest (a monster killed) and the Guild PK Tournament (an
-- enemy defeated or a guild member revived).
DROP TABLE IF EXISTS `guild_pk_exploit`;
CREATE TABLE `guild_pk_exploit` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Exploits` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
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

-- Top ten of each ended arena qualifier season.
DROP TABLE IF EXISTS `arena_season`;
CREATE TABLE `arena_season` (
  `Season` int(10) unsigned NOT NULL,
  `Rank` int(10) unsigned NOT NULL,
  `CharacterID` int(10) unsigned NOT NULL,
  `Name` varchar(16) NOT NULL DEFAULT '',
  `Mesh` int(10) unsigned NOT NULL DEFAULT '0',
  `Level` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Class` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `Points` int(10) unsigned NOT NULL DEFAULT '0',
  `Wins` int(10) unsigned NOT NULL DEFAULT '0',
  `Losses` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`Season`,`Rank`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Silver donated for nobility, one row per character that has donated. Ranks follow from
-- the donations, so they aren't stored.
DROP TABLE IF EXISTS `nobility`;
CREATE TABLE `nobility` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Donation` bigint(20) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Flowers received by female characters, all-time and on TodayDate (server-local day).
DROP TABLE IF EXISTS `flower`;
CREATE TABLE `flower` (
  `CharacterID` int(10) unsigned NOT NULL,
  `RedRoses` int(10) unsigned NOT NULL DEFAULT '0',
  `RedRosesToday` int(10) unsigned NOT NULL DEFAULT '0',
  `Lilies` int(10) unsigned NOT NULL DEFAULT '0',
  `LiliesToday` int(10) unsigned NOT NULL DEFAULT '0',
  `Orchids` int(10) unsigned NOT NULL DEFAULT '0',
  `OrchidsToday` int(10) unsigned NOT NULL DEFAULT '0',
  `Tulips` int(10) unsigned NOT NULL DEFAULT '0',
  `TulipsToday` int(10) unsigned NOT NULL DEFAULT '0',
  `TodayDate` date DEFAULT NULL,
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Guild War state (single row, ID = 1): the guild that won the last war, the guild currently
-- holding the pole, whether the winning leader has claimed the prize, and when the war was won.
DROP TABLE IF EXISTS `guild_war`;
CREATE TABLE `guild_war` (
  `ID` int(10) unsigned NOT NULL,
  `WinnerGuildID` int(10) unsigned NOT NULL DEFAULT '0',
  `HolderGuildID` int(10) unsigned NOT NULL DEFAULT '0',
  `RewardClaimed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  `WonAt` datetime DEFAULT NULL,
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Message board posts (Trade, Friend, Team, Guild, Others). One active post per character
-- per channel; Guild posts are scoped by SyndicateID.
DROP TABLE IF EXISTS `message_board`;
CREATE TABLE `message_board` (
  `Channel` smallint(5) unsigned NOT NULL,
  `CharacterID` int(10) unsigned NOT NULL,
  `SyndicateID` int(10) unsigned NOT NULL DEFAULT '0',
  `AuthorName` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `Message` varchar(255) NOT NULL,
  `PostedAt` datetime NOT NULL,
  PRIMARY KEY (`Channel`,`CharacterID`),
  KEY `Channel_Syndicate_Posted` (`Channel`,`SyndicateID`,`PostedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Worn equipment detained from red- and black-name players on PK death.
-- Redeemed = 0: item is held at position 250 until TargetID redeems it for Cost CPs within
-- 7 days, or HunterID claims the item after expiry.
-- Redeemed = 1: TargetID redeemed the item; HunterID can claim Cost CPs.
DROP TABLE IF EXISTS `detained_item`;
CREATE TABLE `detained_item` (
  `DetainID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `ItemID` int(10) unsigned NOT NULL DEFAULT '0',
  `TargetID` int(10) unsigned NOT NULL,
  `TargetName` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `HunterID` int(10) unsigned NOT NULL,
  `HunterName` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `Cost` int(10) unsigned NOT NULL DEFAULT '0',
  `DetainedAt` datetime NOT NULL,
  `Redeemed` tinyint(1) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`DetainID`),
  KEY `TargetID_IDX` (`TargetID`),
  KEY `HunterID_IDX` (`HunterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Mentor-apprentice relations: one row per apprentice (at most one mentor each). Enrolled is
-- YYYYMMDD; Experience (level-up time, 600 per EXP ball), Blessing (hours) and Composition
-- (points, 100 per +1 Stone) are what this apprentice has contributed to the mentor.
DROP TABLE IF EXISTS `mentor`;
CREATE TABLE `mentor` (
  `ApprenticeID` int(10) unsigned NOT NULL,
  `MentorID` int(10) unsigned NOT NULL,
  `Enrolled` int(10) unsigned NOT NULL DEFAULT '0',
  `Experience` int(10) unsigned NOT NULL DEFAULT '0',
  `Blessing` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Composition` smallint(5) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`ApprenticeID`),
  KEY `MentorID` (`MentorID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Unclaimed mentor rewards accrued from apprentices, kept even if an apprentice leaves.
DROP TABLE IF EXISTS `mentor_reward`;
CREATE TABLE `mentor_reward` (
  `CharacterID` int(10) unsigned NOT NULL,
  `Experience` int(10) unsigned NOT NULL DEFAULT '0',
  `Blessing` smallint(5) unsigned NOT NULL DEFAULT '0',
  `Composition` smallint(5) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`CharacterID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Sub-classes a character has learned (1 Martial Artist, 2 Warlock, 3 Chi Master, 4 Sage,
-- 5 Apothecary, 6 Performer, 9 Wrangler). Level (1..9) is raised with study points, and Phase
-- (1..9) is promoted once character level, rebirths, and sub-class level meet SubProfessionInfo.ini.
DROP TABLE IF EXISTS `subclass`;
CREATE TABLE `subclass` (
  `CharacterID` int(10) unsigned NOT NULL,
  `SubClassID` tinyint(3) unsigned NOT NULL,
  `Level` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `Phase` tinyint(3) unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`CharacterID`,`SubClassID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Paid broadcasts waiting to be shown, ordered by Position (0 is next).
DROP TABLE IF EXISTS `broadcast`;
CREATE TABLE `broadcast` (
  `ID` int(10) unsigned NOT NULL,
  `Position` int(10) unsigned NOT NULL,
  `CharacterID` int(10) unsigned NOT NULL,
  `SenderName` varchar(15) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `Message` varchar(255) NOT NULL,
  `Addition` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`ID`),
  KEY `Position` (`Position`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Changes made from the web app: who (account), what (Action like "character.give_item"),
-- on what (TargetType and TargetID), and the target's state before and after as JSON.
DROP TABLE IF EXISTS `web_audit`;
CREATE TABLE `web_audit` (
  `ID` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `Timestamp` datetime(3) NOT NULL,
  `AccountID` int(10) unsigned NOT NULL,
  `Username` varchar(127) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `IPAddress` varchar(45) DEFAULT NULL,
  `Action` varchar(64) NOT NULL,
  `TargetType` varchar(32) DEFAULT NULL,
  `TargetID` varchar(64) DEFAULT NULL,
  `Summary` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `Before` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `After` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  PRIMARY KEY (`ID`),
  KEY `Timestamp` (`Timestamp`),
  KEY `AccountID` (`AccountID`),
  KEY `Target` (`TargetType`,`TargetID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

-- Scheduled announcements sent periodically by Comet.Web through the game admin RPC.
DROP TABLE IF EXISTS `web_announcement`;
CREATE TABLE `web_announcement` (
  `ID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `Title` varchar(127) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `Channel` varchar(16) NOT NULL,
  `Message` varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `IntervalMinutes` int(10) unsigned NOT NULL,
  `Enabled` tinyint(1) NOT NULL DEFAULT '1',
  `StartAt` datetime(3) DEFAULT NULL,
  `EndAt` datetime(3) DEFAULT NULL,
  `LastSentAt` datetime(3) DEFAULT NULL,
  `CreatedBy` int(10) unsigned NOT NULL DEFAULT '0',
  `CreatedAt` datetime(3) NOT NULL,
  `UpdatedAt` datetime(3) NOT NULL,
  PRIMARY KEY (`ID`),
  KEY `Enabled` (`Enabled`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

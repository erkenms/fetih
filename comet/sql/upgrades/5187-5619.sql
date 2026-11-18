USE `comet.account`;

-- Patch 5619 logs in with SRP6: the account server keeps a salt and verifier per
-- account instead of a password hash. Neither can be made from the old Password and
-- Salt columns, so those are dropped and each account's password has to be set again.
-- Account names become email addresses, so the name column is widened to the 127
-- bytes the client can send.

DROP TRIGGER IF EXISTS `account_passwordhash`;
DROP PROCEDURE IF EXISTS `upgrade_5187_5619`;
DELIMITER //

CREATE PROCEDURE `upgrade_5187_5619`()
BEGIN

  IF NOT EXISTS(
    SELECT NULL
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE table_name = 'account'
      AND table_schema = 'comet.account'
      AND column_name = 'SrpSalt')
  THEN
    ALTER TABLE `account`
      ADD COLUMN `SrpSalt` VARCHAR(64) NULL DEFAULT NULL AFTER `Username`;
  END IF;

  IF NOT EXISTS(
    SELECT NULL
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE table_name = 'account'
      AND table_schema = 'comet.account'
      AND column_name = 'SrpVerifier')
  THEN
    ALTER TABLE `account`
      ADD COLUMN `SrpVerifier` VARCHAR(64) NULL DEFAULT NULL AFTER `SrpSalt`;
  END IF;

  IF EXISTS(
    SELECT NULL
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE table_name = 'account'
      AND table_schema = 'comet.account'
      AND column_name = 'Password')
  THEN
    ALTER TABLE `account` DROP COLUMN `Password`;
  END IF;

  IF EXISTS(
    SELECT NULL
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE table_name = 'account'
      AND table_schema = 'comet.account'
      AND column_name = 'Salt')
  THEN
    ALTER TABLE `account` DROP COLUMN `Salt`;
  END IF;

END //

DELIMITER ;

ALTER TABLE `account` MODIFY COLUMN `Username` VARCHAR(127) CHARACTER SET utf8
  COLLATE utf8_general_ci NOT NULL;

CALL `upgrade_5187_5619`();
DROP PROCEDURE `upgrade_5187_5619`;

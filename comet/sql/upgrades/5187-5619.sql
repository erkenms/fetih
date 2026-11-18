USE `comet.account`;

-- Patch 5619 logs in with SRP6: the account server keeps a salt and verifier per
-- account instead of checking a password hash. Neither can be made from the old
-- Password/Salt columns, so set each password again with:
--   dotnet Comet.Account.dll set-password <username> <password>

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
      ADD COLUMN `SrpSalt` VARCHAR(64) NULL DEFAULT NULL AFTER `Salt`;
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

END //

DELIMITER ;

CALL `upgrade_5187_5619`();
DROP PROCEDURE `upgrade_5187_5619`;

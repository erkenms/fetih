{ pkgs, ... }:
{
  services.mysql = {
    enable = true;
    package = pkgs.mariadb;
    dataDir = "/var/lib/fetih/mysql";
    ensureDatabases = [ "fetih_account" "fetih_game" "fetih_game_test" ];
    settings.mysqld = {
      skip-networking = true;
      bind-address = "127.0.0.1";
      character-set-server = "utf8mb3";
      collation-server = "utf8mb3_bin";
    };
  };

  systemd.services.fetih-db-grants = {
    description = "Apply MariaDB unix_socket users, Grafana view, and realm addresses";
    after = [ "mysql.service" ];
    requires = [ "mysql.service" ];
    wantedBy = [ "multi-user.target" ];
    serviceConfig = {
      Type = "oneshot";
      RemainAfterExit = true;
    };
    script = ''
      ${pkgs.mariadb}/bin/mariadb <<'SQL'
        CREATE USER IF NOT EXISTS 'fetih-account'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES, EXECUTE, LOCK TABLES
          ON fetih_account.* TO 'fetih-account'@'localhost';

        CREATE USER IF NOT EXISTS 'fetih-prod-game'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES, EXECUTE, LOCK TABLES
          ON fetih_game.* TO 'fetih-prod-game'@'localhost';
        GRANT SELECT, UPDATE ON fetih_account.account TO 'fetih-prod-game'@'localhost';

        CREATE USER IF NOT EXISTS 'fetih-test-game'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES, EXECUTE, LOCK TABLES
          ON fetih_game_test.* TO 'fetih-test-game'@'localhost';
        GRANT SELECT, UPDATE ON fetih_account.account TO 'fetih-test-game'@'localhost';

        CREATE USER IF NOT EXISTS 'fetih-prod-web'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES
          ON fetih_game.* TO 'fetih-prod-web'@'localhost';
        GRANT SELECT, INSERT, UPDATE ON fetih_account.account TO 'fetih-prod-web'@'localhost';
        GRANT SELECT ON fetih_account.account_authority TO 'fetih-prod-web'@'localhost';
        GRANT SELECT ON fetih_account.account_status TO 'fetih-prod-web'@'localhost';
        GRANT SELECT ON fetih_account.realm TO 'fetih-prod-web'@'localhost';
        GRANT SELECT ON fetih_account.login TO 'fetih-prod-web'@'localhost';

        CREATE USER IF NOT EXISTS 'fetih-test-web'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES
          ON fetih_game_test.* TO 'fetih-test-web'@'localhost';
        GRANT SELECT, INSERT, UPDATE ON fetih_account.account TO 'fetih-test-web'@'localhost';
        GRANT SELECT ON fetih_account.account_authority TO 'fetih-test-web'@'localhost';
        GRANT SELECT ON fetih_account.account_status TO 'fetih-test-web'@'localhost';
        GRANT SELECT ON fetih_account.realm TO 'fetih-test-web'@'localhost';
        GRANT SELECT ON fetih_account.login TO 'fetih-test-web'@'localhost';

        CREATE USER IF NOT EXISTS 'fetih-backup'@'localhost' IDENTIFIED VIA unix_socket;
        GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT
          ON fetih_account.* TO 'fetih-backup'@'localhost';
        GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT
          ON fetih_game.* TO 'fetih-backup'@'localhost';

        CREATE USER IF NOT EXISTS 'grafana'@'localhost' IDENTIFIED VIA unix_socket;
        REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'grafana'@'localhost';
        FLUSH PRIVILEGES;
SQL

      # Apply Grafana views/grants and realm rows when schemas are populated
      if ${pkgs.mariadb}/bin/mariadb -Nse "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='fetih_account' AND table_name='account'" | grep -q '^1$'; then
        ${pkgs.mariadb}/bin/mariadb <<'SQL'
          CREATE OR REPLACE VIEW fetih_account.grafana_account AS
          SELECT AccountID, AuthorityID, StatusID, Registered
          FROM fetih_account.account;
          GRANT SELECT ON fetih_account.grafana_account TO 'grafana'@'localhost';

          UPDATE fetih_account.realm
          SET GameIPAddress = '163.5.53.47', GamePort = 5816, RpcIPAddress = '127.0.0.1', RpcPort = 5817
          WHERE Name = 'Altay';

          UPDATE fetih_account.realm
          SET GameIPAddress = '163.5.53.47', GamePort = 5826, RpcIPAddress = '127.0.0.1', RpcPort = 5827
          WHERE Name = 'Mihenk';
SQL
      fi

      for schema in fetih_game fetih_game_test; do
        if ${pkgs.mariadb}/bin/mariadb -Nse "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$schema' AND table_name='web_audit'" | grep -q '^1$'; then
          ${pkgs.mariadb}/bin/mariadb <<SQL
            GRANT SELECT ON $schema.\`character\` TO 'grafana'@'localhost';
            GRANT SELECT ON $schema.item TO 'grafana'@'localhost';
            GRANT SELECT ON $schema.syndicate TO 'grafana'@'localhost';
            GRANT SELECT ON $schema.detained_item TO 'grafana'@'localhost';
            GRANT SELECT ON $schema.web_audit TO 'grafana'@'localhost';
SQL
        fi
      done
    '';
  };
}

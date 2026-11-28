{ pkgs, lib, ... }:
let
  skiaLibPath = lib.makeLibraryPath [
    pkgs.stdenv.cc.cc.lib
    pkgs.zlib
    pkgs.fontconfig
    pkgs.freetype
  ];
  hardened = {
    NoNewPrivileges = true;
    ProtectSystem = "strict";
    ProtectHome = true;
    PrivateTmp = true;
    PrivateDevices = true;
    ProtectKernelTunables = true;
    ProtectKernelModules = true;
    ProtectControlGroups = true;
    RestrictSUIDSGID = true;
    LockPersonality = true;
    RestrictNamespaces = true;
    CapabilityBoundingSet = "";
    AmbientCapabilities = "";
    SystemCallArchitectures = "native";
  };
in
{
  users.groups = {
    fetih-logs = {};
    fetih-test = {};
    fetih-account = {};
    fetih-prod-game = {};
    fetih-prod-web = {};
    fetih-test-game = {};
    fetih-test-web = {};
    fetih-deploy = {};
    fetih-backup = {};
  };

  users.users = {
    fetih-account = {
      isSystemUser = true;
      group = "fetih-account";
      extraGroups = [ "fetih-logs" ];
    };
    fetih-prod-game = {
      isSystemUser = true;
      group = "fetih-prod-game";
      extraGroups = [ "fetih-logs" ];
    };
    fetih-prod-web = {
      isSystemUser = true;
      group = "fetih-prod-web";
      extraGroups = [ "fetih-logs" ];
    };
    fetih-test-game = {
      isSystemUser = true;
      group = "fetih-test-game";
      extraGroups = [ "fetih-logs" "fetih-test" ];
    };
    fetih-test-web = {
      isSystemUser = true;
      group = "fetih-test-web";
      extraGroups = [ "fetih-logs" "fetih-test" ];
    };
    fetih-deploy = {
      isSystemUser = true;
      group = "fetih-deploy";
      extraGroups = [ "fetih-test" ];
    };
    fetih-backup = {
      isSystemUser = true;
      group = "fetih-backup";
      extraGroups = [ "fetih-logs" ];
    };
  };

  systemd.services = {
    fetih-account = {
      description = "Fetih Account Server";
      after = [ "mysql.service" "fetih-db-grants.service" ];
      requires = [ "mysql.service" ];
      wantedBy = [ "multi-user.target" ];
      unitConfig = {
        ConditionPathExists = [
          "/var/lib/fetih/secrets/first-admin"
          "/var/lib/fetih/deploy/prod-account/bin/Fetih.Account"
        ];
      };
      environment = {
        FETIH_ACCOUNT_Database__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_ACCOUNT_Database__Schema = "fetih_account";
        FETIH_ACCOUNT_Database__Username = "fetih-account";
        FETIH_ACCOUNT_Database__Password = "";
        FETIH_ACCOUNT_Metrics__IPAddress = "127.0.0.1";
        FETIH_ACCOUNT_Metrics__Port = "9819";
        FETIH_ACCOUNT_Logging__Directory = "/var/lib/fetih/logs/shared/account";
        FETIH_ACCOUNT_Logging__Level = "info";
        FETIH_ACCOUNT_Logging__ConsoleLevel = "warn";
        FETIH_ACCOUNT_Logging__RetainDays = "14";
        FETIH_ACCOUNT_Logging__PacketTrace__All = "false";
        FETIH_ACCOUNT_FirstAdminFile = "/run/credentials/fetih-account.service/first-admin";
      };
      serviceConfig = hardened // {
        User = "fetih-account";
        Group = "fetih-account";
        SupplementaryGroups = [ "fetih-logs" ];
        ExecStart = "/var/lib/fetih/deploy/prod-account/bin/Fetih.Account";
        LoadCredential = "first-admin:/var/lib/fetih/secrets/first-admin";
        ReadWritePaths = [ "/var/lib/fetih/logs/shared/account" ];
        ReadOnlyPaths = [ "/run/mysqld" "/var/lib/fetih/deploy/prod-account" ];
        Restart = "always";
        RestartSec = "5s";
        MemoryMax = "384M";
      };
    };

    fetih-prod-game = {
      description = "Fetih Production Game Server (Altay)";
      after = [ "mysql.service" "fetih-db-grants.service" "fetih-account.service" ];
      requires = [ "mysql.service" ];
      wantedBy = [ "multi-user.target" ];
      unitConfig.ConditionPathExists = "/var/lib/fetih/deploy/prod-game/bin/Fetih.Game";
      environment = {
        FETIH_GAME_Database__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_GAME_Database__Schema = "fetih_game";
        FETIH_GAME_Database__Username = "fetih-prod-game";
        FETIH_GAME_Database__Password = "";
        FETIH_GAME_AccountDatabase__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_GAME_AccountDatabase__Schema = "fetih_account";
        FETIH_GAME_AccountDatabase__Username = "fetih-prod-game";
        FETIH_GAME_AccountDatabase__Password = "";
        FETIH_GAME_GameNetwork__IPAddress = "0.0.0.0";
        FETIH_GAME_GameNetwork__Port = "5816";
        FETIH_GAME_RpcNetwork__IPAddress = "127.0.0.1";
        FETIH_GAME_RpcNetwork__Port = "5817";
        FETIH_GAME_Metrics__IPAddress = "127.0.0.1";
        FETIH_GAME_Metrics__Port = "9818";
        FETIH_GAME_Maps__ClientDirectory = "/var/lib/fetih/client/built";
        FETIH_GAME_Data__Directory = "/var/lib/fetih/repos/prod/src/Fetih.Game/Data";
        FETIH_GAME_Logging__Directory = "/var/lib/fetih/logs/prod/game";
        FETIH_GAME_Logging__Level = "info";
        FETIH_GAME_Logging__ConsoleLevel = "warn";
        FETIH_GAME_Logging__RetainDays = "14";
        FETIH_GAME_Logging__PacketTrace__All = "false";
      };
      serviceConfig = hardened // {
        User = "fetih-prod-game";
        Group = "fetih-prod-game";
        SupplementaryGroups = [ "fetih-logs" ];
        ExecStart = "/var/lib/fetih/deploy/prod-game/bin/Fetih.Game";
        TimeoutStopSec = "30s";
        ReadWritePaths = [ "/var/lib/fetih/logs/prod/game" ];
        ReadOnlyPaths = [
          "/run/mysqld"
          "/var/lib/fetih/deploy/prod-game"
          "/var/lib/fetih/client/built"
          "/var/lib/fetih/repos/prod"
        ];
        Restart = "always";
        RestartSec = "5s";
        MemoryMax = "2560M";
      };
    };

    fetih-prod-web = {
      description = "Fetih Production Web Server";
      after = [ "mysql.service" "fetih-db-grants.service" ];
      requires = [ "mysql.service" ];
      wantedBy = [ "multi-user.target" ];
      unitConfig.ConditionPathExists = "/var/lib/fetih/deploy/prod-web/bin/Fetih.Web";
      environment = {
        FETIH_WEB_Database__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_WEB_Database__Schema = "fetih_game";
        FETIH_WEB_Database__Username = "fetih-prod-web";
        FETIH_WEB_Database__Password = "";
        FETIH_WEB_AccountDatabase__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_WEB_AccountDatabase__Schema = "fetih_account";
        FETIH_WEB_AccountDatabase__Username = "fetih-prod-web";
        FETIH_WEB_AccountDatabase__Password = "";
        FETIH_WEB_Web__Urls = "http://127.0.0.1:5820";
        FETIH_WEB_Web__SecureCookies = "true";
        FETIH_WEB_Web__ForwardedHeaders = "true";
        FETIH_WEB_Web__StaffOnly = "false";
        FETIH_WEB_Web__StudioReadOnly = "true";
        FETIH_WEB_GameRpc__Hostname = "127.0.0.1";
        FETIH_WEB_GameRpc__Port = "5817";
        FETIH_WEB_Observability__Environment = "prod";
        FETIH_WEB_Paths__DataDirectory = "/var/lib/fetih/repos/prod/src/Fetih.Game/Data";
        FETIH_WEB_Paths__ClientDirectory = "/var/lib/fetih/client/built";
        FETIH_WEB_Paths__AssetsDirectory = "/var/lib/fetih/client/assets";
        FETIH_WEB_Paths__KeysDirectory = "/var/lib/fetih/secrets/web-keys-prod";
        FETIH_WEB_Paths__TileCacheDirectory = "/var/lib/fetih/tiles/prod";
        FETIH_WEB_Paths__TileCacheMaxBytes = "734003200";
        FETIH_WEB_Logging__Directory = "/var/lib/fetih/logs/prod/web";
        FETIH_WEB_Logging__ConsoleLevel = "warn";
        FETIH_WEB_Logging__RetainDays = "14";
        LD_LIBRARY_PATH = skiaLibPath;
      };
      path = [ pkgs.git ];
      serviceConfig = hardened // {
        User = "fetih-prod-web";
        Group = "fetih-prod-web";
        SupplementaryGroups = [ "fetih-logs" ];
        ExecStart = "/var/lib/fetih/deploy/prod-web/bin/Fetih.Web";
        ReadWritePaths = [
          "/var/lib/fetih/logs/prod/web"
          "/var/lib/fetih/secrets/web-keys-prod"
          "/var/lib/fetih/tiles/prod"
        ];
        ReadOnlyPaths = [
          "/run/mysqld"
          "/var/lib/fetih/deploy/prod-web"
          "/var/lib/fetih/client/built"
          "/var/lib/fetih/client/assets"
          "/var/lib/fetih/repos/prod"
        ];
        Restart = "always";
        RestartSec = "5s";
        MemoryMax = "768M";
      };
    };

    fetih-test-game = {
      description = "Fetih Test Game Server (Mihenk)";
      after = [ "mysql.service" "fetih-db-grants.service" "fetih-account.service" ];
      requires = [ "mysql.service" ];
      wantedBy = [ "multi-user.target" ];
      unitConfig.ConditionPathExists = "/var/lib/fetih/deploy/test-game/bin/Fetih.Game";
      environment = {
        FETIH_GAME_Database__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_GAME_Database__Schema = "fetih_game_test";
        FETIH_GAME_Database__Username = "fetih-test-game";
        FETIH_GAME_Database__Password = "";
        FETIH_GAME_AccountDatabase__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_GAME_AccountDatabase__Schema = "fetih_account";
        FETIH_GAME_AccountDatabase__Username = "fetih-test-game";
        FETIH_GAME_AccountDatabase__Password = "";
        FETIH_GAME_GameNetwork__IPAddress = "0.0.0.0";
        FETIH_GAME_GameNetwork__Port = "5826";
        FETIH_GAME_RpcNetwork__IPAddress = "127.0.0.1";
        FETIH_GAME_RpcNetwork__Port = "5827";
        FETIH_GAME_Metrics__IPAddress = "127.0.0.1";
        FETIH_GAME_Metrics__Port = "9828";
        FETIH_GAME_Maps__ClientDirectory = "/var/lib/fetih/client/built";
        FETIH_GAME_Data__Directory = "/var/lib/fetih/studio/src/Fetih.Game/Data";
        FETIH_GAME_Logging__Directory = "/var/lib/fetih/logs/test/game";
        FETIH_GAME_Logging__Level = "info";
        FETIH_GAME_Logging__ConsoleLevel = "warn";
        FETIH_GAME_Logging__RetainDays = "3";
        FETIH_GAME_Logging__PacketTrace__All = "false";
      };
      serviceConfig = hardened // {
        User = "fetih-test-game";
        Group = "fetih-test-game";
        SupplementaryGroups = [ "fetih-logs" "fetih-test" ];
        ExecStart = "/var/lib/fetih/deploy/test-game/bin/Fetih.Game";
        TimeoutStopSec = "30s";
        ReadWritePaths = [ "/var/lib/fetih/logs/test/game" ];
        ReadOnlyPaths = [
          "/run/mysqld"
          "/var/lib/fetih/deploy/test-game"
          "/var/lib/fetih/client/built"
          "/var/lib/fetih/studio"
        ];
        Restart = "always";
        RestartSec = "5s";
        MemoryMax = "1024M";
      };
    };

    fetih-test-web = {
      description = "Fetih Test Web Server (Mihenk)";
      after = [ "mysql.service" "fetih-db-grants.service" ];
      requires = [ "mysql.service" ];
      wantedBy = [ "multi-user.target" ];
      unitConfig.ConditionPathExists = "/var/lib/fetih/deploy/test-web/bin/Fetih.Web";
      environment = {
        FETIH_WEB_Database__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_WEB_Database__Schema = "fetih_game_test";
        FETIH_WEB_Database__Username = "fetih-test-web";
        FETIH_WEB_Database__Password = "";
        FETIH_WEB_AccountDatabase__Hostname = "/run/mysqld/mysqld.sock";
        FETIH_WEB_AccountDatabase__Schema = "fetih_account";
        FETIH_WEB_AccountDatabase__Username = "fetih-test-web";
        FETIH_WEB_AccountDatabase__Password = "";
        FETIH_WEB_Web__Urls = "http://127.0.0.1:5821";
        FETIH_WEB_Web__SecureCookies = "true";
        FETIH_WEB_Web__ForwardedHeaders = "true";
        FETIH_WEB_Web__StaffOnly = "true";
        FETIH_WEB_Web__StudioReadOnly = "false";
        FETIH_WEB_GameRpc__Hostname = "127.0.0.1";
        FETIH_WEB_GameRpc__Port = "5827";
        FETIH_WEB_Observability__Environment = "test";
        FETIH_WEB_Paths__DataDirectory = "/var/lib/fetih/studio/src/Fetih.Game/Data";
        FETIH_WEB_Paths__ClientDirectory = "/var/lib/fetih/client/built";
        FETIH_WEB_Paths__AssetsDirectory = "/var/lib/fetih/client/assets";
        FETIH_WEB_Paths__KeysDirectory = "/var/lib/fetih/secrets/web-keys-test";
        FETIH_WEB_Paths__TileCacheDirectory = "/var/lib/fetih/tiles/test";
        FETIH_WEB_Paths__TileCacheMaxBytes = "314572800";
        FETIH_WEB_Logging__Directory = "/var/lib/fetih/logs/test/web";
        FETIH_WEB_Logging__ConsoleLevel = "warn";
        FETIH_WEB_Logging__RetainDays = "3";
        LD_LIBRARY_PATH = skiaLibPath;
      };
      path = [ pkgs.git ];
      serviceConfig = hardened // {
        User = "fetih-test-web";
        Group = "fetih-test-web";
        SupplementaryGroups = [ "fetih-logs" "fetih-test" ];
        ExecStart = "/var/lib/fetih/deploy/test-web/bin/Fetih.Web";
        ReadWritePaths = [
          "/var/lib/fetih/logs/test/web"
          "/var/lib/fetih/secrets/web-keys-test"
          "/var/lib/fetih/tiles/test"
          "/var/lib/fetih/studio"
        ];
        ReadOnlyPaths = [
          "/run/mysqld"
          "/var/lib/fetih/deploy/test-web"
          "/var/lib/fetih/client/built"
          "/var/lib/fetih/client/assets"
        ];
        Restart = "always";
        RestartSec = "5s";
        MemoryMax = "512M";
      };
    };
  };

  services.caddy = {
    enable = true;
    dataDir = "/var/lib/fetih/caddy";
    extraConfig = ''
      (patches) {
        handle /patch/objects/* {
          header Cache-Control "public, max-age=31536000, immutable"
          root * /var/lib/fetih/client
          file_server
        }
        handle /patch/manifests/* {
          encode zstd gzip
          header Cache-Control "public, max-age=31536000, immutable"
          root * /var/lib/fetih/client
          file_server
        }
        handle /patch/manifest.json {
          encode zstd gzip
          header Cache-Control "no-cache"
          root * /var/lib/fetih/client
          file_server
        }
        handle /download/Fetih.exe {
          header Cache-Control "no-cache"
          root * /var/lib/fetih/client/patch
          file_server
        }
      }

      (grafana) {
        redir /grafana /grafana/ permanent
        handle /grafana/* {
          forward_auth {args[0]} {
            uri /auth/check
            copy_headers X-Webauth-User
          }
          reverse_proxy 127.0.0.1:3000
        }
      }

      fetihoyun.com {
        import patches
        import grafana 127.0.0.1:5820
        handle {
          encode zstd gzip
          reverse_proxy 127.0.0.1:5820
        }
      }

      mihenk.fetihoyun.com {
        import patches
        import grafana 127.0.0.1:5821
        handle {
          encode zstd gzip
          reverse_proxy 127.0.0.1:5821
        }
      }
    '';
  };
}

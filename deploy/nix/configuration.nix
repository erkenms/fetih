{ pkgs, ... }:
{
  system.stateVersion = "25.05";

  networking.hostName = "fetih";
  time.timeZone = "Europe/Istanbul";

  nix = {
    settings.experimental-features = [ "nix-command" "flakes" ];
    gc = {
      automatic = true;
      dates = "weekly";
      options = "--delete-older-than 14d";
    };
  };

  services.openssh = {
    enable = true;
    settings = {
      PasswordAuthentication = false;
      PermitRootLogin = "prohibit-password";
    };
  };

  users.users.root.openssh.authorizedKeys.keys = [
    "sk-ecdsa-sha2-nistp256@openssh.com AAAAInNrLWVjZHNhLXNoYTItbmlzdHAyNTZAb3BlbnNzaC5jb20AAAAIbmlzdHAyNTYAAABBBCM7xI1lUjvBp2L6kB4e3smry9UzuaTFsxEjdIse6EcFY3eZLlNbrTGAFV+oysvh8GckVuNmAXswW0SJAermwlEAAAAEc3NoOg== erkenms@MacbookAir"
  ];

  networking.firewall = {
    enable = true;
    allowedTCPPorts = [ 22 80 443 9960 5816 5826 ];
    allowedUDPPorts = [ 443 ];
  };

  environment.systemPackages = with pkgs; [
    git
    jq
    rclone
    sqlite
    zstd
  ];

  programs.git = {
    enable = true;
    config.safe.directory = "*";
  };

  systemd.tmpfiles.rules = [
    "d /var/lib/fetih 0755 root root -"
    "d /var/lib/fetih/secrets 0711 root root -"
    "d /var/lib/fetih/secrets/web-keys-prod 0700 fetih-prod-web fetih-prod-web -"
    "d /var/lib/fetih/secrets/web-keys-test 0700 fetih-test-web fetih-test-web -"
    "d /var/lib/fetih/mysql 0750 mysql mysql -"
    "d /var/lib/fetih/grafana 0700 grafana grafana -"
    "d /var/lib/fetih/loki 0750 loki loki -"
    "d /var/lib/fetih/prometheus 0750 prometheus prometheus -"
    "d /var/lib/fetih/prometheus/textfile 0755 fetih-backup prometheus -"
    "d /var/lib/fetih/caddy 0750 caddy caddy -"
    "d /var/lib/fetih/client 0755 fetih-deploy root -"
    "d /var/lib/fetih/client/base 0755 fetih-deploy root -"
    "d /var/lib/fetih/client/built 0755 fetih-deploy root -"
    "d /var/lib/fetih/client/assets 0755 fetih-deploy root -"
    "d /var/lib/fetih/client/patch 0755 fetih-deploy root -"
    "d /var/lib/fetih/tiles 0755 root root -"
    "d /var/lib/fetih/tiles/prod 0750 fetih-prod-web fetih-prod-web -"
    "d /var/lib/fetih/tiles/test 0750 fetih-test-web fetih-test-web -"
    "d /var/lib/fetih/logs 0750 root fetih-logs -"
    "d /var/lib/fetih/logs/shared 0750 root fetih-logs -"
    "d /var/lib/fetih/logs/shared/account 0750 fetih-account fetih-logs -"
    "d /var/lib/fetih/logs/prod 0750 root fetih-logs -"
    "d /var/lib/fetih/logs/prod/game 0750 fetih-prod-game fetih-logs -"
    "d /var/lib/fetih/logs/prod/web 0750 fetih-prod-web fetih-logs -"
    "d /var/lib/fetih/logs/test 0750 root fetih-logs -"
    "d /var/lib/fetih/logs/test/game 0750 fetih-test-game fetih-logs -"
    "d /var/lib/fetih/logs/test/web 0750 fetih-test-web fetih-logs -"
    "d /var/lib/fetih/studio 0775 fetih-test-web fetih-test -"
    "d /var/lib/fetih/deploy 0755 fetih-deploy root -"
    "d /var/lib/fetih/repos 0755 fetih-deploy root -"
  ];

  systemd.services.fetih-host-secrets = {
    description = "Generate host-local secrets if missing";
    wantedBy = [ "multi-user.target" ];
    before = [ "grafana.service" ];
    serviceConfig = {
      Type = "oneshot";
      RemainAfterExit = true;
    };
    script = ''
      install -d -m 0711 -o root -g root /var/lib/fetih/secrets
      if [ ! -f /var/lib/fetih/secrets/grafana-admin-password ]; then
        tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32 > /var/lib/fetih/secrets/grafana-admin-password.tmp
        chmod 0400 /var/lib/fetih/secrets/grafana-admin-password.tmp
        mv /var/lib/fetih/secrets/grafana-admin-password.tmp /var/lib/fetih/secrets/grafana-admin-password
      fi
      if [ ! -f /var/lib/fetih/secrets/grafana-secret-key ]; then
        tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 32 > /var/lib/fetih/secrets/grafana-secret-key.tmp
        chmod 0400 /var/lib/fetih/secrets/grafana-secret-key.tmp
        mv /var/lib/fetih/secrets/grafana-secret-key.tmp /var/lib/fetih/secrets/grafana-secret-key
      fi
      if [ ! -f /var/lib/fetih/secrets/discord-webhook ]; then
        printf 'http://127.0.0.1:9/unconfigured\n' > /var/lib/fetih/secrets/discord-webhook.tmp
        chmod 0400 /var/lib/fetih/secrets/discord-webhook.tmp
        mv /var/lib/fetih/secrets/discord-webhook.tmp /var/lib/fetih/secrets/discord-webhook
      fi
    '';
  };
}

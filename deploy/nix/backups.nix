{ pkgs, ... }:
let
  r2 = import ./r2.nix;
  rcloneScript = ''
    set -euo pipefail
    source /run/credentials/$1/r2
    export R2_ENDPOINT="${r2.endpoint}"
    export R2_BUCKET="${r2.bucket}"
    export RCLONE_CONFIG_R2_TYPE=s3
    export RCLONE_CONFIG_R2_PROVIDER=Cloudflare
    export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
    export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
    export RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT"
    export RCLONE_CONFIG_R2_REGION=auto
    export RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
  '';
in
{
  systemd.services.fetih-backup-hourly = {
    description = "Hourly MariaDB backup to R2";
    after = [ "mysql.service" ];
    requires = [ "mysql.service" ];
    unitConfig.ConditionPathExists = "/var/lib/fetih/secrets/r2.env";
    serviceConfig = {
      Type = "oneshot";
      User = "fetih-backup";
      Group = "fetih-backup";
      LoadCredential = "r2:/var/lib/fetih/secrets/r2.env";
      ReadWritePaths = [ "/var/lib/fetih/prometheus/textfile" ];
      ReadOnlyPaths = [ "/run/mysqld" ];
      PrivateTmp = true;
    };
    script = ''
      ${rcloneScript} fetih-backup-hourly.service
      stamp="$(date +%Y-%m-%d-%H%M)"
      tmp="$(mktemp -d)"
      trap 'rm -rf "$tmp"' EXIT

      ${pkgs.mariadb}/bin/mariadb-dump --single-transaction --databases fetih_account fetih_game \
        | ${pkgs.gzip}/bin/gzip -c > "$tmp/$stamp.sql.gz"
      ${pkgs.rclone}/bin/rclone copyto "$tmp/$stamp.sql.gz" "r2:$R2_BUCKET/backups/hourly/$stamp.sql.gz"

      prom="/var/lib/fetih/prometheus/textfile/backup.prom"
      printf 'fetih_backup_last_success_timestamp_seconds %s\n' "$(date +%s)" > "$prom.tmp"
      mv "$prom.tmp" "$prom"
    '';
  };

  systemd.timers.fetih-backup-hourly = {
    wantedBy = [ "timers.target" ];
    timerConfig = {
      OnCalendar = "hourly";
      Persistent = true;
      RandomizedDelaySec = "2m";
    };
  };

  systemd.services.fetih-backup-daily = {
    description = "Daily MariaDB, Grafana, and audit log backup to R2";
    after = [ "mysql.service" ];
    requires = [ "mysql.service" ];
    unitConfig.ConditionPathExists = "/var/lib/fetih/secrets/r2.env";
    serviceConfig = {
      Type = "oneshot";
      User = "root";
      LoadCredential = "r2:/var/lib/fetih/secrets/r2.env";
      ReadWritePaths = [ "/var/lib/fetih/prometheus/textfile" ];
      PrivateTmp = true;
    };
    script = ''
      ${rcloneScript} fetih-backup-daily.service
      day="$(date +%Y-%m-%d)"
      tmp="$(mktemp -d)"
      trap 'rm -rf "$tmp"' EXIT

      ${pkgs.mariadb}/bin/mariadb-dump --single-transaction --databases fetih_account fetih_game \
        | ${pkgs.gzip}/bin/gzip -c > "$tmp/db.sql.gz"

      if [ -f /var/lib/fetih/grafana/grafana.db ]; then
        ${pkgs.sqlite}/bin/sqlite3 /var/lib/fetih/grafana/grafana.db ".backup '$tmp/grafana.db'"
        ${pkgs.gzip}/bin/gzip -c "$tmp/grafana.db" > "$tmp/grafana.db.gz"
        rm -f "$tmp/grafana.db"
      fi

      mkdir -p "$tmp/audit"
      find /var/lib/fetih/logs -type f -name '*-audit-*.jsonl' -exec cp --parents {} "$tmp/audit/" \; || true
      ${pkgs.gnutar}/bin/tar -C "$tmp/audit" -cf - . | ${pkgs.zstd}/bin/zstd -q -o "$tmp/audit.tar.zst"
      rm -rf "$tmp/audit"

      ${pkgs.rclone}/bin/rclone copy "$tmp/" "r2:$R2_BUCKET/backups/daily/$day/"

      prom="/var/lib/fetih/prometheus/textfile/backup.prom"
      printf 'fetih_backup_last_success_timestamp_seconds %s\n' "$(date +%s)" > "$prom.tmp"
      chown fetih-backup:prometheus "$prom.tmp"
      mv "$prom.tmp" "$prom"
    '';
  };

  systemd.timers.fetih-backup-daily = {
    wantedBy = [ "timers.target" ];
    timerConfig = {
      OnCalendar = "*-*-* 04:00:00";
      Persistent = true;
    };
  };
}

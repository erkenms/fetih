#!/usr/bin/env bash
set -euo pipefail

DB_PASSWORD="${DB_PASSWORD:-password}"
GRAFANA_DB_PASSWORD="${GRAFANA_DB_PASSWORD:-grafana}"

run_sql() {
  if [[ -n "${MARIADB_CMD:-}" ]]; then
    $MARIADB_CMD "$@"
  elif [[ $# -gt 0 ]]; then
    podman exec fetih-db mariadb -uroot -p"${DB_PASSWORD}" "$@"
  else
    podman exec -i fetih-db mariadb -uroot -p"${DB_PASSWORD}"
  fi
}

has_test=$(run_sql -Nse "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name='fetih_game_test'" 2>/dev/null || echo 0)

run_sql <<SQL
CREATE USER IF NOT EXISTS 'grafana'@'%' IDENTIFIED BY '${GRAFANA_DB_PASSWORD}';
ALTER USER 'grafana'@'%' IDENTIFIED BY '${GRAFANA_DB_PASSWORD}';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'grafana'@'%';

CREATE OR REPLACE VIEW fetih_account.grafana_account AS
SELECT AccountID, AuthorityID, StatusID, Registered
FROM fetih_account.account;

GRANT SELECT ON fetih_account.grafana_account TO 'grafana'@'%';
GRANT SELECT ON fetih_game.\`character\` TO 'grafana'@'%';
GRANT SELECT ON fetih_game.item TO 'grafana'@'%';
GRANT SELECT ON fetih_game.syndicate TO 'grafana'@'%';
GRANT SELECT ON fetih_game.detained_item TO 'grafana'@'%';
GRANT SELECT ON fetih_game.web_audit TO 'grafana'@'%';
SQL

if [[ "${has_test}" == "1" ]]; then
  run_sql <<SQL
GRANT SELECT ON fetih_game_test.\`character\` TO 'grafana'@'%';
GRANT SELECT ON fetih_game_test.item TO 'grafana'@'%';
GRANT SELECT ON fetih_game_test.syndicate TO 'grafana'@'%';
GRANT SELECT ON fetih_game_test.detained_item TO 'grafana'@'%';
GRANT SELECT ON fetih_game_test.web_audit TO 'grafana'@'%';
SQL
fi

run_sql -e "FLUSH PRIVILEGES;"
echo "Grafana read-only MariaDB user configured."

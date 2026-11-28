#!/usr/bin/env bash
set -euo pipefail

# Copies local secret files from ~/.fetih-secrets (or $2) to /var/lib/fetih/secrets/ on the host
# and restarts the services that consume them.
# Usage: deploy/secrets.sh <host> [secrets-dir]

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <host> [secrets-dir]" >&2
  exit 2
fi

HOST="$1"
SECRETS_DIR="${2:-${HOME}/.fetih-secrets}"

if [[ ! -d "$SECRETS_DIR" ]]; then
  echo "Secrets directory not found: $SECRETS_DIR" >&2
  exit 1
fi

RESTART=()

copy_secret() {
  local name="$1"
  shift
  local path="${SECRETS_DIR}/${name}"
  if [[ ! -f "$path" ]]; then
    echo "Skipping missing secret: $path"
    return
  fi
  echo "Installing $name -> root@${HOST}:/var/lib/fetih/secrets/${name}"
  ssh "root@${HOST}" "install -d -m 0700 -o root -g root /var/lib/fetih/secrets && cat > /var/lib/fetih/secrets/${name}.tmp && chmod 0400 /var/lib/fetih/secrets/${name}.tmp && chown root:root /var/lib/fetih/secrets/${name}.tmp && mv /var/lib/fetih/secrets/${name}.tmp /var/lib/fetih/secrets/${name}" < "$path"
  for svc in "$@"; do
    RESTART+=("$svc")
  done
}

copy_secret "r2.env" "fetih-client-base.service" "fetih-deploy.service"
copy_secret "discord-webhook" "grafana.service"
copy_secret "first-admin" "fetih-account.service"

if [[ ${#RESTART[@]} -gt 0 ]]; then
  # Deduplicate while preserving order
  UNIQUE=$(printf '%s\n' "${RESTART[@]}" | awk '!seen[$0]++' | tr '\n' ' ')
  echo "Restarting: $UNIQUE"
  ssh "root@${HOST}" "systemctl restart --no-block $UNIQUE"
fi

echo "Done."

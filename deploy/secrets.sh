#!/usr/bin/env bash
set -euo pipefail

# Prompts for secrets (hidden input, nothing written to disk on this Mac), streams them over
# SSH to /var/lib/fetih/secrets/ on the host (root:root, 0400), and restarts their services.
# Usage: deploy/secrets.sh <host> [r2|discord|admin|all]

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <host> [r2|discord|admin|all]" >&2
  exit 2
fi

HOST="$1"
TARGET="${2:-all}"
RESTART=()

case "$TARGET" in
  r2|discord|admin|all) ;;
  *) echo "Unknown target: $TARGET (expected r2, discord, admin, or all)" >&2; exit 2 ;;
esac

install_secret() {
  local name="$1"
  local content="$2"
  shift 2
  echo "Installing $name -> root@${HOST}:/var/lib/fetih/secrets/${name}"
  printf '%s' "$content" | ssh "root@${HOST}" \
    "install -d -m 0700 -o root -g root /var/lib/fetih/secrets && cat > /var/lib/fetih/secrets/${name}.tmp && chmod 0400 /var/lib/fetih/secrets/${name}.tmp && chown root:root /var/lib/fetih/secrets/${name}.tmp && mv /var/lib/fetih/secrets/${name}.tmp /var/lib/fetih/secrets/${name}"
  for svc in "$@"; do
    RESTART+=("$svc")
  done
}

prompt_hidden() {
  local label="$1"
  local val=""
  read -rsp "${label}: " val </dev/tty
  echo >&2
  printf '%s' "$val"
}

if [[ "$TARGET" == "all" || "$TARGET" == "r2" ]]; then
  R2_ACCESS_KEY_ID=$(prompt_hidden "R2 Access Key ID (leave empty to skip)" | tr -d '[:space:]')
  if [[ -n "$R2_ACCESS_KEY_ID" ]]; then
    R2_SECRET_ACCESS_KEY=$(prompt_hidden "R2 Secret Access Key" | tr -d '[:space:]')
    if [[ -z "$R2_SECRET_ACCESS_KEY" ]]; then
      echo "R2 Secret Access Key cannot be empty." >&2
      exit 1
    fi
    install_secret "r2.env" "R2_ACCESS_KEY_ID=${R2_ACCESS_KEY_ID}"$'\n'"R2_SECRET_ACCESS_KEY=${R2_SECRET_ACCESS_KEY}"$'\n' \
      "fetih-client-base.service" "fetih-deploy.service"
  fi
fi

if [[ "$TARGET" == "all" || "$TARGET" == "discord" ]]; then
  DISCORD_WEBHOOK=$(prompt_hidden "Discord webhook URL (leave empty to skip)")
  if [[ -n "$DISCORD_WEBHOOK" ]]; then
    install_secret "discord-webhook" "${DISCORD_WEBHOOK}"$'\n' "grafana.service"
  fi
fi

if [[ "$TARGET" == "all" || "$TARGET" == "admin" ]]; then
  read -rp "First admin email (leave empty to skip): " ADMIN_EMAIL </dev/tty
  if [[ -n "$ADMIN_EMAIL" ]]; then
    ADMIN_PASS=$(prompt_hidden "First admin temporary password")
    if [[ -z "$ADMIN_PASS" ]]; then
      echo "Password cannot be empty." >&2
      exit 1
    fi
    install_secret "first-admin" "${ADMIN_EMAIL}"$'\n'"${ADMIN_PASS}"$'\n' "fetih-account.service"
  fi
fi

if [[ ${#RESTART[@]} -gt 0 ]]; then
  UNIQUE=$(printf '%s\n' "${RESTART[@]}" | awk '!seen[$0]++' | tr '\n' ' ')
  echo "Restarting: $UNIQUE"
  ssh "root@${HOST}" "systemctl restart --no-block $UNIQUE"
fi

echo "Done."

#!/usr/bin/env bash
set -euo pipefail

# Packages _CLIENT/co_5619 and _CLIENT/fo_3105 into deterministic .tar.zst archives (reusing
# cached archives if their SHA-256 already matches deploy/nix/client-base.nix) and uploads to R2.
# Usage: deploy/upload-client-base.sh

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${HOME}/.cache/fetih-client-base"
NIX_PINS="${ROOT}/deploy/nix/client-base.nix"
R2_ENDPOINT="${R2_ENDPOINT:-https://c5d0a890b5bb2397e192cef5873bdd90.r2.cloudflarestorage.com}"
R2_BUCKET="${R2_BUCKET:-fetih}"

if [[ -z "${R2_ACCESS_KEY_ID:-}" ]]; then
  read -rsp "R2 Access Key ID: " R2_ACCESS_KEY_ID </dev/tty
  echo >&2
fi
if [[ -z "${R2_SECRET_ACCESS_KEY:-}" ]]; then
  read -rsp "R2 Secret Access Key: " R2_SECRET_ACCESS_KEY </dev/tty
  echo >&2
fi

: "${R2_ACCESS_KEY_ID:?R2 Access Key ID is required}"
: "${R2_SECRET_ACCESS_KEY:?R2 Secret Access Key is required}"
R2_ACCESS_KEY_ID=$(printf '%s' "$R2_ACCESS_KEY_ID" | tr -d '[:space:]')
R2_SECRET_ACCESS_KEY=$(printf '%s' "$R2_SECRET_ACCESS_KEY" | tr -d '[:space:]')

mkdir -p "$OUT_DIR"

expected_sha() {
  local name="$1"
  grep "${name}" "$NIX_PINS" | sed -E 's/.*"([0-9a-f]{64})".*/\1/'
}

pack_dir() {
  local name="$1"
  local src="${ROOT}/_CLIENT/${name}"
  local out="${OUT_DIR}/${name}.tar.zst"
  local expected
  expected=$(expected_sha "$name")

  if [[ -f "$out" && -n "$expected" ]]; then
    local actual
    actual=$(shasum -a 256 "$out" | awk '{print $1}')
    if [[ "$actual" == "$expected" ]]; then
      echo "Reusing cached ${name}.tar.zst (sha256 matches client-base.nix)"
      return
    fi
  fi

  if [[ ! -d "$src" ]]; then
    echo "Missing client directory: $src" >&2
    exit 1
  fi

  echo "Packing ${name}.tar.zst..."
  podman run --rm \
    -v "${src}:/src:ro" \
    -v "${OUT_DIR}:/out" \
    docker.io/library/alpine:latest \
    sh -c "apk add --no-cache tar zstd >/dev/null && tar --sort=name --mtime='UTC 2020-01-01' --owner=0 --group=0 --numeric-owner -C /src -cf - . | zstd -f -T0 -9 -q -o /out/${name}.tar.zst"

  local actual
  actual=$(shasum -a 256 "$out" | awk '{print $1}')
  echo "${name}.tar.zst sha256: $actual"
  if [[ -n "$expected" && "$actual" != "$expected" ]]; then
    echo "ERROR: SHA-256 does not match $NIX_PINS ($expected)!" >&2
    exit 1
  fi
}

pack_dir "co_5619"
pack_dir "fo_3105"

upload_file() {
  local name="$1"
  echo "Uploading ${name}.tar.zst to R2 bucket ${R2_BUCKET}..."
  if command -v rclone >/dev/null 2>&1; then
    RCLONE_CONFIG_R2_TYPE=s3 \
    RCLONE_CONFIG_R2_PROVIDER=Cloudflare \
    RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
    RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
    RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT" \
    RCLONE_CONFIG_R2_REGION=auto \
    RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true \
    rclone copyto "${OUT_DIR}/${name}.tar.zst" "r2:${R2_BUCKET}/client-base/${name}.tar.zst" --progress
  else
    podman run --rm \
      -v "${OUT_DIR}:/data:ro" \
      -e RCLONE_CONFIG_R2_TYPE=s3 \
      -e RCLONE_CONFIG_R2_PROVIDER=Cloudflare \
      -e RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
      -e RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
      -e RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT" \
      -e RCLONE_CONFIG_R2_REGION=auto \
      -e RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true \
      docker.io/rclone/rclone:latest \
      copyto "/data/${name}.tar.zst" "r2:${R2_BUCKET}/client-base/${name}.tar.zst" --progress
  fi
}

upload_file "co_5619"
upload_file "fo_3105"
echo "Client base archives uploaded."

#!/usr/bin/env bash
set -euo pipefail

# Packages _CLIENT/co_5619 and _CLIENT/fo_3105 into deterministic .tar.zst archives,
# verifies their SHA-256 against deploy/nix/client-base.nix, and uploads them to R2.
# Usage: deploy/upload-client-base.sh [r2.env]

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
R2_ENV="${1:-${HOME}/.fetih-secrets/r2.env}"
OUT_DIR="${HOME}/.cache/fetih-client-base"
NIX_PINS="${ROOT}/deploy/nix/client-base.nix"

if [[ ! -f "$R2_ENV" ]]; then
  echo "Missing r2.env at $R2_ENV" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "$R2_ENV"
set +a

R2_ENDPOINT="${R2_ENDPOINT:-https://c5d0a890b5bb2397e192cef5873bdd90.r2.cloudflarestorage.com}"
R2_BUCKET="${R2_BUCKET:-fetih}"
: "${R2_ACCESS_KEY_ID:?Set R2_ACCESS_KEY_ID in $R2_ENV}"
: "${R2_SECRET_ACCESS_KEY:?Set R2_SECRET_ACCESS_KEY in $R2_ENV}"

mkdir -p "$OUT_DIR"

pack_dir() {
  local name="$1"
  local src="${ROOT}/_CLIENT/${name}"
  local out="${OUT_DIR}/${name}.tar.zst"
  if [[ ! -d "$src" ]]; then
    echo "Missing client directory: $src" >&2
    exit 1
  fi
  echo "Packing ${name}.tar.zst..."
  # Use GNU tar + zstd inside Linux container for deterministic uid/gid/mtime/sort across macOS & Linux
  podman run --rm \
    -v "${src}:/src:ro" \
    -v "${OUT_DIR}:/out" \
    docker.io/library/alpine:latest \
    sh -c "apk add --no-cache tar zstd >/dev/null && tar --sort=name --mtime='UTC 2020-01-01' --owner=0 --group=0 --numeric-owner -C /src -cf - . | zstd -f -T0 -9 -q -o /out/${name}.tar.zst"
}

pack_dir "co_5619"
pack_dir "fo_3105"

CO_SHA=$(shasum -a 256 "${OUT_DIR}/co_5619.tar.zst" | awk '{print $1}')
FO_SHA=$(shasum -a 256 "${OUT_DIR}/fo_3105.tar.zst" | awk '{print $1}')

echo "co_5619.tar.zst sha256: $CO_SHA"
echo "fo_3105.tar.zst sha256: $FO_SHA"

if [[ -f "$NIX_PINS" ]]; then
  if ! grep -q "$CO_SHA" "$NIX_PINS" || ! grep -q "$FO_SHA" "$NIX_PINS"; then
    echo "WARNING: SHA-256 does not match $NIX_PINS!" >&2
    echo "Update deploy/nix/client-base.nix if you intentionally changed _CLIENT/co_5619 or _CLIENT/fo_3105." >&2
    exit 1
  fi
fi

upload_file() {
  local name="$1"
  echo "Uploading ${name}.tar.zst to R2 bucket ${R2_BUCKET}..."
  if command -v rclone >/dev/null 2>&1; then
    RCLONE_CONFIG_R2_TYPE=s3 \
    RCLONE_CONFIG_R2_PROVIDER=Cloudflare \
    RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
    RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
    RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT" \
    RCLONE_CONFIG_R2_ACL=private \
    rclone copyto "${OUT_DIR}/${name}.tar.zst" "r2:${R2_BUCKET}/client-base/${name}.tar.zst" --progress
  else
    podman run --rm \
      -v "${OUT_DIR}:/data:ro" \
      -e RCLONE_CONFIG_R2_TYPE=s3 \
      -e RCLONE_CONFIG_R2_PROVIDER=Cloudflare \
      -e RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" \
      -e RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY" \
      -e RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT" \
      -e RCLONE_CONFIG_R2_ACL=private \
      docker.io/rclone/rclone:latest \
      copyto "/data/${name}.tar.zst" "r2:${R2_BUCKET}/client-base/${name}.tar.zst" --progress
  fi
}

upload_file "co_5619"
upload_file "fo_3105"
echo "Client base archives uploaded."

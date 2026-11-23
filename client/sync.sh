#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/_CLIENT/fetih/"
SHARE="smb://GUEST@172.16.60.128/Users"
MOUNT="/Volumes/Users"
DST="$MOUNT/Public/Downloads/fetih/"

if [ ! -d "$SRC" ]; then
    echo "No built client at $SRC (run: dotnet run client/build.cs)" >&2
    exit 1
fi

if [ ! -d "$MOUNT" ]; then
    osascript -e "mount volume \"$SHARE\"" >/dev/null
fi

mkdir -p "$DST"

# SMB/Windows doesn't keep POSIX perms/owners, rejects dir mtime updates (-O), and rounds mtimes
exec rsync -rtvO --delete --modify-window=2 --exclude='.DS_Store' "$@" "$SRC" "$DST"

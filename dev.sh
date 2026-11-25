#!/usr/bin/env bash
# Starts the whole development stack in one terminal: the database, logs and metrics in
# Podman, then the account, game and web servers and the Vite frontend, their output
# prefixed by name. Ctrl+C stops the servers; the containers keep running
# (podman compose down stops them).
set -euo pipefail
cd "$(dirname "$0")"

for port in 9960 5816 5820 5173; do
    if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
        echo "Port $port is already in use: a server is still running." >&2
        exit 1
    fi
done

if [[ "$(uname)" == Darwin ]] && ! podman machine inspect --format '{{.State}}' 2>/dev/null | grep -q running; then
    podman machine start
fi
podman compose up -d

printf 'Waiting for the database'
until [[ "$(podman inspect --format '{{.State.Health.Status}}' fetih-db 2>/dev/null)" == healthy ]]; do
    printf '.'
    sleep 1
done
echo

dotnet build comet/Comet.slnx --nologo -v quiet -clp:ErrorsOnly
[[ -d web/node_modules ]] || npm --prefix web ci

pids=()
stop() {
    trap - INT TERM EXIT
    echo
    echo "Stopping the servers..."
    for pid in "${pids[@]}"; do
        pkill -TERM -P "$pid" 2>/dev/null || true
        kill -TERM "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
}
trap stop INT TERM EXIT

# name, color, directory, command...
run() {
    local name=$1 color=$2 dir=$3
    shift 3
    (cd "$dir" && exec "$@") > >(while IFS= read -r line; do
        printf '\033[%sm%-7s\033[0m %s\n' "$color" "$name" "$line"
    done) 2>&1 &
    pids+=($!)
}

run account 36 comet/src/Comet.Account dotnet run --no-build
run game    32 comet/src/Comet.Game    dotnet run --no-build
run web     35 comet/src/Comet.Web     dotnet run --no-build
run vite    33 web                     npm run dev

echo "Web: http://localhost:5173   Grafana: http://localhost:3000   Ctrl+C to stop"
wait

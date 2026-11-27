#!/usr/bin/env sh
# Run tools/build.cs inside the .NET 11 SDK container (for hosts without the .NET SDK).
# Example: FETIH_ACCOUNT_IP=1.2.3.4 FETIH_URL=https://example.com ./tools/build.sh all --env test
set -eu

repo="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
runtime="${CONTAINER_RUNTIME:-$(command -v podman || command -v docker)}"
srv_dir="${FETIH_SRV_DIR:-/srv/fetih}"
nuget_vol="${FETIH_NUGET_VOLUME:-fetih-nuget}"

exec "$runtime" run --rm \
    -v "$repo:/work" \
    -v "$srv_dir:/srv/fetih" \
    -v "$nuget_vol:/root/.nuget/packages" \
    -w /work \
    -e "FETIH_ACCOUNT_IP=${FETIH_ACCOUNT_IP:-}" \
    -e "FETIH_URL=${FETIH_URL:-}" \
    mcr.microsoft.com/dotnet/sdk:11.0 \
    dotnet run /work/tools/build.cs "$@"

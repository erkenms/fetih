# The account, game and web servers, built from the repository root:
#
#   podman build --target game .
#
# compose.yaml builds all three (profile deploy). The client files, the game's Data
# directory and the web's client art are mounted, not baked in.

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY comet/src/Comet.Core/Comet.Core.csproj comet/src/Comet.Core/
COPY comet/src/Comet.Network/Comet.Network.csproj comet/src/Comet.Network/
COPY comet/src/Comet.Shared/Comet.Shared.csproj comet/src/Comet.Shared/
COPY comet/src/Comet.Account/Comet.Account.csproj comet/src/Comet.Account/
COPY comet/src/Comet.Game/Comet.Game.csproj comet/src/Comet.Game/
COPY comet/src/Comet.Web/Comet.Web.csproj comet/src/Comet.Web/
RUN dotnet restore comet/src/Comet.Web/Comet.Web.csproj
COPY comet/src/ comet/src/
RUN dotnet publish comet/src/Comet.Account -c Release --no-restore -o /out/account \
 && dotnet publish comet/src/Comet.Game -c Release --no-restore -o /out/game \
 && dotnet publish comet/src/Comet.Web -c Release --no-restore -o /out/web

FROM docker.io/library/node:24-alpine AS frontend
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/runtime:11.0 AS account
WORKDIR /app
COPY --from=build /out/account .
ENTRYPOINT ["dotnet", "Comet.Account.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS game
WORKDIR /app
COPY --from=build /out/game .
ENTRYPOINT ["dotnet", "Comet.Game.dll"]

# git for the Studio's Changes page, which diffs the mounted checkout's Data directory
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS web
RUN apt-get update && apt-get install -y --no-install-recommends git \
 && rm -rf /var/lib/apt/lists/* \
 && git config --system --add safe.directory '*'
WORKDIR /app
COPY --from=build /out/web .
COPY --from=frontend /src/web/dist ./wwwroot
ENTRYPOINT ["dotnet", "Comet.Web.dll"]

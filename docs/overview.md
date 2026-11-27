# Overview

Conquer Online server for the 5619 client.

| Where | What |
|---|---|
| `src/` | Servers: `Fetih.Account` (login, `:9960`), `Fetih.Game` (world, `:5816`, RPC `:5817`), `Fetih.Web` (site and admin, `:5820`). Plus libraries and the test client. |
| `tests/` | .NET tests |
| `client/`, `tools/build.cs` | Build the playable client, web assets, launcher patches |
| `sql/` | Schemas and `migrations/` (applied at startup) |
| `deploy/`, `observability/` | Containers, Caddy; Loki, Prometheus, Grafana |

## Run

```bash
podman compose -f deploy/compose.yaml up -d db
dotnet test Fetih.slnx
cd src/Fetih.Game && dotnet run              # likewise Account, Web
dotnet run tools/build.cs all --ip <ip>      # client, web assets, patches
```

Settings: `.config` files, or env like `FETIH_GAME_Database__Hostname=db`.

## Database

- Schema change → new `sql/migrations/<game|account>/NNNN-name.sql`. Never edit an applied one
  or the baselines.
- Never pipe `.sql` into the DB by hand (it wiped every account once). Compare schemas with
  `SHOW CREATE TABLE`.

## How the game behaves

Strongest first:

1. Help pages: `~/Developer/co_help_files/` (`INDEX.md`). Not in this repo.
2. Strings: `iconv -f GBK -t UTF-8 _CLIENT/5619/ini/Cn_Res.ini | grep -i <word>`
3. Data files: `_CLIENT/5619/ini/`. If one holds the numbers, read it (like `Shop.dat`).
4. Binaries: [client-notes.md](client-notes.md)
5. Other emulators, ideas only: `~/Developer/Albetros1.1`, `~/Developer/worldconqueronline`

5619 does most item and NPC features through NPC dialogs, not later-patch windows.

## More

- [deploy.md](deploy.md): public server
- [web-style.md](web-style.md): the site's look
- [web.md](web.md): how a web page is built (Razor Pages)
- Feature backlog: issues ([NPCs](https://github.com/erkenms/fetih/issues/105), [quests](https://github.com/erkenms/fetih/issues/106))

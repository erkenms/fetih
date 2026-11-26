# Deploying a public server

`deploy/compose.yaml` runs the shared infrastructure on one machine: MariaDB, Loki, Alloy,
Prometheus and Grafana, plus (under `--profile deploy`) the shared account server and the
Caddy HTTPS proxy. Each environment (`prod` and `test`) runs its own `game` and `web`
containers from `deploy/compose.env.yaml` as separate compose projects (`fetih-prod` and
`fetih-test`) on the shared `fetih` network.

| Service | Reachable from | Port |
|---|---|---|
| `proxy` (Caddy) | the internet | 80, 443 (TCP and UDP) |
| `account` | the internet | 9960 |
| `fetih-prod-game` (`Altay`) | the internet | 5816; RPC 5817 and metrics 9818 only on `fetih` |
| `fetih-test-game` (`Mihenk`) | the internet | 5826 (maps to container 5816); RPC 5817 and metrics 9818 only on `fetih` |
| `fetih-prod-web`, `fetih-test-web` | the proxy only | 5820 on `fetih` |
| `db`, `grafana`, `loki`, `prometheus` | this machine | 127.0.0.1: 3306, 3000, 3100, 9090 |

## What the machine needs

- Linux with **rootful** Podman 5 or later (`sudo podman compose`), or Docker. Rootless
  Podman forwards published ports through a proxy that hides the sender, so every player
  would show up with the same internal address: per-IP connection limits, bans and the
  admin pages' IP columns would all go wrong.
- Separate checkouts for each environment (e.g. `/srv/fetih/prod` and `/srv/fetih/test`).
  Each environment's web container mounts its own checkout: Studio edits on `test` land in
  its `src/Fetih.Game/Data`, and its Changes page diffs them with git. Production
  runs with `STUDIO_READ_ONLY=true` so deploys aren't blocked by uncommitted edits.
- Domains whose A records point at the machine (`<domain>` and `test.<domain>`), for TLS
  certificates.
- The firewall open for **80, 443 (TCP and UDP), 5816, 5826 and 9960**, and nothing else.

## Building the client and web assets

`tools/build.cs` builds the client and web assets from the original 5619 base client.
`Server.dat` is generated from `client/servers.json` (which lists both realms: `Altay` for
production and `Mihenk` for test, staff only) using `--ip` or `FETIH_ACCOUNT_IP` (an IPv4
address; `ServerIP` holds at most 15 characters).

On a machine with the .NET 11 SDK:

```bash
dotnet run tools/build.cs all --ip <public IP>              # local: _CLIENT/5619 -> _CLIENT/fetih, web/assets
dotnet run tools/build.cs all --env prod --ip <public IP> --maps all   # server layout under /srv/fetih/prod
dotnet run tools/build.cs all --env test --ip <public IP> --maps all   # server layout under /srv/fetih/test
```

On a server without the .NET SDK (with the base 5619 client at `/srv/fetih/base/5619`), run
the build inside `mcr.microsoft.com/dotnet/sdk:11.0` with `/srv/fetih` mounted, or use the
`tools/build.sh` wrapper:

```bash
FETIH_ACCOUNT_IP=<public IP> ./tools/build.sh all --env prod --maps all
```

`--maps all` renders every map's tiles (most of an hour the first time; later runs skip
unchanged files). Without web assets, the site shows placeholders.

## First start

1. Start the shared stack (`db`, `account`, `proxy`, and observability):

```bash
cp deploy/env.example deploy/.env       # set FETIH_DOMAIN, FETIH_TEST_DOMAIN and passwords
sudo podman compose -f deploy/compose.yaml --profile deploy up -d --build
```

2. Prepare env files for production and test (from `deploy/env.prod.example` and
   `deploy/env.test.example`) and start both environments:

```bash
cp deploy/env.prod.example /srv/fetih/prod.env
cp deploy/env.test.example /srv/fetih/test.env
sudo podman compose -p fetih-prod -f deploy/compose.env.yaml --env-file /srv/fetih/prod.env up -d --build
sudo podman compose -p fetih-test -f deploy/compose.env.yaml --env-file /srv/fetih/test.env up -d --build
```

MariaDB starts empty (its files are in `sql/data`); the account server creates and
migrates `fetih_account`, and each game server creates and migrates its own schema
(`fetih_game` for prod, `fetih_game_test` for test).

3. Point both realms at this machine (`Altay` is public on 5816, `Mihenk` is staff-only on
   5826; the account server reaches each game's RPC by its container alias on `fetih`):

```bash
sudo podman exec -i fetih-db mariadb -uroot -p<password> fetih_account -e "
  UPDATE realm SET GameIPAddress = '<public IP>', GamePort = 5816, RpcIPAddress = 'fetih-prod-game', RpcPort = 5817 WHERE Name = 'Altay';
  UPDATE realm SET GameIPAddress = '<public IP>', GamePort = 5826, RpcIPAddress = 'fetih-test-game', RpcPort = 5817 WHERE Name = 'Mihenk';
"
sudo podman restart fetih-account
```

4. Create the first account and make it an administrator (authority 5):

```bash
sudo podman exec fetih-account dotnet Fetih.Account.dll create-account <email> <password>
sudo podman exec -i fetih-db mariadb -uroot -p<password> fetih_account \
  -e "UPDATE account SET AuthorityID = 5 WHERE Username = '<email>'"
```

Production is then at `https://<FETIH_DOMAIN>` and the staff-only test site is at
`https://<FETIH_TEST_DOMAIN>`.

## Environments

- **Production (`fetih-prod`)**: realm `Altay` (port 5816, open to all players), schema
  `fetih_game`, `STAFF_ONLY=false`, `STUDIO_READ_ONLY=true`.
- **Test (`fetih-test`)**: realm `Mihenk` (port 5826, `AuthorityID >= 2`), schema
  `fetih_game_test`, `STAFF_ONLY=true` (non-staff accounts are refused at login and public
  registration is disabled), `STUDIO_READ_ONLY=false`.

## Grafana

Grafana, Loki and Prometheus answer only on the machine's loopback. Reach them with an SSH
tunnel; the web's Grafana links and dashboard panels point at `http://localhost:3000`, so
they work through it too:

```bash
ssh -L 3000:127.0.0.1:3000 <server>
```

The servers' logs are in the `fetih-logs` volume under `/logs/<env>/<server>/` (`prod`,
`test`, `shared`), and in Loki with an `env` label. Dashboards default to `env=prod` and
have an `env` selector (`prod`, `test`, `dev`). Each web instance filters its own Loki and
Prometheus queries by its `Observability:Environment`.

## Updating

```bash
git pull
sudo podman compose -f deploy/compose.yaml --profile deploy up -d --build
sudo podman compose -p fetih-prod -f deploy/compose.env.yaml --env-file /srv/fetih/prod.env up -d --build
sudo podman compose -p fetih-test -f deploy/compose.env.yaml --env-file /srv/fetih/test.env up -d --build
```

A new table or column is a migration in `sql/migrations/<account|game>/`: the server
applies it when it starts, so `git pull` and rebuilding is all it takes. Never pipe SQL files
into the database by hand.

## Backups

`sql/data` is the whole database. For a consistent copy while it runs:

```bash
sudo podman exec fetih-db mariadb-dump -uroot -p<password> --single-transaction \
  --databases fetih_account fetih_game fetih_game_test > fetih-$(date +%F).sql
```

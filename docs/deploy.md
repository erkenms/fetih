# Deploying a public server

`compose.yaml` runs everything on one machine. Without a profile it starts what
development needs next to servers run from source (MariaDB, Loki, Alloy, Prometheus,
Grafana). `--profile deploy` adds the three servers and an HTTPS proxy in front of the web.

| Service | Reachable from | Port |
|---|---|---|
| `proxy` (Caddy) | the internet | 80, 443 (TCP and UDP) |
| `account` | the internet | 9960 |
| `game` | the internet | 5816; RPC 5817 and metrics 9818 only on the compose network |
| `web` | the proxy only | 5820 on the compose network |
| `db`, `grafana`, `loki`, `prometheus` | this machine | 127.0.0.1: 3306, 3000, 3100, 9090 |

## What the machine needs

- Linux with **rootful** Podman 5 or later (`sudo podman compose`), or Docker. Rootless
  Podman forwards published ports through a proxy that hides the sender, so every player
  would show up with the same internal address: per-IP connection limits, bans and the
  admin pages' IP columns would all go wrong.
- A clone of this repository. The web container mounts the whole checkout: Studio edits
  land in `comet/src/Comet.Game/Data`, and its Changes page diffs them with git, so they
  can be committed from the server.
- A domain whose A record points at the machine, for the certificate.
- The firewall open for 80, 443 (TCP and UDP), 5816 and 9960, and nothing else.

## Building the client and web assets

`tools/build.cs` builds the client and web assets from the original 5619 base client.
`Server.dat` is generated from `client/servers.json` (which lists both realms, `Altay`, production, and
`Mihenk`, test, staff only) using `--ip` or `FETIH_ACCOUNT_IP` (an IPv4 address; `ServerIP` holds at most 15
characters).

On a machine with the .NET 11 SDK:

```bash
dotnet run tools/build.cs all --ip <public IP>              # local: _CLIENT/5619 -> _CLIENT/fetih, web/assets
dotnet run tools/build.cs all --env prod --ip <public IP> --maps all   # server layout under /srv/fetih
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

```bash
cp deploy/env.example .env               # then set FETIH_DOMAIN and the passwords
sudo podman compose --profile deploy up -d --build
```

MariaDB starts empty (its files are in `comet/sql/data`); the account and game servers create
and migrate their own schemas when they start. Then point the realm at this machine: the account server hands the
client `GameIPAddress`, and reaches the game's RPC at `RpcIPAddress`.

```bash
sudo podman exec -i fetih-db mariadb -uroot -p<password> comet.account \
  -e "UPDATE realm SET GameIPAddress = '<public IP>', RpcIPAddress = 'game' WHERE RealmID = 1"
sudo podman restart fetih-account
```

Create the first account and make it an administrator (authority 5):

```bash
sudo podman exec fetih-account dotnet Comet.Account.dll create-account <email> <password>
sudo podman exec -i fetih-db mariadb -uroot -p<password> comet.account \
  -e "UPDATE account SET AuthorityID = 5 WHERE Username = '<email>'"
```

The site is then at `https://<FETIH_DOMAIN>`.

## Grafana

Grafana, Loki and Prometheus answer only on the machine's loopback. Reach them with an SSH
tunnel; the web's Grafana links and dashboard panels point at `http://localhost:3000`, so
they work through it too:

```bash
ssh -L 3000:127.0.0.1:3000 <server>
```

The servers' logs are in the `fetih_logs` volume, and in Loki through Grafana.

## Updating

```bash
git pull
sudo podman compose --profile deploy up -d --build
```

A new table or column is a migration in `comet/sql/migrations/<account|game>/`: the server
applies it when it starts, so `git pull` and rebuilding is all it takes. Never pipe SQL files
into the database by hand.

Studio edits made on the server are uncommitted changes in its checkout: commit them there,
or `git stash` them before a `git pull` that touches the same files.

## Backups

`comet/sql/data` is the whole database. For a consistent copy while it runs:

```bash
sudo podman exec fetih-db mariadb-dump -uroot -p<password> --single-transaction \
  --databases comet.account comet.game > fetih-$(date +%F).sql
```

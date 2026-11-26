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

## Files that don't come with the clone

Build these on a machine that has the original 5619 client in `_CLIENT/5619`, then copy them
to the same paths in the server's checkout.

1. **The client**, into `_CLIENT/fetih`. Players need it too, and it must point at the
   server: set `ServerIP` in `client/files/Server.dat` to the machine's public IP (an IP
   address; the field holds 15 characters), then `dotnet run client/build.cs`.
2. **The web's client art**, into `web/assets`: `dotnet run tools/assets/build.cs --maps all`
   (most of an hour the first time). The pages show placeholders without it.

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

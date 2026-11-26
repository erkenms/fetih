# Deploy

One machine. `deploy/compose.yaml` is the shared part (db, account, Caddy, Loki, Alloy,
Prometheus, Grafana). `deploy/compose.env.yaml` runs `game` + `web` per environment.

| Env | Project | Realm | Port | Schema | Notes |
|---|---|---|---|---|---|
| prod | `fetih-prod` | Altay | 5816 | `fetih_game` | `STUDIO_READ_ONLY=true` |
| test | `fetih-test` | Mihenk | 5826 | `fetih_game_test` | staff only, no registration |

Open to the internet: 80, 443 (TCP+UDP), 5816, 5826, 9960 (account). Everything else is
internal or `127.0.0.1`.

## Needs

- Rootful Podman 5+ or Docker. Rootless hides player IPs (bans, limits break).
- A checkout per env (`/srv/fetih/prod`, `/srv/fetih/test`): Studio edits land there.
- DNS: `<domain>` and `test.<domain>`.

## Build the client and assets

```bash
dotnet run tools/build.cs all --env prod --ip <public IP> --maps all
dotnet run tools/build.cs all --env test --ip <public IP> --maps all
FETIH_ACCOUNT_IP=<public IP> ./tools/build.sh all --env prod --maps all   # no SDK: runs in a container
```

`--ip` is IPv4 only (`Server.dat` fits 15 chars). `--maps all` takes ~1h the first time.

## First start

```bash
cp deploy/env.example deploy/.env                     # domains, passwords
sudo podman compose -f deploy/compose.yaml --profile deploy up -d --build
cp deploy/env.prod.example /srv/fetih/prod.env
cp deploy/env.test.example /srv/fetih/test.env
sudo podman compose -p fetih-prod -f deploy/compose.env.yaml --env-file /srv/fetih/prod.env up -d --build
sudo podman compose -p fetih-test -f deploy/compose.env.yaml --env-file /srv/fetih/test.env up -d --build
```

Servers create and migrate their schemas. Then point the realms at the machine and add an
admin:

```bash
sudo podman exec -i fetih-db mariadb -uroot -p<pw> fetih_account -e "
  UPDATE realm SET GameIPAddress='<ip>', GamePort=5816, RpcIPAddress='fetih-prod-game', RpcPort=5817 WHERE Name='Altay';
  UPDATE realm SET GameIPAddress='<ip>', GamePort=5826, RpcIPAddress='fetih-test-game', RpcPort=5817 WHERE Name='Mihenk';"
sudo podman restart fetih-account
sudo podman exec fetih-account dotnet Fetih.Account.dll create-account <email> <password>
sudo podman exec -i fetih-db mariadb -uroot -p<pw> fetih_account -e "UPDATE account SET AuthorityID=5 WHERE Username='<email>'"
./deploy/grafana-db.sh
```

## Update

`git pull`, then the three `up -d --build` lines again. Migrations apply at startup.

## Grafana and logs

Grafana is served under `/grafana/` behind the web sign-in (moderators and above); port 3000 is
not published. Logs in the `fetih-logs` volume (`/logs/<env>/<server>/`) and in Loki with an
`env` label.

## Backup

```bash
sudo podman exec fetih-db mariadb-dump -uroot -p<pw> --single-transaction \
  --databases fetih_account fetih_game fetih_game_test > fetih-$(date +%F).sql
```

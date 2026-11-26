# Logs and metrics

`podman compose -f deploy/compose.yaml up -d` starts these next to MariaDB, all on 127.0.0.1:

| Service | Port | What |
|---|---|---|
| Grafana | 8080 (`dev-proxy`) | Dashboards and alerts at `http://localhost:8080/grafana/` behind web sign-in (`/giris`, moderators+); `admin`/`admin` to edit |
| Loki | 3100 | Logs from `src/Fetih.*/logs/*.jsonl` (dev, `env=dev`) and `/logs/<env>/<server>/*.jsonl` (containers: `prod`, `test`, `shared`), per-event retention |
| Prometheus | 9090 | Scrapes `game` (`:9818`), `account` (`:9819`), and `node-exporter` (`:9100`), 30 days |
| Alloy | — | Tails the main log files into Loki (audit files are excluded) |
| Node Exporter | — | Host CPU/memory/disk metrics and backup textfile metrics (`observability/node-exporter/textfile`) |

## Retention and outputs

Each server writes three outputs (`Fetih.Core/Logging/LogSetup.cs`):

1. **Main JSONL files** (`<Directory>/<server>-YYYYMMDD.jsonl`): Alloy's buffer so logs survive a Loki restart. Kept for `RetainDays` (`30` in dev configs, `3` in `deploy/compose.yaml` production containers).
2. **Audit JSONL files** (`<Directory>/audit/<server>-audit-YYYYMMDD.jsonl`): an allowlist of permanent events (`AuditEvents.cs`) — account/session logins and logouts (with account, character, IP, time and session length), completed trades and booth sales (with items and money), all `gm.*` actions and bans, character deletes, cheat reports, and web admin actions. Never deleted by the server; rolled daily files are gzip-compressed (`.jsonl.gz`) when the next day's file opens. Alloy excludes these files so Loki does not ingest them twice.
3. **Console stdout**: everything except packet traces in dev; restricted to `warn` and above in containers (`Logging__ConsoleLevel=warn`), with a `10m` × 3 container log cap in `deploy/compose.yaml`.

### Loki per-stream retention (`loki/loki.yaml`)

Loki's compactor enforces retention per stream (higher priority wins):

| Stream selector | Priority | Retention |
|---|---|---|
| `{env="test"}` | 100 | 7 days (168h) |
| `trade.*`, `booth.sold`, `gm.*`, `web.*`, `character.deleted`, `session.cheat_report`, `pk.jailed`, `account.created` | 50 | 10 years (87600h) |
| `session.*`, `account.login*` | 40 | 2 years (17520h) |
| `move.*`, `combat.*`, `monster.*` | 20 | 7 days (168h) |
| `packet.*` | 10 | 2 days (48h) |
| Everything else (default) | — | 90 days (2160h) |

## Production volume

In production (`deploy/compose.yaml`), `Logging__Level` is `info` and `PacketTrace__All` is `false`. High-volume `move.walk`, `move.jump`, `combat.hit`, and `monster.respawned` events stay at `debug` (not written at `info`), while `monster.killed` (used by the Player dashboard), `monster.drop`, `move.teleport`, and rejections remain at `info`/`warn` with 7-day Loki retention. Packet tracing can still be enabled per IP/account in config or toggled at runtime for an account from the admin panel.

Measured with 10 `Fetih.Bots` (5 hunting, 3 wandering, 2 jumping) over 4 minutes under production logging settings (`Level=info`, `ConsoleLevel=warn`, `PacketTrace.All=false`):

- **Game main JSONL**: ~115 KB total (~77 KB steady-state gameplay: 32% `monster.killed`, 17% `monster.drop`, 10% `item.expired`), or **~2.8–4.1 MB per continuously active player per day** (~280–410 MB/day for 100 active players, down from ~10 GB/day with `PacketTrace.All=true`).
- **Audit JSONL**: ~830 B per game login+logout pair and ~313 B per account login (~400 KB/day uncompressed, ~60 KB/day gzipped for 100 players logging in 5 times/day).
- **Container stdout**: 6 warning lines in 4 minutes on `game`, 0 on `account`.

## Queries and links

Loki labels are only `server`, `level` and `event`. Everything else (`characterId`,
`accountId`, `packetName`...) is filtered after `| json`. Packet trace lines are the
`packet.in` and `packet.out` events; leave them out with `event!~"packet\\.(in|out)"`. A
selector needs one matcher that can't match empty, so add `server=~".+"` to negative-only ones.

Links the web can build (`Grafana` helper):

- Player: `/grafana/d/fetih-player/player?var-characterId=1000000&var-accountId=1`
  (incoming packets carry only `accountId`, so pass both)
- Packets: `/grafana/d/fetih-packets/packets?var-accountId=1&var-packetName=MsgItem`
- Overview, Admin, Economy, Chat, Items, Audit: `/grafana/d/fetih-<name>/<name>`

The dashboards are provisioned from `grafana/dashboards/*.json` (`allowUiUpdates: false`).
Run `./deploy/grafana-db.sh` after schemas are created so Grafana's read-only MariaDB user
(`fetih_account.grafana_account` view and game tables) works.

## Alerting

Alert rules, notification policies, and the Discord contact point are provisioned from
`grafana/provisioning/alerting/` (production only: `env="prod"`, and `env="shared"` for the account server, so the test environment never pages).
Set `GRAFANA_DISCORD_WEBHOOK` in `.env` to route alerts to Discord.

Nightly backups expose `fetih_backup_last_success_timestamp_seconds` (Unix timestamp in
seconds) as a `.prom` file under `BACKUP_METRICS_DIR` (default
`observability/node-exporter/textfile`), which `node-exporter` serves to Prometheus.

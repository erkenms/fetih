# Logs and metrics

`podman compose up -d` starts these next to MariaDB, all on 127.0.0.1:

| Service | Port | What |
|---|---|---|
| Grafana | 3000 | Dashboards; viewers need no login, `admin`/`admin` to edit |
| Loki | 3100 | Logs from `comet/src/Comet.*/logs/*.jsonl` (dev) and `/logs/*/*.jsonl` (containers), per-event retention |
| Prometheus | 9090 | Scrapes the game's `/metrics` (`Metrics` in `Comet.Game.config`, port 9818), 30 days |
| Alloy | — | Tails the main log files into Loki (audit files are excluded) |

## Retention and outputs

Each server writes three outputs (`Comet.Core/Logging/LogSetup.cs`):

1. **Main JSONL files** (`<Directory>/<server>-YYYYMMDD.jsonl`): Alloy's buffer so logs survive a Loki restart. Kept for `RetainDays` (`30` in dev configs, `3` in `compose.yaml` production containers).
2. **Audit JSONL files** (`<Directory>/audit/<server>-audit-YYYYMMDD.jsonl`): an allowlist of permanent events (`AuditEvents.cs`) — account/session logins and logouts (with account, character, IP, time and session length), completed trades and booth sales (with items and money), all `gm.*` actions and bans, character deletes, cheat reports, and web admin actions. Never deleted by the server; rolled daily files are gzip-compressed (`.jsonl.gz`) when the next day's file opens. Alloy excludes these files so Loki does not ingest them twice.
3. **Console stdout**: everything except packet traces in dev; restricted to `warn` and above in containers (`Logging__ConsoleLevel=warn`), with a `10m` × 3 container log cap in `compose.yaml`.

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

In production (`compose.yaml`), `Logging__Level` is `info` and `PacketTrace__All` is `false`. High-volume `move.walk`, `move.jump`, `combat.hit`, and `monster.respawned` events stay at `debug` (not written at `info`), while `monster.killed` (used by the Player dashboard), `monster.drop`, `move.teleport`, and rejections remain at `info`/`warn` with 7-day Loki retention. Packet tracing can still be enabled per IP/account in config or toggled at runtime for an account from the admin panel.

Measured with 10 `Comet.Bots` (5 hunting, 3 wandering, 2 jumping) over 4 minutes under production logging settings (`Level=info`, `ConsoleLevel=warn`, `PacketTrace.All=false`):

- **Game main JSONL**: ~115 KB total (~77 KB steady-state gameplay: 32% `monster.killed`, 17% `monster.drop`, 10% `item.expired`), or **~2.8–4.1 MB per continuously active player per day** (~280–410 MB/day for 100 active players, down from ~10 GB/day with `PacketTrace.All=true`).
- **Audit JSONL**: ~830 B per game login+logout pair and ~313 B per account login (~400 KB/day uncompressed, ~60 KB/day gzipped for 100 players logging in 5 times/day).
- **Container stdout**: 6 warning lines in 4 minutes on `game`, 0 on `account`.

## Queries and links

Loki labels are only `server`, `level` and `event`. Everything else (`characterId`,
`accountId`, `packetName`...) is filtered after `| json`. Packet trace lines are the
`packet.in` and `packet.out` events; leave them out with `event!~"packet\\.(in|out)"`. A
selector needs one matcher that can't match empty, so add `server=~".+"` to negative-only ones.

Links the web can build:

- Player: `http://localhost:3000/d/comet-player/player?var-characterId=1000000&var-accountId=1&from=now-24h&to=now`
  (incoming packets carry only `accountId`, so pass both)
- Packets: `http://localhost:3000/d/comet-packets/packets?var-accountId=1&var-packetName=MsgItem`
- Overview: `http://localhost:3000/d/comet-overview/overview`; a single panel embeds with
  `/d-solo/comet-overview/overview?panelId=<id>`

The dashboards are provisioned from `grafana/dashboards/*.json`. Edit them in Grafana, then
export the JSON over the file.

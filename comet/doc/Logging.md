# Logging

Comet writes every log event twice:

* **Console**: readable lines for people watching the server.
* **JSON Lines files**: one JSON object per line, for tools such as Grafana/Loki,
  `jq`, or custom scripts (economy monitors, drop detectors).

The JSON file format and the event catalog below are a **contract**. Tools may
depend on them. Rules:

1. Never rename or remove an event name, field, or reason value. Add new ones instead.
2. Never change a field's type (a number stays a number).
3. A breaking change increments the schema version `v`.

## Levels

| Level   | Console | Used for |
|---------|---------|----------|
| `trace` | `TRC`   | Raw packets and per-tick details. Very noisy. |
| `debug` | `DBG`   | Internal state transitions and decoded values for debugging. |
| `info`  | `INF`   | Game-world actions: connections, logins, logouts, character creation. |
| `warn`  | `WRN`   | Rejected or suspicious input: failed logins, unhandled packets. |
| `error` | `ERR`   | Exceptions and failures that broke an action or session. |

## Configuration

Each server's config file (`Comet.Account.config`, `Comet.Game.config`) has a
`Logging` section:

```json
"Logging": {
    "Level": "info",
    "Overrides": { "Comet.Network": "debug" },
    "Directory": "logs",
    "RetainDays": 30
}
```

* `Level`: the minimum level that gets written.
* `Overrides`: a minimum level per namespace or class name prefix.
* `Directory`: where the files go, relative to the working directory. Files roll daily as
  `{Directory}/{server}-yyyyMMdd.jsonl`, e.g. `logs/game-20261118.jsonl`.
* `RetainDays`: how many daily files to keep.

Any value can be overridden on the command line, for example
`dotnet Comet.Game.dll --Logging:Level=trace`.

## Line format (schema v1)

```json
{"v":1,"ts":"2026-11-18T12:55:58.130Z","level":"info","server":"game","event":"session.logout","msg":"Bob logged out: timeout after 1834.2s (...)","name":"Bob","reason":"timeout","durationSec":1834.2,"connId":12,"accountId":3,"characterId":1000002,"ip":"10.0.0.5","mapId":1002,"x":430,"y":380,"src":"Comet.Game.Server"}
```

Fields appear in this order:

| Field    | Type   | Always | Meaning |
|----------|--------|--------|---------|
| `v`      | number | yes | Schema version (currently 1) |
| `ts`     | string | yes | UTC timestamp, ISO 8601 with milliseconds |
| `level`  | string | yes | `trace`, `debug`, `info`, `warn`, `error` |
| `server` | string | yes | `account` or `game` |
| `event`  | string | no  | Stable event name (see catalog). Lines without one are free-form diagnostics. |
| `msg`    | string | yes | Human-readable message. **Don't parse it**; use the fields. |
| *fields* | any    | no  | Event fields, camelCase, with typed values |
| `src`    | string | no  | Source class (e.g. `Comet.Game.Server`) |
| `exc`    | string | no  | Exception with stack trace, as a single string |

Event fields that would collide with a reserved name are prefixed with `_` (e.g. `_msg`).

While the game server processes a packet, every line it writes also carries `connId`, and
`accountId` and `characterId` once known, even lines without an `event`.

## Event catalog

### Server

| Event | Level | Fields |
|-------|-------|--------|
| `server.start` | info | `ipAddress`, `port` |
| `server.stop` | info | none |
| `rpc.connected` | info | `agent` (game server; the account server connected) |

### Connections (both servers)

| Event | Level | Fields |
|-------|-------|--------|
| `net.connect` | info | `connId`, `ip` |
| `net.disconnect` | info | `connId`, `ip`, `reason`, `durationSec` |
| `net.rejected` | warn | `ip` (too many connections from one address) |

`connId` is unique per server process and resets when the server restarts. Use it
to pair `net.connect` with `net.disconnect` and other events of the same connection.

**`reason` values** (also used by `session.logout`):

| Reason | Meaning |
|--------|---------|
| `client_closed` | The client closed the connection cleanly (quit the game) |
| `reset` | The connection was reset or aborted (client crashed or was killed) |
| `timeout` | The client's machine or network stopped answering TCP keepalive probes (about 110 s after the last traffic), or a new connection didn't start its handshake within `HandshakeTimeoutSeconds`. Idle players are never dropped. |
| `socket_error` | Any other socket failure (logged with details at warn) |
| `protocol_error` | The client sent bytes that couldn't be split into packets |
| `handshake_failed` | The key exchange failed, or the first packet after it couldn't be read (usually mismatched cipher keys) |
| `auth_failed` | A login was rejected |
| `replaced` | Another login to the same account took over |
| `redirected` | The account server handed the client to the game server (normal) |
| `server` | Closed by the server without a more specific reason |
| `shutdown` | The server is shutting down |

### Account server

| Event | Level | Fields |
|-------|-------|--------|
| `account.login` | info | `username`, `accountId`, `ip`, `realm` |
| `account.login_failed` | warn | `username`, `ip`, `reason` (`unknown_account`, `bad_password`, `realm_offline`, `password_not_set`, `account_banned`, `account_locked`), `realm` (only for `realm_offline`) |
| `account.created` | info | `username`, `accountId` |
| `account.create_failed` | warn | `username`, `reason` (`invalid_username`, `invalid_password`, `username_taken`) |

`bad_password` means the client's SRP6 proof didn't match (patch 5619 never sends the
password). `password_not_set` means the account has no SRP6 salt and verifier (`SrpSalt` and
`SrpVerifier` are empty): set one with
`dotnet Comet.Account.dll set-password <username> <password>`.

`account_banned` and `account_locked` come from the account's `StatusID` (5 and 4 in
`account_status`). They are only logged after a correct password, and the client is sent
its own banned (12) or locked (22) message.

`account.created` and `account.create_failed` come from `create-account` (and anything
else that creates accounts through `AccountsRepository.CreateAsync`). Account names are
email addresses of up to 127 characters; passwords are 6 to 16 printable characters.

### Game server sessions

| Event | Level | Fields |
|-------|-------|--------|
| `game.auth_failed` | warn | `connId`, `ip`, `reason` (`invalid_token`, `ip_mismatch`) |
| `session.login` | info | `name`, `connId`, `accountId`, `characterId`, `level`, `ip`, `mapId`, `x`, `y`, `online` |
| `session.logout` | info | `name`, `reason`, `durationSec`, `connId`, `accountId`, `characterId`, `ip`, `mapId`, `x`, `y`, `online` |
| `session.replaced` | info | `accountId`, `ip`, `previousConnId`, `previousIp` |
| `character.created` | info | `name`, `accountId`, `characterId`, `class`, `mesh` |
| `character.relocated` | warn | `name`, `fromMapId`, `fromX`, `fromY`, `reason` (`unknown_map`, `blocked_cell`): the saved position couldn't be used, so the character entered at the start position |
| `character.saved` | debug | `name`, `characterId`, `mapId`, `x`, `y`: written after the logout save finishes (`session.logout` comes before it) |
| `character.create_failed` | info/warn/error | `reason` (`name_taken`, `invalid_name`, `invalid_input`, `invalid_token`, `db_error`) plus `accountId` and `name`, or `connId` and `ip` for `invalid_token` |

`invalid_name` means the name breaks the character name rules: 4 to 15 printable ASCII
characters without spaces, and no `[GM]` or `[PM]`. Names are unique regardless of case.

`session.login` is written when an existing character enters the world.
`session.logout` is written once per `session.login`, when that connection ends.
`online` is the number of players in the world right after the event, so the latest
value of either event is the current player count (for a Grafana "players online" panel).
`durationSec` is the time in the world.

### World

| Event | Level | Fields |
|-------|-------|--------|
| `map.index_loaded` | info | `mapCount`, `directory` |
| `map.loaded` | debug | `mapId`, `width`, `height`, `portalCount`, `loadMs` |
| `map.load_failed` | error | `mapId`, `path` (the exception is in `exc`) |

Maps are listed at startup from the client's GameMap.dat and each map's cells are read
the first time the map is used, so `map.loaded` appears once per map per server run.

### Packets

| Event | Level | Fields |
|-------|-------|--------|
| `packet.unhandled` | warn | `packetType`, `length`, `hex`, plus `action` for MsgAction/MsgItem |
| `packet.error` | error | `packetType` (with `exc`) |

## Using the logs

### jq

```sh
# Logins and logouts today
jq -c 'select(.event=="session.login" or .event=="session.logout")
       | {ts, event, name, reason, durationSec}' logs/game-20261118.jsonl

# Why do sessions end?
jq -r 'select(.event=="session.logout") | .reason' logs/game-*.jsonl | sort | uniq -c

# Average session length per character
jq -s 'map(select(.event=="session.logout")) | group_by(.name)
       | map({name: .[0].name, avgSec: (map(.durationSec) | add / length)})' logs/game-*.jsonl
```

### Grafana with Loki

Ship `logs/*.jsonl` with Promtail or Grafana Alloy, labeled e.g. `job="comet"`.
Keep labels low-cardinality (`server`, `event`, `level`). Leave names and IDs in the line.

```logql
# Logins per hour
sum by (server) (count_over_time({job="comet"} | json | event="session.login" [1h]))

# Drops vs clean quits
sum by (reason) (count_over_time({job="comet"} | json | event="session.logout" [1h]))

# Players online, approximately: logins minus logouts over a window
sum(count_over_time({job="comet"} | json | event="session.login" [24h]))
  - sum(count_over_time({job="comet"} | json | event="session.logout" [24h]))
```

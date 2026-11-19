# Comet

A Conquer Online server for client patch 5619: an account server that logs players in
with SRP6, and a game server that hosts the world.

## Running

```bash
podman compose up -d                     # MariaDB (comet-db), seeded from sql/
cd src/Comet.Account
dotnet run -- create-account me@example.com secret12
dotnet run                               # account server, port 9960
cd ../Comet.Game && dotnet run           # game server, in another terminal
```

The game server reads maps from a copy of the 5619 client, set by `Maps:ClientDirectory`
in `Comet.Game.config` (default: `_CLIENT/5619` next to `comet/`). Logs go to `logs/`
in each server's folder.

## Layout

| Project | What it is |
|---|---|
| Comet.Account | Account server |
| Comet.Game | Game server: packets, characters, maps, movement |
| Comet.Network | Sockets, ciphers, packet processing, RPC between the servers |
| Comet.Core | Logging |
| Comet.Shared | RPC message types |
| Comet.Client | Headless 5619 client used by tests (and later bots) |

## Docs

- [Protocol](doc/Protocol.md): the login flow and how 5619 facts were established
- [Logging](doc/Logging.md): log format and event catalog
- [Testing](doc/Testing.md): running tests and recording client captures

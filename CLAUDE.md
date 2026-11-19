# fetih

A Conquer Online server for client patch 5619, built on the Comet emulator.

The real client is in `_CLIENT/5619` and is tested in a VM at 172.16.60.128.

## Working rules

- Small steps, each verified (tests, and the VM for anything the client sees).
- Follow how the original game behaves unless there's a clearly better engineering
  choice. Add database tables when a feature needs them, not before.
- Every protocol fact in code carries its source: `5619: from client binary`,
  `from reference`, `seen in VM` / `confirmed in VM`. Don't mark a guess as confirmed.
- Commits: one short line, no trailers. Never push.

## Map

```
comet/
  src/
    Comet.Account/    account server: SRP6 login, hands the client a game server token
      Packets/          one class per message; PacketHandlers.cs = what's handled
      Database/         EF Core models and repositories (accounts, realms)
    Comet.Game/       game server
      Program.cs        startup: config, maps, logging, RPC, listener
      Server.cs         connect / packet dispatch / disconnect
      Kernel.cs         global state: sessions, Players (online), Maps, World
      Remote.cs         RPC the account server calls (login tokens)
      Packets/          one class per message; PacketHandlers.cs is the type table,
                        MsgAction/MsgItem keep their own action tables
      States/           Client (connection + character), Character, registries,
                        creation rules
      World/            maps and everything on them:
                          GameMapIndex/MapStore  read client map files lazily
                          MapGrid/SceneFile      walkable cells and elevation
                          GameMap/WorldMaps      who is where, view range (18)
                          Visibility             spawn/despawn on enter and leave
                          Movement               walk, jump, turn, emote
      Database/         EF Core models and repositories (characters)
    Comet.Network/    sockets, ciphers (TQ, CAST5, DH, SRP6), packet processor,
                      RPC, PacketTrace (raw packet logging)
    Comet.Core/       logging (Serilog setup, JSON Lines formatter, LogEvents names)
    Comet.Shared/     RPC message types shared by both servers
    Comet.Client/     headless 5619 client for tests and bots, written from the
                      protocol notes (not from server classes); also test map files
  tests/
    Comet.*.Tests           unit tests per project
    Comet.Integration.Tests real servers + throwaway databases + Comet.Client
  sql/                schema files (also seed the compose container)
  doc/                Protocol.md, Logging.md, Testing.md
```

## Things that bite

- Packet layouts live in the packet classes' comments, not in docs. When the client
  disagrees with the code, the client wins: check a packet trace or the binary.
- Comet.Client shares our assumptions, so integration tests can't catch a wrong
  protocol guess. To check against the real client, turn on `Logging:PacketTrace` for
  the VM's IP and read the server log.
- Unhandled MsgActions are echoed back on purpose: the client waits for replies to its
  login steps.
- Packets from different clients are processed in parallel (PacketProcessor
  partitions); one client's packets are processed in order.

## Commands

```bash
podman compose -f comet/compose.yaml up -d   # MariaDB as comet-db
dotnet test comet/Comet.slnx                 # all tests; integration needs comet-db
cd comet/src/Comet.Game && dotnet run        # likewise Comet.Account
```
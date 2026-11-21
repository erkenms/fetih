# fetih

Conquer Online server built on the Comet emulator: `comet/src/Comet.Account` (login),
`Comet.Game` (world), `Comet.Network` (sockets, ciphers, RPC), `Comet.Core` (logging),
`Comet.Shared` (RPC types), `Comet.Client` (headless client for tests).

## Rules

- When the real client disagrees with the code, the client wins.
- Comments only for what the code can't say: packet layouts and non-obvious reasons.
- Commits: one short line, no trailers. Never push.

## Commands

```bash
podman compose -f comet/compose.yaml up -d   # MariaDB; integration tests need it
dotnet test comet/Comet.slnx
cd comet/src/Comet.Game && dotnet run        # likewise Comet.Account
dotnet run client/build.cs                    # the client, into _CLIENT/fetih
```

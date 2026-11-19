# fetih

Conquer Online server built on the Comet emulator: `comet/src/Comet.Account` (login),
`Comet.Game` (world), `Comet.Network` (sockets, ciphers, RPC), `Comet.Core` (logging),
`Comet.Shared` (RPC types), `Comet.Client` (headless client for tests).

## Rules

- When the real client disagrees with the code, the client wins.
- Comments only for what the code can't say: packet layouts and non-obvious reasons.
- Log event names and fields (`LogEvents`) are read by external tools: add, never rename.
- Commits: one short line, no trailers. Never push.

## Notes

- Unhandled MsgActions are echoed back on purpose: the client waits on its login steps.
- Packets of one client are processed in order; different clients in parallel.
- Comet.Client is written from the protocol, not from server classes. Keep it that way.

## Commands

```bash
podman compose -f comet/compose.yaml up -d   # MariaDB; integration tests need it
dotnet test comet/Comet.slnx
cd comet/src/Comet.Game && dotnet run        # likewise Comet.Account
```

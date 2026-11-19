# Testing

```bash
podman compose up -d     # comet-db, needed by the integration tests
dotnet test Comet.slnx
```

## Integration tests

`ServerFixture` creates two throwaway databases in comet-db (`comet_test_<run>_*`),
starts both servers from their build output on free ports, and drops everything after
the run. Your own databases are never touched. Maps are small synthetic ones built with
`Comet.Client.MapFiles`.

Tests play through the servers with Comet.Client and check what the client receives and
the events in the servers' logs (`Servers.WaitForEventAsync`). Derive from
`IntegrationTest`; each test creates its own accounts and characters. Logs of the last
run: `tests/Comet.Integration.Tests/bin/<Configuration>/net11.0/integration-logs/`.

Database connection: `COMET_TEST_DB_HOST`, `COMET_TEST_DB_USER`,
`COMET_TEST_DB_PASSWORD` (default `localhost`, `root`, `password`).

## Captures of the real client

Comet.Client is written from our understanding of the protocol, so where that's wrong
the server and the tests are wrong together. Captures fix that: packet traces from the
real client in the VM, in `tests/Captures/5619/`, replayed by `CaptureReplayTests` in
Comet.Game.Tests and Comet.Account.Tests. Every packet the client sent must be handled
and decode to its own length, or be listed in the test's `NotHandledYet`. A new packet
fails the test and prints the line to add.

To record one:

1. Turn on the trace in both configs (takes effect after a rebuild, since the servers
   read the config copied to their build folder):
   ```json
   "PacketTrace": { "All": false, "Accounts": [], "IPs": ["172.16.60.128"] }
   ```
2. Play through what the capture should cover.
3. From `comet/src`, keep the packet events (and `server.start`, since connection numbers
   restart with the server):
   ```bash
   jq -c 'select(.event=="packet.in" or .event=="packet.out" or .event=="server.start")' Comet.Account/logs/account-YYYYMMDD.jsonl Comet.Game/logs/game-YYYYMMDD.jsonl > ../tests/Captures/5619/YYYY-MM-DD-what.jsonl
   ```

Captures contain account names and SRP6 proofs (not passwords): use test accounts.

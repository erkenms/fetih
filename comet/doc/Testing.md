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

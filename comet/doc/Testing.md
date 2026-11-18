# Testing

```bash
podman compose up -d        # once: the comet-db MariaDB container
dotnet test Comet.slnx      # unit and integration tests, about 25 seconds
```

The unit tests need nothing running. The integration tests need the `comet-db` container
and fail with a message saying so if it isn't reachable.

## Integration tests

[Comet.Integration.Tests](../tests/Comet.Integration.Tests/) checks the servers the way
players use them. For each test run, `ServerFixture`:

1. Creates two new databases in `comet-db`, `comet_test_<run>_account` and
   `comet_test_<run>_game`, from the default files in `sql/`. Your own `comet.account`
   and `comet.game` are never touched.
2. Points the `Storm` realm at the test game server and starts the game and account
   servers from their build output, as separate processes on free local ports, with
   debug logging and the per-IP connection limits raised.
3. Stops both servers and drops both databases when the run ends.

Tests play through the servers with [Comet.Client](../src/Comet.Client/), a headless
5619 client, and check two things: what the client receives, and the events in the
servers' JSON Lines logs (the [log contract](Logging.md)). The logs of the last run are
kept in `tests/Comet.Integration.Tests/bin/<Configuration>/net11.0/integration-logs/`.

The database connection can be changed with `COMET_TEST_DB_HOST`, `COMET_TEST_DB_USER`
and `COMET_TEST_DB_PASSWORD` (defaults: `localhost`, `root`, `password`, as in
`compose.yaml`).

### What's covered

| File | Scenarios |
|---|---|
| `AccountCreationTests` | `create-account` and `set-password` commands; names that aren't email addresses or are too long; passwords outside 6 to 16 printable characters; the same email in another case; ten simultaneous creations of one email (one wins); twenty different accounts at once |
| `AccountLoginTests` | right, wrong, empty and wrong-case passwords; unknown account; email in another case; account without a password; unknown realm; banned and locked accounts (and that a wrong password doesn't reveal a ban); a proof sent without a challenge; twenty logins at once; one account logging in twice at once |
| `GameLoginTests` | made-up token; reusing a token; new account asked to create a character; returning player goes straight to the map; logging in again closes the first connection; `MsgConnect` from a player already in the game is ignored; twenty players on the map at once |
| `CharacterCreationTests` | every class and body (stats, spawn point, logs); unknown classes and bodies; names breaking the rules (length, spaces, non-ASCII, `[GM]`/`[PM]`); names with symbols and `#NN` emoji codes; taken names in any case; two players racing for one name; wrong creation token; an account with a character trying to create another |

### Writing a test

Derive from `IntegrationTest` to share the running servers. Each test makes its own
accounts and characters (`Servers.CreateAccountAsync`, `Servers.NewCharacterName`), so
tests don't depend on each other or on order. Useful starting points:

* `Servers.LoginAsync(email, password)`: the account server's answer.
* `AtCharacterCreationAsync()`: a connected game client at the character creation step.
* `PlayingAsync()`: a character standing on the map (`Player`).
* `Servers.WaitForEventAsync(server, event, field, value)`: waits for a log event.

## Comet.Client

The client does what the 5619 client does on the wire, written from the protocol notes in
[Patch5619.md](Patch5619.md) rather than from the server's packet classes, so it checks
the server independently. `Player.LoginAsync` runs the whole login (account server,
SRP6, game server key exchange, character creation, entering the map) and is meant to be
the starting point for bots.

One difference from the real client: the real client hashes 32 bytes of the SRP6 shared
secret even when the secret is shorter, so about 1 real login in 256 fails. Comet.Client
pads with zeros like the server, so tests never fail that way.

## Captures of the real client

Our test client is written from our own understanding of the protocol, so where that
understanding is wrong, the tests and the server agree with each other and both are
wrong (that's how the 5619 LoginComplete, 251 not 132, got past the tests). Captures
close that gap: they are packet traces recorded from the real client in a VM, kept in
`tests/Captures/5619`, and replayed by `Replay/CaptureReplayTests` in Comet.Game.Tests
and Comet.Account.Tests:

* Every packet the client sent must either be handled by the server and decode to its
  own length, or be listed in that test's `NotHandledYet`, with the roadmap step that
  will handle it.
* Anything else fails the test, which prints the line to add to `NotHandledYet` (or the
  packet that needs a handler).
* When a packet gets a handler, the test fails until it's taken off `NotHandledYet`.
* With no captures, the replay tests are skipped.

### Recording a capture

1. Trace the VM's address on both servers, in `Logging:PacketTrace` of each config file
   (or on the command line):
   ```json
   "PacketTrace": { "All": false, "Accounts": [], "IPs": ["172.16.60.128"] }
   ```
2. Restart both servers and play through what the capture should cover: logging in,
   creating a character, walking, jumping, chatting, logging out.
3. Copy the trace into a capture file, one per session, named for what it covers
   (from `comet/src`):
   ```bash
   jq -c 'select(.event=="packet.in" or .event=="packet.out" or .event=="server.start")' \
     Comet.Account/logs/account-*.jsonl Comet.Game/logs/game-*.jsonl \
     > ../tests/Captures/5619/2026-11-18-login-walk.jsonl
   ```
   Add `select(.ts > "2026-11-18T19:30")` to the filter to take only part of a log.
   Keep `server.start`: connection numbers start again after a restart.
4. Run the tests and commit the capture.

Captures hold what the client sent, including account names and the SRP6 proof (not
the password). Record them with test accounts only.

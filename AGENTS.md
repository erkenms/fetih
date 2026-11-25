# fetih

A Conquer Online server for the 5619 client, built on the Comet emulator:
`comet/src/Comet.Account` (login), `Comet.Game` (world), `Comet.Network` (sockets, ciphers,
RPC), `Comet.Core` (logging), `Comet.Shared` (RPC types), `Comet.Client` (headless client for
tests). `client/` builds the patched client. The web app (issue #37, its plan and rules) is
`Comet.Web` plus `web/` (React), with client art built by `tools/assets/build.cs`.

## Rules

- When the real client disagrees with the code, the client wins.
- Comments only for what the code can't say: packet layouts and non-obvious reasons.
- Never write where a rule was learned from (another emulator's name, a forum, "5808
  source") in code or commit messages. State the rule. Mentioning the 5619 client is fine.
- Commits: one short line, no trailers. Never push.

## Commands

```bash
podman compose -f comet/compose.yaml up -d   # MariaDB (integration tests need it), Loki, Grafana :3000
dotnet test comet/Comet.slnx
cd comet/src/Comet.Game && dotnet run        # likewise Comet.Account, Comet.Web (:5820)
cd web && npm run dev                         # the web frontend (:5173); npm run build, npm test
dotnet run client/build.cs                    # the client, into _CLIENT/fetih
```

A public server runs the same compose file with `--profile deploy` (servers in containers
behind an HTTPS proxy): [docs/deploy.md](docs/deploy.md). Settings the servers read from
their `.config` files can be overridden by `COMET_GAME_`, `COMET_ACCOUNT_` and `COMET_WEB_`
environment variables (`COMET_GAME_Database__Hostname=db`).

## Don't break the user's setup

- The user often has `Comet.Game` and `Comet.Account` running from `comet/src`, with a
  character logged in from a Windows VM. For a smoke test, run on other ports
  (`--GameNetwork:Port=... --RpcNetwork:Port=...`) and stop only the PID you started. Never
  `pkill -f Comet.Game`. Starting the game server also runs startup jobs (like the arena season
  catch-up) against the dev database.
- A new table or column goes in `comet/sql/*.sql` **and** into the running `comet-db`
  container (root/password) with `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`.
  The container seeds from the .sql files only on its first run.
- Never pipe the .sql files into the container: they start with `USE` and `DROP TABLE` for the
  live schemas. That wiped every account and character once (2026-11-23). To compare
  schemas, read `information_schema` or `SHOW CREATE TABLE`.

## Finding out how the game behaves

The user tests every commit in the real Windows 5619 client. Evidence, strongest first:

1. **The client's help pages**, as Markdown in `~/Developer/co_help_files/` (`INDEX.md` maps
   `helpN.htm` to files). Grep them before building or changing a feature, and tell the user
   which page decided something. They're the publisher's text: they stay out of this repo.
2. **Client strings**: `iconv -f GBK -t UTF-8 _CLIENT/5619/ini/Cn_Res.ini | grep -i <word>`.
3. **Client data files** in `_CLIENT/5619/ini/`. When one holds the numbers, the server reads
   it from the client directory (like `Shop.dat`) instead of hard-coding them.
4. **The client binaries**. How to read the symbol-rich Mac client, and what was traced so
   far: [docs/client-notes.md](docs/client-notes.md).
5. **Other emulators**, for ideas only: `~/Developer/Albetros1.1` (NPC dialog scripts per NPC
   ID in `Albetros.Game/bin/Debug/NPCs/<id>.npc`, matching `Data/npcs.json`; many are a
   private server's custom features) and `~/Developer/worldconqueronline` (how `cq_action`
   types work, no NPC data).

Before building on a client→server packet, check the 5619 client actually sends it for that
feature. Other emulators handle packets from later patches (item upgrades by dragging a gem
onto gear, Forge-style windows) that 5619 doesn't send; in 5619 most item and NPC features go
through NPC dialogs. When it's unclear, say so up front instead of guessing.

## Keeping this file

Edit this file only for something every future agent needs and can't learn from the code,
the git history or an issue: a rule from the user, or a trap that already cost real damage or
a redo. Nothing an agent already knows (what this file is, how git or the tools work). Before
adding, ask whether a code comment, a test or `docs/` is the better place; usually it is.
Traced client facts go to `docs/client-notes.md`, task notes to the issue. Keep entries
short, update or delete instead of appending, and keep the file under ~120 lines.

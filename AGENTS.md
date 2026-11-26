# fetih

A Conquer Online server for the 5619 client:
`src/Fetih.Account` (login), `src/Fetih.Game` (world), `src/Fetih.Web` (web backend),
`src/Fetih.Network` (sockets, ciphers, RPC), `src/Fetih.Core` (logging, migrations),
`src/Fetih.Shared` (RPC types), `src/Fetih.Patch` (launcher manifests), `src/Fetih.Bots`
(bots), `src/Fetih.TestClient` (headless client for tests). `client/` holds client build
inputs, `tools/build.cs` builds the client and web assets, and `web/` is the React
frontend. The web UI follows [docs/web-style.md](docs/web-style.md): read it before any UI
work. See [docs/overview.md](docs/overview.md) for the full project picture.

## Rules

- When the real client disagrees with the code, the client wins.
- Comments only for what the code can't say: packet layouts and non-obvious reasons.
- Never write where a rule was learned from (another emulator's name, a forum, "5808
  source") in code or commit messages. State the rule. Mentioning the 5619 client is fine.
- Commits: one short line, no trailers. Never push.

## Commands

```bash
podman compose -f deploy/compose.yaml up -d db              # MariaDB (integration tests need it)
dotnet test Fetih.slnx
cd src/Fetih.Game && dotnet run                             # likewise Fetih.Account, Fetih.Web (:5820)
cd web && npm run dev                                       # frontend (:5173); npm run build, npm test
FETIH_BACKEND=https://test.<domain> npm run dev             # Vite against a remote web (see web/vite.config.ts)
dotnet run tools/build.cs client --ip <ip>                  # build client into _CLIENT/fetih (or `all`)
cd src/Fetih.Bots && dotnet run -- --Groups:0:Count=300     # bots against the running servers
```

A public server runs `deploy/compose.yaml` with `--profile deploy` and `deploy/compose.env.yaml`
per environment: [docs/deploy.md](docs/deploy.md). Settings from `.config` files can be
overridden with `FETIH_GAME_`, `FETIH_ACCOUNT_`, `FETIH_WEB_`, and `FETIH_BOTS_` environment
variables (`FETIH_GAME_Database__Hostname=db`).

## Don't break the user's setup

- The user often has `Fetih.Game` and `Fetih.Account` running from `src/`, with a character
  logged in from a Windows VM. For a smoke test, run on other ports
  (`--GameNetwork:Port=... --RpcNetwork:Port=...`) and stop only the PID you started. Never
  `pkill -f Fetih.Game`. Starting the game server also runs startup jobs against the dev DB.
- A new table or column is a migration: `sql/migrations/<game|account>/NNNN-name.sql`,
  applied by the server at startup and recorded in `schema_migrations`. Never edit an applied
  migration or the baseline `fetih_account.sql` / `fetih_game.sql` (startup refuses); add a
  new migration. Don't `ALTER` the dev database by hand.
- Never pipe `.sql` files into a live database container by hand (an old baseline with `USE`
  and `DROP TABLE` wiped every account and character once, 2026-11-23). To compare schemas,
  read `information_schema` or `SHOW CREATE TABLE`.

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

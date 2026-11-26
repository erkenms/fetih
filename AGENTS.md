# fetih

Conquer Online server for the 5619 client. Projects in `src/`, client build in `client/` and
`tools/build.cs`, React in `web/` until the .NET rewrite (#93) replaces it.
UI work: read [docs/web-style.md](docs/web-style.md) first.

## Rules

- Real client disagrees with the code → the client wins.
- Comments only for packet layouts and non-obvious reasons.
- Never name where a rule came from (other emulators, forums) in code or commits. 5619 client is fine.
- Commits: one short line, no trailers. Never push.
- Write short: issues, comments, docs, this file. A few words of direction beat full
  sentences. People lose context faster than you.

## Commands

```bash
podman compose -f deploy/compose.yaml up -d db              # MariaDB, for integration tests
dotnet test Fetih.slnx
cd src/Fetih.Game && dotnet run                             # likewise Account, Web (:5820)
cd web && npm run dev                                       # React (:5173)
dotnet run tools/build.cs client --ip <ip>                  # client into _CLIENT/fetih (or `all`)
cd src/Fetih.Bots && dotnet run -- --Groups:0:Count=300     # bots
```

Deploy: [docs/deploy.md](docs/deploy.md). Env overrides: `FETIH_GAME_Database__Hostname=db`
(likewise `FETIH_ACCOUNT_`, `FETIH_WEB_`, `FETIH_BOTS_`).

## Don't break the user's setup

- Game and Account usually run from `src/`, with a character logged in from a Windows VM.
  Smoke tests on other ports (`--GameNetwork:Port= --RpcNetwork:Port=`); stop only your PID.
  Never `pkill -f Fetih.Game`. Startup jobs run against the dev DB.
- Schema change → new `sql/migrations/<game|account>/NNNN-name.sql`. Never edit an applied
  migration or the baselines (startup refuses). No hand `ALTER`.
- Never pipe `.sql` into the database by hand: it wiped every account once (2026-11-23).
  Compare schemas with `information_schema` or `SHOW CREATE TABLE`.

## How the game behaves

The user tests every commit in the Windows 5619 client. Evidence, strongest first:

1. Help pages: `~/Developer/co_help_files/` (`INDEX.md`). Grep before building a feature; say
   which page decided it. Publisher's text, never in this repo.
2. Strings: `iconv -f GBK -t UTF-8 _CLIENT/5619/ini/Cn_Res.ini | grep -i <word>`.
3. Data files in `_CLIENT/5619/ini/`. If one holds the numbers, read it (like `Shop.dat`).
4. Binaries: [docs/client-notes.md](docs/client-notes.md).
5. Other emulators, ideas only: `~/Developer/Albetros1.1` (NPC scripts in
   `Albetros.Game/bin/Debug/NPCs/<id>.npc`, often custom), `~/Developer/worldconqueronline`
   (`cq_action` types).

Check 5619 actually sends a packet before building on it. Most item and NPC features go
through NPC dialogs, not later-patch windows. Unsure → say so.

## Keeping this file

Only rules from the user and traps that cost real damage. Client facts →
`docs/client-notes.md`, task notes → the issue. Update or delete, don't append.

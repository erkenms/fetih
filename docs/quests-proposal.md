# Quests and daily activities: proposal

Status: study only, nothing implemented (2026-11-24). Evidence is the 5619 Windows client's
`ini/` files and server logs of real play, with the Mac client's code for how packets are
read. No help page covers quests or daily activities: `grep -i quest` over the help files only
finds "completing some quests" in the upgrade page.

## What the 5619 client has

Three separate things, of which only the first needs a server.

1. **The quest window** (`CDlgMissionSystem`, with the tracker `CDlgTasktTrace`), driven by
   `ini/Questinfo.ini` (named in `Conquer.exe`). The client draws the whole quest list, text,
   rewards, objectives, the "!" and "?" marks over NPCs and auto-walking to them from the ini
   on its own. From the server it needs only each quest's **status**, the **counters** of its
   kill/use objectives, and a remaining time for timed quests.
2. **Today's activities** (`CDlgTaskToday`, `CActivityMgr`), driven by `ini/Activity.ini`
   (also named in `Conquer.exe`): 26 scheduled events (Power Arena, Team PK, Guild PK, Dis
   City, ...) with days, level ranges and hours, timed by the client's own clock. Its only
   packet is the **Attend** button.
3. `ini/DailyActivity.ini` (event announcements with NPC links) is **not named in either
   client binary**, so 5619 doesn't load it. There are **no daily activity points** in 5619:
   nothing in the client counts or shows them.

### Questinfo.ini

486 quests in sections `[1]`..`[486]`, plus `[TaskType]` (7 tabs), `ActionBase=20000000`
and `RecommendInterval=300`. Keys per quest: `MissionId`, `TypeId`, `Name`, `Lv_min`,
`Lv_max`, `Prequest` (`a|b|c`, 0 for none; a negative ID is checked differently, see below),
`Profession` (list or 0), `Sex`, `Map`, `FinishTime` (time limit; 120 on one quest),
`BeginNpcId`/`FinishNpcId` (`npc id,map,x,y,name,city`), `Prize` (display text only),
`IntentionDesp`, `IntentAmount`, `Intention1..n`, `Content`, `CompleteFlag`, `ActivityType`,
`ActivityBeginTime`/`EndTime`.

| Tab (TypeId) | Count | What |
|---|---|---|
| 1 Recruit Quest | 58 | newbie chain at TC Captain, Armorer, Storekeeper... (`608` → `650` → `651` ...) |
| 2 Tutorial Quest | 10 | Guild, Steed, Quiz Show, CP and Shopping Mall, Lottery, Nobility, Refinery |
| 3 Daily Quest | 30 | Horse Racing, Power Arena, Couples PK, Escort Mission, Missing Pages, Elite PK, Weekly PK ... |
| 4 Equipment Bonus | 149 | empty placeholders (no name, NPC or objective) |
| 5 Event | 51 | holiday events (Lantern, Easter, Dumplings...), `CompleteFlag=3` on 41 |
| 6 Nezha's Feud | 13 | a chain at Nezha in Desert City, level 137+ |
| 7 Region Quests | 175 | story chains around the cities (Father & Daughter, Bandits' Plot, Ratling King...) |

Objectives are markup inside `IntentionN`:
- `[chk_quest <task>,<index>,<?>,<target>]`: a server counter, e.g. `Slay [mob Pheasants,...]
  [chk_quest 608,1,1,10]` is counter 1 of task 608 reaching 10. 124 quests use it.
- `[chk_item <itemtype>,<amount>]`: the client counts the item in the inventory itself
  (`CHero::GetSameItemNum`). 120 quests.
- otherwise plain text ("Pay a visit to the [npc Armorer,...]"): done when the NPC says so.

## Packets

All four are already sent by the Windows client in play (server logs of 2026-11-18 to
2026-11-24: 989 MsgTaskStatus, 29 MsgTaskDetailInfo, MsgAction 166 with data 6).

### MsgTaskStatus 1134

`8 + 12n` bytes, at most 80 entries: action u16 at 4, count u16 at 6, then from 8 each entry
is task ID u32 at +0, status u16 at +4, 2 bytes padding, remaining time i32 at +8. (The
comment in `MsgTaskStatus.cs` calls the last 8 bytes padding; +8 is the time.)

Status values (`CMissionSystemData`; a quest starts at 2 when the ini loads):

| Status | Meaning |
|---|---|
| 0 | accepted, in progress (with a met objective it puts "?" on the finish NPC) |
| 1 | completed (what a positive `Prequest` needs) |
| 2 | not accepted (puts "!" on the begin NPC when level, class and prerequisites fit) |
| 5 | failed, out of time |

Client → server:
- **3** at game start (`CMyShellDlg::StartGame` → `RequestTaskStatus`): every quest ID in
  the ini, status 0, in 80-entry packets (6 or 7 packets of 968 bytes).
- **1** when the player presses Accept in the quest window and confirms (message box 3499 →
  command 3406): one entry with the task ID. The client then auto-walks to the begin NPC.
  Seen in the logs for 6001 (Couples PK) and 6072 (Elite PK).
- **4** with status 5 when a timed quest's time runs out (`CountDownLimitTask`).

Server → client (`CMsgTaskStatus::Process`):
- **3** status list: per entry, sets the status; status 5 marks it failed and resets it to 0;
  a non-zero time on an unfinished quest starts its countdown. Each entry with status 0 is
  added to the accepted list. For some quests (field `+0x14 == 2` of the ini data, most likely
  `CompleteFlag=2`) the client then asks for its counters with MsgTaskDetailInfo.
- **1** accepted: status 0, optional countdown, joins the accepted list and the tracker.
- **2** given up: status 2 again, counters cleared, removed from the tracker.
- **4** completed: status 1, screen effect, removed from the tracker.
- **8** failed: screen effect, marked failed.
- **9** restarted: status 0 with a new time.

Abandon is not a MsgTaskStatus: the Abandon button (message box 3498) sends **MsgTaskDialog
action 102** with TaskID = 20000000 (`ActionBase`) + the quest ID. Today it is logged as an
unhandled dialog action. The server should answer MsgTaskStatus 2.

### MsgTaskDetailInfo 1135

40 bytes: action u16 at 4 (0), task ID u32 at 8, then seven i32 counters at 12..36
(counter index 1..7). The client sends it with the counters zeroed when the quest window or
tracker shows an accepted quest; the server answers the same layout with the counters
filled, which the client stores per quest and shows as progress (`chk_quest` index n is
counter n).

### MsgAction 166 (Today's activities: Attend)

`CDlgTaskToday::Attend` sends MsgAction 166 with Data at offset 8 = the `id` of the
`Activity.ini` entry (6 = Power Arena in the logs). Nothing in the client says what the
server does with it. Taking the player to the event's NPC (the coordinates the event's
quest gives in `BeginNpcId`) is the natural answer, but it's a guess: ask the user first.

## Task model

Don't port an action/task script engine. Keep quest behaviour in C# NPC dialogs (as today,
with `NpcTalk`), and give them a small shared state store.

**Static data, from the client:** load `ini/Questinfo.ini` at startup like
`HonorRewards.ini`, into `QuestInfo { Id, Type, Name, MinLevel, MaxLevel, Prerequisites,
Professions, TimeLimit, BeginNpc, FinishNpc, Counters[] (from chk_quest: index, target),
Items[] (from chk_item: type, amount) }`. That gives eligibility checks and objective
targets without copying numbers.

**Server-only data**, only what the ini doesn't say, in `Data/quests.json`, and only for
quests that are implemented:

```json
{ "id": 608, "repeat": "once",
  "kills": [{ "counter": 1, "monsters": [1], "map": 1002 }],
  "reward": { "exp": 3000, "items": [] } }
```

`repeat` is `once` or `daily` (Daily Quest tab). Rewards are here because `Prize` is display
text. Quests not listed stay at status 2, can't be accepted, and are refused with a short
message.

**State per character**, a new table (goes in `comet/sql/comet.game.sql`):

```sql
CREATE TABLE IF NOT EXISTS `task` (
  `character_id` int unsigned NOT NULL,
  `task_id` int unsigned NOT NULL,
  `status` tinyint unsigned NOT NULL,          -- 0 accepted, 1 done, 5 failed
  `counter1` int NOT NULL DEFAULT 0,           -- ... through counter7
  `accepted_at` datetime NOT NULL,
  `ends_at` datetime NULL,                     -- timed quests
  `day` date NULL,                             -- server-local day of the last completion, for dailies
  PRIMARY KEY (`character_id`, `task_id`)
);
```

No row means status 2. A daily quest whose `day` isn't today reads as 2 again. Rows go with
the character on delete, like flowers.

**Engine** (`Tasks/` folder, a `TaskStore` on `Client`):
- answer query 3 with every queried ID: the row's status (and remaining seconds), or 2;
- `Accept`, `Complete` (gives the reward, sends 4), `Abandon` (deletes the row, sends 2),
  `Fail` (sends 8), each persisting and sending its MsgTaskStatus;
- accept request 1: checks level, class, prerequisites, the quest being implemented, then
  sends 1, or a refusal in chat;
- `AddCounter(task, index, n)`: called from the monster-kill path for accepted quests with a
  `kills` entry; sends MsgTaskDetailInfo when it changes;
- MsgTaskDetailInfo query: answers the row's counters;
- MsgTaskDialog 102: `Abandon(TaskID - 20000000)`;
- time-out report 4/5: marks it failed when `ends_at` has passed.

NPC scripts then read like this (TC Captain's hunting step):

```csharp
if (tasks.Status(608) == TaskStatus.Accepted && tasks.IsMet(608))
    await tasks.CompleteAsync(608);
```

## What moves onto it

Nothing in today's code is a quest:
- `Npcs/Mines.cs` is a proverb quiz that gates the mine; it keeps no state.
- `Npcs/Rebirths.cs` takes an Exemption Token in place of the second rebirth quest.

So the system adds features rather than replacing code. `docs/npc-features.md` lists the NPCs
that wait on it: the city captains' hunting quests (TC/PC/DC/MC/BI Captain), the newbie
chain (Recruit tab), Nezha's Feud, Flame Taoist's stone chain, Desert City's Soldier Remains
and Village Head, Bird Island, Moon Box, and the second rebirth line at Celestine. A good
first milestone is the Recruit tab chain at TC Captain, Armorer, Storekeeper, Pharmacist and
Blacksmith (talk quests plus one kill counter), which tests every packet.

## Size

| Part | Lines (with tests) |
|---|---|
| MsgTaskStatus replies and actions 1/4, MsgTaskDetailInfo, MsgTaskDialog 102 | ~250 |
| Questinfo.ini parser (objective markup, NPC fields) | ~200 |
| `task` table, model, repository, delete with the character | ~150 |
| Engine: accept, complete, abandon, fail, counters, daily reset, time limits, kill hook | ~350 |
| `quests.json` loader | ~80 |
| First milestone: Recruit chain (~15 quests, five NPC scripts) | ~400 |

About 1,000 lines for the engine and ~400 for the first quests: two to three focused
sessions. After that each quest is content work, about 20-60 lines of dialog plus a JSON
entry, and most of the 486 quests need event maps, monsters or items the server doesn't have
yet.

Today's activities is separate and small: a MsgAction 166 handler and an activity-to-place
table, ~60 lines, once the user decides what Attend should do.

## Recommendation

Worth doing, because the client already shows a full quest window that today accepts
nothing: Accept walks the player to an NPC whose dialog knows nothing of the quest. If quests
aren't wanted soon, a cheap step is to answer query 3 with status 2 for all IDs and MsgTaskDetailInfo
with zero counters, and ignore accept and abandon quietly, so the logs stop filling. That
changes nothing on screen because 2 is already the client's default.

## Not confirmed

- Whether field `+0x14 == 2` (detail query after a status reply) is `CompleteFlag`.
- The time unit of `FinishTime` and of the remaining time in MsgTaskStatus (seconds is
  likely).
- What negative `Prequest` IDs mean: the client accepts them when that quest's status is
  below 2 (accepted or done).
- What the server should do on Attend (MsgAction 166).
- Traced in the Mac client; the Windows client agrees on every packet size and action seen
  in the logs (968-byte status packets, 40-byte detail queries, MsgTaskStatus 1 accepts).

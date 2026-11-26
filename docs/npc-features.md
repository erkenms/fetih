# Unhandled Feature NPCs

Triage of placed NPCs in `src/Fetih.Game/Data/npcs.json` that still fall through to the default dialog because they require server features (quests, exchanges, client windows, missing maps, or game events) rather than pure talker text or fixed teleports.

## Summary of triage (397 placed NPCs)

- **Already handled before triage:** 256 (13 talkers, 12 teleporters, 26 shops in `Shop.dat`, 54 scripted in `Dialogs.cs`, 7 storage/warehouse role 3, 144 market stalls role 16) + 2 non-dialog roles (`10020` Beautician role 5 in `Beautician.cs`, `6700` Guild War Pole role 10 in `GuildWar.cs`).
- **Moved to `talkers.json`:** 8 Birth Village tutorial talkers (`425`, `10004`–`10009`, `10055`).
- **Moved to `teleporters.json`:** 4 pure teleporters (`1338` Lottery exit, `10010` VillageGateman, `1342` Labyrinth 4 exit, `102063` GuildConductor4).
- **Non-interactive decorations / display NPCs:** 35 (27 role-26 city/terrain statues `5336`–`5341`, `5344`–`5349`, `5352`–`5358`, `5360`–`5366`, `3117`; 8 display steeds `5518`–`5520`, `5591`–`5594`, `5601`).
- **Remaining feature NPCs listed below:** 92.

---

## Lottery (Map 700)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 5669, 5670, 5671, 5675, 5676, 5677, 5678, 5679 | ValuableBox / Chest (8 boxes) | 700 | Lottery prize chests inside the Lottery room. Players open a chest using a Lottery Ticket to draw a random prize. | Lottery ticket consumption, prize pool table, daily limit tracking, and reward distribution. |

## Desert City (Map 1000)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 2004 | DCCaptain | 1000 | City Captain daily monster-hunting quest giver; refers higher-level players to Bird Island Captain. | Daily kill-quest tracking (3/day), level-bracket task assignment, EXP + Meteor rewards. |
| 2006 | MCCaptain | 1000 | Mystic Castle Captain daily monster-hunting quest giver; refers higher-level players to Bird Island Captain. | Daily kill-quest tracking (3/day), level-bracket task assignment, EXP + Meteor rewards. |
| 6135 | FrozenGrottoEnvoy | 1000 | Teleports players to Frozen Grotto Floor 1 (map `1762`, 635,379) or Floor 2 (map `1999`, 175,224). | Add maps `1762` and `1999` (and `2054`) to `Data/maps.json` and Frozen Grotto spawns/portals. |
| 6144 | FrozenGrottoGeneral | 1000 | Refers qualified players to Bird Island Captain / Frozen Grotto quests and lends a steed/teleport. | Quest progression and Frozen Grotto quest line. |
| 7833, 7836, 7838, 7841, 7844, 7845, 7846, 7848, 7849, 7850 | SoldierRemains (10 NPCs) | 1000 | Interactive quest corpses scattered in the Desert City region. | Quest state system for Desert City storyline/collection quest. |
| 30102 | VillageHead | 1000 | Desert City quest NPC ("Could you help me?"). | Quest state and objective tracking. |
| 101903, 101905 | DesertCityClan, DesertClan | 1000 | Clan Territory banners (role 31). | Clan War / Clan Territory occupation system (`clans.md`, `clan-qualifier.md`). |
| 600002 | Joe | 1000 | Quest NPC near Mystic Castle entrance. | Quest state system. |

## Twin City (Map 1002)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 31 | GeneralBraver | 1002 | Guide / combat quest NPC. (Note: the reference script is a custom private-server self-duel and was not ported.) | Original 5619 quest / veteran guide dialog. |
| 1061 | WhsGuardian | 1002 | Sets/changes/clears a warehouse password to protect stored items. | Warehouse password persistence and unlock prompt before opening warehouse. |
| 1152 | Simon | 1002 | Labyrinth guide: explains the 4 floors, teleports players into Labyrinth 1 (`1351`), and exchanges Sun/Moon/Star/Cloud Diamonds for rewards. | Labyrinth entry fee/teleport and diamond-to-reward exchange system. |
| 2001 | TCCaptain | 1002 | Twin City Captain daily monster-hunting quest giver; refers higher-level players to Bird Island Captain. | Daily kill-quest tracking (3/day), level-bracket task assignment, EXP + Meteor rewards. |
| 4452 | FlameTaoist | 1002 | Starts the 10 Flame Stones quest chain (gives initial stone `729960`) and exchanges the final stone (`729970`) for a Dragon Ball (`1088000`). | Complete Flame Stone quest chain (stones 2–8 and 10 are already in `GuildWarNpc.FlameStones`). |
| 4453 | 1stFlameStone | 1002 | Exchanges `729960` for `729961` as step 1 of the Flame Stone chain. | Wire into the Flame Stone chain alongside `4452` and `4461`. |
| 4461 | 9thFlameStone | 1002 | Exchanges `729968` for `729969` as step 9 of the Flame Stone chain. | Wire into the Flame Stone chain alongside `4452` and `4453`. |
| 4610 | OrchidGoddess | 1002 | Flower / festival event NPC. (Note: the reference script is a custom level-reset test tool and was not ported.) | Event / flower festival feature. |
| 4619 | DewCrystal | 1002 | Quest/event interactive object outside Twin City. | Quest/event state system. |
| 8588 | MonkZhen | 1002 | PvP / Capture the Flag (CTF) event entry NPC (`capture-the-flag.md`). Reference script teleports to map `1616` (not yet in `maps.json`). | Capture the Flag event scheduler, team sorting, map `1616`, and flag scoring. |
| 8695 | Luna | 1002 | Teleports players into the Moon Platform / Moon Box quest maps (`1100`–`1109`). | Moon Box / Moon Platform quest flow. |
| 10002 | Barber | 1002 | Changes character hairstyle for silver (500 silver classic / 10,000 silver New Dynasty). | Hairstyle catalog validation by gender/body and silver deduction (`MsgUserAttrib` hair update). |
| 10019 | ArtisanWind | 1002 | Role 33 equipment upgrader (`upgrade-items-procedure.md`: "Click on Artisan Wind (423,353) in Twin City to toggle on the Improvement window" for Meteor/Dragon Ball upgrades). | Verify 5619 client window command (`CDlgImprove` / `MsgItem` 19/20; see `docs/client-notes.md`) or dialog-based Meteor/DB upgrade flow. |
| 10063 | Shopboy | 1002 | Dyes equipped armor/headgear/shield for 1 Meteor. | Equipment color/dyeing dialog and Meteor consumption. |
| 20005 | Celestine | 1002 | 2nd Rebirth / Ancient Devil quest line NPC. | 2nd Rebirth quest chain and boss instances. |
| 101887, 101889 | TwinCityClan, WindPlainClan | 1002 | Clan Territory banners (role 31). | Clan War / Clan Territory occupation system (`clans.md`, `clan-qualifier.md`). |

## Stable (Map 1006)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 5482 | SteedKeeper | 1006 | Role 30 stable keeper; opens the Steed/Mount breeding/lineage window (window command 368 in reference script; see `mount-lineage-level.md`). | Verify 5619 client window command and mount breeding / lineage handler. |

## Birth Village (Map 1010)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 5672 | RecruitMaster | 1010 | Multi-page newbie tutorial dialog explaining classes and active/passive skills, then moves player near VillageGateman (`1010`, 89,43). | Multi-page tutorial / newbie quest script. |

## Phoenix Castle (Map 1011) & Sky Pass (Map 1012)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 35 | GemInsetter | 1011 | Role 7 gem embedding NPC (opens socket/gem window or dialog). | Verify whether 5619 client uses `role 7` window or dialog for gem embedding. |
| 2002 | PCCaptain | 1011 | Phoenix Castle Captain daily monster-hunting quest giver; refers higher-level players to Mystic Castle Captain. | Daily kill-quest tracking (3/day), level-bracket task assignment, EXP + Meteor rewards. |
| 6281, 6285 | BeautyHerb (2 NPCs) | 1011 | Gatherable quest herbs in Phoenix Castle. | Quest item gathering system. |
| 10034, 10035 | SpaceMark (2 NPCs) | 1011 | Sells SpaceMark teleport scrolls exclusively to Water Taoists. | Water Taoist profession check and SpaceMark scroll creation/purchase. |
| 30114 | GeneralWu | 1011 | Phoenix Castle quest NPC. | Quest state system. |
| 101891, 101893 | PhoenixCastleClan, MapleForestClan | 1011 | Clan Territory banners (role 31). | Clan War / Clan Territory occupation system. |
| 30055 | Daniel | 1012 | Sky Pass challenge entry NPC ("This is the way to Sky Pass. Are you here to challenge the Sky Pass?"). | Sky Pass dungeon / challenge quest progression. |

## Bird Island (Map 1015)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 2005 | BICaptain | 1015 | Bird Island Captain daily kill quests (Birdmen L87, HawKings L92, Bandit/Robbers L97), 3 times/day for half ExpBall EXP + 1 Meteor. | Daily kill-quest tracking (3/day), kill counter, EXP + Meteor rewards. |
| 4569 | YeSheng | 1015 | Bird Island quest NPC (referenced by `TimeDoor` 4575 in `talkers.json`: owns the Iron Poise for the stone doors). | Bird Island Iron Poise / Stone Door quest line. |
| 4706 | GuideZhang | 1015 | Bird Island guide / quest NPC. | Quest state system. |
| 5332 | StrongBeast | 1015 | Bird Island quest/event NPC. | Quest/event system. |
| 8400, 8401 | Lotus (2 NPCs) | 1015 | Interactive quest/event lotus objects on Bird Island. | Quest/event item gathering. |
| 101899, 101901 | BirdCityClan, BirdIslandClan | 1015 | Clan Territory banners (role 31). | Clan War / Clan Territory occupation system. |

## Ape City (Map 1020) & Phoenix Mine (Map 1025)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 2003 | ACCaptain | 1020 | Ape City Captain daily monster-hunting quest giver; refers higher-level players to Bird Island Captain. | Daily kill-quest tracking (3/day), level-bracket task assignment, EXP + Meteor rewards. |
| 3215 | SolarSaint | 1020 | Demon Exterminator / Dis City quest NPC. | Dis City / Demon Exterminator quest event. |
| 3610 | Carl | 1020 | Flower collection quest NPC (WhiteFlower and RedFlower made from 6 petals, Pistil, and Stalk). | Flower-assembly quest and reward exchange. |
| 10037, 10039 | SpaceMark (2 NPCs) | 1020 | Sells SpaceMark teleport scrolls exclusively to Water Taoists. | Water Taoist profession check and SpaceMark scroll creation/purchase. |
| 101895, 101897 | ApeCityClan, LoveCanyonClan | 1020 | Clan Territory banners (role 31). | Clan War / Clan Territory occupation system. |
| 4502 | ShenJunior | 1025 | Quest NPC inside Phoenix Castle Mine. | Mine quest line. |

## Market (Map 1036) & Poker Maps (1858, 1860)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 923 | LadyLuck | 1036 | Lottery entry NPC (teleports players into Lottery map `700` using a Lottery Ticket / daily limit). | Lottery entry validation and Lottery Ticket / silver fee handling. |
| 941 | ProficiencyGod | 1036 | Consumes EXP Balls to boost weapon proficiency (One-handed, Two-handed, Boxing, Shield). | EXP Ball to weapon proficiency exchange. |
| 1550 | Socketsmith (Lee Zhen) | 1036 | Role 37 weapon socketing NPC: puts 1st/2nd socket into weapons for Dragon Balls (opens window 425 in reference script). | Verify 5619 client socketing window/packet (`OpenWindow(425)`) and Dragon Ball weapon socketing logic. |
| 2065 | GemComposer | 1036 | Role 35 gem composer: upgrades 10 Normal gems to 1 Refined gem, or 10 Refined gems to 1 Super gem. | Gem composition exchange (or verify if 5619 client has a gem compose window). |
| 3825 | UnknownMan | 1036 | Market beggar/charity quest NPC (asks for 100 silver). | Charity/blessing quest state and reward. |
| 5004 | MillionaireLee | 1036 | Packs 10 Meteors into a MeteorScroll (`720027`), 10 Dragon Balls into a DBScroll (`720028`), +1 Stones, or Normal Gems. | Item packing/scroll exchange dialog. |
| 6297 | GameManager | 1036 | Teleports players to Texas Hold'em Poker Room (`1858`, 56,64) or VIP Hall (`1860`, 57,63). | Add maps `1858` and `1860` to `Data/maps.json` and Texas Hold'em Poker tables. |
| 6298, 6299 | CasinoHostess (2 NPCs) | 1858, 1860 | Teleports between Poker Room (`1858`), VIP Hall (`1860`), and Market (`1036`, 211,196). | Add maps `1858` and `1860` to `Data/maps.json` and Texas Hold'em Poker tables. |
| 7538 | RefineryStabilizer | 1036 | Role 38 NPC (`refinery.md`, `refinery-stabilization.md`: "find the Refinery Stabilizer in the Market (299,212)"). Makes temporary 7-day Refinery permanent using Permanent Stones / Big Permanent Stones (opens window 448). | Refinery system and stabilization window/packet. |
| 7763 | PurificationStabilizer | 1036 | Role 38 NPC: fuses Dragon Souls / Artifacts into equipment and purifies equipment (opens window 455). | Dragon Soul / Artifact purification system and window/packet. |
| 8233 | HeavenlyMaster | 1036 | Thrilling Spook / Exorcism quest NPC: awards Exorcism Token + 50 Study Points per kill, swaps 3 Tokens for an Exorcism Pack. | Exorcism quest tracking, Study Points, and token exchange. |
| 9999 | DiceKing | 1036 | Role 19 Dice / Gambling table NPC. | Gambling / Dice mini-game protocol (`MsgBet` / table state). |
| 10065 | GodlyArtisan | 1036 | Repairs max durability of equipped items (Talisman, Hat/Earring, Necklace, Coat, Weapon, Ring/Bracelet, Boots). | Max durability repair dialog and cost calculation. |
| 300000 | Shelby | 1036 | Virtue Points exchange NPC ("transfer virtue points into useful items"). | Virtue Points tracking and reward exchange shop. |

## Labyrinth (Maps 1351–1353) & Frozen Grotto 4 (Map 2054)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 1339 | Blacksmith (Labyrinth 1) | 1351 | Teleports to Labyrinth 2 (`1352`, 27,230) in exchange for a SkyToken (`721537`), or returns player to Twin City (`1002`, 430,378). | Labyrinth token check/consumption and floor progression. |
| 1340 | Blacksmith (Labyrinth 2) | 1352 | Teleports to Labyrinth 3 (`1353`, 27,269) in exchange for an EarthToken (`721538`), or returns player to Twin City (`1002`, 430,378). | Labyrinth token check/consumption and floor progression. |
| 1341 | Blacksmith (Labyrinth 3) | 1353 | Teleports to Labyrinth 4 (`1354`, 6,290) in exchange for a SoulToken (`721539`), or returns player to Twin City (`1002`, 430,378). | Labyrinth token check/consumption and floor progression. |
| 7764 | FrozenGrottoGuardian | 2054 | Guardian on Frozen Grotto F4 (387,460) that admits players to Frozen Grotto F5 (referenced by `6135` FrozenGrottoEnvoy). | Add map `2054` and Frozen Grotto F5/F6 maps to `Data/maps.json` and Frozen Grotto progression. |

## PK Jail (Map 6000)

| ID | Name | Map | What it does | Server feature needed |
|---|---|---|---|---|
| 1000 | CPAdmin | 6000 | Jail CPAdmin (lookface 3331 = `CPAdmin` in `npc.ini`). (Note: the reference script `1000.npc` is a mis-mapped Training Ground Boxer script and was not ported.) | Confirm 5619 Jail CPAdmin behavior (e.g. exchanging Dragon Balls for CPs or paying bail in CPs, similar to Market `2071`). |
| 1001 | PrizeOfficer | 6000 | Jail PrizeOfficer (lookface 1067 = `PrizeOfficer` in `npc.ini`): claims system/event prizes ("You can come to claim your prize if the system notifies you that you have got prize."). | General system prize claim queue (separate from detained-item PrizeOfficer `4542`). |

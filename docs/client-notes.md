# Client notes

Facts traced in the client binaries. One finding = a few lines, with the client and date.
Already in a code comment → not here.

## Reading the Mac client

`_CLIENT/MACcon1133.dmg` (2012) is closest to 5619 (`MACcon1068.dmg` is older). A separate
port: confirm in the Windows client, which wins. i386 only, doesn't run.

- Mount: `yes | hdiutil attach -readonly -nobrowse -mountpoint <dir>` (EULA prompt).
- Binary: `Conquer.app/Contents/ConquerGameExe.app/Contents/MacOS/ConquerGameExe`.
- Not stripped (~180k symbols): `otool -tV -arch i386 -p __ZN10CMsgAction7ProcessEPv <bin> | c++filt`,
  or Ghidra.
- Switch jump tables: `calll x; popl %reg` is the base; case n's int32 is at file offset
  `base + off + 4*(n - low) - 0x1000`, target = base + it.
- Login is native there (`CDlgLoginFlash`, `CDlgLoginServerList`): reference for a native login.

## Windows client login

- The login (`CDlgLoginFlash`, DIALOG 305) is one Flash control playing
  `data/main/start.dat`, a SWF that draws the fields, buttons and server list (Server.dat
  `FlashName`/`FlashIcon`). It sends FSCommands `CMD_NAME`, `CMD_PASSWORD`,
  `CMD_SELECTGROUP`, `CMD_SELECTSERVER`, `CMD_ENTERGAME` and others; the client sets
  `LOGINUSER`, `SERVER`, `REGION` and `VCTOFLASH`.
- Character creation is Flash too (`data/main/role.dat`: `CMD_NICK`, `CMD_VOCATION`,
  `CMD_BODYSIZE`).
- `tqwea.dll` is a weather widget, `TQAnp.dll` a small crypto helper. `Conquer.exe`
  inline-hooks wininet's `InternetOpenUrlA`. The client's own log is `debug/<date>.log` (GBK).
- Wine can't run it (Flash.ocx faults under WoW64, 2026-11-23).

## Findings

- **Files the client writes** (2026-11-26, Windows 5619 strings and a run's file times):
  `ini/GameSetUp.ini`, `Log/<account>/Setup.ini`, `debug/<date>.log` and `.dmp`,
  `ScreenShot/%u.jpg`, `ChatLog.log`, `debug.log`, `leavewordsystem.log`, root `p2p*.log`.
  `GraphicData.dll` also rewrites `ini/c3.wdb` (a cache built from the ini files, through
  `c3.wdb.tmp`). The patch manifest flags these player-owned (`PlayerOwnedFiles`).
- **Screen mode** (2026-11-27, Windows 5619 `Conquer.exe` VA 0x65e2c7, 0x65e37a): `[ScreenMode]`
  of `ini/GameSetUp.ini`. `ScreenModeRecord` 0 is 800×600 with `FullScrType` 1, 1 800×600 with 0,
  2 1024×768 with 1, 3 1024×768 with 0; 4 takes `ScrWidth`, `ScrHeight` and `FullScrType` as
  written, unclamped. Missing keys default to 2 (0 when the desktop is under 1024×768) and
  `FullScrType` 1. Which `FullScrType` is full screen, and whether odd sizes work, not yet
  checked in Windows. `Conquer.exe` isn't DPI-aware: at 150% a 2560 screen is 1707 to it.
- **play.exe** (2026-11-27): its high quality button writes `[GameMode] HighTimes` + 1 and
  `GameModeRecord=1` (normal 0, low 2); `Conquer.exe` reads only `GameModeRecord`. The client
  restarts itself (message 85 with 4, VA 0x5d7e93) by `ShellExecute` of `%s/play.exe`;
  `client/patches.json` makes that `Fetih.exe`. The old Play screen is `ini/PlayGui.ini`:
  `Play/Main.bmp` with `Main.rgn` (a u32 length, then RGNDATA), `Play/MainS.bmp` at 594,0, green
  (00ff00) cut away.
- **Role camera** (2026-11-26, Mac 1133): roles are drawn in an orthographic camera one world
  unit to a pixel (`CMyBitmap::GameCameraCreate`: eye (w/2, -1000, h/2) looking along +y, up
  -z). `C3DRole::Draw` turns the model around z by its angle, then around x by
  `g_nObliqueAngle` (-30). An NPC's angle is -45 * (direction + 1) (`C3DTaskNpcEx::SetPos`).
  A skinned vertex is position * the mesh's initial matrix * its bone's matrix. The asset
  build's look pictures (`tools/assets/Looks/`) use this.
- **Role scale** (2026-11-26, Mac 1133): `CRole::Show` draws a role at
  ZoomPercent * `g_fRoleScale` / 100 * a mount factor, on top of the camera above.
  `g_fRoleScale` is 0.75, `CRole::Init` sets ZoomPercent to 100 and the mount factor to 1
  (`CRole::SetMount` changes it). Maps are one background pixel to a screen pixel, so a look
  picture shows at 0.75 of its size next to the map.
- **Others' level-up** (2026-11-23): in the Mac client MsgAction 92 on another player calls
  `CPlayer::DoLevelUp` (halo and sound), but the Windows client doesn't show it to others.
- **Marriage** (2026-11-23): posted command 1067 → cursor 18 → click → MsgInteract 8.
- **Opening windows** (2026-11-23): MsgAction 116 (ClientCommand) posts its Command straight
  to the client as a window command; MsgAction 126 (ClientDialog) maps window 1 to command
  1086 and 4 to 1090. 1086 opens Compose (`CDlgEquipAppend`, sends MsgDataArray 2036), 1091
  Enchant (`CDlgAttach`, sends MsgItem 28). In the Windows 5619 client composing goes through
  window 2 (`CDlgFuse`, MsgFuse 1028), which is what the Wuxing Oven opens. The command to
  dialog table is the jump table in `CMyShellDlg::WindowProc` at base 0x57a4d3 + 0xfaad for
  commands 1003 to 1309. The Improve window (`CDlgImprove`, MsgItem 19/20) has no command.
- **Monster deaths** (2026-11-23, `CMsgInteract::Process` case 14): MsgInteract Kill on a
  monster sets a die timer command (0x33) a short delay later instead of killing at once, and
  `CPlayer::SetCommand` clears pending timer commands, so a spell hit shown after that stands
  the monster up at 0 HP. Hence the server sends a spell's Kill only once the client shows the
  hit. The delay is WoundDelay of the attacker's `ini/ActionDelay.ini` entry, keyed
  `dir*1e9 + dist%10*1e8 + look%10*1e7 + weapon*1e4 + action`
  (`CRole::GetActionDelayInfoKey`), else 200 ms; the ini lists only swings (310-341,
  401-403), so a spell's kill always falls 200 ms after its hit shows.
- **MsgMagicEffect** (2026-11-24): each 32-byte target entry goes to the client as
  `MagicEffectRoleInfo` as is. The caster turns toward the first target and keeps their facing
  when the list is empty, so self-casts that list the caster turn them east. ClientRepresent
  107 (`CMagicEffectMultiHit`, Whirlwind Kick only) takes X as its hit count (< 100), splits
  the damage over the hits and lifts targets 15 per hit.
- **Jumps** (2026-11-24, `CRole::GetFrameInterval`): the jump action (130) is 20 frames
  (`c3/<body>/<weapon>/130.C3`), each `max(33, 33 * world distance / 300 + 1)` ms. The client
  lets a spell be cast mid-jump, so the server holds it until landing (`Client.LandsAt`).
  Frame time is halved in Cyclone (flag 23), else 2/3 in Oblivion (112); a jump in Fly (27)
  is action 510, 40 frames. Steed speed (`GetRideFrameInterval`) skips jump actions 130, 295
  and 527. Swings are 2/3 in Superman (18), Cyclone or flag 21. The move-speed field
  (`CRole::ChangeMoveSpd`) is only set by speed-change effects, unused in 5619.
- **Others' jumps** (2026-11-25, Mac 1133 `CMsgAction::Process` case 137): a jump for the
  hero or its pet only realigns them; any other role, a monster included, gets a jump
  command to the cell in Command. That's how guards jump at players. Not yet seen in 5619.
- **Booth close button** (2026-11-24): slot 1 of the XP dialog (`CDlgXp::SetBooth`), shown
  while the map type has 0x10 (BoothEnable) and the booth is open. Clicking it posts command
  60 and calls `CBoothManager::Close`, which sends MsgAction 114 (the client sends 111 to
  open). From the server only 111 does anything. The only other close is a map load.
- **Weather** (2026-11-24): the client draws map weather only from MsgWeather 1016, and a new
  map starts with none. `ini/weather.ini` is effects around roles by terrain, not map weather.
  Codes are `CWeather::CreateEnv`'s: 0 none, 1 rain, 2 snow, 3-12 leaves, 13 sand, 14-18
  flowers, 19 fireflies, 20 clouds.
- **Anti-cheat** (2026-11-24, `CAntiCheatMgr`): MsgData 4 asks how many MsgTalk, MsgInteract
  or MsgAction the server received (answer MsgData 3; a mismatch makes the client report).
  Reports come as MsgInteract 16/19/23/30/33/34/38 or MsgAction 127-150 with the cheat code in
  Data; the server logs them.
- **Items** (2026-11-24): MsgItem 49 is the split dialog (`CDlgDevideItem`, in the Windows
  5619 exe too): the stack's ID, then how many leave it; the client wants a stack of 2+ and a
  free slot. Nothing sends 7, 8, 25 or a merge; `CHero::IsPropFull` only asks whether a new item
  would fit on a stack (itemtype field 47, `max_overlay`). Arrows' stack is their durability.
  The count is a u16 at 64 in MsgItemInfo, 72 in MsgItemInfoEx, 44 in each MsgPackage entry
  (Mac 1133). MsgItem 15 is the VIP (level 2+) whole-body repair.
- **Black list** (2026-11-24): client-only (`CDlgBlack`, `BlackNameRecord` in the hero config,
  up to 50 names, chat dropped in `CMsgTalk::ProcessCheckedMsg`); no packet. MsgEnemyList 1041
  is the Enemy window asking for the list when opened.
- **MsgNpcInfoEx 1109** (2026-11-24): names start at 36; role 10 is the guild pole, 26 a gate.
  Gate looks open/closed in `ini/terrainnpc.ini` (241/251 left, 271/281 right).
- **MsgPlayer flower charm** (2026-11-24): a `Dynarank.ini` ID, assumed at offset 123 in 5619
  (the Mac client has 4 extra bytes before it). Not confirmed in the Windows client.
- **Monk auras**: MsgAura 2410 (`CMsgAura`, `CAuraMgr`); the icon's tooltip level and percent
  come from it, not from the status flag.
- **Lucky time** (2026-11-24, `CHero::ProcessLuck`): the client counts it itself between
  updates. Status 30 (LuckDiffuse, set by the Bless spell 9876) gains 3x the time passed, 31
  (LuckAbsorb) 1x, otherwise it runs down; capped at 7,200,000 ms. MsgUserAttrib 28 sets it in
  ms (icon 28). Gaining 30 shows "You start to pray", 31 "You are ready to pray"; with 30 the
  client blocks emote changes for 5 s. No help page gives its effect on drops or upgrades.
- **Sub-classes** (2026-11-24): `ini/SubProfessionInfo.ini` holds requirements and
  `EffectProportion` per phase, `ini/levexp.dat` (seed 0x4D2) the study points per level. The
  attribute window reads only the active sub-class (`CPlayer::GetCurrentSubProfession`).
  `AccessionMapID` says 6072, which 5619's GameMap.dat lacks; the masters stand in 1004.
- **Guild donation ranking** is MsgFactionRankInfo 2101 (`CDlgFactionContriRank`), not
  MsgContribute 2067, which is the mentor contribution (`CEducationMgr`).
- **Detained items** (2026-11-24): MsgDetainItemInfo 1034, MsgDetainItemUpdate 1035. MsgAction
  126 command 336 opens `CDlgPKDetain`, 337 `CDlgPKReward`; both send MsgAction 153 on open,
  then MsgItem 32 (redeem) or 33 (claim). Days >= 8 is expired.
- **Reincarnation, name change, degrade** open native windows through MsgAction 126: 485
  (`CDlgMeteSpecial`, MsgMeteSpecial 1066), 489 (`CDlgChangeName`, MsgChangeName 2080), 506
  (`CDlgEquipDegrade`, MsgItem 54, 54 CPs). The client keeps its old name until relog.
- **Wanted / bounty** (2026-11-24, `CMsgName::Process`): MsgName 8 and 15 only store a name;
  when a player of that name comes into view the client sends an empty MsgName 13/14. No
  NPC, window or item in 5619 posts or claims a bounty, so the server doesn't implement it.
- **Quests** (2026-11-24, `CMsgTaskStatus::Process`, `CMissionSystemData`): the quest window
  runs from `ini/Questinfo.ini`; status 0 accepted, 1 done, 2 not accepted (the default), 5
  failed. Abandon is MsgTaskDialog 102 with TaskID 20000000 + quest ID; Today's activities'
  Attend is MsgAction 166 with the `Activity.ini` id. `DailyActivity.ini` isn't loaded. Details
  in #106.
- **Refinery** (2026-11-24, Mac client `CMsgItemStatus`, `CDlgSolidify`, `CQuenchStatusMgr`):
  using an item from `ini/ItemQuench.ini` turns the cursor into a picker (command 0x28); clicking
  gear asks to confirm, then MsgQuench 2076 (mode u16 at 4, gear at 8, refinery item at 12). The
  client only checks it's equipment; which gear fits is itemtype field 53 ("Headgear", "Bow"...).
  MsgItemStatus 2077: count at 4, 28-byte entries (item, mode, kind, level, power, seconds left,
  unused); modes 0 set temporary, 1 clear it, 3 set permanent, 4 both and refresh the window, 5-9
  artifacts. Kind is the `ItemQuenchTipInfo.ini` section 301-319 (`CHero::GetMaxLife` adds power
  only for 301). MsgItemInfo makes a new client item, so states are sent again after it. Window
  448 (MsgAction 126) posts command 3513, the stabilization window, which sends MsgSolidify 1038
  (mode at 4, gear at 8, count at 12, stones from 16); points from `ini/ItemSolidify.ini`, needed
  per level from `ini/info.ini` [Solidify]. Battle power ignores refinery. Not confirmed in the
  Windows client.
- **Clan War** (2026-11-24, Mac `CMsgFamilyOccupy::Process`, `CDlgFamilyOccupy`, `CMyShellDlg::WindowProc`
  commands 3014-3032): clicking a role 31 banner sends MsgFamilyOccupy 1313 action 6 with the NPC
  at 12. Buttons send Apply 0, Cancel 1, Give Up 2, Join 8, Claim 18 with the NPC at 8; 10 follows
  the re-register confirmation (unverified), 5 comes from an unknown command. From the server: 3
  abandoned (TEXT24), 4 re-register prompt (TEXT23), 6 the dialog, 7 the war invitation
  (`CDlgOccupyConfirm`, TEXT40, accept sends 8 with the invite NPC), 11/19 reward effect on the
  player at 132, 12-17 and 20 message boxes (TEXT29, 25, 26, 27, 28, 39, 42). Layout is in
  `MsgFamilyOccupy.cs`; fields at 108, 129-131 are unknown. MsgFamily Query string 3 is
  `%u %d %u %u %d %d %d` (challenged NPC, days, pack, next pack, challenged, income claimed, exp
  claimed), strings 4 and 5 the occupied and challenged map names; MsgFamily 29 carries the same
  seven numbers. The battlefield is map 3024 (`family.7z`); packs are 722454-722478 per city.
- **Elite PK** (2026-11-24, Mac `CMsgElitePK*::Process`, `CElitePKGameMgr`): MsgElitePKArenic 2218
  action u16 at 4: 1 closes the windows, 2 match start (score window, seconds u16 at 44), 3 result
  (`sports_victory` if u32 at 8 is 1, else `sports_failure`), 4 leaves the match. MsgElitePKScore 2222
  is MsgArenicScore's layout. MsgPKEliteMatchInfo 2219 (the bracket, 124 bytes when the client sends
  it): action u16 at 4, u32 at 6, total matches u32 at 10, group u16 at 14, stage u16 at 16 (3-4
  knock-out, 5 last eight, 6 semifinal, 7 third place, 8-10 final), match seconds u16 at 18, count
  u32 at 20, then 100-byte `PKELITEMATCH_INFO` entries from 24, not traced further.
  MsgElitePKGameRankInfo 2223 (56 bytes from the client: u16 at 4, u32 at 8) is the top-8 list. The
  server sends only 2218 and 2222 so far. Schedule and groups come from `ini/Activity.ini`; the
  waiting rooms 2075-2078 named in GameMapEx.ini aren't in GameMap.dat, so the server uses 2068
  (`pk.7z`). Prize packs 720714-720729 (group + 4 × place), per-match EXP pack 720713.
- **Capture the Flag** (2026-11-25, Mac 1133 `CMsgWarFlag::Process`, `CWarFlagMgr`, `CHero::OnCellToBeClient`,
  `CHero::OnIntoRegion`): MsgWarFlag 2224 is laid out in `MsgWarFlag.cs`. The client sends 6 (flag NPC at 8)
  when the hero steps next to an NPC whose MsgNpcInfoEx role (u16 at 26) is 47, 7 on walking into a
  `ini/region.ini` type 25 region (the flag bases of map 2057; type 24 is the safe zone) and 11 from the
  guild window's reward button. Server actions: 0/9 show/hide the war windows (commands 0xe09/0xe0a), 1 and
  2 the base damage and guild point ranks (1 read only inside a base), 3 the flags on the radar, 5 and 8
  call `CDlgWarFlagTopInfo::SetOccupyInfo`/`OnTouchFlag` (meaning not traced), 6/7 the carry and plant
  effects (7 shows STR_WARFLAG_ESCORT_SUC to the planter), 10 game over, 11 the answer to 11, 12 the X2
  base. `CWarFlagMgr::HasFlag` is status 95, which blocks jumping. `info.ini` [WarflagReward] holds the top
  8 prizes (CPs, silver); npc.ini NpcType868/872 are flag bases, 891 the flag. Not confirmed in Windows.
- **Guild Contest and Guild PK Tournament** (2026-11-25, Mac `CMsgSynCompete`, `CMsgFactionMatch`,
  `CFactionPKMgr`, `CGameMap`): MsgMapInfo's u64 type at 12 is `CGameMap+0xe8`; bit 26 makes the
  client draw the contest ranking (`ShowCompeteRank`), bit 28 is `IsFactionPKMap`, bit 25
  `IsRaceTrackMap`. MsgSynCompete 1062 and MsgFactionMatch 2330 layouts are in their classes. Which
  of the match window's four numbers is points and which is members is a guess (labels in order
  name1, n0, n1, name2, n2, n3). Not traced: MsgFMRoundRobin and MsgFMMatch (group and final
  tables), MsgFactionMatchWitness, the sponsor wagers and reward-setting dialogs, and how the
  StrRes 10537 contest invitation pops up. Maps: 2060 bp-contest02 (GuildContest), 2071
  bp-kingfight; 2072-2074 aren't in GameMap.dat. NPC types: Contest Emissary 813, PK Emissary
  866, Awarders 878-885.
- **Guild reward pools** (2026-11-25, Mac 1133 `CMsgSelfSynMemAwardRank`, `CDlgWarFlagReward*`,
  `CDlgAddAward`, `CMessageBoxCmdProcess::ProcessOk`): one packet, 1063, for Capture the Flag
  (event 1) and the Guild Contest/PK (event 2); layout in `MsgSelfSynMemAwardRank.cs`. The CTF set
  window (`CDlgWarFlagRewardSet`) takes new totals, refuses any below the current pool or a nonzero
  one under 100 (STR_WARFLAG_REWARD_LIMIT), checks the increase against the guild's CP and silver
  funds (STR_WARFLAG_REWARD_NOT_ENOUGH), and sends the increase as 3 (CPs), 4 (silver) or 5 (both).
  The Guild Contest/PK window (`CDlgAddAward`, mode at 0xc14) confirms in a message box that sends
  2-4 or 5-7; which mode sends which wasn't traced. Pages are 8 entries, 4 in
  `CDlgWarFlagRewardNoFight`. The Windows 5619 exe has the same dialog names; not traced there.
- **Map drawing** (2026-11-24, Mac 1133): `CGameMap::SetMapSize` sets the origin to
  (32·width, 16), and `Cell2World` is (32·(x − y), 16·(x + y)) plus it. `GetBgWorldPos`
  puts the puzzle background's top left at (32·width − bgWidth/2, 16·height − bgHeight/2),
  16 lower when the height is odd; the background is the .pul's tiles times the GameMap.dat
  puzzle size. A terrain scene part sits at its object's cell plus its cell offset and is
  drawn at that cell's pixel plus its pixel offset; a cover (DMap object 4) is drawn at its
  cell's pixel minus its offset. Scene parts whose picture is a `.msk` are masks only.
  `tools/assets/Maps/` renders maps this way.
- **Map animations and terrain NPCs** (2026-11-25, Mac 1133): a cover's frame is
  ((TimeGet() − start) / interval) mod frames, so its interval (int32 at 412 of the DMap object) is
  ms per frame, looping (`C2DMapCoverObj::Process`). `CMsgNpcInfo::Process` creates NPC types 25,
  26, 39, 41, 42, 102 and 103 as view 9, which `CRole::Create` hands to
  `CGameMap::AddTerrainNpc(look / 10, look % 10, ...)`: the `ini/terrainnpc.ini` scene of that
  type and direction, placed like a DMap terrain object at the NPC's cell.
- **Team PK** (2026-11-25, Mac `CMsgTeamPKArenic*::Process`, `CDlgTeamPKArenicScore`): MsgTeamPKArenic
  2230 and the Skill Team PK's MsgTeamPopPKArenic 2250 read the same: action u16 at 4, 2 match start
  (seconds u16 at 44), 3 result (victory if u32 at 8 is 1), 4 leaves the match, 5 back in a fight, 6
  out of the fight if u32 at 12 is the hero, 7 closes the windows, 8 the "received EXP and Honor"
  box. MsgTeamPKArenicScore 2231 (Pop: 2251) is two 60-byte `TEAM_PK_ARENIC_SCORE_INFO` at 4 and 64:
  ID at 0, team ID at 4 (the window labels the side equal to `CPlayer::GetTeamID` "my team", the
  rest "enemy"), name at 24, score at 56. Team PK Manager 8158 (lookface 9736) at 1002 440,246,
  Skill Team PK Manager 8592 (11226) at 428,246, from Questinfo.ini and npc.ini. Packs: Team PK
  720794-720809 and per match 720793; Skill Team PK 721300-721315 and per match 720981.

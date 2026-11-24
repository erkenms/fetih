# Client notes

Facts traced in the client binaries that the server relies on. Add a finding here when you
traced it yourself in a client (say which one and the date), and keep each to a few lines.
If a code comment already explains it, don't repeat it here.

## Reading the Mac client

`_CLIENT/MACcon1068.dmg` (Dec 2011) and `_CLIENT/MACcon1133.dmg` (Sep 2012) are TQ's official
Mac clients. 1133 is closer to our 5619 Windows client (127/159 shared ini files identical, vs
86/142 for 1068). It's a separate port of the same game code, so check findings against the
Windows 5619 client, which is the one that counts. It can't run on a modern Mac (i386 only).

- Mounting: the DMGs carry a TQ EULA, so `hdiutil attach` cancels unless fed `yes |`. Use
  `-readonly -nobrowse -mountpoint <scratch dir>`.
- Binary: `Conquer.app/Contents/ConquerGameExe.app/Contents/MacOS/ConquerGameExe`, a native
  i386 Mach-O (OpenGL, OpenAL, Carbon/Cocoa, Qt 4.7). `Play.exe` in Resources is a leftover.
- Not stripped: ~180k symbols, C++ names demangle with `c++filt`, stabs give function
  boundaries and source file names. 221 `CMsg*::Process(void*)` handlers, `CHero::*` has
  820 methods. Quick look:
  `otool -tV -arch i386 -p __ZN10CMsgAction7ProcessEPv <bin> | c++filt`. Ghidra loads it
  with names.
- Switches compile to PIC jump tables (`calll x; popl %reg` gives the base, then
  `movl off(%reg,%idx,4)` and `addl %reg`). `__TEXT` is at vmaddr 0x1000, fileoff 0, so the
  entry for case n is the int32 at file offset `base + off + 4*(n - low) - 0x1000`, and the
  target is `base` plus that int32.
- Its login is native (`CDlgLoginFlash` with `CMyEditEx` fields, `CDlgLoginServerList` with
  `servericon*.jpg`), which makes it the reference for a native login screen.

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
- Wine can't run it: the 2007 Flash.ocx faults under WoW64 right after loading (tried and
  dropped 2026-11-23). Don't suggest Wine again unless the user brings it up.

## Findings

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
- **Items** (2026-11-24): MsgItem 49 is the split dialog (`CDlgDevideItem`, item ID and
  amount), splitting the stack count (`CItem::GetOverlay`), which the server doesn't model yet.
  Nothing sends a merge. MsgItem 15 is the VIP (level 2+) whole-body repair.
- **Black list** (2026-11-24): client-only (`CDlgBlack`, `BlackNameRecord` in the hero config,
  up to 50 names, chat dropped in `CMsgTalk::ProcessCheckedMsg`); no packet. MsgEnemyList 1041
  is the Enemy window asking for the list when opened.
- **MsgNpcInfoEx 1109** (2026-11-24): names start at 36; role 10 is the guild pole, 26 a gate.
  Gate looks open/closed in `ini/terrainnpc.ini` (241/251 left, 271/281 right).
- **MsgPlayer flower charm** (2026-11-24): a `Dynarank.ini` ID, assumed at offset 123 in 5619
  (the Mac client has 4 extra bytes before it). Not confirmed in the Windows client.
- **Monk auras**: MsgAura 2410 (`CMsgAura`, `CAuraMgr`); the icon's tooltip level and percent
  come from it, not from the status flag.

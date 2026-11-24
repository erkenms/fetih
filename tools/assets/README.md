# Web assets

```bash
dotnet run tools/assets/build.cs [client] [output]   # defaults: _CLIENT/fetih, web/assets
dotnet run tools/assets/build.cs [client] [output] --maps 1002,1036   # only these maps' tiles
dotnet run tools/assets/build.cs extract <client> <path> [file]   # one client file; .png converts it
```

Reads the client and writes what the web app needs into `web/assets/`, which the web serves
as `/assets`. The output is TQ's art and data: it's gitignored and never committed. Build the
client first (`dotnet run client/build.cs`), or point the tool at any 5619 client folder.

A run takes about a minute the first time. Later runs only convert images whose source
changed (`.build.json` in the output keeps a stamp per file), rewrite JSON only when its
content changed, and delete outputs the client no longer has. Raise `Catalogs.Version` when
the conversion itself changes. Map tiles aren't in `.build.json`: each map file's tile folder
has a `.stamp` of its inputs instead, and `MapTiles.Version` plays the same part.

## Reading the client

- Files are looked up by their client path (`data/ItemMinIcon/111300.dds`), without case:
  first loose in the client folder, then in `data.wdf`, then in `c3.wdf`. Loose first is an
  assumption, not traced in the client: 195 files differ between a loose copy and a packed
  one, and the loose copies are newer (2006 against 2005), so they look like patches.
- `lib/Wdf.cs` reads the packages. They index files only by a hash of the lowercased path,
  so a file can be found but not listed; the paths come from the client's `ani/*.ani` files.
- `lib/Dds.cs` decodes DXT1, DXT3, DXT5 and uncompressed DDS. Some `.dds` files are really
  TGA; those and JPEG, BMP and PNG go through StbImageSharp. `lib/Png.cs` writes the PNGs.
- `ini/itemtype.dat`, `magictype.dat` and `monster.dat` are decrypted with Comet.Game's
  `TqFileCipher`; text is GBK.

`lib/` is meant to be shared: another file-based tool can `#:include` it.

## Outputs

Paths inside the JSON files are relative to the output folder (prefix them with `/assets/`).
A picture the client doesn't have is left out of the JSON rather than pointing at nothing,
so the web shows a placeholder for a missing field.

| Output | From | What |
|---|---|---|
| `items.json` | `ini/itemtype.dat`, `ani/ItemMinIcon.Ani`, `ani/MapItemIcon.Ani` | Every item type: `id`, `name`, `type` (the client's type name), `description`, `icon`, `groundIcon`, requirements (`requiredProfession`, `requiredWeaponSkill`, `requiredLevel`, `requiredGender`, `requiredStrength`/`Agility`/`Vitality`/`Spirit`), `price`, `emoneyPrice` (CP), `minAttack`, `maxAttack`, `defense`, `magicAttack`, `magicDefense`, `dexterity`, `dodge`, `life`, `mana`, `amount`/`amountLimit` (durability), `gem1`, `gem2`, `attackRange`, `attackSpeed`, `stackLimit`, `monopoly`, `actionId`. To keep the file small, **fields that are 0 or empty are left out**: read a missing number as 0. |
| `icons/items/<file>.png` | `data/ItemMinIcon/*` | The inventory icons, 64×64, named after their source file. Many item types share one; `items.json` says which. `default.png` is the client's icon for an item it has no picture of. |
| `icons/ground/<file>.png` | `data/MapItemIcon/*` | An item lying on the ground, 32×32, likewise, with `default.png`. The 5619 client has no larger item pictures. |
| `magic.json` | `ini/magictype.dat`, `ani/Magic.Ani` | One entry per spell `type`: `name`, `sort` (the `MagicSort` name, or its number), `icon`, `crime`, `ground`, `multi`, `target`, `useXp`, `weaponSubtype`, `autoActive`, and `levels`: per level `useMana`, `useStamina`, `power`, `percent`, `stepSeconds`, `activeTimes`, `range`, `distance`, `status`, `needExperience`, `needLevel`, `useItem`, `useItemCount`, `delayMilliseconds`. `Comet.Game/Magic/MagicTypeTable.cs` explains the fields. |
| `icons/magic/<type>.png` | `data/main/MagicSkillType*.dds`, `XpSkillType*` | Skill icons, 64×64. Only player skills have one. |
| `monsters.json` | `Comet.Game/Data/monsters.json`, `ini/monster.dat` | The server's monsters as `monsters.json` has them, each with `client`: the client's `monster.dat` section with the same `TypeID` (its `name`, `ZoomPercent`, `MaxLife`, `Level`, `Boss`... under the client's own key names). Monsters only the client knows come with `id`, `name` and `client`. |
| `faces.json`, `faces/npc/<n>.png`, `faces/player/<n>.png` | `ani/NpcFace.Ani`, `ani/PlayerFace.Ani` | The numbered faces: `{"npc": {"<n>": "faces/npc/<n>.png"}, "player": {...}}`. NPC faces are the avatar an NPC dialog shows; player faces the one picked when creating a character. |
| `maps.json` | `ini/GameMap.dat`, `ini/GameMapEx.ini`, `Comet.Game/Data/maps.json`, `ani/MiniMap.Ani` | Every map the client has: `id`, `name` (as the client shows it), `serverName` and `aliases` (the server's `/tp` names), `file` (the map file, `.7z` or `.dmap`), `puzzleSize`, `minimap`. |
| `minimaps/<id>.png` | `data/MiniMap/*` | The minimap. The client splits big ones into four quarters (`a` top left, `b` top right, `c` bottom left, `d` bottom right); they're joined into one picture. |
| `maps/<id>/meta.json` | `ini/GameMap.dat`, the map's DMap | Where the map's tiles are and how a cell lands on them: `width`, `height` (the background in pixels), `tileSize`, `maxZoom` (the zoom with one tile pixel per background pixel), `cell` (64×32), `mapSize` (cells), `origin` and `background` (world pixels), `puzzle` (`width`, `height` in tiles, `gridSize`), and `ground` and `covers`, the Leaflet URL templates of the two pyramids. A cell (x, y) is at background pixel (32·(x − y) + origin.x − background.x, 16·(x + y) + origin.y − background.y). |
| `maps/tiles/<map file>/ground/{z}/{x}/{y}.webp` | `map/puzzle/*.pul` and its .ani, terrain scenes | The background with the terrain objects' scene parts drawn on it, as 256×256 tiles. Maps that share a map file share these. Tiles with nothing on them are left out. |
| `maps/tiles/<map file>/covers/{z}/{x}/{y}.webp` | the DMap's cover objects | Buildings, trees and roofs, which the client draws over the roles, on a transparent layer. |
| `.build.json` | | The build's own record of what it wrote. |

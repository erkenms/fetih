namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Placing and moving run on the monster loop only
    public static class Monsters
    {
        // A dead monster lies for this long before it's taken off players' screens
        public static readonly TimeSpan CorpseTime = TimeSpan.FromSeconds(3);

        private static List<Monster> all = new List<Monster>();
        private static Dictionary<uint, Monster> byId = new Dictionary<uint, Monster>();
        private static uint nextId = Monster.FirstID;
        private static IReadOnlyList<Generator> generators = new List<Generator>();

        public static IReadOnlyList<Monster> All => all;

        // The spawn file's generators the world runs
        public static IReadOnlyList<Generator> Generators => generators;

        public static bool TryGet(uint id, out Monster monster) => byId.TryGetValue(id, out monster);

        // Monsters for each generator whose map the client has; generators beyond the monster
        // ID range are left out. Monsters with no free cell wait for the respawn pass.
        public static (int Placed, int Skipped) Spawn(IEnumerable<Generator> generators, Random random)
        {
            Monsters.generators = generators.ToList();
            var monsters = new List<Monster>();
            uint next = Monster.FirstID;
            int skipped = 0;
            foreach (var generator in Monsters.generators)
            {
                if (!Kernel.MonsterTypes.TryGet(generator.MonsterID, out var type) ||
                    !Kernel.Monsters.TryGet(generator.MapID, out var map) ||
                    next + generator.Amount - 1 > Monster.LastID)
                {
                    skipped++;
                    continue;
                }

                for (int i = 0; i < generator.Amount; i++)
                {
                    var monster = new Monster(next++, type, generator, map) { CorpseRemoved = true };
                    monsters.Add(monster);
                    if (TryPlace(monster, random))
                        monster.Revive();
                }
            }

            all = monsters;
            byId = monsters.ToDictionary(m => m.Identity);
            nextId = next;
            return (monsters.Count(m => m.Alive), skipped);
        }

        // Swaps the spawn file's generators on a running world. A generator that's the same, of a
        // monster type that's the same, keeps its monsters; the others' monsters leave players'
        // screens and the new generators' monsters are placed at once. Scheduled bosses stay.
        // Monster IDs aren't reused, so a client never mixes an old monster with a new one.
        public static async Task<(int Kept, int Removed, int Placed, int Skipped)> ReplaceSpawnsAsync(
            IEnumerable<Generator> replacement, Func<uint, bool> typeChanged, DateTime now, Random random)
        {
            var oldById = generators.GroupBy(g => g.ID).ToDictionary(g => g.Key, g => g.First());
            var spawned = new HashSet<Generator>(generators);
            var kept = new HashSet<Generator>();
            var finalGenerators = new List<Generator>();
            var toSpawn = new List<Generator>();
            foreach (var generator in replacement)
            {
                if (oldById.TryGetValue(generator.ID, out var old) && Generator.Same(old, generator) &&
                    !typeChanged(old.MonsterID))
                {
                    kept.Add(old);
                    finalGenerators.Add(old);
                }
                else
                {
                    toSpawn.Add(generator);
                    finalGenerators.Add(generator);
                }
            }

            var monsters = new List<Monster>();
            int removed = 0;
            foreach (var monster in all)
            {
                if (!spawned.Contains(monster.Generator) || kept.Contains(monster.Generator))
                {
                    monsters.Add(monster);
                    continue;
                }

                if (monster.Alive || !monster.CorpseRemoved)
                    await SendToViewAsync(monster, MsgAction.RemoveEntity(monster.Identity));
                monster.Map.Remove(monster);
                removed++;
            }

            int keptCount = monsters.Count;
            int placed = 0, skipped = 0;
            var newMonsters = new List<Monster>();
            foreach (var generator in toSpawn)
            {
                if (!Kernel.MonsterTypes.TryGet(generator.MonsterID, out var type) ||
                    !Kernel.Monsters.TryGet(generator.MapID, out var map) ||
                    nextId + generator.Amount - 1 > Monster.LastID)
                {
                    skipped++;
                    continue;
                }

                for (int i = 0; i < generator.Amount; i++)
                    newMonsters.Add(new Monster(nextId++, type, generator, map) { CorpseRemoved = true });
            }

            generators = finalGenerators;
            monsters.AddRange(newMonsters);
            all = monsters;
            byId = monsters.ToDictionary(m => m.Identity);
            foreach (var monster in newMonsters)
                if (await SpawnNowAsync(monster, now, random))
                    placed++;
            return (keptCount, removed, placed, skipped);
        }

        // Registers a scheduled monster (initially dead) using the next free monster ID
        public static Monster RegisterScheduled(MonsterType type, Generator generator, GameMap<Monster> map)
        {
            if (nextId > Monster.LastID)
                return null;
            var monster = new Monster(nextId++, type, generator, map) { CorpseRemoved = true };
            all.Add(monster);
            byId[monster.Identity] = monster;
            return monster;
        }

        // Places and revives a monster immediately, showing it to players in view
        public static async Task<bool> SpawnNowAsync(Monster monster, DateTime now, Random random)
        {
            if (monster.Alive || !TryPlace(monster, random))
                return false;
            monster.Revive();
            monster.NextMoveAt = monster.NextAttackAt = now + TimeSpan.FromSeconds(1);
            await SendToViewAsync(monster, new MsgPlayer(monster));
            return true;
        }

        public static async Task RespawnAsync(DateTime now, Random random)
        {
            foreach (var monster in all)
            {
                if (monster.Alive)
                    continue;
                if (!monster.CorpseRemoved)
                {
                    if (now - monster.DiedAt < CorpseTime)
                        continue;
                    monster.CorpseRemoved = true;
                    await SendToViewAsync(monster, MsgAction.RemoveEntity(monster.Identity));
                    if (monster.Generator.RespawnSeconds < 0)
                        monster.Map.Remove(monster);
                    continue;
                }

                if (monster.Generator.RespawnSeconds < 0)
                    continue;
                var wait = TimeSpan.FromSeconds(monster.Generator.RespawnSeconds);
                if (now - monster.DiedAt < wait || !TryPlace(monster, random))
                    continue;
                monster.Revive();
                monster.NextMoveAt = monster.NextAttackAt = now + TimeSpan.FromSeconds(1);
                await SendToViewAsync(monster, new MsgPlayer(monster));
                LogEvents.For(typeof(Monsters), LogEvents.MonsterRespawned).Debug(
                    "{MonsterName} {MonsterId} came back on map {MapId} at {X},{Y}",
                    monster.Type.Name, monster.Identity, monster.Map.Id, monster.X, monster.Y);
            }
        }

        // Monsters coming into the player's view, and with a previous cell, monsters leaving it
        public static async Task ShowAroundAsync(Client client, uint mapID, ushort x, ushort y,
            ushort? fromX = null, ushort? fromY = null)
        {
            if (Kernel.Monsters == null || !Kernel.Monsters.TryGet(mapID, out var map))
                return;
            foreach (var monster in map.InView(x, y))
            {
                if (!monster.Alive ||
                    fromX.HasValue && GameMap<Monster>.InView(fromX.Value, fromY.Value, monster.X, monster.Y))
                    continue;
                await client.SendAsync(new MsgPlayer(monster));
            }

            if (!fromX.HasValue)
                return;
            foreach (var monster in map.InView(fromX.Value, fromY.Value))
                if (!GameMap<Monster>.InView(x, y, monster.X, monster.Y))
                    await client.SendAsync(MsgAction.RemoveEntity(monster.Identity));
        }

        public static async Task HideAroundAsync(Client client, uint mapID, ushort x, ushort y)
        {
            if (Kernel.Monsters == null || !Kernel.Monsters.TryGet(mapID, out var map))
                return;
            foreach (var monster in map.InView(x, y))
                await client.SendAsync(MsgAction.RemoveEntity(monster.Identity));
        }

        public static List<Client> PlayersInView(Monster monster) =>
            Kernel.World.TryGet(monster.Map.Id, out var players)
                ? players.InView(monster.X, monster.Y) : new List<Client>();

        public static async Task SendToViewAsync(Monster monster, IPacket packet)
        {
            foreach (var player in PlayersInView(monster))
                await player.SendAsync(packet);
        }

        public static async Task<bool> StepAsync(Monster monster, byte direction, bool run)
        {
            int toX = monster.X + Movement.DeltaX[direction];
            int toY = monster.Y + Movement.DeltaY[direction];
            var map = monster.Map;
            if (!IsFree(map, toX, toY))
                return false;

            var before = PlayersInView(monster);
            map.Move(monster, (ushort)toX, (ushort)toY);
            monster.X = (ushort)toX;
            monster.Y = (ushort)toY;
            monster.Direction = direction;
            var after = PlayersInView(monster);

            var walk = new MsgWalk
            {
                Direction = direction,
                CharacterID = monster.Identity,
                Mode = run ? 1u : 0u,
                Timestamp = (uint)Environment.TickCount,
                Map = map.Id,
            };
            var stillInView = new HashSet<Client>(after);
            foreach (var player in before)
                await player.SendAsync(stillInView.Contains(player) ? walk : MsgAction.RemoveEntity(monster.Identity));
            var wasInView = new HashSet<Client>(before);
            foreach (var player in after.Where(p => !wasInView.Contains(p)))
                await player.SendAsync(new MsgPlayer(monster));
            return true;
        }

        public static async Task<bool> JumpAsync(Monster monster, ushort toX, ushort toY)
        {
            var map = monster.Map;
            if (!IsFree(map, toX, toY))
                return false;

            ushort fromX = monster.X, fromY = monster.Y;
            var before = PlayersInView(monster);
            byte direction = Movement.DirectionTo(fromX, fromY, toX, toY, monster.Direction);
            map.Move(monster, toX, toY);
            monster.X = toX;
            monster.Y = toY;
            monster.Direction = direction;
            var after = PlayersInView(monster);

            var jump = MsgAction.Jump(monster.Identity, fromX, fromY, toX, toY, direction);
            var stillInView = new HashSet<Client>(after);
            foreach (var player in before)
                await player.SendAsync(stillInView.Contains(player) ? jump : MsgAction.RemoveEntity(monster.Identity));
            var wasInView = new HashSet<Client>(before);
            foreach (var player in after.Where(p => !wasInView.Contains(p)))
                await player.SendAsync(new MsgPlayer(monster));
            return true;
        }

        // A walkable cell with no monster, NPC or player on it
        public static bool IsFree(GameMap<Monster> map, int x, int y)
        {
            if (!map.Grid.IsWalkable(x, y) || map.AnyAt((ushort)x, (ushort)y))
                return false;
            foreach (var npc in Kernel.Npcs.InView(map.Id, x, y))
                if (npc.X == x && npc.Y == y)
                    return false;
            return Kernel.World == null || !Kernel.World.TryGet(map.Id, out var players) ||
                !players.AnyAt((ushort)x, (ushort)y);
        }

        private static bool TryPlace(Monster monster, Random random)
        {
            var generator = monster.Generator;
            var map = monster.Map;
            for (int tries = 0; tries < 10; tries++)
            {
                int x = generator.X + random.Next(-generator.Spread, generator.Spread + 1);
                int y = generator.Y + random.Next(-generator.Spread, generator.Spread + 1);
                bool placed = map.TryGetPosition(monster, out var atX, out var atY);
                bool ownCell = placed && atX == x && atY == y;
                if (!map.Grid.IsWalkable(x, y) || !ownCell && map.AnyAt((ushort)x, (ushort)y))
                    continue;

                if (placed)
                    map.Move(monster, (ushort)x, (ushort)y);
                else
                    map.Add(monster, (ushort)x, (ushort)y);
                monster.X = monster.HomeX = (ushort)x;
                monster.Y = monster.HomeY = (ushort)y;
                monster.Direction = (byte)random.Next(8);
                return true;
            }
            return false;
        }
    }
}

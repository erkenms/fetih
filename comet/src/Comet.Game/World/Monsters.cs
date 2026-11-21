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

        public static IReadOnlyList<Monster> All => all;

        // Monsters for each generator whose map the client has; generators beyond the monster
        // ID range are left out. Monsters with no free cell wait for the respawn pass.
        public static (int Placed, int Skipped) Spawn(IEnumerable<Generator> generators, Random random)
        {
            var monsters = new List<Monster>();
            uint next = Monster.FirstID;
            int skipped = 0;
            foreach (var generator in generators)
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
            return (monsters.Count(m => m.Alive), skipped);
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
                    continue;
                }

                var wait = TimeSpan.FromSeconds(Math.Max(monster.Generator.RespawnSeconds, 0));
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
            if (!map.Grid.IsWalkable(toX, toY) || map.AnyAt((ushort)toX, (ushort)toY))
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
                monster.X = (ushort)x;
                monster.Y = (ushort)y;
                monster.Direction = (byte)random.Next(8);
                return true;
            }
            return false;
        }
    }
}

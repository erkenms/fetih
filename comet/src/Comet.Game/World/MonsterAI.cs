namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Combat;
    using Comet.Game.States;

    // One loop thinks for every monster: only those a player can see act, so empty areas cost
    // nothing. Monsters attack players through the player's own queue (Kernel.Post), so a
    // player's health only changes there.
    public static class MonsterAI
    {
        // Out of this range a monster loses its target
        private const int ChaseRange = GameMap<Monster>.ViewRange;

        // The shortest time between a monster's attacks, whatever its data says
        public static TimeSpan AttackInterval = TimeSpan.FromMilliseconds(1500);

        // Chance in percent that an idle monster takes a step when it may move
        public static int WanderChance = 10;

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow, Random.Shared); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(MonsterAI), LogEvents.MonsterLoopFailed).Error(e, "Monster loop tick failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now, Random random)
        {
            await Monsters.RespawnAsync(now, random);
            var awake = Awake();

            // Monsters out of every player's sight don't think, so they'd keep a target forever
            foreach (var monster in Monsters.All)
                if (monster.Target != 0 && !awake.Contains(monster))
                    monster.Target = 0;
            foreach (var monster in awake)
                if (monster.Alive)
                {
                    await Magic.Poison.TickAsync(monster, now);
                    await ThinkAsync(monster, now, random);
                }
        }

        private static HashSet<Monster> Awake()
        {
            var awake = new HashSet<Monster>();
            foreach (var player in Kernel.Players.Values)
            {
                var map = player.Map;
                if (player.Character == null || map == null ||
                    !Kernel.Monsters.TryGet(map.Id, out var monsters) ||
                    !map.TryGetPosition(player, out var x, out var y))
                    continue;
                awake.UnionWith(monsters.InView(x, y));
            }
            return awake;
        }

        private static async Task ThinkAsync(Monster monster, DateTime now, Random random)
        {
            var (player, victim, x, y) = FindTarget(monster, now);
            if (player == null && victim == null)
            {
                if (!monster.IsGuard && now >= monster.NextMoveAt && random.Next(100) < WanderChance)
                {
                    monster.NextMoveAt = now + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
                    await Monsters.StepAsync(monster, (byte)random.Next(8), run: false);
                }
                return;
            }

            if (Distance(monster.X, monster.Y, x, y) <= Math.Max(1, monster.Type.AttackRange))
            {
                if (now < monster.NextAttackAt)
                    return;
                monster.NextAttackAt = now + TimeSpan.FromMilliseconds(
                    Math.Max(monster.Type.AttackSpeed, AttackInterval.TotalMilliseconds));
                if (player != null)
                    Kernel.Post(player, () => Attacks.MonsterHitsPlayerAsync(monster, player));
                else
                    await Attacks.MonsterHitsMonsterAsync(monster, victim, now, random);
                return;
            }

            if (monster.IsGuard || now < monster.NextMoveAt)
                return;
            monster.NextMoveAt = now + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
            byte toward = Movement.DirectionTo(monster.X, monster.Y, x, y, monster.Direction);
            foreach (int turn in new[] { 0, 1, -1 })
                if (await Monsters.StepAsync(monster, (byte)((toward + turn + 8) % 8), run: true))
                    return;
        }

        // Keeps a target that's still there; otherwise guards pick the nearest monster or guilty
        // player (flashing or black name), and aggressive monsters the nearest player they see
        private static (Client Player, Monster Victim, ushort X, ushort Y) FindTarget(Monster monster, DateTime now)
        {
            uint target = monster.Target;
            if (target != 0)
            {
                var found = Resolve(monster, target, now);
                if (found.Player != null || found.Victim != null)
                    return found;
                monster.Target = 0;
            }

            int range = monster.Type.ViewRange;
            Monster prey = null;
            if (monster.IsGuard)
                prey = monster.Map.InView(monster.X, monster.Y, except: monster)
                    .Where(m => !m.IsGuard && m.Alive && Distance(monster.X, monster.Y, m.X, m.Y) <= range)
                    .OrderBy(m => Distance(monster.X, monster.Y, m.X, m.Y))
                    .FirstOrDefault();

            if ((!monster.Type.IsAggressive && !monster.IsGuard) || !Kernel.World.TryGet(monster.Map.Id, out var players))
                return Chosen(monster, prey);
            Client nearest = null;
            ushort nearestX = 0, nearestY = 0;
            foreach (var player in players.InView(monster.X, monster.Y))
            {
                if (!players.TryGetPosition(player, out var px, out var py) || !CanBeAttacked(player, now) ||
                    monster.IsGuard && !Pk.Pking.IsGuilty(player, now) ||
                    Distance(monster.X, monster.Y, px, py) > range ||
                    nearest != null && Distance(monster.X, monster.Y, px, py) >= Distance(monster.X, monster.Y, nearestX, nearestY))
                    continue;
                (nearest, nearestX, nearestY) = (player, px, py);
            }
            if (nearest == null || prey != null &&
                Distance(monster.X, monster.Y, prey.X, prey.Y) < Distance(monster.X, monster.Y, nearestX, nearestY))
                return Chosen(monster, prey);
            monster.Target = nearest.ID;
            return (nearest, null, nearestX, nearestY);
        }

        private static (Client Player, Monster Victim, ushort X, ushort Y) Chosen(Monster monster, Monster prey)
        {
            if (prey == null)
                return default;
            monster.Target = prey.Identity;
            return (null, prey, prey.X, prey.Y);
        }

        private static (Client Player, Monster Victim, ushort X, ushort Y) Resolve(Monster monster, uint target, DateTime now)
        {
            if (target >= Monster.FirstID && target <= Monster.LastID)
            {
                if (monster.Map.TryGet(target, out var victim, out var vx, out var vy) && victim.Alive &&
                    Distance(monster.X, monster.Y, vx, vy) <= ChaseRange)
                    return (null, victim, vx, vy);
                return default;
            }

            if (Kernel.Players.TryGet(target, out var player) && player.Map?.Id == monster.Map.Id &&
                player.Map.TryGetPosition(player, out var x, out var y) && CanBeAttacked(player, now) &&
                (!monster.IsGuard || Pk.Pking.IsGuilty(player, now)) &&
                Distance(monster.X, monster.Y, x, y) <= ChaseRange)
                return (player, null, x, y);
            return default;
        }

        // Monsters don't see the invisible
        private static bool CanBeAttacked(Client player, DateTime now) =>
            player.Character != null && !player.Life.Dead && now >= player.Life.ProtectedUntil &&
            !player.Effects.Has(StatusFlags.Invisible, now);

        public static int Distance(int x1, int y1, int x2, int y2) =>
            Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
    }
}

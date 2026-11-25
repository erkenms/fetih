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

        // Chance in percent that an idle monster takes a step when it may move (a third of
        // it for guards)
        public static int WanderChance = 10;

        // How long a monster watches a player it sees before going for them
        public static TimeSpan ReactionMin = TimeSpan.FromMilliseconds(700);
        public static TimeSpan ReactionMax = TimeSpan.FromMilliseconds(1500);

        // How long it takes to turn on what hit it, or to follow a target that jumped
        public static TimeSpan ProvokedMin = TimeSpan.FromMilliseconds(250);
        public static TimeSpan ProvokedMax = TimeSpan.FromMilliseconds(600);

        // A player who jumped goes unnoticed until this long after landing
        public static TimeSpan JumpGrace = TimeSpan.FromMilliseconds(1500);

        // Farther than this from home a monster or guard gives up the chase and walks back
        public static int LeashRange = 20;
        public static int GuardLeashRange = 12;

        // How far from home an idle monster wanders, and a guard patrols
        public static int WanderRange = 5;
        public static int GuardPatrolRange = 3;

        // Guards jump at players farther than this, and wait this long after landing to swing
        private const int GuardJumpDistance = 4;
        private static readonly TimeSpan LandingPause = TimeSpan.FromMilliseconds(400);

        // How long the last tick took, and the slowest one
        public static TimeSpan LastTick { get; private set; }
        public static TimeSpan SlowestTick { get; private set; }

        public static void Configure(Database.ServerConfiguration.MonstersConfiguration config)
        {
            static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms));
            ReactionMin = Ms(config.ReactionMinMilliseconds);
            ReactionMax = Ms(config.ReactionMaxMilliseconds);
            ProvokedMin = Ms(config.ProvokedMinMilliseconds);
            ProvokedMax = Ms(config.ProvokedMaxMilliseconds);
            JumpGrace = Ms(config.JumpGraceMilliseconds);
            LeashRange = Math.Max(1, config.LeashRange);
            GuardLeashRange = Math.Max(1, config.GuardLeashRange);
            WanderRange = Math.Max(0, config.WanderRange);
            GuardPatrolRange = Math.Max(0, config.GuardPatrolRange);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { await TickAsync(DateTime.UtcNow, Random.Shared); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(MonsterAI), LogEvents.MonsterLoopFailed).Error(e, "Monster loop tick failed");
                }
                LastTick = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                if (LastTick > SlowestTick)
                    SlowestTick = LastTick;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Func<Task>> Posted =
            new System.Collections.Concurrent.ConcurrentQueue<Func<Task>>();

        // Work on monsters from players' queues (a knockback), run on the monster loop
        public static void Post(Func<Task> work) => Posted.Enqueue(work);

        public static async Task TickAsync(DateTime now, Random random)
        {
            while (Posted.TryDequeue(out var work))
                await work();
            await Monsters.RespawnAsync(now, random);
            await Kernel.Bosses.TickAsync(now, random);
            var awake = Awake();

            // Monsters out of every player's sight don't think, so they'd keep a target forever
            foreach (var monster in Monsters.All)
                if (monster.Target != 0 && !awake.Contains(monster))
                    monster.Target = 0;
            foreach (var monster in awake)
                if (monster.Alive)
                {
                    await Magic.Poison.TickAsync(monster, now);
                    if (monster.EndStun(now))
                        foreach (var player in Monsters.PlayersInView(monster))
                            await player.SendAsync(new Packets.MsgUserAttrib(monster.Identity, Packets.MsgUserAttrib.AttributeType.Status, 0));
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
            if (monster.Returning)
            {
                await ReturnHomeAsync(monster, now);
                return;
            }

            uint noticed = monster.Noticed;
            var (player, victim, x, y) = FindTarget(monster, now);
            if (player == null && victim == null)
            {
                // A guard goes back to its post after every fight
                if (monster.IsGuard && monster.Engaged != 0 &&
                    (monster.X, monster.Y) != (monster.HomeX, monster.HomeY))
                    monster.Returning = true;
                monster.Engaged = 0;
                if (monster.Noticed != 0 && monster.Noticed != noticed &&
                    Kernel.Players.TryGet(monster.Noticed, out var seen) && seen.Character != null)
                    await FaceAsync(monster, seen.Character.X, seen.Character.Y);
                await IdleAsync(monster, now, random);
                return;
            }

            if (Distance(monster.HomeX, monster.HomeY, monster.X, monster.Y) > (monster.IsGuard ? GuardLeashRange : LeashRange))
            {
                GiveUp(monster);
                return;
            }

            // Something that hit it, or a monster a guard spots, takes a moment to turn on; a
            // player it watched has already had that moment
            if (monster.Engaged != monster.Target)
            {
                bool watched = player != null && monster.Target == monster.Noticed;
                monster.Engaged = monster.Target;
                if (!watched)
                    monster.ReactAt = now + Between(ProvokedMin, ProvokedMax, random);
                await FaceAsync(monster, x, y);
            }
            if (now < monster.ReactAt)
                return;

            // It waits for a jump to land, then a moment to see where they went
            if (player != null && now < player.LandsAt)
            {
                monster.ReactAt = player.LandsAt + Between(ProvokedMin, ProvokedMax, random);
                return;
            }

            int distance = Distance(monster.X, monster.Y, x, y);
            bool melee = InMelee(monster, player, now);
            if (!melee && monster.Type.HasMagic && Magic.Casting.ChooseMonsterSpell(monster, distance, random) is { } spell)
            {
                if (spell.Sort == Magic.MagicSort.Recruit || distance <= Magic.Casting.MonsterCastRange(spell))
                {
                    if (now < monster.NextAttackAt || monster.Stunned(now))
                        return;
                    double delayMs = Math.Max(
                        Math.Max(monster.Type.AttackSpeed, AttackInterval.TotalMilliseconds),
                        spell.DelayMilliseconds);
                    monster.NextAttackAt = now + TimeSpan.FromMilliseconds(delayMs);
                    await Magic.Casting.MonsterCastAsync(monster, spell, player, victim, x, y, now, random);
                    return;
                }
            }
            else if (distance <= Reach(monster, player, now))
            {
                if (now < monster.NextAttackAt || monster.Stunned(now))
                    return;
                monster.NextAttackAt = now + TimeSpan.FromMilliseconds(
                    Math.Max(monster.Type.AttackSpeed, AttackInterval.TotalMilliseconds));
                if (player != null)
                    Kernel.Post(player, () => Attacks.MonsterHitsPlayerAsync(monster, player));
                else
                    await Attacks.MonsterHitsMonsterAsync(monster, victim, now, random);
                return;
            }

            // Guards only close in on players; monsters they shoot from where they stand
            if (monster.IsGuard && !melee || now < monster.NextMoveAt)
                return;
            if (monster.IsGuard && distance > GuardJumpDistance && NextTo(monster, x, y) is { } landing)
            {
                await JumpAsync(monster, landing.X, landing.Y, now);
                return;
            }
            await ChaseStepAsync(monster, x, y, now);
        }

        // Guards fight players hand to hand, unless they fly
        private static bool InMelee(Monster monster, Client player, DateTime now) =>
            monster.IsGuard && player != null && !Magic.Buffs.Flying(player.Effects, now);

        // How close it must be to hit without a spell
        public static int Reach(Monster monster, Client player, DateTime now) =>
            InMelee(monster, player, now) ? 1 : Math.Max(1, monster.Type.AttackRange);

        private static async Task ChaseStepAsync(Monster monster, int x, int y, DateTime now)
        {
            monster.NextMoveAt = now + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
            byte toward = Movement.DirectionTo(monster.X, monster.Y, x, y, monster.Direction);
            foreach (int turn in new[] { 0, 1, -1, 2, -2 })
                if (await Monsters.StepAsync(monster, (byte)((toward + turn + 8) % 8), run: true))
                {
                    // Having just arrived, it takes a moment before it swings
                    if (monster.NextAttackAt < monster.NextMoveAt)
                        monster.NextAttackAt = monster.NextMoveAt;
                    return;
                }
        }

        private static async Task JumpAsync(Monster monster, ushort x, ushort y, DateTime now)
        {
            ushort fromX = monster.X, fromY = monster.Y;
            if (!await Monsters.JumpAsync(monster, x, y))
                return;
            var landed = now + Movement.JumpTime(fromX, fromY, x, y, cyclone: false, oblivion: false, flying: false);
            monster.NextMoveAt = landed + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
            if (monster.NextAttackAt < landed + LandingPause)
                monster.NextAttackAt = landed + LandingPause;
        }

        // A free cell next to x, y that's a jump away and within the guard's leash, the one
        // nearest to it
        private static (ushort X, ushort Y)? NextTo(Monster monster, int x, int y)
        {
            (ushort X, ushort Y)? best = null;
            int bestDistance = int.MaxValue;
            for (byte direction = 0; direction < 8; direction++)
            {
                int cx = x + Movement.DeltaX[direction], cy = y + Movement.DeltaY[direction];
                int d = Distance(monster.X, monster.Y, cx, cy);
                if (d > Movement.MaxJumpDistance || d >= bestDistance || cx < 0 || cy < 0 ||
                    Distance(monster.HomeX, monster.HomeY, cx, cy) > GuardLeashRange ||
                    !monster.Map.Grid.IsWalkable(cx, cy) || monster.Map.AnyAt((ushort)cx, (ushort)cy))
                    continue;
                (best, bestDistance) = (((ushort)cx, (ushort)cy), d);
            }
            return best;
        }

        // Too far from home: it forgets its target and goes back, ignoring everyone on the way
        private static void GiveUp(Monster monster)
        {
            monster.Target = 0;
            monster.Engaged = monster.Noticed = 0;
            monster.Returning = true;
        }

        private static async Task ReturnHomeAsync(Monster monster, DateTime now)
        {
            int distance = Distance(monster.X, monster.Y, monster.HomeX, monster.HomeY);
            if (distance == 0)
            {
                monster.Returning = false;
                return;
            }
            if (now < monster.NextMoveAt)
                return;
            if (monster.IsGuard && distance > GuardJumpDistance && distance <= Movement.MaxJumpDistance)
            {
                await JumpAsync(monster, monster.HomeX, monster.HomeY, now);
                if (monster.X == monster.HomeX && monster.Y == monster.HomeY)
                    return;
            }

            monster.NextMoveAt = now + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
            byte toward = Movement.DirectionTo(monster.X, monster.Y, monster.HomeX, monster.HomeY, monster.Direction);
            foreach (int turn in new[] { 0, 1, -1, 2, -2 })
                if (await Monsters.StepAsync(monster, (byte)((toward + turn + 8) % 8), run: false))
                    return;
            // Boxed in: this is home now
            (monster.HomeX, monster.HomeY) = (monster.X, monster.Y);
            monster.Returning = false;
        }

        // Monsters roam near home; guards keep to their post
        private static async Task IdleAsync(Monster monster, DateTime now, Random random)
        {
            int roam = monster.IsGuard ? GuardPatrolRange : WanderRange;
            if (Distance(monster.X, monster.Y, monster.HomeX, monster.HomeY) > roam)
            {
                monster.Returning = true;
                return;
            }
            int chance = monster.IsGuard ? WanderChance / 3 : WanderChance;
            if (now < monster.NextMoveAt || random.Next(100) >= chance)
                return;
            monster.NextMoveAt = now + TimeSpan.FromMilliseconds(monster.Type.MoveSpeed);
            byte direction = (byte)random.Next(8);
            if (Distance(monster.X + Movement.DeltaX[direction], monster.Y + Movement.DeltaY[direction],
                    monster.HomeX, monster.HomeY) <= roam)
                await Monsters.StepAsync(monster, direction, run: false);
        }

        private static async Task FaceAsync(Monster monster, int x, int y)
        {
            byte direction = Movement.DirectionTo(monster.X, monster.Y, x, y, monster.Direction);
            if (direction == monster.Direction)
                return;
            monster.Direction = direction;
            await Monsters.SendToViewAsync(monster, Packets.MsgAction.Turn(monster.Identity, direction));
        }

        private static TimeSpan Between(TimeSpan min, TimeSpan max, Random random) =>
            max <= min ? min : min + TimeSpan.FromMilliseconds(random.NextDouble() * (max - min).TotalMilliseconds);

        // Keeps a target that's still there; otherwise guards pick the nearest monster or guilty
        // player (flashing or black name), and aggressive monsters the nearest player they see.
        // A player is only taken on after being watched for a reaction time, and one in the air
        // or just landed from a jump goes unnoticed: monsters don't chase travellers.
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
            {
                monster.Noticed = 0;
                return Chosen(monster, prey);
            }
            Client nearest = null;
            ushort nearestX = 0, nearestY = 0;
            foreach (var player in players.InView(monster.X, monster.Y))
            {
                if (!players.TryGetPosition(player, out var px, out var py) || !CanBeAttacked(player, now) ||
                    now < player.LandsAt + JumpGrace ||
                    monster.IsGuard && !Pk.Pking.IsGuilty(player, now) ||
                    Distance(monster.X, monster.Y, px, py) > range ||
                    nearest != null && Distance(monster.X, monster.Y, px, py) >= Distance(monster.X, monster.Y, nearestX, nearestY))
                    continue;
                (nearest, nearestX, nearestY) = (player, px, py);
            }
            if (nearest == null)
            {
                monster.Noticed = 0;
                return Chosen(monster, prey);
            }
            if (monster.Noticed != nearest.ID)
            {
                monster.Noticed = nearest.ID;
                monster.DecidesAt = now + Between(ReactionMin, ReactionMax, Random.Shared);
            }
            if (now < monster.DecidesAt || prey != null &&
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

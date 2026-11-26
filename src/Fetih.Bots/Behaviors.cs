namespace Fetih.Bots
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.TestClient;
    using Fetih.Game.World;

    // What a bot does each tick. Moves go out at a person's pace: a step every 350-450 ms, a
    // jump every 1.5-3 s.
    public abstract class Behavior
    {
        protected readonly GameClient Game;
        protected readonly BotWorld World;
        protected readonly BotsConfiguration.Group Group;
        protected readonly MapGrid Grid;
        protected readonly BotStats Stats;
        protected readonly Random Random = new Random();
        protected DateTime NextMoveAt;

        protected Behavior(GameClient game, BotWorld world, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
        {
            this.Game = game;
            this.World = world;
            this.Group = group;
            this.Grid = grid;
            this.Stats = stats;
        }

        public static Behavior Create(string name, GameClient game, BotWorld world, BotsConfiguration.Group group,
            MapGrid grid, BotStats stats) => name?.ToLowerInvariant() switch
        {
            "hunt" => new Hunt(game, world, group, grid, stats),
            "wander" => new Wander(game, world, group, grid, stats),
            "jump" => new Jump(game, world, group, grid, stats),
            "idle" => new Idle(game, world, group, grid, stats),
            _ => throw new ArgumentException($"No bot behavior called {name}; there are hunt, wander, jump and idle"),
        };

        public abstract Task TickAsync(DateTime now);

        // Far from its spot (revived in town, say), it jumps back the way a player would; false
        // when it's there already
        public async Task<bool> GoBackAsync(DateTime now)
        {
            int distance = Steps.Distance(this.World.X, this.World.Y, this.Group.X, this.Group.Y);
            if (distance <= this.Group.Spread + 8)
                return false;
            if (now < this.NextMoveAt || this.World.JumpSentAt != null)
                return true;
            for (int reach = Math.Min(15, distance); reach >= 2; reach--)
            {
                int x = this.World.X + (this.Group.X - this.World.X) * reach / distance;
                int y = this.World.Y + (this.Group.Y - this.World.Y) * reach / distance;
                if (this.Walkable(x, y))
                {
                    await this.JumpToAsync(x, y, now);
                    this.NextMoveAt = now + this.Between(900, 1400);
                    return true;
                }
            }
            await this.StepTowardAsync(this.Group.X, this.Group.Y, now);
            return true;
        }

        protected bool Walkable(int x, int y) => this.Grid == null || this.Grid.IsWalkable(x, y);

        protected bool InArea(int x, int y, int slack = 0) =>
            Steps.Distance(x, y, this.Group.X, this.Group.Y) <= this.Group.Spread + slack;

        protected TimeSpan Between(int minMs, int maxMs) => TimeSpan.FromMilliseconds(this.Random.Next(minMs, maxMs));

        // A step toward x, y, going around what's in the way; false if it's boxed in
        protected async Task<bool> StepTowardAsync(int x, int y, DateTime now)
        {
            int toward = Steps.DirectionTo(this.World.X, this.World.Y, x, y);
            foreach (int turn in new[] { 0, 1, -1, 2, -2 })
            {
                int direction = (toward + turn + 8) % 8;
                int nx = this.World.X + Steps.DeltaX[direction], ny = this.World.Y + Steps.DeltaY[direction];
                if (!this.Walkable(nx, ny))
                    continue;
                await this.Game.WalkAsync(this.World.Me, direction);
                (this.World.X, this.World.Y) = ((ushort)nx, (ushort)ny);
                this.NextMoveAt = now + this.Between(350, 450);
                this.Stats.Stepped();
                return true;
            }
            return false;
        }

        protected async Task JumpToAsync(int x, int y, DateTime now)
        {
            await this.Game.JumpAsync(this.World.Me, this.World.X, this.World.Y, x, y);
            this.World.JumpSentAt ??= now;
            (this.World.X, this.World.Y) = ((ushort)x, (ushort)y);
            this.NextMoveAt = now + this.Between(1500, 3000);
            this.Stats.Jumped();
        }

        // A walkable cell within the area, at most reach cells from where the bot stands
        protected (int X, int Y)? RandomCell(int reach)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                int x = this.World.X + this.Random.Next(-reach, reach + 1);
                int y = this.World.Y + this.Random.Next(-reach, reach + 1);
                if (this.InArea(x, y) && this.Walkable(x, y))
                    return (x, y);
            }
            // Out of the area: head back to its middle
            return this.Walkable(this.Group.X, this.Group.Y) ? (this.Group.X, this.Group.Y) : null;
        }
    }

    public sealed class Idle : Behavior
    {
        public Idle(GameClient game, BotWorld world, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
            : base(game, world, group, grid, stats) { }

        public override Task TickAsync(DateTime now) => Task.CompletedTask;
    }

    // Walks to a spot in the area, stands a while, picks another
    public class Wander : Behavior
    {
        private (int X, int Y)? waypoint;

        public Wander(GameClient game, BotWorld world, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
            : base(game, world, group, grid, stats) { }

        public override async Task TickAsync(DateTime now)
        {
            if (now < this.NextMoveAt)
                return;
            if (this.waypoint is not { } to || (this.World.X, this.World.Y) == to)
            {
                if (this.waypoint != null)
                {
                    this.waypoint = null;
                    this.NextMoveAt = now + this.Between(1000, 6000);
                    return;
                }
                this.waypoint = this.RandomCell(8);
                return;
            }
            if (!await this.StepTowardAsync(to.X, to.Y, now))
                this.waypoint = null;
        }
    }

    public sealed class Jump : Behavior
    {
        public Jump(GameClient game, BotWorld world, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
            : base(game, world, group, grid, stats) { }

        public override async Task TickAsync(DateTime now)
        {
            if (now < this.NextMoveAt || this.World.JumpSentAt != null)
                return;
            if (this.RandomCell(12) is { } to)
                await this.JumpToAsync(to.X, to.Y, now);
        }
    }

    // Goes for the nearest monster in the area: jumps when it's far, walks when it's close,
    // then attacks it. It picks up the potions monsters drop and drinks one when its health is
    // under half. With no monster about it wanders.
    public sealed class Hunt : Wander
    {
        private uint prey;
        private DateTime attackedAt, drinkAt;

        public Hunt(GameClient game, BotWorld world, BotsConfiguration.Group group, MapGrid grid, BotStats stats)
            : base(game, world, group, grid, stats) { }

        public override async Task TickAsync(DateTime now)
        {
            if (this.World.Health * 2 < this.World.MaxHealth && now >= this.drinkAt &&
                this.World.Bag.Values.FirstOrDefault(i => Steps.IsHealthPotion(i.TypeId)) is { } potion)
            {
                await this.Game.UseAsync(potion.Id);
                this.World.Bag.Remove(potion.Id);
                this.drinkAt = now + TimeSpan.FromSeconds(1.5);
                this.Stats.Drank();
            }

            var target = this.World.Around.Values
                .Where(s => s.Monster && !s.Dead && this.InArea(s.X, s.Y, slack: 10))
                .OrderBy(s => s.Id == this.prey ? 0 : 1)
                .ThenBy(s => Steps.Distance(this.World.X, this.World.Y, s.X, s.Y))
                .FirstOrDefault();
            int distance = target == null ? int.MaxValue : Steps.Distance(this.World.X, this.World.Y, target.X, target.Y);

            // A potion nearer than the next monster is worth the detour
            var loot = this.World.Ground.Values
                .Where(g => Steps.IsPotion(g.TypeId) && this.InArea(g.X, g.Y, slack: 10))
                .OrderBy(g => Steps.Distance(this.World.X, this.World.Y, g.X, g.Y))
                .FirstOrDefault();
            if (loot != null && distance > 1 &&
                Steps.Distance(this.World.X, this.World.Y, loot.X, loot.Y) < Math.Min(distance, 10))
            {
                await this.LootAsync(loot, now);
                return;
            }

            if (target == null)
            {
                this.prey = 0;
                await base.TickAsync(now);
                return;
            }

            if (distance <= 1)
            {
                // The server keeps swinging at a target until something stops it
                if (target.Id != this.prey || now - this.attackedAt > TimeSpan.FromSeconds(3))
                {
                    await this.Game.AttackAsync(this.World.Me, target.Id, target.X, target.Y);
                    (this.prey, this.attackedAt) = (target.Id, now);
                    this.Stats.Attacked();
                }
                return;
            }

            this.prey = target.Id;
            if (now < this.NextMoveAt || this.World.JumpSentAt != null)
                return;
            if (distance > 6 && this.NextTo(target.X, target.Y) is { } landing)
                await this.JumpToAsync(landing.X, landing.Y, now);
            else
                await this.StepTowardAsync(target.X, target.Y, now);
        }

        private async Task LootAsync(GroundItemMessage loot, DateTime now)
        {
            if (Steps.Distance(this.World.X, this.World.Y, loot.X, loot.Y) <= 1)
            {
                // Forgotten whether or not it works: another's loot stays theirs for a while
                await this.Game.PickUpAsync(loot.Id, loot.X, loot.Y);
                this.World.Ground.Remove(loot.Id);
                this.Stats.PickedUp();
                return;
            }
            if (now < this.NextMoveAt || this.World.JumpSentAt != null)
                return;
            if (!await this.StepTowardAsync(loot.X, loot.Y, now))
                this.World.Ground.Remove(loot.Id);
        }

        private (int X, int Y)? NextTo(int tx, int ty)
        {
            for (int direction = 0; direction < 8; direction++)
            {
                int x = tx + Steps.DeltaX[direction], y = ty + Steps.DeltaY[direction];
                if (this.Walkable(x, y) && Steps.Distance(this.World.X, this.World.Y, x, y) <= 16 &&
                    !this.World.Around.Values.Any(s => s.X == x && s.Y == y && !s.Dead))
                    return (x, y);
            }
            return null;
        }
    }
}

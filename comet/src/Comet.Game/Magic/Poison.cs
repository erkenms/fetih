namespace Comet.Game.Magic
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // A poison takes its percentage of the life left every interval, a number of times, but
    // never the last point
    public sealed class Poisoning
    {
        public int Percent { get; init; }
        public TimeSpan Interval { get; init; }
        public int Left { get; set; }
        public DateTime NextAt { get; set; }

        public uint Take(uint health) =>
            health <= 1 ? 0 : (uint)Math.Clamp((ulong)health * (uint)Math.Max(0, this.Percent) / 100, 1, health - 1);
    }

    // Toxic Fog's poison, on players (each on their own queue, from Recovery's tick) and on
    // monsters (on the monster loop)
    public static class Poison
    {
        public static Poisoning From(MagicType spell, DateTime now)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, spell.StepSeconds));
            return new Poisoning
            {
                Percent = spell.PowerPercent,
                Interval = interval,
                Left = (int)Math.Max(1, spell.ActiveTimes),
                NextAt = now + interval,
            };
        }

        public static async Task StartAsync(Client client, Poisoning poison, DateTime now)
        {
            if (client.Character == null || client.Life.Dead)
                return;
            client.Poison = poison;
            client.Effects.Set(StatusFlags.Poisoned, now + poison.Interval * (poison.Left + 1), now);
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Poison), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from poison",
                client.Character.Name, StatusFlags.Poisoned, (int)(poison.Interval * poison.Left).TotalSeconds);
        }

        public static async Task TickAsync(Client client, DateTime now)
        {
            var poison = client.Poison;
            var character = client.Character;
            if (poison == null || character == null)
                return;
            if (client.Life.Dead)
            {
                await EndAsync(client);
                return;
            }
            if (now < poison.NextAt)
                return;

            uint taken = poison.Take(character.HealthPoints);
            character.HealthPoints -= (ushort)taken;
            poison.Left--;
            poison.NextAt += poison.Interval;
            if (taken > 0)
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Health, character.HealthPoints));
            LogEvents.For(typeof(Poison), LogEvents.CombatHit).Debug(
                "Poison took {Damage} from {Name}; {Health} health left", taken, character.Name, character.HealthPoints);
            if (poison.Left <= 0)
                await EndAsync(client);
        }

        private static async Task EndAsync(Client client)
        {
            client.Poison = null;
            client.Effects.Remove(StatusFlags.Poisoned);
            await Pking.SendStatusAsync(client);
        }

        public static Task StartAsync(Monster monster, Poisoning poison) =>
            monster.Poison(poison)
                ? SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Status, StatusFlags.Poisoned))
                : Task.CompletedTask;

        public static async Task TickAsync(Monster monster, DateTime now)
        {
            if (monster.TickPoison(now) is not var (taken, health, ended))
                return;
            if (taken > 0)
                await SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Health, health));
            if (ended)
                await SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Status, 0));
        }

        private static async Task SendAroundAsync(Monster monster, MsgUserAttrib packet)
        {
            foreach (var player in Monsters.PlayersInView(monster))
                await player.SendAsync(packet);
        }
    }
}

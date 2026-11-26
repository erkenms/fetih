namespace Fetih.Game.Magic
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Packets;
    using Fetih.Game.Pk;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgUserAttrib;

    // A poison takes its percentage of the life left every interval, a number of times, but
    // never the last point
    public sealed class Poisoning
    {
        public ulong Status { get; init; }
        public int Percent { get; init; }
        public TimeSpan Interval { get; init; }
        public int Left { get; set; }
        public DateTime NextAt { get; set; }

        public uint Take(uint health) =>
            health <= 1 ? 0 : (uint)Math.Clamp((ulong)health * (uint)Math.Max(0, this.Percent) / 100, 1, health - 1);
    }

    // Toxic Fog's and Poisonous Arrows' poison, on players (each on their own queue, from
    // Recovery's tick) and on monsters (on the monster loop)
    public static class Poison
    {
        // Magictype's status 43 is Toxic Fog's own bit; the client tells it from a plain poison
        private const int ToxicFogStatus = 43;

        public static Poisoning From(MagicType spell, DateTime now)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, spell.StepSeconds));
            return new Poisoning
            {
                Status = spell.Status == ToxicFogStatus ? StatusFlags.ToxicFog : StatusFlags.Poisoned,
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
            if (client.Poison is { } was && was.Status != poison.Status)
                client.Effects.Remove(was.Status);
            client.Poison = poison;
            client.Effects.Set(poison.Status, now + poison.Interval * (poison.Left + 1), now);
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Poison), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from poison",
                client.Character.Name, poison.Status,(int)(poison.Interval * poison.Left).TotalSeconds);
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
            int detox = (client.SubClasses?.DetoxicationPercent() ?? 0) +
                Items.Refinery.Bonus(client.Equipment?.Items, Items.RefineryKind.Detoxication, now);
            if (taken > 0 && detox > 0)
                taken = Combat.Damage.DetoxifiedPoison(taken, detox);
            character.HealthPoints -= (ushort)taken;
            poison.Left--;
            poison.NextAt += poison.Interval;
            if (taken > 0)
            {
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Health, character.HealthPoints));
                var shown = Shown(client.ID, character.X, character.Y, taken);
                await client.SendAsync(shown);
                await Movement.SendToViewAsync(client, shown);
            }
            LogEvents.For(typeof(Poison), LogEvents.CombatHit).Debug(
                "Poison took {Damage} from {Name}; {Health} health left", taken, character.Name, character.HealthPoints);
            if (poison.Left <= 0)
                await EndAsync(client);
        }

        private static async Task EndAsync(Client client)
        {
            client.Effects.Remove(client.Poison.Status);
            client.Poison = null;
            await Pking.SendStatusAsync(client);
        }

        public static Task StartAsync(Monster monster, Poisoning poison) =>
            monster.Poison(poison)
                ? SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Status, poison.Status))
                : Task.CompletedTask;

        public static async Task TickAsync(Monster monster, DateTime now)
        {
            if (monster.TickPoison(now) is not var (taken, health, ended))
                return;
            if (taken > 0)
            {
                await SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Health, health));
                await SendAroundAsync(monster, Shown(monster.Identity, monster.X, monster.Y, taken));
            }
            if (ended)
                await SendAroundAsync(monster, new MsgUserAttrib(monster.Identity, AttributeType.Status, 0));
        }

        // Each tick shows as the poisoned one's own Poison0 spell hitting them for what it took,
        // which is how the client shows the number over their head
        private const ushort ShownSpell = 10010;

        private static MsgMagicEffect Shown(uint id, ushort x, ushort y, uint taken)
        {
            var effect = new MsgMagicEffect(id, x, y, ShownSpell);
            effect.Targets.Add((id, taken));
            return effect;
        }

        private static async Task SendAroundAsync(Monster monster, Fetih.Network.Packets.IPacket packet)
        {
            foreach (var player in Monsters.PlayersInView(monster))
                await player.SendAsync(packet);
        }
    }
}

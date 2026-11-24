namespace Comet.Game.Training
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // Lucky time, counted as the 5619 client counts it (CHero::ProcessLuck): a player praying
    // (the Bless spell, status LuckDiffuse) gains three times the time passed, players standing
    // next to one (LuckAbsorb) gain the time passed, and otherwise it runs down. It stops at
    // two hours. MsgUserAttrib 28 carries it in milliseconds. Changed on the player's
    // recovery tick.
    public static class LuckyTime
    {
        public const uint MaxMilliseconds = 7_200_000;
        public const int AbsorbRange = 3;

        public sealed class State
        {
            public bool Praying { get; set; }
            public bool Absorbing { get; set; }
            public DateTime LastTick { get; set; }

            public UInt128 Status => this.Absorbing ? StatusFlags.LuckAbsorb : 0;
        }

        public static bool Prays(Client client, DateTime now) =>
            client.Character != null && !client.Life.Dead &&
            (client.Effects.Status(now) & StatusFlags.Bless) != 0;

        public static uint Accrue(uint milliseconds, bool praying, bool absorbing, TimeSpan elapsed)
        {
            long ms = (long)Math.Max(0, elapsed.TotalMilliseconds);
            long next = praying ? milliseconds + 3 * ms : absorbing ? milliseconds + ms : milliseconds - ms;
            return (uint)Math.Clamp(next, 0, MaxMilliseconds);
        }

        public static async Task TickAsync(Client client, DateTime now)
        {
            var character = client.Character;
            var luck = client.Luck;
            if (luck.LastTick != default)
                character.LuckyTime = Accrue(character.LuckyTime, luck.Praying, luck.Absorbing, now - luck.LastTick);
            luck.LastTick = now;

            bool praying = Prays(client, now);
            bool absorbing = !praying && Absorbs(client, now);
            if (praying == luck.Praying && absorbing == luck.Absorbing)
                return;

            bool statusChanged = absorbing != luck.Absorbing;
            luck.Praying = praying;
            luck.Absorbing = absorbing;
            await SendAsync(client);
            if (statusChanged)
                await Pk.Pking.SendStatusAsync(client);
            LogEvents.For(typeof(LuckyTime), LogEvents.LuckChanged).Information(
                "{Name} {State} with {Milliseconds} ms of lucky time", character.Name,
                praying ? "prays" : absorbing ? "absorbs luck" : "stopped praying", character.LuckyTime);
        }

        public static Task SendAsync(Client client) => client.SendAsync(new MsgUserAttrib(
            client.ID, MsgUserAttrib.AttributeType.LuckyTime, client.Character.LuckyTime));

        private static bool Absorbs(Client client, DateTime now)
        {
            var map = client.Map;
            if (client.Life.Dead || map == null || !map.TryGetPosition(client, out var x, out var y))
                return false;
            return map.InView(x, y, client).Any(other =>
                Prays(other, now) && map.TryGetPosition(other, out var ox, out var oy) &&
                Math.Max(Math.Abs(ox - x), Math.Abs(oy - y)) <= AbsorbRange);
        }
    }
}

namespace Comet.Game.Magic
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Each second, on every player's own queue: statuses that ran out end, stamina comes back
    // and the XP bar fills. Rates: 2 stamina per half second standing, 11
    // sitting; 1 XP every 3 seconds; a full bar lasts 20 seconds, then empties.
    public static class Recovery
    {
        public static TimeSpan XpInterval { get; set; } = TimeSpan.FromSeconds(3);
        public static TimeSpan XpFullTime { get; set; } = TimeSpan.FromSeconds(20);
        public const int StaminaStanding = 4, StaminaSitting = 22;

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try
                {
                    var now = DateTime.UtcNow;
                    foreach (var client in Kernel.Players.Values.ToList())
                        Kernel.Post(client, () => TickAsync(client, now));
                }
                catch (Exception e)
                {
                    LogEvents.For(typeof(Recovery), LogEvents.EnergyLoopFailed).Error(e, "Energy round failed");
                }
            }
        }

        public static async Task TickAsync(Client client, DateTime now)
        {
            // Not while logging in: the client waits for the reply to each login step
            var character = client.Character;
            if (character == null || client.Map == null || !client.Map.TryGetPosition(client, out _, out _))
                return;
            var energy = client.Energy;

            ulong ended = client.Effects.Expire(now);
            if (ended != 0)
            {
                if ((ended & StatusFlags.XpFull) != 0)
                    await SetXpAsync(client, 0);
                if ((ended & (StatusFlags.Superman | StatusFlags.Cyclone)) != 0)
                    energy.KoCount = 0;
                await Pk.Pking.SendStatusAsync(client);
                LogEvents.For(typeof(Recovery), LogEvents.StatusEnded).Debug(
                    "{Name} status 0x{Status:X} ended", character.Name, ended);
            }

            await HeavenBlessing.ExpireAsync(client, now);
            await Training.GodExp.TickAsync(client, now);

            if (client.Life.Dead)
                return;
            await Items.Riding.RecoverAsync(client, now);

            int gain = character.Stance == Items.Vending.SitStance ? StaminaSitting : StaminaStanding;
            byte stamina = (byte)Math.Min(HeavenBlessing.MaxStamina(client), energy.Stamina + gain);
            if (stamina != energy.Stamina)
            {
                energy.Stamina = stamina;
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Stamina, stamina));
            }

            if (now < energy.NextXpAt ||
                client.Effects.Has(StatusFlags.XpFull, now) ||
                client.Effects.Has(StatusFlags.Superman, now) ||
                client.Effects.Has(StatusFlags.Cyclone, now))
                return;
            energy.NextXpAt = now + XpInterval;
            await AddXpAsync(client, 1, now);
        }

        public static async Task AddXpAsync(Client client, int amount, DateTime now)
        {
            var energy = client.Energy;
            byte xp = (byte)Math.Min(Energy.MaxXp, energy.Xp + amount);
            await SetXpAsync(client, xp);
            if (xp < Energy.MaxXp || !client.Effects.Set(StatusFlags.XpFull, now + XpFullTime, now))
                return;
            await Pk.Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Recovery), LogEvents.XpFull).Information(
                "{Name} has a full XP bar", client.Character.Name);
        }

        public static async Task SetXpAsync(Client client, byte xp)
        {
            if (client.Energy.Xp == xp)
                return;
            client.Energy.Xp = xp;
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.XpCircle, xp));
        }

        public static async Task<bool> UseStaminaAsync(Client client, byte amount)
        {
            if (amount == 0)
                return true;
            if (client.Energy.Stamina < amount)
                return false;
            client.Energy.Stamina -= amount;
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Stamina, client.Energy.Stamina));
            return true;
        }
    }
}

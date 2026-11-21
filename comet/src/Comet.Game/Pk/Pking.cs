namespace Comet.Game.Pk
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Who may attack whom, flashing names, PK points and the name colors they give.
    // Combat asks CanAttack before a hit on a player and calls OnAttackAsync and OnKillAsync after.
    // Guards hunt the guilty, and attacking a guard is allowed in free PK mode only and makes
    // the attacker flash (Albetros).
    public static class Pking
    {
        public const ulong FlashingStatus = StatusFlags.Flashing;
        public const ulong RedNameStatus = StatusFlags.RedName;
        public const ulong BlackNameStatus = StatusFlags.BlackName;

        public const ushort RedNamePoints = 30;
        public const ushort BlackNamePoints = 100;

        // Killing an innocent player; a red name counts for less (Albetros)
        public const int PointsPerKill = 10;
        public const int PointsPerRedKill = 3;

        public static TimeSpan FlashTime { get; set; } = TimeSpan.FromSeconds(30);
        public static TimeSpan DecayInterval { get; set; } = TimeSpan.FromMinutes(6);

        // No player may attack another here (Twin City, the market, ...)
        public static bool IsPeaceful(uint mapID) => Kernel.MapTypes.Of(mapID).HasFlag(MapFlags.PkDisable);

        // Attacking or killing here is no crime: PK arenas and the jail
        public static bool IsFree(uint mapID) =>
            (Kernel.MapTypes.Of(mapID) & (MapFlags.PkField | MapFlags.PrisonMap)) != 0;

        public static ulong NameStatus(ushort points) =>
            points >= BlackNamePoints ? BlackNameStatus :
            points >= RedNamePoints ? RedNameStatus : 0;

        // Guilty players (flashing or black) may be attacked in capture mode, and attacking
        // or killing them is no crime
        public static bool Permits(PkMode mode, bool targetGuilty, bool targetIsAlly) => mode switch
        {
            PkMode.Free => true,
            PkMode.Team => !targetIsAlly,
            PkMode.Capture => targetGuilty,
            _ => false,
        };

        public static bool IsGuilty(Client client, DateTime now) =>
            client.Character.KillPoints >= BlackNamePoints || client.Pk.IsFlashing(now);

        public static ulong Status(Client client) =>
            client.Character == null ? 0 : client.Pk.Status(client.Character, DateTime.UtcNow);

        public static bool CanAttack(Client attacker, Client target)
        {
            if (attacker == target || attacker.Character == null || target.Character == null)
                return false;
            if (Arena.Qualifier.CanFight(attacker, target) is bool fighting)
                return fighting;
            if (IsPeaceful(attacker.Character.MapID))
                return false;
            bool ally = attacker.Friends.ContainsKey(target.ID) ||
                Social.Teams.OthersOf(attacker).Contains(target);
            return Permits(attacker.Pk.Mode, IsGuilty(target, DateTime.UtcNow), ally);
        }

        public static async Task OnAttackAsync(Client attacker, Client target)
        {
            if (IsFree(attacker.Character.MapID) || IsGuilty(target, DateTime.UtcNow))
                return;
            await FlashAsync(attacker, FlashTime, "attack");
        }

        // Death ends flashing, so whether the victim was guilty is asked before they die
        public static async Task OnKillAsync(Client killer, Client victim, bool victimGuilty)
        {
            ushort victimPoints = victim.Character.KillPoints;
            bool crime = !IsFree(killer.Character.MapID) && !victimGuilty;
            LogEvents.For(typeof(Pking), LogEvents.PkKill).Information(
                "{Name} killed {Victim} on map {MapId} ({Crime})",
                killer.Character.Name, victim.Character.Name, killer.Character.MapID,
                crime ? "crime" : "no_crime");

            if (victimPoints >= BlackNamePoints)
                await Jail.SendAsync(victim, "killed");
            if (!crime)
                return;
            await ChangePointsAsync(killer,
                victimPoints >= RedNamePoints ? PointsPerRedKill : PointsPerKill, "kill");
            await FlashAsync(killer, FlashTime, "kill");
        }

        // The dead don't flash: guards would hunt the ghost and capture mode could target it.
        // The caller sends the status.
        public static void OnDeath(Client client)
        {
            if (client.Pk.ClearFlashing(DateTime.UtcNow))
                LogEvents.For(typeof(Pking), LogEvents.PkFlashing).Information(
                    "{Name} stopped flashing ({Reason})", client.Character.Name, "died");
        }

        public static async Task OnKilledByGuardAsync(Client victim)
        {
            if (victim.Character?.KillPoints >= BlackNamePoints)
                await Jail.SendAsync(victim, "killed_by_guard");
        }

        public static async Task SetModeAsync(Client client, MsgAction msg)
        {
            msg.CharacterID = client.ID;
            if (!Enum.IsDefined(typeof(PkMode), msg.Command))
            {
                LogEvents.For(typeof(Pking), LogEvents.PkModeRejected).Warning(
                    "{Name} can't switch to PK mode {Mode}: unknown_mode", client.Character?.Name, msg.Command);
                msg.Command = (uint)client.Pk.Mode;
                await client.SendAsync(msg);
                return;
            }

            var mode = (PkMode)msg.Command;
            client.Pk.Mode = mode;
            await client.SendAsync(msg);
            await Talk.NoticeAsync(client, Describe(mode));
            LogEvents.For(typeof(Pking), LogEvents.PkMode).Debug(
                "{Name} switched to {Mode} PK mode", client.Character?.Name, mode.ToString());
        }

        private static string Describe(PkMode mode) => mode switch
        {
            PkMode.Free => "Free PK mode. You can attack monsters and all players.",
            PkMode.Peace => "Peace mode. You can only attack monsters.",
            PkMode.Team => "Team PK mode. You can attack monsters and all players except your teammates and friends.",
            _ => "Capture mode. You can only attack monsters, black-name and flashing players.",
        };

        public static async Task ChangePointsAsync(Client client, int amount, string reason)
        {
            var character = client.Character;
            if (character == null)
                return;
            var (from, to) = client.Pk.ChangePoints(character, amount);
            await PointsChangedAsync(client, from, to, reason);
        }

        public static async Task FlashAsync(Client client, TimeSpan duration, string reason)
        {
            if (!client.Pk.Flash(DateTime.UtcNow, duration))
                return;
            await SendStatusAsync(client);
            LogEvents.For(typeof(Pking), LogEvents.PkFlashing).Information(
                "{Name} is flashing for {Seconds}s ({Reason})",
                client.Character.Name, duration.TotalSeconds, reason);
        }

        public static async Task SendStatusAsync(Client client)
        {
            var update = new MsgUserAttrib(client.ID, AttributeType.Status, client.Status);
            await client.SendAsync(update);
            await Movement.SendToViewAsync(client, update);
        }

        public static async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await TickAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(Pking), LogEvents.PkPoints).Error(e, "PK round failed");
                }
            }
        }

        public static async Task TickAsync(DateTime now)
        {
            foreach (var client in Kernel.Players.Values.ToList())
            {
                var character = client.Character;
                if (character == null || client.Map == null)
                    continue;
                if (client.Pk.StopFlashing(now))
                {
                    await SendStatusAsync(client);
                    LogEvents.For(typeof(Pking), LogEvents.PkFlashing).Information(
                        "{Name} stopped flashing", character.Name);
                }
                if (client.Pk.Decay(character, now, DecayInterval, out var points))
                    await PointsChangedAsync(client, (ushort)(points + 1), points, "decay");
            }
        }

        private static async Task PointsChangedAsync(Client client, ushort from, ushort to, string reason)
        {
            if (from == to)
                return;
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.PkPoints, to));
            if (NameStatus(from) != NameStatus(to))
                await SendStatusAsync(client);
            var log = LogEvents.For(typeof(Pking), LogEvents.PkPoints);
            const string template = "{Name} PK points {Amount:+#;-#;0} to {Points} ({Reason})";
            if (reason == "decay")
                log.Debug(template, client.Character.Name, to - from, to, reason);
            else
                log.Information(template, client.Character.Name, to - from, to, reason);
        }
    }
}

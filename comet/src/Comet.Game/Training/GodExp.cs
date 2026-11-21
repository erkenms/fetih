namespace Comet.Game.Training
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgGodExp;

    // The heaven blessing's two EXP pools, claimed from the client's blessing panel
    // (MsgGodExp): online training fills while a blessed player is online, blessed hunting
    // with a share of what monsters give. Amounts are level-up time times 10000, so an
    // EXP ball (600 of that time) is 6,000,000. Runs on the player's own queue.
    public static class GodExp
    {
        public const uint UnitsPerTime = 10000;
        public const uint UnitsPerBall = 600 * UnitsPerTime;

        // 40 time every 10 minutes online, an EXP ball every two and a half hours
        public const uint OnlineUnitsPerMinute = 4 * UnitsPerTime;

        // The client's heaven blessing tooltip: 20% extra EXP from killing monsters
        public const double HuntingShare = 0.2;

        // When a pool is full the client asks to claim it
        public const uint PoolCap = 5 * UnitsPerBall;

        public static TimeSpan OnlineInterval { get; set; } = TimeSpan.FromMinutes(1);

        // No online training in the training grounds: the online one counts its own points
        // (StrRes 11044) and the offline one pays when claimed
        public static bool Trains(uint mapID) =>
            mapID != OfflineTraining.MapID && mapID != OfflineTraining.OnlineGroundMapID;

        public static async Task HandleAsync(Client client, GodExpAction action)
        {
            if (client.Character == null)
                return;
            switch (action)
            {
                case GodExpAction.ClaimOnline:
                    await ClaimAsync(client, online: true);
                    break;
                case GodExpAction.ClaimHunting:
                    await ClaimAsync(client, online: false);
                    break;
                default:
                    await SendAsync(client);
                    break;
            }
        }

        public static Task SendAsync(Client client, GodExpAction action = GodExpAction.Query) =>
            client.SendAsync(new MsgGodExp
            {
                Action = action,
                Online = client.Character.OnlineTrainingExp,
                Hunting = client.Character.BlessedHuntingExp,
            });

        // On the player's recovery tick: a minute's online training for the blessed
        public static async Task TickAsync(Client client, DateTime now)
        {
            var character = client.Character;
            if (!HeavenBlessing.IsBlessed(character, now) || client.Life.Dead || !Trains(character.MapID))
            {
                client.NextOnlineTrainingAt = DateTime.MinValue;
                return;
            }
            if (client.NextOnlineTrainingAt == DateTime.MinValue)
                client.NextOnlineTrainingAt = now + OnlineInterval;
            if (now < client.NextOnlineTrainingAt)
                return;
            client.NextOnlineTrainingAt = now + OnlineInterval;

            uint before = character.OnlineTrainingExp;
            if (before >= PoolCap)
                return;
            character.OnlineTrainingExp = Math.Min(PoolCap, before + OnlineUnitsPerMinute);
            await SendAsync(client);
            if (character.OnlineTrainingExp == PoolCap)
                await SendAsync(client, GodExpAction.OnlineFull);
        }

        public static async Task AddHuntingAsync(Client client, ulong experience)
        {
            var character = client.Character;
            if (experience == 0 || !HeavenBlessing.IsBlessed(character, DateTime.UtcNow))
                return;
            uint before = character.BlessedHuntingExp;
            if (before >= PoolCap)
                return;
            double time = Kernel.Experience.TimeOfExperience(character.Level, (ulong)(experience * HuntingShare));
            ulong units = (ulong)(time * UnitsPerTime);
            if (units == 0)
                return;
            character.BlessedHuntingExp = (uint)Math.Min(PoolCap, before + units);
            await SendAsync(client);
            if (character.BlessedHuntingExp == PoolCap)
                await SendAsync(client, GodExpAction.HuntingFull);
        }

        public static async Task<bool> ClaimAsync(Client client, bool online)
        {
            var character = client.Character;
            string kind = online ? "online_training" : "blessed_hunting";
            uint units = online ? character.OnlineTrainingExp : character.BlessedHuntingExp;
            if (client.Life.Dead)
            {
                await Talk.NoticeAsync(client, "You can't get Heaven Blessing EXP while dead.");
                return Reject(client, kind, "dead");
            }
            if (units == 0)
                return Reject(client, kind, "nothing");

            if (online)
                character.OnlineTrainingExp = 0;
            else
                character.BlessedHuntingExp = 0;
            ulong experience = Kernel.Experience.ExperienceOfTime(
                character.Level, character.Experience, (double)units / UnitsPerTime);
            await Progress.AwardExperienceAsync(client, experience, kind);
            await SendAsync(client);
            LogEvents.For(typeof(GodExp), LogEvents.TrainingExpClaimed).Information(
                "{Name} claimed {Balls:0.00} EXP balls of {Kind} ({Experience} experience)",
                character.Name, (double)units / UnitsPerBall, kind, experience);
            return true;
        }

        private static bool Reject(Client client, string kind, string reason)
        {
            LogEvents.For(typeof(GodExp), LogEvents.TrainingRejected).Warning(
                "{Name} can't claim {Kind}: {Reason}", client.Character?.Name, kind, reason);
            return false;
        }
    }
}

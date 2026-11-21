namespace Comet.Game.Training
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Chat;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgTraining;

    // Offline training, free for the blessed (StrRes 11012): the player enters the offline
    // training ground (601) and may log out; time there turns into experience, claimed from
    // the result dialog (MsgTraining, MsgTrainingInfo), which also sends them back where
    // they came from. Runs on the player's own queue.
    public static class OfflineTraining
    {
        public const uint MapID = 601;
        public const uint OnlineGroundMapID = 1039;
        public const ushort EntranceX = 65, EntranceY = 55;

        // Hours of training that count, by VIP level (Cn_Res STR_VIP_Acrobatics; 15 without)
        private static readonly int[] HoursByVip = { 15, 16, 17, 18, 19, 21, 24 };

        // Level-up time a minute's training is worth: an EXP ball for five hours, so 15 hours
        // make the three balls Hybrid's 5165 server gave
        public const double TimePerMinute = 2;

        public static int MaxMinutes(DbCharacter character) =>
            HoursByVip[Math.Min((int)character.VIPLevel, HoursByVip.Length - 1)] * 60;

        public static int TrainedMinutes(DbCharacter character, DateTime now) =>
            character.OfflineTrainingSince is DateTime since
                ? (int)Math.Clamp((now - since).TotalMinutes, 0, MaxMinutes(character))
                : 0;

        public static bool IsTraining(DbCharacter character) => character.OfflineTrainingSince != null;

        public static async Task HandleAsync(Client client, MsgTraining msg)
        {
            if (client.Character == null)
                return;
            switch (msg.Action)
            {
                case TrainingAction.Query:
                    // The VM showed the client asking even when blessed; the answer's minutes
                    // go to the dialog it opens, whose OK enters
                    await client.SendAsync(new MsgTraining(TrainingAction.Query, (uint)MaxMinutes(client.Character)));
                    break;
                case TrainingAction.Enter:
                    await EnterAsync(client);
                    break;
                case TrainingAction.Result:
                    await ShowResultAsync(client);
                    break;
                case TrainingAction.Claim:
                    await ClaimAsync(client);
                    break;
                default:
                    Reject(client, msg.Action.ToString(), "unknown_action");
                    break;
            }
        }

        public static async Task<bool> EnterAsync(Client client)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            string refused =
                !HeavenBlessing.IsBlessed(character, now) ? "not_blessed" :
                client.Life.Dead ? "dead" :
                IsTraining(character) ? "already_training" :
                (Kernel.MapTypes.Of(character.MapID) & (MapFlags.PrisonMap | MapFlags.TeleportDisable)) != 0 ? "map" :
                null;
            if (refused != null)
            {
                if (refused == "map")
                    await Talk.NoticeAsync(client, "You can't take offline training in this map.");
                return Reject(client, "enter", refused);
            }

            uint fromMap = character.MapID;
            ushort fromX = character.X, fromY = character.Y;
            if (!await Movement.TeleportAsync(client, MapID, EntranceX, EntranceY))
                return Reject(client, "enter", "no_training_ground");
            character.OfflineReturnMapID = fromMap;
            character.OfflineReturnX = fromX;
            character.OfflineReturnY = fromY;
            character.OfflineTrainingSince = now;
            await client.SendAsync(new MsgTraining(TrainingAction.Enter));
            LogEvents.For(typeof(OfflineTraining), LogEvents.TrainingOfflineStarted).Information(
                "{Name} started offline training from map {MapId} at {X},{Y}",
                character.Name, fromMap, fromX, fromY);
            return true;
        }

        // The training ground's portal, which no reference lists: those training go back to
        // the entrance and get their result to claim (claiming takes them back where they
        // came from); others go to Twin City. Leaving the client on the portal cell would
        // make it ask again and again.
        public static async Task ExitAsync(Client client)
        {
            if (!IsTraining(client.Character))
            {
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
                return;
            }
            await Movement.TeleportAsync(client, MapID, EntranceX, EntranceY);
            await ShowResultAsync(client);
        }

        // After logging in on the training ground: the client asks for the result
        public static async Task LoginAsync(Client client)
        {
            if (client.Character != null && IsTraining(client.Character))
                await client.SendAsync(new MsgTraining(TrainingAction.AskResult));
        }

        public static async Task<bool> ShowResultAsync(Client client)
        {
            var character = client.Character;
            if (!IsTraining(character))
                return Reject(client, "result", "not_training");
            int trained = TrainedMinutes(character, DateTime.UtcNow);
            var (level, experience) = Kernel.Experience.Gain(character.Level, character.Experience, Earned(character, trained));
            uint progress = Kernel.Experience.TryGetRequired(level, out var required)
                ? (uint)Math.Min(9_999_999, experience * 10_000_000.0 / required)
                : 0;
            await client.SendAsync(new MsgTrainingInfo
            {
                Trained = (ushort)trained,
                Left = (ushort)(MaxMinutes(character) - trained),
                Level = level,
                Progress = progress,
            });
            return true;
        }

        public static async Task<bool> ClaimAsync(Client client)
        {
            var character = client.Character;
            if (!IsTraining(character))
                return Reject(client, "claim", "not_training");
            if (client.Life.Dead)
                return Reject(client, "claim", "dead");

            int trained = TrainedMinutes(character, DateTime.UtcNow);
            ulong experience = Earned(character, trained);
            character.OfflineTrainingSince = null;
            await Progress.AwardExperienceAsync(client, experience, "offline_training");
            if (!Kernel.World.TryGet(character.OfflineReturnMapID, out _) ||
                !await Movement.TeleportAsync(client, character.OfflineReturnMapID, character.OfflineReturnX, character.OfflineReturnY))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
            LogEvents.For(typeof(OfflineTraining), LogEvents.TrainingOfflineClaimed).Information(
                "{Name} claimed {Minutes} minutes of offline training ({Experience} experience)",
                character.Name, trained, experience);
            return true;
        }

        private static ulong Earned(DbCharacter character, int minutes) =>
            Kernel.Experience.ExperienceOfTime(character.Level, character.Experience, minutes * TimePerMinute);

        private static bool Reject(Client client, string step, string reason)
        {
            LogEvents.For(typeof(OfflineTraining), LogEvents.TrainingRejected).Warning(
                "{Name} can't {Step} offline training: {Reason}", client.Character?.Name, step, reason);
            return false;
        }
    }
}

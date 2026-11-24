namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgContribute;
    using static Comet.Game.Packets.MsgGuide;
    using static Comet.Game.Packets.MsgGuideInfo;

    // Mentor and apprentice relations, shared battle power, and apprentice-to-mentor
    // contribution rewards (experience level-up time, heaven blessing hours, +1 stone
    // composition points). Changes run under one gate because they span two players.
    public static class Mentors
    {
        public const uint LeaveCost = 50_000;
        public const ushort CompositionPerStone = 100;
        public const uint PlusOneStoneTypeID = 730001;
        public const double RewardShare = 0.1;

        private sealed class Relation
        {
            public uint ApprenticeID;
            public uint MentorID;
            public uint Enrolled;
            public uint Experience;
            public ushort Blessing;
            public ushort Composition;
            public double PendingExpTime;
        }

        private sealed class RewardPool
        {
            public uint Experience;
            public ushort Blessing;
            public ushort Composition;
        }

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<uint, Relation> ByApprentice = new Dictionary<uint, Relation>();
        private static readonly Dictionary<uint, HashSet<uint>> ByMentor = new Dictionary<uint, HashSet<uint>>();
        private static readonly Dictionary<uint, RewardPool> Rewards = new Dictionary<uint, RewardPool>();

        public static int Count => ByApprentice.Count;

        public static async Task LoadAsync()
        {
            var (relations, rewards) = await MentorsRepository.LoadAllAsync();
            await Gate.WaitAsync();
            try
            {
                ByApprentice.Clear();
                ByMentor.Clear();
                Rewards.Clear();
                foreach (var row in relations)
                {
                    var rel = new Relation
                    {
                        ApprenticeID = row.ApprenticeID,
                        MentorID = row.MentorID,
                        Enrolled = row.Enrolled,
                        Experience = row.Experience,
                        Blessing = row.Blessing,
                        Composition = row.Composition,
                    };
                    ByApprentice[row.ApprenticeID] = rel;
                    GetApprenticeSet(row.MentorID).Add(row.ApprenticeID);
                }
                foreach (var row in rewards)
                {
                    Rewards[row.CharacterID] = new RewardPool
                    {
                        Experience = row.Experience,
                        Blessing = row.Blessing,
                        Composition = row.Composition,
                    };
                }
            }
            finally { Gate.Release(); }
        }

        public static void Reset()
        {
            Gate.Wait();
            try
            {
                ByApprentice.Clear();
                ByMentor.Clear();
                Rewards.Clear();
            }
            finally { Gate.Release(); }
        }

        public static uint SharedBattlePower(DbCharacter mentor, DbCharacter apprentice)
        {
            if (mentor == null || apprentice == null)
                return 0;
            uint cap = Kernel.TutorTypes.MaxBattlePower(mentor);
            if (cap == 0)
                return 0;
            uint mentorBp = Attributes.BattlePower(mentor);
            uint apprenticeBp = Attributes.BattlePower(apprentice);
            if (mentorBp <= apprenticeBp)
                return 0;
            return Math.Min(cap, (mentorBp - apprenticeBp) / 2);
        }

        public static async Task LoginAsync(Client client)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                await SendMentorInfoAsync(client);
                await SendApprenticesInfoAsync(client);

                // Tell online mentor that this apprentice is online
                if (ByApprentice.TryGetValue(client.ID, out var mentorRel) &&
                    Kernel.Players.TryGet(mentorRel.MentorID, out var mentor) &&
                    mentor.Character != null)
                {
                    await mentor.SendAsync(BuildInfo(
                        GuideInfoMode.Apprentice, mentorRel, mentor.Character.Name, client.Character, online: true, sharedBp: 0));
                }

                // Tell online apprentices that their mentor is online and update their shared BP
                if (ByMentor.TryGetValue(client.ID, out var apps))
                {
                    foreach (var appID in apps.ToList())
                    {
                        if (!Kernel.Players.TryGet(appID, out var apprentice) || apprentice.Character == null ||
                            !ByApprentice.TryGetValue(appID, out var appRel))
                            continue;
                        uint bp = SharedBattlePower(client.Character, apprentice.Character);
                        await apprentice.SendAsync(BuildInfo(
                            GuideInfoMode.Mentor, appRel, client.Character.Name, client.Character,
                            online: true, sharedBp: bp, apprenticeName: apprentice.Character.Name));
                        await apprentice.SendAsync(BuildBattlePowerUpdate(client.ID, appID, bp));
                    }
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task LogoutAsync(Client client)
        {
            var character = client.Character;
            if (character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                if (ByApprentice.TryGetValue(client.ID, out var mentorRel) &&
                    Kernel.Players.TryGet(mentorRel.MentorID, out var mentor) &&
                    mentor.Character != null)
                {
                    await mentor.SendAsync(new MsgGuide
                    {
                        Action = GuideAction.ApprenticeOffline,
                        Identity = mentorRel.MentorID,
                        Param = client.ID,
                        Name = character.Name,
                    });
                }

                if (ByMentor.TryGetValue(client.ID, out var apps))
                {
                    foreach (var appID in apps.ToList())
                    {
                        if (!Kernel.Players.TryGet(appID, out var apprentice) || apprentice.Character == null)
                            continue;
                        await apprentice.SendAsync(new MsgGuide
                        {
                            Action = GuideAction.MentorOffline,
                            Identity = client.ID,
                            Param = appID,
                            Name = character.Name,
                        });
                        await apprentice.SendAsync(new MsgGuide
                        {
                            Action = GuideAction.ClearBattlePower,
                            Identity = appID,
                        });
                    }
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task HandleGuideAsync(Client client, MsgGuide msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case GuideAction.InviteApprentice:
                        await InviteApprenticeAsync(client, msg.Param);
                        break;
                    case GuideAction.RequestMentor:
                        await RequestMentorAsync(client, msg.Param);
                        break;
                    case GuideAction.AcceptInviteApprentice:
                        await RespondInviteApprenticeAsync(client, msg.Identity, msg.Value != 0);
                        break;
                    case GuideAction.AcceptRequestMentor:
                        await RespondRequestMentorAsync(client, msg.Identity, msg.Value != 0);
                        break;
                    case GuideAction.AutoRefuse:
                        await RespondRequestMentorAsync(client, msg.Identity, accept: false);
                        break;
                    case GuideAction.LeaveMentor:
                        await LeaveMentorAsync(client);
                        break;
                    case GuideAction.ExpelApprentice:
                        await ExpelApprenticeAsync(client, msg.Param);
                        break;
                    default:
                        LogEvents.For(typeof(Mentors), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                            msg.Type, msg.Action, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task HandleGuideInfoAsync(Client client, MsgGuideInfo msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Mode)
                {
                    case GuideInfoMode.Mentor:
                        await SendMentorInfoAsync(client);
                        break;
                    case GuideInfoMode.Apprentice:
                        await SendApprenticesInfoAsync(client);
                        break;
                    case GuideInfoMode.SecondGenApprentice:
                        await SendSecondGenInfoAsync(client, msg.Identity);
                        break;
                    default:
                        LogEvents.For(typeof(Mentors), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} mode {Mode} ({Length} bytes)",
                            msg.Type, msg.Mode, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task HandleContributeAsync(Client client, MsgContribute msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case ContributeAction.Rewards:
                        await SendRewardsAsync(client);
                        break;
                    case ContributeAction.Apprentices:
                        await SendCurrentContributionsAsync(client);
                        break;
                    case ContributeAction.ClaimExperience:
                        await ClaimExperienceAsync(client, partial: false, requestedUnits: 0);
                        break;
                    case ContributeAction.ClaimExperiencePart:
                        await ClaimExperienceAsync(client, partial: true, requestedUnits: msg.Experience);
                        break;
                    case ContributeAction.ClaimBlessing:
                        await ClaimBlessingAsync(client);
                        break;
                    case ContributeAction.ClaimComposition:
                        await ClaimCompositionAsync(client);
                        break;
                    default:
                        LogEvents.For(typeof(Mentors), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                            msg.Type, msg.Action, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task RecordExperienceAsync(Client apprentice, byte fromLevel, ulong amount)
        {
            if (apprentice?.Character == null || amount == 0)
                return;
            double time = Kernel.Experience.TimeOfExperience(fromLevel, amount) * RewardShare;
            if (time <= 0)
                return;

            await Gate.WaitAsync();
            try
            {
                if (!ByApprentice.TryGetValue(apprentice.ID, out var rel))
                    return;
                rel.PendingExpTime += time;
                uint whole = (uint)Math.Floor(rel.PendingExpTime);
                if (whole == 0)
                    return;
                rel.PendingExpTime -= whole;

                rel.Experience = AddUInt(rel.Experience, whole);
                var pool = GetRewardPool(rel.MentorID);
                pool.Experience = AddUInt(pool.Experience, whole);
                await PersistContributionAsync(rel, pool);
                await NotifyOnlineMentorRewardsAsync(rel.MentorID, pool);
            }
            finally { Gate.Release(); }
        }

        public static async Task RecordBlessingAsync(Client apprentice, TimeSpan duration)
        {
            if (apprentice?.Character == null || duration <= TimeSpan.Zero)
                return;
            ushort hours = (ushort)Math.Min(ushort.MaxValue, (uint)(duration.TotalHours * RewardShare));
            if (hours == 0)
                return;

            await Gate.WaitAsync();
            try
            {
                if (!ByApprentice.TryGetValue(apprentice.ID, out var rel))
                    return;
                rel.Blessing = AddUShort(rel.Blessing, hours);
                var pool = GetRewardPool(rel.MentorID);
                pool.Blessing = AddUShort(pool.Blessing, hours);
                await PersistContributionAsync(rel, pool);
                await NotifyOnlineMentorRewardsAsync(rel.MentorID, pool);
            }
            finally { Gate.Release(); }
        }

        public static async Task RecordCompositionAsync(Client apprentice, uint composePoints)
        {
            if (apprentice?.Character == null || composePoints == 0)
                return;
            ushort points = (ushort)Math.Min(ushort.MaxValue, (uint)(composePoints * RewardShare));
            if (points == 0)
                return;

            await Gate.WaitAsync();
            try
            {
                if (!ByApprentice.TryGetValue(apprentice.ID, out var rel))
                    return;
                rel.Composition = AddUShort(rel.Composition, points);
                var pool = GetRewardPool(rel.MentorID);
                pool.Composition = AddUShort(pool.Composition, points);
                await PersistContributionAsync(rel, pool);
                await NotifyOnlineMentorRewardsAsync(rel.MentorID, pool);
            }
            finally { Gate.Release(); }
        }

        private static async Task InviteApprenticeAsync(Client mentor, uint targetID)
        {
            if (!Kernel.Players.TryGet(targetID, out var apprentice) || apprentice.Character == null || apprentice == mentor)
            {
                Reject(mentor, "invite", targetID, "offline");
                return;
            }
            if (!await ValidatePairAsync(mentor, apprentice, initiatorIsMentor: true))
                return;

            mentor.ApprenticeInvite = apprentice.ID;
            await apprentice.SendAsync(new MsgGuide
            {
                Action = GuideAction.AcceptInviteApprentice,
                Identity = mentor.ID,
                Param = apprentice.ID,
                Value = 1,
                Name = mentor.Character.Name,
            });
            await Talk.NoticeAsync(mentor, "Apprentice invitation sent.");
            LogEvents.For(typeof(Mentors), LogEvents.MentorRequested).Information(
                "{Mentor} invited {Apprentice} to be their apprentice",
                mentor.Character.Name, apprentice.Character.Name);
        }

        private static async Task RequestMentorAsync(Client apprentice, uint targetID)
        {
            if (!Kernel.Players.TryGet(targetID, out var mentor) || mentor.Character == null || mentor == apprentice)
            {
                Reject(apprentice, "request", targetID, "offline");
                return;
            }
            if (!await ValidatePairAsync(mentor, apprentice, initiatorIsMentor: false))
                return;

            apprentice.MentorRequest = mentor.ID;
            await mentor.SendAsync(new MsgGuide
            {
                Action = GuideAction.AcceptRequestMentor,
                Identity = apprentice.ID,
                Param = mentor.ID,
                Value = 1,
                Name = apprentice.Character.Name,
            });
            await Talk.NoticeAsync(apprentice, "Mentor request sent.");
            LogEvents.For(typeof(Mentors), LogEvents.MentorRequested).Information(
                "{Apprentice} asked {Mentor} to be their mentor",
                apprentice.Character.Name, mentor.Character.Name);
        }

        private static async Task RespondInviteApprenticeAsync(Client apprentice, uint mentorID, bool accept)
        {
            if (!Kernel.Players.TryGet(mentorID, out var mentor) || mentor.Character == null ||
                mentor.ApprenticeInvite != apprentice.ID)
            {
                Reject(apprentice, "accept_invite", mentorID, "not_invited");
                return;
            }
            mentor.ApprenticeInvite = 0;
            if (!accept)
            {
                await Talk.NoticeAsync(mentor, $"{apprentice.Character.Name} declined to be your apprentice.");
                Reject(mentor, "invite", apprentice.ID, "refused");
                return;
            }
            if (!await ValidatePairAsync(mentor, apprentice, initiatorIsMentor: false))
                return;
            await EnrollAsync(mentor, apprentice);
        }

        private static async Task RespondRequestMentorAsync(Client mentor, uint apprenticeID, bool accept)
        {
            if (!Kernel.Players.TryGet(apprenticeID, out var apprentice) || apprentice.Character == null ||
                apprentice.MentorRequest != mentor.ID)
            {
                Reject(mentor, "accept_request", apprenticeID, "not_requested");
                return;
            }
            apprentice.MentorRequest = 0;
            if (!accept)
            {
                await Talk.NoticeAsync(apprentice, $"{mentor.Character.Name} declined to be your mentor.");
                Reject(apprentice, "request", mentor.ID, "refused");
                return;
            }
            if (!await ValidatePairAsync(mentor, apprentice, initiatorIsMentor: true))
                return;
            await EnrollAsync(mentor, apprentice);
        }

        private static async Task<bool> ValidatePairAsync(Client mentor, Client apprentice, bool initiatorIsMentor)
        {
            var initiator = initiatorIsMentor ? mentor : apprentice;
            uint targetID = initiatorIsMentor ? apprentice.ID : mentor.ID;

            if (mentor.Life.Dead || apprentice.Life.Dead)
            {
                Reject(initiator, "enroll", targetID, "dead");
                await Talk.NoticeAsync(initiator, "Wait for the target to revive.");
                return false;
            }
            if (ByApprentice.TryGetValue(apprentice.ID, out var existing))
            {
                Reject(initiator, "enroll", targetID, "already_has_mentor");
                if (existing.MentorID == mentor.ID)
                {
                    await initiator.SendAsync(new MsgGuide
                    {
                        Action = GuideAction.AlreadyApprentice,
                        Identity = mentor.ID,
                        Param = apprentice.ID,
                        Name = apprentice.Character.Name,
                    });
                }
                else if (initiatorIsMentor)
                {
                    await mentor.SendAsync(new MsgGuide
                    {
                        Action = GuideAction.TargetHasMentor,
                        Identity = apprentice.ID,
                        Name = apprentice.Character.Name,
                    });
                }
                else
                {
                    await apprentice.SendAsync(new MsgGuide
                    {
                        Action = GuideAction.AlreadyHasMentor,
                        Identity = apprentice.ID,
                    });
                }
                return false;
            }
            if (ByApprentice.TryGetValue(mentor.ID, out var mentorOfMentor) && mentorOfMentor.MentorID == apprentice.ID)
            {
                Reject(initiator, "enroll", targetID, "already_mentor");
                await Talk.NoticeAsync(initiator, "The target is already your mentor.");
                return false;
            }
            if (!Kernel.TutorTypes.CanBeMentor(mentor.Character))
            {
                Reject(initiator, "enroll", targetID, "mentor_level_low");
                await Talk.NoticeAsync(initiator,
                    $"You haven't reached level {Kernel.TutorTypes.MinMentorLevel}. You can't recruit apprentices.");
                return false;
            }
            if (!Kernel.TutorTypes.MeetsLevelGap(mentor.Character, apprentice.Character))
            {
                Reject(initiator, "enroll", targetID, "level_gap");
                await Talk.NoticeAsync(initiator, "The target's level is too high.");
                return false;
            }
            uint maxApps = Kernel.TutorTypes.MaxApprentices(mentor.Character);
            int currentApps = ByMentor.TryGetValue(mentor.ID, out var set) ? set.Count : 0;
            if (currentApps >= maxApps)
            {
                Reject(initiator, "enroll", targetID, "mentor_full");
                await Talk.NoticeAsync(initiator, $"{mentor.Character.Name} can't recruit more apprentices.");
                return false;
            }
            return true;
        }

        private static async Task EnrollAsync(Client mentor, Client apprentice)
        {
            uint enrolled = TodayDate();
            var dbRow = new DbMentor
            {
                ApprenticeID = apprentice.ID,
                MentorID = mentor.ID,
                Enrolled = enrolled,
            };
            await MentorsRepository.AddRelationAsync(dbRow);

            var rel = new Relation
            {
                ApprenticeID = apprentice.ID,
                MentorID = mentor.ID,
                Enrolled = enrolled,
            };
            ByApprentice[apprentice.ID] = rel;
            GetApprenticeSet(mentor.ID).Add(apprentice.ID);

            uint bp = SharedBattlePower(mentor.Character, apprentice.Character);
            await apprentice.SendAsync(BuildInfo(
                GuideInfoMode.Mentor, rel, mentor.Character.Name, mentor.Character,
                online: true, sharedBp: bp, apprenticeName: apprentice.Character.Name));
            await apprentice.SendAsync(BuildBattlePowerUpdate(mentor.ID, apprentice.ID, bp));
            await mentor.SendAsync(BuildInfo(
                GuideInfoMode.Apprentice, rel, mentor.Character.Name, apprentice.Character, online: true, sharedBp: 0));

            await Talk.NoticeAsync(mentor, $"You've built mentor-apprentice relations with {apprentice.Character.Name}.");
            await Talk.NoticeAsync(apprentice, $"You've built mentor-apprentice relations with {mentor.Character.Name}.");
            LogEvents.For(typeof(Mentors), LogEvents.MentorAdded).Information(
                "{Mentor} ({MentorId}) enrolled {Apprentice} ({ApprenticeId}) as apprentice",
                mentor.Character.Name, mentor.ID, apprentice.Character.Name, apprentice.ID);
        }

        private static async Task LeaveMentorAsync(Client apprentice)
        {
            if (!ByApprentice.TryGetValue(apprentice.ID, out var rel))
            {
                Reject(apprentice, "leave", 0, "no_mentor");
                return;
            }
            if (!await Progress.ChangeSilverAsync(apprentice, -(long)LeaveCost, "mentor_leave"))
            {
                Reject(apprentice, "leave", rel.MentorID, "not_enough_silver");
                await Talk.NoticeAsync(apprentice, "You don't have enough silver.");
                return;
            }

            await RemoveRelationAsync(rel, apprentice.Character.Name, "leave");
        }

        private static async Task ExpelApprenticeAsync(Client mentor, uint apprenticeID)
        {
            if (!ByApprentice.TryGetValue(apprenticeID, out var rel) || rel.MentorID != mentor.ID)
            {
                Reject(mentor, "expel", apprenticeID, "not_apprentice");
                return;
            }
            string apprenticeName = Kernel.Players.TryGet(apprenticeID, out var app) && app.Character != null
                ? app.Character.Name
                : (await MentorsRepository.FindCharacterAsync(apprenticeID))?.Name ?? string.Empty;

            await RemoveRelationAsync(rel, apprenticeName, "expel");
        }

        private static async Task RemoveRelationAsync(Relation rel, string apprenticeName, string reason)
        {
            await MentorsRepository.RemoveRelationAsync(rel.ApprenticeID);
            ByApprentice.Remove(rel.ApprenticeID);
            if (ByMentor.TryGetValue(rel.MentorID, out var set))
            {
                set.Remove(rel.ApprenticeID);
                if (set.Count == 0)
                    ByMentor.Remove(rel.MentorID);
            }

            var removeMsg = new MsgGuide
            {
                Action = GuideAction.Remove,
                Identity = rel.MentorID,
                Param = rel.ApprenticeID,
                Value = 0,
                Name = apprenticeName,
            };
            if (Kernel.Players.TryGet(rel.ApprenticeID, out var apprentice) && apprentice.Character != null)
            {
                await apprentice.SendAsync(removeMsg);
                await apprentice.SendAsync(new MsgGuide
                {
                    Action = GuideAction.ClearBattlePower,
                    Identity = rel.ApprenticeID,
                });
            }
            if (Kernel.Players.TryGet(rel.MentorID, out var mentor) && mentor.Character != null)
            {
                await mentor.SendAsync(removeMsg);
            }
            LogEvents.For(typeof(Mentors), LogEvents.MentorRemoved).Information(
                "Mentor relation ended between mentor {MentorId} and apprentice {ApprenticeId} ({Reason})",
                rel.MentorID, rel.ApprenticeID, reason);
        }

        private static async Task SendMentorInfoAsync(Client apprentice)
        {
            if (!ByApprentice.TryGetValue(apprentice.ID, out var rel))
                return;
            bool online = Kernel.Players.TryGet(rel.MentorID, out var mentorClient) && mentorClient.Character != null;
            DbCharacter mentorChar = online
                ? mentorClient.Character
                : await MentorsRepository.FindCharacterAsync(rel.MentorID);
            if (mentorChar == null)
                return;

            uint bp = online ? SharedBattlePower(mentorChar, apprentice.Character) : 0;
            await apprentice.SendAsync(BuildInfo(
                GuideInfoMode.Mentor, rel, mentorChar.Name, mentorChar,
                online: online, sharedBp: bp, apprenticeName: apprentice.Character.Name));
            if (online && bp > 0)
                await apprentice.SendAsync(BuildBattlePowerUpdate(rel.MentorID, apprentice.ID, bp));
        }

        private static async Task SendApprenticesInfoAsync(Client mentor)
        {
            if (!ByMentor.TryGetValue(mentor.ID, out var appIDs) || appIDs.Count == 0)
                return;
            foreach (var appID in appIDs.ToList())
            {
                if (!ByApprentice.TryGetValue(appID, out var rel))
                    continue;
                bool online = Kernel.Players.TryGet(appID, out var appClient) && appClient.Character != null;
                DbCharacter appChar = online
                    ? appClient.Character
                    : await MentorsRepository.FindCharacterAsync(appID);
                if (appChar == null)
                    continue;
                await mentor.SendAsync(BuildInfo(
                    GuideInfoMode.Apprentice, rel, mentor.Character.Name, appChar, online: online, sharedBp: 0));
            }
        }

        private static async Task SendSecondGenInfoAsync(Client mentor, uint firstGenID)
        {
            if (!ByMentor.TryGetValue(mentor.ID, out var firstGenSet) || !firstGenSet.Contains(firstGenID))
                return;
            if (!ByMentor.TryGetValue(firstGenID, out var secondGenSet) || secondGenSet.Count == 0)
                return;

            string firstGenName = Kernel.Players.TryGet(firstGenID, out var firstClient) && firstClient.Character != null
                ? firstClient.Character.Name
                : (await MentorsRepository.FindCharacterAsync(firstGenID))?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(firstGenName))
                return;

            foreach (var secondID in secondGenSet.ToList())
            {
                if (!ByApprentice.TryGetValue(secondID, out var rel))
                    continue;
                bool online = Kernel.Players.TryGet(secondID, out var secondClient) && secondClient.Character != null;
                DbCharacter secondChar = online
                    ? secondClient.Character
                    : await MentorsRepository.FindCharacterAsync(secondID);
                if (secondChar == null)
                    continue;
                await mentor.SendAsync(BuildInfo(
                    GuideInfoMode.SecondGenApprentice, rel, firstGenName, secondChar, online: online, sharedBp: 0));
            }
        }

        private static Task SendRewardsAsync(Client client)
        {
            Rewards.TryGetValue(client.ID, out var pool);
            return client.SendAsync(new MsgContribute
            {
                Action = ContributeAction.Rewards,
                Experience = pool?.Experience ?? 0,
                Blessing = pool?.Blessing ?? 0,
                Composition = pool?.Composition ?? 0,
            });
        }

        private static async Task SendCurrentContributionsAsync(Client mentor)
        {
            await mentor.SendAsync(new MsgContribute { Action = ContributeAction.ResetCurrent });

            if (ByMentor.TryGetValue(mentor.ID, out var firstGen))
            {
                foreach (var appID in firstGen.ToList())
                {
                    if (!ByApprentice.TryGetValue(appID, out var rel))
                        continue;
                    string appName = await ResolveNameAsync(appID);
                    if (string.IsNullOrEmpty(appName))
                        continue;
                    await mentor.SendAsync(new MsgContribute
                    {
                        Action = ContributeAction.Apprentices,
                        Generation = 0,
                        Experience = rel.Experience,
                        Blessing = rel.Blessing,
                        Composition = rel.Composition,
                        Strings = new List<string> { mentor.Character.Name, appName },
                    });

                    if (ByMentor.TryGetValue(appID, out var secondGen))
                    {
                        foreach (var secondID in secondGen.ToList())
                        {
                            if (!ByApprentice.TryGetValue(secondID, out var secondRel))
                                continue;
                            string secondName = await ResolveNameAsync(secondID);
                            if (string.IsNullOrEmpty(secondName))
                                continue;
                            await mentor.SendAsync(new MsgContribute
                            {
                                Action = ContributeAction.Apprentices,
                                Generation = 1,
                                Experience = secondRel.Experience,
                                Blessing = secondRel.Blessing,
                                Composition = secondRel.Composition,
                                Strings = new List<string> { appName, secondName },
                            });
                        }
                    }
                }
            }

            await mentor.SendAsync(new MsgContribute { Action = ContributeAction.Refresh });
        }

        private static async Task ClaimExperienceAsync(Client client, bool partial, ulong requestedUnits)
        {
            var character = client.Character;
            if (client.Life.Dead)
            {
                Reject(client, "claim_exp", 0, "dead");
                await Talk.NoticeAsync(client, "You can't claim EXP when you are dead.");
                return;
            }
            if (character.Level >= Kernel.Experience.MaxLevel)
            {
                Reject(client, "claim_exp", 0, "max_level");
                await Talk.NoticeAsync(client, "You are in the supreme level. You can't get more EXP.");
                return;
            }
            if (!Rewards.TryGetValue(client.ID, out var pool) || pool.Experience == 0)
            {
                Reject(client, "claim_exp", 0, "nothing");
                await SendRewardsAsync(client);
                return;
            }
            uint claim = partial
                ? (uint)Math.Min(pool.Experience, requestedUnits)
                : pool.Experience;
            if (claim == 0)
            {
                Reject(client, "claim_exp", 0, "zero");
                return;
            }

            pool.Experience -= claim;
            await SavePoolAsync(client.ID, pool);
            ulong experience = Kernel.Experience.ExperienceOfTime(character.Level, character.Experience, claim);
            await Progress.AwardExperienceAsync(client, experience, "mentor_reward");
            await SendRewardsAsync(client);
            LogEvents.For(typeof(Mentors), LogEvents.MentorRewardClaimed).Information(
                "{Name} claimed {Units} mentor EXP time ({Experience} experience)",
                character.Name, claim, experience);
        }

        private static async Task ClaimBlessingAsync(Client client)
        {
            var character = client.Character;
            if (client.Life.Dead)
            {
                Reject(client, "claim_blessing", 0, "dead");
                await Talk.NoticeAsync(client, "You can't claim Blessing when you are dead.");
                return;
            }
            if (!Rewards.TryGetValue(client.ID, out var pool) || pool.Blessing == 0)
            {
                Reject(client, "claim_blessing", 0, "nothing");
                await SendRewardsAsync(client);
                return;
            }

            ushort hours = pool.Blessing;
            pool.Blessing = 0;
            await SavePoolAsync(client.ID, pool);
            await HeavenBlessing.AddAsync(client, TimeSpan.FromHours(hours), "mentor_reward");
            await SendRewardsAsync(client);
            LogEvents.For(typeof(Mentors), LogEvents.MentorRewardClaimed).Information(
                "{Name} claimed {Hours} hours of mentor blessing", character.Name, hours);
        }

        private static async Task ClaimCompositionAsync(Client client)
        {
            var character = client.Character;
            if (!Rewards.TryGetValue(client.ID, out var pool) || pool.Composition < CompositionPerStone)
            {
                Reject(client, "claim_stone", 0, "insufficient");
                await Talk.NoticeAsync(client, "You don't have +1 stone to claim.");
                await SendRewardsAsync(client);
                return;
            }
            if (!Kernel.ItemTypes.TryGet(PlusOneStoneTypeID, out var stoneType))
            {
                Reject(client, "claim_stone", PlusOneStoneTypeID, "no_item_type");
                return;
            }
            if (client.Inventory == null || client.Inventory.FreeSlots == 0)
            {
                Reject(client, "claim_stone", 0, "inventory_full");
                await Talk.NoticeAsync(client, "Your inventory is full.");
                await SendRewardsAsync(client);
                return;
            }

            pool.Composition -= CompositionPerStone;
            await SavePoolAsync(client.ID, pool);
            await ItemActions.GiveAsync(client, stoneType, "mentor_reward");
            await SendRewardsAsync(client);
            LogEvents.For(typeof(Mentors), LogEvents.MentorRewardClaimed).Information(
                "{Name} claimed a +1 Stone from mentor rewards", character.Name);
        }

        private static MsgGuideInfo BuildInfo(
            GuideInfoMode mode, Relation rel, string mentorName, DbCharacter subject,
            bool online, uint sharedBp, string apprenticeName = null)
        {
            uint guildID = 0, guildRank = 0;
            if (Guilds.Of(subject.CharacterID) is Guilds.Membership guild)
            {
                guildID = guild.Guild.ID;
                guildRank = (uint)guild.Member.Rank;
            }
            string secondName = mode == GuideInfoMode.Mentor
                ? (apprenticeName ?? string.Empty)
                : subject.Name;
            return new MsgGuideInfo
            {
                Mode = mode,
                Identity = rel.MentorID,
                TargetID = rel.ApprenticeID,
                Mesh = (uint)(subject.Mesh + subject.Avatar * 10000),
                SharedBattlePower = sharedBp,
                BetrayHour = NotBetraying,
                EnrolDate = rel.Enrolled,
                Level = subject.Level,
                Profession = subject.CurrentClass,
                PkPoints = (short)Math.Min(short.MaxValue, subject.KillPoints),
                SyndicateID = guildID,
                SyndicateRank = guildRank,
                SyndicateTitle = 0,
                Sex = (subject.Mesh % 10) is 3 or 4 ? 1 : 2,
                Online = online,
                Experience = rel.Experience,
                Blessing = rel.Blessing,
                Composition = rel.Composition,
                Strings = new List<string>
                {
                    mentorName,
                    secondName,
                    string.IsNullOrEmpty(subject.Spouse) ? Marriage.None : subject.Spouse,
                },
            };
        }

        private static MsgGuideInfo BuildBattlePowerUpdate(uint mentorID, uint apprenticeID, uint bp) =>
            new MsgGuideInfo
            {
                Mode = GuideInfoMode.BattlePower,
                Identity = mentorID,
                TargetID = apprenticeID,
                SharedBattlePower = bp,
            };

        private static async Task<string> ResolveNameAsync(uint characterID)
        {
            if (Kernel.Players.TryGet(characterID, out var player) && player.Character != null)
                return player.Character.Name;
            return (await MentorsRepository.FindCharacterAsync(characterID))?.Name ?? string.Empty;
        }

        private static HashSet<uint> GetApprenticeSet(uint mentorID)
        {
            if (!ByMentor.TryGetValue(mentorID, out var set))
                ByMentor[mentorID] = set = new HashSet<uint>();
            return set;
        }

        private static RewardPool GetRewardPool(uint mentorID)
        {
            if (!Rewards.TryGetValue(mentorID, out var pool))
                Rewards[mentorID] = pool = new RewardPool();
            return pool;
        }

        private static async Task PersistContributionAsync(Relation rel, RewardPool pool)
        {
            await MentorsRepository.SaveRelationContributionAsync(
                rel.ApprenticeID, rel.Experience, rel.Blessing, rel.Composition);
            await SavePoolAsync(rel.MentorID, pool);
        }

        private static Task SavePoolAsync(uint mentorID, RewardPool pool) =>
            MentorsRepository.SaveRewardAsync(new DbMentorReward
            {
                CharacterID = mentorID,
                Experience = pool.Experience,
                Blessing = pool.Blessing,
                Composition = pool.Composition,
            });

        private static Task NotifyOnlineMentorRewardsAsync(uint mentorID, RewardPool pool)
        {
            if (!Kernel.Players.TryGet(mentorID, out var mentor) || mentor.Character == null)
                return Task.CompletedTask;
            return mentor.SendAsync(new MsgContribute
            {
                Action = ContributeAction.Rewards,
                Experience = pool.Experience,
                Blessing = pool.Blessing,
                Composition = pool.Composition,
            });
        }

        private static uint TodayDate()
        {
            var d = Clock.Local(DateTime.UtcNow);
            return (uint)(d.Year * 10000 + d.Month * 100 + d.Day);
        }

        private static uint AddUInt(uint a, uint b) => uint.MaxValue - a < b ? uint.MaxValue : a + b;

        private static ushort AddUShort(ushort a, ushort b) => (ushort)Math.Min(ushort.MaxValue, a + b);

        private static void Reject(Client client, string kind, uint id, string reason) =>
            LogEvents.For(typeof(Mentors), LogEvents.MentorRejected).Warning(
                "{Name} can't {Kind} mentor/apprentice {Id}: {Reason}", client.Character?.Name, kind, id, reason);
    }
}

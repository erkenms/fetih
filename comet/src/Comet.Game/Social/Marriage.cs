namespace Comet.Game.Social
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgInteract;
    using static Comet.Game.Packets.MsgTalk;

    // Marriages are stored as the spouse's name on both characters ("None" when single).
    // A marriage or divorce changes two characters at once, so changes run under one gate.
    public static class Marriage
    {
        public const string None = "None";
        public const uint MeteorTear = 1088002;
        public const uint ProposeCommand = 1067;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static string SpouseOf(Character character) =>
            string.IsNullOrEmpty(character?.Spouse) ? None : character.Spouse;

        public static bool IsMarried(Character character) => SpouseOf(character) != None;

        public static bool IsMale(Character character) => character.Mesh % 10000 < 2000;

        public static async Task CourtAsync(Client client, MsgInteract msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                Kernel.Players.TryGet(msg.TargetID, out var target);
                string reason = ValidatePair(client, target);
                if (reason != null)
                {
                    await RejectAsync(client, "propose", msg.TargetID, target, reason);
                    return;
                }

                client.MarriageProposal = target.ID;
                var character = client.Character;
                await target.SendAsync(new MsgInteract(
                    InteractionType.Court, client.ID, target.ID, character.X, character.Y, 0));
                LogEvents.For(typeof(Marriage), LogEvents.MarriageProposed).Information(
                    "{Name} proposed to {Target}", character.Name, target.Character.Name);
            }
            finally { Gate.Release(); }
        }

        public static async Task MarryAsync(Client client, MsgInteract msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                Kernel.Players.TryGet(msg.TargetID, out var asker);
                string reason = ValidatePair(client, asker) ??
                    (asker.MarriageProposal != client.ID ? "not_proposed" : null);
                if (reason != null)
                {
                    await RejectAsync(client, "marry", msg.TargetID, asker, reason);
                    return;
                }

                asker.MarriageProposal = 0;
                if (client.MarriageProposal == asker.ID)
                    client.MarriageProposal = 0;

                var accepter = client.Character;
                var proposer = asker.Character;
                accepter.Spouse = proposer.Name;
                proposer.Spouse = accepter.Name;
                await CharactersRepository.SetMarriageAsync(
                    proposer.CharacterID, proposer.Spouse, accepter.CharacterID, accepter.Spouse);

                await BroadcastSpouseAsync(client);
                await BroadcastSpouseAsync(asker);

                var announcement = new MsgTalk(0, TalkChannel.Center,
                    $"{proposer.Name} and {accepter.Name} are now married!");
                foreach (var player in Kernel.Players.Values)
                    if (player.Character != null)
                        await player.SendAsync(announcement);

                LogEvents.For(typeof(Marriage), LogEvents.MarriageMarried).Information(
                    "{Proposer} and {Accepter} married", proposer.Name, accepter.Name);
            }
            finally { Gate.Release(); }
        }

        // Ends the player's marriage, taking a Meteor Tear; returns null when done, or why not
        public static async Task<string> DivorceAsync(Client client)
        {
            var character = client.Character;
            if (character == null)
                return "not_found";
            await Gate.WaitAsync();
            try
            {
                if (!IsMarried(character))
                {
                    LogReject(client, "divorce", 0, "not_married");
                    return "not_married";
                }

                var tear = client.Inventory?.Items.FirstOrDefault(i => i.TypeID == MeteorTear);
                if (tear == null || !client.Inventory.Remove(tear))
                {
                    LogReject(client, "divorce", 0, "no_item");
                    return "no_item";
                }

                await ItemsRepository.DeleteAsync(tear.ItemID);
                await client.SendAsync(MsgItem.Removed(tear.ItemID));

                string formerSpouse = character.Spouse;
                character.Spouse = None;
                if (Kernel.Players.TryGet(formerSpouse, out var spouse) && spouse.Character != null)
                    spouse.Character.Spouse = None;

                await CharactersRepository.ClearSpouseAsync(client.ID, formerSpouse, None);

                await BroadcastSpouseAsync(client);
                if (spouse?.Character != null)
                {
                    await BroadcastSpouseAsync(spouse);
                    await Talk.NoticeAsync(spouse, $"{character.Name} has divorced you.");
                }

                LogEvents.For(typeof(Marriage), LogEvents.MarriageDivorced).Information(
                    "{Name} divorced {Spouse}", character.Name, formerSpouse);
                return null;
            }
            finally { Gate.Release(); }
        }

        private static string ValidatePair(Client client, Client other)
        {
            var character = client.Character;
            if (other?.Character == null || other == client)
                return "offline";
            if (other.Character.MapID != character.MapID ||
                !GameMap<Client>.InView(character.X, character.Y, other.Character.X, other.Character.Y))
                return "too_far";
            if (IsMarried(character))
                return "already_married";
            if (IsMarried(other.Character))
                return "target_married";
            if (IsMale(character) == IsMale(other.Character))
                return "same_gender";
            return null;
        }

        private static async Task BroadcastSpouseAsync(Client client)
        {
            var msg = MsgName.SetSpouse(client.ID, SpouseOf(client.Character));
            await client.SendAsync(msg);
            await Movement.SendToViewAsync(client, msg);
        }

        private static Task RejectAsync(Client client, string action, uint targetID, Client target, string reason)
        {
            LogReject(client, action, targetID, reason);
            string notice = reason switch
            {
                "already_married" => "You are already married.",
                "target_married" => $"{target.Character.Name} is already married.",
                "same_gender" => "You can't marry someone of the same gender.",
                "too_far" => "That player is too far away.",
                _ => null,
            };
            return notice == null ? Task.CompletedTask : Talk.NoticeAsync(client, notice);
        }

        private static void LogReject(Client client, string action, uint targetID, string reason) =>
            LogEvents.For(typeof(Marriage), LogEvents.MarriageRejected).Warning(
                "{Name} can't {Action} {TargetId}: {Reason}",
                client.Character?.Name, action, targetID, reason);
    }
}

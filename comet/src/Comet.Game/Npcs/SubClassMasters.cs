namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // The seven sub-class masters in the JobCenter (map 1004), matching SubProfessionInfo.ini.
    // Each teaches their sub-class after level 70 for its required items, promotes phases 2..9,
    // and lets the player switch their active sub-class.
    public static class SubClassMasters
    {
        public const uint LeadPerformerID = 7776, LeadApothecaryID = 7777, LeadMartialArtistID = 7778,
            LeadWarlockID = 7779, LeadSageID = 7780, LeadChiMasterID = 7781, WranglerID = 8616;

        public static readonly uint[] AllIDs =
        {
            LeadPerformerID, LeadApothecaryID, LeadMartialArtistID,
            LeadWarlockID, LeadSageID, LeadChiMasterID, WranglerID,
        };

        private const byte Join = 1, Promote = 2, Activate = 3;

        public static async Task TalkAsync(NpcTalk talk)
        {
            if (!Kernel.SubClasses.TryGetByNpc(talk.Npc.NpcID, out var def))
            {
                await SayAsync(talk, "Sub-classes are not available right now.");
                return;
            }

            if (talk.Option == Join)
            {
                string reason = await SubClasses.LearnAsync(talk.Client, def.Id);
                if (reason == null)
                {
                    await SayAsync(talk, $"Congratulations! You have joined the {def.Title} sub-class at Phase 1.");
                    return;
                }
                await SayAsync(talk, ExplainLearnFailure(def, reason));
                return;
            }

            if (talk.Option == Promote)
            {
                string reason = await SubClasses.PromoteAsync(talk.Client, def.Id);
                if (reason == null)
                {
                    talk.Client.SubClasses.TryGet(def.Id, out var updated);
                    await SayAsync(talk, $"Congratulations! You have been promoted to Phase {updated?.Phase ?? 1} {def.Title}.");
                    return;
                }
                await SayAsync(talk, ExplainPromoteFailure(talk.Client, def, reason));
                return;
            }

            if (talk.Option == Activate)
            {
                string reason = await SubClasses.ActivateAsync(talk.Client, def.Id);
                await SayAsync(talk, reason == null
                    ? $"You have activated the {def.Title} sub-class."
                    : $"You haven't joined the {def.Title} sub-class yet.");
                return;
            }

            await GreetAsync(talk, def);
        }

        private static Task GreetAsync(NpcTalk talk, SubClassDefinition def)
        {
            var client = talk.Client;
            if (!client.SubClasses.TryGet(def.Id, out var entry))
            {
                var p1 = def.Phase(1);
                var req = SubClassTable.LearnItemOf(def.Id);
                string items = req == null ? "" : $" and bring {req.Count} {req.ItemName}(s)";
                return talk
                    .Text($"Welcome! Reach level {p1?.RequiredLevel ?? 70}{items} to join the {def.Title} sub-class.")
                    .Choice($"Join {def.Title}.", Join)
                    .Choice("Not now.", MsgTaskDialog.NoOption)
                    .ShowAsync();
            }

            bool isActive = client.Character.ActiveSubClass == (byte)def.Id;
            if (entry.Phase >= SubClassTable.MaxPhase)
            {
                talk.Text($"You are a Phase {entry.Phase} {def.Title} (Level {entry.Level}), the highest phase.");
                if (!isActive)
                    talk.Choice($"Activate {def.Title}.", Activate);
                return talk.Choice("I see.", MsgTaskDialog.NoOption).ShowAsync();
            }

            var next = def.Phase((byte)(entry.Phase + 1));
            talk.Text($"You are a Phase {entry.Phase} {def.Title} (Level {entry.Level}). {DescribePhaseRequirement(next)}");
            talk.Choice($"Promote to Phase {entry.Phase + 1}.", Promote);
            if (!isActive)
                talk.Choice($"Activate {def.Title}.", Activate);
            return talk.Choice("Not now.", MsgTaskDialog.NoOption).ShowAsync();
        }

        private static string ExplainLearnFailure(SubClassDefinition def, string reason)
        {
            var p1 = def.Phase(1);
            var req = SubClassTable.LearnItemOf(def.Id);
            return reason switch
            {
                "already_learned" => $"You have already joined the {def.Title} sub-class.",
                "level" => $"You need to reach level {p1?.RequiredLevel ?? 70} before joining {def.Title}.",
                "rebirths" => $"You need {p1?.RequiredRebirths ?? 0} rebirth(s) before joining {def.Title}.",
                "no_item" when req != null => $"Bring me {req.Count} {req.ItemName}(s) to join {def.Title}.",
                _ => $"You don't meet the requirements to join {def.Title} yet.",
            };
        }

        private static string ExplainPromoteFailure(Client client, SubClassDefinition def, string reason)
        {
            if (reason == "not_learned")
                return $"You haven't joined the {def.Title} sub-class yet.";
            if (reason == "max_phase")
                return $"You have already reached the highest phase of {def.Title}.";
            if (client.SubClasses.TryGet(def.Id, out var entry) &&
                def.Phase((byte)(entry.Phase + 1)) is { } next)
                return DescribePhaseRequirement(next);
            return $"You don't meet the requirements for the next phase of {def.Title}.";
        }

        private static string DescribePhaseRequirement(SubClassPhaseInfo next)
        {
            if (next == null)
                return string.Empty;
            string rebirths = next.RequiredRebirths switch
            {
                1 => " (Reborn once)",
                >= 2 => $" (Reborn {next.RequiredRebirths} times)",
                _ => string.Empty,
            };
            return $"Phase {next.Phase} requires level {next.RequiredLevel}{rebirths} and Sub-Class Level {next.RequiredSubLevel}.";
        }

        private static Task SayAsync(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .ShowAsync();
    }
}

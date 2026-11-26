namespace Fetih.Game.Npcs
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Game.Events;
    using Fetih.Game.Packets;
    using Fetih.Game.Tournaments;

    // The Team PK Manager and the Skill Team PK Manager in Twin City: sign-up while it's open,
    // prizes for the final eight's leaders
    public static class TeamPkManager
    {
        private const byte Face = 40;
        private const byte SignUp = 1, Claim = 2, Rules = 3;

        public static async Task TalkAsync(NpcTalk talk)
        {
            var ev = TeamPk.ByManager(talk.Npc.NpcID);
            var client = talk.Client;
            int group = TeamPk.GroupOf(client.Character.Level);
            switch (talk.Option)
            {
                case 0:
                    talk.Text($"The {ev.Title} is held every week. Team up with your friends: the top 8 teams of " +
                        "each level group are handsomely rewarded, and every match gives each fighter EXP and 1,000 " +
                        "Honor Points. " + ScheduleText(ev, group));
                    if (ev.PhaseOf(group) == TeamPk.Phase.SignUp || client.Team?.Leader != client)
                        talk.Choice("I'm on!", SignUp);
                    var reward = ev.RewardOf(client.ID);
                    if (reward != null && !reward.Claimed)
                        talk.Choice("I want to claim our prize.", Claim);
                    await talk.Choice("Tell me the rules.", Rules)
                        .Choice("Not interested.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case SignUp:
                    var refused = await ev.SignUpAsync(client);
                    if (refused != null)
                        await Say(talk, refused switch
                        {
                            "no_team" => "You have to be in a team. Your team leader signs the team up first.",
                            "leader_first" => "Your team leader has to sign your team up first.",
                            "started" => "The tournament has already started. No entry is allowed now.",
                            "signed_up" => "You have already signed up.",
                            "level" => "Your level is above your team's level group.",
                            "dead" => "You can't sign up while you are dead.",
                            "qualifier" => "Quit the Arena Qualifier and the Elite PK Tournament first.",
                            _ => "Sign-up isn't open now. " + ScheduleText(ev, group),
                        });
                    return;

                case Claim:
                    var rank = ev.RewardOf(client.ID)?.Rank ?? 0;
                    var failed = await ev.ClaimAsync(client);
                    await Say(talk, failed switch
                    {
                        null => rank switch
                        {
                            1 => "Congratulations, champions! Here is your prize.",
                            2 => "Congratulations on second place! Here is your prize.",
                            3 => "Congratulations on third place! Here is your prize.",
                            _ => "Congratulations on making the final eight! Here is your prize.",
                        },
                        "claimed" => "You have already claimed your prize.",
                        "inventory_full" => "Please make room in your inventory first.",
                        _ => "You have no prize to claim. The leaders of the final eight teams can claim theirs until the next tournament opens.",
                    });
                    return;

                case Rules:
                    await Say(talk, (ev.Capped
                        ? "Gear counts as at most Super, one socket, +4 and level 120, nobility as at most Earl, " +
                          "sub-classes but the Apothecary as at most phase 5, and shared battle power not at all. "
                        : "") +
                        "Players without a team are sent out when the tournament starts, and dismissing the team " +
                        $"forfeits. Each knock-out match lasts {TeamPk.KnockoutTime.TotalMinutes:0} minutes, and " +
                        $"{TeamPk.FinalTime.TotalMinutes:0} once the final eight are left. If both teams still stand " +
                        "at the end, the one that dealt more damage wins.");
                    return;
            }
        }

        private static string ScheduleText(TeamPk ev, int group)
        {
            if (ev.PhaseOf(group) == TeamPk.Phase.SignUp)
                return $"Sign-up for the {TeamPk.GroupNames[group]} group is open now!";
            if (ev.PhaseOf(group) == TeamPk.Phase.Running)
                return $"The {TeamPk.GroupNames[group]} group is fighting now.";
            var scheduled = ev.EventOf(group);
            if (scheduled == null)
                return string.Empty;
            var next = EventSchedule.NextSignUp(scheduled, Clock.Local(DateTime.UtcNow));
            return $"The {TeamPk.GroupNames[group]} group signs up from {next:dddd HH:mm}.";
        }

        private static Task Say(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(Face)
            .ShowAsync();
    }
}

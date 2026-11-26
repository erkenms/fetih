namespace Fetih.Game.Npcs
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Game.Events;
    using Fetih.Game.Packets;
    using Fetih.Game.Tournaments;

    // The Elite PK Envoy in Twin City: sign-up while it's open, prizes for the final eight
    public static class ElitePkEnvoy
    {
        public const uint ID = ElitePk.EnvoyID;
        private const byte Face = 9;
        private const byte SignUp = 1, Claim = 2, Rules = 3;

        public static async Task TalkAsync(NpcTalk talk)
        {
            var client = talk.Client;
            int group = ElitePk.GroupOf(client.Character.Level);
            switch (talk.Option)
            {
                case 0:
                    talk.Text("The Elite PK Tournament is held every week. The best fighters of each level group " +
                        "battle it out until only the champion is left. " + ScheduleText(group));
                    if (ElitePk.PhaseOf(group) == ElitePk.Phase.SignUp)
                        talk.Choice("Sign me up!", SignUp);
                    var reward = ElitePk.RewardOf(client.ID);
                    if (reward != null && !reward.Claimed)
                        talk.Choice("Claim my prize.", Claim);
                    await talk.Choice("Tell me the rules.", Rules)
                        .Choice("Just passing by.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case SignUp:
                    var refused = await ElitePk.SignUpAsync(client);
                    if (refused != null)
                        await Say(talk, refused switch
                        {
                            "started" => "The tournament of your level group has already started. No entry is allowed now.",
                            "signed_up" => "You have already signed up.",
                            "dead" => "You can't sign up while you are dead.",
                            "qualifier" => "Quit the Arena Qualifier first.",
                            "team_pk" => "You are taking part in a team tournament.",
                            _ => "Sign-up isn't open now. " + ScheduleText(group),
                        });
                    return;

                case Claim:
                    var rank = ElitePk.RewardOf(client.ID)?.Rank ?? 0;
                    var failed = await ElitePk.ClaimAsync(client);
                    await Say(talk, failed switch
                    {
                        null => rank switch
                        {
                            1 => "Congratulations, champion! Here is your prize.",
                            2 => "Congratulations on second place! Here is your prize.",
                            3 => "Congratulations on third place! Here is your prize.",
                            _ => "Congratulations on making the final eight! Here is your prize.",
                        },
                        "claimed" => "You have already claimed your prize.",
                        "inventory_full" => "Please make room in your inventory first.",
                        _ => "You have no prize to claim. The final eight of each tournament can claim theirs until the next one opens.",
                    });
                    return;

                case Rules:
                    await Say(talk, "Wait in the waiting room until your matches begin; leaving it forfeits. Each match " +
                        "lasts 3 minutes, and 5 minutes once the final eight are left. If nobody falls, the one who " +
                        "dealt more damage wins. Every match earns you an Elite PK EXP Pack and 1,000 Honor Points. " +
                        "Lose once and you're out.");
                    return;
            }
        }

        private static string ScheduleText(int group)
        {
            if (ElitePk.PhaseOf(group) == ElitePk.Phase.SignUp)
                return $"Sign-up for the {ElitePk.GroupNames[group]} group is open now!";
            if (ElitePk.PhaseOf(group) == ElitePk.Phase.Running)
                return $"The {ElitePk.GroupNames[group]} group is fighting now.";
            var ev = ElitePk.EventOf(group);
            if (ev == null)
                return string.Empty;
            var next = EventSchedule.NextSignUp(ev, Clock.Local(DateTime.UtcNow));
            return $"The {ElitePk.GroupNames[group]} group signs up from {next:dddd HH:mm}.";
        }

        private static Task Say(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(Face)
            .ShowAsync();
    }
}

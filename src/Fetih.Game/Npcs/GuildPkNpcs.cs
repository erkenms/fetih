namespace Fetih.Game.Npcs
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.Tournaments;

    // The Guild Contest Emissary and Guild PK Emissary in Twin City let guild members into the
    // contest and their guild's match; the Awarders of places 1-8 pay leaders and deputies
    public static class GuildPkNpcs
    {
        private const byte Face = 9;
        private const byte Enter = 1, Rules = 2, ClaimContest = 3, ClaimTournament = 4;

        public static async Task ContestEmissaryAsync(NpcTalk talk)
        {
            switch (talk.Option)
            {
                case 0:
                    talk.Text("Every Monday from 20:30 to 22:30 guilds compete in the Guild Contest. Kill the monsters " +
                        "in the contest map to win points for your guild. The top 8 guilds enter the Guild PK Tournament.");
                    if (GuildPk.ContestPhaseNow != GuildPk.Phase.Idle)
                        talk.Choice("Let me in!", Enter);
                    await talk.Choice("Just passing by.", MsgTaskDialog.NoOption).Face(Face).ShowAsync();
                    return;
                case Enter:
                    var refused = await GuildPk.EnterContestAsync(talk.Client);
                    if (refused != null)
                        await Say(talk, refused switch
                        {
                            "no_guild" => "Only guild members can take part in the Guild Contest.",
                            "dead" => "You can't enter while you are dead.",
                            _ => "The Guild Contest isn't open now. It opens on Monday at 20:25.",
                        });
                    return;
            }
        }

        public static async Task PkEmissaryAsync(NpcTalk talk)
        {
            var client = talk.Client;
            switch (talk.Option)
            {
                case 0:
                    var opponent = Social.Guilds.Of(client.ID) is { } m ? GuildPk.MatchOf(m.Guild.ID) : null;
                    talk.Text(opponent != null
                        ? $"Your guild fights {opponent} now. Members who joined the guild over a week ago may enter."
                        : "The top 8 guilds of the Guild Contest fight the Guild PK Tournament: rounds on Tuesday, " +
                          "Wednesday and Thursday from 20:30, the final on Saturday from 21:30.");
                    if (opponent != null)
                        talk.Choice("Take me to the battle!", Enter);
                    await talk.Choice("Tell me the rules.", Rules)
                        .Choice("Just passing by.", MsgTaskDialog.NoOption).Face(Face).ShowAsync();
                    return;
                case Enter:
                    var refused = await GuildPk.EnterMatchAsync(client, DateTime.UtcNow);
                    if (refused != null)
                        await Say(talk, refused switch
                        {
                            "new_member" => "You must have been in your guild for more than a week to fight.",
                            "not_playing" => "Your guild has no match now.",
                            "dead" => "You can't enter while you are dead.",
                            _ => "There's no match now.",
                        });
                    return;
                case Rules:
                    await Say(talk, "Guilds 1, 3, 5 and 7 of the contest form Group A, guilds 2, 4, 6 and 8 Group B. " +
                        "Each guild meets the other three of its group: a win is 3 points, a draw 1. Seize the flag " +
                        "in the middle of the field: it earns your guild a point every second you hold it. In the " +
                        "final the guilds of the same place in both groups meet. Defeating an enemy or reviving a " +
                        "guild member earns you an Exploit.");
                    return;
            }
        }

        public static async Task AwarderAsync(NpcTalk talk)
        {
            int place = (int)(talk.Npc.NpcID - GuildPk.FirstAwarderID) + 1;
            var client = talk.Client;
            var now = DateTime.UtcNow;
            switch (talk.Option)
            {
                case 0:
                    talk.Text($"I reward the guild in place {place} of the Guild Contest and the Guild PK Tournament. " +
                        "The guild leader or a deputy may claim the prize.");
                    if (Social.Guilds.Of(client.ID) is { } m)
                    {
                        if (GuildPk.PrizeOf(m.Guild.ID, GuildPk.ContestPrize, now) is { Claimed: false })
                            talk.Choice("Claim the Guild Contest prize.", ClaimContest);
                        if (GuildPk.PrizeOf(m.Guild.ID, GuildPk.TournamentPrize, now) is { Claimed: false })
                            talk.Choice("Claim the Guild PK Tournament prize.", ClaimTournament);
                    }
                    await talk.Choice("Just passing by.", MsgTaskDialog.NoOption).Face(Face).ShowAsync();
                    return;
                case ClaimContest:
                case ClaimTournament:
                    byte kind = talk.Option == ClaimContest ? GuildPk.ContestPrize : GuildPk.TournamentPrize;
                    var guild = Social.Guilds.Of(client.ID)?.Guild.ID ?? 0;
                    int rank = GuildPk.PrizeOf(guild, kind, now)?.Rank ?? 0;
                    var failed = await GuildPk.ClaimAsync(client, kind, place, now);
                    await Say(talk, failed switch
                    {
                        null => $"Congratulations on place {rank}! Here are {GuildPk.ConquerPointsOf(kind, rank)} CPs.",
                        "not_leader" => "Only the guild leader or a deputy may claim the prize.",
                        "claimed" => "Your guild's prize has already been claimed.",
                        "other_awarder" => $"Your guild placed {rank}. Please see the Awarder of that place.",
                        _ => "Your guild has no prize to claim.",
                    });
                    return;
            }
        }

        private static Task Say(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(Face)
            .ShowAsync();
    }
}

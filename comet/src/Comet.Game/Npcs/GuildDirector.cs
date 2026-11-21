namespace Comet.Game.Npcs
{
    using System.Globalization;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.Social;

    public static class GuildDirector
    {
        public const uint ID = 10003;
        private const byte Face = 7;
        private const byte Create = 1, Disband = 2, Transfer = 3, ConfirmDisband = 4;
        private const byte NameTyped = 10, MemberTyped = 11;
        private static readonly string CostText = Guilds.CreateCost.ToString("N0", CultureInfo.InvariantCulture);

        public static async Task TalkAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var membership = Guilds.Of(client.ID);
            switch (talk.Option)
            {
                case 0:
                    talk.Text("Greetings! I am the guild director, in charge of administrating and managing guilds. " +
                        "What business do you have with me?");
                    if (membership == null)
                        talk.Choice("Create a guild.", Create);
                    else if (membership.Member.Rank == GuildRank.Leader)
                        talk.Choice("Disband my guild.", Disband).Choice("Pass on the leadership.", Transfer);
                    await talk.Choice("Just passing by.", MsgTaskDialog.NoOption).Face(Face).ShowAsync();
                    return;

                case Create:
                    if (client.Character.Level < Guilds.CreateLevel)
                        await Say(talk, $"You must reach level {Guilds.CreateLevel} to create a guild.");
                    else
                        await talk.Text($"Creating a guild costs {CostText} silver. What will your guild be called?")
                            .TextBox("Guild name", NameTyped, Guilds.MaxNameLength)
                            .Choice("Let me think it over.", MsgTaskDialog.NoOption)
                            .Face(Face).ShowAsync();
                    return;

                case NameTyped:
                    var reason = await Guilds.CreateAsync(client, talk.Input);
                    await Say(talk, reason switch
                    {
                        null => "Your guild has been set up. Lead it well!",
                        "level" => $"You must reach level {Guilds.CreateLevel} to create a guild.",
                        "in_guild" => "You are already in a guild.",
                        "silver" => $"You need {CostText} silver to create a guild.",
                        "name_taken" => "That name is already taken.",
                        _ => $"That name can't be used. Use 2 to {Guilds.MaxNameLength} letters or digits, without spaces.",
                    });
                    return;

                case Disband:
                    await talk.Text("Disbanding your guild is a decision not to be taken lightly. Are you sure?")
                        .Choice("I decide to disband.", ConfirmDisband)
                        .Choice("I changed my mind.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case ConfirmDisband:
                    if (await Guilds.DisbandAsync(client) != null)
                        await Say(talk, "Only the guild leader can disband the guild.");
                    else
                        client.ActiveNpc = 0;
                    return;

                case Transfer:
                    await talk.Text("Who shall lead the guild after you?")
                        .TextBox("Member name", MemberTyped, 15)
                        .Choice("Let me think it over.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case MemberTyped:
                    var refused = await Guilds.TransferAsync(client, talk.Input);
                    await Say(talk, refused switch
                    {
                        null => $"{talk.Input} leads the guild now.",
                        "not_member" => $"There is no member named {talk.Input} in your guild.",
                        "self" => "You already lead the guild.",
                        _ => "Only the guild leader can pass on the leadership.",
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

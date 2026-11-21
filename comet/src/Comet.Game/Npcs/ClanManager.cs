namespace Comet.Game.Npcs
{
    using System.Globalization;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.Social;

    public static class ClanManager
    {
        public const uint ID = 5533;
        private const byte Face = 7;
        private const byte Create = 1, Disband = 2, ConfirmDisband = 3;
        private const byte NameTyped = 10;
        private static readonly string CostText = Clans.CreateCost.ToString("N0", CultureInfo.InvariantCulture);
        private static readonly string FundText = Clans.CreateFund.ToString("N0", CultureInfo.InvariantCulture);

        public static async Task TalkAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var membership = Clans.Of(client.ID);
            switch (talk.Option)
            {
                case 0:
                    talk.Text("A clan is a family of friends who fight side by side. What can I do for you?");
                    if (membership == null)
                        talk.Choice("Create a clan.", Create);
                    else if (membership.Member.Rank == ClanRank.Leader)
                        talk.Choice("Disband my clan.", Disband);
                    await talk.Choice("Just passing by.", MsgTaskDialog.NoOption).Face(Face).ShowAsync();
                    return;

                case Create:
                    if (client.Character.Level < Clans.CreateLevel)
                        await Say(talk, $"You must reach level {Clans.CreateLevel} to create a clan.");
                    else
                        await talk.Text($"Creating a clan costs {CostText} silver, and {FundText} of it becomes " +
                                "the clan fund. What will your clan be called?")
                            .TextBox("Clan name", NameTyped, Guilds.MaxNameLength)
                            .Choice("Let me think it over.", MsgTaskDialog.NoOption)
                            .Face(Face).ShowAsync();
                    return;

                case NameTyped:
                    var reason = await Clans.CreateAsync(client, talk.Input);
                    await Say(talk, reason switch
                    {
                        null => "Your clan has been set up. Invite your friends to join it!",
                        "level" => $"You must reach level {Clans.CreateLevel} to create a clan.",
                        "in_clan" => "You are already in a clan.",
                        "silver" => $"You need {CostText} silver to create a clan.",
                        "name_taken" => "That name is already taken.",
                        _ => $"That name can't be used. Use 2 to {Guilds.MaxNameLength} letters or digits, without spaces.",
                    });
                    return;

                case Disband:
                    await talk.Text("Are you sure you want to disband your clan?")
                        .Choice("Yes, disband it.", ConfirmDisband)
                        .Choice("I changed my mind.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case ConfirmDisband:
                    if (await Clans.DisbandAsync(client) != null)
                        await Say(talk, "Only the clan leader can disband the clan.");
                    else
                        client.ActiveNpc = 0;
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

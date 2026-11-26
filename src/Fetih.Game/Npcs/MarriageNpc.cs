namespace Fetih.Game.Npcs
{
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.Social;

    // LoveStone in the market hands out the proposal cursor; Starlit ends a marriage
    // for a Meteor Tear.
    public static class MarriageNpc
    {
        public const uint LoveStoneID = 390;
        public const uint StarlitID = 600055;

        private const byte Face = 6;
        private const byte AskPropose = 1, StartPropose = 2;
        private const byte AskDivorce = 1, ConfirmDivorce = 2;

        public static async Task LoveStoneAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var character = client.Character;
            if (Marriage.IsMarried(character))
            {
                await SayAsync(talk, $"You are already married to {Marriage.SpouseOf(character)}.");
                return;
            }

            switch (talk.Option)
            {
                case 0:
                    await talk.Text("Sometimes you may feel lonely and need someone to share your journey. " +
                            "Have you found someone you want to marry?")
                        .Choice("Yes, what should I do?", AskPropose)
                        .Choice("Not interested.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case AskPropose:
                    await talk.Text("Marriage is a promise to spend your life together. " +
                            "Are you sure you have found the one?")
                        .Choice("I am sure.", StartPropose)
                        .Choice("Maybe not.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case StartPropose:
                    await client.SendAsync(MsgAction.ClientCommand(
                        client.ID, Marriage.ProposeCommand, character.X, character.Y));
                    await SayAsync(talk, "Click on the one you love to propose. They may accept or decline.");
                    return;
            }
        }

        public static async Task StarlitAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var character = client.Character;
            if (!Marriage.IsMarried(character))
            {
                await SayAsync(talk, "You are not married.");
                return;
            }

            switch (talk.Option)
            {
                case 0:
                    await talk.Text($"You are married to {Marriage.SpouseOf(character)}. " +
                            "If your paths have grown apart, I can end your marriage for a Meteor Tear.")
                        .Choice("I want a divorce.", AskDivorce)
                        .Choice("Just passing by.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case AskDivorce:
                    await talk.Text($"Divorcing {Marriage.SpouseOf(character)} will cost one Meteor Tear. " +
                            "Are you sure?")
                        .Choice("Yes, divorce us.", ConfirmDivorce)
                        .Choice("I changed my mind.", MsgTaskDialog.NoOption)
                        .Face(Face).ShowAsync();
                    return;

                case ConfirmDivorce:
                    string reason = await Marriage.DivorceAsync(client);
                    await SayAsync(talk, reason switch
                    {
                        null => "Your marriage has ended. You are free again.",
                        "no_item" => "You need a Meteor Tear to get divorced.",
                        _ => "You are not married.",
                    });
                    return;
            }
        }

        private static Task SayAsync(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(Face)
            .ShowAsync();
    }
}

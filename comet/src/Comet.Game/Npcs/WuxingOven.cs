namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.Packets;

    // The market's Wuxing Oven (320,224) composes +n items, and Ethereal (320,228) beside it
    // enchants gear with gems in it. Both only open the client's window for it.
    public static class WuxingOven
    {
        public const uint OvenID = 35016, EtherealID = 35015;

        private const byte Compose = 1, Enchant = 2, HowToCompose = 3, Back = 4;
        private const byte Face = 67;

        public static async Task OvenAsync(NpcTalk talk)
        {
            switch (talk.Option)
            {
                case Compose:
                    await talk.OpenWindowAsync(MsgAction.Window.Compose);
                    return;
                case Enchant:
                    await OpenEnchantAsync(talk);
                    return;
            }
            await talk.Text("The Wuxing Oven burns minor items into your gear to raise its bonus level, " +
                    "and gems into it for extra HP. What will you put in?")
                .Choice("Compose +n items.", Compose)
                .Choice("Enchant my gear.", Enchant)
                .Choice("Nothing for now.", MsgTaskDialog.NoOption)
                .Face(Face).ShowAsync();
        }

        public static async Task EtherealAsync(NpcTalk talk)
        {
            switch (talk.Option)
            {
                case Enchant:
                    await OpenEnchantAsync(talk);
                    return;
                case HowToCompose:
                    await talk.Text($"Put a +n item in the Wuxing Oven, then +1 to +{Composing.MaxMinorPlus} items " +
                            "of the same kind or +n Stones as minor items. Each minor adds composition points " +
                            "by its own bonus level, and the main item goes up a level each time they fill. " +
                            $"It never fails, up to +{Composing.MaxPlus}.")
                        .Choice("I see.", Back)
                        .Face(Face).ShowAsync();
                    return;
            }
            await talk.Text("I can use my Wuxing Oven to enchant your gear with a gem; enchanted gear raises " +
                    "your max HP. If you enchant it again, a higher result replaces the old one, and a " +
                    "lower one leaves it as it was. You may also compose +n items in the oven beside me.")
                .Choice("Enchant my gear.", Enchant)
                .Choice("How to compose +n items?", HowToCompose)
                .Choice("Just looking.", MsgTaskDialog.NoOption)
                .Face(Face).ShowAsync();
        }

        private static Task OpenEnchantAsync(NpcTalk talk)
        {
            var client = talk.Client;
            client.ActiveNpc = 0;
            return client.SendAsync(MsgAction.ClientCommand(client.ID, MsgAction.EnchantWindow,
                client.Character.X, client.Character.Y));
        }
    }
}

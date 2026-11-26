namespace Fetih.Game.Npcs
{
    using System.Globalization;
    using System.Threading.Tasks;
    using Fetih.Game.Items;
    using Fetih.Game.Magic;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // Riding costs 50,000 silver
    public static class MountTrainer
    {
        public const uint ID = 5600;
        public const uint Cost = 50000;
        private const byte Face = 15, Learn = 1;
        private static readonly string CostText = Cost.ToString("N0", CultureInfo.InvariantCulture);

        public static async Task TalkAsync(NpcTalk talk)
        {
            var client = talk.Client;
            if (talk.Option != Learn)
            {
                await talk.Text($"I train riders. For {CostText} silver I can teach you to ride a steed.")
                    .Choice("Teach me to ride.", Learn)
                    .Choice("Just passing by.", MsgTaskDialog.NoOption)
                    .Face(Face).ShowAsync();
                return;
            }

            if (client.Skills.TryGetSpell(Riding.MagicType, out _))
                await Say(talk, "You already know how to ride.");
            else if (!Kernel.Magics.Exists(Riding.MagicType))
                await Say(talk, "I can't teach riding right now.");
            else if (!await Progress.ChangeSilverAsync(client, -Cost, "learn_riding"))
                await Say(talk, $"Come back when you have {CostText} silver.");
            else
            {
                await Casting.LearnAsync(client, Riding.MagicType, 0, "mount_trainer");
                await Say(talk, "You've learned Riding. Wear a steed and use the skill to get on it.");
            }
        }

        private static Task Say(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(Face)
            .ShowAsync();
    }
}

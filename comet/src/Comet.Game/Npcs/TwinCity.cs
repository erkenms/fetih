namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    public static class TwinCity
    {
        public const uint GuardID = 30104;
        public const uint ConductressID = 10050;
        public const uint ConductressFare = 100;

        public static Task GuardAsync(NpcTalk talk) => talk
            .Text("I am in charge of the security of the city. No bad guys can escape here.")
            .Choice("Wow, it is wonderful.", MsgTaskDialog.NoOption)
            .Face(30)
            .ShowAsync();

        private static readonly Dictionary<byte, (uint Map, ushort X, ushort Y)> Destinations =
            new Dictionary<byte, (uint, ushort, ushort)>
            {
                [1] = (1002, 958, 555),     // Phoenix Castle
                [2] = (1002, 69, 473),      // Desert City
                [3] = (1002, 555, 957),     // Ape Mountain
                [4] = (1002, 232, 190),     // Bird Island
                [5] = (1002, 53, 399),      // Mine Cave
                [6] = (1036, 211, 196),     // Market
            };

        public static async Task ConductressAsync(NpcTalk talk)
        {
            if (talk.Option == 0)
            {
                await talk
                    .Text($"Where are you heading for? I can teleport you for a price of {ConductressFare} silver.")
                    .Choice("Phoenix Castle", 1)
                    .Choice("Desert City", 2)
                    .Choice("Ape Mountain", 3)
                    .Choice("Bird Island", 4)
                    .Choice("Mine Cave", 5)
                    .Choice("Market", 6)
                    .Choice("Just passing by.", MsgTaskDialog.NoOption)
                    .Face(1)
                    .ShowAsync();
                return;
            }

            if (!Destinations.TryGetValue(talk.Option, out var to))
                return;
            var client = talk.Client;
            if (client.Character.Silver < ConductressFare)
            {
                await talk
                    .Text($"Sorry, you do not have {ConductressFare} silver.")
                    .Choice("I see.", MsgTaskDialog.NoOption)
                    .Face(1)
                    .ShowAsync();
                return;
            }

            await Progress.ChangeSilverAsync(client, -ConductressFare, "conductress");
            await Movement.TeleportAsync(client, to.Map, to.X, to.Y);
        }
    }
}

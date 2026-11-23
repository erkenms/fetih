namespace Comet.Game.Training
{
    using System.Threading.Tasks;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // The training ground (1039): from level 20 the Boxers take players there for a fare, and
    // Boxer Huang sends them back. Runs on the player's own queue.
    public static class TrainingGround
    {
        public const uint MapID = 1039;
        public const ushort EntranceX = 217, EntranceY = 215;
        public const byte MinLevel = 20;
        public const uint Fare = 1000;

        // The Boxers of Twin City, Phoenix Castle, Desert City, Ape Mountain and Bird Island,
        // and Boxer Huang in the ground, who sends players back
        public static readonly uint[] BoxerIDs = { 101607, 101619, 101621, 101623, 101625 };
        public const uint ExitID = 600075;

        private const byte Enter = 1, Leave = 1;
        private const byte BoxerFace = 20, ExitFace = 1;

        public static async Task BoxerAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var character = client.Character;
            if (talk.Option != Enter)
            {
                await talk
                    .Text("If you are level 20 or above, you may level up at the training grounds. It does")
                    .Text("not consume any durability, life and mana. Do you want me to teleport you")
                    .Text($"there? It is free for my guild members, and {Fare} silver for others.")
                    .Choice("Please teleport me there.", Enter)
                    .Choice("Just passing by.", MsgTaskDialog.NoOption)
                    .Face(BoxerFace)
                    .ShowAsync();
                return;
            }

            string refusal =
                character.Level < MinLevel ? $"Sorry, you must be level {MinLevel} or above to train there." :
                character.Silver < Fare ? $"Sorry, you do not have {Fare:N0} silver." :
                null;
            if (refusal != null)
            {
                await talk.Text(refusal).Choice("I see.", MsgTaskDialog.NoOption).Face(BoxerFace).ShowAsync();
                return;
            }

            var from = new MapPoint(character.MapID, character.X, character.Y);
            if (!await Movement.TeleportAsync(client, MapID, EntranceX, EntranceY))
                return;
            client.TrainingReturn = from;
            await Progress.ChangeSilverAsync(client, -Fare, "training_ground");
        }

        // Back where the player came from, or, after logging in there, where the map revives
        public static async Task ExitAsync(NpcTalk talk)
        {
            var client = talk.Client;
            if (talk.Option != Leave)
            {
                await talk
                    .Text("Although nothing will be consumed here, you cannot level up as fast as")
                    .Text("killing the monsters. Shall I teleport you out?")
                    .Choice("Yeah, please.", Leave)
                    .Choice("No, thanks.", MsgTaskDialog.NoOption)
                    .Face(ExitFace)
                    .ShowAsync();
                return;
            }

            var to = client.TrainingReturn ?? Kernel.MapTypes.RebornPointOf(MapID)
                ?? new MapPoint(StartPosition.MapID, StartPosition.X, StartPosition.Y);
            if (await Movement.TeleportAsync(client, to.MapID, to.X, to.Y))
                client.TrainingReturn = null;
        }
    }
}

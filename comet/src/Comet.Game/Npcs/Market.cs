namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.World;

    // The market's Mark.Controller
    public static class Market
    {
        public const uint MapID = 1036;
        public const uint MarkControllerID = 45;

        private const byte Leave = 1;
        private const byte MarkControllerFace = 156;

        // Back to where the player came in from; Twin City when that isn't known, as after
        // logging in at the market
        public static async Task MarkControllerAsync(NpcTalk talk)
        {
            if (talk.Option != Leave)
            {
                await talk.Text("Do you want to leave the market? I can teleport you for free.")
                    .Choice("Yeah. Thanks.", Leave)
                    .Choice("No, I shall stay here.", MsgTaskDialog.NoOption)
                    .Face(MarkControllerFace).ShowAsync();
                return;
            }
            var client = talk.Client;
            var (map, x, y) = client.MarketReturn ?? (StartPosition.MapID, StartPosition.X, StartPosition.Y);
            if (!await Movement.TeleportAsync(client, map, x, y))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
        }
    }
}

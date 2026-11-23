namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // The market's Mark.Controller and CPAdmin
    public static class Market
    {
        public const uint MapID = 1036;
        public const uint MarkControllerID = 45, CPAdminID = 2071;

        // CPAdmin pays these for a DragonBall and a DragonBall Scroll
        public const uint DragonBallCPs = 215, DragonBallScrollCPs = 2150;

        private const byte Leave = 1, DragonBall = 1, DragonBallScroll = 2;
        private const byte MarkControllerFace = 156, CPAdminFace = 7;

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

        public static async Task CPAdminAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var (item, name, cps) = talk.Option switch
            {
                DragonBall => (Combat.Loot.DragonBall, "DragonBall", DragonBallCPs),
                DragonBallScroll => (Using.DragonBallScroll, "DragonBall Scroll", DragonBallScrollCPs),
                _ => (0u, null, 0u),
            };
            if (name == null)
            {
                await talk.Text($"I can exchange a DragonBall for {DragonBallCPs} CPs, or a DragonBall Scroll " +
                        $"for {DragonBallScrollCPs} CPs. Which will you claim?")
                    .Choice("Exchange a DragonBall.", DragonBall)
                    .Choice("Exchange a DragonBall Scroll.", DragonBallScroll)
                    .Choice("I have nothing.", MsgTaskDialog.NoOption)
                    .Face(CPAdminFace).ShowAsync();
                return;
            }

            if (!await ItemActions.TakeAsync(client, item, 1, "cp_exchange"))
            {
                await Say(talk, $"You do not have a {name}!", "Sorry.", CPAdminFace);
                return;
            }
            await Progress.ChangeConquerPointsAsync(client, cps, "cp_exchange");
            await Say(talk, "Please enjoy your new CPs!", "I shall.", CPAdminFace);
        }

        private static Task Say(NpcTalk talk, string text, string reply, byte face) => talk
            .Text(text)
            .Choice(reply, MsgTaskDialog.NoOption)
            .Face(face)
            .ShowAsync();
    }
}

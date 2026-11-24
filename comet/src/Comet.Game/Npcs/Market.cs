namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // The market's Mark.Controller, CPAdmin and SurgeonMiracle
    public static class Market
    {
        public const uint MapID = 1036;
        public const uint MarkControllerID = 45, CPAdminID = 2071, SurgeonMiracleID = 3381, NameRegistrarID = 7935;

        // CPAdmin pays these for a DragonBall and a DragonBall Scroll
        public const uint DragonBallCPs = 215, DragonBallScrollCPs = 2150;

        private const byte Leave = 1, DragonBall = 1, DragonBallScroll = 2, ChangeSize = 1, ConfirmSize = 2, OpenChangeName = 1;
        private const byte MarkControllerFace = 156, CPAdminFace = 7, SurgeonFace = 67, NameRegistrarFace = 31;

        public static Task NameRegistrarAsync(NpcTalk talk)
        {
            if (talk.Option == OpenChangeName)
                return talk.OpenWindowAsync(MsgAction.Window.ChangeName);
            return talk.Text($"Good day! If you want to turn a new page in your life, you can pay me {NameChange.CostCPs} CPs to get a new name. ")
                .Text("But choose carefully, as every player has only one chance in a lifetime! Your new name will take effect after you relog.")
                .Choice("Okay, here's my CPs!", OpenChangeName)
                .Choice("I'll think about it.", MsgTaskDialog.NoOption)
                .Face(NameRegistrarFace).ShowAsync();
        }

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

        // A DragonBall swaps the agile and muscular bodies of the player's gender
        public static async Task SurgeonMiracleAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var character = client.Character;
            if (talk.Option == ChangeSize)
            {
                await talk.Text("It will cost you a DragonBall. Are you sure?")
                    .Choice("Here is a DragonBall.", ConfirmSize)
                    .Choice("Let me think it over.", MsgTaskDialog.NoOption)
                    .Face(SurgeonFace).ShowAsync();
                return;
            }
            if (talk.Option != ConfirmSize)
            {
                await talk.Text("Do you want to have your size changed so that you can become much more adorable? " +
                        "Now here is a precious chance for you.")
                    .Choice("I want to change my size.", ChangeSize)
                    .Choice("I don't want to change.", MsgTaskDialog.NoOption)
                    .Face(SurgeonFace).ShowAsync();
                return;
            }

            uint before = character.Mesh;
            uint body = OtherBody(before);
            if (body == 0)
                return;
            if (!await ItemActions.TakeAsync(client, Combat.Loot.DragonBall, 1, "body_size"))
            {
                await Say(talk, "You do not have a DragonBall.", "I see.", SurgeonFace);
                return;
            }
            character.Mesh = body;
            var look = new MsgUserAttrib(client.ID, AttributeType.Mesh, client.Life.Mesh(character));
            await client.SendAsync(look);
            await Movement.SendToViewAsync(client, look);
            LogEvents.For(typeof(Market), LogEvents.CharacterBody).Information(
                "{Name} changed body {Before} to {Body}", character.Name, before, body);
            await Say(talk, "It's done. How do you like your new look?", "Thank you.", SurgeonFace);
        }

        public static uint OtherBody(uint body) => (BodyType)body switch
        {
            BodyType.AgileMale => (uint)BodyType.MuscularMale,
            BodyType.MuscularMale => (uint)BodyType.AgileMale,
            BodyType.AgileFemale => (uint)BodyType.MuscularFemale,
            BodyType.MuscularFemale => (uint)BodyType.AgileFemale,
            _ => 0,
        };

        private static Task Say(NpcTalk talk, string text, string reply, byte face) => talk
            .Text(text)
            .Choice(reply, MsgTaskDialog.NoOption)
            .Face(face)
            .ShowAsync();
    }
}

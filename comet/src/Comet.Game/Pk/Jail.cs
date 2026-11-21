namespace Comet.Game.Pk
{
    using System.Globalization;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Npcs;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // The PK jail, map 6000 (PKerJail in GameMapEx.ini). Black names killed land here. The
    // warden lets red names and lighter out for free; black names pay bail.
    public static class Jail
    {
        public const uint MapID = 6000;

        // The jail's revive point
        public const ushort X = 29, Y = 72;

        public const uint WardenID = 42;
        public const uint Bail = 500_000;
        private const byte WardenFace = 54;
        private static readonly string BailText = Bail.ToString("N0", CultureInfo.InvariantCulture);

        public static async Task<bool> SendAsync(Client client, string reason)
        {
            if (client.Character == null || !await Movement.TeleportAsync(client, MapID, X, Y))
                return false;
            await Chat.Talk.NoticeAsync(client, "You have been sent to jail. Talk to the warden to leave.");
            LogEvents.For(typeof(Jail), LogEvents.PkJailed).Information(
                "{Name} was sent to jail with {Points} PK points ({Reason})",
                client.Character.Name, client.Character.KillPoints, reason);
            return true;
        }

        public static async Task WardenAsync(NpcTalk talk)
        {
            var client = talk.Client;
            bool black = client.Character.KillPoints >= Pking.BlackNamePoints;
            if (talk.Option == 0)
            {
                if (black)
                    talk.Text($"Black names, with {Pking.BlackNamePoints} PK points or more, must pay " +
                            $"{BailText} silver as recognizance to leave. Red names leave for free.")
                        .Choice($"I'll pay {BailText} silver.", 1)
                        .Choice("I would rather stay here.", MsgTaskDialog.NoOption);
                else
                    talk.Text("You needn't stay here any more. Let me help you out.")
                        .Choice("Ok, thanks.", 1)
                        .Choice("Leave me alone.", MsgTaskDialog.NoOption);
                await talk.Face(WardenFace).ShowAsync();
                return;
            }

            if (talk.Option != 1)
                return;
            if (black && !await Progress.ChangeSilverAsync(client, -Bail, "bail"))
            {
                await talk.Text($"You don't have {BailText} silver.")
                    .Choice("I see.", MsgTaskDialog.NoOption)
                    .Face(WardenFace)
                    .ShowAsync();
                return;
            }

            if (!await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y))
            {
                if (black)
                    await Progress.ChangeSilverAsync(client, Bail, "bail_refund");
                return;
            }
            LogEvents.For(typeof(Jail), LogEvents.PkReleased).Information(
                "{Name} left jail with {Points} PK points ({Reason})",
                client.Character.Name, client.Character.KillPoints, black ? "bail" : "free");
        }
    }
}

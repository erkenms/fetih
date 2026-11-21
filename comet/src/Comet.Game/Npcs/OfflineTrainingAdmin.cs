namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.Training;
    using Comet.Game.World;

    // The offline training ground's admin (client npc.ini NpcType301): shows the training
    // result to claim (StrRes 11003), or takes others back to Twin City
    public static class OfflineTrainingAdmin
    {
        public const uint ID = 6010;
        private const byte Leave = 1;

        public static async Task TalkAsync(NpcTalk talk)
        {
            var client = talk.Client;
            if (OfflineTraining.IsTraining(client.Character))
            {
                await OfflineTraining.ShowResultAsync(client);
                return;
            }
            if (talk.Option == Leave)
            {
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
                return;
            }
            await talk.Text("You aren't training here. Shall I send you back to Twin City?")
                .Choice("Yes, please.", Leave)
                .Choice("Not yet.", MsgTaskDialog.NoOption)
                .ShowAsync();
        }
    }
}

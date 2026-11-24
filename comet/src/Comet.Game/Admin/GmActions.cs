namespace Comet.Game.Admin
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Sockets;

    // What the GM chat commands and the admin RPC both do to a player
    public static class GmActions
    {
        // Ten years; much more would overflow DateTime
        public const int MaxMuteMinutes = 10 * 365 * 24 * 60;

        // False if there's no such account. Closes the account's connection, if any.
        public static async Task<bool> BanAsync(uint accountID, string reason)
        {
            if (!await AccountsRepository.BanAsync(accountID))
                return false;
            if (Kernel.Clients.TryGet(accountID, out var client))
            {
                if (client.Character != null)
                    await Talk.NoticeAsync(client, string.IsNullOrWhiteSpace(reason)
                        ? "You have been banned." : "You have been banned: " + reason.Trim());
                client.Disconnect(DisconnectReasons.Banned);
            }
            return true;
        }

        public static async Task MuteAsync(Client target, int minutes)
        {
            target.Character.MutedUntil = DateTime.UtcNow.AddMinutes(minutes);
            await target.SaveAsync("mute");
            await Talk.NoticeAsync(target, $"You have been muted for {minutes} minutes.");
        }

        // False if the player wasn't muted
        public static async Task<bool> UnmuteAsync(Client target)
        {
            if (!Talk.IsMuted(target.Character, DateTime.UtcNow))
                return false;
            target.Character.MutedUntil = null;
            await target.SaveAsync("mute");
            await Talk.NoticeAsync(target, "You can chat again.");
            return true;
        }

        public static async Task BroadcastAsync(MsgTalk.TalkChannel channel, string text)
        {
            var message = new MsgTalk(0, channel, text);
            foreach (var player in Kernel.Players.Values)
                await player.SendAsync(message);
        }
    }
}

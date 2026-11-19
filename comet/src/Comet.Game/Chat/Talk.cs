namespace Comet.Game.Chat
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgTalk;

    public static class Talk
    {
        public const int WorldMinimumLevel = 70;
        public static readonly TimeSpan WorldCooldown = TimeSpan.FromSeconds(15);

        public static async Task ReceiveAsync(Client client, MsgTalk msg)
        {
            var character = client.Character;
            if (character == null || client.Map == null || string.IsNullOrWhiteSpace(msg.Message))
                return;

            if (Commands.IsCommand(client, msg.Message))
            {
                await Commands.RunAsync(client, msg.Message);
                return;
            }

            // The client fills in the sender itself; never trust it
            msg.SenderName = character.Name;
            msg.SenderMesh = Mesh(character);

            switch (msg.Channel)
            {
                case TalkChannel.Talk:
                    Log(client, msg, null);
                    await Movement.SendToViewAsync(client, msg);
                    break;

                case TalkChannel.Whisper:
                    if (!Kernel.Players.TryGet(msg.RecipientName ?? string.Empty, out var target) ||
                        target.Character == null)
                    {
                        await RejectAsync(client, msg, "offline", $"{msg.RecipientName} is not online.");
                        return;
                    }
                    msg.RecipientName = target.Character.Name;
                    msg.RecipientMesh = Mesh(target.Character);
                    Log(client, msg, target.Character.Name);
                    await target.SendAsync(msg);
                    break;

                case TalkChannel.World:
                    // The client stops this itself and tells the player
                    if (character.Level < WorldMinimumLevel)
                    {
                        await RejectAsync(client, msg, "level", null);
                        return;
                    }
                    var wait = client.LastWorldTalk + WorldCooldown - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await RejectAsync(client, msg, "cooldown",
                            $"You can speak in the world channel again in {Math.Ceiling(wait.TotalSeconds)} seconds.");
                        return;
                    }
                    client.LastWorldTalk = DateTime.UtcNow;
                    Log(client, msg, null);
                    foreach (var other in Kernel.Players.Values)
                        if (other != client)
                            await other.SendAsync(msg);
                    break;

                default:
                    LogEvents.For(typeof(Talk), LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} channel {Channel} ({Length} bytes)",
                        msg.Type, msg.Channel, msg.Length);
                    await NoticeAsync(client, $"Missing packet {msg.Type}, Channel {msg.Channel}");
                    break;
            }
        }

        // Server replies to one player: command output, refusals
        public static Task NoticeAsync(Client client, string text) =>
            client.SendAsync(new MsgTalk(client.ID, TalkChannel.Service, text));

        private static uint Mesh(Character character) =>
            (uint)(character.Mesh + character.Avatar * 10000);

        private static void Log(Client client, MsgTalk msg, string recipient) =>
            LogEvents.For(typeof(Talk), LogEvents.ChatMessage).Information(
                "{Name} said on {Channel} to {Recipient}: {Text}",
                client.Character.Name, msg.Channel.ToString(), recipient, msg.Message);

        private static Task RejectAsync(Client client, MsgTalk msg, string reason, string notice)
        {
            LogEvents.For(typeof(Talk), LogEvents.ChatRejected).Information(
                "{Name} can't speak on {Channel} to {Recipient}: {Reason}",
                client.Character.Name, msg.Channel.ToString(), msg.RecipientName, reason);
            return notice == null ? Task.CompletedTask : NoticeAsync(client, notice);
        }
    }
}

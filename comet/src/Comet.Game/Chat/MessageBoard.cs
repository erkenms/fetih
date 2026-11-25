namespace Comet.Game.Chat
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgMessageBoard;
    using static Comet.Game.Packets.MsgTalk;

    public sealed class MessageBoardStore
    {
        public const int PageSize = 8;
        public const int MaxMessageLength = 255;
        public const int PreviewLength = 44;

        public sealed record Post(
            TalkChannel Channel,
            uint CharacterID,
            uint SyndicateID,
            string AuthorName,
            string Message,
            DateTime PostedAt);

        private readonly object Sync = new object();
        private readonly Dictionary<(TalkChannel Channel, uint CharacterID), Post> Posts =
            new Dictionary<(TalkChannel, uint), Post>();

        public int Count
        {
            get { lock (this.Sync) return this.Posts.Count; }
        }

        public void Load(IEnumerable<Post> posts)
        {
            lock (this.Sync)
            {
                this.Posts.Clear();
                foreach (var post in posts)
                    this.Posts[(post.Channel, post.CharacterID)] = post;
            }
        }

        public Post Upsert(TalkChannel channel, uint characterID, uint syndicateID,
            string authorName, string message, DateTime postedAt)
        {
            var post = new Post(channel, characterID, syndicateID, authorName, message, postedAt);
            lock (this.Sync)
                this.Posts[(channel, characterID)] = post;
            return post;
        }

        public List<Post> List(TalkChannel channel, uint syndicateID, int index)
        {
            if (index < 0)
                index = 0;
            lock (this.Sync)
            {
                return this.Posts.Values
                    .Where(p => p.Channel == channel &&
                        (channel != TalkChannel.GuildBoard || (syndicateID != 0 && p.SyndicateID == syndicateID)))
                    .OrderByDescending(p => p.PostedAt)
                    .ThenByDescending(p => p.CharacterID)
                    .Skip(index)
                    .Take(PageSize)
                    .ToList();
            }
        }

        public Post Find(TalkChannel channel, uint syndicateID, string authorName)
        {
            if (string.IsNullOrEmpty(authorName))
                return null;
            lock (this.Sync)
            {
                return this.Posts.Values.FirstOrDefault(p =>
                    p.Channel == channel &&
                    (channel != TalkChannel.GuildBoard || (syndicateID != 0 && p.SyndicateID == syndicateID)) &&
                    string.Equals(p.AuthorName, authorName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool DeleteOwn(TalkChannel channel, uint characterID, string authorName)
        {
            lock (this.Sync)
            {
                if (!this.Posts.TryGetValue((channel, characterID), out var post))
                    return false;
                if (!string.IsNullOrEmpty(authorName) &&
                    !string.Equals(post.AuthorName, authorName, StringComparison.OrdinalIgnoreCase))
                    return false;
                return this.Posts.Remove((channel, characterID));
            }
        }

        public static string Sanitize(string message)
        {
            string clean = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length > MaxMessageLength ? clean[..MaxMessageLength] : clean;
        }

        public static string Preview(string message) =>
            message.Length <= PreviewLength ? message : message[..PreviewLength];

        // The client slices 12 digits into YYYY-MM-DD-HH-mm when rendering list rows
        public static string FormatTime(DateTime utc) =>
            Clock.Local(utc).ToString("yyyyMMddHHmm");
    }

    // Message board across Trade, Friend, Team, Guild and Others channels. Each character has
    // at most one active post per channel; Guild posts are visible only within that guild.
    public static class MessageBoard
    {
        private static readonly MessageBoardStore Store = new MessageBoardStore();

        public static int Count => Store.Count;

        public static bool IsBoardChannel(TalkChannel channel) =>
            channel >= TalkChannel.TradeBoard && channel <= TalkChannel.OthersBoard;

        public static async Task LoadAsync()
        {
            var rows = await MessageBoardRepository.LoadAsync();
            Store.Load(rows.Select(r => new MessageBoardStore.Post(
                (TalkChannel)r.Channel,
                r.CharacterID,
                r.SyndicateID,
                r.AuthorName,
                r.Message,
                DateTime.SpecifyKind(r.PostedAt, DateTimeKind.Utc))));
        }

        public static async Task PostAsync(Client client, MsgTalk msg)
        {
            var character = client.Character;
            if (character == null || !IsBoardChannel(msg.Channel))
                return;

            uint syndicateID = 0;
            if (msg.Channel == TalkChannel.GuildBoard)
            {
                var membership = Guilds.Of(client.ID);
                if (membership == null)
                {
                    await RejectAsync(client, msg.Channel, "no_guild", "You are not in a guild.");
                    return;
                }
                syndicateID = membership.Guild.ID;
            }

            string text = MessageBoardStore.Sanitize(msg.Message);
            if (string.IsNullOrEmpty(text))
            {
                await RejectAsync(client, msg.Channel, "empty", null);
                return;
            }

            DateTime now = DateTime.UtcNow;
            Store.Upsert(msg.Channel, character.CharacterID, syndicateID, character.Name, text, now);
            await MessageBoardRepository.SaveAsync(
                (ushort)msg.Channel, character.CharacterID, syndicateID, character.Name, text, now);
            LogEvents.For(typeof(MessageBoard), LogEvents.MessageBoardPosted).Information(
                "{Name} posted on {Channel}: {Text}", character.Name, msg.Channel.ToString(), text);
        }

        public static Task HandleAsync(Client client, MsgMessageBoard msg)
        {
            if (client.Character == null || !IsBoardChannel(msg.Channel))
                return Task.CompletedTask;

            return msg.Action switch
            {
                BoardAction.GetList => SendListAsync(client, msg.Channel, msg.Index),
                BoardAction.GetWords => SendWordsAsync(client, msg.Channel, FirstString(msg)),
                BoardAction.Delete => DeleteAsync(client, msg.Channel, FirstString(msg)),
                _ => LogUnhandledAsync(msg)
            };
        }

        private static Task SendListAsync(Client client, TalkChannel channel, ushort index)
        {
            uint syndicateID = channel == TalkChannel.GuildBoard ? Guilds.Of(client.ID)?.Guild.ID ?? 0 : 0;
            var posts = Store.List(channel, syndicateID, index);
            var strings = new List<string>(posts.Count * 3);
            foreach (var post in posts)
            {
                strings.Add(post.AuthorName);
                strings.Add(MessageBoardStore.Preview(post.Message));
                strings.Add(MessageBoardStore.FormatTime(post.PostedAt));
            }
            return client.SendAsync(new MsgMessageBoard(index, channel, BoardAction.List, strings));
        }

        private static Task SendWordsAsync(Client client, TalkChannel channel, string authorName)
        {
            uint syndicateID = channel == TalkChannel.GuildBoard ? Guilds.Of(client.ID)?.Guild.ID ?? 0 : 0;
            var post = Store.Find(channel, syndicateID, authorName);
            if (post == null)
                return Task.CompletedTask;
            return client.SendAsync(new MsgTalk(
                client.ID, channel, Color.White, client.Character.Name, post.AuthorName, post.Message));
        }

        // A moderator's delete of anyone's post
        public static async Task<bool> RemoveAsync(TalkChannel channel, uint characterID)
        {
            if (!IsBoardChannel(channel) || !Store.DeleteOwn(channel, characterID, null))
                return false;
            await MessageBoardRepository.DeleteAsync((ushort)channel, characterID);
            return true;
        }

        private static async Task DeleteAsync(Client client, TalkChannel channel, string authorName)
        {
            if (!Store.DeleteOwn(channel, client.ID, authorName))
            {
                await RejectAsync(client, channel, "not_owner", null);
                return;
            }
            await MessageBoardRepository.DeleteAsync((ushort)channel, client.ID);
            LogEvents.For(typeof(MessageBoard), LogEvents.MessageBoardDeleted).Information(
                "{Name} deleted post on {Channel}", client.Character.Name, channel.ToString());
        }

        private static string FirstString(MsgMessageBoard msg) =>
            msg.Strings != null && msg.Strings.Count > 0 ? msg.Strings[0] : string.Empty;

        private static Task RejectAsync(Client client, TalkChannel channel, string reason, string notice)
        {
            LogEvents.For(typeof(MessageBoard), LogEvents.MessageBoardRejected).Information(
                "{Name} message board action on {Channel} rejected ({Reason})",
                client.Character.Name, channel.ToString(), reason);
            return notice == null ? Task.CompletedTask : Talk.NoticeAsync(client, notice);
        }

        private static Task LogUnhandledAsync(MsgMessageBoard msg)
        {
            LogEvents.For(typeof(MessageBoard), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                msg.Type, msg.Action, msg.Length);
            return Task.CompletedTask;
        }
    }
}

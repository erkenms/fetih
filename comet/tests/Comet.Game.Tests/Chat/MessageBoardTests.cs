namespace Comet.Game.Tests.Chat
{
    using System;
    using Comet.Game.Chat;
    using Comet.Game.Packets;
    using Xunit;
    using static Comet.Game.Packets.MsgMessageBoard;
    using static Comet.Game.Packets.MsgTalk;

    public class MessageBoardTests
    {
        [Fact]
        public void MsgMessageBoardDecodesRealClientListRequest()
        {
            // Captured from the 5619 client opening the Trade message board
            var raw = Convert.FromHexString("0A005704000099080200");
            var msg = new MsgMessageBoard();
            msg.Decode(raw);

            Assert.Equal((ushort)0, msg.Index);
            Assert.Equal(TalkChannel.TradeBoard, msg.Channel);
            Assert.Equal(BoardAction.GetList, msg.Action);
            Assert.Empty(msg.Strings);
        }

        [Fact]
        public void MsgMessageBoardRoundTripsTriples()
        {
            var list = new MsgMessageBoard(8, TalkChannel.FriendBoard, BoardAction.List,
                new() { "Alice", "Looking for friends", "202611241530" });
            var decoded = new MsgMessageBoard();
            decoded.Decode(list.Encode());

            Assert.Equal((ushort)8, decoded.Index);
            Assert.Equal(TalkChannel.FriendBoard, decoded.Channel);
            Assert.Equal(BoardAction.List, decoded.Action);
            Assert.Equal(new[] { "Alice", "Looking for friends", "202611241530" }, decoded.Strings);
        }

        [Fact]
        public void StoreReplacesSameAuthorPerChannelAndPaginatesByEight()
        {
            var store = new MessageBoardStore();
            var t0 = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
            for (uint i = 1; i <= 10; i++)
                store.Upsert(TalkChannel.TradeBoard, i, 0, $"User{i}", $"Offer {i}", t0.AddMinutes(i));

            // Submitting another post on the same channel replaces the previous one
            store.Upsert(TalkChannel.TradeBoard, 1, 0, "User1", "Updated offer", t0.AddMinutes(20));
            Assert.Equal(10, store.Count);

            var page0 = store.List(TalkChannel.TradeBoard, 0, 0);
            Assert.Equal(8, page0.Count);
            Assert.Equal("User1", page0[0].AuthorName);
            Assert.Equal("Updated offer", page0[0].Message);
            Assert.Equal("User10", page0[1].AuthorName);

            var page1 = store.List(TalkChannel.TradeBoard, 0, 8);
            Assert.Equal(2, page1.Count);
            Assert.Equal("User3", page1[0].AuthorName);
            Assert.Equal("User2", page1[1].AuthorName);
        }

        [Fact]
        public void GuildBoardIsScopedByGuildAndDeleteChecksOwner()
        {
            var store = new MessageBoardStore();
            var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            store.Upsert(TalkChannel.GuildBoard, 1, 50, "Alice", "Guild 50 war", now);
            store.Upsert(TalkChannel.GuildBoard, 2, 60, "Bob", "Guild 60 war", now.AddMinutes(1));

            Assert.Single(store.List(TalkChannel.GuildBoard, 50, 0));
            Assert.Empty(store.List(TalkChannel.GuildBoard, 0, 0));
            Assert.Null(store.Find(TalkChannel.GuildBoard, 50, "Bob"));
            Assert.NotNull(store.Find(TalkChannel.GuildBoard, 50, "Alice"));

            // Bob cannot delete Alice's post; Alice can delete her own
            Assert.False(store.DeleteOwn(TalkChannel.GuildBoard, 2, "Alice"));
            Assert.True(store.DeleteOwn(TalkChannel.GuildBoard, 1, "Alice"));
            Assert.Empty(store.List(TalkChannel.GuildBoard, 50, 0));
        }

        [Fact]
        public void TimestampFormatsAsTwelveDigitsAndPreviewTruncates()
        {
            var prevZone = Clock.Zone;
            try
            {
                Clock.Zone = TimeZoneInfo.Utc;
                var utc = new DateTime(2026, 11, 24, 9, 5, 0, DateTimeKind.Utc);
                Assert.Equal("202611240905", MessageBoardStore.FormatTime(utc));
                Assert.Equal(MessageBoardStore.PreviewLength,
                    MessageBoardStore.Preview(new string('a', 100)).Length);
            }
            finally
            {
                Clock.Zone = prevZone;
            }
        }
    }
}

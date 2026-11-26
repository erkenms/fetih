namespace Fetih.Game.Tests.Chat
{
    using System;
    using System.Linq;
    using Fetih.Game.Chat;
    using Fetih.Game.Packets;
    using Xunit;
    using static Fetih.Game.Packets.MsgPigeon;

    public class BroadcastTests
    {
        [Fact]
        public void MsgPigeonDecodesRealClientQueryPacket()
        {
            // Captured from the 5619 client opening the Broadcast check window
            var raw = Convert.FromHexString("0F0002080100000000000000000000");
            var msg = new MsgPigeon();
            msg.Decode(raw);

            Assert.Equal(PigeonAction.QueryAll, msg.Action);
            Assert.Equal((byte)0, msg.Currency);
            Assert.Equal(0u, msg.Param);
            Assert.Empty(msg.Strings);
        }

        [Fact]
        public void MsgPigeonRoundTripsWithAndWithoutStrings()
        {
            var reply = new MsgPigeon(PigeonAction.Send, 4);
            var encoded = reply.Encode();
            Assert.Equal(15, encoded.Length);

            var decoded = new MsgPigeon();
            decoded.Decode(encoded);
            Assert.Equal(PigeonAction.Send, decoded.Action);
            Assert.Equal(4u, decoded.Param);
            Assert.Empty(decoded.Strings);

            var withText = new MsgPigeon(PigeonAction.Send, 0) { Currency = 1 };
            withText.Strings.Add("Selling DB 500k");
            var decodedText = new MsgPigeon();
            decodedText.Decode(withText.Encode());
            Assert.Equal((byte)1, decodedText.Currency);
            Assert.Equal(new[] { "Selling DB 500k" }, decodedText.Strings);
        }

        [Fact]
        public void MsgPigeonQueryEncodes112ByteEntriesAndCapsAtEightPerPacket()
        {
            var query = new MsgPigeonQuery(page: 2, append: true, null);
            for (uint i = 1; i <= 10; i++)
                query.Entries.Add(new MsgPigeonQuery.Entry(i, i - 1, 1000 + i, $"Player{i}", i * 5, $"Message {i}"));

            var bytes = query.Encode();
            Assert.Equal(12 + 8 * 112, bytes.Length);

            var parsed = new MsgPigeonQuery();
            parsed.Decode(bytes);
            Assert.Equal(2u, parsed.Page);
            Assert.Equal((ushort)1, parsed.Append);
            Assert.Equal(8, parsed.Entries.Count);
            Assert.Equal((1u, 0u, 1001u, "Player1", 5u, "Message 1"),
                (parsed.Entries[0].ID, parsed.Entries[0].Position, parsed.Entries[0].CharacterID,
                 parsed.Entries[0].SenderName, parsed.Entries[0].Addition, parsed.Entries[0].Message));
            Assert.Equal((8u, 7u, 1008u, "Player8", 40u, "Message 8"),
                (parsed.Entries[7].ID, parsed.Entries[7].Position, parsed.Entries[7].CharacterID,
                 parsed.Entries[7].SenderName, parsed.Entries[7].Addition, parsed.Entries[7].Message));
        }

        [Fact]
        public void QueuePromotesByFiveAndToFirstAndTracksAddition()
        {
            var queue = new BroadcastQueue();
            for (uint i = 1; i <= 8; i++)
            {
                var (entry, rank) = queue.Enqueue(i == 7 ? 200u : 100u + i, $"P{i}", $"msg {i}");
                Assert.Equal((int)i, rank);
                Assert.Equal(i, entry.ID);
            }

            // Entry 7 is at index 6 (rank 7); Urgent +5 moves it to index 1 (rank 2)
            Assert.True(queue.TryPromote(7, 200, Broadcasts.UrgentAdvance, Broadcasts.UrgentCost, out int rankAfterFive));
            Assert.Equal(2, rankAfterFive);

            // Another character cannot promote entry 7
            Assert.False(queue.TryPromote(7, 999, int.MaxValue, Broadcasts.UrgentTopCost, out _));

            // Urgent top moves entry 7 from index 1 to index 0 (rank 1) and accumulates Addition
            Assert.True(queue.TryPromote(7, 200, int.MaxValue, Broadcasts.UrgentTopCost, out int rankTop));
            Assert.Equal(1, rankTop);

            // Already 1st cannot be promoted again
            Assert.False(queue.TryPromote(7, 200, Broadcasts.UrgentAdvance, Broadcasts.UrgentCost, out _));

            var (page, items) = queue.Page(0, onlyCharacterID: 200);
            Assert.Equal(0, page);
            var single = Assert.Single(items);
            Assert.Equal(0, single.Position);
            Assert.Equal(20u, single.Entry.Addition);
        }

        [Fact]
        public void QueuePagesClampAndDequeueRespectsInterval()
        {
            var queue = new BroadcastQueue();
            for (int i = 1; i <= 12; i++)
                queue.Enqueue(10, "Alice", $"m{i}");

            var (p0, items0) = queue.Page(0, null);
            Assert.Equal(0, p0);
            Assert.Equal(10, items0.Count);
            Assert.Equal(0, items0[0].Position);
            Assert.Equal(9, items0[9].Position);

            var (pClamped, items1) = queue.Page(99, null);
            Assert.Equal(1, pClamped);
            Assert.Equal(2, items1.Count);
            Assert.Equal(10, items1[0].Position);

            var t0 = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            var interval = TimeSpan.FromSeconds(60);
            Assert.True(queue.TryDequeue(t0, interval, out var first));
            Assert.Equal("m1", first.Message);
            Assert.False(queue.TryDequeue(t0.AddSeconds(59), interval, out _));
            Assert.True(queue.TryDequeue(t0.AddSeconds(60), interval, out var second));
            Assert.Equal("m2", second.Message);
        }

        [Fact]
        public void LoadedQueueKeepsSavedOrderAndContinuesIds()
        {
            var queue = new BroadcastQueue();
            for (uint i = 1; i <= 4; i++)
                queue.Enqueue(100 + i, $"P{i}", $"msg {i}");
            Assert.True(queue.TryPromote(4, 104, int.MaxValue, Broadcasts.UrgentTopCost, out _));

            var restarted = new BroadcastQueue();
            restarted.Load(queue.Snapshot());

            Assert.Equal(new uint[] { 4, 1, 2, 3 }, restarted.Snapshot().Select(e => e.ID).ToArray());
            Assert.Equal(15u, restarted.Snapshot()[0].Addition);
            var (next, rank) = restarted.Enqueue(7, "New", "after restart");
            Assert.Equal(5u, next.ID);
            Assert.Equal(5, rank);
        }
    }
}

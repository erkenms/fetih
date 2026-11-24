namespace Comet.Integration.Tests
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class FlowerTests : IntegrationTest
    {
        private const uint RedRoseRankType = 30000002;
        private const uint LilyRankType = 30000102;

        public FlowerTests(ServerFixture servers) : base(servers) { }

        private static byte[] BuildFlowerSend(uint targetId, uint itemId = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgFlower, 60);
            BitConverter.TryWriteBytes(packet.AsSpan(4), 0u);
            BitConverter.TryWriteBytes(packet.AsSpan(8), targetId);
            BitConverter.TryWriteBytes(packet.AsSpan(12), itemId);
            return packet;
        }

        private static byte[] BuildRankQuery(byte mode, uint rankType = 0, byte page = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgRank, 80);
            packet[4] = mode;
            BitConverter.TryWriteBytes(packet.AsSpan(8), rankType);
            packet[14] = page;
            return packet;
        }

        private static string ReadFixedAscii(byte[] packet, int offset, int length)
        {
            int end = Array.IndexOf(packet, (byte)0, offset, length);
            int count = end < 0 ? length : end - offset;
            return Encoding.ASCII.GetString(packet, offset, count);
        }

        [Fact]
        public async Task SendingFreeAndItemFlowersUpdatesCountsRankingsAndMsgPlayer()
        {
            var (sender, senderEmail) = await this.PlayingAtAsync(1002, 430, 380, GameMaster, mesh: 1003);
            var (receiver, receiverEmail) = await this.PlayingAtAsync(1002, 431, 380, mesh: 2001);
            var senderName = sender.User.Name;
            var receiverName = receiver.User.Name;

            // Both logged in: sender sees receiver spawn
            await using (sender)
            await using (receiver)
            {
                Assert.NotNull(await sender.Game.WaitForSpawnAsync(receiver.User.Id));

                // 1. Send daily free flower (VIP 0 -> 1 Red Rose, Flags = 1)
                await sender.Game.SendAsync(BuildFlowerSend(receiver.User.Id, 0));
                var sentToSender = await sender.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFlower && BitConverter.ToUInt16(p, 4) == 0);
                var sentToReceiver = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFlower && BitConverter.ToUInt16(p, 4) == 0);
                Assert.NotNull(sentToSender);
                Assert.NotNull(sentToReceiver);
                Assert.Equal(senderName, ReadFixedAscii(sentToReceiver, 16, 16));
                Assert.Equal(receiverName, ReadFixedAscii(sentToReceiver, 32, 16));
                Assert.Equal(1u, BitConverter.ToUInt32(sentToReceiver, 48));
                Assert.Equal(0u, BitConverter.ToUInt32(sentToReceiver, 52)); // RedRose
                Assert.Equal(1u, BitConverter.ToUInt32(sentToSender, 56));   // free flower flag

                // Receiver gets updated spawn with RedRose charm (30010002 or better)
                var charmedSpawn = await sender.Game.WaitForSpawnAsync(receiver.User.Id, s => s.FlowerCharm != 0);
                Assert.NotNull(charmedSpawn);

                // 2. Sending a second free flower on the same day is rejected
                await sender.Game.SendAsync(BuildFlowerSend(receiver.User.Id, 0));
                await this.Servers.WaitForEventAsync("game", "flowers.rejected",
                    e => e.GetProperty("name").GetString() == senderName && e.GetProperty("reason").GetString() == "daily_used");

                // 3. Send a flower item (752009 = 9 Lilies)
                await CommandAsync(sender, "/item 752009");
                var lilyItem = await sender.Game.WaitForItemAsync(i => i.TypeId == 752009);
                Assert.NotNull(lilyItem);

                await sender.Game.SendAsync(BuildFlowerSend(receiver.User.Id, lilyItem.Id));
                var lilySent = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFlower
                        && BitConverter.ToUInt16(p, 4) == 0
                        && BitConverter.ToUInt32(p, 52) == 1);
                Assert.NotNull(lilySent);
                Assert.Equal(9u, BitConverter.ToUInt32(lilySent, 48));
                Assert.Equal(0u, BitConverter.ToUInt32(lilySent, 56)); // item flower, not free

                // 4. Query own flower ranks (MsgRank mode 2 -> mode 2 entries + mode 5 end)
                await receiver.Game.SendAsync(BuildRankQuery(2));
                var ownRed = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgRank
                        && p[4] == 2
                        && BitConverter.ToUInt32(p, 8) == RedRoseRankType);
                var ownLily = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgRank
                        && p[4] == 2
                        && BitConverter.ToUInt32(p, 8) == LilyRankType);
                var ownEnd = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgRank && p[4] == 5);
                Assert.NotNull(ownRed);
                Assert.NotNull(ownLily);
                Assert.NotNull(ownEnd);
                Assert.Equal(receiver.User.Id, BitConverter.ToUInt32(ownLily, 44));

                // 5. Query Lily ranking page 0 (MsgRank mode 1)
                await receiver.Game.SendAsync(BuildRankQuery(1, LilyRankType, 0));
                var page = await receiver.Game.ReceiveAsync(
                    p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgRank
                        && p[4] == 1
                        && BitConverter.ToUInt32(p, 8) == LilyRankType);
                Assert.NotNull(page);
                int count = BitConverter.ToInt32(page, 16);
                Assert.True(count >= 1);
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    int off = 24 + i * 56;
                    if (BitConverter.ToUInt32(page, off + 20) == receiver.User.Id)
                    {
                        Assert.Equal(9ul, BitConverter.ToUInt64(page, off + 8));
                        Assert.Equal(receiverName, ReadFixedAscii(page, off + 40, 16));
                        found = true;
                        break;
                    }
                }
                Assert.True(found);

                await this.LogOutAsync(sender);
                await this.LogOutAsync(receiver);
            }

            // 6. Re-login verifies persisted counts (female) and daily reset (male)
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET FlowerSentDay = DATE_SUB(CURDATE(), INTERVAL 1 DAY), VIPLevel = 2 WHERE Name = @name",
                ("@name", senderName));

            await using var senderAgain = await this.Servers.PlayAsync(senderEmail, Password);
            await using var receiverAgain = await this.Servers.PlayAsync(receiverEmail, Password);

            // VIP 2 male can send 3 free Red Roses on the new day
            await senderAgain.Game.SendAsync(BuildFlowerSend(receiverAgain.User.Id, 0));
            var vipSent = await receiverAgain.Game.ReceiveAsync(
                p => BitConverter.ToUInt16(p, 2) == PacketTypes.MsgFlower && BitConverter.ToUInt16(p, 4) == 0);
            Assert.NotNull(vipSent);
            Assert.Equal(3u, BitConverter.ToUInt32(vipSent, 48));
            Assert.Equal(1u, BitConverter.ToUInt32(vipSent, 56));
        }
    }
}

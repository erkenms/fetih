namespace Fetih.Game.Tests.Social
{
    using System;
    using System.Linq;
    using System.Text;
    using Fetih.Game.Packets;
    using Fetih.Game.Social;
    using Xunit;
    using static Fetih.Game.Packets.MsgSelfSynMemAwardRank;

    public class GuildRewardsTests
    {
        [Fact]
        public void TheLeaderAddsAtLeast100OfACurrencyOutOfTheFunds()
        {
            Assert.Null(GuildRewards.CheckAdd((0, 0), (100, 0), 500, 0));
            Assert.Null(GuildRewards.CheckAdd((0, 0), (0, 5000), 0, 5000));
            Assert.Equal("minimum", GuildRewards.CheckAdd((0, 0), (99, 0), 500, 0));
            Assert.Equal("minimum", GuildRewards.CheckAdd((0, 0), (200, 50), 500, 500));
            Assert.Equal("amount", GuildRewards.CheckAdd((0, 0), (0, 0), 500, 500));
            Assert.Equal("funds", GuildRewards.CheckAdd((0, 0), (600, 0), 500, 0));
            Assert.Equal("funds", GuildRewards.CheckAdd((0, 0), (0, 1000), 0, 999));
        }

        [Fact]
        public void OnceSetAnyAmountMayBeAdded()
        {
            Assert.Null(GuildRewards.CheckAdd((100, 0), (1, 0), 1, 0));
            Assert.Null(GuildRewards.CheckAdd((100, 300), (0, 1), 0, 1));
            Assert.Equal("amount", GuildRewards.CheckAdd((uint.MaxValue, 0), (1, 0), 10, 0));
        }

        [Fact]
        public void ThePoolIsSplitByExploitsRoundedDown()
        {
            var shares = GuildRewards.Split(1000, 10_000, new[] { (1u, 3u), (2u, 1u), (3u, 2u) });
            Assert.Equal(new[] { (1u, 500u, 5000ul), (2u, 166u, 1666ul), (3u, 333u, 3333ul) }, shares);
            Assert.True(shares.Sum(s => (long)s.ConquerPoints) <= 1000);
        }

        [Fact]
        public void NoExploitsSplitNothing()
        {
            Assert.Empty(GuildRewards.Split(1000, 1000, new[] { (1u, 0u) }));
            Assert.Empty(GuildRewards.Split(1000, 1000, Array.Empty<(uint, uint)>()));
        }

        [Fact]
        public void HugePoolsDontOverflow()
        {
            var shares = GuildRewards.Split(uint.MaxValue, ulong.MaxValue, new[] { (1u, uint.MaxValue), (2u, uint.MaxValue) });
            Assert.Equal(uint.MaxValue / 2, shares[0].ConquerPoints);
            Assert.Equal(ulong.MaxValue / 2, shares[1].Silver);
        }

        [Fact]
        public void ExploitsAreTalliedPerEvent()
        {
            GuildRewards.Begin(AwardEvent.CaptureTheFlag);
            GuildRewards.AddExploits(AwardEvent.CaptureTheFlag, 7, 1000001, "Ann", 3);
            Assert.Equal(5u, GuildRewards.AddExploits(AwardEvent.CaptureTheFlag, 7, 1000001, "Ann", 2));
            Assert.Equal(0u, GuildRewards.ExploitsOf(AwardEvent.GuildPk, 1000001));
            GuildRewards.Begin(AwardEvent.CaptureTheFlag);
            Assert.Equal(0u, GuildRewards.ExploitsOf(AwardEvent.CaptureTheFlag, 1000001));
        }

        [Fact]
        public void TheClientsAddIsDecodedByAction()
        {
            var bytes = new byte[92];
            BitConverter.TryWriteBytes(bytes.AsSpan(0), (ushort)92);
            BitConverter.TryWriteBytes(bytes.AsSpan(2), (ushort)1063);
            BitConverter.TryWriteBytes(bytes.AsSpan(4), (ushort)4);
            BitConverter.TryWriteBytes(bytes.AsSpan(6), 1u);
            BitConverter.TryWriteBytes(bytes.AsSpan(18), 0u);
            BitConverter.TryWriteBytes(bytes.AsSpan(22), 5000ul);
            var msg = new MsgSelfSynMemAwardRank();
            msg.Decode(bytes);
            Assert.Equal((AwardAction.AddSilver, AwardEvent.CaptureTheFlag), (msg.Action, msg.Event));
            Assert.True(msg.IsAdd);
            Assert.Equal((0u, 5000ul), msg.Added);

            BitConverter.TryWriteBytes(bytes.AsSpan(4), (ushort)6);
            BitConverter.TryWriteBytes(bytes.AsSpan(18), 300u);
            msg.Decode(bytes);
            Assert.Equal((300u, 0ul), msg.Added);

            BitConverter.TryWriteBytes(bytes.AsSpan(4), (ushort)1);
            BitConverter.TryWriteBytes(bytes.AsSpan(10), 2u);
            msg.Decode(bytes);
            Assert.False(msg.IsAdd);
            Assert.Equal(2u, msg.Page);
        }

        [Fact]
        public void GuildPoolsAre52ByteEntriesFrom30()
        {
            var msg = new MsgSelfSynMemAwardRank(AwardAction.GuildPools, AwardEvent.GuildPk)
            {
                Total = 9, ConquerPoints = 100, Silver = 2000,
            };
            msg.Pools.Add((500, 7_000_000_000, "Lions"));
            msg.Pools.Add((100, 0, "Wolves"));
            var bytes = msg.Encode();
            Assert.Equal(30 + 2 * 52, bytes.Length);
            Assert.Equal((1063, 0), (BitConverter.ToUInt16(bytes, 2), BitConverter.ToUInt16(bytes, 4)));
            Assert.Equal((2u, 9u, 2u), (BitConverter.ToUInt32(bytes, 6), BitConverter.ToUInt32(bytes, 10), BitConverter.ToUInt32(bytes, 14)));
            Assert.Equal((100u, 2000ul), (BitConverter.ToUInt32(bytes, 18), BitConverter.ToUInt64(bytes, 22)));
            Assert.Equal((500u, 7_000_000_000ul), (BitConverter.ToUInt32(bytes, 30), BitConverter.ToUInt64(bytes, 34)));
            Assert.Equal("Lions", Encoding.ASCII.GetString(bytes, 42, 5));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 94, 6));
        }

        [Fact]
        public void MemberSharesAre60ByteEntries()
        {
            var msg = new MsgSelfSynMemAwardRank(AwardAction.MemberShares, AwardEvent.CaptureTheFlag);
            msg.Shares.Add((12, 400, 9000, 1000007, "Ann"));
            var bytes = msg.Encode();
            Assert.Equal(MinimumLength, bytes.Length);
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 14));
            Assert.Equal((12u, 400u, 9000ul, 1000007u), (BitConverter.ToUInt32(bytes, 34), BitConverter.ToUInt32(bytes, 38),
                BitConverter.ToUInt64(bytes, 42), BitConverter.ToUInt32(bytes, 50)));
            Assert.Equal("Ann", Encoding.ASCII.GetString(bytes, 54, 3));
        }

        [Fact]
        public void CaptureTheFlagExploitsAre20ByteEntriesWithTheReadersAt18()
        {
            var msg = new MsgSelfSynMemAwardRank(AwardAction.Exploits, AwardEvent.CaptureTheFlag) { Total = 1, ConquerPoints = 7 };
            msg.Exploits.Add(("Ann", 7));
            var bytes = msg.Encode();
            Assert.Equal(8, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 18));
            Assert.Equal("Ann", Encoding.ASCII.GetString(bytes, 30, 3));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 46));

            var own = GuildRewards.OwnExploitsPacket(15).Encode();
            Assert.Equal((13, 1u, 15u), (BitConverter.ToUInt16(own, 4), BitConverter.ToUInt32(own, 6), BitConverter.ToUInt32(own, 18)));
        }
    }
}

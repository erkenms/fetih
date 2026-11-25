namespace Comet.Game.Tests.Social
{
    using System;
    using System.IO;
    using System.Linq;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Xunit;
    using static Comet.Game.Packets.MsgWarFlag;

    public class CaptureTheFlagTests
    {
        // Lines as the 5619 client's ini/region.ini and info.ini have them
        private const string RegionIni =
            "1002 4 348 209 160 225 TwinCity TwinCity 0 0 0 0 none none\r\n" +
            "2057 24 439 325 86 86 CTF-Arena none 0 0 0 0 none none\r\n" +
            "2057 25 318 439 20 20 CTF-Arena none 0 0 0 0 none none\r\n" +
            "2057 25 206 261 20 20 CTF-Arena none 0 0 0 0 none none\r\n" +
            "2057 25 196 086 20 20 CTF-Arena none 0 0 0 0 none none\r\n";
        private const string InfoIni =
            "[WarFlagEntrance]\r\nx=352\r\ny=337\r\nnpc=380\r\n\r\n" +
            "// rank1=CPs,silver\r\n[WarflagReward]\r\nrank1=3000,120000000\r\nrank2=2000,100000000\r\nrank3=1000,80000000\r\n\r\n" +
            "[MailBox]\r\nx=311\r\n";

        [Fact]
        public void ReadsTheSafeZoneBasesAndPrizesFromTheClient()
        {
            var arena = WarFlagArena.Parse(RegionIni, InfoIni, CaptureTheFlag.MapID);

            Assert.Equal(new Area(439, 325, 86, 86), arena.SafeZone);
            Assert.Equal(new[] { (328, 449), (216, 271), (206, 96) }, arena.Bases.Select(b => ((int)b.Center.X, (int)b.Center.Y)));
            Assert.True(arena.Bases[0].Contains(318, 439) && arena.Bases[0].Contains(337, 458));
            Assert.False(arena.Bases[0].Contains(338, 449));
            Assert.Equal(new[] { (3000u, 120_000_000ul), (2000u, 100_000_000ul), (1000u, 80_000_000ul) }, arena.Prizes);
        }

        [Fact]
        public void AnArenaWithoutRegionsIsRefused() =>
            Assert.Throws<InvalidDataException>(() => WarFlagArena.Parse("1002 4 1 1 1 1 x\r\n", InfoIni, CaptureTheFlag.MapID));

        [Fact]
        public void KillsScoreByTheVictimsBattlePower()
        {
            Assert.Equal(2, CaptureTheFlag.KillScore(300, 300));
            Assert.Equal(2, CaptureTheFlag.KillScore(300, 295));
            Assert.Equal(1, CaptureTheFlag.KillScore(300, 294));
            Assert.Equal(2, CaptureTheFlag.KillScore(300, 305));
            Assert.Equal(3, CaptureTheFlag.KillScore(300, 306));
        }

        [Fact]
        public void GuildsRankByPointsThenName()
        {
            var ranking = CaptureTheFlag.Ranking(new[] { (1u, "Bravo", 18u), (2u, "Alpha", 18u), (3u, "Charlie", 30u) });
            Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, ranking.Select(r => r.Name));
        }

        [Fact]
        public void RanksAndFlagPositionsAreLaidOutAsTheClientReadsThem()
        {
            var rank = new MsgWarFlag(FlagAction.ScoreRank) { GuildID = 77 };
            rank.GuildBases.Add((328, 449));
            rank.Ranks.Add((18, "Alpha"));
            rank.Ranks.Add((3, "Bravo"));
            var bytes = rank.Encode();
            Assert.Equal(32 + 2 * 24, bytes.Length);
            Assert.Equal((ushort)2, BitConverter.ToUInt16(bytes, 4));
            Assert.Equal(77u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal((328, 449, 0), (BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18), BitConverter.ToUInt16(bytes, 20)));
            Assert.Equal((ushort)2, BitConverter.ToUInt16(bytes, 28));
            Assert.Equal((1u, 3u), (BitConverter.ToUInt32(bytes, 32 + 24), BitConverter.ToUInt32(bytes, 36 + 24)));
            Assert.Equal("Bravo", System.Text.Encoding.ASCII.GetString(bytes, 40 + 24, 16).TrimEnd('\0'));

            var flags = new MsgWarFlag(FlagAction.FlagPositions);
            flags.Positions.Add((100, 200));
            bytes = flags.Encode();
            Assert.Equal(56, bytes.Length);
            Assert.Equal((1, 100, 200), (BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt16(bytes, 32), BitConverter.ToUInt16(bytes, 34)));
        }

        [Fact]
        public void ReadsWhatTheClientSends()
        {
            var packet = new byte[56];
            BitConverter.TryWriteBytes(packet.AsSpan(0), (ushort)56);
            BitConverter.TryWriteBytes(packet.AsSpan(2), (ushort)2224);
            BitConverter.TryWriteBytes(packet.AsSpan(4), (ushort)6);
            BitConverter.TryWriteBytes(packet.AsSpan(8), 700101u);
            var msg = new MsgWarFlag();
            msg.Decode(packet);
            Assert.Equal((FlagAction.PickedUp, 700101u), (msg.Action, msg.Data));
        }
    }
}

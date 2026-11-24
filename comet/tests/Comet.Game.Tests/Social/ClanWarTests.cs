namespace Comet.Game.Tests.Social
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Xunit;
    using static Comet.Game.Packets.MsgFamilyOccupy;

    public class ClanWarTests
    {
        [Fact]
        public void OccupyDialogIs136BytesWithTheFieldsWhereTheClientReadsThem()
        {
            var bytes = new MsgFamilyOccupy(OccupyAction.Query, 101887)
            {
                Role = ClanWar.RoleLeader, Occupier = "Wolves", MapName = "Twin City",
                CanJoin = true, CanApply = false, CanChange = true, Days = 7,
                DailyPrize = 722457, WeeklyPrize = 722456, Challenged = true, Fee = 5_000_000_000,
                RevenueUnclaimed = true, PlayerID = 1000001,
            }.Encode();

            Assert.Equal(Size, bytes.Length);
            Assert.Equal(1313, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(6u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((101887u, 101887u), (BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt32(bytes, 12)));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal("Wolves", Encoding.ASCII.GetString(bytes, 20, 6));
            Assert.Equal("Twin City", Encoding.ASCII.GetString(bytes, 56, 9));
            Assert.Equal((1, 0, 1), (bytes[92], bytes[93], bytes[94]));
            Assert.Equal((7u, 722457u, 722456u), (BitConverter.ToUInt32(bytes, 96),
                BitConverter.ToUInt32(bytes, 100), BitConverter.ToUInt32(bytes, 104)));
            Assert.Equal(1, bytes[112]);
            Assert.Equal(5_000_000_000ul, BitConverter.ToUInt64(bytes, 120));
            Assert.Equal(1, bytes[128]);
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 132));

            var parsed = Comet.Client.FamilyOccupyMessage.Parse(bytes);
            Assert.Equal(("Wolves", "Twin City", 7u, 5_000_000_000ul), (parsed.Occupier, parsed.MapName, parsed.Days, parsed.Fee));
        }

        [Fact]
        public void TheQueryCarriesTheNpcAt12AndOtherActionsAt8()
        {
            // As the 5619 client sent it on clicking Bird Island's banner
            var logged = Convert.FromHexString(
                "8800210506000000000000000D8E0100" + new string('0', (136 - 16) * 2));
            var query = new MsgFamilyOccupy();
            query.Decode(logged);
            Assert.Equal((OccupyAction.Query, 101901u), (query.Action, query.NpcID));

            var join = new MsgFamilyOccupy();
            join.Decode(Comet.Client.FamilyOccupyMessage.Build(Comet.Client.FamilyOccupyMessage.Join, 101887));
            Assert.Equal((OccupyAction.Join, 101887u), (join.Action, join.NpcID));
        }

        [Fact]
        public void RevenueIsANormalPackAndABetterOneEverySeventhDay()
        {
            var twinCity = ClanWar.Find(101887);
            Assert.Equal(722458u, ClanWar.RevenueOf(twinCity, 1));      // Normal
            Assert.Equal(722457u, ClanWar.RevenueOf(twinCity, 7));      // Refined
            Assert.Equal(722456u, ClanWar.RevenueOf(twinCity, 14));     // Unique
            Assert.Equal(722455u, ClanWar.RevenueOf(twinCity, 21));     // Elite
            Assert.Equal(722454u, ClanWar.RevenueOf(twinCity, 28));     // Super
            Assert.Equal(722457u, ClanWar.RevenueOf(twinCity, 35));
            Assert.Equal(722457u, ClanWar.NextSpecialOf(twinCity, 0));
            Assert.Equal(722456u, ClanWar.NextSpecialOf(twinCity, 7));
            Assert.Equal(722473u, ClanWar.RevenueOf(ClanWar.Find(101897), 3)); // Love Canyon: Ape City's packs
        }

        [Fact]
        public void SignUpClosesAtEightAndReopensWhenTheWarEnds()
        {
            DateTime At(int h, int m) => new DateTime(2026, 11, 24, h, m, 0);
            Assert.True(ClanWar.IsSignUpOpen(At(19, 59)));
            Assert.False(ClanWar.IsSignUpOpen(At(20, 0)));
            Assert.False(ClanWar.IsSignUpOpen(At(20, 45)));
            Assert.True(ClanWar.IsSignUpOpen(At(21, 0)));
            Assert.False(ClanWar.IsWarTime(At(20, 29)));
            Assert.True(ClanWar.IsWarTime(At(20, 30)));
            Assert.False(ClanWar.IsWarTime(At(21, 0)));
        }

        [Fact]
        public void MostKillsWinTheOccupierKeepsTiesAndNoShows()
        {
            var clans = new uint[] { 1, 2, 3 };
            var none = new Dictionary<uint, int>();
            Func<uint, bool> free = _ => false;

            var won = ClanWar.Decide(1, new Dictionary<uint, int> { [2] = 3, [1] = 2 }, none, clans, free);
            Assert.Equal((2u, true), (won.WinnerID, won.Taken));

            var tie = ClanWar.Decide(1, new Dictionary<uint, int> { [2] = 2, [1] = 2 }, none, clans, free);
            Assert.Equal((1u, false), (tie.WinnerID, tie.Taken));

            var noShow = ClanWar.Decide(1, none, none, clans, free);
            Assert.Equal((1u, false), (noShow.WinnerID, noShow.Taken));

            var lastStanding = ClanWar.Decide(0, none, new Dictionary<uint, int> { [3] = 2 }, clans, free);
            Assert.Equal((3u, true), (lastStanding.WinnerID, lastStanding.Taken));

            // A clan already holding another map can't take this one
            var holder = ClanWar.Decide(1, new Dictionary<uint, int> { [2] = 5 }, none, clans, id => id == 2);
            Assert.Equal((1u, false), (holder.WinnerID, holder.Taken));
        }
    }
}

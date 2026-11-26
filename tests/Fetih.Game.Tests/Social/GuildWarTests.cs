namespace Fetih.Game.Tests.Social
{
    using System;
    using System.Linq;
    using System.Text;
    using Fetih.Game.Npcs;
    using Fetih.Game.Packets;
    using Fetih.Game.Social;
    using Fetih.Game.World;
    using Xunit;

    public class GuildWarTests
    {
        [Fact]
        public void MsgNpcInfoExCarriesNameAtOffset36()
        {
            var pole = new Npc { NpcID = GuildWar.PoleID, Lookface = 1135, Role = Npc.PoleRole, MapID = 1038, X = 84, Y = 99, Life = 20_000_000 };
            var bytes = new MsgNpcInfoEx(pole, 20_000_000, 15_000_000, 1135, "Storm").Encode();

            Assert.Equal(1109, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(GuildWar.PoleID, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(20_000_000u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(15_000_000u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(((ushort)84, (ushort)99, (ushort)1135, (ushort)Npc.PoleRole), (BitConverter.ToUInt16(bytes, 20), BitConverter.ToUInt16(bytes, 22),
                BitConverter.ToUInt16(bytes, 24), BitConverter.ToUInt16(bytes, 26)));
            Assert.Equal((1, 5), (bytes[36], bytes[37]));
            Assert.Equal("Storm", Encoding.Latin1.GetString(bytes, 38, 5));

            var parsed = Fetih.TestClient.GoalSpawn.Parse(bytes);
            Assert.Equal((GuildWar.PoleID, 20_000_000u, 15_000_000u, (ushort)1135, (ushort)Npc.PoleRole, "Storm"),
                (parsed.Id, parsed.MaxLife, parsed.Life, parsed.Lookface, parsed.Role, parsed.Name));
        }

        [Theory]
        // Saturday 20:00 -> Sunday 20:00 (wraps past Sunday 00:00)
        [InlineData(2026, 11, 21, 19, 59, false)] // Saturday 19:59
        [InlineData(2026, 11, 21, 20, 00, true)]  // Saturday 20:00
        [InlineData(2026, 11, 22, 00, 00, true)]  // Sunday 00:00
        [InlineData(2026, 11, 22, 19, 59, true)]  // Sunday 19:59
        [InlineData(2026, 11, 22, 20, 00, false)] // Sunday 20:00
        [InlineData(2026, 11, 25, 12, 00, false)] // Wednesday 12:00
        public void WeeklyWrapAroundScheduleMatchesWindow(int year, int month, int day, int hour, int minute, bool expected)
        {
            var local = new DateTime(year, month, day, hour, minute, 0);
            Assert.Equal(expected, GuildWar.IsScheduledWindow(local, DayOfWeek.Saturday, 20, 0, DayOfWeek.Sunday, 20, 0));
        }

        [Fact]
        public void NonWrappingScheduleMatchesWindow()
        {
            var inside = new DateTime(2026, 11, 20, 21, 0, 0); // Friday 21:00
            var before = new DateTime(2026, 11, 20, 19, 59, 0);
            var after = new DateTime(2026, 11, 21, 20, 0, 0); // Saturday 20:00
            Assert.True(GuildWar.IsScheduledWindow(inside, DayOfWeek.Friday, 20, 0, DayOfWeek.Saturday, 20, 0));
            Assert.False(GuildWar.IsScheduledWindow(before, DayOfWeek.Friday, 20, 0, DayOfWeek.Saturday, 20, 0));
            Assert.False(GuildWar.IsScheduledWindow(after, DayOfWeek.Friday, 20, 0, DayOfWeek.Saturday, 20, 0));
        }

        [Fact]
        public void AllMap1038NpcsHaveDialogsOrTeleportsAndStructuresLoad()
        {
            var npcs = NpcTable.Load(DataFiles.PathOf("npcs.json"));
            var teleporters = new Teleporters(Teleporters.Load(DataFiles.PathOf("teleporters.json")));
            var gwNpcs = npcs.Where(n => n.MapID == GuildWar.MapID).ToList();

            var pole = gwNpcs.Single(n => n.NpcID == GuildWar.PoleID);
            Assert.Equal((Npc.PoleRole, (ushort)1135, (ushort)84, (ushort)99), (pole.Role, pole.Lookface, pole.X, pole.Y));
            Assert.True(pole.IsGuildWarTarget);

            var left = gwNpcs.Single(n => n.NpcID == GuildWar.LeftGateID);
            var right = gwNpcs.Single(n => n.NpcID == GuildWar.RightGateID);
            Assert.Equal((Npc.GateRole, GuildWar.LeftGateClosedLook), (left.Role, left.Lookface));
            Assert.Equal((Npc.GateRole, GuildWar.RightGateClosedLook), (right.Role, right.Lookface));

            foreach (var npc in gwNpcs.Where(n => n.NpcID != GuildWar.PoleID))
                Assert.True(Dialogs.Has(npc.NpcID) || teleporters.Find(npc.NpcID) != null,
                    $"NPC {npc.NpcID} on map 1038 has no dialog or teleporter");
        }
    }
}

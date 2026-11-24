namespace Comet.Game.Tests.World
{
    using System.Linq;
    using Comet.Game.World;
    using Xunit;

    public class NpcTableTests
    {
        private static Npc At(uint id, ushort x, ushort y, ushort lookface = 20) =>
            new Npc { NpcID = id, Lookface = lookface, Role = 2, MapID = 1002, X = x, Y = y };

        [Fact]
        public void ChangesListMovedRemovedAddedAndRelookedNpcs()
        {
            var before = new NpcTable(new[] { At(1, 10, 10), At(2, 20, 20), At(3, 30, 30), At(4, 40, 40) });
            var after = new NpcTable(new[] { At(1, 10, 10), At(2, 21, 20), At(4, 40, 40, lookface: 30), At(5, 50, 50) });

            var (gone, shown) = before.Changes(after);

            Assert.Equal(new uint[] { 2, 3, 4 }, gone.Select(n => n.NpcID).OrderBy(i => i));
            Assert.Equal(new uint[] { 2, 4, 5 }, shown.Select(n => n.NpcID).OrderBy(i => i));
            Assert.Equal(21, shown.Single(n => n.NpcID == 2).X);
        }
    }
}

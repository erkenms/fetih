namespace Fetih.Game.Tests.Items
{
    using Fetih.Game.Combat;
    using Fetih.Game.Items;
    using Xunit;

    public class GemsTests
    {
        [Theory]
        [InlineData((byte)13, Gems.Dragon, 15)]    // super dragon
        [InlineData((byte)2, Gems.Phoenix, 10)]    // refined phoenix
        [InlineData((byte)71, Gems.Tortoise, 2)]   // normal tortoise
        [InlineData((byte)33, Gems.Rainbow, 25)]
        [InlineData((byte)255, -1, 0)]             // an empty socket
        [InlineData((byte)0, -1, 0)]               // no socket
        [InlineData((byte)14, -1, 0)]              // no such grade
        public void SocketsHoldTheGemsKindAndGrade(byte socket, int kind, int percent)
        {
            Assert.Equal(kind, Gems.Kind(socket));
            Assert.Equal(percent, Gems.Percent(socket));
        }

        [Theory]
        [InlineData(1000u, 0, 0, 1000u)]
        [InlineData(1000u, 6, 0, 940u)]
        [InlineData(1000u, 6, 5, 893u)]   // one after the other
        [InlineData(1u, 54, 50, 1u)]      // a hit that lands does at least 1
        [InlineData(0u, 54, 50, 0u)]      // a miss stays a miss
        public void TortoiseGemsAndBlessingsCutDamageTaken(uint damage, int tortoise, int bless, uint taken)
        {
            var stats = new Stats(1, 1, 0, 0, 1, 1000, false) { TortoisePercent = tortoise, BlessPercent = bless };
            Assert.Equal(taken, stats.Taken(damage));
        }

        [Fact]
        public void AttackPercentagesRaiseDamageNotTheBase()
        {
            var stats = new Stats(100, 200, 0, 0, 1, 1000, false, MagicAttack: 50) { AttackPercent = 30, MagicPercent = 10 };
            Assert.Equal((130, 260, 55, 100), (stats.MinDamage, stats.MaxDamage, stats.MagicDamage, stats.MinAttack));
        }
    }
}

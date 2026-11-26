namespace Fetih.Game.Tests.Items
{
    using Fetih.Game.Items;
    using Xunit;

    public class ItemAdditionTableTests
    {
        [Theory]
        [InlineData(120023u, 120020u)]  // necklace
        [InlineData(300000u, 300000u)]  // steed
        [InlineData(410301u, 444300u)]  // one-handed blade
        [InlineData(421305u, 421300u)]  // backsword keeps its own
        [InlineData(510301u, 555300u)]  // two-handed glaive
        [InlineData(500301u, 500300u)]  // bow keeps its own
        [InlineData(511008u, 511000u)]  // scythe keeps its own
        [InlineData(601109u, 601100u)]
        public void KeysDropTheQualityAndShareWeaponTables(uint type, uint key)
        {
            Assert.Equal(key, ItemAdditionTable.Key(type));
        }

        [Fact]
        public void ItemsFindTheirPlusByKey()
        {
            var table = ItemAdditionTable.Parse(
                "120020 2 47 0 0 4 0 0 0 0\r\n444300 1 0 5 5 0 0 0 0 0\r\nbad line\r\n");
            Assert.Equal(2, table.Count);
            Assert.Equal(47, table.Of(120023, 2).Life);
            Assert.Equal(5, table.Of(410305, 1).MaxAttack);
            Assert.Null(table.Of(120023, 1));
        }
    }
}

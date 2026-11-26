namespace Fetih.Game.Tests.Items
{
    using System.Text;
    using Fetih.TestClient;
    using Fetih.Game.Items;
    using Xunit;

    public class ItemTypeTableTests
    {
        // Records as the 5619 client has them, decrypted
        private const string LuckyBlade =
            "410301@@LuckyBlade@@0@@0@@1@@0@@0@@0@@0@@0@@0@@0@@100@@0@@4@@2@@0@@0@@0@@0@@0@@1099@@1099@@" +
            "0@@0@@0@@0@@0@@0@@0@@0@@0@@1@@1000@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@" +
            "0@@Blade@@Specially~for~new~players.@@5@@0@@0@@0@@1000@@500@@";
        private const string Stancher =
            "1000000@@Stancher@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@5@@0@@0@@0@@0@@0@@0@@70@@0@@1@@1@@0@@0@@0@@0@@0@@0@@0@@0@@0@@" +
            "1@@1000@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@5@@0@@0@@0@@0@@0@@Drug@@None@@5@@0@@0@@0@@14020@@500@@";
        private const string DragonBall =
            "1088000@@DragonBall@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@10000@@0@@0@@0@@0@@0@@0@@0@@0@@1@@1@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@215@@0@@0@@0@@0@@0@@0@@0@@0@@0@@" +
            "0@@0@@0@@0@@0@@0@@RareItem@@You~can~use~it~to~improve~the~quality~of~weapons~and~equipment~at~the~Magic~Artisan~(Market~259,248).@@8@@0@@0@@0@@11000@@500@@";

        private static ItemTypeTable Parse(params string[] lines) =>
            ItemTypeTable.Parse(Encoding.Latin1.GetBytes(string.Join("\r\n", lines) + "\r\n"));

        [Fact]
        public void ReadsTheClientsFields()
        {
            var table = Parse(LuckyBlade, Stancher, DragonBall);
            Assert.Equal(3, table.Count);

            Assert.True(table.TryGet(410301, out var blade));
            Assert.Equal(("LuckyBlade", (byte)1, 100u), (blade.Name, blade.RequiredLevel, blade.Price));
            Assert.Equal((4, 2), ((int)blade.MaxAttack, (int)blade.MinAttack));
            Assert.Equal((1099, 1099), ((int)blade.Amount, (int)blade.AmountLimit));
            Assert.Equal((1, 1000), ((int)blade.AttackRange, (int)blade.AttackSpeed));

            Assert.True(table.TryGet(1000000, out var stancher));
            Assert.Equal((70, 5u), ((int)stancher.Life, stancher.StackLimit));

            Assert.True(table.TryGet(1088000, out var dragonBall));
            Assert.Equal(215u, dragonBall.EmoneyPrice);
        }

        [Fact]
        public void SkipsShortAndRepeatedRecords()
        {
            var table = Parse(LuckyBlade, "410302@@Broken@@0@@", LuckyBlade.Replace("LuckyBlade", "Copy"));
            Assert.Equal(1, table.Count);
            Assert.Equal(2, table.Skipped);
            Assert.True(table.TryGet(410301, out var blade));
            Assert.Equal("LuckyBlade", blade.Name);
        }

        [Fact]
        public void DecryptsWhatTheTestClientEncrypts()
        {
            var data = ItemFiles.ItemTypeDat((111003, "Iron Helmet", 3899, 15), (1000000, "Stancher", 1, 0));
            TqFileCipher.Decrypt(data);
            var table = ItemTypeTable.Parse(data);
            Assert.True(table.TryGet(111003, out var helmet));
            Assert.Equal(("Iron Helmet", (ushort)3899, (byte)15), (helmet.Name, helmet.AmountLimit, helmet.RequiredLevel));
            Assert.True(table.TryGet(1000000, out _));
        }

        [Fact]
        public void DecryptsTheClientFilesFirstBytes()
        {
            // The first bytes of the 5619 client's itemtype.dat
            var data = new byte[] { 0x98, 0x0B, 0x8F, 0x7A, 0xDE, 0xB0, 0x1E, 0xA0, 0x63, 0xF9, 0x1A };
            TqFileCipher.Decrypt(data);
            Assert.Equal("50000@@Spee", Encoding.ASCII.GetString(data));
        }
    }
}

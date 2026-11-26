namespace Fetih.Game.Tests.Magic
{
    using System.Text;
    using Fetih.Game.Magic;
    using Xunit;
    using Operation = Fetih.Game.Magic.MagicTypeOpTable.Operation;

    public class MagicTypeOpTableTests
    {
        [Fact]
        public void MagicTypeOpsReadTheClientsLines()
        {
            var data = Encoding.Latin1.GetBytes(
                "3,1,15,21,0,1015,1115,0,0\r\n" +
                "253,2,15,11,7,9876,3050,0,0\r\n" +
                "1,1,15,11,2,1360,1045,0\r\n" +
                "4,1,15,11,2,1360,4000,0\r\n");
            var ops = MagicTypeOpTable.Parse(data);
            Assert.Equal(3, ops.Count);
            Assert.Equal(new ushort[] { 1015, 1115 }, ops.Of(1, 15, 21, Operation.Forget));
            Assert.Equal(new ushort[] { 9876, 3050 }, ops.Of(2, 15, 11, Operation.Learn));
            Assert.Equal(new ushort[] { 1360, 1045, 4000 }, ops.Of(1, 15, 11, Operation.Restart));
            Assert.Empty(ops.Of(1, 15, 11, Operation.Forget));
        }
    }
}

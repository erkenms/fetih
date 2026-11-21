namespace Comet.Game.Tests.Packets
{
    using System;
    using System.Linq;
    using Comet.Game.Combat;
    using Comet.Game.Packets;
    using Xunit;

    public class MsgPlayerAttribInfoTests
    {
        [Fact]
        public void StatsAreAtTheWikisOffsets()
        {
            var stats = new Stats(MinAttack: 30, MaxAttack: 45, Defense: 12, Dodge: 5, Range: 1,
                AttackSpeed: 1000, Bow: false, MagicAttack: 7, MagicDefense: 3);
            var bytes = new MsgPlayerAttribInfo(1000042, 318, 25, 6, stats).Encode();

            Assert.Equal(MsgPlayerAttribInfo.Size, bytes.Length);
            Assert.Equal(1040, BitConverter.ToUInt16(bytes, 2));
            var fields = Enumerable.Range(1, 10).Select(i => BitConverter.ToUInt32(bytes, i * 4));
            Assert.Equal(new uint[] { 1000042, 318, 25, 45, 30, 12, 7, 3, 5, 6 }, fields);
            Assert.All(bytes.Skip(44), b => Assert.Equal(0, b));
        }

        [Fact]
        public void TheClientAsksWithAnIdAtFour()
        {
            // As the 5619 client sent it on opening the stat pane, seal included
            var msg = new MsgPlayerAttribInfo();
            msg.Decode(Convert.FromHexString("8800100440420F00" + new string('0', 256) + "5451436C69656E74"));
            Assert.Equal(1000000u, msg.Identity);
        }
    }
}

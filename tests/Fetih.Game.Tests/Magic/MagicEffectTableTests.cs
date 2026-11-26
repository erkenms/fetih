namespace Fetih.Game.Tests.Magic
{
    using Fetih.Game.Magic;
    using Xunit;

    public class MagicEffectTableTests
    {
        [Fact]
        public void ALevelTakesItsLevelZerosKeysAndTheClientsDefaults()
        {
            var table = MagicEffectTable.Parse(
                "[110400]\r\nName=Scurvy Bomb\r\nClientRepresent=3\r\nTraceEffectDelay=250\r\nTraceEffectSpeed=2\r\n" +
                "TraceEffect=pro_gre_assbom\r\n\r\n[110401]\r\nDesc=Upgradable\r\n\r\n" +
                "[110600]\r\nTraceEffectAcceleration=2\r\nTraceEffectAcceleration=0\r\n\r\n" +
                "[11800]\r\nClientRepresent=1\r\nDelayOfTarget3DEffect=500\r\nWoundDelayOfTarget=500\r\n");

            var bomb = table.Find(11040, 1);
            Assert.Equal(new MagicEffect(MagicEffect.Thrown, 0, 0, true, 250, 2, 300, 0), bomb);
            Assert.Equal(2, table.Find(11060, 0).TraceAcceleration);
            var meteor = table.Find(1180, 0);
            Assert.Equal((500, 500, false, 100), (meteor.TargetDelay, meteor.WoundDelay, meteor.Traces, meteor.TraceSpeed));
            Assert.Null(table.Find(1000, 0));
            var kick = MagicEffectTable.Parse("[104150]\r\nClientRepresent=107\r\nSkillLastTime=1000\r\n" +
                "[103810]\r\nClientRepresent=1\r\nIntoneDuration=500\r\n");
            Assert.Equal((MagicEffect.Lift, 1000), (kick.Find(10415, 0).Represent, kick.Find(10415, 0).LastTime));
            Assert.Equal(500, kick.Find(10381, 0).Intone);
        }
    }
}

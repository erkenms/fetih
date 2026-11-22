namespace Comet.Game.Tests.Magic
{
    using System;
    using System.Text;
    using Comet.Client;
    using Comet.Game.Combat;
    using Comet.Game.Magic;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Xunit;

    public class MagicTypeTableTests
    {
        // Records as the 5619 client has them, decrypted
        private const string Thunder1 =
            "1001@@1000@@1@@Thunder@@1@@0@@0@@0@@1@@6@@16@@0@@100@@0@@0@@9@@-1@@0@@113060@@1177@@10@@0@@0@@0@@0@@0@@" +
            "0@@0@@0@@0@@0@@0@@1000@@0@@1@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@600@@";
        private const string Superman =
            "1028@@1025@@6@@Superman@@0@@0@@0@@2@@0@@0@@31000@@0@@100@@20@@0@@0@@13@@0@@0@@0@@3@@1@@0@@0@@0@@0@@" +
            "0@@0@@0@@0@@0@@0@@0@@0@@1@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@";
        private const string FastBlade =
            "1037@@1045@@14@@FastBlade@@1@@1@@0@@4@@0@@0@@30110@@0@@100@@0@@4@@10@@-1@@0@@100000@@7@@40@@2@@410@@" +
            "0@@0@@0@@0@@5@@0@@20@@1@@0@@0@@600@@1@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@0@@";

        private static MagicTypeTable Parse(params string[] lines) =>
            MagicTypeTable.Parse(Encoding.Latin1.GetBytes(string.Join("\r\n", lines) + "\r\n"));

        [Fact]
        public void ReadsTheClientsFields()
        {
            var table = Parse(Thunder1, Superman, FastBlade);
            Assert.Equal(3, table.Count);

            Assert.True(table.TryGet(1000, 1, out var thunder));
            Assert.Equal((MagicSort.Attack, (ushort)6, 16, 9u), (thunder.Sort, thunder.UseMana, thunder.Power, thunder.Distance));
            Assert.Equal((113060u, (byte)10, false), (thunder.NeedExperience, thunder.NeedLevel, thunder.IsXpSkill));

            Assert.True(table.TryGet(1025, 0, out var superman));
            Assert.True(superman.IsXpSkill);
            Assert.Equal((MagicSort.AttachStatus, 20u, 1000), (superman.Sort, superman.StepSeconds, superman.PowerPercent));

            Assert.True(table.TryGet(1045, 0, out var fastBlade));
            Assert.Equal((MagicSort.Line, 4u, (byte)20, 410u), (fastBlade.Sort, fastBlade.Range, fastBlade.UseStamina, fastBlade.WeaponSubtype));
            Assert.Equal(600u, fastBlade.DelayMilliseconds);
        }

        [Fact]
        public void SkipsShortRecords() => Assert.Equal(1, Parse(Thunder1, "1@@2@@3@@").Skipped);

        [Fact]
        public void ReadsWhatTheTestClientWrites()
        {
            var data = MagicFiles.MagicTypeDat(new MagicFiles.Spell(1000, 0, 1, "Thunder") { Power = 7, Distance = 9 },
                new MagicFiles.Spell(5030, 0, 16, "Phoenix") { Percent = 40, NeedLevel = 15, AutoActive = 4 },
                new MagicFiles.Spell(8001, 3, 4, "ScatterFire") { UseItem = 50, UseItemCount = 3 });
            Comet.Game.Items.TqFileCipher.Decrypt(data);
            var table = MagicTypeTable.Parse(data);
            Assert.True(table.TryGet(1000, 0, out var thunder));
            Assert.Equal((7, 9u, false), (thunder.Power, thunder.Distance, thunder.IsPassive));
            Assert.True(table.TryGet(5030, 0, out var phoenix));
            Assert.Equal((MagicSort.AttackStatus, 40u, (byte)15, true), (phoenix.Sort, phoenix.Percent, phoenix.NeedLevel, phoenix.IsPassive));
            Assert.True(table.TryGet(8001, 3, out var scatter));
            Assert.Equal((50u, (ushort)3), (scatter.UseItem, scatter.UseItemCount));
        }
    }

    public class ProficiencyTableTests
    {
        private static readonly ProficiencyTable Table =
            ProficiencyTable.Parse(new[] { "1,0,0", "2,1200,600", "3,68000,600", "" });

        [Theory]
        [InlineData(0, 0u, 5u, 1, 5u)]            // the first experience reaches level one
        [InlineData(1, 1100u, 200u, 2, 100u)]     // levels carry the rest over
        [InlineData(1, 0u, 1_000_000u, 3, 0u)]    // stops at the top
        public void ExperienceRaisesTheLevel(ushort level, uint experience, uint amount, ushort newLevel, uint left) =>
            Assert.Equal((newLevel, left), Table.Gain(level, experience, amount));

        [Fact]
        public void TheTopLevelNeedsNothing()
        {
            Assert.Equal(68000u, Table.Needed(2));
            Assert.Equal(0u, Table.Needed(3));
        }
    }

    public class MagicPacketTests
    {
        [Fact]
        public void DecodesTheClientsMagicAttack()
        {
            var bytes = InteractMessage.BuildMagic(1000042, 1000, 2, 400123, 431, 382);
            var msg = new MsgInteract();
            msg.Decode(bytes);
            Assert.Equal(MsgInteract.InteractionType.MagicAttack, msg.Action);
            Assert.Equal((1000042u, (ushort)1000, 400123u), (msg.SenderID, msg.MagicType, msg.TargetID));
            Assert.Equal(((ushort)431, (ushort)382), (msg.X, msg.Y));
        }

        [Fact]
        public void MagicInfoHasExperienceFirst()
        {
            var bytes = new MsgMagicInfo(1000, 3, 1234).Encode();
            Assert.Equal((20, 1103), (bytes.Length, (int)BitConverter.ToUInt16(bytes, 2)));
            Assert.Equal(new MagicInfo(1000, 3, 1234), MagicInfo.Parse(bytes));
        }

        [Fact]
        public void WeaponSkillHasWhatTheLevelNeeds()
        {
            var bytes = new MsgWeaponSkill(410, 2, 30, 68000).Encode();
            Assert.Equal((20, 1025), (bytes.Length, (int)BitConverter.ToUInt16(bytes, 2)));
            Assert.Equal(new WeaponSkill(410, 2, 30, 68000), WeaponSkill.Parse(bytes));
        }
    }

    public class SpellShapeTests
    {
        private static MagicType Spell(MagicSort sort, uint range) => new MagicType { Sort = sort, Range = range };

        [Theory]
        [InlineData(103, 100, true)]   // on the line toward the aim
        [InlineData(104, 100, true)]
        [InlineData(105, 100, false)]  // past its range
        [InlineData(102, 101, false)]  // beside it
        [InlineData(98, 100, false)]   // behind the caster
        public void LinesRunTowardTheAim(int x, int y, bool hit) =>
            Assert.Equal(hit, Casting.InShape(Spell(MagicSort.Line, 4), 100, 100, 110, 100, x, y));

        [Theory]
        [InlineData(105, 103, true)]
        [InlineData(103, 105, false)]
        [InlineData(95, 100, false)]
        public void FansOpenTowardTheAim(int x, int y, bool hit) =>
            Assert.Equal(hit, Casting.InShape(Spell(MagicSort.Fan, 8), 100, 100, 110, 100, x, y));

        [Fact]
        public void BombsHitAllAroundButNotTheCaster()
        {
            var bomb = Spell(MagicSort.Bomb, 3);
            Assert.True(Casting.InShape(bomb, 100, 100, 0, 0, 97, 103));
            Assert.False(Casting.InShape(bomb, 100, 100, 0, 0, 96, 100));
            Assert.False(Casting.InShape(bomb, 100, 100, 0, 0, 100, 100));
        }
    }

    public class StatusTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void StatusesRunOut()
        {
            var effects = new TimedStatus();
            Assert.True(effects.Set(StatusFlags.Superman, Now.AddSeconds(20), Now, 31000));
            Assert.True(effects.Set(StatusFlags.XpFull, Now.AddSeconds(5), Now));
            Assert.Equal(StatusFlags.Superman | StatusFlags.XpFull, effects.Status(Now));
            Assert.Equal(31000, effects.Power(StatusFlags.Superman, Now));

            Assert.Equal(StatusFlags.XpFull, effects.Expire(Now.AddSeconds(6)));
            Assert.Equal(StatusFlags.Superman, effects.Status(Now.AddSeconds(6)));
            Assert.Null(effects.Power(StatusFlags.XpFull, Now.AddSeconds(6)));
        }

        [Fact]
        public void KillsExtendOnlyBelowTheCap()
        {
            var effects = new TimedStatus();
            effects.Set(StatusFlags.Cyclone, Now.AddSeconds(20), Now);
            Assert.True(effects.Extend(StatusFlags.Cyclone, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(25), Now));
            effects.Set(StatusFlags.Cyclone, Now.AddSeconds(30), Now);
            Assert.False(effects.Extend(StatusFlags.Cyclone, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(25), Now));
            Assert.False(effects.Extend(StatusFlags.Superman, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(25), Now));
        }

        [Fact]
        public void StatusesScaleByTheirPower()
        {
            var effects = new TimedStatus();
            Assert.Equal(50u, Buffs.AgainstMonster(effects, 50, Now));
            effects.Set(StatusFlags.Superman, Now.AddSeconds(20), Now, 31000);
            effects.Set(StatusFlags.Shield, Now.AddSeconds(20), Now, 30300);
            Assert.Equal(500u, Buffs.AgainstMonster(effects, 50, Now));
            Assert.Equal(95, Buffs.AgainstPlayer(effects, 50, Now));
            Assert.Equal(30, Buffs.Defense(effects, 10, Now));
        }

        [Fact]
        public void MagicAddsPowerToMagicAttack()
        {
            Assert.Equal(26u, Damage.Magic(10, 16, 10, 10));
            Assert.Equal(39u, Damage.Magic(10, 16, 15, 10));
            Assert.Equal(1u, Damage.PlayerMagic(10, 16, 50));
        }
    }
}

namespace Comet.Game.Tests.States
{
    using System;
    using System.Text;
    using Comet.Client;
    using Comet.Game.Combat;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Xunit;

    public class SubClassTests
    {
        private static SubClassTable SampleTable()
        {
            const string ini = @"
[1]
title=Martial Artist
UplimitLev=9
AccessionNpcID=7778
[11]
req_lv=70
req_meto=0
req_joblv=0
EffectProportion=1
[12]
req_lv=90
req_meto=0
req_joblv=2
EffectProportion=2
[19]
req_lv=130
req_meto=2
req_joblv=9
EffectProportion=15

[2]
title=Warlock
UplimitLev=9
AccessionNpcID=7779
[21]
req_lv=70
req_meto=0
req_joblv=1
EffectProportion=1

[3]
title=Chi Master
UplimitLev=9
AccessionNpcID=7781
[35]
req_lv=90
req_meto=1
req_joblv=5
EffectProportion=6

[4]
title=Sage
UplimitLev=9
AccessionNpcID=7780
[44]
req_lv=120
req_meto=0
req_joblv=4
EffectProportion=4

[5]
title=Apothecary
UplimitLev=9
AccessionNpcID=7777
[53]
req_lv=110
req_meto=0
req_joblv=3
EffectProportion=24

[6]
title=Performer
UplimitLev=9
AccessionNpcID=7776
[62]
req_lv=90
req_meto=0
req_joblv=2
EffectProportion=200

[9]
title=Wrangler
UplimitLev=9
AccessionNpcID=8616
[93]
req_lv=110
req_meto=0
req_joblv=3
EffectProportion=300
";
            const string levExp = @"
0 1 100 0 0
1 1 300 0 0
1 2 900 0 0
1 9 0 0 0
5 1 100 0 0
9 1 400 0 0
";
            return SubClassTable.Parse(ini, levExp);
        }

        [Fact]
        public void ParsesSubProfessionInfoAndLevExp()
        {
            var table = SampleTable();
            Assert.Equal(7, table.Count);

            Assert.True(table.TryGet(SubClassId.MartialArtist, out var ma));
            Assert.Equal(7778u, ma.NpcId);
            Assert.Equal(9, ma.MaxLevel);
            Assert.Equal(70, ma.Phase(1).RequiredLevel);
            Assert.Equal(15, ma.Phase(9).Effect);
            Assert.Equal(2, ma.Phase(9).RequiredRebirths);
            Assert.Equal(300UL, ma.StudyCostForNextLevel(1));
            Assert.Equal(900UL, ma.StudyCostForNextLevel(2));
            Assert.Null(ma.StudyCostForNextLevel(9));

            Assert.True(table.TryGetByNpc(8616, out var wrangler));
            Assert.Equal(SubClassId.Wrangler, wrangler.Id);
            Assert.Equal(400UL, wrangler.StudyCostForNextLevel(1));
        }

        [Fact]
        public void LevExpCipherRoundTripsWithSeed0x4D2()
        {
            const string text = "1 1 300 0 0\r\n9 8 11600 0 0\r\n";
            var encrypted = ItemFiles.Encrypt(Encoding.Latin1.GetBytes(text), SubClassTable.LevExpSeed);
            TqFileCipher.Decrypt(encrypted, SubClassTable.LevExpSeed);
            Assert.Equal(text, Encoding.Latin1.GetString(encrypted));
        }

        [Fact]
        public void PacksAllSubProfMaskInBase10AndComputesBonuses()
        {
            var table = SampleTable();
            var subs = new SubClasses();
            subs.Set(SubClassId.MartialArtist, level: 2, phase: 2);
            subs.Set(SubClassId.Warlock, level: 1, phase: 1);
            subs.Set(SubClassId.ChiMaster, level: 5, phase: 5);
            subs.Set(SubClassId.Sage, level: 4, phase: 4);
            subs.Set(SubClassId.Apothecary, level: 3, phase: 3);
            subs.Set(SubClassId.Performer, level: 2, phase: 2);
            subs.Set(SubClassId.Wrangler, level: 3, phase: 3);

            // Digit 1=2, 2=1, 3=5, 4=4, 5=3, 6=2, 7=0, 8=0, 9=3 -> 300_234_512
            Assert.Equal(300_234_512UL, subs.AllSubProfMask());
            // Nothing is active: no bonus at all
            Assert.Equal(0, subs.CriticalStrikePercent(table));
            Assert.Equal(0, subs.AttackBonus(table));
            Assert.Equal(0, subs.MaxHealthBonus(table));

            // Only the active sub-class gives its bonus
            subs.Active = SubClassId.MartialArtist;
            Assert.Equal(2, subs.CriticalStrikePercent(table));
            Assert.Equal(0, subs.SkillCriticalStrikePercent(table));
            Assert.Equal(0, subs.AttackBonus(table));
            subs.Active = SubClassId.Warlock;
            Assert.Equal(1, subs.SkillCriticalStrikePercent(table));
            subs.Active = SubClassId.ChiMaster;
            Assert.Equal(6, subs.ImmunityPercent(table));
            subs.Active = SubClassId.Sage;
            Assert.Equal(4, subs.PenetrationPercent(table));
            subs.Active = SubClassId.Apothecary;
            Assert.Equal(24, subs.DetoxicationPercent(table));
            subs.Active = SubClassId.Performer;
            Assert.Equal(200, subs.AttackBonus(table));
            Assert.Equal(200, subs.MagicAttackBonus(table));
            Assert.Equal(0, subs.MaxHealthBonus(table));
            subs.Active = SubClassId.Wrangler;
            Assert.Equal(300, subs.MaxHealthBonus(table));
            Assert.Equal(0, subs.CriticalStrikePercent(table));
        }

        [Fact]
        public void APhaseCapLowersEverySubClassButTheApothecary()
        {
            var table = SampleTable();
            var subs = new SubClasses();
            subs.Set(SubClassId.Performer, level: 2, phase: 2);
            subs.Set(SubClassId.Apothecary, level: 3, phase: 3);
            subs.Active = SubClassId.Performer;
            Assert.Equal(200, subs.AttackBonus(table, maxPhase: 2));
            // Capped to phase 1, which the sample table gives nothing
            Assert.Equal(0, subs.AttackBonus(table, maxPhase: 1));
            subs.Active = SubClassId.Apothecary;
            Assert.Equal(24, subs.DetoxicationPercent(table, maxPhase: 1));
        }

        [Fact]
        public void CombatHelpersApplyCriticalImmunityPenetrationAndDetoxication()
        {
            var fixedZero = new Random(0);
            Assert.Equal(150u, Damage.ApplyCritical(100, critPercent: 100, immunityPercent: 0, 1.5, fixedZero));
            Assert.Equal(200u, Damage.ApplyCritical(100, critPercent: 100, immunityPercent: 0, 2.0, fixedZero));
            Assert.Equal(100u, Damage.ApplyCritical(100, critPercent: 15, immunityPercent: 15, 1.5, fixedZero));

            Assert.Equal(85, Damage.PenetratedMagicDefense(100, 15));
            Assert.Equal(0, Damage.PenetratedMagicDefense(100, 100));

            Assert.Equal(76u, Damage.DetoxifiedPoison(100, 24));
            Assert.Equal(28u, Damage.DetoxifiedPoison(100, 72));
            Assert.Equal(1u, Damage.DetoxifiedPoison(1, 72));
        }
    }
}

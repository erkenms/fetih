namespace Fetih.Game.Tests.Magic
{
    using System.Linq;
    using Fetih.Game.Magic;
    using Xunit;
    using static Fetih.Game.Magic.Learning;

    public class LearningTests
    {
        private static readonly MagicTypeTable Magics = new MagicTypeTable(new[]
        {
            new MagicType { Type = Learning.Thunder, Name = "Thunder", Sort = MagicSort.Attack },
            new MagicType { Type = Learning.Fire, Name = "Fire", Sort = MagicSort.Attack, NeedLevel = 40 },
            new MagicType { Type = Learning.Tornado, Name = "Tornado", Sort = MagicSort.Attack, NeedLevel = 90 },
            new MagicType { Type = Learning.Nectar, Name = "Nectar", Sort = MagicSort.Recruit, NeedLevel = 94 },
            new MagicType { Type = Learning.Phoenix, Name = "Phoenix", Sort = MagicSort.Attack, NeedLevel = 15 },
            new MagicType { Type = Learning.Cyclone, Name = "Cyclone", Sort = MagicSort.AttachStatus, NeedLevel = 3 },
            new MagicType { Type = Learning.Hercules, Name = "Hercules", Sort = MagicSort.Attack, NeedLevel = 40 },
            new MagicType { Type = Learning.Intensify, Name = "Intensify", Sort = MagicSort.AttachStatus, NeedLevel = 83 },
        });

        private const byte Trojan = 10, Taoist = 101, WaterTaoist = 132, FireTaoist = 142;

        [Fact]
        public void BooksTeachOnlyTheirLines()
        {
            Assert.Null(Unmet(Taoist, 1, new Skills(), Magics, Learning.Thunder));
            Assert.Equal("class", Unmet(Trojan, 1, new Skills(), Magics, Learning.Thunder));
            Assert.Null(Unmet(Trojan, 15, new Skills(), Magics, Learning.Phoenix));
        }

        [Fact]
        public void WithoutTheHelpsLevelTheFirstLevelsNeedLevelIsTheLevelToLearn()
        {
            Assert.Equal("level", Unmet(Trojan, 14, new Skills(), Magics, Learning.Phoenix));
            Assert.Null(Unmet(Trojan, 15, new Skills(), Magics, Learning.Phoenix));
        }

        [Fact]
        public void FireBuildsOnThunderAtLevelFour()
        {
            var skills = new Skills();
            skills.SetSpell(Learning.Thunder, new SkillLevel(4, 0));
            Assert.Equal("level", Unmet(Taoist, 39, skills, Magics, Learning.Fire));
            Assert.Null(Unmet(Taoist, 40, skills, Magics, Learning.Fire));
            skills.SetSpell(Learning.Thunder, new SkillLevel(3, 0));
            Assert.Equal("prerequisite", Unmet(Taoist, 40, skills, Magics, Learning.Fire));
        }

        [Fact]
        public void XpSkillsComeWithPromotions()
        {
            const byte Promoted = 11, Veteran = 12;
            Assert.Equal("level", Unmet(Trojan, 14, new Skills(), Magics, Learning.Cyclone));
            Assert.Equal("promotion", Unmet(Trojan, 15, new Skills(), Magics, Learning.Cyclone));
            Assert.Null(Unmet(Promoted, 15, new Skills(), Magics, Learning.Cyclone));
            Assert.Equal("promotion", Unmet(Promoted, 40, new Skills(), Magics, Learning.Hercules));
            Assert.Null(Unmet(Veteran, 40, new Skills(), Magics, Learning.Hercules));
        }

        [Fact]
        public void IntensifyIsLearnedAtTheHelpsLevelNotMagictypes()
        {
            const byte Archer = 40;
            Assert.Equal("level", Unmet(Archer, 70, new Skills(), Magics, Learning.Intensify));
            Assert.Null(Unmet(Archer, 71, new Skills(), Magics, Learning.Intensify));
        }

        [Fact]
        public void ElementSpellsNeedTheirElement()
        {
            Assert.Equal("element", Unmet(Taoist, 100, new Skills(), Magics, Learning.Nectar));
            Assert.Equal("element", Unmet(FireTaoist, 100, new Skills(), Magics, Learning.Nectar));
            Assert.Null(Unmet(WaterTaoist, 100, new Skills(), Magics, Learning.Nectar));
        }

        [Fact]
        public void TornadoBuildsOnFireAtLevelThree()
        {
            var skills = new Skills();
            Assert.Equal("prerequisite", Unmet(FireTaoist, 90, skills, Magics, Learning.Tornado));
            skills.SetSpell(Learning.Fire, new SkillLevel(2, 0));
            Assert.Equal("prerequisite", Unmet(FireTaoist, 90, skills, Magics, Learning.Tornado));
            skills.SetSpell(Learning.Fire, new SkillLevel(3, 0));
            Assert.Null(Unmet(FireTaoist, 90, skills, Magics, Learning.Tornado));
        }

        [Fact]
        public void LearnedOrUnknownSpellsArent()
        {
            var skills = new Skills();
            skills.SetSpell(Learning.Thunder, new SkillLevel(0, 0));
            Assert.Equal("learned", Unmet(Taoist, 1, skills, Magics, Learning.Thunder));
            Assert.Equal("unknown_magic", Unmet(WaterTaoist, 100, skills, Magics, Learning.Revive));
            Assert.Equal("no_rule", Unmet(Taoist, 100, skills, Magics, 7001));
        }

        [Fact]
        public void EveryBookHasARule() =>
            Assert.All(AllBooks, book => Assert.True(TryGetRule(BookOf(book).Value, out _)));

        [Fact]
        public void MastersTeachTheirLinesSkillsInOrder()
        {
            Assert.Equal(new ushort[] { Learning.Cyclone, Learning.Accuracy, Learning.Hercules, Learning.SpiritHealing },
                TaughtBy(Learning.Trojan));
            Assert.Contains(Learning.Accuracy, TaughtBy(Learning.Warrior));
            Assert.DoesNotContain(Learning.Thunder, TaughtBy(Learning.Taoist));
            Assert.Equal(new ushort[]
            {
                Learning.TripleAttack, Learning.Oblivion, Learning.WhirlwindKick, Learning.TyrantAura, Learning.FendAura,
                Learning.Serenity, Learning.RadiantPalm, Learning.Tranquility, Learning.SoulShackle, Learning.Compassion,
                Learning.MetalAura, Learning.WoodAura, Learning.WaterAura, Learning.FireAura, Learning.EarthAura,
            }, TaughtBy(Learning.Monk));
            Assert.Equal(new ushort[] { Learning.TwofoldBlades, Learning.ArcherBane, Learning.ToxicFog, Learning.ShurikenVortex, Learning.FatalStrike,
                Learning.PoisonStar, Learning.GapingWounds, Learning.CounterKill },
                TaughtBy(Learning.Ninja));
            Assert.Equal(new ushort[]
            {
                Learning.Windstorm, Learning.BladeTempest, Learning.CannonBarrage, Learning.GaleBomb, Learning.EagleEye,
                Learning.ScurvyBomb, Learning.BlackSpot, Learning.AdrenalineRush, Learning.BlackbeardsRage, Learning.KrakensRevenge,
            }, TaughtBy(Learning.Pirate));
        }

        [Fact]
        public void ElementsComeFromTheClass()
        {
            Assert.Equal(new[] { Element.Any, Element.Any, Element.Water, Element.Water, Element.Fire },
                new byte[] { 100, 101, 132, 135, 145 }.Select(ElementOf));
        }
    }
}

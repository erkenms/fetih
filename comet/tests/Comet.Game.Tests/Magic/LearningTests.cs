namespace Comet.Game.Tests.Magic
{
    using System.Linq;
    using Comet.Game.Magic;
    using Xunit;
    using static Comet.Game.Magic.Learning;

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
        public void TheFirstLevelsNeedLevelIsTheLevelToLearn()
        {
            Assert.Equal("level", Unmet(Taoist, 39, new Skills(), Magics, Learning.Fire));
            Assert.Null(Unmet(Taoist, 40, new Skills(), Magics, Learning.Fire));
            Assert.Equal("level", Unmet(Trojan, 14, new Skills(), Magics, Learning.Phoenix));
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
            Assert.Equal(new ushort[] { Learning.TwofoldBlades, Learning.ArcherBane, Learning.ToxicFog }, TaughtBy(Learning.Ninja));
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

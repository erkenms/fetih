namespace Comet.Game.Tests.Combat
{
    using System;
    using System.IO;
    using System.Linq;
    using Comet.Game.Combat;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Xunit;

    public class DamageTests
    {
        [Theory]
        [InlineData(10, 10, 1.0)]
        [InlineData(13, 10, 1.0)]
        [InlineData(14, 10, 1.5)]
        [InlineData(15, 10, 1.5)]
        [InlineData(20, 10, 2.4)]
        public void LowerMonstersTakeMoreDamage(int attacker, int target, double bonus) =>
            Assert.Equal(bonus, Damage.LevelBonus(attacker, target), 3);

        [Fact]
        public void MeleeIsReducedByDefenseButAlwaysDoesSomething()
        {
            Assert.Equal(7u, Damage.Melee(10, 10, 1, 1, 3, new Random(1)));
            Assert.Equal(1u, Damage.Melee(10, 10, 1, 1, 50, new Random(1)));
        }

        [Fact]
        public void ArrowsIgnoreDefenseButNotDodge() =>
            Assert.Equal(60u, Damage.Archery(100, 100, 1, 1, 40, new Random(1)));

        [Fact]
        public void PlayersHitPlayersWithoutALevelBonus()
        {
            Assert.Equal(70u, Damage.PlayerMelee(100, 30));
            Assert.Equal(1u, Damage.PlayerMelee(10, 30));
        }

        [Fact]
        public void ArrowsDoLessToPlayers() =>
            Assert.Equal(27u, Damage.PlayerArchery(100, 40));

        [Fact]
        public void MonsterHitsAreReducedByDefense() =>
            Assert.Equal(4u, Damage.MonsterMelee(9, 9, 5, new Random(1)));

        [Theory]
        [InlineData(36, true, 100)]
        [InlineData(85, true, 89)]
        [InlineData(30, false, 80)]
        public void HitChanceFallsWithDodge(int dodge, bool monster, int chance) =>
            Assert.Equal(chance, Damage.HitChance(dodge, monster));

        [Theory]
        [InlineData(100, 50, 1, 5)]       // far above: 5%
        [InlineData(100, 10, 7, 70)]      // 3 to 5 above: 70%
        [InlineData(100, 10, 10, 100)]
        [InlineData(100, 10, 14, 130)]    // up to 5 below: 130%
        [InlineData(100, 1, 30, 230)]
        [InlineData(1, 100, 1, 1)]        // never less than 1
        public void ExperienceFollowsTheLevelGap(uint dealt, int attacker, int monster, ulong experience) =>
            Assert.Equal(experience, Damage.Experience(dealt, attacker, monster));

        [Fact]
        public void KillsGiveFivePercentOfTheMonstersLife() =>
            Assert.Equal(25UL, Damage.KillBonus(500));
    }

    public class LootTests
    {
        private static MonsterType Pheasant(uint dropMoney = 50, byte level = 1) => new MonsterType
        {
            ID = 1, Name = "Pheasant", Level = level, Life = 33, DropMoney = dropMoney, DropHp = 1000000, DropMp = 1001000,
        };

        private static readonly ItemTypeTable Items = new ItemTypeTable(new[]
        {
            new ItemType { ID = 1000000, Name = "Stancher" },
            new ItemType { ID = 1001000, Name = "Agrypnotic" },
            new ItemType { ID = 410013, Name = "Blade", RequiredLevel = 1 },
            new ItemType { ID = 410016, Name = "RefinedBlade", RequiredLevel = 1 },
            new ItemType { ID = 130023, Name = "CoatArmor", RequiredLevel = 10 },
            new ItemType { ID = 410303, Name = "HighBlade", RequiredLevel = 40 },
            new ItemType { ID = 181303, Name = "Garment", RequiredLevel = 1 },
            new ItemType { ID = 1050000, Name = "Arrows", RequiredLevel = 1 },
            new ItemType { ID = Loot.Meteor, Name = "Meteor" },
        });

        [Fact]
        public void DropsAreSilverWithinTheMonstersRangeOrItsPotions()
        {
            var random = new Random(7);
            var rolls = Enumerable.Range(0, 2000).SelectMany(_ => Loot.Roll(Pheasant(), Items, random)).ToList();
            var silver = rolls.Where(d => d.Silver > 0).ToList();
            Assert.InRange(silver.Count, 450, 750);
            Assert.All(silver, d => Assert.InRange(d.Silver, 25u, 50u));
            Assert.InRange(rolls.Count(d => d.TypeID == 1000000), 50, 150);
            Assert.InRange(rolls.Count(d => d.TypeID == 1001000), 50, 150);
            Assert.InRange(rolls.Count(d => d.TypeID / 1000 == 410), 20, 100);
            Assert.InRange(rolls.Count(d => d.TypeID == Loot.Meteor), 3, 40);
        }

        [Fact]
        public void MonstersWithoutMoneyDropNoSilver()
        {
            var random = new Random(3);
            Assert.DoesNotContain(Enumerable.Range(0, 500).SelectMany(_ => Loot.Roll(Pheasant(0), Items, random)), d => d.Silver > 0);
        }

        [Fact]
        public void EquipmentIsWornGearOfNormalQualityUpToTheMonstersLevel()
        {
            var random = new Random(5);
            var drops = Enumerable.Range(0, 1000).Select(_ => Loot.Equipment(15, Items, random)).ToList();
            Assert.All(drops, d => Assert.Contains(d, new uint?[] { 410013, 410016, 130023 }));
            Assert.Contains(drops, d => d == 130023);
            Assert.InRange(drops.Count(d => d == 410016), 1, 40);
            Assert.Null(Loot.Equipment(0, Items, random));
        }

        [Fact]
        public void EquipmentReachesTwentyLevelsBelowTheMonster()
        {
            var random = new Random(5);
            var drops = Enumerable.Range(0, 200).Select(_ => Loot.Equipment(45, Items, random)).ToList();
            Assert.All(drops, d => Assert.Equal(410303u, d));
        }

        [Fact]
        public void LootIsTheKillersUntilItsTimeRunsOut()
        {
            var now = DateTime.UtcNow;
            var loot = new GroundItem { OwnerID = 1000001, OwnedUntil = now.AddSeconds(20) };
            Assert.True(loot.MayPickUp(1000001, now));
            Assert.False(loot.MayPickUp(1000002, now));
            Assert.True(loot.MayPickUp(1000002, now.AddSeconds(20)));
            Assert.True(new GroundItem().MayPickUp(1000002, now));
        }
    }

    public class MonsterTests
    {
        private static Monster New(uint life = 100)
        {
            var type = new MonsterType { ID = 1, Name = "Pheasant", Life = life, Lookface = 104, Level = 3 };
            var generator = new Generator { ID = 1, MapID = 1002, X = 10, Y = 10, Amount = 1, MonsterID = 1 };
            var monster = new Monster(Monster.FirstID, type, generator, null) { X = 11, Y = 12, Direction = 3 };
            monster.Revive();
            return monster;
        }

        [Fact]
        public void OnlyTheHitThatEmptiesItsHealthKillsIt()
        {
            var monster = New(100);
            var now = DateTime.UtcNow;
            Assert.Equal((60u, false), monster.Damage(60, 1000001, now));
            Assert.Equal((40u, true), monster.Damage(60, 1000002, now));
            Assert.Equal((0u, false), monster.Damage(60, 1000002, now));
            Assert.False(monster.Alive);
            Assert.Equal(now, monster.DiedAt);
        }

        [Fact]
        public void TheFirstAttackerBecomesTheTarget()
        {
            var monster = New();
            monster.Provoke(1000001);
            monster.Damage(1, 1000002, DateTime.UtcNow);
            Assert.Equal(1000001u, monster.Target);
        }

        [Fact]
        public void TheShippedMonstersAndSpawnsLoad()
        {
            var types = new MonsterTypeTable(MonsterTypeTable.Load(DataFiles.PathOf("monsters.json")));
            Assert.True(types.TryGet(1, out var pheasant));
            Assert.Equal(("Pheasant", 33u), (pheasant.Name, pheasant.Life));
            Assert.True(types.TryGet(900, out var guard) && guard.IsGuard);
            var spawns = Generator.Load(DataFiles.PathOf("spawns.json"), types);
            Assert.Contains(spawns, s => s.MapID == 1002 && s.MonsterID == 1);
            Assert.True(spawns.Sum(s => s.Amount) <= Monster.LastID - Monster.FirstID + 1);
        }

        [Fact]
        public void SpawnsOfUnknownMonstersAreRefused()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "{ \"spawns\": [ { \"id\": 1, \"map\": 1002, \"x\": 1, \"y\": 1, \"amount\": 1, \"monster\": 77 } ] }");
            try
            {
                var error = Assert.Throws<InvalidDataException>(() =>
                    Generator.Load(path, new MonsterTypeTable(new MonsterType[0])));
                Assert.Contains("unknown monsters: 1", error.Message);
            }
            finally { File.Delete(path); }
        }
    }

    public class InteractTests
    {
        [Fact]
        public void AttacksAreAtTheirOffsets()
        {
            var bytes = new MsgInteract(MsgInteract.InteractionType.Kill, 1000001, 400002, 820, 821, 1).Encode();
            Assert.Equal(40, bytes.Length);
            Assert.Equal(1022, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 8));
            Assert.Equal(400002u, BitConverter.ToUInt32(bytes, 12));
            Assert.Equal((820, 821), (BitConverter.ToUInt16(bytes, 16), BitConverter.ToUInt16(bytes, 18)));
            Assert.Equal(14u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 24));

        }

        [Fact]
        public void TheClientsAttacksAreRead()
        {
            var decoded = new MsgInteract();
            decoded.Decode(Comet.Client.InteractMessage.Build(Comet.Client.InteractMessage.Attack, 1000001, 400002, 820, 821));
            Assert.Equal((1000001u, 400002u, (ushort)820, (ushort)821, MsgInteract.InteractionType.Attack),
                (decoded.SenderID, decoded.TargetID, decoded.X, decoded.Y, decoded.Action));
        }

        [Fact]
        public void TheReviveEffectNamesItsTargets()
        {
            var effect = new MsgMagicEffect(1000001, 428, 378, MsgMagicEffect.Revive);
            effect.Targets.Add((1000001, 0));
            var bytes = effect.Encode();
            Assert.Equal(84, bytes.Length);
            Assert.Equal(1105, BitConverter.ToUInt16(bytes, 2));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((428, 378, 1050), (BitConverter.ToUInt16(bytes, 8), BitConverter.ToUInt16(bytes, 10), BitConverter.ToUInt16(bytes, 12)));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 16));
            Assert.Equal(1000001u, BitConverter.ToUInt32(bytes, 20));
            Assert.Equal(1u, BitConverter.ToUInt32(bytes, 28));
        }

        [Fact]
        public void GhostsWearTheGhostBody()
        {
            var character = new Character(new DbCharacter(1, 1000001, "Hero") { Mesh = 2001, Avatar = 5 });
            var life = new Life();
            Assert.Equal(52001u, life.Mesh(character));
            life.Dead = true;
            life.Ghost = true;
            Assert.Equal(99u * 10_000_000 + 52001, life.Mesh(character));
            Assert.Equal(Life.DeadStatus, life.Status);
        }

        [Theory]
        [InlineData(1000ul, 10, 10, 1000ul)]   // same level: all of it
        [InlineData(1000ul, 31, 10, 50ul)]     // over 20 levels above the monster: 5%
        [InlineData(100000ul, 10, 10, 3600ul)] // at most 360 a level
        [InlineData(1ul, 130, 1, 1ul)]         // at least 1
        public void TeammatesGetTheKillBonusByTheirOwnLevel(ulong bonus, int memberLevel, int monsterLevel, ulong gained) =>
            Assert.Equal(gained, Attacks.TeamExperience(bonus, memberLevel, monsterLevel));
    }
}

namespace Comet.Game.Magic
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;

    // Who may learn a spell, from a class master or a book. A spell's first level needs the
    // character level magictype gives it at level 0 (Fire Ring 55, Nectar 94); on top of that
    // a spell may belong to class lines (Promotion.Line), to water or fire taoists, and build
    // on another spell's level.
    public static class Learning
    {
        public enum Element : byte { Any, Water, Fire }

        // Level, when not 0, is the character level the client's help gives for learning it, where
        // magictype's is that for the next level (Intensify 71, not 83)
        public sealed record Rule(ushort MagicType, int[] Lines, Element Element = Element.Any,
            bool FromMaster = true, ushort Prerequisite = 0, ushort PrerequisiteLevel = 0, byte Level = 0)
        {
            public bool AnyLine => this.Lines.Length == 0;
        }

        public const int Trojan = 1, Warrior = 2, Archer = 4, Ninja = 5, Monk = 6, Pirate = 7, Taoist = 10;
        private static readonly int[] AnyLine = System.Array.Empty<int>();

        public const ushort Thunder = 1000, Fire = 1001, Tornado = 1002, Cure = 1005, Lightning = 1010,
            Accuracy = 1015, Shield = 1020, Superman = 1025, Roar = 1040, FastBlade = 1045, ScentSword = 1046,
            Revive = 1050, HealingRain = 1055, StarOfAccuracy = 1085, MagicShield = 1090, Stigma = 1095,
            Pray = 1100, Cyclone = 1110, Hercules = 1115, FireCircle = 1120, Volcano = 1125, FireRing = 1150,
            Bomb = 1160, FireOfHell = 1165, Nectar = 1170, AdvancedCure = 1175, FireMeteor = 1180,
            SpiritHealing = 1190, Meditation = 1195, WideStrike = 1250, SpeedGun = 1260, Penetration = 1290,
            Halt = 1300, FlyingMoon = 1320, DivineHare = 1350, NightDevil = 1360, SpeedLightning = 5001,
            Snow = 5010, StrandedMonster = 5020, Phoenix = 5030, Boom = 5040, Boreas = 5050,
            TwofoldBlades = 6000, ToxicFog = 6001, ArcherBane = 6004, Invisibility = 1075, Fly = 8002, Seizer = 7000, Earthquake = 7010, Rage = 7020, Celestial = 7030,
            Roamer = 7040, RapidFire = 8000, ScatterFire = 8001, ArrowRain = 8030, DragonTail = 11000,
            ViperFang = 11005, EagleEye = 11030, RadiantPalm = 10381, Oblivion = 10390, TyrantAura = 10395,
            Serenity = 10400, SoulShackle = 10405, FendAura = 10410, WhirlwindKick = 10415, MetalAura = 10420,
            WoodAura = 10421, WaterAura = 10422, FireAura = 10423, EarthAura = 10424, Tranquility = 10425,
            Compassion = 10430, TripleAttack = 10490, ScurvyBomb = 11040, CannonBarrage = 11050, BlackbeardsRage = 11060,
            GaleBomb = 11070, KrakensRevenge = 11100, BladeTempest = 11110, BlackSpot = 11120, AdrenalineRush = 11130,
            Windstorm = 11140, Intensify = 9000, ShurikenVortex = 6010, FatalStrike = 6011, PoisonStar = 6002, GapingWounds = 11230, CounterKill = 6003;

        // In the order masters list them
        private static readonly Rule[] Rules =
        {
            new Rule(Cyclone, new[] { Trojan }),
            new Rule(Accuracy, new[] { Trojan, Warrior }),
            new Rule(Hercules, new[] { Trojan }),
            new Rule(SpiritHealing, new[] { Trojan }),
            new Rule(Superman, new[] { Warrior }),
            new Rule(Shield, new[] { Warrior }),
            new Rule(Roar, new[] { Warrior }),
            new Rule(FlyingMoon, new[] { Warrior }, FromMaster: false),
            new Rule(ScatterFire, new[] { Archer }),
            new Rule(RapidFire, new[] { Archer }),
            new Rule(ArrowRain, new[] { Archer }),
            new Rule(Fly, new[] { Archer }),
            new Rule(Intensify, new[] { Archer }, Level: 71),
            new Rule(TwofoldBlades, new[] { Ninja }),
            new Rule(ArcherBane, new[] { Ninja }),
            new Rule(ToxicFog, new[] { Ninja }),
            new Rule(ShurikenVortex, new[] { Ninja }),
            new Rule(FatalStrike, new[] { Ninja }),
            new Rule(PoisonStar, new[] { Ninja }),
            new Rule(GapingWounds, new[] { Ninja }),
            new Rule(CounterKill, new[] { Ninja }),
            new Rule(TripleAttack, new[] { Monk }),
            new Rule(Oblivion, new[] { Monk }),
            new Rule(WhirlwindKick, new[] { Monk }),
            new Rule(TyrantAura, new[] { Monk }),
            new Rule(FendAura, new[] { Monk }),
            new Rule(Serenity, new[] { Monk }),
            new Rule(RadiantPalm, new[] { Monk }),
            new Rule(Tranquility, new[] { Monk }),
            new Rule(SoulShackle, new[] { Monk }),
            new Rule(Compassion, new[] { Monk }),
            new Rule(MetalAura, new[] { Monk }),
            new Rule(WoodAura, new[] { Monk }),
            new Rule(WaterAura, new[] { Monk }),
            new Rule(FireAura, new[] { Monk }),
            new Rule(EarthAura, new[] { Monk }),
            new Rule(Windstorm, new[] { Pirate }),
            new Rule(BladeTempest, new[] { Pirate }),
            new Rule(CannonBarrage, new[] { Pirate }),
            new Rule(GaleBomb, new[] { Pirate }),
            new Rule(EagleEye, new[] { Pirate }),
            new Rule(ScurvyBomb, new[] { Pirate }),
            new Rule(BlackSpot, new[] { Pirate }),
            new Rule(AdrenalineRush, new[] { Pirate }),
            new Rule(BlackbeardsRage, new[] { Pirate }),
            new Rule(KrakensRevenge, new[] { Pirate }),
            new Rule(Meditation, new[] { Taoist }),
            new Rule(Thunder, new[] { Taoist }, FromMaster: false),
            new Rule(Cure, new[] { Taoist }, FromMaster: false),
            new Rule(Lightning, new[] { Taoist }, FromMaster: false),
            new Rule(Fire, new[] { Taoist }, FromMaster: false),
            new Rule(SpeedLightning, new[] { Taoist }, FromMaster: false),
            new Rule(Revive, new[] { Taoist }, Element.Water),
            new Rule(HealingRain, new[] { Taoist }, Element.Water),
            new Rule(StarOfAccuracy, new[] { Taoist }, Element.Water),
            new Rule(MagicShield, new[] { Taoist }, Element.Water),
            new Rule(Invisibility, new[] { Taoist }, Element.Water),
            new Rule(Stigma, new[] { Taoist }, Element.Water),
            new Rule(Pray, new[] { Taoist }, Element.Water),
            new Rule(AdvancedCure, new[] { Taoist }, Element.Water),
            new Rule(Nectar, new[] { Taoist }, Element.Water),
            new Rule(DivineHare, new[] { Taoist }, Element.Water, FromMaster: false),
            new Rule(Volcano, new[] { Taoist }, Element.Fire),
            new Rule(FireMeteor, new[] { Taoist }, Element.Fire),
            new Rule(FireRing, new[] { Taoist }, Element.Fire),
            new Rule(FireCircle, new[] { Taoist }, Element.Fire),
            new Rule(Bomb, new[] { Taoist }, Element.Fire),
            new Rule(FireOfHell, new[] { Taoist }, Element.Fire),
            new Rule(Tornado, new[] { Taoist }, Element.Fire, FromMaster: false, Prerequisite: Fire, PrerequisiteLevel: 3),
            new Rule(NightDevil, AnyLine, FromMaster: false),
            new Rule(FastBlade, AnyLine, FromMaster: false),
            new Rule(ScentSword, AnyLine, FromMaster: false),
            new Rule(WideStrike, AnyLine, FromMaster: false),
            new Rule(SpeedGun, AnyLine, FromMaster: false),
            new Rule(Penetration, AnyLine, FromMaster: false),
            new Rule(Halt, AnyLine, FromMaster: false),
            new Rule(Snow, AnyLine, FromMaster: false),
            new Rule(StrandedMonster, AnyLine, FromMaster: false),
            new Rule(Phoenix, AnyLine, FromMaster: false),
            new Rule(Boom, AnyLine, FromMaster: false),
            new Rule(Boreas, AnyLine, FromMaster: false),
            new Rule(Seizer, AnyLine, FromMaster: false),
            new Rule(Earthquake, AnyLine, FromMaster: false),
            new Rule(Rage, AnyLine, FromMaster: false),
            new Rule(Celestial, AnyLine, FromMaster: false),
            new Rule(Roamer, AnyLine, FromMaster: false),
            new Rule(DragonTail, AnyLine, FromMaster: false),
            new Rule(ViperFang, AnyLine, FromMaster: false),
        };

        private static readonly Dictionary<ushort, Rule> ByMagic = Rules.ToDictionary(r => r.MagicType);

        // The books of itemtype.dat, the Pedlar's among them, by the spell each teaches
        private static readonly Dictionary<uint, ushort> Books = new Dictionary<uint, ushort>
        {
            [725000] = Thunder, [725001] = Fire, [725002] = Tornado, [725003] = Cure, [725004] = Lightning,
            [725005] = FastBlade, [725010] = ScentSword, [725011] = WideStrike, [725012] = SpeedGun,
            [725013] = Penetration, [725014] = Halt, [725015] = DivineHare, [725016] = NightDevil,
            [725025] = FlyingMoon, [725026] = Snow, [725027] = StrandedMonster, [725028] = SpeedLightning,
            [725029] = Phoenix, [725030] = Boom, [725031] = Boreas, [725040] = Seizer, [725041] = Earthquake,
            [725042] = Rage, [725043] = Celestial, [725044] = Roamer, [721157] = DragonTail, [721158] = ViperFang,
        };

        public static ushort? BookOf(uint itemType) => Books.TryGetValue(itemType, out var magic) ? magic : null;

        public static IEnumerable<uint> AllBooks => Books.Keys;

        public static bool TryGetRule(ushort magicType, out Rule rule) => ByMagic.TryGetValue(magicType, out rule);

        // What a line's master teaches, in order
        public static IReadOnlyList<ushort> TaughtBy(int line) =>
            Rules.Where(r => r.FromMaster && r.Lines.Contains(line)).Select(r => r.MagicType).ToList();

        public static Element ElementOf(byte currentClass) => (currentClass / 10) switch
        {
            13 => Element.Water,
            14 => Element.Fire,
            _ => Element.Any,
        };

        // Why a character can't learn the spell, or null
        public static string Unmet(byte currentClass, byte level, Skills skills, MagicTypeTable magics, ushort magicType)
        {
            if (!ByMagic.TryGetValue(magicType, out var rule))
                return "no_rule";
            if (!magics.TryGet(magicType, 0, out var first))
                return "unknown_magic";
            if (skills.TryGetSpell(magicType, out _))
                return "learned";
            if (!rule.AnyLine && !rule.Lines.Contains(Promotion.Line(currentClass)))
                return "class";
            if (rule.Element != Element.Any && ElementOf(currentClass) != rule.Element)
                return "element";
            if (level < (rule.Level != 0 ? rule.Level : first.NeedLevel))
                return "level";
            if (rule.Prerequisite != 0 &&
                (!skills.TryGetSpell(rule.Prerequisite, out var before) || before.Level < rule.PrerequisiteLevel))
                return "prerequisite";
            return null;
        }

        public static string Unmet(Client client, ushort magicType) =>
            client.Character == null ? "no_character" :
            Unmet(client.Character.CurrentClass, client.Character.Level, client.Skills, Kernel.Magics, magicType);

        // What to tell the player a reason from Unmet means
        public static string Explain(string reason, ushort magicType)
        {
            string name = NameOf(magicType);
            TryGetRule(magicType, out var rule);
            return reason switch
            {
                "learned" => $"You have already learned {name}.",
                "class" => $"Only {string.Join(" and ", rule.Lines.Select(LineName))} can learn {name}.",
                "element" => $"Only {(rule.Element == Element.Water ? "water" : "fire")} taoists can learn {name}.",
                "level" => $"You need to be level {RequiredLevel(magicType)} to learn {name}.",
                "prerequisite" => $"You need {NameOf(rule.Prerequisite)} at level {rule.PrerequisiteLevel} to learn {name}.",
                _ => $"You can't learn {name}.",
            };
        }

        public static byte RequiredLevel(ushort magicType) =>
            TryGetRule(magicType, out var rule) && rule.Level != 0 ? rule.Level :
            Kernel.Magics.TryGet(magicType, 0, out var first) ? first.NeedLevel : (byte)0;

        public static string NameOf(ushort magicType) =>
            Kernel.Magics != null && Kernel.Magics.TryGet(magicType, 0, out var first) ? first.Name : $"magic {magicType}";

        private static string LineName(int line) => line switch
        {
            Trojan => "trojans",
            Warrior => "warriors",
            Archer => "archers",
            Ninja => "ninjas",
            Monk => "monks",
            Pirate => "pirates",
            Taoist => "taoists",
            _ => $"class line {line}",
        };

        // Learns the spell at level 0 if the character may; null when learned, or why not
        public static async Task<string> LearnAsync(Client client, ushort magicType, string source)
        {
            string reason = Unmet(client, magicType);
            if (reason == null && !await Casting.LearnAsync(client, magicType, 0, source))
                reason = "unknown_magic";
            if (reason != null)
                LogEvents.For(typeof(Learning), LogEvents.MagicLearnRejected).Information(
                    "{Name} can't learn magic {MagicType} from {Source}: {Reason}",
                    client.Character?.Name, magicType, source, reason);
            return reason;
        }
    }
}

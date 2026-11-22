namespace Comet.Game.Magic
{
    using System;
    using Comet.Game.States;

    // What statuses from spells do in a fight. A status keeps its spell's power: from 30000
    // on a percentage (Superman 31000 is 1000%, Shield 30300 is 300%).
    public static class Buffs
    {
        // Each kill during Superman or Cyclone adds this while less than the cap is left
        public static readonly TimeSpan KillExtension = TimeSpan.FromMilliseconds(700);
        public static readonly TimeSpan KillExtensionCap = TimeSpan.FromSeconds(25);

        // Superman against players: 1.9 times
        public const int SupermanAgainstPlayers = 190;

        public static UInt128 StatusOf(ushort magicType) => magicType switch
        {
            1015 => StatusFlags.Accuracy,
            1020 => StatusFlags.Shield,
            1085 => StatusFlags.Accuracy,
            1090 => StatusFlags.Shield,
            1025 => StatusFlags.Superman,
            1095 => StatusFlags.Stigma,
            1110 => StatusFlags.Cyclone,
            1075 => StatusFlags.Invisible,
            8002 or 8003 => StatusFlags.Fly,
            10405 => StatusFlags.SoulShackle,
            10390 => StatusFlags.Oblivion,
            3080 => StatusFlags.Dodge,
            6002 => StatusFlags.NoPotion,
            5100 => StatusFlags.IronShirt,
            9000 => StatusFlags.Intensify,
            9876 => StatusFlags.Bless,
            30000 => StatusFlags.AzureShield,
            _ => 0,
        };

        public static UInt128 AuraOf(ushort magicType) => magicType switch
        {
            10395 => StatusFlags.TyrantAura,
            10410 => StatusFlags.FendAura,
            10420 => StatusFlags.MetalAura,
            10421 => StatusFlags.WoodAura,
            10422 => StatusFlags.WaterAura,
            10423 => StatusFlags.FireAura,
            10424 => StatusFlags.EarthAura,
            _ => UInt128.Zero,
        };

        // The flag for magictype's status number, for spells that take a status away
        public static ulong FlagOf(int status) => status switch
        {
            22 => StatusFlags.Fly,
            _ => 0,
        };

        // A flying player can't be reached by melee, from players or monsters within this range
        public const int MeleeRange = 2;

        public static bool Shackled(TimedStatus effects, DateTime now) => effects.Has(StatusFlags.SoulShackle, now);

        public static bool Flying(TimedStatus effects, DateTime now) => effects.Has(StatusFlags.Fly, now);

        public static bool HasAccuracy(TimedStatus effects, DateTime now) =>
            effects.Has(StatusFlags.Accuracy, now);

        // As the client describes Oblivion: 200% damage, kills worth half again their experience,
        // over at the 32nd kill
        public const int OblivionDamage = 2;
        public const int OblivionKills = 32;

        public static uint AgainstMonster(TimedStatus effects, uint damage, DateTime now)
        {
            damage = Scale(damage, effects.Power(StatusFlags.Stigma, now));
            damage = Scale(damage, effects.Power(StatusFlags.Intensify, now));
            if (effects.Has(StatusFlags.Oblivion, now))
                damage = (uint)Math.Min(uint.MaxValue, (ulong)damage * OblivionDamage);
            return Scale(damage, effects.Power(StatusFlags.Superman, now));
        }

        public static int AgainstPlayer(TimedStatus effects, int attack, DateTime now)
        {
            attack = (int)Scale((uint)Math.Max(0, attack), effects.Power(StatusFlags.Stigma, now));
            attack = (int)Scale((uint)attack, effects.Power(StatusFlags.Intensify, now));
            if (effects.Has(StatusFlags.Oblivion, now))
                attack *= OblivionDamage;
            return effects.Has(StatusFlags.Superman, now) ? attack * SupermanAgainstPlayers / 100 : attack;
        }

        public static ulong KillExperience(TimedStatus effects, ulong experience, DateTime now) =>
            effects.Has(StatusFlags.Oblivion, now) ? experience + experience / 2 : experience;

        public static int Defense(TimedStatus effects, int defense, DateTime now) =>
            (int)Scale(Scale((uint)Math.Max(0, defense), effects.Power(StatusFlags.Shield, now)),
                effects.Power(StatusFlags.IronShirt, now));

        public static int Dodge(TimedStatus effects, int dodge, DateTime now) =>
            (int)Scale((uint)Math.Max(0, dodge), effects.Power(StatusFlags.Dodge, now));

        // Azure Shield takes damage until it has taken as much as its power; it ends then
        public static uint Absorb(TimedStatus effects, uint damage, DateTime now)
        {
            if (damage == 0 || effects.Power(StatusFlags.AzureShield, now) is not int left || left <= 0)
                return damage;
            uint taken = Math.Min(damage, (uint)left);
            if (taken == (uint)left)
                effects.Remove(StatusFlags.AzureShield);
            else
                effects.SetPower(StatusFlags.AzureShield, left - (int)taken);
            return damage - taken;
        }

        // The kill's data for MsgInteract: 1, with the KO count in the high half during
        // Superman, Cyclone or Oblivion (the beads that change color)
        public static uint CountKill(Client client, DateTime now)
        {
            bool superman = client.Effects.Extend(StatusFlags.Superman, KillExtension, KillExtensionCap, now) ||
                client.Effects.Has(StatusFlags.Superman, now);
            bool cyclone = client.Effects.Extend(StatusFlags.Cyclone, KillExtension, KillExtensionCap, now) ||
                client.Effects.Has(StatusFlags.Cyclone, now);
            bool oblivion = client.Effects.Has(StatusFlags.Oblivion, now);
            if (!superman && !cyclone && !oblivion)
                return 1;
            client.Energy.KoCount++;
            return (client.Energy.KoCount << 16) | 1;
        }

        // Ends Oblivion at its last kill; true if it did
        public static bool EndOblivion(Client client, DateTime now)
        {
            if (!client.Effects.Has(StatusFlags.Oblivion, now) || client.Energy.KoCount < OblivionKills)
                return false;
            client.Effects.Remove(StatusFlags.Oblivion);
            client.Energy.KoCount = 0;
            return true;
        }

        private static uint Scale(uint value, int? power) =>
            power is int p && p > MagicType.PercentBase ? (uint)((ulong)value * (uint)(p - MagicType.PercentBase) / 100) : value;
    }
}

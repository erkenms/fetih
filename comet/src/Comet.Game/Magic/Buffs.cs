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

        public static ulong StatusOf(ushort magicType) => magicType switch
        {
            1015 => StatusFlags.Accuracy,
            1020 => StatusFlags.Shield,
            1025 => StatusFlags.Superman,
            1095 => StatusFlags.Stigma,
            1110 => StatusFlags.Cyclone,
            _ => 0,
        };

        public static bool HasAccuracy(TimedStatus effects, DateTime now) =>
            effects.Has(StatusFlags.Accuracy, now);

        public static uint AgainstMonster(TimedStatus effects, uint damage, DateTime now)
        {
            damage = Scale(damage, effects.Power(StatusFlags.Stigma, now));
            return Scale(damage, effects.Power(StatusFlags.Superman, now));
        }

        public static int AgainstPlayer(TimedStatus effects, int attack, DateTime now)
        {
            attack = (int)Scale((uint)Math.Max(0, attack), effects.Power(StatusFlags.Stigma, now));
            return effects.Has(StatusFlags.Superman, now) ? attack * SupermanAgainstPlayers / 100 : attack;
        }

        public static int Defense(TimedStatus effects, int defense, DateTime now) =>
            (int)Scale((uint)Math.Max(0, defense), effects.Power(StatusFlags.Shield, now));

        // The kill's data for MsgInteract: 1, with the KO count in the high half during
        // Superman or Cyclone
        public static uint CountKill(Client client, DateTime now)
        {
            bool superman = client.Effects.Extend(StatusFlags.Superman, KillExtension, KillExtensionCap, now) ||
                client.Effects.Has(StatusFlags.Superman, now);
            bool cyclone = client.Effects.Extend(StatusFlags.Cyclone, KillExtension, KillExtensionCap, now) ||
                client.Effects.Has(StatusFlags.Cyclone, now);
            if (!superman && !cyclone)
                return 1;
            client.Energy.KoCount++;
            return (client.Energy.KoCount << 16) | 1;
        }

        private static uint Scale(uint value, int? power) =>
            power is int p && p > MagicType.PercentBase ? (uint)((ulong)value * (uint)(p - MagicType.PercentBase) / 100) : value;
    }
}

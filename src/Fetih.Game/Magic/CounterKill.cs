namespace Fetih.Game.Magic
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Combat;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgInteract;

    // Counter-Kill, while switched on: a melee hit has the spell's percent to fall on a
    // scapegoat instead, and the ninja hits back with the power's percent of a normal
    // attack, as the client describes it. The client shows the switch from MsgInteract 44
    // (data 1 on, 0 off) and the hit back from MsgInteract 43.
    public static class CounterKill
    {
        public const ushort Type = 6003;

        // On the ninja's queue; true if the scapegoat took the player's hit
        public static async Task<bool> TryAsync(Client ninja, Client attacker, DateTime now)
        {
            if (!Strikes(ninja, out var spell) || attacker.Character == null)
                return false;
            var attackerStats = Stats.Of(attacker);
            int attack = Damage.Roll(Stats.Of(ninja).MinDamage, Stats.Of(ninja).MaxDamage, Random.Shared) * spell.PowerPercent / 100;
            uint damage = attackerStats.Taken(Damage.PlayerMelee(attack, Buffs.Defense(attacker.Effects, attackerStats.Defense, now)));
            var hit = new MsgInteract(InteractionType.CounterKill, ninja.ID, attacker.ID,
                attacker.Character.X, attacker.Character.Y, damage);
            await ninja.SendAsync(hit);
            await Movement.SendToViewAsync(ninja, hit);
            Kernel.Post(attacker, () => Attacks.DamagePlayerAsync(ninja, attacker, damage, "counter_kill"));
            Log(ninja, attacker.Character.Name, damage);
            return true;
        }

        // On the ninja's queue, from the monster loop's hit
        public static async Task<bool> TryAsync(Client ninja, Monster monster, DateTime now)
        {
            if (!Strikes(ninja, out var spell) || !Kernel.Monsters.TryGet(ninja.Character.MapID, out var map) ||
                !map.TryGetPosition(monster, out var x, out var y))
                return false;
            var stats = Stats.Of(ninja);
            var type = monster.Type;
            uint damage = Math.Max(1, Damage.Melee(stats.MinDamage, stats.MaxDamage, ninja.Character.Level, type.Level,
                type.Defense, Random.Shared) * (uint)spell.PowerPercent / 100);
            var (dealt, killed) = monster.Damage(damage, ninja.ID, now);
            await Attacks.SendAroundAsync(ninja, monster, new MsgInteract(InteractionType.CounterKill, ninja.ID, monster.Identity, x, y, damage));
            if (dealt > 0 || killed)
                await Attacks.AfterMonsterHitAsync(ninja, monster, x, y, dealt, killed);
            Log(ninja, type.Name, damage);
            return true;
        }

        private static bool Strikes(Client ninja, out MagicType spell)
        {
            spell = null;
            return ninja.CounterKill && !ninja.Life.Dead && Pirates.TryGetLearned(ninja, Type, out spell) &&
                Random.Shared.Next(100) < spell.Percent;
        }

        private static void Log(Client ninja, string attacker, uint damage) =>
            LogEvents.For(typeof(CounterKill), LogEvents.CombatHit).Debug(
                "{Name}'s scapegoat took {Attacker}'s hit; {Name} hit back for {Damage}",
                ninja.Character.Name, attacker, ninja.Character.Name, damage);
    }
}

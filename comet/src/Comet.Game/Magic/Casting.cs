namespace Comet.Game.Magic
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Combat;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // Spells, cast with MsgInteract 24 on the caster's queue. Spells that hurt (attack, bomb,
    // line, fan) hit monsters and the players the caster's PK mode allows; heals and status
    // spells go to a player in view or the caster. Damage to a player is applied on their
    // queue, as for melee.
    public static class Casting
    {
        private const int DistanceSlack = 1;

        // As many targets as one MsgMagicEffect shows
        private const int MaxTargets = 28;

        private const double FanHalfAngle = 45;

        public static TimeSpan MinimumDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        private static readonly HashSet<MagicSort> Supported = new HashSet<MagicSort>
        {
            MagicSort.Attack, MagicSort.Bomb, MagicSort.Line, MagicSort.Fan,
            MagicSort.Recruit, MagicSort.AttachStatus, MagicSort.Riding, MagicSort.AttackStatus,
            MagicSort.DetachStatus, MagicSort.AddMana, MagicSort.DispatchXp,
        };

        private sealed record Victim(Monster Monster, Client Player, ushort X, ushort Y)
        {
            public uint Damage { get; set; }
            public uint ID => this.Monster?.Identity ?? this.Player.ID;
        }

        public static async Task CastAsync(Client client, ushort magicType, uint targetID, ushort x, ushort y)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            SkillLevel learned = null;
            MagicType spell = null;
            string reason =
                character == null ? "no_character" :
                client.Life.Dead ? "dead" :
                !client.Skills.TryGetSpell(magicType, out learned) ? "not_learned" :
                !Kernel.Magics.TryGet(magicType, learned.Level, out spell) ? "unknown_magic" :
                null;
            // Getting off costs nothing
            if (reason == null && spell.Sort == MagicSort.Riding && client.Mount.Riding)
            {
                await Riding.DismountAsync(client, "magic");
                return;
            }
            reason ??= Problem(client, spell, now);
            if (reason != null)
            {
                Reject(client, magicType, reason);
                return;
            }

            Attacks.Stop(client);
            if (spell.Sort != MagicSort.Riding)
                await Riding.DismountToFightAsync(client, "magic");
            switch (spell.Sort)
            {
                case MagicSort.Riding:
                    await RideAsync(client, spell, now);
                    break;
                case MagicSort.Recruit:
                    await HealAsync(client, spell, targetID, now);
                    break;
                case MagicSort.AttachStatus:
                    await AttachAsync(client, spell, targetID, now);
                    break;
                case MagicSort.DetachStatus:
                    await ReviveAsync(client, spell, targetID, now);
                    break;
                case MagicSort.AddMana:
                    await MeditateAsync(client, spell, now);
                    break;
                case MagicSort.DispatchXp:
                    await RoarAsync(client, spell, now);
                    break;
                default:
                    await AttackAsync(client, spell, targetID, x, y, now);
                    break;
            }
        }

        private static string Problem(Client client, MagicType spell, DateTime now) =>
            !Supported.Contains(spell.Sort) ? "unsupported_sort" :
            spell.IsPassive ? "passive" :
            now < client.NextCastAt ? "too_soon" :
            spell.IsXpSkill && !client.Effects.Has(StatusFlags.XpFull, now) ? "xp_not_full" :
            client.Character.ManaPoints < spell.UseMana ? "no_mana" :
            client.Energy.Stamina < spell.UseStamina ? "no_stamina" :
            spell.WeaponSubtype != 0 && !Holds(client, spell.WeaponSubtype) ? "wrong_weapon" :
            spell.Sort == MagicSort.Riding ? Riding.Problem(client) :
            null;

        private static bool Holds(Client client, uint weaponSubtype) =>
            new[] { ItemPosition.RightHand, ItemPosition.LeftHand }
                .Select(p => client.Equipment?[p])
                .Any(item => item != null && Proficiency.WeaponType(item.TypeID) == weaponSubtype);

        // A learned weapon skill for a weapon in hand may go off instead of a melee swing, at
        // its percent; false if none did
        public static async Task<bool> TryPassiveAsync(Client client, uint targetID, ushort x, ushort y, DateTime now)
        {
            foreach (var (type, learned) in client.Skills.AllSpells())
            {
                if (!Kernel.Magics.TryGet(type, learned.Level, out var spell) || !spell.IsPassive ||
                    !spell.IsPercent || !IsAttack(spell.Sort) || spell.WeaponSubtype == 0 ||
                    !Holds(client, spell.WeaponSubtype) || Random.Shared.Next(100) >= spell.Percent)
                    continue;
                return await AttackAsync(client, spell, targetID, x, y, now, passive: true);
            }
            return false;
        }

        private static bool IsAttack(MagicSort sort) => sort is MagicSort.Attack or MagicSort.AttackStatus or
            MagicSort.Bomb or MagicSort.Line or MagicSort.Fan;

        private static async Task<bool> AttackAsync(Client client, MagicType spell, uint targetID, ushort x, ushort y,
            DateTime now, bool passive = false)
        {
            var character = client.Character;
            List<Victim> victims;
            if (spell.Sort is MagicSort.Attack or MagicSort.AttackStatus)
            {
                var (victim, reason) = FindTarget(client, spell, targetID, now);
                if (victim == null)
                {
                    Reject(client, spell.Type, reason);
                    return false;
                }
                victims = new List<Victim> { victim };
                (x, y) = (victim.X, victim.Y);
            }
            else
            {
                victims = Around(client, now)
                    .Where(v => InShape(spell, character.X, character.Y, x, y, v.X, v.Y))
                    .Take(MaxTargets)
                    .ToList();
            }

            var stats = Stats.Of(client);
            foreach (var victim in victims)
                victim.Damage = victim.Monster != null
                    ? AgainstMonster(client, stats, spell, victim.Monster, now)
                    : AgainstPlayer(client, stats, spell, victim.Player, now);

            await PayAsync(client, spell, now, passive);
            await Durability.WearOnAttackAsync(client);
            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            foreach (var victim in victims)
                effect.Targets.Add((victim.ID, victim.Damage));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);

            uint experience = 0;
            foreach (var victim in victims)
            {
                if (victim.Player != null)
                {
                    var target = victim.Player;
                    await Pking.OnAttackAsync(client, target);
                    Kernel.Post(target, () => Attacks.DamagePlayerAsync(client, target, victim.Damage, "magic"));
                    continue;
                }

                var monster = victim.Monster;
                if (monster.IsGuard)
                    await Pking.FlashAsync(client, Pking.FlashTime, "attack_guard");
                if (victim.Damage == 0)
                    monster.Provoke(client.ID);
                var (dealt, killed) = victim.Damage > 0 ? monster.Damage(victim.Damage, client.ID, now) : (0u, false);
                if (victim.Damage > 0 && dealt == 0 && !killed)
                    continue;
                if (!monster.IsGuard)
                    experience += dealt;
                await Attacks.AfterMonsterHitAsync(client, monster, victim.X, victim.Y, dealt, killed);
            }

            LogCast(client, spell, x, y, victims.Count);
            await GainExperienceAsync(client, spell, experience);
            return true;
        }

        private static (Victim Victim, string Reason) FindTarget(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var character = client.Character;
            Victim victim = null;
            string reason;
            if (Attacks.IsMonster(targetID))
            {
                Monster monster = null;
                ushort mx = 0, my = 0;
                reason =
                    !Kernel.Monsters.TryGet(character.MapID, out var map) ||
                    !map.TryGet(targetID, out monster, out mx, out my) ? "not_found" :
                    !monster.Alive ? "dead_target" :
                    monster.IsGuard && client.Pk.Mode != PkMode.Free ? "guard_not_free_pk" :
                    null;
                if (reason == null)
                    victim = new Victim(monster, null, mx, my);
            }
            else
            {
                Client target = null;
                ushort px = 0, py = 0;
                reason =
                    !Kernel.Players.TryGet(targetID, out target) || target.Character == null ||
                    target.Map != client.Map || !target.Map.TryGetPosition(target, out px, out py) ? "not_found" :
                    target.Life.Dead ? "dead_target" :
                    now < target.Life.ProtectedUntil ? "protected" :
                    Pking.IsPeaceful(character.MapID) ? "peaceful_map" :
                    !Pking.CanAttack(client, target) ? "pk_mode" :
                    null;
                if (reason == null)
                    victim = new Victim(null, target, px, py);
            }

            if (victim != null &&
                MonsterAI.Distance(character.X, character.Y, victim.X, victim.Y) > spell.Distance + DistanceSlack)
                return (null, "too_far");
            return (victim, reason);
        }

        // Guards only in free PK mode, so an area spell doesn't make the caster flash by accident
        private static IEnumerable<Victim> Around(Client client, DateTime now)
        {
            var character = client.Character;
            if (Kernel.Monsters != null && Kernel.Monsters.TryGet(character.MapID, out var monsters))
                foreach (var monster in monsters.InView(character.X, character.Y))
                    if (monster.Alive && (!monster.IsGuard || client.Pk.Mode == PkMode.Free) &&
                        monsters.TryGetPosition(monster, out var mx, out var my))
                        yield return new Victim(monster, null, mx, my);

            foreach (var player in client.Map.InView(character.X, character.Y, except: client))
                if (player.Character != null && !player.Life.Dead && now >= player.Life.ProtectedUntil &&
                    Pking.CanAttack(client, player) && client.Map.TryGetPosition(player, out var px, out var py))
                    yield return new Victim(null, player, px, py);
        }

        public static bool InShape(MagicType spell, int x, int y, int aimX, int aimY, int tx, int ty)
        {
            int range = (int)spell.Range;
            if (MonsterAI.Distance(x, y, tx, ty) > range || (tx == x && ty == y))
                return false;
            switch (spell.Sort)
            {
                case MagicSort.Bomb:
                    return true;
                case MagicSort.Line:
                {
                    int dx = aimX - x, dy = aimY - y;
                    int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (steps == 0)
                        return false;
                    for (int i = 1; i <= range; i++)
                        if (x + (int)Math.Round((double)dx * i / steps) == tx &&
                            y + (int)Math.Round((double)dy * i / steps) == ty)
                            return true;
                    return false;
                }
                case MagicSort.Fan:
                {
                    if (aimX == x && aimY == y)
                        return false;
                    double aim = Math.Atan2(aimY - y, aimX - x);
                    double target = Math.Atan2(ty - y, tx - x);
                    double degrees = Math.Abs(Math.IEEERemainder(target - aim, 2 * Math.PI)) * 180 / Math.PI;
                    return degrees <= FanHalfAngle;
                }
                default:
                    return false;
            }
        }

        // Spells with power from 30000 on hit like the weapon, by that percentage; others are
        // magic and miss when the spell's percent says so
        private static uint AgainstMonster(Client client, Stats stats, MagicType spell, Monster monster, DateTime now)
        {
            var type = monster.Type;
            int level = client.Character.Level;
            if (!spell.IsPercent)
                return Random.Shared.Next(100) < spell.Percent
                    ? Damage.Magic(stats.MagicDamage, spell.Power, level, type.Level) : 0;
            if (!Buffs.HasAccuracy(client.Effects, now) && !Damage.Hits(type.Dodge, targetIsMonster: true, Random.Shared))
                return 0;
            uint damage = stats.Bow
                ? Damage.Archery(stats.MinDamage, stats.MaxDamage, level, type.Level, type.Dodge, Random.Shared)
                : Damage.Melee(stats.MinDamage, stats.MaxDamage, level, type.Level, type.Defense, Random.Shared);
            return Buffs.AgainstMonster(client.Effects, (uint)Math.Max(1, damage * (ulong)spell.PowerPercent / 100), now);
        }

        private static uint AgainstPlayer(Client client, Stats stats, MagicType spell, Client target, DateTime now)
        {
            var targetStats = Stats.Of(target);
            if (!spell.IsPercent)
                return Random.Shared.Next(100) < spell.Percent
                    ? targetStats.Taken(Damage.PlayerMagic(stats.MagicDamage, spell.Power, targetStats.MagicDefense)) : 0;
            if (!Buffs.HasAccuracy(client.Effects, now) && !Damage.Hits(targetStats.Dodge, targetIsMonster: false, Random.Shared))
                return 0;
            int attack = Damage.Roll(stats.MinDamage, stats.MaxDamage, Random.Shared) * spell.PowerPercent / 100;
            attack = Buffs.AgainstPlayer(client.Effects, attack, now);
            return targetStats.Taken(stats.Bow
                ? Damage.PlayerArchery(attack, targetStats.Dodge)
                : Damage.PlayerMelee(attack, Buffs.Defense(target.Effects, targetStats.Defense, now)));
        }

        // Heals the caster if the target isn't a living player in reach; a spell for many
        // (Healing Rain) heals the caster and the living teammates in reach too
        private static async Task HealAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var target = Friendly(client, spell, targetID);
            var targets = new List<Client> { target };
            if (spell.Multi)
                targets = targets.Append(client).Concat(Teammates(client, spell.Distance)).Distinct().ToList();
            await PayAsync(client, spell, now);
            var character = target.Character;
            uint amount = (uint)Math.Max(0, spell.Power);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var healed in targets)
                effect.Targets.Add((healed.ID, amount));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            foreach (var healed in targets)
                Kernel.Post(healed, () => HealAsync(healed, amount));
            LogCast(client, spell, character.X, character.Y, targets.Count);
            await GainExperienceAsync(client, spell, amount);
        }

        private static async Task HealAsync(Client target, uint amount)
        {
            var healed = target.Character;
            if (healed == null || target.Life.Dead)
                return;
            healed.HealthPoints = (ushort)Math.Min(Attributes.MaxHealth(target), healed.HealthPoints + amount);
            await target.SendAsync(new MsgUserAttrib(target.ID, AttributeType.Health, healed.HealthPoints));
        }

        // Living teammates on the caster's map within the distance
        private static IEnumerable<Client> Teammates(Client client, uint distance)
        {
            var character = client.Character;
            foreach (var member in Social.Teams.OthersOf(client))
            {
                var other = member.Character;
                if (other != null && !member.Life.Dead && member.Map == client.Map &&
                    MonsterAI.Distance(character.X, character.Y, other.X, other.Y) <= distance + DistanceSlack)
                    yield return member;
            }
        }

        // Revive and Pray raise a dead player where they lie
        private static async Task ReviveAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var character = client.Character;
            Client target = null;
            string reason =
                targetID == client.ID || !Kernel.Players.TryGet(targetID, out target) || target.Character == null ||
                target.Map != client.Map ? "not_found" :
                !target.Life.Dead ? "alive_target" :
                MonsterAI.Distance(character.X, character.Y, target.Character.X, target.Character.Y) >
                    spell.Distance + DistanceSlack ? "too_far" :
                null;
            if (reason != null)
            {
                Reject(client, spell.Type, reason);
                return;
            }

            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, target.Character.X, target.Character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            Kernel.Post(target, () => Death.ReviveHereAsync(target, $"magic {spell.Type} from {character.Name}"));
            LogCast(client, spell, target.Character.X, target.Character.Y, 1);
        }

        // Meditation turns the caster's stamina into mana, the spell's power of it
        private static async Task MeditateAsync(Client client, MagicType spell, DateTime now)
        {
            var character = client.Character;
            ushort mana = (ushort)Math.Min(Attributes.MaxMana(client), character.ManaPoints + Math.Max(0, spell.Power));
            uint restored = (uint)(mana - character.ManaPoints);
            if (restored == 0)
            {
                Reject(client, spell.Type, "full_mana");
                return;
            }

            await PayAsync(client, spell, now);
            character.ManaPoints = mana;
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Mana, character.ManaPoints));
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, restored));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            LogCast(client, spell, character.X, character.Y, 1);
            await GainExperienceAsync(client, spell, restored);
        }

        // Roar fills the XP bars of the caster's teammates in view
        private static async Task RoarAsync(Client client, MagicType spell, DateTime now)
        {
            var character = client.Character;
            var members = Social.Teams.OthersOf(client)
                .Where(m => m.Character != null && !m.Life.Dead && m.Map == client.Map &&
                    GameMap<Client>.InView(character.X, character.Y, m.Character.X, m.Character.Y))
                .ToList();
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var member in members)
                effect.Targets.Add((member.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            foreach (var member in members)
                Kernel.Post(member, () => member.Character == null || member.Life.Dead
                    ? Task.CompletedTask
                    : Recovery.AddXpAsync(member, Energy.MaxXp, DateTime.UtcNow));
            LogCast(client, spell, character.X, character.Y, members.Count);
        }

        private static async Task AttachAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            ulong status = Buffs.StatusOf(spell.Type);
            if (status == 0)
            {
                Reject(client, spell.Type, "unknown_status");
                return;
            }

            var target = spell.IsXpSkill ? client : Friendly(client, spell, targetID);
            await PayAsync(client, spell, now);
            if (status == StatusFlags.Superman || status == StatusFlags.Cyclone)
                target.Energy.KoCount = 0;
            target.Effects.Set(status, now + TimeSpan.FromSeconds(spell.StepSeconds), now, spell.Power);

            var character = target.Character;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.SendStatusAsync(target);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                character.Name, status, spell.StepSeconds, client.Character.Name, spell.Name);
            LogCast(client, spell, character.X, character.Y, 1);
        }

        private static async Task RideAsync(Client client, MagicType spell, DateTime now)
        {
            await PayAsync(client, spell, now);
            var character = client.Character;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Riding.MountAsync(client);
            LogCast(client, spell, character.X, character.Y, 1);
        }

        private static Client Friendly(Client client, MagicType spell, uint targetID)
        {
            if (targetID == 0 || targetID == client.ID || !Kernel.Players.TryGet(targetID, out var target) ||
                target.Character == null || target.Life.Dead || target.Map != client.Map ||
                MonsterAI.Distance(client.Character.X, client.Character.Y, target.Character.X, target.Character.Y) >
                    spell.Distance + DistanceSlack)
                return client;
            return target;
        }

        // A passive skill costs nothing and doesn't delay the next cast
        private static async Task PayAsync(Client client, MagicType spell, DateTime now, bool passive = false)
        {
            if (passive)
                return;
            var character = client.Character;
            client.NextCastAt = now + TimeSpan.FromMilliseconds(Math.Max(MinimumDelay.TotalMilliseconds, spell.DelayMilliseconds));
            if (spell.UseMana > 0)
            {
                character.ManaPoints -= spell.UseMana;
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Mana, character.ManaPoints));
            }
            await Recovery.UseStaminaAsync(client, spell.UseStamina);
            if (spell.IsXpSkill)
            {
                client.Effects.Remove(StatusFlags.XpFull);
                await Recovery.SetXpAsync(client, 0);
                await Pking.SendStatusAsync(client);
            }
        }

        // Experience from damage or healing, toward the spell's next level once the character
        // is high enough
        public static async Task GainExperienceAsync(Client client, MagicType spell, uint amount)
        {
            if (amount == 0 || spell.NeedExperience == 0 || client.Character.Level < spell.NeedLevel ||
                !client.Skills.TryGetSpell(spell.Type, out var learned) || learned.Level != spell.Level)
                return;
            ulong experience = (ulong)learned.Experience + amount;
            ushort level = learned.Level;
            if (experience >= spell.NeedExperience && Kernel.Magics.TryGet(spell.Type, (ushort)(level + 1), out _))
            {
                experience -= spell.NeedExperience;
                level++;
            }
            await SetAsync(client, spell.Type, new SkillLevel(level, (uint)Math.Min(experience, uint.MaxValue)),
                learned.Level, "experience");
        }

        public static async Task SetAsync(Client client, ushort magicType, SkillLevel spell, ushort fromLevel, string reason)
        {
            client.Skills.SetSpell(magicType, spell);
            await client.SendAsync(new MsgMagicInfo(magicType, spell.Level, spell.Experience));
            if (spell.Level != fromLevel)
                LogEvents.For(typeof(Casting), LogEvents.MagicLevel).Information(
                    "{Name} magic {MagicType} went from level {FromLevel} to {Level} ({Reason})",
                    client.Character.Name, magicType, fromLevel, spell.Level, reason);
        }

        public static async Task<bool> LearnAsync(Client client, ushort magicType, ushort level, string reason)
        {
            if (client.Character == null || !Kernel.Magics.TryGet(magicType, level, out var spell))
                return false;
            client.Skills.SetSpell(magicType, new SkillLevel(level, 0));
            await client.SendAsync(new MsgMagicInfo(magicType, level, 0));
            LogEvents.For(typeof(Casting), LogEvents.MagicLearned).Information(
                "{Name} learned {MagicName} {MagicType} at level {Level} ({Reason})",
                client.Character.Name, spell.Name, magicType, level, reason);
            return true;
        }

        public static async Task SendAllAsync(Client client)
        {
            foreach (var (type, spell) in client.Skills.AllSpells())
                await client.SendAsync(new MsgMagicInfo(type, spell.Level, spell.Experience));
        }

        private static void LogCast(Client client, MagicType spell, int x, int y, int targets) =>
            LogEvents.For(typeof(Casting), LogEvents.MagicCast).Debug(
                "{Name} cast {MagicName} {MagicType} level {Level} at {X},{Y} on {TargetCount} targets",
                client.Character.Name, spell.Name, spell.Type, spell.Level, x, y, targets);

        private static void Reject(Client client, ushort magicType, string reason) =>
            LogEvents.For(typeof(Casting), LogEvents.MagicRejected).Warning(
                "{Name} can't cast {MagicType}: {Reason}", client.Character?.Name, magicType, reason);
    }
}

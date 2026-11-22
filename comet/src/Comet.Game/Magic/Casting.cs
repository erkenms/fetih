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
            MagicSort.DetachStatus, MagicSort.AddMana, MagicSort.DispatchXp, MagicSort.DecLife,
            MagicSort.GroundSting, MagicSort.RadiantPalm, MagicSort.WhirlwindKick, MagicSort.TripleAttack,
            MagicSort.Aura, MagicSort.Serenity, MagicSort.Tranquility, MagicSort.Compassion, MagicSort.Oblivion,
            MagicSort.ScurvyBomb, MagicSort.GaleBomb, MagicSort.BladeTempest,
            MagicSort.KrakensRevenge, MagicSort.CannonBarrage, MagicSort.BlackbeardsRage,
        };

        // Serenity, Tranquility and Compassion, as the client describes them
        public static readonly TimeSpan DispelInterval = TimeSpan.FromSeconds(3);

        // Scurvy Bomb's fatigue: how much more stamina each of its levels makes spells cost,
        // as its description in MagicEffect.ini has it
        private static readonly int[] FatigueStamina = { 5, 6, 6, 8, 8 };

        // As many as Gale Bomb's description says it hits, and Kraken's Revenge at each level
        private const int GaleBombTargets = 3;
        private static readonly int[] KrakenTargets = { 3, 3, 4, 4, 5 };

        // What Cannon Barrage and Blackbeard's Rage do to players, by percent of the attack, as
        // their descriptions have it; magictype's power is for monsters
        private const int CannonAgainstPlayers = 10;
        private static readonly int[] BlackbeardAgainstPlayers = { 20, 22, 24, 27, 30 };

        private sealed record Victim(Monster Monster, Client Player, ushort X, ushort Y)
        {
            public uint Damage { get; set; }
            public List<uint> Hits { get; } = new List<uint>();
            public bool Missed { get; set; }
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
                case MagicSort.AttachStatus when Buffs.StatusOf(spell.Type) == StatusFlags.SoulShackle:
                    await ShackleAsync(client, spell, targetID, now);
                    break;
                case MagicSort.AttachStatus:
                case MagicSort.Oblivion:
                    await AttachAsync(client, spell, targetID, now);
                    break;
                case MagicSort.DetachStatus when spell.Status > 0:
                    await TakeStatusAsync(client, spell, targetID, now);
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
                case MagicSort.GroundSting:
                    await FogAsync(client, spell, x, y, now);
                    break;
                case MagicSort.Aura:
                    await AuraAsync(client, spell, now);
                    break;
                case MagicSort.Serenity:
                    await DispelAsync(client, spell, new List<Client> { client }, now);
                    break;
                case MagicSort.Tranquility:
                    await DispelAsync(client, spell, new List<Client> { Anyone(client, spell, targetID) }, now);
                    break;
                case MagicSort.Compassion:
                    await DispelAsync(client, spell, Teammates(client, spell.Range).Prepend(client).ToList(), now);
                    break;
                case MagicSort.KrakensRevenge:
                    await KrakenAsync(client, spell, targetID, now);
                    break;
                case MagicSort.CannonBarrage or MagicSort.BlackbeardsRage when !InXpStatus(client, spell, now):
                    await StartXpStatusAsync(client, spell, now);
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
            spell.IsXpSkill && !client.Effects.Has(StatusFlags.XpFull, now) && !InXpStatus(client, spell, now) ? "xp_not_full" :
            client.Character.ManaPoints < spell.UseMana ? "no_mana" :
            client.Energy.Stamina < StaminaOf(client, spell, now) ? "no_stamina" :
            spell.WeaponSubtype != 0 && !Holds(client, spell.WeaponSubtype) ? "wrong_weapon" :
            spell.UseItem != 0 && ItemToUse(client, spell) == null ? "no_item" :
            spell.Sort == MagicSort.Riding ? Riding.Problem(client) :
            null;

        // The left hand's item a spell takes from, if it holds enough
        private static Database.Models.DbItem ItemToUse(Client client, MagicType spell)
        {
            var item = client.Equipment?[ItemPosition.LeftHand];
            return item != null && item.TypeID / 1000 % 1000 == spell.UseItem && item.Amount >= spell.UseItemCount
                ? item : null;
        }

        public static bool Holds(Client client, uint weaponSubtype) =>
            new[] { ItemPosition.RightHand, ItemPosition.LeftHand }
                .Select(p => client.Equipment?[p])
                .Any(item => item != null && Proficiency.WeaponType(item.TypeID) == weaponSubtype);

        // A learned weapon skill for a weapon in hand may go off instead of a melee swing, at
        // its percent; false if none did
        public static async Task<bool> TryPassiveAsync(Client client, uint targetID, ushort x, ushort y, DateTime now)
        {
            if (await TryWindstormAsync(client, targetID, x, y, now))
                return true;
            foreach (var (type, learned) in client.Skills.AllSpells())
            {
                // Windstorm shares Triple Attack's sort, 55, and went off above if at all
                if (type == Pirates.Windstorm || !Kernel.Magics.TryGet(type, learned.Level, out var spell) || !spell.IsPassive ||
                    spell.WeaponSubtype == 0 || !Holds(client, spell.WeaponSubtype))
                    continue;
                if (spell.Sort == MagicSort.TripleAttack)
                {
                    if (Random.Shared.Next(TripleAttackOdds(targetID)) >= spell.Percent)
                        continue;
                    var swing = spell with { Power = MagicType.PercentBase + 100 };
                    return await AttackAsync(client, swing, targetID, x, y, now, passive: true, hits: 3);
                }
                if (!spell.IsPercent || !IsAttack(spell.Sort) || Random.Shared.Next(100) >= spell.Percent)
                    continue;
                return await AttackAsync(client, spell, targetID, x, y, now, passive: true);
            }
            return false;
        }

        // The client's description: Triple Attack's percent against monsters, a third of it
        // against players
        private static int TripleAttackOdds(uint targetID) => Attacks.IsMonster(targetID) ? 100 : 300;

        // Windstorm: at its percent, a third against players, a rapier swing is a combo of three
        // hits, shown as the skill
        private const int WindstormHits = 3, WindstormAgainstPlayersPercent = 33;

        private static async Task<bool> TryWindstormAsync(Client client, uint targetID, ushort x, ushort y, DateTime now)
        {
            if (!Pirates.TryGetLearned(client, Pirates.Windstorm, out var spell) ||
                spell.WeaponSubtype != 0 && !Holds(client, spell.WeaponSubtype) ||
                Random.Shared.Next(100) >= (Attacks.IsMonster(targetID) ? spell.Percent : WindstormAgainstPlayersPercent))
                return false;
            // The swing checked the reach
            var combo = spell with
            {
                Sort = MagicSort.Attack,
                Power = MagicType.PercentBase + 100 * WindstormHits,
                Distance = ushort.MaxValue,
            };
            return await AttackAsync(client, combo, targetID, x, y, now, passive: true);
        }

        private static bool IsAttack(MagicSort sort) => sort is MagicSort.Attack or MagicSort.AttackStatus or MagicSort.DecLife or
            MagicSort.Bomb or MagicSort.Line or MagicSort.Fan or MagicSort.RadiantPalm or MagicSort.WhirlwindKick or MagicSort.BladeTempest ||
            AtCell(sort);

        // Spells that hit around the cell aimed at, within their distance of the caster
        private static bool AtCell(MagicSort sort) => sort is MagicSort.ScurvyBomb or MagicSort.GaleBomb or MagicSort.CannonBarrage;

        // The status of an XP skill whose first cast only starts it: while it's on, the client
        // casts the skill for each attack
        private static UInt128 XpStatusOf(MagicSort sort) => sort switch
        {
            MagicSort.CannonBarrage => StatusFlags.CannonBarrage,
            MagicSort.BlackbeardsRage => StatusFlags.BlackbeardsRage,
            _ => 0,
        };

        private static bool InXpStatus(Client client, MagicType spell, DateTime now) =>
            XpStatusOf(spell.Sort) is var flag && flag != 0 && client.Effects.Has(flag, now);

        // A spell that costs stamina costs more during Scurvy Bomb's fatigue
        private static byte StaminaOf(Client client, MagicType spell, DateTime now) =>
            spell.UseStamina == 0 ? (byte)0 :
            (byte)Math.Min(byte.MaxValue, spell.UseStamina + (client.Effects.Power(StatusFlags.Fatigue, now) ?? 0));

        private static async Task<bool> AttackAsync(Client client, MagicType spell, uint targetID, ushort x, ushort y,
            DateTime now, bool passive = false, int hits = 1)
        {
            var character = client.Character;
            List<Victim> victims;
            if (spell.Sort == MagicSort.BlackbeardsRage)
            {
                // The target and the nearest around it, as many as the spell's active times
                var (target, reason) = FindTarget(client, spell, targetID, now);
                if (target == null)
                {
                    Reject(client, spell.Type, reason);
                    return false;
                }
                victims = Around(client, now)
                    .Where(v => v.ID != target.ID && MonsterAI.Distance(target.X, target.Y, v.X, v.Y) <= spell.Range)
                    .OrderBy(v => MonsterAI.Distance(target.X, target.Y, v.X, v.Y))
                    .Prepend(target)
                    .Take((int)Math.Max(1, spell.ActiveTimes))
                    .ToList();
                (x, y) = (target.X, target.Y);
            }
            else if (spell.Sort is MagicSort.Attack or MagicSort.AttackStatus or MagicSort.DecLife or MagicSort.TripleAttack)
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
            else if (AtCell(spell.Sort))
            {
                if (MonsterAI.Distance(character.X, character.Y, x, y) > spell.Distance + DistanceSlack)
                {
                    Reject(client, spell.Type, "too_far");
                    return false;
                }
                victims = Around(client, now)
                    .Where(v => MonsterAI.Distance(x, y, v.X, v.Y) <= spell.Range)
                    .OrderBy(v => MonsterAI.Distance(x, y, v.X, v.Y))
                    .Take(spell.Sort == MagicSort.GaleBomb ? GaleBombTargets : MaxTargets)
                    .ToList();
            }
            else if (spell.Sort == MagicSort.BladeTempest)
            {
                // The client sends the cell it dashes to, no farther than the distance
                string reason = MonsterAI.Distance(character.X, character.Y, x, y) > spell.Distance + DistanceSlack
                    ? "too_far" : Movement.CheckDash(client, x, y);
                if (reason != null)
                {
                    Reject(client, spell.Type, reason);
                    return false;
                }
                var path = Line(character.X, character.Y, x, y).ToHashSet();
                victims = Around(client, now).Where(v => path.Contains((v.X, v.Y))).Take(MaxTargets).ToList();
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
            {
                victim.Missed = victim.Player != null && Resists(client, victim.Player, spell);
                for (int hit = 0; hit < hits; hit++)
                {
                    uint damage = victim.Missed ? 0 : victim.Monster != null
                        ? AgainstMonster(client, stats, spell, victim.Monster, now)
                        : AgainstPlayer(client, stats, spell, victim.Player, now);
                    victim.Hits.Add(damage);
                    victim.Damage += damage;
                }
            }

            await PayAsync(client, spell, now, passive);
            await Durability.WearOnAttackAsync(client);
            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            foreach (var victim in victims)
                foreach (var damage in victim.Hits)
                    effect.Targets.Add((victim.ID, damage));
            if (spell.Sort == MagicSort.BladeTempest)
                await Movement.DashAsync(client, x, y, effect);
            else
            {
                await client.SendAsync(effect);
                await Movement.SendToViewAsync(client, effect);
            }

            // Blade Tempest spots all it hits, once Black Spot is learned
            MagicType blackSpot = null;
            bool spots = spell.Sort == MagicSort.BladeTempest &&
                Pirates.TryGetLearned(client, Pirates.BlackSpot, out blackSpot);
            foreach (var victim in victims.Where(v => v.Damage > 0))
            {
                if (spots)
                    await Pirates.SpotAsync(client, blackSpot, victim.ID, victim.X, victim.Y, now);
                else
                    await Pirates.AfterDamageAsync(client, victim.ID, victim.X, victim.Y, now);
            }
            if (spell.Type == Pirates.EagleEye && victims.Any(v => Pirates.IsSpotted(v.ID, now)))
                await Pirates.RushAsync(client, "black_spot");

            uint experience = 0;
            foreach (var victim in victims)
            {
                if (victim.Player != null)
                {
                    var target = victim.Player;
                    await Pking.OnAttackAsync(client, target);
                    Kernel.Post(target, () => Attacks.DamagePlayerAsync(client, target, victim.Damage, "magic"));
                    if (spell.Sort == MagicSort.ScurvyBomb && !victim.Missed)
                        Kernel.Post(target, () => FatigueAsync(client, target, spell));
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

        private static async Task StartXpStatusAsync(Client client, MagicType spell, DateTime now)
        {
            var character = client.Character;
            var flag = XpStatusOf(spell.Sort);
            await PayAsync(client, spell, now);
            client.Effects.Set(flag, now + TimeSpan.FromSeconds(spell.StepSeconds), now, spell.Power);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                character.Name, flag, spell.StepSeconds, character.Name, spell.Name);
            LogCast(client, spell, character.X, character.Y, 1);
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

        // The cells from the one after (x, y) to (toX, toY)
        private static IEnumerable<(ushort X, ushort Y)> Line(int x, int y, int toX, int toY)
        {
            int dx = toX - x, dy = toY - y;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            for (int i = 1; i <= steps; i++)
                yield return ((ushort)(x + (int)Math.Round((double)dx * i / steps)),
                    (ushort)(y + (int)Math.Round((double)dy * i / steps)));
        }

        public static bool InShape(MagicType spell, int x, int y, int aimX, int aimY, int tx, int ty)
        {
            int range = (int)spell.Range;
            if (MonsterAI.Distance(x, y, tx, ty) > range || (tx == x && ty == y))
                return false;
            switch (spell.Sort)
            {
                case MagicSort.Bomb:
                case MagicSort.WhirlwindKick:
                    return true;
                case MagicSort.Line:
                    return OnLine(x, y, aimX, aimY, tx, ty, range);
                // The client's description: all targets within 4 paces in a row, magictype's distance
                case MagicSort.RadiantPalm:
                    return OnLine(x, y, aimX, aimY, tx, ty, (int)spell.Distance);
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

        private static bool OnLine(int x, int y, int aimX, int aimY, int tx, int ty, int length)
        {
            int dx = aimX - x, dy = aimY - y;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps == 0)
                return false;
            for (int i = 1; i <= length; i++)
                if (x + (int)Math.Round((double)dx * i / steps) == tx &&
                    y + (int)Math.Round((double)dy * i / steps) == ty)
                    return true;
            return false;
        }

        // The pirates' bombs miss a player of higher battle power: 2% more often for each point
        // of it, 20% at most
        private const int MaxResistPercent = 20;

        private static bool Resists(Client caster, Client target, MagicType spell)
        {
            if (spell.Sort is not (MagicSort.ScurvyBomb or MagicSort.GaleBomb))
                return false;
            int gap = (int)Attributes.BattlePower(target.Character) - (int)Attributes.BattlePower(caster.Character);
            return gap > 0 && Random.Shared.Next(100) < Math.Min(MaxResistPercent, gap * 2);
        }

        // On the target's queue
        private static async Task FatigueAsync(Client caster, Client target, MagicType spell)
        {
            var character = target.Character;
            if (character == null || target.Life.Dead)
                return;
            var now = DateTime.UtcNow;
            int extra = FatigueStamina[Math.Min(spell.Level, FatigueStamina.Length - 1)];
            target.Effects.Set(StatusFlags.Fatigue, now + TimeSpan.FromSeconds(spell.StepSeconds), now, extra);
            await Pking.SendStatusAsync(target);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                character.Name, StatusFlags.Fatigue, spell.StepSeconds, caster.Character?.Name, spell.Name);
        }

        // Spells with power from 30000 on hit like the weapon, by that percentage; others are
        // magic and miss when the spell's percent says so
        private static uint AgainstMonster(Client client, Stats stats, MagicType spell, Monster monster, DateTime now)
        {
            var type = monster.Type;
            int level = client.Character.Level;
            if (spell.Sort == MagicSort.DecLife)
                return ShareOf(monster.Health, spell);
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
            if (spell.Sort == MagicSort.DecLife)
                return ShareOf(target.Character.HealthPoints, spell);
            if (!spell.IsPercent)
                return Random.Shared.Next(100) < spell.Percent
                    ? targetStats.Taken(Damage.PlayerMagic(stats.MagicDamage, spell.Power, targetStats.MagicDefense)) : 0;
            // Whirlwind Kick reaches flyers, as the client's description says
            if (!stats.Bow && spell.Sort != MagicSort.WhirlwindKick && Buffs.Flying(target.Effects, now))
                return 0;
            if (!Buffs.HasAccuracy(client.Effects, now) && !Damage.Hits(targetStats.Dodge, targetIsMonster: false, Random.Shared))
                return 0;
            int attack = Damage.Roll(stats.MinDamage, stats.MaxDamage, Random.Shared) * AgainstPlayersPercent(spell) / 100;
            attack = Buffs.AgainstPlayer(client.Effects, attack, now);
            return targetStats.Taken(stats.Bow
                ? Damage.PlayerArchery(attack, targetStats.Dodge)
                : Damage.PlayerMelee(attack, Buffs.Defense(target.Effects, targetStats.Defense, now)));
        }

        private static int AgainstPlayersPercent(MagicType spell) => spell.Sort switch
        {
            MagicSort.CannonBarrage => CannonAgainstPlayers,
            MagicSort.BlackbeardsRage => BlackbeardAgainstPlayers[Math.Min(spell.Level, BlackbeardAgainstPlayers.Length - 1)],
            _ => spell.PowerPercent,
        };

        // Cruel Shade takes its power's percentage of the target's life left
        private static uint ShareOf(uint health, MagicType spell) =>
            (uint)Math.Max(1, (ulong)health * (uint)Math.Max(0, spell.PowerPercent) / 100);

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
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var healed in targets)
                effect.Targets.Add((healed.ID, HealOf(spell, healed)));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            foreach (var healed in targets)
            {
                uint amount = HealOf(spell, healed);
                Kernel.Post(healed, () => HealAsync(healed, amount));
            }
            LogCast(client, spell, character.X, character.Y, targets.Count);
            await GainExperienceAsync(client, spell, HealOf(spell, target));
        }

        // A negative power (Restore's -32768) heals to full
        private static uint HealOf(MagicType spell, Client target) =>
            spell.Power >= 0 ? (uint)spell.Power
                : (uint)Math.Max(0, Attributes.MaxHealth(target) - (target.Character?.HealthPoints ?? 0));

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
                Buffs.Shackled(target.Effects, now) ? "shackled" :
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

        // Toxic Fog poisons the monsters and attackable players around the cell aimed at, each
        // at the spell's percent
        private static async Task FogAsync(Client client, MagicType spell, ushort x, ushort y, DateTime now)
        {
            var character = client.Character;
            if (MonsterAI.Distance(character.X, character.Y, x, y) > spell.Distance + DistanceSlack)
            {
                Reject(client, spell.Type, "too_far");
                return;
            }

            var victims = Around(client, now)
                .Where(v => MonsterAI.Distance(x, y, v.X, v.Y) <= spell.Range)
                .Take(MaxTargets)
                .ToList();
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            foreach (var victim in victims)
                effect.Targets.Add((victim.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);

            int poisoned = 0;
            foreach (var victim in victims)
            {
                if (victim.Player != null)
                    await Pking.OnAttackAsync(client, victim.Player);
                else if (victim.Monster.IsGuard)
                    await Pking.FlashAsync(client, Pking.FlashTime, "attack_guard");
                else
                    victim.Monster.Provoke(client.ID);
                if (Random.Shared.Next(100) >= spell.Percent)
                    continue;
                poisoned++;
                var poison = Poison.From(spell, now);
                if (victim.Player != null)
                {
                    var target = victim.Player;
                    Kernel.Post(target, () => Poison.StartAsync(target, poison, DateTime.UtcNow));
                }
                else
                    await Poison.StartAsync(victim.Monster, poison);
            }
            LogCast(client, spell, x, y, poisoned);
        }

        // Kraken's Revenge puts Black Spot on the target and the nearest around it
        private static async Task KrakenAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var (target, reason) = FindTarget(client, spell, targetID, now);
            if (target == null)
            {
                Reject(client, spell.Type, reason);
                return;
            }

            var victims = Around(client, now)
                .Where(v => v.ID != target.ID && MonsterAI.Distance(target.X, target.Y, v.X, v.Y) <= spell.Range)
                .OrderBy(v => MonsterAI.Distance(target.X, target.Y, v.X, v.Y))
                .Prepend(target)
                .Take(KrakenTargets[Math.Min(spell.Level, KrakenTargets.Length - 1)])
                .ToList();
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, target.X, target.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var victim in victims)
                effect.Targets.Add((victim.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);

            var until = now + TimeSpan.FromSeconds(spell.StepSeconds);
            foreach (var victim in victims)
            {
                if (victim.Player != null)
                    await Pking.OnAttackAsync(client, victim.Player);
                else if (victim.Monster.IsGuard)
                    await Pking.FlashAsync(client, Pking.FlashTime, "attack_guard");
                else
                    victim.Monster.Provoke(client.ID);
                Pirates.Spot(victim.ID, until);
            }
            LogCast(client, spell, target.X, target.Y, victims.Count);
        }

        // Archer Bane takes a player's status (Fly) away, at the spell's percent
        private static async Task TakeStatusAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            ulong flag = Buffs.FlagOf(spell.Status);
            var (victim, reason) = flag == 0 ? (null, "unknown_status") : FindTarget(client, spell, targetID, now);
            if (victim != null && victim.Player == null)
                (victim, reason) = (null, "not_player");
            if (victim == null)
            {
                Reject(client, spell.Type, reason);
                return;
            }

            var target = victim.Player;
            bool taken = target.Effects.Has(flag, now) && Random.Shared.Next(100) < spell.Percent;
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, victim.X, victim.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.OnAttackAsync(client, target);
            if (taken)
                Kernel.Post(target, async () =>
                {
                    target.Effects.Remove(flag);
                    await Pking.SendStatusAsync(target);
                    LogEvents.For(typeof(Casting), LogEvents.StatusEnded).Information(
                        "{Name} lost status 0x{Status:X} to {Caster}'s {MagicName}",
                        target.Character?.Name, flag, client.Character.Name, spell.Name);
                });
            LogCast(client, spell, victim.X, victim.Y, 1);
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
            var status = Buffs.StatusOf(spell.Type);
            if (status == UInt128.Zero)
            {
                Reject(client, spell.Type, "unknown_status");
                return;
            }

            var target = spell.IsXpSkill ? client : Friendly(client, spell, targetID);
            await PayAsync(client, spell, now);
            if (status == StatusFlags.Superman || status == StatusFlags.Cyclone || status == StatusFlags.Oblivion)
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

        // A monk keeps one aura up on themself and the teammates within its range, as long as
        // magictype's step; casting it again takes it down, casting another swaps it
        private static async Task AuraAsync(Client client, MagicType spell, DateTime now)
        {
            var flag = Buffs.AuraOf(spell.Type);
            if (flag == UInt128.Zero)
            {
                Reject(client, spell.Type, "unknown_status");
                return;
            }
            var character = client.Character;
            bool off = client.Aura == spell.Type;
            await DropAuraAsync(client);
            if (off)
            {
                LogCast(client, spell, character.X, character.Y, 0);
                return;
            }

            var members = Teammates(client, spell.Range).Prepend(client).ToList();
            await PayAsync(client, spell, now);
            client.Aura = spell.Type;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var member in members)
            {
                member.Effects.Set(flag, now + TimeSpan.FromSeconds(spell.StepSeconds), now, spell.Power);
                effect.Targets.Add((member.ID, 0));
            }
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            foreach (var member in members)
                await Pking.SendStatusAsync(member);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} put up {MagicName} on {Count} players", character.Name, spell.Name, members.Count);
            LogCast(client, spell, character.X, character.Y, members.Count);
        }

        // Takes the monk's aura off them and their teammates
        public static async Task DropAuraAsync(Client client)
        {
            var flag = Buffs.AuraOf(client.Aura);
            client.Aura = 0;
            if (flag == UInt128.Zero)
                return;
            foreach (var member in Social.Teams.OthersOf(client).Prepend(client))
                if (member.Effects.Remove(flag))
                    await Pking.SendStatusAsync(member);
            LogEvents.For(typeof(Casting), LogEvents.StatusEnded).Information(
                "{Name} took down aura 0x{Status:X}", client.Character?.Name, flag);
        }

        // Soul Shackle keeps a dead player from reviving for magictype's step. As the client
        // describes it: sure against a lower battle power, 5% less for each point above the
        // caster's.
        private static async Task ShackleAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var character = client.Character;
            Client target = null;
            string reason =
                !Kernel.Players.TryGet(targetID, out target) || target == client || target.Character == null ||
                target.Map != client.Map ? "not_found" :
                !target.Life.Dead ? "alive_target" :
                Buffs.Shackled(target.Effects, now) ? "shackled" :
                !Pking.CanAttack(client, target) ? "pk_mode" :
                MonsterAI.Distance(character.X, character.Y, target.Character.X, target.Character.Y) >
                    spell.Distance + DistanceSlack ? "too_far" :
                null;
            if (reason != null)
            {
                Reject(client, spell.Type, reason);
                return;
            }

            int above = (int)Attributes.BattlePower(target.Character) - (int)Attributes.BattlePower(character);
            bool shackled = Random.Shared.Next(100) < 100 - 5 * Math.Max(0, above);
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, target.Character.X, target.Character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.OnAttackAsync(client, target);
            if (shackled)
            {
                target.Effects.Set(StatusFlags.SoulShackle, now + TimeSpan.FromSeconds(spell.StepSeconds), now);
                await Pking.SendStatusAsync(target);
                LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                    "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                    target.Character.Name, StatusFlags.SoulShackle, spell.StepSeconds, character.Name, spell.Name);
            }
            LogCast(client, spell, target.Character.X, target.Character.Y, shackled ? 1 : 0);
            await GainExperienceAsync(client, spell, shackled ? 1u : 0u);
        }

        // Takes poison and Soul Shackle away; Compassion leaves the shackle
        private static async Task DispelAsync(Client client, MagicType spell, List<Client> targets, DateTime now)
        {
            var character = client.Character;
            await PayAsync(client, spell, now);
            if (client.NextCastAt < now + DispelInterval)
                client.NextCastAt = now + DispelInterval;
            var at = targets[0].Character;
            var effect = new MsgMagicEffect(client.ID, at.X, at.Y, spell.Type) { MagicLevel = spell.Level };
            foreach (var target in targets)
                effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);

            var flags = spell.Sort == MagicSort.Compassion
                ? (UInt128)StatusFlags.Poisoned
                : StatusFlags.Poisoned | StatusFlags.SoulShackle;
            foreach (var target in targets)
                Kernel.Post(target, async () =>
                {
                    var had = target.Effects.Status(DateTime.UtcNow) & flags;
                    if (had == UInt128.Zero)
                        return;
                    target.Effects.Remove(StatusFlags.Poisoned);
                    target.Poison = null;
                    if ((flags & StatusFlags.SoulShackle) != UInt128.Zero)
                        target.Effects.Remove(StatusFlags.SoulShackle);
                    await Pking.SendStatusAsync(target);
                    LogEvents.For(typeof(Casting), LogEvents.StatusEnded).Information(
                        "{Name} lost status 0x{Status:X} to {Caster}'s {MagicName}",
                        target.Character?.Name, had, character.Name, spell.Name);
                });
            LogCast(client, spell, at.X, at.Y, targets.Count);
        }

        // A player in reach, living or dead, or the caster
        private static Client Anyone(Client client, MagicType spell, uint targetID)
        {
            if (targetID == 0 || targetID == client.ID || !Kernel.Players.TryGet(targetID, out var target) ||
                target.Character == null || target.Map != client.Map ||
                MonsterAI.Distance(client.Character.X, client.Character.Y, target.Character.X, target.Character.Y) >
                    spell.Distance + DistanceSlack)
                return client;
            return target;
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
            await Recovery.UseStaminaAsync(client, StaminaOf(client, spell, now));
            if (spell.UseItem != 0 && ItemToUse(client, spell) is Database.Models.DbItem item)
                await Quivers.UseAsync(client, item, spell.UseItemCount);
            if (spell.IsXpSkill && !InXpStatus(client, spell, now))
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

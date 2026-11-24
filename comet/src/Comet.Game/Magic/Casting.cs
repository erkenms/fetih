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
            MagicSort.ShurikenVortex, MagicSort.CounterKill, MagicSort.Collide,
            MagicSort.Transform, MagicSort.StarArrow, MagicSort.DragonWhirl,
        };

        public const ushort FatalStrike = 6011;

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

        // A monster, a player or a training ground goal
        private sealed record Victim(Monster Monster, Client Player, Npc Goal, ushort X, ushort Y)
        {
            public uint Damage { get; set; }
            public List<uint> Hits { get; } = new List<uint>();
            public bool Missed { get; set; }
            public uint ID => this.Monster?.Identity ?? this.Goal?.NpcID ?? this.Player.ID;
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
                case MagicSort.AttachStatus when spell.Type == FatalStrike:
                    await StartXpStatusAsync(client, spell, now);
                    break;
                case MagicSort.AttachStatus when Buffs.StatusOf(spell.Type) == StatusFlags.NoPotion:
                    await PoisonStarAsync(client, spell, targetID, now);
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
                case MagicSort.Transform:
                    await TransformAsync(client, spell, now);
                    break;
                case MagicSort.CounterKill:
                    await SwitchCounterKillAsync(client, spell, now);
                    break;
                case MagicSort.KrakensRevenge:
                    await KrakenAsync(client, spell, targetID, now);
                    break;
                case MagicSort.ShurikenVortex:
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
            client.Effects.Has(StatusFlags.Stun, now) ? "stunned" :
            spell.IsXpSkill && ServerDriven(spell) && InXpStatus(client, spell, now) ? "xp_status" :
            spell.IsXpSkill && !client.Effects.Has(StatusFlags.XpFull, now) && !InXpStatus(client, spell, now) ? "xp_not_full" :
            client.Character.ManaPoints < spell.UseMana && !Training.TrainingGround.CostsNothing(client) ? "no_mana" :
            client.Energy.Stamina < StaminaOf(client, spell, now) ? "no_stamina" :
            spell.WeaponSubtype != 0 && !Holds(client, spell.WeaponSubtype) ? "wrong_weapon" :
            spell.UseItem != 0 && ItemToUse(client, spell) == null ? "no_item" :
            spell.Sort == MagicSort.Riding ? Riding.Problem(client) :
            XpStatusOf(spell) is var xp && (xp == StatusFlags.ShurikenVortex || xp == StatusFlags.FatalStrike) &&
                Buffs.Flying(client.Effects, now) ? "flying" :
            Buffs.StatusOf(spell.Type) == StatusFlags.Fly &&
                (client.Effects.Has(StatusFlags.ShurikenVortex, now) || client.Effects.Has(StatusFlags.FatalStrike, now)) ? "xp_status" :
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
                .Any(item => item != null && WeaponsOf(weaponSubtype).Contains(Proficiency.WeaponType(item.TypeID)));

        private static IEnumerable<uint> WeaponsOf(uint weaponSubtype)
        {
            for (; weaponSubtype > 0; weaponSubtype /= 1000)
                yield return weaponSubtype % 1000;
        }

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
                    if (Random.Shared.Next(TripleAttackOdds(client, targetID)) >= spell.Percent)
                        continue;
                    var swing = spell with { Power = MagicType.PercentBase + 100 };
                    return await AttackAsync(client, swing, targetID, x, y, now, passive: true, hits: 3, swing: true);
                }
                // Gaping Wounds leaves bosses and guards to the plain swing
                if (spell.Sort == MagicSort.DecLife && IsBossOrGuard(client, targetID))
                    continue;
                if (!spell.IsPercent || GoesWithHit(spell) || !IsAttack(spell.Sort) || Random.Shared.Next(100) >= PassiveOdds(client, spell))
                    continue;
                return await AttackAsync(client, spell, targetID, x, y, now, passive: true, swing: true);
            }
            return false;
        }

        // Weapon skills that go with a hit rather than in place of a swing, at their percent: a
        // stun for the power's milliseconds (magictype's status 16; Earthquake's description:
        // the target can't attack), or poison (status 2)
        private const int StunStatus = 16, PoisonStatus = 2;

        private static bool GoesWithHit(MagicType spell) =>
            spell.Sort == MagicSort.AttackStatus && spell.Status is StunStatus or PoisonStatus;

        // After the caster's swing or shot hurt the target
        public static async Task AfterHitAsync(Client client, uint targetID, DateTime now)
        {
            foreach (var (type, learned) in client.Skills.AllSpells())
                if (Kernel.Magics.TryGet(type, learned.Level, out var spell) && spell.IsPassive && GoesWithHit(spell) &&
                    spell.WeaponSubtype != 0 && Holds(client, spell.WeaponSubtype) && Random.Shared.Next(100) < spell.Percent)
                    await AfflictAsync(client, spell, targetID, now);
        }

        private static async Task AfflictAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var character = client.Character;
            Monster monster = null;
            Client player = null;
            ushort x = 0, y = 0;
            bool found = Attacks.IsMonster(targetID)
                ? Kernel.Monsters.TryGet(character.MapID, out var map) && map.TryGet(targetID, out monster, out x, out y) && monster.Alive
                : Kernel.Players.TryGet(targetID, out player) && player.Map == client.Map && player.Map.TryGetPosition(player, out x, out y);
            if (!found)
                return;

            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((targetID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            if (spell.Status == PoisonStatus)
            {
                var poison = Poison.From(spell, now);
                if (player != null)
                    Kernel.Post(player, () => Poison.StartAsync(player, poison, DateTime.UtcNow));
                else
                    await Poison.StartAsync(monster, poison);
            }
            else
            {
                var until = now + TimeSpan.FromMilliseconds(Math.Max(0, spell.Power));
                if (player != null)
                    Kernel.Post(player, () => StunAsync(player, until));
                else if (monster.Stun(until))
                    foreach (var other in Monsters.PlayersInView(monster))
                        await other.SendAsync(new MsgUserAttrib(monster.Identity, AttributeType.Status, StatusFlags.Stun));
            }
            LogCast(client, spell, x, y, 1);
        }

        // On the player's queue
        private static async Task StunAsync(Client target, DateTime until)
        {
            if (target.Character == null || target.Life.Dead)
                return;
            Attacks.Stop(target);
            target.Effects.Set(StatusFlags.Stun, until, DateTime.UtcNow);
            await Pking.SendStatusAsync(target);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from a stun",
                target.Character.Name, StatusFlags.Stun, (int)(until - DateTime.UtcNow).TotalSeconds);
        }

        private static bool IsBossOrGuard(Client client, uint targetID) =>
            Attacks.IsMonster(targetID) && Kernel.Monsters.TryGet(client.Character.MapID, out var map) &&
            map.TryGet(targetID, out var monster, out _, out _) && (monster.Type.Boss || monster.IsGuard);

        // Gaping Wounds goes off twice as often with its weapons in both hands, as its
        // description says
        private static uint PassiveOdds(Client client, MagicType spell) =>
            spell.Sort == MagicSort.DecLife && BothHandsHold(client, spell.WeaponSubtype) ? spell.Percent * 2 : spell.Percent;

        private static bool BothHandsHold(Client client, uint weaponSubtype) =>
            new[] { ItemPosition.RightHand, ItemPosition.LeftHand }
                .Select(p => client.Equipment?[p])
                .All(item => item != null && WeaponsOf(weaponSubtype).Contains(Proficiency.WeaponType(item.TypeID)));

        // The client's description: Triple Attack's percent against monsters, a third of it
        // against players
        private static int TripleAttackOdds(Client client, uint targetID) => AgainstPlayer(client, targetID) ? 300 : 100;

        private static bool AgainstPlayer(Client client, uint targetID) =>
            !Attacks.IsMonster(targetID) && Training.TrainingGround.FindGoal(client, targetID) == null;

        // Windstorm: at its percent, a third against players, a rapier swing is a combo of three
        // hits of a swing each, shown as the skill
        private const int WindstormHits = 3, WindstormAgainstPlayersPercent = 33;

        private static async Task<bool> TryWindstormAsync(Client client, uint targetID, ushort x, ushort y, DateTime now)
        {
            if (!Pirates.TryGetLearned(client, Pirates.Windstorm, out var spell) ||
                spell.WeaponSubtype != 0 && !Holds(client, spell.WeaponSubtype) ||
                Random.Shared.Next(100) >= (AgainstPlayer(client, targetID) ? WindstormAgainstPlayersPercent : spell.Percent))
                return false;
            // The swing checked the reach; each hit of the combo shows on its own, as Triple
            // Attack's do
            var combo = spell with
            {
                Sort = MagicSort.Attack,
                Power = MagicType.PercentBase + 100,
                Distance = ushort.MaxValue,
            };
            return await AttackAsync(client, combo, targetID, x, y, now, passive: true, hits: WindstormHits, swing: true);
        }

        private static bool IsAttack(MagicSort sort) => sort is MagicSort.Attack or MagicSort.AttackStatus or MagicSort.DecLife or
            MagicSort.Bomb or MagicSort.Line or MagicSort.Fan or MagicSort.RadiantPalm or MagicSort.WhirlwindKick or MagicSort.BladeTempest ||
            AtCell(sort);

        // Spells that hit around the cell aimed at, within their distance of the caster
        private static bool AtCell(MagicSort sort) => sort is MagicSort.ScurvyBomb or MagicSort.GaleBomb or MagicSort.CannonBarrage;

        // Ninja XP skills the client casts once: the server spins Shuriken Vortex every step, and
        // turns each melee click into a Fatal Strike
        private static bool ServerDriven(MagicType spell) =>
            spell.Sort == MagicSort.ShurikenVortex || spell.Type == FatalStrike;

        // The status of an XP skill whose first cast only starts it: while it's on, the pirates'
        // client casts the skill for each attack
        private static UInt128 XpStatusOf(MagicType spell) => spell.Sort switch
        {
            MagicSort.CannonBarrage => StatusFlags.CannonBarrage,
            MagicSort.BlackbeardsRage => StatusFlags.BlackbeardsRage,
            MagicSort.ShurikenVortex => StatusFlags.ShurikenVortex,
            _ when spell.Type == FatalStrike => StatusFlags.FatalStrike,
            _ => 0,
        };

        // Shuriken Vortex goes off every step, active times over
        private static uint XpStatusSeconds(MagicType spell) =>
            spell.Sort == MagicSort.ShurikenVortex ? spell.StepSeconds * Math.Max(1, spell.ActiveTimes) : spell.StepSeconds;

        private static bool InXpStatus(Client client, MagicType spell, DateTime now) =>
            XpStatusOf(spell) is var flag && flag != 0 && client.Effects.Has(flag, now);

        // A spell that costs stamina costs more during Scurvy Bomb's fatigue, and none on the
        // training ground
        private static byte StaminaOf(Client client, MagicType spell, DateTime now) =>
            spell.UseStamina == 0 || Training.TrainingGround.CostsNothing(client) ? (byte)0 :
            (byte)Math.Min(byte.MaxValue, spell.UseStamina + (client.Effects.Power(StatusFlags.Fatigue, now) ?? 0));

        // A swing (a weapon skill going off in place of one) hits stakes, a spell scarecrows
        private static async Task<bool> AttackAsync(Client client, MagicType spell, uint targetID, ushort x, ushort y,
            DateTime now, bool passive = false, int hits = 1, bool swing = false)
        {
            IEnumerable<Victim> Around(Client client, DateTime now) => Casting.Around(client, now, goals: true, swing);
            (Victim, string) FindTarget(Client client, MagicType spell, uint targetID, DateTime now) =>
                Casting.FindTarget(client, spell, targetID, now, swing);
            var character = client.Character;
            List<Victim> victims;
            (ushort X, ushort Y)? dashTo = null;
            byte direction = 0;
            (ushort X, ushort Y)? knockTo = null;
            if (spell.Sort == MagicSort.Collide)
            {
                // Dash: a step toward the cell aimed at; who stands there is hit and knocked a
                // cell on, and the caster takes their cell, unless they can't go
                if (x == character.X && y == character.Y)
                {
                    Reject(client, spell.Type, "no_direction");
                    return false;
                }
                direction = Movement.DirectionTo(character.X, character.Y, x, y, character.Direction);
                int toX = character.X + Movement.DeltaX[direction], toY = character.Y + Movement.DeltaY[direction];
                victims = Around(client, now).Where(v => v.X == toX && v.Y == toY).Take(1).ToList();
                int backX = toX + Movement.DeltaX[direction], backY = toY + Movement.DeltaY[direction];
                bool canStep = Movement.CheckDash(client, toX, toY) == null;
                bool canKnock = victims.Count > 0 && victims[0].Goal == null && Movement.CheckDash(client, backX, backY) == null &&
                    !Around(client, now).Any(v => v.X == backX && v.Y == backY);
                if (!canStep && victims.Count == 0)
                {
                    Reject(client, spell.Type, "blocked_cell");
                    return false;
                }
                if (canStep && (victims.Count == 0 || canKnock))
                    dashTo = ((ushort)toX, (ushort)toY);
                if (canKnock)
                    knockTo = ((ushort)backX, (ushort)backY);
                (x, y) = ((ushort)toX, (ushort)toY);
            }
            else if (spell.Sort == MagicSort.BlackbeardsRage)
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
            else if (spell.Sort == MagicSort.StarArrow)
            {
                // The arrow throws the target back along the line from the archer, as far as the
                // spell's range from the archer and the way is clear
                var (victim, reason) = FindTarget(client, spell, targetID, now);
                if (victim == null)
                {
                    Reject(client, spell.Type, reason);
                    return false;
                }
                victims = new List<Victim> { victim };
                (x, y) = (victim.X, victim.Y);
                direction = Movement.DirectionTo(character.X, character.Y, x, y, character.Direction);
                foreach (var cell in victim.Goal == null ? KnockedBack(client, victim, (int)spell.Range) : Enumerable.Empty<(ushort, ushort)>())
                    knockTo = cell;
                if (knockTo is var (landX, landY))
                    (x, y) = (landX, landY);
            }
            else if (spell.Sort == MagicSort.DragonWhirl)
            {
                // The trojan leaps toward the cell aimed at, as far as the way is clear, and hits
                // all within the range of where they land
                if (MonsterAI.Distance(character.X, character.Y, x, y) > spell.Distance + DistanceSlack)
                {
                    Reject(client, spell.Type, "too_far");
                    return false;
                }
                var (aimX, aimY) = (x, y);
                (x, y) = (character.X, character.Y);
                foreach (var cell in Line(character.X, character.Y, aimX, aimY))
                {
                    if (Movement.CheckDash(client, cell.X, cell.Y) != null)
                        break;
                    (x, y) = cell;
                }
                if (x != character.X || y != character.Y)
                    dashTo = (x, y);
                ushort landX = x, landY = y;
                victims = Around(client, now)
                    .Where(v => MonsterAI.Distance(landX, landY, v.X, v.Y) <= spell.Range)
                    .OrderBy(v => MonsterAI.Distance(landX, landY, v.X, v.Y))
                    .Take(MaxTargets)
                    .ToList();
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
                if (spell.Sort == MagicSort.ShurikenVortex)
                    (x, y) = (character.X, character.Y);
                // The client lets a body that's still falling be clicked
                string reason = AimsAtDead(client, targetID) ? "dead_target" :
                    spell.Sort is MagicSort.Line or MagicSort.RadiantPalm or MagicSort.Fan &&
                        x == character.X && y == character.Y ? "no_direction" :
                    null;
                if (reason != null)
                {
                    Reject(client, spell.Type, reason);
                    return false;
                }
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
                    uint damage = victim.Missed ? 0 :
                        victim.Monster != null ? AgainstMonster(client, stats, spell, victim.Monster, now) :
                        victim.Goal != null ? AgainstGoal(client, stats, spell, victim.Goal, now) :
                        AgainstPlayer(client, stats, spell, victim.Player, now);
                    victim.Hits.Add(damage);
                    victim.Damage += damage;
                    // A combo ends with the hit that kills
                    if (victim.Monster != null && victim.Damage >= victim.Monster.Health)
                        break;
                }
            }

            await PayAsync(client, spell, now, passive);
            await Durability.WearOnAttackAsync(client);
            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            // The client plays a kick that lifts its targets (Whirlwind Kick) as X hits, each
            // showing a share of the damage and lifting the targets higher, up to 99
            if (Kernel.MagicEffects.Find(spell.Type, spell.Level)?.Represent == MagicEffect.Lift)
                (effect.X, effect.Y) = (LiftHits, 0);
            foreach (var victim in victims)
                foreach (var damage in victim.Hits)
                    effect.Targets.Add((victim.ID, damage));
            // Dash shows as MsgInteract 27, which the client takes as a move to its cell, with the
            // direction in the data's high byte and the damage below
            Network.Packets.IPacket shown = spell.Sort != MagicSort.Collide ? effect : new MsgInteract(
                MsgInteract.InteractionType.Dash, client.ID, victims.FirstOrDefault()?.ID ?? 0,
                dashTo?.X ?? character.X, dashTo?.Y ?? character.Y,
                (uint)direction << 24 | Math.Min(victims.FirstOrDefault()?.Damage ?? 0, 0xFFFFFFu));
            if (spell.Sort == MagicSort.BladeTempest)
                await Movement.DashAsync(client, x, y, effect);
            else if (dashTo is var (toX, toY))
                await Movement.DashAsync(client, toX, toY, shown);
            else
            {
                await client.SendAsync(shown);
                await Movement.SendToViewAsync(client, shown);
            }
            if (knockTo is var (knockX, knockY))
                Knock(victims[0], knockX, knockY, direction);
            // A kick's targets can't move while it holds them in the air, as its description says
            if (Kernel.MagicEffects.Find(spell.Type, spell.Level) is { Represent: MagicEffect.Lift, LastTime: > 0 } lift)
                Hold(victims, now + TimeSpan.FromMilliseconds(lift.LastTime));

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
            var show = Kernel.MagicEffects.Find(spell.Type, spell.Level);
            for (int i = 0; i < victims.Count; i++)
            {
                var victim = victims[i];
                var after = HitShownAfter(show, spell, character.X, character.Y, x, y, victim) + ShotGap(spell) * i;
                if (victim.Player != null)
                {
                    var target = victim.Player;
                    await Pking.OnAttackAsync(client, target);
                    Later(target, after, () => Attacks.DamagePlayerAsync(client, target, victim.Damage, "magic"));
                    if (spell.Sort == MagicSort.ScurvyBomb && !victim.Missed)
                        Later(target, after, () => FatigueAsync(client, target, spell));
                    continue;
                }
                if (victim.Goal != null)
                {
                    experience += (uint)Math.Min(uint.MaxValue,
                        await Training.TrainingGround.AfterHitAsync(client, victim.Goal, victim.Damage, "magic"));
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
                if (killed && after > TimeSpan.Zero)
                    Later(client, after, () => client.Character == null ? Task.CompletedTask
                        : Attacks.AfterMonsterHitAsync(client, monster, victim.X, victim.Y, dealt, killed));
                else
                    await Attacks.AfterMonsterHitAsync(client, monster, victim.X, victim.Y, dealt, killed);
            }

            LogCast(client, spell, x, y, victims.Count);
            await GainExperienceAsync(client, spell, experience);
            return true;
        }

        // Blackbeard's Rage shoots its targets one after another, so each falls a shot after the
        // one before rather than all at once
        public static TimeSpan BlackbeardShotGap { get; set; } = TimeSpan.FromMilliseconds(330);

        private static TimeSpan ShotGap(MagicType spell) =>
            spell.Sort == MagicSort.BlackbeardsRage ? BlackbeardShotGap : TimeSpan.Zero;

        // When the client shows a victim's last hit, so what it kills falls then rather than
        // before: the client lets a monster fall a moment after the Kill, and a hit shown later
        // stands it up again at no life. A thrown body (the bombs) lands TraceEffectDelay and TraceEffectSpeed ms for
        // each world unit from the caster to the cell aimed at; the client shows Gale Bomb's
        // hits at once, but it falls as its bomb lands too, like Scurvy Bomb's. A shot covers
        // (TraceEffectSpeed + TraceEffectAcceleration * t) * t world units in t seconds; a
        // combo's hits come ExtraCmdTime apart.
        private static TimeSpan HitShownAfter(MagicEffect effect, MagicType spell, ushort casterX, ushort casterY,
            ushort aimX, ushort aimY, Victim victim)
        {
            if (effect == null)
                return TimeSpan.Zero;
            double ms = effect.Intone + effect.TargetDelay;
            if (effect.Represent == MagicEffect.Thrown && effect.Traces)
                ms += effect.TraceDelay + effect.TraceSpeed * (int)WorldDistance(casterX, casterY, aimX, aimY);
            // Blackbeard's Rage keeps its shots a measured gap apart
            else if (effect.Represent is MagicEffect.Shot or MagicEffect.ShotEx or MagicEffect.ShotChain &&
                effect.Traces && spell.Sort != MagicSort.BlackbeardsRage)
                ms = Math.Max(ms, ShotFlight(WorldDistance(casterX, casterY, victim.X, victim.Y),
                    effect.TraceSpeed, effect.TraceAcceleration));
            else if (effect.Represent == MagicEffect.Combo)
                ms += effect.HitGap * Math.Max(0, victim.Hits.Count - 1);
            // A kick that lifts its targets (Whirlwind Kick) is told of a kill at once: when the
            // kick ends, the client lands the living and lets the dead fall
            else if (effect.Represent == MagicEffect.Lift)
                return TimeSpan.Zero;
            if (spell.Sort == MagicSort.CannonBarrage)
                ms += CannonBarrageLanding;
            return TimeSpan.FromMilliseconds(ms);
        }

        // The client shows Cannon Barrage's hits at once, but its shells fall about this long
        // after, as seen in the client
        private const int CannonBarrageLanding = 200;

        // A cell is (x - y) * 32 by (x + y) * 16 world units
        private static double WorldDistance(int fromX, int fromY, int toX, int toY)
        {
            int dx = toX - fromX, dy = toY - fromY;
            return Math.Sqrt(Math.Pow((dx - dy) * 32, 2) + Math.Pow((dx + dy) * 16, 2));
        }

        // Milliseconds for (speed + acceleration * t) * t to reach the distance
        private static double ShotFlight(double distance, int speed, int acceleration)
        {
            if (distance <= 0)
                return 0;
            if (acceleration == 0)
                return speed > 0 ? distance / speed * 1000 : 0;
            return (-speed + Math.Sqrt((double)speed * speed + 4.0 * acceleration * distance)) / (2.0 * acceleration) * 1000;
        }

        // Runs the work on the player's queue after the delay, or now
        private static void Later(Client client, TimeSpan delay, Func<Task> work)
        {
            if (delay <= TimeSpan.Zero)
                Kernel.Post(client, work);
            else
                _ = Task.Delay(delay).ContinueWith(_ => Kernel.Post(client, work), TaskScheduler.Default);
        }

        private static async Task StartXpStatusAsync(Client client, MagicType spell, DateTime now)
        {
            var character = client.Character;
            var flag = XpStatusOf(spell);
            await PayAsync(client, spell, now);
            client.Effects.Set(flag, now + TimeSpan.FromSeconds(XpStatusSeconds(spell)), now, spell.Power);
            if (spell.Sort == MagicSort.ShurikenVortex)
                client.NextVortexAt = now + VortexStep(spell);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.SendStatusAsync(client);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                character.Name, flag, XpStatusSeconds(spell), character.Name, spell.Name);
            LogCast(client, spell, character.X, character.Y, 1);
        }

        // Fatal Strike's spell while its status is on and its weapon in hand, or null
        public static MagicType FatalStrikeOf(Client client, DateTime now) =>
            client.Effects.Has(StatusFlags.FatalStrike, now) && Pirates.TryGetLearned(client, FatalStrike, out var spell) &&
            (spell.WeaponSubtype == 0 || Holds(client, spell.WeaponSubtype)) ? spell : null;

        public const ushort ShurikenVortex = 6010;

        private static TimeSpan VortexStep(MagicType spell) => TimeSpan.FromSeconds(Math.Max(1, spell.StepSeconds));

        // On the player's queue, from Recovery's tick: while Shuriken Vortex is on, it hits
        // around the ninja every step, wherever they've walked
        public static async Task VortexTickAsync(Client client, DateTime now)
        {
            var character = client.Character;
            if (character == null || client.Life.Dead || !client.Effects.Has(StatusFlags.ShurikenVortex, now) ||
                now < client.NextVortexAt || !Pirates.TryGetLearned(client, ShurikenVortex, out var spell))
                return;
            client.NextVortexAt = now + VortexStep(spell);
            await AttackAsync(client, spell, 0, character.X, character.Y, now, passive: true);
        }

        private static (Victim Victim, string Reason) FindTarget(Client client, MagicType spell, uint targetID, DateTime now,
            bool swing = false)
        {
            var character = client.Character;
            Victim victim = null;
            string reason;
            if (Training.TrainingGround.FindGoal(client, targetID) is Npc goal)
            {
                reason = Training.TrainingGround.Problem(client, goal, spell: !swing);
                if (reason == null)
                    victim = new Victim(null, null, goal, goal.X, goal.Y);
            }
            else if (Attacks.IsMonster(targetID))
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
                    victim = new Victim(monster, null, null, mx, my);
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
                    victim = new Victim(null, target, null, px, py);
            }

            if (victim != null &&
                MonsterAI.Distance(character.X, character.Y, victim.X, victim.Y) > spell.Distance + DistanceSlack)
                return (null, "too_far");
            return (victim, reason);
        }

        // Moves a living target to (x, y): a player at once, a monster a step at a time
        private static void Knock(Victim victim, ushort x, ushort y, byte direction)
        {
            if (victim.Player is Client player)
                Kernel.Post(player, () => player.Life.Dead ? Task.CompletedTask : Movement.PushAsync(player, x, y));
            else
                MonsterAI.Post(async () =>
                {
                    var monster = victim.Monster;
                    while (monster.Alive && (monster.X != x || monster.Y != y) &&
                        await Monsters.StepAsync(monster, Movement.DirectionTo(monster.X, monster.Y, x, y, direction), run: false))
                    {
                    }
                });
        }

        private static bool AimsAtDead(Client client, uint targetID) =>
            Attacks.IsMonster(targetID) && Kernel.Monsters.TryGet(client.Character.MapID, out var map) &&
            map.TryGet(targetID, out var monster, out _, out _) && !monster.Alive;

        private const ushort LiftHits = 6;

        private static void Hold(List<Victim> victims, DateTime until)
        {
            var monsters = victims.Where(v => v.Monster != null).Select(v => v.Monster).ToList();
            if (monsters.Count > 0)
                MonsterAI.Post(() =>
                {
                    foreach (var monster in monsters)
                        if (monster.NextMoveAt < until)
                            monster.NextMoveAt = until;
                    return Task.CompletedTask;
                });
        }

        // The cells past the victim on the line from the caster, up to range cells from the
        // caster, until one is blocked or taken
        private static IEnumerable<(ushort X, ushort Y)> KnockedBack(Client client, Victim victim, int range)
        {
            var character = client.Character;
            int dx = victim.X - character.X, dy = victim.Y - character.Y;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps == 0 || steps >= range)
                yield break;
            var taken = Around(client, DateTime.UtcNow).Where(v => v.ID != victim.ID).Select(v => (v.X, v.Y)).ToHashSet();
            int endX = character.X + dx * range / steps, endY = character.Y + dy * range / steps;
            foreach (var cell in Line(character.X, character.Y, endX, endY).Skip(steps))
            {
                if (Movement.CheckDash(client, cell.X, cell.Y) != null || taken.Contains(cell))
                    yield break;
                yield return cell;
            }
        }

        // Guards only in free PK mode, so an area spell doesn't make the caster flash by accident.
        // Training ground goals for attacks: stakes for a swing, scarecrows for a spell.
        private static IEnumerable<Victim> Around(Client client, DateTime now, bool goals = false, bool swing = false)
        {
            var character = client.Character;
            if (goals)
                foreach (var goal in Training.TrainingGround.InView(client, spell: !swing))
                    yield return new Victim(null, null, goal, goal.X, goal.Y);
            if (Kernel.Monsters != null && Kernel.Monsters.TryGet(character.MapID, out var monsters))
                foreach (var monster in monsters.InView(character.X, character.Y))
                    if (monster.Alive && (!monster.IsGuard || client.Pk.Mode == PkMode.Free) &&
                        monsters.TryGetPosition(monster, out var mx, out var my))
                        yield return new Victim(monster, null, null, mx, my);

            foreach (var player in client.Map.InView(character.X, character.Y, except: client))
                if (player.Character != null && !player.Life.Dead && now >= player.Life.ProtectedUntil &&
                    Pking.CanAttack(client, player) && client.Map.TryGetPosition(player, out var px, out var py))
                    yield return new Victim(null, player, null, px, py);
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
                case MagicSort.ShurikenVortex:
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
                return spell.IsPassive
                    ? Math.Max(ShareOf(monster.Health, spell), AgainstMonster(client, stats, Swing(spell), monster, now))
                    : ShareOf(monster.Health, spell);
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

        // A goal has no defense or dodge
        private static uint AgainstGoal(Client client, Stats stats, MagicType spell, Npc goal, DateTime now)
        {
            int level = client.Character.Level;
            if (spell.Sort == MagicSort.DecLife)
                return spell.IsPassive
                    ? Math.Max(ShareOf(Training.TrainingGround.LifeOf(goal), spell), AgainstGoal(client, stats, Swing(spell), goal, now))
                    : ShareOf(Training.TrainingGround.LifeOf(goal), spell);
            if (!spell.IsPercent)
                return Random.Shared.Next(100) < spell.Percent
                    ? Damage.Magic(stats.MagicDamage, spell.Power, level, goal.Level) : 0;
            uint damage = stats.Bow
                ? Damage.Archery(stats.MinDamage, stats.MaxDamage, level, goal.Level, 0, Random.Shared)
                : Damage.Melee(stats.MinDamage, stats.MaxDamage, level, goal.Level, 0, Random.Shared);
            return Buffs.AgainstMonster(client.Effects, (uint)Math.Max(1, damage * (ulong)spell.PowerPercent / 100), now);
        }

        private static uint AgainstPlayer(Client client, Stats stats, MagicType spell, Client target, DateTime now)
        {
            var targetStats = Stats.Of(target);
            if (spell.Sort == MagicSort.DecLife)
                return spell.IsPassive
                    ? Math.Max(ShareOf(target.Character.HealthPoints, spell), AgainstPlayer(client, stats, Swing(spell), target, now))
                    : ShareOf(target.Character.HealthPoints, spell);
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

        // Gaping Wounds goes off with a swing, so never hits for less than that swing would
        private static MagicType Swing(MagicType spell) =>
            spell with { Sort = MagicSort.Attack, Power = MagicType.PercentBase + 100 };

        // Cruel Shade and Gaping Wounds take their power's percentage of the target's life left
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
            // Bosses and guards shrug the fog off
            foreach (var victim in victims)
                victim.Missed = victim.Monster is { } monster && (monster.Type.Boss || monster.IsGuard) ||
                    Random.Shared.Next(100) >= spell.Percent;
            await PayAsync(client, spell, now);
            // Damage 1 marks a target the fog poisoned, 0 one it missed
            var effect = new MsgMagicEffect(client.ID, x, y, spell.Type) { MagicLevel = spell.Level };
            foreach (var victim in victims)
                effect.Targets.Add((victim.ID, victim.Missed ? 0u : 1u));
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
                if (victim.Missed)
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
                else if (victim.Monster?.IsGuard == true)
                    await Pking.FlashAsync(client, Pking.FlashTime, "attack_guard");
                else
                    victim.Monster?.Provoke(client.ID);
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
                Kernel.Post(member, () => member.Character == null || member.Life.Dead || !Recovery.HasXpSkill(member)
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

            var target = spell.IsXpSkill || spell.OnSelf ? client : Friendly(client, spell, targetID);
            await PayAsync(client, spell, now);
            if (status == StatusFlags.Superman || status == StatusFlags.Cyclone || status == StatusFlags.Oblivion)
                target.Energy.KoCount = 0;
            var (seconds, power) = status == StatusFlags.AzureShield ? AzureShieldOf(spell) : (spell.StepSeconds, spell.Power);
            target.Effects.Set(status, now + TimeSpan.FromSeconds(seconds), now, power);

            var character = target.Character;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.SendStatusAsync(target);
            LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                character.Name, status, seconds, client.Character.Name, spell.Name);
            LogCast(client, spell, character.X, character.Y, 1);
        }

        // Azure Shield's seconds and the damage it takes, by level, as its descriptions have
        // them; magictype gives it neither
        private static readonly (uint Seconds, int Absorbs)[] AzureShieldLevels =
            { (30, 1200), (35, 2800), (40, 5200), (50, 8000), (60, 12000) };

        private static (uint Seconds, int Absorbs) AzureShieldOf(MagicType spell) =>
            AzureShieldLevels[Math.Min(spell.Level, AzureShieldLevels.Length - 1)];

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

        // Poison Star keeps a player from potions for magictype's step, at its percent: less
        // against a higher battle power, as the client describes it, 5% less for each point
        private static async Task PoisonStarAsync(Client client, MagicType spell, uint targetID, DateTime now)
        {
            var (victim, reason) = FindTarget(client, spell, targetID, now);
            if (victim != null && victim.Player == null)
                (victim, reason) = (null, "not_player");
            if (victim == null)
            {
                Reject(client, spell.Type, reason);
                return;
            }

            var target = victim.Player;
            int above = (int)Attributes.BattlePower(target.Character) - (int)Attributes.BattlePower(client.Character);
            bool hit = Random.Shared.Next(100) < (int)spell.Percent - 5 * Math.Max(0, above);
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, victim.X, victim.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((target.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Pking.OnAttackAsync(client, target);
            if (hit)
                Kernel.Post(target, async () =>
                {
                    var at = DateTime.UtcNow;
                    if (target.Character == null || target.Life.Dead)
                        return;
                    target.Effects.Set(StatusFlags.NoPotion, at + TimeSpan.FromSeconds(spell.StepSeconds), at);
                    await Pking.SendStatusAsync(target);
                    LogEvents.For(typeof(Casting), LogEvents.StatusStarted).Information(
                        "{Name} got status 0x{Status:X} for {Seconds}s from {Caster}'s {MagicName}",
                        target.Character.Name, StatusFlags.NoPotion, spell.StepSeconds, client.Character.Name, spell.Name);
                });
            LogCast(client, spell, victim.X, victim.Y, hit ? 1 : 0);
            await GainExperienceAsync(client, spell, hit ? 1u : 0u);
        }

        private static async Task TransformAsync(Client client, MagicType spell, DateTime now)
        {
            if (!Transforming.TryGetType(spell, out var type))
            {
                Reject(client, spell.Type, "unknown_monster");
                return;
            }
            var character = client.Character;
            await PayAsync(client, spell, now);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, spell.Type) { MagicLevel = spell.Level };
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            await Transforming.StartAsync(client, type, spell.StepSeconds, now);
            LogCast(client, spell, character.X, character.Y, 1);
        }

        private static async Task SwitchCounterKillAsync(Client client, MagicType spell, DateTime now)
        {
            var character = client.Character;
            await PayAsync(client, spell, now);
            client.CounterKill = !client.CounterKill;
            await client.SendAsync(new MsgInteract(MsgInteract.InteractionType.CounterKillSwitch, client.ID, client.ID,
                character.X, character.Y, client.CounterKill ? 1u : 0u));
            LogEvents.For(typeof(Casting), client.CounterKill ? LogEvents.StatusStarted : LogEvents.StatusEnded).Information(
                "{Name} switched {MagicName} {State}", character.Name, spell.Name, client.CounterKill ? "on" : "off");
            LogCast(client, spell, character.X, character.Y, 0);
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
                ? (UInt128)(StatusFlags.Poisoned | StatusFlags.ToxicFog)
                : StatusFlags.Poisoned | StatusFlags.ToxicFog | StatusFlags.SoulShackle;
            foreach (var target in targets)
                Kernel.Post(target, async () =>
                {
                    var had = target.Effects.Status(DateTime.UtcNow) & flags;
                    if (had == UInt128.Zero)
                        return;
                    target.Effects.Remove(StatusFlags.Poisoned);
                    target.Effects.Remove(StatusFlags.ToxicFog);
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
            bool free = Training.TrainingGround.CostsNothing(client);
            if (spell.UseMana > 0 && !free)
            {
                character.ManaPoints -= spell.UseMana;
                await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Mana, character.ManaPoints));
            }
            await Recovery.UseStaminaAsync(client, StaminaOf(client, spell, now));
            if (spell.UseItem != 0 && !free && ItemToUse(client, spell) is Database.Models.DbItem item)
                await Quivers.UseAsync(client, item, spell.UseItemCount);
            if (spell.IsXpSkill && !InXpStatus(client, spell, now))
            {
                client.Effects.Remove(StatusFlags.XpFull);
                await Recovery.SetXpAsync(client, 0);
                await Pking.SendStatusAsync(client);
            }
        }

        // Experience from damage or healing, toward the spell's next level once the character
        // is high enough. A spell a rebirth put back to 0 goes up a level with any experience
        // from half its old level until it's back at it.
        public static async Task GainExperienceAsync(Client client, MagicType spell, uint amount)
        {
            if (amount == 0 || spell.NeedExperience == 0 || client.Character.Level < spell.NeedLevel ||
                !client.Skills.TryGetSpell(spell.Type, out var learned) || learned.Level != spell.Level)
                return;
            ulong experience = (ulong)learned.Experience + amount;
            ushort level = learned.Level;
            if (LevelsUp(learned, experience, spell.NeedExperience) &&
                Kernel.Magics.TryGet(spell.Type, (ushort)(level + 1), out _))
            {
                experience = experience >= spell.NeedExperience ? experience - spell.NeedExperience : 0;
                level++;
            }
            await SetAsync(client, spell.Type,
                learned with { Level = level, Experience = (uint)Math.Min(experience, uint.MaxValue) },
                learned.Level, "experience");
        }

        public static bool LevelsUp(SkillLevel learned, ulong experience, uint needExperience) =>
            experience >= needExperience ||
            learned.Level < learned.OldLevel && learned.Level >= learned.OldLevel / 2;

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

        // The client takes the spell off its skill window on MsgAction 109 with the type in Command
        public static async Task<bool> ForgetAsync(Client client, ushort magicType, string reason)
        {
            if (!client.Skills.ForgetSpell(magicType))
                return false;
            await client.SendAsync(MsgAction.RemoveSpell(client.ID, magicType));
            LogEvents.For(typeof(Casting), LogEvents.MagicForgotten).Information(
                "{Name} forgot magic {MagicType} ({Reason})", client.Character.Name, magicType, reason);
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

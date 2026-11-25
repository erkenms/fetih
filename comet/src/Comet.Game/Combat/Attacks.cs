namespace Comet.Game.Combat
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Magic;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgInteract;

    // The client asks once to attack a monster or a player; the server keeps swinging at the
    // weapon's speed until the target dies, the player moves, dies, or asks for something else.
    // Swings run on the attacker's own packet queue; a hit on a player is applied on the
    // target's queue, so a player's health only changes there. A learned weapon skill for the
    // weapon in hand may go off in place of a melee swing.
    public static class Attacks
    {
        // How far past the weapon's range a target may be, for a monster that just stepped away
        private const int RangeSlack = 1;

        // The shortest time between a player's swings, whatever the weapon says
        public static TimeSpan PlayerInterval = TimeSpan.FromMilliseconds(1500);

        // During Fatal Strike the client sends a melee click on a monster for each strike, from
        // as far as the spell's distance. Each click gets its own turn, so clicks that latency
        // bunches up all strike, a gap apart; turns keep to what a hand on a mouse can do, so
        // many in any few seconds and in a minute. A click whose turn is too far off is dropped.
        public static TimeSpan FatalStrikeGap { get; set; } = TimeSpan.FromMilliseconds(100);
        public static readonly (TimeSpan Window, int Most)[] FatalStrikeLimits =
        {
            (TimeSpan.FromSeconds(5), 20),
            (TimeSpan.FromSeconds(60), 144),
        };
        public static readonly TimeSpan FatalStrikeLongestWait = TimeSpan.FromSeconds(1);

        // The turn of a strike clicked now, counted in, or null if it would wait too long
        public static DateTime? ReserveFatalStrike(System.Collections.Generic.Queue<DateTime> strikes, DateTime now)
        {
            var longest = FatalStrikeLimits.Max(l => l.Window);
            while (strikes.Count > 0 && strikes.Peek() <= now - longest)
                strikes.Dequeue();
            var turns = strikes.ToArray();
            var at = turns.Length > 0 && turns[^1] + FatalStrikeGap > now ? turns[^1] + FatalStrikeGap : now;
            foreach (var (window, most) in FatalStrikeLimits)
                if (turns.Length >= most && turns[^most] + window > at)
                    at = turns[^most] + window;
            if (at - now > FatalStrikeLongestWait)
                return null;
            strikes.Enqueue(at);
            return at;
        }

        public static async Task RequestAsync(Client client, MsgInteract request)
        {
            if (request.Action != InteractionType.Attack && request.Action != InteractionType.Shoot)
            {
                LogEvents.For(typeof(Attacks), LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                    request.Type, request.Action, request.Length, Convert.ToHexString(request.Encode()));
                return;
            }

            Stop(client);
            string reason =
                client.Character == null ? "no_character" :
                request.SenderID != client.ID ? "wrong_id" :
                client.Life.Dead ? "dead" :
                !IsMonster(request.TargetID) && !Kernel.Players.TryGet(request.TargetID, out _) &&
                    Training.TrainingGround.FindGoal(client, request.TargetID) == null &&
                    Social.GuildWar.FindTarget(client, request.TargetID) == null &&
                    Tournaments.GuildPk.FindFlag(client, request.TargetID) == null ? "unknown_target" :
                null;
            if (reason != null)
            {
                Reject(client, request.TargetID, reason);
                return;
            }

            await Riding.DismountToFightAsync(client, "attack");
            var now = DateTime.UtcNow;
            if (request.Action == InteractionType.Attack &&
                (IsMonster(request.TargetID) || Training.TrainingGround.FindGoal(client, request.TargetID) != null) &&
                Casting.FatalStrikeOf(client, now) != null)
            {
                if (ReserveFatalStrike(client.FatalStrikes, now) is not DateTime turn)
                {
                    Reject(client, request.TargetID, "too_fast");
                    return;
                }
                uint targetID = request.TargetID;
                _ = Task.Delay(turn - now).ContinueWith(_ => Kernel.Post(client, () =>
                {
                    int run = ++client.AttackRun;
                    client.AttackTarget = targetID;
                    client.AttackShooting = false;
                    client.NextAttackAt = DateTime.MinValue;
                    return SwingAsync(client, run);
                }), TaskScheduler.Default);
                return;
            }
            client.AttackTarget = request.TargetID;
            client.AttackShooting = request.Action == InteractionType.Shoot;
            await SwingAsync(client, client.AttackRun);
        }

        // Ends the player's attack; a swing already waiting finds it changed and stops
        public static void Stop(Client client)
        {
            client.AttackRun++;
            client.AttackTarget = 0;
        }

        private static async Task SwingAsync(Client client, int run)
        {
            if (run != client.AttackRun || client.Character == null || client.Life.Dead)
                return;
            var now = DateTime.UtcNow;
            if (client.Effects.Has(StatusFlags.Stun, now))
            {
                Stop(client);
                Reject(client, client.AttackTarget, "stunned");
                return;
            }
            if (now < client.NextAttackAt)
            {
                Schedule(client, run, client.NextAttackAt - now);
                return;
            }

            if (Training.TrainingGround.FindGoal(client, client.AttackTarget) is Npc goal)
            {
                await SwingAtGoalAsync(client, goal, run, now);
                return;
            }
            if (Social.GuildWar.FindTarget(client, client.AttackTarget) is Npc gwTarget)
            {
                await SwingAtStructureAsync(client, gwTarget, run, now, Social.GuildWar.Problem, Social.GuildWar.AfterHitAsync);
                return;
            }
            if (Tournaments.GuildPk.FindFlag(client, client.AttackTarget) is Npc flag)
            {
                await SwingAtStructureAsync(client, flag, run, now, Tournaments.GuildPk.FlagProblem, Tournaments.GuildPk.AfterFlagHitAsync);
                return;
            }
            if (!IsMonster(client.AttackTarget))
            {
                await SwingAtPlayerAsync(client, run, now);
                return;
            }

            var character = client.Character;
            uint targetID = client.AttackTarget;
            Monster monster = null;
            ushort x = 0, y = 0;
            var stats = Stats.Of(client);
            var arrows = client.Equipment?[ItemPosition.LeftHand];
            bool shooting = client.AttackShooting;
            var fatal = shooting ? null : Casting.FatalStrikeOf(client, now);
            string reason =
                !Kernel.Monsters.TryGet(character.MapID, out var map) ||
                !map.TryGet(targetID, out monster, out x, out y) ? "not_found" :
                !monster.Alive ? "dead_target" :
                monster.IsGuard && client.Pk.Mode != PkMode.Free ? "guard_not_free_pk" :
                fatal != null ? (MonsterAI.Distance(character.X, character.Y, x, y) > fatal.Distance + RangeSlack ? "too_far" : null) :
                WeaponProblem(stats, shooting, arrows, MonsterAI.Distance(character.X, character.Y, x, y));
            if (reason != null)
            {
                Stop(client);
                if (reason != "dead_target")
                    Reject(client, targetID, reason);
                return;
            }

            var interval = Interval(client, stats, now);
            client.NextAttackAt = fatal != null ? now : now + interval;
            if (!shooting && fatal == null && await Casting.TryPassiveAsync(client, targetID, x, y, now))
            {
                Schedule(client, run, interval);
                return;
            }
            await Durability.WearOnAttackAsync(client);
            var type = monster.Type;
            uint damage = 0;
            if (Buffs.HasAccuracy(client.Effects, now) || Damage.Hits(type.Dodge, targetIsMonster: true, Random.Shared))
                damage = shooting
                    ? Damage.Archery(stats.MinDamage, stats.MaxDamage, character.Level, type.Level, type.Dodge, Random.Shared)
                    : Damage.Melee(stats.MinDamage, stats.MaxDamage, character.Level, type.Level, type.Defense, Random.Shared);
            damage = Buffs.AgainstMonster(client.Effects, damage, now);
            damage = Damage.ApplyCritical(damage, stats.CriticalStrike, 0, 1.5, Random.Shared);
            if (fatal != null && damage > 0)
                damage = (uint)Math.Min(uint.MaxValue, (ulong)damage * (uint)fatal.PowerPercent / 100);
            if (shooting)
                await Quivers.UseAsync(client, arrows, 1);
            if (type.IsGuard)
                await Pking.FlashAsync(client, Pking.FlashTime, "attack_guard");

            if (damage == 0)
                monster.Provoke(client.ID);
            var (dealt, killed) = damage > 0 ? monster.Damage(damage, client.ID, now) : (0u, false);
            if (damage > 0 && dealt == 0 && !killed)
            {
                Stop(client); // someone else killed it first
                return;
            }

            // The ninja steps onto what Fatal Strike hits, bosses and guards aside
            if (fatal != null && !type.Boss && !monster.IsGuard && (character.X != x || character.Y != y))
                await Movement.DashAsync(client, x, y, MsgAction.NinjaStep(client.ID, monster.Identity, x, y,
                    Movement.DirectionTo(character.X, character.Y, x, y, character.Direction)));
            var hit = new MsgInteract(fatal != null ? InteractionType.FatalStrike : shooting ? InteractionType.Shoot : InteractionType.Attack,
                client.ID, monster.Identity, x, y, damage);
            await SendAroundAsync(client, monster, hit);
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{Name} hit {MonsterName} {MonsterId} for {Damage} ({Dealt} dealt, {Kind})",
                character.Name, type.Name, monster.Identity, damage, dealt, fatal != null ? "fatal_strike" : shooting ? "shoot" : "melee");

            // Each click is one Fatal Strike
            if (killed || fatal != null)
                Stop(client);
            if (!killed && damage > 0)
            {
                await Pirates.AfterDamageAsync(client, monster.Identity, x, y, now);
                await Casting.AfterHitAsync(client, monster.Identity, now);
            }
            if (!type.IsGuard)
                await Proficiency.GainAsync(client, dealt);
            await AfterMonsterHitAsync(client, monster, x, y, dealt, killed);
            if (!killed && fatal == null)
                Schedule(client, run, interval);
        }

        public static async Task AfterMonsterHitAsync(Client client, Monster monster, ushort x, ushort y,
            uint dealt, bool killed)
        {
            var character = client.Character;
            var type = monster.Type;
            ulong experience = type.IsGuard ? 0 : Damage.Experience(dealt, character.Level, type.Level);
            if (killed)
            {
                var now = DateTime.UtcNow;
                if (!type.IsGuard)
                    experience = Buffs.KillExperience(client.Effects, experience + Damage.KillBonus(type.Life), now);
                uint data = Buffs.CountKill(client, now);
                await SendAroundAsync(client, monster, new MsgInteract(InteractionType.Kill, client.ID, monster.Identity, x, y, data));
                if (Buffs.EndOblivion(client, now))
                    await Pking.SendStatusAsync(client);
                LogEvents.For(typeof(Attacks), LogEvents.MonsterKilled).Information(
                    "{Name} killed {MonsterName} {MonsterId} (type {MonsterTypeId}) on map {MapId} at {X},{Y}",
                    character.Name, type.Name, monster.Identity, type.ID, monster.Map.Id, x, y);
                await Tournaments.GuildPk.OnMonsterKilledAsync(client, monster.Map.Id);
            }

            if (experience > 0)
            {
                experience = DoubleExp.Scale(client.ID, experience, DateTime.UtcNow);
                await Progress.AwardExperienceAsync(client, experience, "monster");
                await Training.GodExp.AddHuntingAsync(client, experience);
            }
            if (killed && !type.IsGuard)
                ShareKill(client, monster);
            if (killed)
                await Loot.DropAsync(client, monster, x, y, Random.Shared);
        }

        // The killer's living teammates on the map within this many cells get the kill bonus as
        // if the kill were theirs, up to 360 per level of theirs
        public const int TeamShareDistance = 32;
        public const int TeamShareMaxPerLevel = 360;

        private static void ShareKill(Client client, Monster monster)
        {
            var map = client.Map;
            var character = client.Character;
            ushort x = character.X, y = character.Y;
            foreach (var member in Social.Teams.OthersOf(client))
                Kernel.Post(member, () => TeamExperienceAsync(member, character.Name, map, x, y, monster.Type));
        }

        private static async Task TeamExperienceAsync(Client member, string killer, GameMap<Client> map,
            ushort x, ushort y, MonsterType type)
        {
            var character = member.Character;
            if (character == null || member.Life.Dead || member.Map != map ||
                MonsterAI.Distance(character.X, character.Y, x, y) > TeamShareDistance)
                return;
            ulong experience = TeamExperience(Damage.KillBonus(type.Life), character.Level, type.Level);
            await Progress.AwardExperienceAsync(member, experience, "team");
            await Chat.Talk.NoticeAsync(member, $"You gained {experience} team experience.");
            LogEvents.For(typeof(Attacks), LogEvents.TeamExperience).Information(
                "{Name} gained {Experience} team experience from {Killer}'s kill of {MonsterName}",
                character.Name, experience, killer, type.Name);
        }

        public static ulong TeamExperience(ulong bonus, int memberLevel, int monsterLevel) =>
            Math.Clamp(Damage.Experience((uint)Math.Min(bonus, uint.MaxValue), memberLevel, monsterLevel),
                1UL, (ulong)(memberLevel * TeamShareMaxPerLevel));

        private static TimeSpan Interval(Client client, Stats stats, DateTime now)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(stats.AttackSpeed, PlayerInterval.TotalMilliseconds));
            return client.Effects.Has(StatusFlags.Cyclone, now) ? interval / 3 : interval;
        }

        // A stake takes swings and shots, which always hit, and the weapon skills going off in
        // their place; Fatal Strike, a skill, strikes scarecrows instead
        private static async Task SwingAtGoalAsync(Client client, Npc goal, int run, DateTime now)
        {
            var character = client.Character;
            var stats = Stats.Of(client);
            var arrows = client.Equipment?[ItemPosition.LeftHand];
            bool shooting = client.AttackShooting;
            var fatal = shooting ? null : Casting.FatalStrikeOf(client, now);
            int distance = MonsterAI.Distance(character.X, character.Y, goal.X, goal.Y);
            string reason = Training.TrainingGround.Problem(client, goal, spell: fatal != null) ??
                (fatal != null ? (distance > fatal.Distance + RangeSlack ? "too_far" : null) : WeaponProblem(stats, shooting, arrows, distance));
            if (reason != null)
            {
                Stop(client);
                Reject(client, goal.NpcID, reason);
                return;
            }

            var interval = Interval(client, stats, now);
            client.NextAttackAt = fatal != null ? now : now + interval;
            if (!shooting && fatal == null && await Casting.TryPassiveAsync(client, goal.NpcID, goal.X, goal.Y, now))
            {
                Schedule(client, run, interval);
                return;
            }
            uint damage = shooting
                ? Damage.Archery(stats.MinDamage, stats.MaxDamage, character.Level, goal.Level, 0, Random.Shared)
                : Damage.Melee(stats.MinDamage, stats.MaxDamage, character.Level, goal.Level, 0, Random.Shared);
            damage = Buffs.AgainstMonster(client.Effects, damage, now);
            if (fatal != null)
                damage = (uint)Math.Min(uint.MaxValue, (ulong)damage * (uint)fatal.PowerPercent / 100);
            var hit = new MsgInteract(fatal != null ? InteractionType.FatalStrike : shooting ? InteractionType.Shoot : InteractionType.Attack,
                client.ID, goal.NpcID, goal.X, goal.Y, damage);
            await client.SendAsync(hit);
            await Movement.SendToViewAsync(client, hit);
            await Training.TrainingGround.AfterHitAsync(client, goal, damage,
                fatal != null ? "fatal_strike" : shooting ? "shoot" : "melee");
            if (fatal != null)
                Stop(client);
            else
                Schedule(client, run, interval);
        }

        // A Guild War pole or gate, or a Guild PK Tournament flag
        private static async Task SwingAtStructureAsync(Client client, Npc target, int run, DateTime now,
            Func<Client, Npc, string> problem, Func<Client, Npc, uint, string, Task> afterHit)
        {
            var character = client.Character;
            var stats = Stats.Of(client);
            var arrows = client.Equipment?[ItemPosition.LeftHand];
            bool shooting = client.AttackShooting;
            int distance = MonsterAI.Distance(character.X, character.Y, target.X, target.Y);
            string reason = problem(client, target) ?? WeaponProblem(stats, shooting, arrows, distance);
            if (reason != null)
            {
                Stop(client);
                Reject(client, target.NpcID, reason);
                return;
            }

            var interval = Interval(client, stats, now);
            client.NextAttackAt = now + interval;
            if (!shooting && await Casting.TryPassiveAsync(client, target.NpcID, target.X, target.Y, now))
            {
                Schedule(client, run, interval);
                return;
            }
            await Durability.WearOnAttackAsync(client);
            uint damage = shooting
                ? Damage.Archery(stats.MinDamage, stats.MaxDamage, character.Level, target.Level, 0, Random.Shared)
                : Damage.Melee(stats.MinDamage, stats.MaxDamage, character.Level, target.Level, 0, Random.Shared);
            damage = Buffs.AgainstMonster(client.Effects, damage, now);
            if (shooting)
                await Quivers.UseAsync(client, arrows, 1);

            var hit = new MsgInteract(shooting ? InteractionType.Shoot : InteractionType.Attack,
                client.ID, target.NpcID, target.X, target.Y, damage);
            await client.SendAsync(hit);
            await Movement.SendToViewAsync(client, hit);
            await afterHit(client, target, damage, shooting ? "shoot" : "melee");

            if (problem(client, target) != null)
                Stop(client);
            else
                Schedule(client, run, interval);
        }

        private static async Task SwingAtPlayerAsync(Client client, int run, DateTime now)
        {
            var character = client.Character;
            uint targetID = client.AttackTarget;
            var stats = Stats.Of(client);
            var arrows = client.Equipment?[ItemPosition.LeftHand];
            bool shooting = client.AttackShooting;
            Client target = null;
            ushort x = 0, y = 0;
            string reason =
                !Kernel.Players.TryGet(targetID, out target) || target.Character == null ||
                target.Map != client.Map || !target.Map.TryGetPosition(target, out x, out y) ? "not_found" :
                target.Life.Dead ? "dead_target" :
                now < target.Life.ProtectedUntil ? "protected" :
                Pking.IsPeaceful(character.MapID) ? "peaceful_map" :
                !Pking.CanAttack(client, target) ? "pk_mode" :
                WeaponProblem(stats, shooting, arrows, MonsterAI.Distance(character.X, character.Y, x, y));
            if (reason != null)
            {
                Stop(client);
                if (reason != "dead_target")
                    Reject(client, targetID, reason);
                return;
            }

            var interval = Interval(client, stats, now);
            client.NextAttackAt = now + interval;
            if (!shooting && await Casting.TryPassiveAsync(client, targetID, x, y, now))
            {
                Schedule(client, run, interval);
                return;
            }
            await Durability.WearOnAttackAsync(client);
            await Pking.OnAttackAsync(client, target);
            int attack = Buffs.AgainstPlayer(client.Effects, Damage.Roll(stats.MinDamage, stats.MaxDamage, Random.Shared), now);
            bool sure = Buffs.HasAccuracy(client.Effects, now);
            if (shooting)
                await Quivers.UseAsync(client, arrows, 1);
            int critical = stats.CriticalStrike;
            Kernel.Post(target, () => PlayerHitsPlayerAsync(client, target, attack, critical, shooting, sure));
            Schedule(client, run, interval);
        }

        // On the target's queue: their defense and dodge decide what the attacker's roll does
        private static async Task PlayerHitsPlayerAsync(Client attacker, Client target, int attack, int critical,
            bool shooting, bool sure)
        {
            var character = target.Character;
            var attackerCharacter = attacker.Character;
            if (character == null || attackerCharacter == null || target.Life.Dead || target.Map != attacker.Map)
                return;

            if (!shooting && await CounterKill.TryAsync(target, attacker, DateTime.UtcNow))
                return;
            var stats = Stats.Of(target);
            int defense = Buffs.Defense(target.Effects, stats.Defense, DateTime.UtcNow);
            uint damage = !shooting && Buffs.Flying(target.Effects, DateTime.UtcNow) ? 0 :
                !sure && !Damage.Hits(stats.Dodge, targetIsMonster: false, Random.Shared) ? 0 :
                stats.Taken(shooting ? Damage.PlayerArchery(attack, stats.Dodge) : Damage.PlayerMelee(attack, defense));
            damage = Damage.ApplyCritical(damage, critical, stats.Immunity, 1.5, Random.Shared);
            damage = Damage.ApplyBlock(damage, stats.Block, Random.Shared);
            if (!shooting)
                await FacePlayerTowardAsync(target, attackerCharacter.X, attackerCharacter.Y);
            var hit = new MsgInteract(shooting ? InteractionType.Shoot : InteractionType.Attack,
                attacker.ID, target.ID, character.X, character.Y, damage);
            await target.SendAsync(hit);
            await Movement.SendToViewAsync(target, hit);
            if (damage > 0)
            {
                await Pirates.AfterDamageAsync(attacker, target.ID, character.X, character.Y, DateTime.UtcNow);
                await Casting.AfterHitAsync(attacker, target.ID, DateTime.UtcNow);
            }
            await DamagePlayerAsync(attacker, target, damage, shooting ? "shoot" : "melee");
        }

        private static async Task FacePlayerTowardAsync(Client target, int x, int y)
        {
            var character = target.Character;
            if (character == null)
                return;
            byte direction = Movement.DirectionTo(character.X, character.Y, x, y, character.Direction);
            if (direction == character.Direction)
                return;
            character.Direction = direction;
            await Movement.SendToViewAsync(target, MsgAction.Turn(target.ID, direction));
        }

        // On the target's queue, after what shows the hit was sent: health, and death
        public static async Task DamagePlayerAsync(Client attacker, Client target, uint damage, string kind)
        {
            var character = target.Character;
            var attackerCharacter = attacker.Character;
            if (character == null || attackerCharacter == null || target.Life.Dead || target.Map != attacker.Map)
                return;
            damage = await AbsorbAsync(target, damage, DateTime.UtcNow);
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;
            await target.SendAsync(new MsgUserAttrib(target.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            if (damage > 0)
            {
                await Durability.WearOnHitAsync(target);
                if (character.HealthPoints > 0)
                    await Pirates.OnDamagedAsync(target);
            }
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{Attacker} hit {Name} for {Damage}; {Health} health left ({Kind})",
                attackerCharacter.Name, character.Name, damage, character.HealthPoints, kind);
            await Arena.Qualifier.OnHitAsync(attacker, target, dealt);
            await Tournaments.ElitePk.OnHitAsync(attacker, target, dealt);
            await Tournaments.TeamPk.OnHitAsync(attacker, target, dealt);

            if (character.HealthPoints > 0)
                return;
            bool guilty = Pking.IsGuilty(target, DateTime.UtcNow);
            await Death.DieAsync(target, attacker.ID, attackerCharacter.Name);
            if (!await Arena.Qualifier.OnKillAsync(attacker, target) &&
                !await Tournaments.ElitePk.OnKillAsync(attacker, target) &&
                !await Tournaments.TeamPk.OnKillAsync(attacker, target) &&
                !await Tournaments.GuildPk.OnKillAsync(attacker, target) &&
                !await Social.ClanWar.OnKillAsync(attacker, target))
            {
                await Pking.OnKillAsync(attacker, target, guilty);
                await Social.Relationships.AddEnemyAsync(target, attacker);
            }
        }

        // What's left of the damage after the player's Azure Shield, which may end; during
        // Shuriken Vortex all damage is 1, as its description says
        private static async Task<uint> AbsorbAsync(Client client, uint damage, DateTime now)
        {
            if (damage > 0 && client.Effects.Has(StatusFlags.ShurikenVortex, now))
                return 1;
            bool shielded = client.Effects.Has(StatusFlags.AzureShield, now);
            uint left = Buffs.Absorb(client.Effects, damage, now);
            if (shielded && !client.Effects.Has(StatusFlags.AzureShield, now))
                await Pking.SendStatusAsync(client);
            return left;
        }

        // Runs on the player's queue, posted by the monster loop
        public static async Task MonsterHitsPlayerAsync(Monster monster, Client client)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            if (character == null || client.Life.Dead || now < client.Life.ProtectedUntil || !monster.Alive ||
                monster.IsGuard && !Pking.IsGuilty(client, now) ||
                client.Map?.Id != monster.Map.Id ||
                MonsterAI.Distance(monster.X, monster.Y, character.X, character.Y) > Math.Max(1, monster.Type.AttackRange) + RangeSlack)
                return;

            if (await CounterKill.TryAsync(client, monster, now))
                return;
            var stats = Stats.Of(client);
            var type = monster.Type;
            bool flownOver = Buffs.Flying(client.Effects, now) && type.AttackRange <= Buffs.MeleeRange;
            uint damage = !flownOver && Damage.Hits(stats.Dodge, targetIsMonster: false, Random.Shared) ? stats.Taken(
                Damage.MonsterMelee(type.AttackMin, type.AttackMax, Buffs.Defense(client.Effects, stats.Defense, now), Random.Shared)) : 0;
            damage = Damage.ApplyBlock(damage, stats.Block, Random.Shared);
            damage = await AbsorbAsync(client, damage, now);
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;

            await FacePlayerTowardAsync(client, monster.X, monster.Y);
            var hit = new MsgInteract(InteractionType.Attack, monster.Identity, client.ID, character.X, character.Y, damage);
            await client.SendAsync(hit);
            await Movement.SendToViewAsync(client, hit);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            if (damage > 0)
            {
                await Durability.WearOnHitAsync(client);
                if (character.HealthPoints > 0)
                    await Pirates.OnDamagedAsync(client);
            }
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{MonsterName} {MonsterId} hit {Name} for {Damage}; {Health} health left",
                type.Name, monster.Identity, character.Name, damage, character.HealthPoints);

            if (character.HealthPoints > 0)
                return;
            await Death.DieAsync(client, monster.Identity, type.Name);
            if (type.IsGuard)
                await Pking.OnKilledByGuardAsync(client);
        }

        // Runs on the player's queue, after the monster's MsgMagicEffect was sent
        public static async Task MonsterSpellHitsPlayerAsync(Monster monster, Client client, uint damage)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            if (character == null || client.Life.Dead || now < client.Life.ProtectedUntil ||
                client.Map?.Id != monster.Map.Id)
                return;

            damage = await AbsorbAsync(client, damage, now);
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;

            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            if (damage > 0)
            {
                await Durability.WearOnHitAsync(client);
                if (character.HealthPoints > 0)
                    await Pirates.OnDamagedAsync(client);
            }
            var type = monster.Type;
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{MonsterName} {MonsterId} hit {Name} for {Damage}; {Health} health left (magic)",
                type.Name, monster.Identity, character.Name, damage, character.HealthPoints);

            if (character.HealthPoints > 0)
                return;
            await Death.DieAsync(client, monster.Identity, type.Name);
            if (type.IsGuard)
                await Pking.OnKilledByGuardAsync(client);
        }

        // Guards against monsters, on the monster loop
        public static async Task MonsterHitsMonsterAsync(Monster attacker, Monster victim, DateTime now, Random random)
        {
            uint damage = Damage.Hits(victim.Type.Dodge, targetIsMonster: true, random)
                ? Damage.MonsterMelee(attacker.Type.AttackMin, attacker.Type.AttackMax, victim.Type.Defense, random) : 0;
            if (damage == 0)
                victim.Provoke(attacker.Identity);
            var (dealt, killed) = damage > 0 ? victim.Damage(damage, attacker.Identity, now) : (0u, false);
            if (damage > 0 && dealt == 0 && !killed)
                return;

            await Monsters.SendToViewAsync(victim,
                new MsgInteract(InteractionType.Attack, attacker.Identity, victim.Identity, victim.X, victim.Y, damage));
            if (!killed)
                return;
            attacker.Target = 0;
            await Monsters.SendToViewAsync(victim,
                new MsgInteract(InteractionType.Kill, attacker.Identity, victim.Identity, victim.X, victim.Y, 1));
            LogEvents.For(typeof(Attacks), LogEvents.MonsterKilled).Information(
                "{Killer} {KillerId} killed {MonsterName} {MonsterId} (type {MonsterTypeId}) on map {MapId} at {X},{Y}",
                attacker.Type.Name, attacker.Identity, victim.Type.Name, victim.Identity, victim.Type.ID,
                victim.Map.Id, victim.X, victim.Y);
        }

        public static bool IsMonster(uint id) => id >= Monster.FirstID && id <= Monster.LastID;

        private static string WeaponProblem(Stats stats, bool shooting, Database.Models.DbItem arrows, int distance) =>
            shooting != stats.Bow ? (shooting ? "no_bow" : "bow_melee") :
            shooting && (arrows == null || !ItemSlots.IsArrow(arrows.TypeID) || arrows.Amount == 0) ? "no_arrows" :
            distance > stats.Range + RangeSlack ? "too_far" :
            null;

        private static void Schedule(Client client, int run, TimeSpan delay) =>
            _ = Task.Delay(delay).ContinueWith(_ => Kernel.Post(client, () => SwingAsync(client, run)),
                TaskScheduler.Default);

        public static async Task SendAroundAsync(Client client, Monster monster, IPacket packet)
        {
            await client.SendAsync(packet);
            foreach (var player in Monsters.PlayersInView(monster))
                if (player != client)
                    await player.SendAsync(packet);
        }

        public static void Reject(Client client, uint targetID, string reason) =>
            LogEvents.For(typeof(Attacks), LogEvents.CombatRejected).Warning(
                "{Name} can't attack {TargetId}: {Reason}", client.Character?.Name, targetID, reason);
    }
}

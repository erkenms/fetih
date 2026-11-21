namespace Comet.Game.Combat
{
    using System;
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
    // target's queue, so a player's health only changes there.
    public static class Attacks
    {
        // How far past the weapon's range a target may be, for a monster that just stepped away
        private const int RangeSlack = 1;

        // The shortest time between a player's swings, whatever the weapon says
        public static TimeSpan PlayerInterval = TimeSpan.FromMilliseconds(1500);

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
                !IsMonster(request.TargetID) && !Kernel.Players.TryGet(request.TargetID, out _) ? "unknown_target" :
                null;
            if (reason != null)
            {
                Reject(client, request.TargetID, reason);
                return;
            }

            await Riding.DismountToFightAsync(client, "attack");
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
            if (now < client.NextAttackAt)
            {
                Schedule(client, run, client.NextAttackAt - now);
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
            string reason =
                !Kernel.Monsters.TryGet(character.MapID, out var map) ||
                !map.TryGet(targetID, out monster, out x, out y) ? "not_found" :
                !monster.Alive ? "dead_target" :
                monster.IsGuard && client.Pk.Mode != PkMode.Free ? "guard_not_free_pk" :
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
            var type = monster.Type;
            uint damage = 0;
            if (Buffs.HasAccuracy(client.Effects, now) || Damage.Hits(type.Dodge, targetIsMonster: true, Random.Shared))
                damage = shooting
                    ? Damage.Archery(stats.MinDamage, stats.MaxDamage, character.Level, type.Level, type.Dodge, Random.Shared)
                    : Damage.Melee(stats.MinDamage, stats.MaxDamage, character.Level, type.Level, type.Defense, Random.Shared);
            damage = Buffs.AgainstMonster(client.Effects, damage, now);
            if (shooting)
                await UseArrowAsync(client, arrows);
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

            var hit = new MsgInteract(shooting ? InteractionType.Shoot : InteractionType.Attack,
                client.ID, monster.Identity, x, y, damage);
            await SendAroundAsync(client, monster, hit);
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{Name} hit {MonsterName} {MonsterId} for {Damage} ({Dealt} dealt, {Kind})",
                character.Name, type.Name, monster.Identity, damage, dealt, shooting ? "shoot" : "melee");

            if (killed)
                Stop(client);
            if (!type.IsGuard)
                await Proficiency.GainAsync(client, dealt);
            await AfterMonsterHitAsync(client, monster, x, y, dealt, killed);
            if (!killed)
                Schedule(client, run, interval);
        }

        // After a player's hit on a monster: experience, and for a kill the kill message (with
        // the XP skill's KO count) and the loot. Hitting guards teaches nothing.
        public static async Task AfterMonsterHitAsync(Client client, Monster monster, ushort x, ushort y,
            uint dealt, bool killed)
        {
            var character = client.Character;
            var type = monster.Type;
            ulong experience = type.IsGuard ? 0 : Damage.Experience(dealt, character.Level, type.Level);
            if (killed)
            {
                if (!type.IsGuard)
                    experience += Damage.KillBonus(type.Life);
                uint data = Buffs.CountKill(client, DateTime.UtcNow);
                await SendAroundAsync(client, monster, new MsgInteract(InteractionType.Kill, client.ID, monster.Identity, x, y, data));
                LogEvents.For(typeof(Attacks), LogEvents.MonsterKilled).Information(
                    "{Name} killed {MonsterName} {MonsterId} (type {MonsterTypeId}) on map {MapId} at {X},{Y}",
                    character.Name, type.Name, monster.Identity, type.ID, monster.Map.Id, x, y);
            }

            if (experience > 0)
            {
                await Progress.AwardExperienceAsync(client, experience, "monster");
                await Training.GodExp.AddHuntingAsync(client, experience);
            }
            if (killed && !type.IsGuard)
                ShareKill(client, monster);
            if (killed)
                await Loot.DropAsync(client, monster, x, y, Random.Shared);
        }

        // TQ's (WCO): the killer's living teammates on the map within this many cells get the
        // kill bonus as if the kill were theirs, up to 360 per level of theirs
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

        // Cyclone swings three times as fast
        private static TimeSpan Interval(Client client, Stats stats, DateTime now)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(stats.AttackSpeed, PlayerInterval.TotalMilliseconds));
            return client.Effects.Has(StatusFlags.Cyclone, now) ? interval / 3 : interval;
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
            await Pking.OnAttackAsync(client, target);
            int attack = Buffs.AgainstPlayer(client.Effects, Damage.Roll(stats.MinDamage, stats.MaxDamage, Random.Shared), now);
            bool sure = Buffs.HasAccuracy(client.Effects, now);
            if (shooting)
                await UseArrowAsync(client, arrows);
            Kernel.Post(target, () => PlayerHitsPlayerAsync(client, target, attack, shooting, sure));
            Schedule(client, run, interval);
        }

        // On the target's queue: their defense and dodge decide what the attacker's roll does
        private static async Task PlayerHitsPlayerAsync(Client attacker, Client target, int attack, bool shooting, bool sure)
        {
            var character = target.Character;
            var attackerCharacter = attacker.Character;
            if (character == null || attackerCharacter == null || target.Life.Dead || target.Map != attacker.Map)
                return;

            var stats = Stats.Of(target);
            int defense = Buffs.Defense(target.Effects, stats.Defense, DateTime.UtcNow);
            uint damage = !sure && !Damage.Hits(stats.Dodge, targetIsMonster: false, Random.Shared) ? 0 :
                stats.Taken(shooting ? Damage.PlayerArchery(attack, stats.Dodge) : Damage.PlayerMelee(attack, defense));
            if (!shooting)
                await FacePlayerTowardAsync(target, attackerCharacter.X, attackerCharacter.Y);
            var hit = new MsgInteract(shooting ? InteractionType.Shoot : InteractionType.Attack,
                attacker.ID, target.ID, character.X, character.Y, damage);
            await target.SendAsync(hit);
            await Movement.SendToViewAsync(target, hit);
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
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;
            await target.SendAsync(new MsgUserAttrib(target.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{Attacker} hit {Name} for {Damage}; {Health} health left ({Kind})",
                attackerCharacter.Name, character.Name, damage, character.HealthPoints, kind);
            await Arena.Qualifier.OnHitAsync(attacker, target, dealt);

            if (character.HealthPoints > 0)
                return;
            bool guilty = Pking.IsGuilty(target, DateTime.UtcNow);
            await Death.DieAsync(target, attacker.ID, attackerCharacter.Name);
            if (!await Arena.Qualifier.OnKillAsync(attacker, target))
                await Pking.OnKillAsync(attacker, target, guilty);
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

            var stats = Stats.Of(client);
            var type = monster.Type;
            uint damage = Damage.Hits(stats.Dodge, targetIsMonster: false, Random.Shared) ? stats.Taken(
                Damage.MonsterMelee(type.AttackMin, type.AttackMax, Buffs.Defense(client.Effects, stats.Defense, now), Random.Shared)) : 0;
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;

            await FacePlayerTowardAsync(client, monster.X, monster.Y);
            var hit = new MsgInteract(InteractionType.Attack, monster.Identity, client.ID, character.X, character.Y, damage);
            await client.SendAsync(hit);
            await Movement.SendToViewAsync(client, hit);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{MonsterName} {MonsterId} hit {Name} for {Damage}; {Health} health left",
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

        // Each shot takes an arrow; the last one leaves the quiver empty and gone
        private static async Task UseArrowAsync(Client client, Database.Models.DbItem arrows)
        {
            arrows.Amount--;
            if (arrows.Amount > 0)
            {
                await ItemsRepository.SetAmountAsync(arrows.ItemID, arrows.Amount);
                await client.SendAsync(new MsgItemInfo(arrows, MsgItemInfo.ItemAction.Update));
                return;
            }

            client.Equipment.Clear(ItemPosition.LeftHand);
            await ItemsRepository.DeleteAsync(arrows.ItemID);
            await client.SendAsync(MsgItem.Unequipped(arrows.ItemID, ItemPosition.LeftHand));
            await client.SendAsync(MsgItem.Removed(arrows.ItemID));
            await client.SendAsync(MsgItem.DisplayGears(client.ID, client.Equipment));
            await Movement.SendToViewAsync(client, new MsgPlayer(client));
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

        // The attacker and everyone who sees the monster
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

namespace Comet.Game.Combat
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;
    using static Comet.Game.Packets.MsgInteract;

    // The client asks once to attack a monster; the server keeps swinging at the weapon's speed
    // until the monster dies, the player moves, dies, or asks for something else. Swings run on
    // the player's own packet queue.
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
                request.TargetID < Monster.FirstID || request.TargetID > Monster.LastID ? "not_a_monster" :
                null;
            if (reason != null)
            {
                Reject(client, request.TargetID, reason);
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
            if (now < client.NextAttackAt)
            {
                Schedule(client, run, client.NextAttackAt - now);
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
                shooting != stats.Bow ? (shooting ? "no_bow" : "bow_melee") :
                shooting && (arrows == null || !ItemSlots.IsArrow(arrows.TypeID) || arrows.Amount == 0) ? "no_arrows" :
                MonsterAI.Distance(character.X, character.Y, x, y) > stats.Range + RangeSlack ? "too_far" :
                null;
            if (reason != null)
            {
                Stop(client);
                if (reason != "dead_target")
                    Reject(client, targetID, reason);
                return;
            }

            var interval = TimeSpan.FromMilliseconds(Math.Max(stats.AttackSpeed, PlayerInterval.TotalMilliseconds));
            client.NextAttackAt = now + interval;
            var type = monster.Type;
            uint damage = 0;
            if (Damage.Hits(type.Dodge, targetIsMonster: true, Random.Shared))
                damage = shooting
                    ? Damage.Archery(stats.MinAttack, stats.MaxAttack, character.Level, type.Level, type.Dodge, Random.Shared)
                    : Damage.Melee(stats.MinAttack, stats.MaxAttack, character.Level, type.Level, type.Defense, Random.Shared);
            if (shooting)
                await UseArrowAsync(client, arrows);

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

            // Hitting guards teaches nothing
            ulong experience = type.IsGuard ? 0 : Damage.Experience(dealt, character.Level, type.Level);
            if (killed)
            {
                if (!type.IsGuard)
                    experience += Damage.KillBonus(type.Life);
                Stop(client);
                await SendAroundAsync(client, monster, new MsgInteract(InteractionType.Kill, client.ID, monster.Identity, x, y, 1));
                LogEvents.For(typeof(Attacks), LogEvents.MonsterKilled).Information(
                    "{Name} killed {MonsterName} {MonsterId} (type {MonsterTypeId}) on map {MapId} at {X},{Y}",
                    character.Name, type.Name, monster.Identity, type.ID, monster.Map.Id, x, y);
            }

            if (experience > 0)
                await Progress.AwardExperienceAsync(client, experience, "monster");
            if (killed)
                await Loot.DropAsync(client, monster, x, y, Random.Shared);
            else
                Schedule(client, run, interval);
        }

        // Runs on the player's queue, posted by the monster loop
        public static async Task MonsterHitsPlayerAsync(Monster monster, Client client)
        {
            var character = client.Character;
            var now = DateTime.UtcNow;
            if (character == null || client.Life.Dead || now < client.Life.ProtectedUntil || !monster.Alive ||
                client.Map?.Id != monster.Map.Id ||
                MonsterAI.Distance(monster.X, monster.Y, character.X, character.Y) > Math.Max(1, monster.Type.AttackRange) + RangeSlack)
                return;

            var stats = Stats.Of(client);
            var type = monster.Type;
            uint damage = Damage.Hits(stats.Dodge, targetIsMonster: false, Random.Shared)
                ? Damage.MonsterMelee(type.AttackMin, type.AttackMax, stats.Defense, Random.Shared) : 0;
            uint dealt = Math.Min(damage, character.HealthPoints);
            character.HealthPoints -= (ushort)dealt;

            var hit = new MsgInteract(InteractionType.Attack, monster.Identity, client.ID, character.X, character.Y, damage);
            await client.SendAsync(hit);
            await Movement.SendToViewAsync(client, hit);
            await client.SendAsync(new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints));
            LogEvents.For(typeof(Attacks), LogEvents.CombatHit).Debug(
                "{MonsterName} {MonsterId} hit {Name} for {Damage}; {Health} health left",
                type.Name, monster.Identity, character.Name, damage, character.HealthPoints);

            if (character.HealthPoints == 0)
                await Death.DieAsync(client, monster.Identity, type.Name);
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

        private static void Schedule(Client client, int run, TimeSpan delay) =>
            _ = Task.Delay(delay).ContinueWith(_ => Kernel.Post(client, () => SwingAsync(client, run)),
                TaskScheduler.Default);

        // The attacker and everyone who sees the monster
        private static async Task SendAroundAsync(Client client, Monster monster, IPacket packet)
        {
            await client.SendAsync(packet);
            foreach (var player in Monsters.PlayersInView(monster))
                if (player != client)
                    await player.SendAsync(packet);
        }

        private static void Reject(Client client, uint targetID, string reason) =>
            LogEvents.For(typeof(Attacks), LogEvents.CombatRejected).Warning(
                "{Name} can't attack {TargetId}: {Reason}", client.Character?.Name, targetID, reason);
    }
}

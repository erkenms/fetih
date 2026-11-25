namespace Comet.Game.Combat
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.Pk;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // A player's death and revival, on their own packet queue. The body falls; the client asks
    // for the ghost (MsgAction 145) when its death animation is over. Reviving in town is
    // allowed once ReviveDelay has passed.
    public static class Death
    {
        public static TimeSpan ReviveDelay = TimeSpan.FromSeconds(20);

        public static readonly TimeSpan Protection = TimeSpan.FromSeconds(5);

        // The client asks for the ghost (MsgAction 145) only sometimes; past this it's sent anyway
        public static readonly TimeSpan GhostDelay = TimeSpan.FromSeconds(3);

        public static async Task DieAsync(Client client, uint killerID, string killer)
        {
            var character = client.Character;
            var life = client.Life;
            if (character == null || life.Dead)
                return;

            Attacks.Stop(client);
            await Social.Trading.CancelAsync(client, "died");
            await Items.Riding.DismountAsync(client, "died");
            character.HealthPoints = 0;
            await Magic.Casting.DropAuraAsync(client);
            client.CounterKill = false;
            Magic.Transforming.Revert(client);
            client.Effects.Clear();
            Pking.OnDeath(client);
            client.Energy.KoCount = 0;
            await Magic.Recovery.SetXpAsync(client, 0);
            life.Dead = true;
            life.Ghost = false;
            var diedAt = life.DiedAt = DateTime.UtcNow;

            var kill = new MsgInteract(MsgInteract.InteractionType.Kill, killerID, client.ID, character.X, character.Y, 1);
            await client.SendAsync(kill);
            await Movement.SendToViewAsync(client, kill);
            await SendLookAsync(client);
            LogEvents.For(typeof(Death), LogEvents.CharacterDied).Information(
                "{Name} was killed by {Killer} {KillerId} on map {MapId} at {X},{Y} (level {Level})",
                character.Name, killer, killerID, character.MapID, character.X, character.Y, character.Level);

            _ = Task.Delay(GhostDelay).ContinueWith(_ => Kernel.Post(client, () =>
                life.DiedAt == diedAt ? BecomeGhostAsync(client) : Task.CompletedTask), TaskScheduler.Default);
        }

        public static async Task BecomeGhostAsync(Client client)
        {
            if (client.Character == null || !client.Life.Dead || client.Life.Ghost)
                return;
            client.Life.Ghost = true;
            await SendLookAsync(client);
        }

        // The client's Revive button (MsgAction 94). Revives at full health at the reborn
        // point of the map died on (the jail's is the jail), or in Twin City if it has none.
        // The blessed may ask to stay where they lie.
        public static async Task<bool> ReviveAsync(Client client, string reason, bool anyTime = false, bool here = false)
        {
            var character = client.Character;
            var life = client.Life;
            var now = DateTime.UtcNow;
            string refused =
                character == null ? "no_character" :
                !life.Dead ? "alive" :
                !anyTime && now - life.DiedAt < ReviveDelay ? "too_soon" :
                Magic.Buffs.Shackled(client.Effects, now) ? "shackled" :
                Tournaments.TeamPk.BlocksRevive(client) ? "team_pk" :
                null;
            if (refused != null)
            {
                LogEvents.For(typeof(Death), LogEvents.CharacterReviveRejected).Warning(
                    "{Name} can't revive: {Reason}", character?.Name, refused);
                return false;
            }

            await RaiseAsync(client, now);
            if (here && HeavenBlessing.CanReviveHere(client))
            {
                await ShowRevivalAsync(client, reason + "_here");
                return true;
            }
            var reborn = Kernel.MapTypes.RebornPointOf(character.MapID);
            if (reborn == null || !await Movement.TeleportAsync(client, reborn.MapID, reborn.X, reborn.Y))
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
            await ShowRevivalAsync(client, reason);
            return true;
        }

        public static async Task ReviveHereAsync(Client client, string reason)
        {
            if (client.Character == null || !client.Life.Dead || Magic.Buffs.Shackled(client.Effects, DateTime.UtcNow))
                return;
            await RaiseAsync(client, DateTime.UtcNow);
            await ShowRevivalAsync(client, reason);
        }

        private static async Task RaiseAsync(Client client, DateTime now)
        {
            var character = client.Character;
            var life = client.Life;
            life.Dead = false;
            life.Ghost = false;
            life.ProtectedUntil = now + Protection;
            character.HealthPoints = Attributes.MaxHealth(client);
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Health, character.HealthPoints));
            await SendLookAsync(client);
        }

        private static async Task ShowRevivalAsync(Client client, string reason)
        {
            var character = client.Character;
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, MsgMagicEffect.Revive);
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            LogEvents.For(typeof(Death), LogEvents.CharacterRevived).Information(
                "{Name} revived on map {MapId} at {X},{Y} ({Reason})",
                character.Name, character.MapID, character.X, character.Y, reason);
        }

        // One packet each, as the VM showed the ghost body for a Mesh attribute sent on its own
        private static async Task SendLookAsync(Client client)
        {
            foreach (var look in new[]
            {
                new MsgUserAttrib(client.ID, AttributeType.Status, client.Status),
                new MsgUserAttrib(client.ID, AttributeType.Mesh, client.Life.Mesh(client.Character)),
            })
            {
                await client.SendAsync(look);
                await Movement.SendToViewAsync(client, look);
            }
        }
    }
}

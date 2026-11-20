namespace Comet.Game.Combat
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgUserAttrib;

    // A player's death and revival, on their own packet queue. The body falls; the client asks
    // for the ghost (MsgAction 145) when its death animation is over. Reviving in town is
    // allowed once ReviveDelay has passed.
    public static class Death
    {
        public static TimeSpan ReviveDelay = TimeSpan.FromSeconds(20);

        // Monsters leave a revived player alone for this long
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
            character.HealthPoints = 0;
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

        // The client's Revive button (MsgAction 94). Revives at full health in Twin City, or
        // in the jail for those in it.
        public static async Task<bool> ReviveAsync(Client client, string reason, bool anyTime = false)
        {
            var character = client.Character;
            var life = client.Life;
            var now = DateTime.UtcNow;
            string refused =
                character == null ? "no_character" :
                !life.Dead ? "alive" :
                !anyTime && now - life.DiedAt < ReviveDelay ? "too_soon" :
                null;
            if (refused != null)
            {
                LogEvents.For(typeof(Death), LogEvents.CharacterReviveRejected).Warning(
                    "{Name} can't revive: {Reason}", character?.Name, refused);
                return false;
            }

            life.Dead = false;
            life.Ghost = false;
            life.ProtectedUntil = now + Protection;
            character.HealthPoints = Attributes.MaxHealth(character);
            await client.SendAsync(new MsgUserAttrib(client.ID, AttributeType.Health, character.HealthPoints));
            await SendLookAsync(client);
            if (character.MapID == Pk.Jail.MapID)
                await Movement.TeleportAsync(client, Pk.Jail.MapID, Pk.Jail.X, Pk.Jail.Y);
            else
                await Movement.TeleportAsync(client, StartPosition.MapID, StartPosition.X, StartPosition.Y);
            var effect = new MsgMagicEffect(client.ID, character.X, character.Y, MsgMagicEffect.Revive);
            effect.Targets.Add((client.ID, 0));
            await client.SendAsync(effect);
            await Movement.SendToViewAsync(client, effect);
            LogEvents.For(typeof(Death), LogEvents.CharacterRevived).Information(
                "{Name} revived on map {MapId} at {X},{Y} ({Reason})",
                character.Name, character.MapID, character.X, character.Y, reason);
            return true;
        }

        // Status and body, to the player and everyone who sees them. One packet each, as the
        // VM showed the ghost body for a Mesh attribute sent on its own.
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

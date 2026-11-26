namespace Fetih.Game.Magic
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgUserAttrib;

    // A player turned into a monster type for a while: its look, its attack, defense and
    // dodge, and its life as the most, the health left scaled to it and back
    public sealed record Transformation(MonsterType Type, DateTime Until);

    // Golem, Water Elf, Divine Hare and Night Devil: magictype's power names the monster type
    // and its step the seconds. On the player's own queue.
    public static class Transforming
    {
        public static bool TryGetType(MagicType spell, out MonsterType type) =>
            Kernel.MonsterTypes.TryGet((uint)Math.Max(0, spell.Power), out type);

        public static async Task StartAsync(Client client, MonsterType type, uint seconds, DateTime now)
        {
            var character = client.Character;
            Revert(client);
            ushort before = Attributes.MaxHealth(client);
            client.Transformation = new Transformation(type, now + TimeSpan.FromSeconds(seconds));
            client.Life.TransformLook = type.Lookface;
            Rescale(client, before);
            await SendAsync(client);
            LogEvents.For(typeof(Transforming), LogEvents.StatusStarted).Information(
                "{Name} turned into {MonsterName} (type {MonsterTypeId}) for {Seconds}s",
                character.Name, type.Name, type.ID, seconds);
        }

        // Back to the player's own look and life, shown
        public static async Task EndAsync(Client client, string reason)
        {
            if (client.Transformation == null || client.Character == null)
                return;
            Revert(client);
            await SendAsync(client);
            LogEvents.For(typeof(Transforming), LogEvents.StatusEnded).Information(
                "{Name} turned back ({Reason})", client.Character.Name, reason);
        }

        public static Task TickAsync(Client client, DateTime now) =>
            client.Transformation is { } t && now >= t.Until ? EndAsync(client, "expired") : Task.CompletedTask;

        // Back without showing it: at death and logout
        public static void Revert(Client client)
        {
            if (client.Transformation == null || client.Character == null)
                return;
            ushort before = Attributes.MaxHealth(client);
            client.Transformation = null;
            client.Life.TransformLook = 0;
            Rescale(client, before);
        }

        private static void Rescale(Client client, ushort before)
        {
            var character = client.Character;
            ushort after = Attributes.MaxHealth(client);
            if (before == 0 || character.HealthPoints == 0)
                return;
            character.HealthPoints = (ushort)Math.Clamp((ulong)character.HealthPoints * after / before, 1, after);
        }

        // The look on its own, as for the ghost
        private static async Task SendAsync(Client client)
        {
            var character = client.Character;
            var look = new MsgUserAttrib(client.ID, AttributeType.Mesh, client.Life.Mesh(character));
            await client.SendAsync(look);
            await Movement.SendToViewAsync(client, look);
            await client.SendAsync(new MsgUserAttrib(client.ID)
                .Append(AttributeType.MaxHealth, Attributes.MaxHealth(client))
                .Append(AttributeType.Health, character.HealthPoints));
        }
    }
}

namespace Comet.Game.World
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;

    /// <summary>
    /// Shows players to each other when one of them appears on or leaves a map: everyone
    /// within <see cref="GameMap{T}.ViewRange"/> gets the other's <see cref="MsgPlayer"/>,
    /// or a remove-entity action when they leave.
    /// </summary>
    public static class Visibility
    {
        /// <summary>
        /// Puts the client's character on their map and shows them and the players
        /// around them to each other. Does nothing if they are already on the map.
        /// </summary>
        public static async Task EnterAsync(Client client)
        {
            var map = client.Map;
            var character = client.Character;
            if (map == null || character == null || map.TryGetPosition(client, out _, out _))
                return;

            map.Add(client, character.X, character.Y);
            var spawn = new MsgPlayer(character);
            foreach (var other in map.InView(character.X, character.Y, except: client))
            {
                if (other.Character == null)
                    continue;
                await other.SendAsync(spawn);
                await client.SendAsync(new MsgPlayer(other.Character));
            }
        }

        /// <summary>
        /// Takes the client's character off their map and removes them from the screens
        /// of the players around them.
        /// </summary>
        public static void Leave(Client client)
        {
            var map = client.Map;
            if (map == null || !map.TryGetPosition(client, out var x, out var y) || !map.Remove(client))
                return;

            var removal = MsgAction.RemoveEntity(client.ID);
            foreach (var other in map.InView(x, y))
                _ = other.SendAsync(removal);
        }
    }
}

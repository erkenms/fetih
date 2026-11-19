namespace Comet.Game.World
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;

    public static class Visibility
    {
        public static async Task EnterAsync(Client client)
        {
            var map = client.Map;
            var character = client.Character;
            if (map == null || character == null || map.TryGetPosition(client, out _, out _))
                return;

            map.Add(client, character.X, character.Y);
            var spawn = new MsgPlayer(client);
            foreach (var other in map.InView(character.X, character.Y, except: client))
            {
                if (other.Character == null)
                    continue;
                await other.SendAsync(spawn);
                await client.SendAsync(new MsgPlayer(other));
            }
            await Items.Ground.ShowAroundAsync(client, character.X, character.Y);
        }

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

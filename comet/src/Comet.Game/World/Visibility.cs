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
            await ShowNpcsAsync(client, character.X, character.Y);
            await Monsters.ShowAroundAsync(client, character.MapID, character.X, character.Y);
        }

        // NPCs in view of the cell; with a previous cell, only those that weren't in view there
        public static async Task ShowNpcsAsync(Client client, ushort x, ushort y, ushort? fromX = null, ushort? fromY = null)
        {
            foreach (var npc in Kernel.Npcs.InView(client.Character.MapID, x, y))
            {
                if (fromX.HasValue && GameMap<Client>.InView(fromX.Value, fromY.Value, npc.X, npc.Y))
                    continue;
                await Items.Vending.ShowNpcAsync(client, npc);
            }
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

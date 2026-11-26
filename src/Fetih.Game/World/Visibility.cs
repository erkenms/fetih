namespace Fetih.Game.World
{
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

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
            }
            await ShowAroundAsync(client, character.X, character.Y);
        }

        public static async Task ShowAroundAsync(Client client, ushort x, ushort y)
        {
            var map = client.Map;
            var character = client.Character;
            if (map == null || character == null)
                return;

            foreach (var other in map.InView(x, y, except: client))
                if (other.Character != null)
                    await client.SendAsync(new MsgPlayer(other));
            await Items.Ground.ShowAroundAsync(client, x, y);
            await ShowNpcsAsync(client, x, y);
            await Monsters.ShowAroundAsync(client, character.MapID, x, y);
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
            await Social.CaptureTheFlag.ShowAsync(client, x, y, fromX, fromY);
            await Tournaments.GuildPk.ShowFlagAsync(client, x, y, fromX, fromY);
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

        // The client asks for an entity it heard from (walk, jump, interact) before seeing its spawn
        public static async Task QueryAsync(Client client, uint targetID)
        {
            var map = client.Map;
            var character = client.Character;
            if (map == null || character == null || targetID == 0 || targetID == client.ID)
                return;
            if (!map.TryGetPosition(client, out var x, out var y))
                return;

            if (Kernel.Players.TryGet(targetID, out var other))
            {
                if (other.Character != null && other.Character.MapID == character.MapID &&
                    GameMap<Client>.InView(x, y, other.Character.X, other.Character.Y))
                    await client.SendAsync(new MsgPlayer(other));
                return;
            }

            if (Monsters.TryGet(targetID, out var monster))
            {
                if (monster.Alive && monster.Map.Id == character.MapID &&
                    GameMap<Monster>.InView(x, y, monster.X, monster.Y))
                    await client.SendAsync(new MsgPlayer(monster));
                return;
            }

            if (await Social.CaptureTheFlag.QueryAsync(client, targetID))
                return;
            if (Tournaments.GuildPk.FindFlag(client, targetID) != null)
            {
                await Tournaments.GuildPk.ShowFlagAsync(client, x, y);
                return;
            }

            var npc = Kernel.Npcs?.Find(character.MapID, targetID);
            if (npc != null && GameMap<Client>.InView(x, y, npc.X, npc.Y))
                await Items.Vending.ShowNpcAsync(client, npc);
        }
    }
}

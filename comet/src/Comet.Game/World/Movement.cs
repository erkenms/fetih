namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public static class Movement
    {
        public const int MaxJumpDistance = 16;

        public static readonly int[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly int[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        public static async Task WalkAsync(Client client, MsgWalk step)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            int toX = x + DeltaX[step.Facing];
            int toY = y + DeltaY[step.Facing];
            string reason = step.CharacterID != client.ID ? "wrong_id" : CheckCell(map, toX, toY);
            if (reason != null)
            {
                await RejectAsync(client, map, x, y, toX, toY, "walk", reason);
                return;
            }

            client.Character.Direction = step.Facing;
            await MoveAsync(client, map, x, y, (ushort)toX, (ushort)toY, step);
            LogEvents.For(typeof(Movement), LogEvents.MoveWalk).Debug(
                "{Name} walked to {X},{Y} on map {MapId}", client.Character.Name, toX, toY, map.Id);
        }

        public static async Task JumpAsync(Client client, MsgAction jump)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            int toX = (int)(jump.Command & 0xFFFF);
            int toY = (int)(jump.Command >> 16);
            string reason =
                jump.CharacterID != client.ID ? "wrong_id" :
                Math.Max(Math.Abs(toX - x), Math.Abs(toY - y)) > MaxJumpDistance ? "too_far" :
                CheckCell(map, toX, toY);
            if (reason != null)
            {
                await RejectAsync(client, map, x, y, toX, toY, "jump", reason);
                return;
            }

            client.Character.Direction = DirectionTo(x, y, toX, toY, client.Character.Direction);
            await MoveAsync(client, map, x, y, (ushort)toX, (ushort)toY, jump);
            LogEvents.For(typeof(Movement), LogEvents.MoveJump).Debug(
                "{Name} jumped to {X},{Y} on map {MapId}", client.Character.Name, toX, toY, map.Id);
        }

        // The client sends this when it steps on a portal cell and then waits for a
        // teleport. The portal is found from the server's position, not the packet's.
        public static async Task PortalAsync(Client client, MsgAction request)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            var portal = map.Grid.PortalNear(x, y);
            PortalTarget target = default;
            string reason =
                request.CharacterID != client.ID ? "wrong_id" :
                portal == null ? "no_portal" :
                !Kernel.Portals.TryGet(map.Id, portal.Value.Index, out target) ? "no_destination" :
                null;
            if (reason == null && !await TeleportAsync(client, target.MapID, target.X, target.Y))
                reason = "bad_destination";
            if (reason != null)
                await RejectAsync(client, map, x, y, x, y, "portal", reason);
        }

        // Moves the character to a cell on any map. Players around the old cell lose them;
        // players around the new one see them.
        public static async Task<bool> TeleportAsync(Client client, uint mapID, ushort x, ushort y)
        {
            if (!TryGetPosition(client, out var from, out var fromX, out var fromY) ||
                !Kernel.World.TryGet(mapID, out var to) || !to.Grid.Contains(x, y))
                return false;

            var before = from.InView(fromX, fromY, except: client);
            Visibility.Leave(client);
            foreach (var other in before)
                await client.SendAsync(MsgAction.RemoveEntity(other.ID));

            var character = client.Character;
            client.Map = to;
            character.MapID = mapID;
            character.X = x;
            character.Y = y;
            character.Stance = 0;
            await client.SendAsync(MsgAction.Teleport(client.ID, mapID, x, y, character.Direction));
            await Visibility.EnterAsync(client);
            LogEvents.For(typeof(Movement), LogEvents.MoveTeleport).Information(
                "{Name} teleported from map {FromMapId} at {FromX},{FromY} to map {MapId} at {X},{Y}",
                character.Name, from.Id, fromX, fromY, mapID, x, y);
            return true;
        }

        public static byte DirectionTo(int x, int y, int toX, int toY, byte current)
        {
            int dx = Math.Sign(toX - x), dy = Math.Sign(toY - y);
            for (byte direction = 0; direction < 8; direction++)
                if (DeltaX[direction] == dx && DeltaY[direction] == dy)
                    return direction;
            return current;
        }

        public static async Task SendToViewAsync(Client client, IPacket packet)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            foreach (var other in map.InView(x, y, except: client))
                await other.SendAsync(packet);
        }

        private static bool TryGetPosition(Client client, out GameMap<Client> map, out ushort x, out ushort y)
        {
            map = client.Map;
            x = y = 0;
            return client.Character != null && map != null && map.TryGetPosition(client, out x, out y);
        }

        private static string CheckCell(GameMap<Client> map, int x, int y) =>
            !map.Grid.Contains(x, y) ? "outside_map" :
            !map.Grid.IsWalkable(x, y) ? "blocked_cell" : null;

        private static async Task MoveAsync(Client client, GameMap<Client> map,
            ushort fromX, ushort fromY, ushort toX, ushort toY, IPacket move)
        {
            var before = map.InView(fromX, fromY, except: client);
            map.Move(client, toX, toY);
            client.Character.X = toX;
            client.Character.Y = toY;
            client.Character.Stance = 0; // moving ends sitting or an emote
            var after = map.InView(toX, toY, except: client);

            await client.SendAsync(move);
            foreach (var other in before)
                await other.SendAsync(move);

            var stillInView = new HashSet<Client>(after);
            var wasInView = new HashSet<Client>(before);
            var me = new MsgPlayer(client);
            foreach (var other in before)
            {
                if (stillInView.Contains(other))
                    continue;
                await other.SendAsync(MsgAction.RemoveEntity(client.ID));
                await client.SendAsync(MsgAction.RemoveEntity(other.ID));
            }

            foreach (var other in after)
            {
                if (wasInView.Contains(other) || other.Character == null)
                    continue;
                await other.SendAsync(me);
                await client.SendAsync(new MsgPlayer(other));
            }
        }

        private static async Task RejectAsync(Client client, GameMap<Client> map,
            ushort x, ushort y, int toX, int toY, string kind, string reason)
        {
            LogEvents.For(typeof(Movement), LogEvents.MoveRejected).Warning(
                "{Name} can't {Kind} from {X},{Y} to {ToX},{ToY} on map {MapId}: {Reason}",
                client.Character.Name, kind, x, y, toX, toY, map.Id, reason);
            await client.SendAsync(MsgAction.Teleport(
                client.ID, map.Id, x, y, client.Character.Direction));
        }
    }
}

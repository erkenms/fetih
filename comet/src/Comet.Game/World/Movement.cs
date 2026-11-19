namespace Comet.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Network.Packets;

    /// <summary>
    /// Walking and jumping: checks each move against the map, passes it on to the
    /// players who can see it, and updates who sees whom. A move the server refuses puts
    /// the player back where the server has them.
    /// </summary>
    public static class Movement
    {
        /// <summary>Farthest jump, in cells on either axis (5619: from reference).</summary>
        public const int MaxJumpDistance = 16;

        /// <summary>
        /// The cell offset of one step in each direction, 0 to 7 (5619: from reference).
        /// </summary>
        public static readonly int[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly int[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        /// <summary>Walks the client's character one cell in the step's direction.</summary>
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

        /// <summary>
        /// Jumps the client's character to the cell in the action: X in the low half of
        /// Command and Y in the high half (5619: seen in VM; the reference folds it the
        /// same way).
        /// </summary>
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

        /// <summary>
        /// The direction (0 to 7) from one cell toward another, or the given one if they
        /// are the same cell.
        /// </summary>
        public static byte DirectionTo(int x, int y, int toX, int toY, byte current)
        {
            int dx = Math.Sign(toX - x), dy = Math.Sign(toY - y);
            for (byte direction = 0; direction < 8; direction++)
                if (DeltaX[direction] == dx && DeltaY[direction] == dy)
                    return direction;
            return current;
        }

        /// <summary>Sends a packet to everyone who can see the client, but not the client.</summary>
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

        /// <summary>
        /// Moves the character and tells everyone who needs to know: the move goes to the
        /// mover and to those who could see where it started; players who come into view
        /// and the mover are shown to each other; players who drop out of view and the
        /// mover are removed from each other's screens.
        /// </summary>
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
            var me = new MsgPlayer(client.Character);
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
                await client.SendAsync(new MsgPlayer(other.Character));
            }
        }

        /// <summary>
        /// Refuses a move: logs why and puts the player back at the cell the server has
        /// for them, with a teleport to it (5619: from reference, which does the same).
        /// </summary>
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

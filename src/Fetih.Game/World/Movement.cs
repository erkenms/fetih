namespace Fetih.Game.World
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    public static class Movement
    {
        public const int MaxJumpDistance = 16;

        public static readonly int[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly int[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        // A ride step (MsgWalk mode 9) by its direction modulo 24
        public static readonly int[] RideDeltaX = { 0, -2, -2, -2, 0, 2, 2, 2, 1, 0, -2, 0, 1, 0, 2, 0, 0, -2, 0, -1, 0, 2, 0, 1 };
        public static readonly int[] RideDeltaY = { 2, 2, 0, -2, -2, -2, 0, 2, 2, 0, -1, 0, -2, 0, 1, 0, 0, 1, 0, -2, 0, -1, 0, 2 };

        public static async Task WalkAsync(Client client, MsgWalk step)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            bool ride = step.Mode == MsgWalk.RideMode;
            int toX = x + (ride ? RideDeltaX[step.Direction % 24] : DeltaX[step.Facing]);
            int toY = y + (ride ? RideDeltaY[step.Direction % 24] : DeltaY[step.Facing]);
            // A body that hasn't turned into a ghost yet stays where it fell
            string reason =
                step.CharacterID != client.ID ? "wrong_id" :
                client.Life.Dead && !client.Life.Ghost ? "dead" :
                ride && !client.Mount.Riding ? "not_riding" :
                CheckCell(map, toX, toY);
            if (reason != null)
            {
                await RejectAsync(client, map, x, y, toX, toY, ride ? "ride" : "walk", reason);
                return;
            }

            client.Character.Direction = ride
                ? DirectionTo(x, y, toX, toY, client.Character.Direction)
                : step.Facing;
            await MoveAsync(client, map, x, y, (ushort)toX, (ushort)toY, step);
            await Items.Riding.SpendAsync(client, ride ? Items.Riding.RideStepCost : Items.Riding.WalkStepCost, DateTime.UtcNow);
            LogEvents.For(typeof(Movement), LogEvents.MoveWalk).Debug(
                "{Name} walked to {X},{Y} on map {MapId} (mode {Mode})", client.Character.Name, toX, toY, map.Id, step.Mode);
        }

        public static async Task JumpAsync(Client client, MsgAction jump)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            int toX = (int)(jump.Command & 0xFFFF);
            int toY = (int)(jump.Command >> 16);
            string reason =
                jump.CharacterID != client.ID ? "wrong_id" :
                client.Life.Dead ? (client.Life.Ghost ? "ghost" : "dead") :
                client.Effects.Has(StatusFlags.ShurikenVortex, DateTime.UtcNow) ? "shuriken_vortex" :
                Math.Max(Math.Abs(toX - x), Math.Abs(toY - y)) > MaxJumpDistance ? "too_far" :
                CheckCell(map, toX, toY);
            if (reason != null)
            {
                await RejectAsync(client, map, x, y, toX, toY, "jump", reason);
                return;
            }

            client.Character.Direction = DirectionTo(x, y, toX, toY, client.Character.Direction);
            var now = DateTime.UtcNow;
            client.LandsAt = now + JumpTime(client, x, y, toX, toY, now);
            await MoveAsync(client, map, x, y, (ushort)toX, (ushort)toY, jump);
            await Items.Riding.SpendAsync(client,
                Items.Riding.JumpCost(Math.Max(Math.Abs(toX - x), Math.Abs(toY - y))), DateTime.UtcNow);
            LogEvents.For(typeof(Movement), LogEvents.MoveJump).Debug(
                "{Name} jumped to {X},{Y} on map {MapId}", client.Character.Name, toX, toY, map.Id);
        }

        // The client sends this when it steps on a portal cell and then waits for a
        // teleport. The portal is found from the server's position, not the packet's.
        public static async Task PortalAsync(Client client, MsgAction request)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            if (map.Id == Training.OfflineTraining.MapID && request.CharacterID == client.ID)
            {
                await Training.OfflineTraining.ExitAsync(client);
                return;
            }
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

        public static async Task<bool> TeleportAsync(Client client, uint mapID, ushort x, ushort y) =>
            Kernel.World.TryGet(mapID, out var to) && await TeleportAsync(client, to, x, y);

        // Works for a map of its own too, such as a qualifier arena: the client is told the map's ID
        public static async Task<bool> TeleportAsync(Client client, GameMap<Client> to, ushort x, ushort y)
        {
            uint mapID = to.Id;
            if (!TryGetPosition(client, out var from, out var fromX, out var fromY) || !to.Grid.Contains(x, y))
                return false;

            if (from != to)
                await Social.Trading.CancelAsync(client, "teleport");
            await Items.Vending.CloseAsync(client, "teleport");
            var before = from.InView(fromX, fromY, except: client);
            Visibility.Leave(client);
            foreach (var other in before)
                await client.SendAsync(MsgAction.RemoveEntity(other.ID));
            await Monsters.HideAroundAsync(client, from.Id, fromX, fromY);
            Combat.Attacks.Stop(client);

            var character = client.Character;
            client.Map = to;
            character.MapID = mapID;
            character.X = x;
            character.Y = y;
            character.Stance = 0;
            if (client.Mount.Riding && Items.Riding.Problem(client) != null)
                await Items.Riding.DismountAsync(client, "map");
            await client.SendAsync(MsgAction.Teleport(client.ID, mapID, x, y, character.Direction));
            if (from != to)
            {
                await client.SendAsync(MsgMapInfo.For(mapID));
                await Weather.SendAsync(client, mapID);
            }
            await Visibility.EnterAsync(client);
            if (mapID == Npcs.Market.MapID && from.Id != mapID)
                client.MarketReturn = (from.Id, fromX, fromY);
            LogEvents.For(typeof(Movement), LogEvents.MoveTeleport).Information(
                "{Name} teleported from map {FromMapId} at {FromX},{FromY} to map {MapId} at {X},{Y}",
                character.Name, from.Id, fromX, fromY, mapID, x, y);
            return true;
        }

        // A move a spell makes (Blade Tempest's dash), shown by the spell's effect; null when
        // the move is allowed, or why not
        public static string CheckDash(Client client, int toX, int toY) =>
            client.Map == null ? "no_map" : CheckCell(client.Map, toX, toY);

        public static async Task DashAsync(Client client, ushort toX, ushort toY, IPacket shown)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            client.Character.Direction = DirectionTo(x, y, toX, toY, client.Character.Direction);
            await MoveAsync(client, map, x, y, toX, toY, shown);
            LogEvents.For(typeof(Movement), LogEvents.MoveJump).Debug(
                "{Name} dashed to {X},{Y} on map {MapId}", client.Character.Name, toX, toY, map.Id);
        }

        // A move another's spell makes (Dash's knockback), which that spell's packet shows
        public static async Task PushAsync(Client client, ushort toX, ushort toY)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y) || CheckCell(map, toX, toY) != null)
                return;
            await MoveAsync(client, map, x, y, toX, toY, null);
            LogEvents.For(typeof(Movement), LogEvents.MoveJump).Debug(
                "{Name} was pushed to {X},{Y} on map {MapId}", client.Character.Name, toX, toY, map.Id);
        }

        // The client plays a jump's 20 frames (40 in flight) max(33, 33 * world distance / 300 + 1)
        // ms apart, half that in Cyclone and two thirds in Oblivion. A steed's speed leaves
        // jumps alone.
        public static TimeSpan JumpTime(Client client, int fromX, int fromY, int toX, int toY, DateTime now) =>
            JumpTime(fromX, fromY, toX, toY, client.Effects.Has(StatusFlags.Cyclone, now),
                client.Effects.Has(StatusFlags.Oblivion, now), client.Effects.Has(StatusFlags.Fly, now));

        public static TimeSpan JumpTime(int fromX, int fromY, int toX, int toY, bool cyclone, bool oblivion, bool flying)
        {
            int frame = Math.Max(33, (int)(33 * WorldDistance(fromX, fromY, toX, toY) / 300 + 1));
            if (cyclone)
                frame /= 2;
            else if (oblivion)
                frame = frame * 2 / 3;
            return TimeSpan.FromMilliseconds((flying ? 40 : 20) * frame);
        }

        // A cell is (x - y) * 32 by (x + y) * 16 world units
        public static double WorldDistance(int fromX, int fromY, int toX, int toY)
        {
            int dx = toX - fromX, dy = toY - fromY;
            return Math.Sqrt(Math.Pow((dx - dy) * 32, 2) + Math.Pow((dx + dy) * 16, 2));
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

        public static async Task SendToViewAsync(Client client, IPacket packet, Func<Client, bool> filter)
        {
            if (!TryGetPosition(client, out var map, out var x, out var y))
                return;
            foreach (var other in map.InView(x, y, except: client))
                if (filter(other))
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
            await Items.Vending.CloseAsync(client, "moved");
            Combat.Attacks.Stop(client);
            var before = map.InView(fromX, fromY, except: client);
            map.Move(client, toX, toY);
            client.Character.X = toX;
            client.Character.Y = toY;
            client.Character.Stance = 0; // moving ends sitting or an emote
            bool prayed = client.Effects.Remove(StatusFlags.Bless);
            var after = map.InView(toX, toY, except: client);

            if (move != null)
            {
                await client.SendAsync(move);
                foreach (var other in before)
                    await other.SendAsync(move);
            }

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
            await Items.Ground.ShowAroundAsync(client, toX, toY, fromX, fromY);
            await Visibility.ShowNpcsAsync(client, toX, toY, fromX, fromY);
            if (prayed)
                await Pk.Pking.SendStatusAsync(client);
            await Monsters.ShowAroundAsync(client, map.Id, toX, toY, fromX, fromY);
        }

        private static async Task RejectAsync(Client client, GameMap<Client> map,
            ushort x, ushort y, int toX, int toY, string kind, string reason)
        {
            LogEvents.For(typeof(Movement), LogEvents.MoveRejected).Warning(
                "{Name} can't {Kind} from {X},{Y} to {ToX},{ToY} on map {MapId}: {Reason}",
                client.Character.Name, kind, x, y, toX, toY, map.Id, reason);
            await client.SendAsync(MsgAction.Teleport(
                client.ID, map.Id, x, y, client.Character.Direction));
            await Visibility.ShowAroundAsync(client, x, y);
        }
    }
}

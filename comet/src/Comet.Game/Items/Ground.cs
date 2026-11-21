namespace Comet.Game.Items
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgMapItem;

    // Dropping, picking up and expiring items on the ground. Drops and pickups run on the
    // player's own packet queue; different players race only for the same ground item, and
    // removing it from the map decides who gets it.
    public static class Ground
    {
        public static TimeSpan Lifetime = TimeSpan.FromSeconds(60);

        // Two drops at once must not pick the same cell
        private static readonly object PlaceLock = new object();

        public static async Task<bool> DropItemAsync(Client client, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (character == null || item == null)
                return Reject(client, "drop", itemID, "not_in_inventory");
            if (character.MapID == Arena.Qualifier.MapID)
                return Reject(client, "drop", itemID, "qualifier");
            if (Locking.IsLocked(item, DateTime.UtcNow))
            {
                await Chat.Talk.NoticeAsync(client, "This equipment is locked. It can't be discarded.");
                return Reject(client, "drop", itemID, "locked");
            }
            if (!Kernel.Ground.TryGet(character.MapID, out var ground))
                return Reject(client, "drop", itemID, "no_map");

            // Taken out first, so an item that just left in a trade can't land on the ground too
            if (!client.Inventory.Remove(item))
                return Reject(client, "drop", itemID, "not_in_inventory");
            var placed = Place(client, ground, new GroundItem
            {
                MapID = character.MapID,
                TypeID = item.TypeID,
                Item = item,
                ExpiresAt = DateTime.UtcNow + Lifetime,
            });
            if (placed == null)
            {
                client.Inventory.Add(item);
                return Reject(client, "drop", itemID, "no_room");
            }

            item.CharacterID = 0;
            item.Position = (byte)ItemPosition.Ground;
            await ItemsRepository.SetOwnerAsync(item.ItemID, 0, item.Position);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
            await ShowAsync(client, placed);

            LogEvents.For(typeof(Ground), LogEvents.ItemDropped).Information(
                "{Name} dropped item {ItemId} (type {TypeId}) and {Silver} silver as ground item " +
                "{GroundId} on map {MapId} at {X},{Y}", character.Name, item.ItemID, item.TypeID, 0,
                placed.Identity, placed.MapID, placed.X, placed.Y);
            return true;
        }

        public static async Task<bool> DropSilverAsync(Client client, uint amount)
        {
            var character = client.Character;
            if (character == null || amount == 0 || amount > character.Silver)
                return Reject(client, "drop", 0, "not_enough_silver");
            if (character.MapID == Arena.Qualifier.MapID)
                return Reject(client, "drop", 0, "qualifier");
            if (!Kernel.Ground.TryGet(character.MapID, out var ground))
                return Reject(client, "drop", 0, "no_map");

            var placed = Place(client, ground, new GroundItem
            {
                MapID = character.MapID,
                TypeID = GroundItem.SilverType(amount),
                Silver = amount,
                ExpiresAt = DateTime.UtcNow + Lifetime,
            });
            if (placed == null)
                return Reject(client, "drop", 0, "no_room");

            await Progress.ChangeSilverAsync(client, -amount, "drop");
            await ShowAsync(client, placed);
            LogEvents.For(typeof(Ground), LogEvents.ItemDropped).Information(
                "{Name} dropped item {ItemId} (type {TypeId}) and {Silver} silver as ground item " +
                "{GroundId} on map {MapId} at {X},{Y}", character.Name, 0, placed.TypeID, amount,
                placed.Identity, placed.MapID, placed.X, placed.Y);
            return true;
        }

        public static async Task<bool> PickUpAsync(Client client, uint groundID)
        {
            var character = client.Character;
            if (character == null || !Kernel.Ground.TryGet(character.MapID, out var ground))
                return Reject(client, "pick_up", groundID, "no_map");
            if (client.Life.Dead)
                return Reject(client, "pick_up", groundID, "dead");
            var found = ground.InView(character.X, character.Y).FirstOrDefault(g => g.Identity == groundID);
            if (found == null)
                return Reject(client, "pick_up", groundID, "not_found");
            if (Math.Max(Math.Abs(found.X - character.X), Math.Abs(found.Y - character.Y)) > 1)
                return Reject(client, "pick_up", groundID, "too_far");
            if (!found.MayPickUp(character.CharacterID, DateTime.UtcNow))
            {
                await Chat.Talk.NoticeAsync(client, "This item belongs to someone else for now.");
                return Reject(client, "pick_up", groundID, "not_owner");
            }
            if (found.Item != null && client.Inventory.FreeSlots == 0)
            {
                await Chat.Talk.NoticeAsync(client, "Your inventory is full.");
                return Reject(client, "pick_up", groundID, "inventory_full");
            }
            if (!ground.Remove(found))
                return Reject(client, "pick_up", groundID, "taken");

            await SendToViewAsync(client, found, new MsgMapItem(found, MapItemAction.Pick));
            await SendToViewAsync(client, found, new MsgMapItem(found, MapItemAction.Delete));
            if (found.Item != null)
            {
                var item = found.Item;
                item.CharacterID = character.CharacterID;
                item.Position = (byte)ItemPosition.Inventory;
                await ItemsRepository.SetOwnerAsync(item.ItemID, item.CharacterID, item.Position);
                client.Inventory.Add(item);
                await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            }
            else
            {
                await Progress.ChangeSilverAsync(client, found.Silver, "pick_up");
            }

            LogEvents.For(typeof(Ground), LogEvents.ItemPickedUp).Information(
                "{Name} picked up item {ItemId} (type {TypeId}) and {Silver} silver from ground item " +
                "{GroundId} on map {MapId} at {X},{Y}", character.Name, found.Item?.ItemID ?? 0,
                found.TypeID, found.Silver, found.Identity, found.MapID, found.X, found.Y);
            return true;
        }

        public static async Task ShowAroundAsync(Client client, ushort x, ushort y, ushort? fromX = null, ushort? fromY = null)
        {
            var character = client.Character;
            if (character == null || !Kernel.Ground.TryGet(character.MapID, out var ground))
                return;
            foreach (var item in ground.InView(x, y))
            {
                if (fromX.HasValue && GameMap<GroundItem>.InView(fromX.Value, fromY.Value, item.X, item.Y))
                    continue;
                await client.SendAsync(new MsgMapItem(item, MapItemAction.Create));
            }
        }

        public static async Task RunExpiryAsync(TimeSpan interval, CancellationToken cancellation)
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                try { await ExpireAsync(DateTime.UtcNow); }
                catch (Exception e)
                {
                    LogEvents.For(typeof(Ground), LogEvents.ItemExpired).Error(e, "Expiring ground items failed");
                }
            }
        }

        public static async Task ExpireAsync(DateTime now)
        {
            foreach (var ground in Kernel.Ground.Loaded)
            {
                foreach (var item in ground.All().Where(i => i.ExpiresAt <= now))
                {
                    if (!ground.Remove(item))
                        continue;
                    if (Kernel.World.TryGet(item.MapID, out var map))
                    {
                        var removal = new MsgMapItem(item, MapItemAction.Delete);
                        foreach (var player in map.InView(item.X, item.Y))
                            await player.SendAsync(removal);
                    }
                    if (item.Item != null)
                        await ItemsRepository.DeleteAsync(item.Item.ItemID);
                    LogEvents.For(typeof(Ground), LogEvents.ItemExpired).Information(
                        "Ground item {GroundId} expired: item {ItemId} (type {TypeId}) and {Silver} silver " +
                        "on map {MapId} at {X},{Y}", item.Identity, item.Item?.ItemID ?? 0, item.TypeID,
                        item.Silver, item.MapID, item.X, item.Y);
                }
            }
        }

        // Loot left by a monster at its cell or next to it; null if there's no room
        public static async Task<GroundItem> PlaceLootAsync(uint mapID, ushort x, ushort y, GroundItem item)
        {
            if (!Kernel.Ground.TryGet(mapID, out var ground))
                return null;
            var placed = Place(ground, x, y, item);
            if (placed != null && Kernel.World.TryGet(mapID, out var map))
            {
                var shown = new MsgMapItem(placed, MapItemAction.Create);
                foreach (var player in map.InView(placed.X, placed.Y))
                    await player.SendAsync(shown);
            }
            return placed;
        }

        private static GroundItem Place(Client client, GameMap<GroundItem> ground, GroundItem item) =>
            Place(ground, client.Character.X, client.Character.Y, item);

        // The cell, or a free walkable cell next to it; null if there's none
        private static GroundItem Place(GameMap<GroundItem> ground, ushort atX, ushort atY, GroundItem item)
        {
            lock (PlaceLock)
            {
                foreach (var (x, y) in Around(atX, atY))
                {
                    if (!ground.Grid.Contains(x, y) || !ground.Grid.IsWalkable(x, y) ||
                        ground.AnyAt((ushort)x, (ushort)y))
                        continue;
                    var placed = new GroundItem
                    {
                        MapID = item.MapID, X = (ushort)x, Y = (ushort)y, TypeID = item.TypeID,
                        Item = item.Item, Silver = item.Silver, ExpiresAt = item.ExpiresAt,
                        OwnerID = item.OwnerID, OwnedUntil = item.OwnedUntil,
                    };
                    ground.Add(placed, placed.X, placed.Y);
                    return placed;
                }
            }
            return null;
        }

        private static IEnumerable<(int X, int Y)> Around(int x, int y)
        {
            yield return (x, y);
            for (int direction = 0; direction < 8; direction++)
                yield return (x + Movement.DeltaX[direction], y + Movement.DeltaY[direction]);
        }

        private static Task ShowAsync(Client client, GroundItem item) =>
            SendToViewAsync(client, item, new MsgMapItem(item, MapItemAction.Create));

        private static async Task SendToViewAsync(Client client, GroundItem item, MsgMapItem packet)
        {
            await client.SendAsync(packet);
            if (Kernel.World.TryGet(item.MapID, out var map))
                foreach (var other in map.InView(item.X, item.Y, except: client))
                    await other.SendAsync(packet);
        }

        private static bool Reject(Client client, string kind, uint id, string reason)
        {
            LogEvents.For(typeof(Ground), LogEvents.ItemGroundRejected).Warning(
                "{Name} can't {Kind} {Id}: {Reason}", client.Character?.Name, kind, id, reason);
            return false;
        }
    }
}

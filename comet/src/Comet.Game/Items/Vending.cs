namespace Comet.Game.Items
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    // Booths in the market. A sale changes two players, whose packets run in parallel, so
    // every booth step runs under one gate, as trades do.
    public static class Vending
    {
        // How far from a flag a player may set up their booth
        public const int FlagReach = 3;

        // The stance of a vendor
        public const uint SitStance = 250;

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        // Open booths by their flag's NPC ID; read without the gate to show them
        private static readonly ConcurrentDictionary<uint, Booth> Booths = new ConcurrentDictionary<uint, Booth>();

        public static async Task OpenAsync(Client client, MsgAction msg)
        {
            if (client.Character == null)
                return;
            if (Arena.Qualifier.IsSignedUp(client))
            {
                Reject(client, "open", 0, 0, "qualifier");
                await Talk.NoticeAsync(client, "Please quit the Qualifier before you start running booth.");
                return;
            }
            await Gate.WaitAsync();
            try
            {
                if (client.Booth != null)
                    await CloseUnderGateAsync(client, "reopened");
                var map = client.Map;
                if (map == null || !map.TryGetPosition(client, out var x, out var y))
                    return;
                var flag = Kernel.Npcs.InView(map.Id, x, y)
                    .Where(n => n.Role == Booth.FlagRole && !Booths.ContainsKey(n.NpcID) &&
                        Math.Max(Math.Abs(n.X - x), Math.Abs(n.Y - y)) <= FlagReach)
                    .OrderBy(n => Math.Max(Math.Abs(n.X - x), Math.Abs(n.Y - y)))
                    .FirstOrDefault();
                if (flag == null)
                {
                    Reject(client, "open", 0, 0, "no_free_flag");
                    await Talk.NoticeAsync(client, "Set up your booth next to a free booth flag in the market.");
                    return;
                }

                var booth = new Booth(client, flag);
                Booths[flag.NpcID] = booth;
                client.Booth = booth;

                // The client opens its booth window on the echo, with the booth's ID in Command
                msg.CharacterID = client.ID;
                msg.Command = booth.ID;
                await client.SendAsync(msg);
                await ShowAroundAsync(map, booth, booth.Spawn());
                LogEvents.For(typeof(Vending), LogEvents.BoothOpened).Information(
                    "{Name} opened booth {BoothId} on map {MapId} at {X},{Y}",
                    client.Character.Name, booth.ID, map.Id, booth.X, booth.Y);
            }
            finally { Gate.Release(); }
        }

        // When the player closes the booth, moves, or leaves the game
        public static async Task CloseAsync(Client client, string reason)
        {
            if (client.Booth == null)
                return;
            await Gate.WaitAsync();
            try { await CloseUnderGateAsync(client, reason); }
            finally { Gate.Release(); }
        }

        // The item ID and its price, in silver or Conquer Points
        public static async Task ListAsync(Client client, MsgItem msg, bool conquerPoints)
        {
            await Gate.WaitAsync();
            try
            {
                var booth = client.Booth;
                var item = client.Inventory?.Find(msg.CharacterID);
                string reason =
                    booth == null ? "no_booth" :
                    item == null ? "not_in_inventory" :
                    booth.Items.ContainsKey(item.ItemID) ? "listed" :
                    msg.Command == 0 ? "no_price" :
                    null;
                if (reason != null)
                {
                    Reject(client, "list", msg.CharacterID, item?.TypeID ?? 0, reason);
                    return;
                }

                booth.Items[item.ItemID] = new Booth.Goods(item, msg.Command, conquerPoints);
                await client.SendAsync(msg);
                LogEvents.For(typeof(Vending), LogEvents.BoothListed).Information(
                    "{Name} put item {ItemId} (type {TypeId}) in booth {BoothId} for {Price} {Currency}",
                    client.Character.Name, item.ItemID, item.TypeID, booth.ID, msg.Command, Currency(conquerPoints));
            }
            finally { Gate.Release(); }
        }

        public static async Task UnlistAsync(Client client, MsgItem msg)
        {
            await Gate.WaitAsync();
            try
            {
                var booth = client.Booth;
                if (booth == null || !booth.Items.Remove(msg.CharacterID, out var goods))
                {
                    Reject(client, "unlist", msg.CharacterID, 0, booth == null ? "no_booth" : "not_listed");
                    return;
                }
                await client.SendAsync(msg);
                LogEvents.For(typeof(Vending), LogEvents.BoothUnlisted).Information(
                    "{Name} took item {ItemId} (type {TypeId}) out of booth {BoothId}",
                    client.Character.Name, goods.Item.ItemID, goods.Item.TypeID, booth.ID);
            }
            finally { Gate.Release(); }
        }

        // A player opens someone's booth: each item for sale, with its price
        public static async Task ShowAsync(Client client, uint boothID)
        {
            await Gate.WaitAsync();
            try
            {
                var booth = Find(boothID);
                if (booth == null || !Near(client, booth))
                {
                    Reject(client, "view", boothID, 0, "not_near");
                    return;
                }
                foreach (var goods in booth.Items.Values.ToList())
                {
                    if (!Owns(booth.Owner, goods))
                    {
                        booth.Items.Remove(goods.Item.ItemID);
                        continue;
                    }
                    await client.SendAsync(new MsgItemInfoEx(goods.Item, booth.ID, goods.Price,
                        goods.ConquerPoints ? MsgItemInfoEx.ViewType.ConquerPoints : MsgItemInfoEx.ViewType.Silver));
                }
            }
            finally { Gate.Release(); }
        }

        // The item ID, and the booth's ID in Command
        public static async Task BuyAsync(Client client, MsgItem msg)
        {
            if (client.Character == null || client.Inventory == null)
                return;
            await Gate.WaitAsync();
            try { await BuyUnderGateAsync(client, msg); }
            finally { Gate.Release(); }
        }

        private static async Task BuyUnderGateAsync(Client buyer, MsgItem msg)
        {
            uint itemID = msg.CharacterID, boothID = msg.Command;
            var booth = Find(boothID);
            Booth.Goods goods = null;
            var owner = booth?.Owner;
            string reason =
                booth == null ? "no_booth" :
                owner == buyer ? "own_booth" :
                !booth.Items.TryGetValue(itemID, out goods) ? "not_listed" :
                !Owns(owner, goods) ? "item_gone" :
                !Near(buyer, booth) ? "not_near" :
                buyer.Inventory.FreeSlots < 1 ? "inventory_full" :
                goods.Price > Balance(buyer, goods.ConquerPoints) ? "not_enough_money" :
                goods.Price + (long)Balance(owner, goods.ConquerPoints) > uint.MaxValue ? "seller_too_rich" :
                null;
            if (reason != null)
            {
                if (reason == "item_gone")
                    booth.Items.Remove(itemID);
                Reject(buyer, "buy", itemID, goods?.Item.TypeID ?? 0, reason);
                await Talk.NoticeAsync(buyer, reason switch
                {
                    "inventory_full" => "Your inventory is full.",
                    "not_enough_money" => goods.ConquerPoints ? "You don't have enough Conquer Points." : "You don't have enough silver.",
                    _ => "That item is no longer for sale.",
                });
                return;
            }

            var item = goods.Item;
            if (!owner.Inventory.Remove(item))
            {
                booth.Items.Remove(itemID);
                Reject(buyer, "buy", itemID, item.TypeID, "item_gone");
                return;
            }
            if (!await Pay(buyer, -goods.Price, goods.ConquerPoints, "booth_buy"))
            {
                owner.Inventory.Add(item);
                Reject(buyer, "buy", itemID, item.TypeID, "not_enough_money");
                return;
            }
            try
            {
                await ItemsRepository.TransferAsync(new[] { (item.ItemID, buyer.ID) });
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(Vending), LogEvents.BoothRejected).Error(e,
                    "Moving item {ItemId} from {Owner}'s booth to {Name} failed",
                    item.ItemID, owner.Character?.Name, buyer.Character.Name);
                owner.Inventory.Add(item);
                await Pay(buyer, goods.Price, goods.ConquerPoints, "booth_refund");
                return;
            }

            booth.Items.Remove(itemID);
            item.CharacterID = buyer.ID;
            item.Position = (byte)ItemPosition.Inventory;
            buyer.Inventory.Add(item);
            await Pay(owner, goods.Price, goods.ConquerPoints, "booth_sale");

            await buyer.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            await buyer.SendAsync(msg);
            await owner.SendAsync(MsgItem.BoothUnlisted(item.ItemID, booth.ID));
            await owner.SendAsync(MsgItem.Removed(item.ItemID));
            var name = Kernel.ItemTypes.TryGet(item.TypeID, out var type) ? type.Name : item.TypeID.ToString();
            await Talk.NoticeAsync(owner, $"{buyer.Character.Name} bought your {name} for {goods.Price} {Currency(goods.ConquerPoints)}.");
            LogEvents.For(typeof(Vending), LogEvents.BoothSold).Information(
                "{Name} bought item {ItemId} (type {TypeId}) from {Owner}'s booth {BoothId} for {Price} {Currency}",
                buyer.Character.Name, item.ItemID, item.TypeID, owner.Character?.Name, booth.ID, goods.Price,
                Currency(goods.ConquerPoints));
            await buyer.SaveAsync("booth");
            await owner.SaveAsync("booth");
        }

        // A vendor message: everyone around the booth hears it now, and those who come later
        public static async Task HawkAsync(Client client, MsgTalk msg)
        {
            var booth = client.Booth;
            var map = client.Map;
            if (booth == null || map == null)
            {
                Reject(client, "hawk", 0, 0, "no_booth");
                return;
            }
            booth.Hawk = msg;
            foreach (var other in map.InView(booth.X, booth.Y))
                await other.SendAsync(msg);
        }

        // What a player sees of an NPC: a flag, and the booth open at it
        public static async Task ShowNpcAsync(Client client, Npc npc)
        {
            await client.SendAsync(new MsgNpcInfo(npc));
            if (npc.Role == Booth.FlagRole && Booths.TryGetValue(npc.NpcID, out var booth))
            {
                await client.SendAsync(booth.Spawn());
                if (booth.Hawk != null)
                    await client.SendAsync(booth.Hawk);
            }
        }

        private static async Task CloseUnderGateAsync(Client client, string reason)
        {
            var booth = client.Booth;
            if (booth == null)
                return;
            client.Booth = null;
            Booths.TryRemove(booth.Flag.NpcID, out _);
            if (Kernel.World.TryGet(booth.Flag.MapID, out var map))
                await ShowAroundAsync(map, booth, MsgAction.RemoveEntity(booth.ID));
            LogEvents.For(typeof(Vending), LogEvents.BoothClosed).Information(
                "{Name} closed booth {BoothId} with {Count} items unsold: {Reason}",
                client.Character?.Name, booth.ID, booth.Items.Count, reason);
        }

        // Players see a booth when they see its flag
        private static async Task ShowAroundAsync(GameMap<Client> map, Booth booth, IPacket packet)
        {
            foreach (var other in map.InView(booth.Flag.X, booth.Flag.Y))
                if (other.Character != null)
                    await other.SendAsync(packet);
        }

        private static Booth Find(uint boothID) =>
            boothID > Booth.IdOffset && Booths.TryGetValue(boothID - Booth.IdOffset, out var booth) ? booth : null;

        private static bool Owns(Client owner, Booth.Goods goods) =>
            owner.Inventory?.Find(goods.Item.ItemID) == goods.Item;

        private static bool Near(Client client, Booth booth)
        {
            var map = client.Map;
            return map != null && map.Id == booth.Flag.MapID &&
                map.TryGetPosition(client, out var x, out var y) &&
                GameMap<Client>.InView(x, y, booth.X, booth.Y);
        }

        private static uint Balance(Client client, bool conquerPoints) =>
            conquerPoints ? client.Character?.Jewels ?? 0 : client.Character?.Silver ?? 0;

        private static Task<bool> Pay(Client client, long amount, bool conquerPoints, string reason) =>
            conquerPoints
                ? Progress.ChangeConquerPointsAsync(client, amount, reason)
                : Progress.ChangeSilverAsync(client, amount, reason);

        private static string Currency(bool conquerPoints) => conquerPoints ? "CPs" : "silver";

        private static void Reject(Client client, string action, uint id, uint typeID, string reason) =>
            LogEvents.For(typeof(Vending), LogEvents.BoothRejected).Warning(
                "{Name} can't {Action} {Id} (type {TypeId}): {Reason}",
                client.Character?.Name, action, id, typeID, reason);
    }
}

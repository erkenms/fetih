namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using static Comet.Game.Packets.MsgTrade;

    // Trades change two players, whose packets run in parallel, so every trade step runs
    // under one gate. Items and silver leave the givers before anything reaches the
    // receivers, and the database moves all items in one transaction.
    public static class Trading
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static async Task HandleAsync(Client client, MsgTrade msg)
        {
            if (client.Character == null)
                return;
            await Gate.WaitAsync();
            try
            {
                switch (msg.Action)
                {
                    case TradeAction.Request: await RequestAsync(client, msg.Data); break;
                    case TradeAction.Close:
                    case TradeAction.TimeOut: await CancelUnderGateAsync(client, "closed"); break;
                    case TradeAction.AddItem: await OfferItemAsync(client, msg.Data); break;
                    case TradeAction.SetSilver: await OfferSilverAsync(client, msg.Data); break;
                    case TradeAction.SetConquerPoints: await OfferConquerPointsAsync(client, msg.Data); break;
                    case TradeAction.Accept: await AcceptAsync(client); break;
                    default:
                        LogEvents.For(typeof(Trading), LogEvents.PacketUnhandled).Warning(
                            "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                            msg.Type, msg.Action, msg.Length);
                        break;
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task CancelAsync(Client client, string reason)
        {
            if (client.Trade == null && client.TradeRequest == 0)
                return;
            await Gate.WaitAsync();
            try { await CancelUnderGateAsync(client, reason); }
            finally { Gate.Release(); }
        }

        private static async Task RequestAsync(Client client, uint targetID)
        {
            if (client.Trade != null)
                return;
            if (!Kernel.Players.TryGet(targetID, out var target) || target == client || !Near(client, target))
            {
                Reject(client, "request", targetID, "not_near");
                return;
            }
            if (target.Trade != null)
            {
                Reject(client, "request", targetID, "busy");
                await Talk.NoticeAsync(client, $"{target.Character.Name} is trading with someone else.");
                return;
            }

            // Asking someone who asked first opens the window
            if (target.TradeRequest == client.ID)
            {
                var trade = new Trade(target, client);
                client.Trade = target.Trade = trade;
                client.TradeRequest = target.TradeRequest = 0;
                await target.SendAsync(new MsgTrade(TradeAction.ShowTable, client.ID));
                await client.SendAsync(new MsgTrade(TradeAction.ShowTable, target.ID));
                LogEvents.For(typeof(Trading), LogEvents.TradeOpened).Information(
                    "{Name} and {Partner} opened a trade (characters {CharacterId} and {PartnerId})",
                    target.Character.Name, client.Character.Name, target.ID, client.ID);
                return;
            }

            client.TradeRequest = target.ID;
            await target.SendAsync(new MsgRelation(client.Character, target.ID));
            await target.SendAsync(new MsgTrade(TradeAction.Request, client.ID));
            await Talk.NoticeAsync(client, "Trade request sent.");
            LogEvents.For(typeof(Trading), LogEvents.TradeRequested).Information(
                "{Name} asked {Partner} to trade", client.Character.Name, target.Character.Name);
        }

        private static async Task OfferItemAsync(Client client, uint itemID)
        {
            var trade = client.Trade;
            var item = client.Inventory?.Find(itemID);
            string reason =
                trade == null ? "no_trade" :
                trade.AnyAccepted ? "accepted" :
                item == null ? "not_in_inventory" :
                trade.Of(client).Items.Contains(item) ? "offered" :
                trade.PartnerOf(client).Player.Inventory.FreeSlots <= trade.Of(client).Items.Count ? "partner_full" :
                Guilds.IsInscribed(item.ItemID) ? "inscribed" :
                !MayGive(client, trade.PartnerOf(client).Player, item, DateTime.UtcNow) ? "locked" :
                null;
            if (reason != null)
            {
                Reject(client, "offer_item", itemID, reason);
                await client.SendAsync(new MsgTrade(TradeAction.AddItemFailed, itemID));
                if (reason == "partner_full")
                    await Talk.NoticeAsync(client, $"{trade.PartnerOf(client).Player.Character.Name} has no room for more items.");
                else if (reason == "inscribed")
                    await Talk.NoticeAsync(client, "The inscribed equipment cannot be traded.");
                else if (reason == "locked")
                    await Talk.NoticeAsync(client, "This equipment is locked. You can only trade it to your trade partners.");
                return;
            }

            trade.Of(client).Items.Add(item);
            await trade.PartnerOf(client).Player.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Trade));
            Offered(client, "item", item.ItemID, item.TypeID);
        }

        private static async Task OfferSilverAsync(Client client, uint amount)
        {
            var trade = client.Trade;
            string reason =
                trade == null ? "no_trade" :
                trade.AnyAccepted ? "accepted" :
                amount > client.Character.Silver ? "not_enough_silver" :
                null;
            if (reason != null)
            {
                Reject(client, "offer_silver", amount, reason);
                if (reason == "not_enough_silver")
                    await Talk.NoticeAsync(client, "You don't have that much silver.");
                return;
            }

            trade.Of(client).Silver = amount;
            await trade.PartnerOf(client).Player.SendAsync(new MsgTrade(TradeAction.ShowSilver, amount));
            Offered(client, "silver", amount, 0);
        }

        private static async Task OfferConquerPointsAsync(Client client, uint amount)
        {
            var trade = client.Trade;
            string reason =
                trade == null ? "no_trade" :
                trade.AnyAccepted ? "accepted" :
                amount > client.Character.Jewels ? "not_enough_conquer_points" :
                null;
            if (reason != null)
            {
                Reject(client, "offer_conquer_points", amount, reason);
                if (reason == "not_enough_conquer_points")
                    await Talk.NoticeAsync(client, "You don't have that many Conquer Points.");
                return;
            }

            trade.Of(client).ConquerPoints = amount;
            await trade.PartnerOf(client).Player.SendAsync(new MsgTrade(TradeAction.ShowConquerPoints, amount));
            Offered(client, "conquer_points", amount, 0);
        }

        private static async Task AcceptAsync(Client client)
        {
            var trade = client.Trade;
            if (trade == null || trade.Of(client).Accepted)
                return;
            trade.Of(client).Accepted = true;
            var partner = trade.PartnerOf(client);
            if (!partner.Accepted)
            {
                await partner.Player.SendAsync(new MsgTrade(TradeAction.Accept, client.ID));
                return;
            }

            // The client keeps its own silver while the window is open and puts it back on
            // closing, so the windows close before any silver or item changes go out
            var first = trade.First.Player;
            var second = trade.Second.Player;
            first.Trade = second.Trade = null;
            await first.SendAsync(new MsgTrade(TradeAction.HideTable, second.ID));
            await second.SendAsync(new MsgTrade(TradeAction.HideTable, first.ID));
            var failure = await ExchangeAsync(trade);
            await Talk.NoticeAsync(first, failure == null ? "Trade succeeded." : "Trade failed.");
            await Talk.NoticeAsync(second, failure == null ? "Trade succeeded." : "Trade failed.");
            if (failure != null)
            {
                Cancelled(trade, client, failure);
                return;
            }

            LogEvents.For(typeof(Trading), LogEvents.TradeCompleted).Information(
                "{Name} gave {Partner} items {ItemIds} (types {TypeIds}), {Silver} silver and " +
                "{ConquerPoints} CPs for items {PartnerItemIds} (types {PartnerTypeIds}), " +
                "{PartnerSilver} silver and {PartnerConquerPoints} CPs " +
                "(characters {CharacterId} and {PartnerId})",
                first.Character.Name, second.Character.Name,
                trade.First.Items.Select(i => i.ItemID).ToArray(), trade.First.Items.Select(i => i.TypeID).ToArray(),
                trade.First.Silver, trade.First.ConquerPoints,
                trade.Second.Items.Select(i => i.ItemID).ToArray(), trade.Second.Items.Select(i => i.TypeID).ToArray(),
                trade.Second.Silver, trade.Second.ConquerPoints, first.ID, second.ID);
            await first.SaveAsync("trade");
            await second.SaveAsync("trade");
        }

        // Null when done, else why nothing changed hands
        private static async Task<string> ExchangeAsync(Trade trade)
        {
            var first = trade.First;
            var second = trade.Second;
            if (!InGame(first.Player) || !InGame(second.Player) || !Near(first.Player, second.Player))
                return "not_near";

            // Each receiver gets room from the items they give away
            if (first.Player.Inventory.FreeSlots + first.Items.Count < second.Items.Count ||
                second.Player.Inventory.FreeSlots + second.Items.Count < first.Items.Count)
                return "inventory_full";

            var now = DateTime.UtcNow;
            if (first.Items.Any(i => !MayGive(first.Player, second.Player, i, now)) ||
                second.Items.Any(i => !MayGive(second.Player, first.Player, i, now)))
                return "locked";

            var taken = new List<(Trade.Offer From, DbItem Item)>();
            foreach (var offer in new[] { first, second })
            {
                foreach (var item in offer.Items)
                {
                    if (!offer.Player.Inventory.Remove(item))
                    {
                        PutBack(taken);
                        return "item_gone";
                    }
                    taken.Add((offer, item));
                }
            }

            var paid = new List<(Trade.Offer From, bool Silver, uint Amount)>();
            foreach (var offer in new[] { first, second })
            {
                if (offer.Silver > 0)
                {
                    if (!await Progress.ChangeSilverAsync(offer.Player, -offer.Silver, "trade"))
                        return await RefundAsync(taken, paid, "not_enough_silver");
                    paid.Add((offer, true, offer.Silver));
                }
                if (offer.ConquerPoints > 0)
                {
                    if (!await Progress.ChangeConquerPointsAsync(offer.Player, -offer.ConquerPoints, "trade"))
                        return await RefundAsync(taken, paid, "not_enough_conquer_points");
                    paid.Add((offer, false, offer.ConquerPoints));
                }
            }

            try
            {
                await ItemsRepository.TransferAsync(taken
                    .Select(t => (t.Item.ItemID, trade.PartnerOf(t.From.Player).Player.ID)).ToList());
            }
            catch (Exception e)
            {
                LogEvents.For(typeof(Trading), LogEvents.TradeCancelled).Error(e,
                    "Moving the traded items of {Name} and {Partner} failed",
                    first.Player.Character.Name, second.Player.Character.Name);
                return await RefundAsync(taken, paid, "database");
            }

            foreach (var (from, item) in taken)
            {
                var receiver = trade.PartnerOf(from.Player).Player;
                item.CharacterID = receiver.ID;
                item.Position = (byte)ItemPosition.Inventory;
                receiver.Inventory.Add(item);
                await from.Player.SendAsync(MsgItem.Removed(item.ItemID));
                await receiver.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
            }
            foreach (var (from, silver, amount) in paid)
            {
                var receiver = trade.PartnerOf(from.Player).Player;
                if (silver)
                    await Progress.ChangeSilverAsync(receiver, amount, "trade");
                else
                    await Progress.ChangeConquerPointsAsync(receiver, amount, "trade");
            }
            return null;
        }

        private static void PutBack(List<(Trade.Offer From, DbItem Item)> taken)
        {
            foreach (var (from, item) in taken)
                from.Player.Inventory.Add(item);
        }

        private static async Task<string> RefundAsync(List<(Trade.Offer From, DbItem Item)> taken,
            List<(Trade.Offer From, bool Silver, uint Amount)> paid, string reason)
        {
            PutBack(taken);
            foreach (var (from, silver, amount) in paid)
            {
                if (silver)
                    await Progress.ChangeSilverAsync(from.Player, amount, "trade_refund");
                else
                    await Progress.ChangeConquerPointsAsync(from.Player, amount, "trade_refund");
            }
            return reason;
        }

        private static async Task CancelUnderGateAsync(Client client, string reason)
        {
            client.TradeRequest = 0;
            var trade = client.Trade;
            if (trade == null)
                return;
            var first = trade.First.Player;
            var second = trade.Second.Player;
            first.Trade = second.Trade = null;
            await first.SendAsync(new MsgTrade(TradeAction.HideTable, second.ID));
            await second.SendAsync(new MsgTrade(TradeAction.HideTable, first.ID));
            var partner = trade.PartnerOf(client).Player;
            if (partner.Character != null)
                await Talk.NoticeAsync(partner, "The trade was cancelled.");
            Cancelled(trade, client, reason);
        }

        private static void Cancelled(Trade trade, Client by, string reason) =>
            LogEvents.For(typeof(Trading), LogEvents.TradeCancelled).Information(
                "Trade of {Name} and {Partner} ended without exchange: {Reason} (by {By})",
                trade.First.Player.Character?.Name, trade.Second.Player.Character?.Name, reason,
                by.Character?.Name);

        private static bool MayGive(Client giver, Client receiver, DbItem item, DateTime now) =>
            !Guilds.IsInscribed(item.ItemID) &&
            (!Locking.IsLocked(item, now) || TradePartners.IsFormal(giver, receiver.ID, now));

        private static bool InGame(Client client) =>
            client.Character != null && Kernel.Players.TryGet(client.ID, out var online) && online == client;

        private static bool Near(Client client, Client other)
        {
            var map = client.Map;
            return map != null && map == other.Map &&
                map.TryGetPosition(client, out var x, out var y) &&
                map.TryGetPosition(other, out var otherX, out var otherY) &&
                GameMap<Client>.InView(x, y, otherX, otherY);
        }

        private static void Offered(Client client, string kind, uint value, uint typeID) =>
            LogEvents.For(typeof(Trading), LogEvents.TradeOffered).Debug(
                "{Name} offered {Kind} {Value} (type {TypeId}) to {Partner}",
                client.Character.Name, kind, value, typeID, client.Trade.PartnerOf(client).Player.Character.Name);

        private static void Reject(Client client, string kind, uint value, string reason) =>
            LogEvents.For(typeof(Trading), LogEvents.TradeRejected).Warning(
                "{Name} can't {Kind} {Value}: {Reason}", client.Character?.Name, kind, value, reason);
    }
}

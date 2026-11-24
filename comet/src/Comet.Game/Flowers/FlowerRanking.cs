namespace Comet.Game.Flowers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    public static class FlowerRanking
    {
        private const int MaleGender = 1;
        private const int FemaleGender = 2;

        private static readonly object Sync = new object();
        private static readonly Dictionary<uint, FlowerRecord> Records = new Dictionary<uint, FlowerRecord>();

        private sealed record Snapshot(
            Dictionary<FlowerKind, List<FlowerEntry>> Boards,
            Dictionary<uint, Dictionary<FlowerKind, FlowerEntry>> ByPlayer,
            Dictionary<uint, uint> Charms);

        // Replaced whole on each change so MsgPlayer and rank queries read without the lock
        private static volatile Snapshot current = EmptySnapshot();

        public static int Count
        {
            get { lock (Sync) return Records.Count; }
        }

        public static async Task LoadAsync()
        {
            var rows = await FlowersRepository.LoadAsync();
            lock (Sync)
            {
                Records.Clear();
                foreach (var row in rows)
                {
                    Records[row.CharacterID] = new FlowerRecord(
                        row.CharacterID, row.Name,
                        row.RedRoses, row.RedRosesToday,
                        row.Lilies, row.LiliesToday,
                        row.Orchids, row.OrchidsToday,
                        row.Tulips, row.TulipsToday,
                        row.TodayDate);
                }
                Rebuild();
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Records.Clear();
                current = EmptySnapshot();
            }
        }

        public static uint CharmOf(uint characterID) =>
            current.Charms.TryGetValue(characterID, out var charm) ? charm : 0;

        public static bool CanSendFreeToday(Character character, DateTime utcNow) =>
            character != null &&
            Requirements.Gender(character.Mesh) == MaleGender &&
            character.FlowerSentDay != Clock.Today(utcNow);

        // After MsgUserInfo: male characters get the free-flower button if unused today;
        // female characters get their flower counts and the flower button.
        public static async Task LoginAsync(Client client)
        {
            var character = client.Character;
            if (character == null)
                return;
            var today = Clock.Today(DateTime.UtcNow);
            int gender = Requirements.Gender(character.Mesh);
            if (gender == MaleGender)
            {
                if (character.FlowerSentDay != today)
                    await client.SendAsync(MsgFlower.DailyAvailable(client.ID));
                return;
            }
            if (gender != FemaleGender)
                return;

            FlowerRecord record;
            lock (Sync)
            {
                if (Records.TryGetValue(character.CharacterID, out record))
                {
                    if (record.Name != character.Name)
                    {
                        record = record with { Name = character.Name };
                        Records[character.CharacterID] = record;
                        Rebuild();
                    }
                }
                else
                {
                    record = new FlowerRecord(character.CharacterID, character.Name, 0, 0, 0, 0, 0, 0, 0, 0, null);
                }
            }
            await client.SendAsync(MsgFlower.Info(character.CharacterID, record, today));
        }

        public static async Task HandleSendAsync(Client client, MsgFlower msg)
        {
            if (client.Character == null)
                return;
            if (msg.Mode != MsgFlower.SendFlowerMode)
            {
                LogEvents.For(typeof(FlowerRanking), LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} mode {Mode} ({Length} bytes)",
                    msg.Type, msg.Mode, msg.Length);
                return;
            }

            var sender = client.Character;
            if (client.Life.Dead)
            {
                await RejectAsync(client, msg.TargetID, "dead", "You cannot send flowers while dead.");
                return;
            }
            if (Requirements.Gender(sender.Mesh) != MaleGender)
            {
                await RejectAsync(client, msg.TargetID, "not_male", "Only male characters can send flowers.");
                return;
            }
            if (!Kernel.Players.TryGet(msg.TargetID, out var target) || target.Character == null)
            {
                await RejectAsync(client, msg.TargetID, "target_offline", "That player is not online.");
                return;
            }
            if (Requirements.Gender(target.Character.Mesh) != FemaleGender)
            {
                await RejectAsync(client, msg.TargetID, "target_not_female", "You can only send flowers to female characters.");
                return;
            }

            var today = Clock.Today(DateTime.UtcNow);
            FlowerKind kind;
            uint amount;
            bool isFree;
            DbItem consumed = null;

            if (msg.ItemID == 0)
            {
                if (sender.FlowerSentDay == today)
                {
                    await RejectAsync(client, msg.TargetID, "daily_used", "You have already sent your free flowers today.");
                    return;
                }
                kind = FlowerKind.RedRose;
                amount = FlowerBoard.DailyAllowance(sender.VIPLevel);
                isFree = true;
            }
            else
            {
                consumed = client.Inventory?.Find(msg.ItemID);
                if (consumed == null)
                {
                    await RejectAsync(client, msg.TargetID, "item_missing", "Flower item not found.");
                    return;
                }
                if (!FlowerBoard.TryParseFlowerItem(consumed.TypeID, Kernel.ItemTypes, out kind, out amount))
                {
                    await RejectAsync(client, msg.TargetID, "not_flower", "That item is not a flower.");
                    return;
                }
                isFree = false;
            }

            if (isFree)
            {
                sender.FlowerSentDay = today;
                await FlowersRepository.SaveSentDayAsync(sender.CharacterID, today);
            }
            else
            {
                client.Inventory.Remove(consumed);
                await ItemsRepository.DeleteAsync(consumed.ItemID);
                await client.SendAsync(MsgItem.Removed(consumed.ItemID));
            }

            FlowerRecord updated;
            Snapshot before, after;
            lock (Sync)
            {
                before = current;
                if (!Records.TryGetValue(target.ID, out var existing))
                    existing = new FlowerRecord(target.ID, target.Character.Name, 0, 0, 0, 0, 0, 0, 0, 0, null);
                else if (existing.Name != target.Character.Name)
                    existing = existing with { Name = target.Character.Name };

                updated = existing.Add(kind, amount, today);
                Records[target.ID] = updated;
                Rebuild();
                after = current;
            }

            await FlowersRepository.SaveAsync(ToDb(updated));

            LogEvents.For(typeof(FlowerRanking), LogEvents.FlowerSent).Information(
                "{Sender} sent {Amount} {Kind} to {Receiver} (free={Free})",
                sender.Name, amount, kind, target.Character.Name, isFree);

            var sent = MsgFlower.Sent(sender.Name, target.Character.Name, amount, kind, isFree);
            await client.SendAsync(sent);
            if (target.ID != client.ID)
                await target.SendAsync(sent);
            await Movement.SendToViewAsync(target, sent, other => other.ID != client.ID);

            await BroadcastCharmChangesAsync(before, after);
        }

        public static async Task HandleRankAsync(Client client, MsgRank msg)
        {
            if (client.Character == null)
                return;

            switch (msg.Mode)
            {
                case MsgRank.OwnFlowerRankMode:
                    await SendOwnRanksAsync(client);
                    return;
                case MsgRank.QueryMode:
                    await SendPageAsync(client, msg.Data, msg.Page);
                    return;
                default:
                    LogEvents.For<MsgRank>(LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} action {Action} ({Length} bytes)",
                        msg.Type, msg.Mode, msg.Length);
                    return;
            }
        }

        public static async Task SendOwnRanksAsync(Client client)
        {
            var snap = current;
            if (snap.ByPlayer.TryGetValue(client.ID, out var ranks))
            {
                foreach (var kind in FlowerBoard.AllKinds)
                {
                    if (ranks.TryGetValue(kind, out var entry))
                    {
                        await client.SendAsync(MsgRank.OwnRank(
                            FlowerBoard.RankTypeOf(kind),
                            entry.Place,
                            entry.Amount,
                            client.ID,
                            client.Character.Name));
                    }
                }
            }
            await client.SendAsync(MsgRank.OwnRankEnd());
        }

        private static async Task SendPageAsync(Client client, uint rankType, byte page)
        {
            if (!FlowerBoard.TryKindOf(rankType, out var kind))
            {
                LogEvents.For<MsgRank>(LogEvents.PacketUnhandled).Warning(
                    "Unhandled packet {PacketType} rank type {RankType} page {Page}",
                    PacketType.MsgRank, rankType, page);
                return;
            }
            var snap = current;
            var entries = FlowerBoard.Page(snap.Boards[kind], page);
            await client.SendAsync(MsgRank.PageResult(rankType, page, entries));
        }

        private static async Task BroadcastCharmChangesAsync(Snapshot before, Snapshot after)
        {
            var changed = new HashSet<uint>();
            foreach (var (id, charm) in after.Charms)
                if (!before.Charms.TryGetValue(id, out var old) || old != charm)
                    changed.Add(id);
            foreach (var id in before.Charms.Keys)
                if (!after.Charms.ContainsKey(id))
                    changed.Add(id);

            foreach (var id in changed)
            {
                if (!Kernel.Players.TryGet(id, out var player) || player.Character == null)
                    continue;
                uint newCharm = after.Charms.TryGetValue(id, out var c) ? c : 0;
                await SendOwnRanksAsync(player);
                await Movement.SendToViewAsync(player, MsgRank.OtherRank(player.ID, newCharm));
                await Movement.SendToViewAsync(player, new MsgPlayer(player));
            }
        }

        private static async Task RejectAsync(Client client, uint targetID, string reason, string text)
        {
            LogEvents.For(typeof(FlowerRanking), LogEvents.FlowerRejected).Warning(
                "{Name} could not send flower to {TargetId}: {Reason}",
                client.Character?.Name, targetID, reason);
            await client.SendAsync(new MsgTalk(client.ID, MsgTalk.TalkChannel.System, text));
        }

        private static void Rebuild()
        {
            var boards = new Dictionary<FlowerKind, List<FlowerEntry>>(4);
            var byPlayer = new Dictionary<uint, Dictionary<FlowerKind, FlowerEntry>>();
            foreach (var kind in FlowerBoard.AllKinds)
            {
                var ordered = FlowerBoard.Order(Records.Values, kind);
                boards[kind] = ordered;
                foreach (var entry in ordered)
                {
                    if (!byPlayer.TryGetValue(entry.CharacterID, out var map))
                        byPlayer[entry.CharacterID] = map = new Dictionary<FlowerKind, FlowerEntry>();
                    map[kind] = entry;
                }
            }

            var charms = new Dictionary<uint, uint>(byPlayer.Count);
            foreach (var (id, map) in byPlayer)
            {
                var places = map.ToDictionary(kv => kv.Key, kv => kv.Value.Place);
                uint charm = FlowerBoard.BestCharmId(places);
                if (charm != 0)
                    charms[id] = charm;
            }

            current = new Snapshot(boards, byPlayer, charms);
        }

        private static Snapshot EmptySnapshot()
        {
            var boards = FlowerBoard.AllKinds.ToDictionary(k => k, _ => new List<FlowerEntry>());
            return new Snapshot(
                boards,
                new Dictionary<uint, Dictionary<FlowerKind, FlowerEntry>>(),
                new Dictionary<uint, uint>());
        }

        private static DbFlower ToDb(FlowerRecord r) => new DbFlower
        {
            CharacterID = r.CharacterID,
            RedRoses = r.RedRoses,
            RedRosesToday = r.RedRosesToday,
            Lilies = r.Lilies,
            LiliesToday = r.LiliesToday,
            Orchids = r.Orchids,
            OrchidsToday = r.OrchidsToday,
            Tulips = r.Tulips,
            TulipsToday = r.TulipsToday,
            TodayDate = r.TodayDate
        };
    }
}

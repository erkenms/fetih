namespace Fetih.Game.Items
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Database.Repositories;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using static Fetih.Game.Packets.MsgItemStatus;

    // Refinery items give gear a state for 7 days (help455): the player uses one and picks the
    // gear, and the client sends MsgQuench. The Refinery Stabilizer (help459) makes it
    // permanent for Permanent Stones' points. A temporary state hides a permanent one until it
    // runs out. Runs on the player's own packet queue.
    public static class Refinery
    {
        public const uint StabilizerID = 7538;
        public static readonly TimeSpan Term = TimeSpan.FromDays(7);

        // VIP levels add days to the term (StrRes 11328-11333: 1, 2, 4, then 7 days)
        public static int VipDays(uint vipLevel) => vipLevel switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            3 => 4,
            _ => 7,
        };

        public static bool HasTemporary(DbItem item, DateTime now) =>
            item.RefineryKind != 0 && item.RefineryExpiresAt > now;

        public static bool HasState(DbItem item, DateTime now) => item.StableKind != 0 || HasTemporary(item, now);

        // The state in effect
        public static (RefineryKind Kind, uint Power) Active(DbItem item, DateTime now) => HasTemporary(item, now)
            ? ((RefineryKind)item.RefineryKind, item.RefineryPower)
            : ((RefineryKind)item.StableKind, item.StablePower);

        // The worn items' powers of one kind
        public static int Bonus(IEnumerable<DbItem> worn, RefineryKind kind, DateTime now) =>
            worn?.Sum(item => Active(item, now) is var (k, power) && k == kind ? (int)power : 0) ?? 0;

        // Whether the refinery item can go on the gear, by its itemtype kind line (field 53),
        // which names the gear its description tells the player to refine
        public static bool Fits(ItemType refinery, uint gear)
        {
            uint sort = gear / 1000;
            bool heavyRing = refinery.Description.Contains("Heavy Ring", StringComparison.OrdinalIgnoreCase);
            return refinery.Kind switch
            {
                "Necklace" => sort == 120,
                "Bag" => sort == 121,
                "[Necklace&Bag][FireResistance]Item" => sort is 120 or 121,
                "Headgear" => ItemSlots.Fits(gear, ItemPosition.Headwear),
                "Armor" => ItemSlots.Fits(gear, ItemPosition.Armor),
                "Ring" => sort == 150 || heavyRing && sort == 151,
                "(Heavy)Ring" => sort is 150 or 151,
                "Bracelet" => sort == 152,
                "Boots" => sort == 160,
                "Shield" => ItemSlots.IsShield(gear),
                "Bow" => ItemSlots.IsBow(gear),
                "Backsword" => sort == 421,
                "1-Handed" => ItemSlots.IsOneHanded(gear),
                "2-Handed" => ItemSlots.IsTwoHanded(gear) && !ItemSlots.IsBow(gear),
                _ => false,
            };
        }

        public static void Apply(DbItem gear, Refinement refinement, DateTime now, uint vipLevel)
        {
            gear.RefineryKind = (uint)refinement.Kind;
            gear.RefineryLevel = refinement.Level;
            gear.RefineryPower = refinement.Power;
            gear.RefineryExpiresAt = now + Term + TimeSpan.FromDays(VipDays(vipLevel));
        }

        // The points stabilizing the temporary state takes, and those a permanent one of a lower
        // level already counts for, as the 5619 window shows them; one already as high asks
        // for the first level's
        public static (uint Needed, uint Credit) Points(DbItem gear, RefineryTable table)
        {
            int stable = gear.StableKind != 0 ? gear.StableLevel : 0;
            if (stable >= 1 && gear.RefineryLevel <= stable)
                return (table.PointsFor(1), 0);
            return (table.PointsFor(gear.RefineryLevel), stable >= 1 ? table.PointsFor(stable) : 0);
        }

        public static void Stabilize(DbItem gear)
        {
            gear.StableKind = gear.RefineryKind;
            gear.StableLevel = gear.RefineryLevel;
            gear.StablePower = gear.RefineryPower;
            ClearTemporary(gear);
        }

        public static void ClearTemporary(DbItem gear)
        {
            gear.RefineryKind = 0;
            gear.RefineryLevel = 0;
            gear.RefineryPower = 0;
            gear.RefineryExpiresAt = null;
        }

        // The item's states for the client: the permanent one first, as setting the temporary
        // one leaves it
        public static Entry[] Entries(DbItem item, DateTime now)
        {
            var entries = new List<Entry>();
            if (item.StableKind != 0)
                entries.Add(new Entry(item.ItemID, StatusMode.Stable, item.StableKind, item.StableLevel, item.StablePower, 0));
            if (HasTemporary(item, now))
                entries.Add(TemporaryEntry(item, now));
            return entries.ToArray();
        }

        private static Entry TemporaryEntry(DbItem item, DateTime now) => new Entry(item.ItemID, StatusMode.Refine,
            item.RefineryKind, item.RefineryLevel, item.RefineryPower,
            (uint)Math.Max(0, Math.Ceiling(((item.RefineryExpiresAt ?? now) - now).TotalSeconds)));

        // MsgQuench: the gear at TargetID, from the inventory or worn; the refinery item from
        // the inventory
        public static async Task RefineAsync(Client client, uint targetID, uint refineryID, DateTime now)
        {
            var character = client.Character;
            if (character == null)
                return;
            var refinery = client.Inventory?.Find(refineryID);
            var gear = Find(client, targetID);
            if (refinery == null)
            {
                await RejectAsync(client, targetID, "no_refinery_item");
                return;
            }
            if (gear == null || gear == refinery)
            {
                await RejectAsync(client, targetID, "not_found");
                return;
            }
            if (!Kernel.Refineries.TryGet(refinery.TypeID, out var refinement) ||
                !Kernel.ItemTypes.TryGet(refinery.TypeID, out var refineryType))
            {
                await RejectAsync(client, targetID, "not_refinery");
                return;
            }
            if (!Fits(refineryType, gear.TypeID))
            {
                await RejectAsync(client, targetID, "wrong_gear");
                return;
            }

            await ItemActions.SpendOneAsync(client, refinery);

            Apply(gear, refinement, now, character.VIPLevel);
            await ItemsRepository.SetRefineryAsync(gear);
            await client.SendAsync(new MsgItemStatus(TemporaryEntry(gear, now)));
            if (gear.Position != (byte)ItemPosition.Inventory)
                await Equipping.FollowMaximumsAsync(client);
            LogEvents.For(typeof(Refinery), LogEvents.ItemRefined).Information(
                "{Name} refined item {ItemId} (type {TypeId}) with {RefineryType}: {Kind} level {Level}, " +
                "power {Power}, until {ExpiresAt}", character.Name, gear.ItemID, gear.TypeID, refinery.TypeID,
                refinement.Kind, refinement.Level, refinement.Power, gear.RefineryExpiresAt);
        }

        // MsgSolidify at the Refinery Stabilizer: the gear's temporary state becomes permanent
        // when the stones' points reach what its level needs. Every stone put in is spent.
        public static async Task StabilizeAsync(Client client, uint gearID, uint[] stoneIDs, DateTime now)
        {
            var character = client.Character;
            if (character == null || Npcs.Dialogs.Reach(client, StabilizerID, "stabilize") == null)
                return;
            var gear = Find(client, gearID);
            if (gear == null)
            {
                await RejectAsync(client, gearID, "not_found");
                return;
            }
            if (!HasTemporary(gear, now))
            {
                await RejectAsync(client, gearID, "no_temporary");
                return;
            }
            var stones = stoneIDs.Distinct().Select(id => client.Inventory?.Find(id)).ToList();
            if (stones.Count == 0 || stones.Any(s => s == null || s == gear || Kernel.Refineries.StonePoints(s.TypeID) == 0))
            {
                await RejectAsync(client, gearID, "not_a_stone");
                return;
            }
            var (needed, credit) = Points(gear, Kernel.Refineries);
            uint points = (uint)stones.Sum(s => Kernel.Refineries.StonePoints(s.TypeID));
            if (needed == 0 || credit + points < needed)
            {
                await RejectAsync(client, gearID, "not_enough_points");
                return;
            }

            foreach (var stone in stones)
            {
                await ItemActions.SpendOneAsync(client, stone);
            }
            Stabilize(gear);
            await ItemsRepository.SetRefineryAsync(gear);
            await client.SendAsync(new MsgItemStatus(new Entry(gear.ItemID, StatusMode.Stabilized,
                gear.StableKind, gear.StableLevel, gear.StablePower, 0)));
            LogEvents.For(typeof(Refinery), LogEvents.ItemStabilized).Information(
                "{Name} stabilized item {ItemId}: {Kind} level {Level} for {Points} points of {Needed}",
                character.Name, gear.ItemID, (RefineryKind)gear.StableKind, gear.StableLevel, credit + points, needed);
        }

        // Each second: temporary states that ran out end
        public static async Task ExpireAsync(Client client, DateTime now)
        {
            var items = (client.Inventory?.Items ?? Enumerable.Empty<DbItem>())
                .Concat(client.Equipment?.Items ?? Enumerable.Empty<DbItem>())
                .Where(i => i.RefineryKind != 0 && !(i.RefineryExpiresAt > now)).ToList();
            if (items.Count == 0)
                return;
            bool worn = false;
            var msg = new MsgItemStatus();
            foreach (var item in items)
            {
                var kind = (RefineryKind)item.RefineryKind;
                ClearTemporary(item);
                await ItemsRepository.SetRefineryAsync(item);
                msg.Entries.Add(new Entry(item.ItemID, StatusMode.Expire, 0, 0, 0, 0));
                worn |= item.Position != (byte)ItemPosition.Inventory;
                LogEvents.For(typeof(Refinery), LogEvents.ItemRefineryExpired).Information(
                    "{Name}'s item {ItemId} lost its temporary {Kind}", client.Character?.Name, item.ItemID, kind);
            }
            await client.SendAsync(msg);
            if (worn)
                await Equipping.FollowMaximumsAsync(client);
        }

        public static Task RejectAsync(Client client, uint itemID, string reason)
        {
            LogEvents.For(typeof(Refinery), LogEvents.ItemRefineRejected).Warning(
                "{Name} can't refine item {ItemId}: {Reason}", client.Character?.Name, itemID, reason);
            return Task.CompletedTask;
        }

        private static DbItem Find(Client client, uint itemID) =>
            client.Inventory?.Find(itemID) ?? client.Equipment?.Items.FirstOrDefault(i => i.ItemID == itemID);
    }
}

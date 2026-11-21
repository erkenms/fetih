namespace Comet.Game.Items
{
    using System;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Chat;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Items used from the inventory (MsgItem 4 with slot 0): potions, city scrolls, praying stones,
    // and scrolls that open into ten meteors or dragonballs. Runs on the player's own packet
    // queue, like Progress.
    public static class Using
    {
        // MeteorScroll and DragonBallScroll (Albetros)
        public const uint MeteorScroll = 720027, DragonBallScroll = 720028;
        public const int ScrollGems = 10;

        public static async Task<bool> UseAsync(Client client, uint itemID)
        {
            var character = client.Character;
            var item = client.Inventory?.Find(itemID);
            if (character == null || item == null)
                return Reject(client, itemID, 0, "not_in_inventory");

            if (HeavenBlessing.StoneDuration(item.TypeID) is TimeSpan blessing)
                return await PrayAsync(client, item, blessing);
            if (Kernel.ItemTypes.TryGet(item.TypeID, out var type) && IsPotion(type))
                return await DrinkAsync(client, item, type);
            if (item.TypeID is MeteorScroll or DragonBallScroll)
                return await OpenScrollAsync(client, item,
                    item.TypeID == MeteorScroll ? Upgrading.Meteor : Upgrading.DragonBall);

            var scroll = Kernel.MapTypes.ScrollPointOf(item.TypeID);
            if (scroll == null)
                return Reject(client, itemID, item.TypeID, "not_usable");
            if (client.Life.Dead)
                return Reject(client, itemID, item.TypeID, "dead");
            if ((Kernel.MapTypes.Of(character.MapID) & (MapFlags.TeleportDisable | MapFlags.PrisonMap)) != 0)
            {
                await Talk.NoticeAsync(client, "You can't teleport out of this map.");
                return Reject(client, itemID, item.TypeID, "teleport_disabled");
            }

            uint fromMap = character.MapID;
            if (!await Movement.TeleportAsync(client, scroll.MapID, scroll.X, scroll.Y))
                return Reject(client, itemID, item.TypeID, "bad_destination");
            await UseUpAsync(client, item);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) on map {FromMapId} and went to map {MapId}",
                character.Name, item.ItemID, item.TypeID, fromMap, scroll.MapID);
            return true;
        }

        // Stancher to Mil.Ginseng (1000000 to 1002050): their life and mana fields are the HP and MP they restore
        public static bool IsPotion(ItemType type) => type.ID / 10000 == 100 && (type.Life > 0 || type.Mana > 0);

        // Up to full HP and MP; a potion that would restore nothing is kept
        private static async Task<bool> DrinkAsync(Client client, DbItem potion, ItemType type)
        {
            var character = client.Character;
            if (client.Life.Dead)
                return Reject(client, potion.ItemID, potion.TypeID, "dead");
            ushort health = (ushort)Math.Min(Attributes.MaxHealth(client), character.HealthPoints + type.Life);
            ushort mana = (ushort)Math.Min(Attributes.MaxMana(client), character.ManaPoints + type.Mana);
            int healed = health - character.HealthPoints, restored = mana - character.ManaPoints;
            if (healed <= 0 && restored <= 0)
                return Reject(client, potion.ItemID, potion.TypeID, "full");

            await UseUpAsync(client, potion);
            character.HealthPoints = health;
            character.ManaPoints = mana;
            await new MsgUserAttrib(client.ID, MsgUserAttrib.AttributeType.Health, character.HealthPoints)
                .Append(MsgUserAttrib.AttributeType.Mana, character.ManaPoints)
                .SendEachAsync(client);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) and restored {Health} HP and {Mana} MP",
                character.Name, potion.ItemID, potion.TypeID, healed, restored);
            return true;
        }

        // The scroll's own slot holds one of the gems
        private static async Task<bool> OpenScrollAsync(Client client, DbItem scroll, uint gemType)
        {
            if (client.Inventory.FreeSlots < ScrollGems - 1)
            {
                await Talk.NoticeAsync(client, $"You need {ScrollGems - 1} free inventory slots for that.");
                return Reject(client, scroll.ItemID, scroll.TypeID, "inventory_full");
            }
            if (!Kernel.ItemTypes.TryGet(gemType, out var type))
                return Reject(client, scroll.ItemID, scroll.TypeID, "unknown_gem");

            await UseUpAsync(client, scroll);
            for (int i = 0; i < ScrollGems; i++)
                await ItemActions.GiveAsync(client, type, "scroll");
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for {Amount} of {GemTypeId}",
                client.Character.Name, scroll.ItemID, scroll.TypeID, ScrollGems, gemType);
            return true;
        }

        private static async Task<bool> PrayAsync(Client client, DbItem stone, TimeSpan blessing)
        {
            if (client.Life.Dead)
                return Reject(client, stone.ItemID, stone.TypeID, "dead");
            await UseUpAsync(client, stone);
            LogEvents.For(typeof(Using), LogEvents.ItemUsed).Information(
                "{Name} used item {ItemId} (type {TypeId}) for heaven blessing",
                client.Character.Name, stone.ItemID, stone.TypeID);
            await HeavenBlessing.AddAsync(client, blessing, "praying_stone");
            return true;
        }

        private static async Task UseUpAsync(Client client, DbItem item)
        {
            client.Inventory.Remove(item);
            await ItemsRepository.DeleteAsync(item.ItemID);
            await client.SendAsync(MsgItem.Removed(item.ItemID));
        }

        private static bool Reject(Client client, uint itemID, uint typeID, string reason)
        {
            LogEvents.For(typeof(Using), LogEvents.ItemUseRejected).Warning(
                "{Name} can't use item {ItemId} (type {TypeId}): {Reason}",
                client.Character?.Name, itemID, typeID, reason);
            return false;
        }
    }
}

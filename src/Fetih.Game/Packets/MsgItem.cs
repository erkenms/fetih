namespace Fetih.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Items;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    public sealed class MsgItem : MsgBase<Client>
    {
        public const int Size = 88;

        public uint CharacterID { get; set; }
        public uint Command { get; set; }
        public uint Timestamp { get; set; }
        public uint Argument { get; set; }
        public ItemActionType Action { get; set; }

        // Item IDs worn, for DisplayGears: slots 1 to 9, then the right and left accessories,
        // the steed armor and the crop. The steed isn't in it.
        public uint[] Gears { get; set; }

        private const int GearCount = 13;

        private static int GearIndex(byte position) => position switch
        {
            >= (byte)ItemPosition.Headwear and <= (byte)ItemPosition.Garment => position - 1,
            (byte)ItemPosition.Crop => 12,
            _ => -1,
        };

        public static MsgItem Equipped(uint itemID, ItemPosition position) =>
            Create(itemID, (uint)position, ItemActionType.EquipmentWear);

        public static MsgItem Removed(uint itemID) =>
            Create(itemID, 0, ItemActionType.InventoryRemove);

        // Tells a booth's owner that an item left the booth; booth ID in Command
        public static MsgItem BoothUnlisted(uint itemID, uint boothID) =>
            Create(itemID, boothID, ItemActionType.BoothRemove);

        public static MsgItem Unequipped(uint itemID, ItemPosition position) =>
            Create(itemID, (uint)position, ItemActionType.EquipmentRemove);

        public static MsgItem Fireworks(uint characterID) =>
            Create(characterID, 0, ItemActionType.Fireworks);

        // What the player wears, by item ID. The client draws its own character from this,
        // not from MsgPlayer or the equip replies.
        public static MsgItem DisplayGears(uint characterID, Equipment equipment)
        {
            var msg = Create(characterID, 255, ItemActionType.DisplayGears);
            msg.Gears = new uint[GearCount];
            foreach (var item in equipment?.Items ?? Enumerable.Empty<Database.Models.DbItem>())
                if (GearIndex(item.Position) is var index and >= 0)
                    msg.Gears[index] = item.ItemID;
            return msg;
        }

        private static MsgItem Create(uint id, uint command, ItemActionType action) => new MsgItem
        {
            Type = PacketType.MsgItem,
            CharacterID = id,
            Command = command,
            Action = action,
            Timestamp = (uint)Environment.TickCount,
        };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.Command = reader.ReadUInt32();
            this.Action = (ItemActionType)reader.ReadUInt32();
            this.Timestamp = reader.ReadUInt32();
            this.Argument = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.CharacterID);
            writer.Write(this.Command);
            writer.Write((uint)this.Action);
            writer.Write(this.Timestamp);
            writer.Write(this.Argument);
            writer.Write(new byte[8]);                      // 24
            for (int i = 0; i < GearCount; i++)             // 32 gears; the crop at 80
                writer.Write(this.Gears?[i] ?? 0u);
            writer.Write(new byte[MsgItem.Size - 32 - 4 * GearCount]);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (Handlers.TryGetValue(this.Action, out var handler))
            {
                await handler(this, client);
                return;
            }

            await client.SendAsync(this);
            await client.SendAsync(new MsgTalk(client.ID, MsgTalk.TalkChannel.Service,
                String.Format("Missing packet {0}, Action {1}, Length {2}",
                this.Type, this.Action, this.Length)));
            LogEvents.For(this.GetType(), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                this.Type, this.Action, this.Length, Convert.ToHexString(this.Encode()));
        }

        private static bool IsUse(Client client, MsgItem msg) =>
            msg.Command == (uint)ItemPosition.Inventory ||
            client.Inventory?.Find(msg.CharacterID) is { } item && !ItemSlots.IsWearable(item.TypeID);

        private static readonly Dictionary<ItemActionType, Func<MsgItem, Client, Task>> Handlers =
            new Dictionary<ItemActionType, Func<MsgItem, Client, Task>>
            {
                // The client checks the connection with pings; the reply is the echo
                [ItemActionType.ClientPing] = (msg, client) => client.SendAsync(msg),

                // The item ID, and the slot in Command; slot 0 uses the item. The 5619 client
                // also sends a slot for items that can't be worn (17 for praying stones).
                [ItemActionType.InventoryEquip] = (msg, client) => IsUse(client, msg)
                    ? Using.UseAsync(client, msg.CharacterID)
                    : Equipping.EquipAsync(client, msg.CharacterID, (ItemPosition)msg.Command),
                [ItemActionType.EquipmentRemove] = (msg, client) =>
                    Equipping.UnequipAsync(client, msg.CharacterID, (ItemPosition)msg.Command),

                // The item ID, or the amount of silver, in the ID field
                [ItemActionType.InventoryDrop] = (msg, client) => Ground.DropItemAsync(client, msg.CharacterID),
                [ItemActionType.DropSilver] = (msg, client) => Ground.DropSilverAsync(client, msg.CharacterID),

                // The shopkeeper's NPC ID, then the item type and amount to buy, or the item to sell
                [ItemActionType.ShopPurchase] = (msg, client) =>
                    Shopping.BuyAsync(client, msg.CharacterID, msg.Command, msg.Argument),
                [ItemActionType.ShopSell] = (msg, client) => Shopping.SellAsync(client, msg.CharacterID, msg.Command),

                // The split window: the stack in the ID field, how many leave it in Command
                [ItemActionType.InventorySplit] = (msg, client) => Stacking.SplitAsync(client, msg.CharacterID, msg.Command),

                // The item to repair, at a shopkeeper
                [ItemActionType.EquipmentRepair] = (msg, client) => Durability.RepairAsync(client, msg.CharacterID),
                [ItemActionType.EquipmentRepairAll] = (msg, client) => Durability.RepairAllAsync(client),

                // Ethereal's enchant window: the gear in the ID field, the gem in Command
                [ItemActionType.EquipmentEnchant] = (msg, client) => Enchanting.EnchantAsync(client, msg, Random.Shared),
                [ItemActionType.EquipmentDegrade] = (msg, client) => Upgrading.DegradeAsync(client, msg.CharacterID),

                // Warehouse silver; the amount in Command
                [ItemActionType.BankQuery] = (msg, client) => Storing.ShowSilverAsync(client, msg),
                [ItemActionType.BankDeposit] = (msg, client) => Storing.MoveSilverAsync(client, msg.Command),
                [ItemActionType.BankWithdraw] = (msg, client) => Storing.MoveSilverAsync(client, -(long)msg.Command),

                // Booths: the item ID, then its price, or the booth's ID when buying. Viewing a
                // booth has the booth's ID in the ID field.
                [ItemActionType.BoothSell] = (msg, client) => Vending.ListAsync(client, msg, conquerPoints: false),
                [ItemActionType.BoothSellPoints] = (msg, client) => Vending.ListAsync(client, msg, conquerPoints: true),
                [ItemActionType.BoothRemove] = (msg, client) => Vending.UnlistAsync(client, msg),
                [ItemActionType.BoothQuery] = (msg, client) => Vending.ShowAsync(client, msg.CharacterID),
                [ItemActionType.BoothPurchase] = (msg, client) => Vending.BuyAsync(client, msg),

                // Detained equipment: redeem for CPs (32) or claim ransom/expired gear (33), detain ID in CharacterID
                [ItemActionType.RedeemEquipment] = (msg, client) => Pk.Detaining.RedeemAsync(client, msg.CharacterID),
                [ItemActionType.ClaimDetained] = (msg, client) => Pk.Detaining.ClaimAsync(client, msg.CharacterID),
            };

        public static bool Handles(ItemActionType action) => Handlers.ContainsKey(action);

        // The 5619 client never sends 7, 8 or 25: splitting is 49, and it has no merge (new
        // items go on stacks with room)
        public enum ItemActionType
        {
            ShopPurchase = 1,
            ShopSell,
            InventoryRemove,
            InventoryEquip,
            EquipmentWear,
            EquipmentRemove,
            EquipmentSplit,
            EquipmentCombine,
            BankQuery,
            BankDeposit,
            BankWithdraw,
            InventoryDropSilver,
            EquipmentRepair = 14,
            EquipmentRepairAll,
            EquipmentImprove = 19,
            EquipmentLevelUp,
            BoothQuery,
            BoothSell,
            BoothRemove,
            BoothPurchase,
            EquipmentAmount,
            Fireworks = 26,
            ClientPing = 27,
            EquipmentEnchant,
            BoothSellPoints,
            RedeemEquipment = 32,
            ClaimDetained = 33,
            InventoryDrop = 37,
            DropSilver = 38,
            DisplayGears = 46,
            InventorySplit = 49,
            EquipmentDegrade = 54,
        }
    }
}
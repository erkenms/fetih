namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    public sealed class MsgItem : MsgBase<Client>
    {
        public const int Size = 88;

        public uint CharacterID { get; set; }
        public uint Command { get; set; }
        public uint Timestamp { get; set; }
        public uint Argument { get; set; }
        public ItemActionType Action { get; set; }

        // Item IDs worn in slots 1 to 9, for DisplayGears
        public uint[] Gears { get; set; }

        // The server's answers to equipping: the item now in a slot, or back in the inventory
        public static MsgItem Equipped(uint itemID, ItemPosition position) =>
            Create(itemID, (uint)position, ItemActionType.EquipmentWear);

        public static MsgItem Removed(uint itemID) =>
            Create(itemID, 0, ItemActionType.InventoryRemove);

        public static MsgItem Unequipped(uint itemID, ItemPosition position) =>
            Create(itemID, (uint)position, ItemActionType.EquipmentRemove);

        // What the player wears, by item ID. The client draws its own character from this,
        // not from MsgPlayer or the equip replies.
        public static MsgItem DisplayGears(uint characterID, Equipment equipment)
        {
            var msg = Create(characterID, 255, ItemActionType.DisplayGears);
            msg.Gears = new uint[9];
            foreach (var item in equipment?.Items ?? Enumerable.Empty<Database.Models.DbItem>())
                msg.Gears[item.Position - 1] = item.ItemID;
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
            for (int i = 0; i < 9; i++)                     // 32 item IDs in slots 1 to 9
                writer.Write(this.Gears?[i] ?? 0u);
            writer.Write(new byte[MsgItem.Size - 68]);
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

        private static readonly Dictionary<ItemActionType, Func<MsgItem, Client, Task>> Handlers =
            new Dictionary<ItemActionType, Func<MsgItem, Client, Task>>
            {
                // The client checks the connection with pings; the reply is the echo
                [ItemActionType.ClientPing] = (msg, client) => client.SendAsync(msg),

                // The item ID, and the slot in Command
                [ItemActionType.InventoryEquip] = (msg, client) =>
                    Equipping.EquipAsync(client, msg.CharacterID, (ItemPosition)msg.Command),
                [ItemActionType.EquipmentRemove] = (msg, client) =>
                    Equipping.UnequipAsync(client, msg.CharacterID, (ItemPosition)msg.Command),

                // The item ID, or the amount of silver, in the ID field
                [ItemActionType.InventoryDrop] = (msg, client) => Ground.DropItemAsync(client, msg.CharacterID),
                [ItemActionType.DropSilver] = (msg, client) => Ground.DropSilverAsync(client, msg.CharacterID),

                // The shopkeeper's NPC ID, then the item type and amount to buy, or the item to sell
                [ItemActionType.ShopPurchase] = (msg, client) =>
                    Shopping.BuyAsync(client, msg.CharacterID, msg.Command, msg.Argument),
                [ItemActionType.ShopSell] = (msg, client) => Shopping.SellAsync(client, msg.CharacterID, msg.Command),

                // Warehouse silver; the amount in Command
                [ItemActionType.BankQuery] = (msg, client) => Storing.ShowSilverAsync(client, msg),
                [ItemActionType.BankDeposit] = (msg, client) => Storing.MoveSilverAsync(client, msg.Command),
                [ItemActionType.BankWithdraw] = (msg, client) => Storing.MoveSilverAsync(client, -(long)msg.Command),
            };

        public static bool Handles(ItemActionType action) => Handlers.ContainsKey(action);

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
            ClientPing = 27,
            EquipmentEnchant,
            BoothSellPoints,
            InventoryDrop = 37,
            DropSilver = 38,
            DisplayGears = 46,
        }
    }
}
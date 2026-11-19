namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
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
            writer.Write(new byte[MsgItem.Size - 24]);
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
            BoothSellPoints
        }
    }
}
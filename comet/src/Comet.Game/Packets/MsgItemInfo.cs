namespace Comet.Game.Packets
{
    using System;
    using Comet.Game.Database.Models;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // One item's full state, sent when the player gets or sees it
    public sealed class MsgItemInfo : MsgBase<Client>
    {
        public const int Size = 84;

        public DbItem Item { get; }
        public ItemAction Action { get; }

        public MsgItemInfo(DbItem item, ItemAction action)
        {
            base.Type = PacketType.MsgItemInfo;
            this.Item = item;
            this.Action = action;
        }

        public override byte[] Encode()
        {
            var body = new byte[Size];
            var span = body.AsSpan();
            BitConverter.TryWriteBytes(span[4..], this.Item.ItemID);
            BitConverter.TryWriteBytes(span[8..], this.Item.TypeID);
            BitConverter.TryWriteBytes(span[12..], this.Item.Amount);
            BitConverter.TryWriteBytes(span[14..], this.Item.AmountLimit);
            BitConverter.TryWriteBytes(span[16..], (ushort)this.Action);
            BitConverter.TryWriteBytes(span[18..], (ushort)this.Item.Position);
            // 20: socket progress (uint)
            body[24] = this.Item.Gem1;
            body[25] = this.Item.Gem2;
            // 28: effect (ushort)
            body[33] = this.Item.Plus;
            body[34] = this.Item.Bless;
            // 35: bound
            body[36] = this.Item.Enchant;
            // 44: suspicious
            body[46] = (byte)(this.Item.Locked ? 1 : 0);
            BitConverter.TryWriteBytes(span[48..], (uint)this.Item.Color);
            BitConverter.TryWriteBytes(span[52..], this.Item.PlusProgress);
            BitConverter.TryWriteBytes(span[56..], Social.Guilds.ArsenalSyndicateOf(this.Item.ItemID));
            // 60: time left (uint)
            BitConverter.TryWriteBytes(span[64..], this.Item.StackAmount);

            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(body, 4, Size - 4);
            return writer.ToArray();
        }

        public enum ItemAction : ushort
        {
            Add = 1,
            Trade = 2,
            Update = 3,
            OtherPlayerEquipment = 4,
        }
    }
}

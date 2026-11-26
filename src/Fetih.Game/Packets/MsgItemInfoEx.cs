namespace Fetih.Game.Packets
{
    using System;
    using Fetih.Game.Database.Models;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // An item on sale in a booth, as a buyer sees it, or one worn by another player. 84 bytes.
    public sealed class MsgItemInfoEx : MsgBase<Client>
    {
        public const int Size = 84;

        public DbItem Item { get; }
        public uint OwnerID { get; }
        public uint Price { get; }
        public ViewType View { get; }

        public MsgItemInfoEx(DbItem item, uint ownerID, uint price, ViewType view)
        {
            base.Type = PacketType.MsgItemInfoEx;
            this.Item = item;
            this.OwnerID = ownerID;
            this.Price = price;
            this.View = view;
        }

        public override byte[] Encode()
        {
            var body = new byte[Size];
            var span = body.AsSpan();
            BitConverter.TryWriteBytes(span[4..], this.Item.ItemID);
            BitConverter.TryWriteBytes(span[8..], this.OwnerID);
            BitConverter.TryWriteBytes(span[12..], this.Price);
            BitConverter.TryWriteBytes(span[16..], this.Item.TypeID);
            BitConverter.TryWriteBytes(span[20..], this.Item.Amount);
            BitConverter.TryWriteBytes(span[22..], this.Item.AmountLimit);
            BitConverter.TryWriteBytes(span[24..], (ushort)this.View);
            BitConverter.TryWriteBytes(span[26..], (ushort)this.Item.Position);
            body[32] = this.Item.Gem1;
            body[33] = this.Item.Gem2;
            body[41] = this.Item.Plus;
            body[42] = this.Item.Bless;
            // 43: bound
            body[44] = this.Item.Enchant;
            // 53: suspicious
            body[56] = this.Item.Color;
            BitConverter.TryWriteBytes(span[72..], this.Item.StackAmount);

            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(body, 4, Size - 4);
            return writer.ToArray();
        }

        public enum ViewType : ushort
        {
            Silver = 1,
            ConquerPoints = 3,
            Equipment = 4,
        }
    }
}

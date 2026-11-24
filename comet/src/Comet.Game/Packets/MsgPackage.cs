namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Warehouse contents: NPC ID @4, action byte @8, kind byte
    // @9, capacity u32 @12, item ID @16, item count byte @20, then 52 bytes per item: ID @0,
    // type @4, gems @9/10, plus @17, bless @18, enchant @20, locked @26, color @27, stack amount
    // u16 @44.
    public sealed class MsgPackage : MsgBase<Client>
    {
        public const int ItemSize = 52;

        public const int ItemsPerPacket = 10;

        public uint NpcID { get; set; }
        public PackageAction Action { get; set; }
        public byte Kind { get; set; } = Storage;
        public uint ItemID { get; set; }
        public IReadOnlyList<DbItem> Items { get; set; } = Array.Empty<DbItem>();

        public const byte Storage = 10;

        public MsgPackage() => base.Type = PacketType.MsgPackage;

        public MsgPackage(uint npcID, PackageAction action, uint itemID = 0) : this()
        {
            this.NpcID = npcID;
            this.Action = action;
            this.ItemID = itemID;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.NpcID = reader.ReadUInt32();
            this.Action = (PackageAction)reader.ReadByte();
            this.Kind = reader.ReadByte();
            reader.ReadUInt16();
            reader.ReadUInt32();
            this.ItemID = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var body = new byte[24 + ItemSize * this.Items.Count];
            var span = body.AsSpan();
            BitConverter.TryWriteBytes(span[4..], this.NpcID);
            body[8] = (byte)this.Action;
            body[9] = this.Kind;
            BitConverter.TryWriteBytes(span[12..], (uint)Warehouses.Capacity);
            BitConverter.TryWriteBytes(span[16..], this.ItemID);
            body[20] = (byte)this.Items.Count;
            for (int i = 0; i < this.Items.Count; i++)
            {
                var item = this.Items[i];
                var entry = span[(24 + ItemSize * i)..];
                BitConverter.TryWriteBytes(entry, item.ItemID);
                BitConverter.TryWriteBytes(entry[4..], item.TypeID);
                entry[9] = item.Gem1;
                entry[10] = item.Gem2;
                entry[17] = item.Plus;
                entry[18] = item.Bless;
                entry[20] = item.Enchant;
                entry[26] = (byte)(item.Locked ? 1 : 0);
                entry[27] = item.Color;
                BitConverter.TryWriteBytes(entry[44..], item.StackAmount);
            }

            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(body, 4, body.Length - 4);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Action switch
        {
            PackageAction.Query => Storing.ShowAsync(client, this.NpcID),
            PackageAction.Deposit => Storing.DepositAsync(client, this.NpcID, this.ItemID),
            PackageAction.Withdraw => Storing.WithdrawAsync(client, this.NpcID, this.ItemID),
            _ => this.UnhandledAsync(),
        };

        private Task UnhandledAsync()
        {
            LogEvents.For<MsgPackage>(LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes)", this.Type, this.Action, this.Length);
            return Task.CompletedTask;
        }

        public enum PackageAction : byte
        {
            Query = 0,
            Deposit = 1,
            Withdraw = 2,
        }
    }
}

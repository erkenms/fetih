namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // 16 bytes: the request at 4, the password at 8 and the new one at 12. To
    // the client, Correct carries flags at 8 instead: HasPassword, and Verified once given.
    public sealed class Msg2ndPsw : MsgBase<Client>
    {
        public const uint HasPassword = 0x1, Verified = 0x100;

        public PasswordRequest Request { get; set; }
        public uint Password { get; set; }
        public uint NewPassword { get; set; }

        public Msg2ndPsw() => base.Type = PacketType.Msg2ndPsw;

        public Msg2ndPsw(PasswordRequest request, uint password = 0) : this()
        {
            this.Request = request;
            this.Password = password;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Request = (PasswordRequest)reader.ReadUInt32();
            this.Password = reader.ReadUInt32();
            this.NewPassword = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Request);       // 4
            writer.Write(this.Password);            // 8
            writer.Write(this.NewPassword);         // 12
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => SecondPassword.HandleAsync(client, this);

        public enum PasswordRequest : uint
        {
            Verify = 1,
            Forgot = 2,
            Set = 3,
            Correct = 5,
            Wrong = 6,
        }
    }
}

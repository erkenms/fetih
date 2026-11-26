namespace Fetih.Account.Packets
{
    using Fetih.Account.States;
    using Fetih.Network.Packets;

    public sealed class MsgConnectEx : MsgBase<Client>
    {
        public ulong Token { get; set; }
        public RejectionCode Code { get; set; }
        public string IPAddress { get; set; }
        public uint Port { get; set; }

        public MsgConnectEx(RejectionCode rejectionCode)
        {
            this.Code = rejectionCode;
        }

        public MsgConnectEx(string ipAddress, uint port, ulong token)
        {
            this.Token = token;
            this.IPAddress = ipAddress;
            this.Port = port;
        }

        // The client treats a zero token low half as a rejection. It pings both addresses and
        // connects to the faster one, so both point at the same server.
        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgConnectEx);
            if (this.Code != RejectionCode.Clear)
            {
                writer.Write((uint)0);
                writer.Write((uint)this.Code);
            }
            else
            {
                writer.Write(this.Token);               // 4
                writer.Write(this.Port);                // 12
                writer.Write((uint)0);                  // 16
                writer.Write(this.IPAddress, 32);       // 20
                writer.Write(this.Port);                // 52
                writer.Write(this.IPAddress, 32);       // 56
            }
            
            return writer.ToArray();
        }

        public enum RejectionCode : uint
        {
            Clear = 0,
            InvalidPassword = 1,
            ServerDown = 10,
            AccountBanned = 12,
            ServerBusy = 20,
            AccountLocked = 22,
            AccountNotActivated = 30,
            AccountActivationFailed = 31,
            ServerTimedOut = 42,
            AccountMaxLoginAttempts = 51,
            ServerLocked = 70,
            ServerOldProtocol = 73
        }
    }
}

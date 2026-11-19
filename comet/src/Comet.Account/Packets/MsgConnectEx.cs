namespace Comet.Account.Packets
{
    using Comet.Account.States;
    using Comet.Network.Packets;

    /// <remarks>Packet Type 1055</remarks>
    /// <summary>
    /// Message containing an authentication response from the server to the client. Can
    /// either accept a client with realm connection details and an access token, or 
    /// reject a client with a reason on why the login attempt was rejected.
    /// </summary>
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

        /// <remarks>
        /// 5619: confirmed in VM. Accept (88 bytes): the token's low half at 4 (the
        /// client treats zero as a rejection) and high half at 8, which the client returns
        /// in MsgConnect; then two game server addresses, each a port at 12 or 52 and an
        /// IP address text at 20 or 56. The client pings both and connects to the faster,
        /// so both hold the same server. Reject: zero at 4 and the code at 8.
        /// </remarks>
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

        /// <summary>
        /// Rejection codes are sent to the client in offset 8 of this packet when the
        /// client has failed authentication with the account server. These codes define
        /// which error message will be displayed in the client.
        /// </summary>
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

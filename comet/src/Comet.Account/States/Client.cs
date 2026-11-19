namespace Comet.Account.States
{
    using System;
    using System.Net.Sockets;
    using Comet.Account.Database.Models;
    using Comet.Network.Security;
    using Comet.Network.Sockets;

    public sealed class Client : TcpServerActor
    {
        public DbAccount Account;

        // SRP6 login in progress (set by MsgAccountSRP6, checked by MsgLoginProofC)
        public Srp6 Srp;
        public byte[] UsernameBytes;
        public string Realm;

        public Client(Socket socket, Memory<byte> buffer, uint partition) 
            : base(socket, buffer, new TQCipher(), partition)
        {
            
        }
    }
}

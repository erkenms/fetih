namespace Fetih.Account.States
{
    using System;
    using System.Net.Sockets;
    using Fetih.Account.Database.Models;
    using Fetih.Network.Security;
    using Fetih.Network.Sockets;

    public sealed class Client : TcpServerActor
    {
        public DbAccount Account;

        public Srp6 Srp;
        public byte[] UsernameBytes;
        public string Realm;

        public Client(Socket socket, Memory<byte> buffer, uint partition) 
            : base(socket, buffer, new TQCipher(), partition)
        {
        }
    }
}

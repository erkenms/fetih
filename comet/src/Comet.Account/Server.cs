namespace Comet.Account
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Account.Database;
    using Comet.Account.Packets;
    using Comet.Account.States;
    using Comet.Core.Logging;
    using Comet.Network.Packets;
    using Comet.Network.Sockets;

    internal sealed class Server : TcpServerListener<Client>
    {
        private readonly PacketProcessor<Client> Processor;
        private readonly Task ProcessorTask;

        public Server(ServerConfiguration config)
            : base(maxConn: config.Network.MaxConn,
                maxConnPerIP: config.Network.MaxConnPerIP,
                maxAttemptsPerMinute: config.Network.MaxAttemptsPerMinute,
                loginTimeoutSeconds: config.Network.LoginTimeoutSeconds)
        {
            this.Processor = new PacketProcessor<Client>(this.ProcessAsync);
            this.ProcessorTask = this.Processor.StartAsync(CancellationToken.None);
        }

        protected override async Task<Client> AcceptedAsync(Socket socket, Memory<byte> buffer)
        {
            uint partition = this.Processor.SelectPartition();
            var client = new Client(socket, buffer, partition);
            var seed = (uint)(await Kernel.NextAsync(10000, int.MaxValue));
            await client.SendAsync(new MsgEncryptCode(seed));
            return client;
        }

        protected override void Received(Client actor, ReadOnlySpan<byte> packet)
        {
            this.Processor.Queue(actor, packet.ToArray());
        }

        private async Task ProcessAsync(Client actor, byte[] packet) 
        {
            if (!actor.Socket.Connected)
                return;

            var length = BitConverter.ToUInt16(packet, 0);
            var type = (PacketType)BitConverter.ToUInt16(packet, 2);

            try
            {
                MsgBase<Client> msg = PacketHandlers.Create(type);
                if (msg == null)
                {
                    LogEvents.For<Server>(LogEvents.PacketUnhandled).Warning(
                        "Unhandled packet {PacketType} ({Length} bytes) from connection {ConnId}: {Hex}",
                        type, length, actor.ConnId, Convert.ToHexString(packet));
                    return;
                }

                msg.Decode(packet);
                await msg.ProcessAsync(actor);
            }
            catch (Exception e)
            {
                LogEvents.For<Server>(LogEvents.PacketError).Error(e,
                    "Failed to process packet {PacketType} from connection {ConnId}",
                    type, actor.ConnId);
            }
        }
    }
}

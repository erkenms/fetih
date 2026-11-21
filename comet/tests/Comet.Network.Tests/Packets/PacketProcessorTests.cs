namespace Comet.Network.Packets.Tests
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Comet.Network.Sockets;
    using Xunit;

    public class PacketProcessorTests
    {
        private sealed class Actor : TcpServerActor
        {
            public Actor(Socket socket) : base(socket, Memory<byte>.Empty, null) { }
        }

        [Fact]
        public async Task QueueKeepsRunningAfterAThrow()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(listener.LocalEndPoint);
            using var server = await listener.AcceptAsync();
            var actor = new Actor(server);

            var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processor = new PacketProcessor<Actor>((_, packet) =>
            {
                if (packet[0] == 0)
                    throw new InvalidOperationException("first");
                processed.SetResult();
                return Task.CompletedTask;
            }, count: 1);
            await processor.StartAsync(CancellationToken.None);

            processor.Queue(actor, () => throw new InvalidOperationException("work"));
            processor.Queue(actor, new byte[] { 0 });
            processor.Queue(actor, new byte[] { 1 });

            await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

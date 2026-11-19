namespace Comet.Network.RPC
{
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using StreamJsonRpc;

    public class RpcServerListener
    {
        protected TcpListener BaseListener;
        protected CancellationTokenSource ShutdownToken;
        private IRpcServerTarget Target;

        public RpcServerListener(IRpcServerTarget target)
        {
            this.Target = target;
        }

        public Task StartAsync(int port, string address = "0.0.0.0")
        {
            this.ShutdownToken = new CancellationTokenSource();
            this.BaseListener = new TcpListener(IPAddress.Parse(address), port);
            this.BaseListener.Start();
            return this.AcceptingAsync();
        }

        private async Task AcceptingAsync()
        {
            while (this.BaseListener.Server.IsBound && !this.ShutdownToken.IsCancellationRequested)
            {
                var socket = await this.BaseListener.AcceptSocketAsync();
                var task = Task.Run(() => this.ReceivingAsync(socket));
            }
        }

        private async Task ReceivingAsync(Socket socket)
        {            
            using var stream = new NetworkStream(socket, true);
            Stream input = new BufferedStream(stream); 
            Stream output = new BufferedStream(stream);

            var rpc = JsonRpc.Attach(output, input, this.Target);
            await rpc.Completion;
        }
    }
}

namespace Fetih.Network.RPC
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using StreamJsonRpc;

    public class RpcClient
    {
        protected TcpClient BaseClient;
        protected JsonRpc Rpc;

        public async Task ConnectAsync(string address, int port, string agent = "Client")
        {
            while (true)
            {
                try
                {
                    this.BaseClient = new TcpClient();
                    await this.BaseClient.ConnectAsync(address, port);
                    using var stream = new NetworkStream(this.BaseClient.Client, true);
                    
                    this.Rpc = JsonRpc.Attach(stream, stream);
                    await this.Rpc.InvokeAsync("Connected", agent);
                    await this.Rpc.Completion;
                }
                catch (IOException) { }
                catch (SocketException) { }
                catch (Exception e) { Serilog.Log.ForContext<RpcClient>().Error(e, "RPC connection to {Address}:{Port} failed", address, port); }
                Thread.Sleep(1000);
            }
        }

        public bool Online => this.BaseClient.Connected;

        public Task<T> CallAsync<T>(string method, object arg)
        {
            return this.Rpc.InvokeAsync<T>(method, arg);
        }

        public Task<T> CallAsync<T>(string method, params object[] args)
        {
            return this.Rpc.InvokeAsync<T>(method, args);
        }
    }
}

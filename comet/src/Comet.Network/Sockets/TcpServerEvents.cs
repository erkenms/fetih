namespace Comet.Network.Sockets
{
    using System;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Network.Packets;

    public abstract class TcpServerEvents<TActor> 
        where TActor : TcpServerActor
    {
        protected abstract Task<TActor> AcceptedAsync(Socket socket, Memory<byte> buffer);

        protected virtual bool Exchanged(TActor actor, ReadOnlySpan<byte> buffer)
        {
            throw new NotImplementedException();
        }

        protected virtual void Received(TActor actor, ReadOnlySpan<byte> packet)
        {
            Serilog.Log.ForContext(this.GetType()).Verbose(
                "Received {Length} bytes: {Hex}", packet.Length, Convert.ToHexString(packet));
        }

        protected virtual void Disconnected(TActor actor) 
        {
        }
    }
}

namespace Fetih.Network.Sockets
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Fetih.Network.Packets;
    using Fetih.Network.Security;
    using Serilog;

    public abstract class TcpServerActor
    {
        public readonly Memory<byte> Buffer;
        public readonly ICipher Cipher;
        public readonly Socket Socket;
        public readonly string IPAddress;
        public readonly byte[] PacketFooter;
        public readonly uint Partition;
        private readonly object SendLock;

        // 0 until the connection logs in
        public uint AccountID { get; set; }

        private static long NextConnId;
        public readonly long ConnId;
        public readonly DateTime ConnectedAt;
        private string disconnectReason;

        // The first reason set wins, so a reason like "replaced" isn't overwritten by the socket
        // error it causes.
        public string DisconnectReason => this.disconnectReason;

        public TcpServerActor(
            Socket socket, 
            Memory<byte> buffer, 
            ICipher cipher, 
            uint partition = 0,
            string packetFooter = "")
        {
            this.Buffer = buffer;
            this.Cipher = cipher;
            this.Socket = socket;
            this.IPAddress = (socket.RemoteEndPoint as IPEndPoint).Address.MapToIPv4().ToString();
            this.PacketFooter = Encoding.ASCII.GetBytes(packetFooter);
            this.Partition = partition;
            this.SendLock = new object();
            this.ConnId = Interlocked.Increment(ref NextConnId);
            this.ConnectedAt = DateTime.UtcNow;
        }

        public CancellationTokenSource LoginDeadline { get; } = new CancellationTokenSource();

        public void LoggedIn()
        {
            try { this.LoginDeadline.CancelAfter(Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }

        public void SetDisconnectReason(string reason) =>
            Interlocked.CompareExchange(ref this.disconnectReason, reason, null);

        public virtual Task SendAsync(byte[] packet) => this.SendAsync(packet, null);

        private Task SendAsync(byte[] packet, IPacket source)
        {
            var encrypted = new byte[packet.Length + this.PacketFooter.Length];
            packet.CopyTo(encrypted, 0);

            BitConverter.TryWriteBytes(encrypted, (ushort)packet.Length);
            Array.Copy(this.PacketFooter, 0, encrypted, packet.Length, this.PacketFooter.Length);
            PacketTrace.Out(this, encrypted.AsSpan(0, packet.Length), source);

            lock (SendLock)
            {
                try 
                {
                    this.Cipher.Encrypt(encrypted, encrypted);
                    this.Socket?.Send(encrypted, SocketFlags.None);
                    return Task.CompletedTask;
                }
                catch (SocketException e) when (e.SocketErrorCode is
                    SocketError.ConnectionAborted or
                    SocketError.ConnectionReset or
                    SocketError.NotConnected or
                    SocketError.Shutdown)
                {
                    Log.ForContext<TcpServerActor>().Debug(
                        "Dropped {Length} byte send to closed connection {ConnId} ({SocketError})",
                        packet.Length, this.ConnId, e.SocketErrorCode);
                    return Task.CompletedTask;
                }
                catch (SocketException e)
                {
                    Log.ForContext<TcpServerActor>().Error(e,
                        "Send failed for connection {ConnId}", this.ConnId);
                    return Task.FromException(e);
                }
                catch (ObjectDisposedException)
                {
                    Log.ForContext<TcpServerActor>().Debug(
                        "Dropped {Length} byte send to closed connection {ConnId}",
                        packet.Length, this.ConnId);
                    return Task.CompletedTask;
                }
            }
        }

        public virtual async Task SendAsync(IPacket packet)
        {
            await this.SendAsync(packet.Encode(), packet);
        }

        public virtual void Disconnect(string reason = DisconnectReasons.Server)
        {
            this.SetDisconnectReason(reason);
            try { this.Socket?.Disconnect(false); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }    
}

namespace Fetih.TestClient
{
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Fetih.Network.Security;

    public class Connection : IAsyncDisposable
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        private readonly TcpClient Tcp;
        private readonly NetworkStream Stream;
        private readonly Channel<byte[]> Packets = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource Closing = new CancellationTokenSource();
        private readonly byte[] Footer;
        private Task ReadTask;
        private readonly List<byte[]> Kept = new List<byte[]>();
        protected ICipher Cipher;

        protected Connection(TcpClient tcp, ICipher cipher, string footer)
        {
            this.Tcp = tcp;
            this.Stream = tcp.GetStream();
            this.Cipher = cipher;
            this.Footer = System.Text.Encoding.ASCII.GetBytes(footer);
        }

        protected virtual int FrameLength(byte[] buffer, int count) =>
            count < 2 ? 0 : BitConverter.ToUInt16(buffer, 0) + this.Footer.Length;

        protected static async Task<TcpClient> OpenAsync(string host, int port)
        {
            var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(host, port);
            return tcp;
        }

        protected void StartReading() => this.ReadTask = Task.Run(this.ReadLoopAsync);

        public bool Closed => this.Packets.Reader.Completion.IsCompleted;

        public async Task SendAsync(byte[] packet)
        {
            var wire = new byte[packet.Length + this.Footer.Length];
            packet.CopyTo(wire, 0);
            this.Footer.CopyTo(wire, packet.Length);
            this.Cipher.Encrypt(wire, wire);
            await this.Stream.WriteAsync(wire);
        }

        public async Task SendRawAsync(byte[] bytes)
        {
            var wire = (byte[])bytes.Clone();
            this.Cipher.Encrypt(wire, wire);
            await this.Stream.WriteAsync(wire);
        }

        public async Task<byte[]> ReceiveAsync(TimeSpan? timeout = null)
        {
            using var cancel = new CancellationTokenSource(timeout ?? DefaultTimeout);
            try
            {
                if (await this.Packets.Reader.WaitToReadAsync(cancel.Token))
                    return await this.Packets.Reader.ReadAsync(cancel.Token);
                return null;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("No packet from the server in time");
            }
        }

        public Task<byte[]> ReceiveAsync(ushort type, TimeSpan? timeout = null) =>
            this.ReceiveAsync(packet => BitConverter.ToUInt16(packet, 2) == type, timeout);

        public async Task<byte[]> ReceiveAsync(Func<byte[], bool> match, TimeSpan? timeout = null)
        {
            int kept = this.Kept.FindIndex(p => match(p));
            if (kept >= 0)
            {
                var packet = this.Kept[kept];
                this.Kept.RemoveAt(kept);
                return packet;
            }

            var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
            while (true)
            {
                var left = deadline - DateTime.UtcNow;
                var packet = await this.ReceiveAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero);
                if (packet == null || match(packet))
                    return packet;
                this.Kept.Add(packet);
            }
        }

        public void ForgetKept() => this.Kept.Clear();

        // Every packet already in, without waiting
        public List<byte[]> Drain()
        {
            var packets = new List<byte[]>(this.Kept);
            this.Kept.Clear();
            while (this.Packets.Reader.TryRead(out var packet))
                packets.Add(packet);
            return packets;
        }

        public async Task<bool> WaitClosedAsync(TimeSpan? timeout = null)
        {
            try
            {
                while (await this.ReceiveAsync(timeout) != null) { }
                return true;
            }
            catch (TimeoutException) { return false; }
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            int count = 0;
            try
            {
                while (true)
                {
                    int read = await this.Stream.ReadAsync(
                        buffer.AsMemory(count), this.Closing.Token);
                    if (read == 0)
                        break;

                    // Both ciphers are stream ciphers, so bytes are decrypted as they come
                    this.Cipher.Decrypt(buffer.AsSpan(count, read), buffer.AsSpan(count, read));
                    count += read;

                    int length;
                    while (count > 0 && (length = this.FrameLength(buffer, count)) > 0
                        && length <= count)
                    {
                        var packet = buffer.AsSpan(0, length - this.Footer.Length).ToArray();
                        this.Packets.Writer.TryWrite(packet);
                        Buffer.BlockCopy(buffer, length, buffer, 0, count - length);
                        count -= length;
                    }
                }
            }
            catch (Exception) { }
            finally { this.Packets.Writer.TryComplete(); }
        }

        public async ValueTask DisposeAsync()
        {
            this.Closing.Cancel();
            this.Tcp.Dispose();
            if (this.ReadTask != null)
                await this.ReadTask;
            GC.SuppressFinalize(this);
        }
    }
}

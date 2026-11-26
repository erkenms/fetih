namespace Fetih.Network.Packets
{
    using System;
    using System.Threading.Tasks;

    public abstract class MsgBase<TClient> : IPacket
    {
        public ushort Length { get; protected set; }
        public PacketType Type { get; protected set; }

        public virtual void Decode(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        public virtual byte[] Encode()
        {
            throw new NotImplementedException();
        }

        public virtual Task ProcessAsync(TClient client)
        {
            return Task.CompletedTask;
        }
    }
}

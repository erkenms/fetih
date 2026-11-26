namespace Fetih.Network.Packets
{
    public interface IPacket
    {
        void Decode(byte[] bytes);
        byte[] Encode();
    }
}
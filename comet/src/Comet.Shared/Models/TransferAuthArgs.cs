namespace Comet.Shared.Models
{
    public class TransferAuthArgs
    {
        public string IPAddress { get; set; }
        public uint AccountID { get; set; }
        public ushort AuthorityID { get; set; }
        public string AuthorityName { get; set; }
    }
}
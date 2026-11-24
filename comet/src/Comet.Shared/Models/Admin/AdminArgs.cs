namespace Comet.Shared.Models.Admin
{
    // Every admin call names the account acting, so the game can log who did it
    public class AdminArgs
    {
        public uint ActorAccountID { get; set; }
    }

    public class CharacterArgs : AdminArgs
    {
        public uint CharacterID { get; set; }
    }

    public class KickArgs : CharacterArgs
    {
        public string Reason { get; set; }
    }

    // Zero minutes lifts the mute
    public class MuteArgs : CharacterArgs
    {
        public int Minutes { get; set; }
    }

    public class BanArgs : AdminArgs
    {
        public uint AccountID { get; set; }
        public string Reason { get; set; }
    }

    public class TeleportArgs : CharacterArgs
    {
        public uint MapID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
    }

    // Gems are the gem codes of the item table; null keeps what the item type gives
    public class GiveItemArgs : CharacterArgs
    {
        public uint TypeID { get; set; }
        public int Count { get; set; } = 1;
        public byte Plus { get; set; }
        public byte Bless { get; set; }
        public byte? Gem1 { get; set; }
        public byte? Gem2 { get; set; }
    }

    // Only from the inventory
    public class TakeItemArgs : CharacterArgs
    {
        public uint ItemID { get; set; }
    }

    // Added to the balance; negative takes away
    public class AmountArgs : CharacterArgs
    {
        public long Amount { get; set; }
    }

    public class LevelArgs : CharacterArgs
    {
        public byte Level { get; set; }
    }

    public class MessageArgs : CharacterArgs
    {
        public string Text { get; set; }
    }

    public enum BroadcastChannel
    {
        Center,
        System,
        TopLeft,
    }

    public class BroadcastArgs : AdminArgs
    {
        public string Text { get; set; }
        public BroadcastChannel Channel { get; set; }
    }
}

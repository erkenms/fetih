namespace Fetih.Shared.Models.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // The game's RPC methods for the web's PK and jail overview, next to IGameAdmin
    public interface IGamePk
    {
        Task<PkLiveSnapshot> PkSnapshot();
        // Releases a character from the PK jail (map 6000) to the start point; Invalid if not in jail
        Task<AdminResult> ReleaseFromJail(CharacterArgs args);
    }

    public class PkLiveSnapshot
    {
        public DateTime TakenAt { get; set; }
        public List<ActivePkPlayer> Active { get; set; } = new List<ActivePkPlayer>();
        public List<JailedOnlinePlayer> Jailed { get; set; } = new List<JailedOnlinePlayer>();
    }

    public class ActivePkPlayer
    {
        public uint CharacterID { get; set; }
        public uint AccountID { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }
        public byte Rebirths { get; set; }
        public uint MapID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort PkPoints { get; set; }
        public bool Flashing { get; set; }
        public string NameColor { get; set; }
        public string PkMode { get; set; }
        public bool InJail { get; set; }
        public bool Dead { get; set; }
    }

    public class JailedOnlinePlayer
    {
        public uint CharacterID { get; set; }
        public uint AccountID { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort PkPoints { get; set; }
        public bool Flashing { get; set; }
        public string NameColor { get; set; }
        public DateTime? LoggedInAt { get; set; }
    }
}

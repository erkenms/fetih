namespace Comet.Shared.Models.Admin
{
    using System;
    using System.Collections.Generic;

    public class ServerState
    {
        public DateTime StartedAt { get; set; }
        public double UptimeSeconds { get; set; }
        // Connections include sessions still logging in or creating a character
        public int Connections { get; set; }
        public int Players { get; set; }
        public int MapsLoaded { get; set; }
        public int Monsters { get; set; }
        public int MonstersAlive { get; set; }
        public int GroundItems { get; set; }
        public int Booths { get; set; }
        public int BroadcastQueue { get; set; }
        // How long the last monster loop tick took, and the slowest since the server started
        public double MonsterTickMilliseconds { get; set; }
        public double SlowestMonsterTickMilliseconds { get; set; }
        public long WorkingSetBytes { get; set; }
    }

    public class OnlinePlayer
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
        public ushort Authority { get; set; }
        public string IPAddress { get; set; }
        public DateTime? LoggedInAt { get; set; }
    }

    public class MapPlayer
    {
        public uint CharacterID { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort PkPoints { get; set; }
        // white, red, black or flashing (blue), as the client colors the name
        public string NameColor { get; set; }
        public bool Dead { get; set; }
        public uint Health { get; set; }
        public uint MaxHealth { get; set; }
        public uint Mesh { get; set; }
    }

    public class MapMonster
    {
        public uint ID { get; set; }
        public uint TypeID { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public uint Lookface { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public bool Alive { get; set; }
        public uint Health { get; set; }
        public uint MaxHealth { get; set; }
        public bool Guard { get; set; }
    }

    public class MapGroundItem
    {
        public uint ID { get; set; }
        public uint TypeID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Silver { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class MapBooth
    {
        public uint ID { get; set; }
        public uint OwnerID { get; set; }
        public string OwnerName { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public int Items { get; set; }
        public string Hawk { get; set; }
    }

    public class MapNpc
    {
        public uint ID { get; set; }
        public ushort Lookface { get; set; }
        public byte Role { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
    }

    public class MapState
    {
        public uint MapID { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime TakenAt { get; set; }
        public List<MapPlayer> Players { get; set; } = new List<MapPlayer>();
        public List<MapMonster> Monsters { get; set; } = new List<MapMonster>();
        public List<MapGroundItem> GroundItems { get; set; } = new List<MapGroundItem>();
        public List<MapBooth> Booths { get; set; } = new List<MapBooth>();
        public List<MapNpc> Npcs { get; set; } = new List<MapNpc>();
    }

    public class CharacterItem
    {
        public uint ItemID { get; set; }
        public uint TypeID { get; set; }
        // The item table's position: 0 inventory, 1-19 equipped, warehouse and so on
        public byte Position { get; set; }
        public ushort Amount { get; set; }
        public ushort AmountLimit { get; set; }
        public byte Plus { get; set; }
        public byte Bless { get; set; }
        public byte Enchant { get; set; }
        public byte Gem1 { get; set; }
        public byte Gem2 { get; set; }
        public byte Color { get; set; }
        public bool Locked { get; set; }
        // The warehouse NPC, for items in a warehouse
        public uint Package { get; set; }
    }

    public class CharacterState
    {
        public bool Online { get; set; }
        public uint CharacterID { get; set; }
        public uint AccountID { get; set; }
        public string Name { get; set; }
        public string Spouse { get; set; }
        public uint Mesh { get; set; }
        public ushort Hairstyle { get; set; }
        public byte Level { get; set; }
        public ulong Experience { get; set; }
        public byte Class { get; set; }
        public byte PreviousClass { get; set; }
        public byte AncestorClass { get; set; }
        public byte Rebirths { get; set; }
        public uint Silver { get; set; }
        public uint StoredSilver { get; set; }
        public uint ConquerPoints { get; set; }
        public ushort Strength { get; set; }
        public ushort Agility { get; set; }
        public ushort Vitality { get; set; }
        public ushort Spirit { get; set; }
        public ushort AttributePoints { get; set; }
        public ushort Health { get; set; }
        public ushort Mana { get; set; }
        // Only known online, where equipment counts
        public ushort? MaxHealth { get; set; }
        public ushort? MaxMana { get; set; }
        public ushort PkPoints { get; set; }
        public uint Virtue { get; set; }
        public uint MapID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public DateTime? MutedUntil { get; set; }
        public DateTime Registered { get; set; }
        public string IPAddress { get; set; }
        public DateTime? LoggedInAt { get; set; }
        public bool Dead { get; set; }
        public List<CharacterItem> Items { get; set; } = new List<CharacterItem>();
    }
}

namespace Fetih.Web.Features.Maps;

using Fetih.Game.Npcs;

public sealed record MapCellPoint(ushort X, ushort Y);

public sealed record MapSummary(
    uint Id,
    string? Name,
    string? ServerName,
    string[] Aliases,
    string? File,
    string? Minimap,
    bool HasTiles,
    string[] Flags,
    MapCellPoint? Portal,
    uint? RebornMap,
    int NpcCount,
    int SpawnCount,
    int PortalCount);

public sealed record MapListPage(MapSummary[] Items, int Total);

public sealed record MapTileSize(int Width, int Height);

public sealed record MapTilePoint(int X, int Y);

public sealed record MapPuzzleMeta(int Width, int Height, int GridSize);

// A sprite the client loops over the map (fire, orb, dragonfly), left out of the tiles: its top
// left in background pixels, its size, how long each frame shows in ms, and the frames' paths.
public sealed record MapAnimation(int X, int Y, int Width, int Height, int Interval, string[] Frames);

public sealed record MapTileMeta(
    int Width,
    int Height,
    int TileSize,
    int MaxZoom,
    MapTileSize Cell,
    MapTileSize MapSize,
    MapTilePoint Origin,
    MapTilePoint Background,
    MapPuzzleMeta Puzzle,
    string Ground,
    string Covers,
    MapAnimation[]? Animations = null);

public sealed record MapNpcEntry(
    uint Id,
    string? Name,
    ushort Lookface,
    byte Direction,
    ushort Role,
    ushort X,
    ushort Y,
    uint Life,
    bool IsGoal,
    string? FacePath,
    string Kind);

public sealed record MapNpcData(uint MapId, string? MapName, MapNpcEntry Npc, Teleporter? Teleporter, Talker? Talker);

public sealed record MapSpawnEntry(
    uint Id,
    ushort X,
    ushort Y,
    ushort Spread,
    int Amount,
    int Respawn,
    uint MonsterId,
    string? MonsterName,
    int? MonsterLevel,
    bool MonsterBoss);

public sealed record MapPortalEntry(
    uint Index,
    ushort? X,
    ushort? Y,
    uint? ToMap,
    string? ToMapName,
    ushort? ToX,
    ushort? ToY);

public sealed record MapDestinationEntry(
    string Name,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y);

public sealed record MapTeleporterEntry(
    uint NpcId,
    string? NpcName,
    ushort X,
    ushort Y,
    uint Fare,
    byte Face,
    string? FacePath,
    string[] Text,
    MapDestinationEntry[] Destinations);

public sealed record MapGuildWarObject(
    uint NpcId,
    string Name,
    string Role,
    ushort X,
    ushort Y,
    uint MaxLife,
    MapCellPoint? InsidePoint);

public sealed record MapGuildWarConductor(
    uint NpcId,
    string City,
    ushort X,
    ushort Y,
    uint ToMap,
    string? ToMapName,
    ushort ToX,
    ushort ToY);

public sealed record MapGuildWarInfo(
    MapGuildWarObject? Pole,
    MapGuildWarObject? LeftGate,
    MapGuildWarObject? RightGate,
    MapGuildWarConductor[] Conductors);

public sealed record MapDetail(
    uint Id,
    string? Name,
    string? ServerName,
    string[] Aliases,
    string? File,
    string? Minimap,
    bool HasTiles,
    bool HasGrid,
    int? Width,
    int? Height,
    string[] Flags,
    MapCellPoint? Portal,
    uint? RebornMap,
    string? RebornMapName,
    uint? ScrollTypeId,
    byte RidePlus,
    MapTileMeta? Meta,
    MapNpcEntry[] Npcs,
    MapSpawnEntry[] Spawns,
    MapPortalEntry[] Portals,
    MapTeleporterEntry[] Teleporters,
    MapGuildWarInfo? GuildWar);

public sealed record MapGridResponse(
    uint MapId,
    int Width,
    int Height,
    string WalkableBase64,
    short MinElevation,
    short MaxElevation,
    string ElevationBase64);

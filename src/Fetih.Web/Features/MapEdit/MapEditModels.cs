namespace Fetih.Web.Features.MapEdit;

using Fetih.Web.Features.Maps;

public sealed record MapEditHashes(
    string Maps,
    string Npcs,
    string Spawns,
    string Portals);

public sealed record EditableMapSettings(
    bool InMapsJson,
    string? Name,
    string[] Aliases,
    string[] Flags,
    MapCellPoint? Portal,
    uint? RebornMap,
    uint? ScrollTypeId,
    byte RidePlus);

public sealed record EditableNpc(
    uint Id,
    ushort Lookface,
    byte Role,
    ushort X,
    ushort Y,
    uint Life,
    byte Level,
    string? Name = null);

public sealed record EditableSpawn(
    uint Id,
    ushort X,
    ushort Y,
    ushort Spread,
    int Amount,
    int Respawn,
    uint MonsterId,
    string? MonsterName = null,
    int? MonsterLevel = null,
    bool MonsterBoss = false);

public sealed record EditablePortal(
    uint Index,
    ushort? X,
    ushort? Y,
    uint? ToMap,
    string? ToMapName,
    ushort? ToX,
    ushort? ToY);

public sealed record MapEditProblemDto(
    string File,
    int? Entry,
    int? Line,
    string Message,
    string Level);

public sealed record MapEditResponse(
    uint MapId,
    MapEditHashes Hashes,
    EditableMapSettings Settings,
    string[] AvailableFlags,
    EditableNpc[] Npcs,
    EditableSpawn[] Spawns,
    EditablePortal[] Portals,
    uint NextNpcId,
    uint NextSpawnId,
    MapEditProblemDto[] Problems);

public sealed record SaveMapEditRequest(
    MapEditHashes Hashes,
    EditableMapSettings Settings,
    EditableNpc[] Npcs,
    EditableSpawn[] Spawns,
    EditablePortal[] Portals);

public sealed record MapEditChangeCounts(
    bool MapsChanged,
    int NpcsAdded,
    int NpcsUpdated,
    int NpcsRemoved,
    int SpawnsAdded,
    int SpawnsUpdated,
    int SpawnsRemoved,
    int PortalsAdded,
    int PortalsUpdated,
    int PortalsRemoved);

public sealed record SaveMapEditResponse(
    bool Saved,
    MapEditHashes Hashes,
    string[] ChangedFiles,
    MapEditProblemDto[] Problems,
    MapEditChangeCounts Counts);

public sealed record ReloadMapFilesRequest(string[]? Files);

public sealed record ReloadMapFileResult(
    string File,
    bool Applied,
    string[] Errors,
    string[] Warnings,
    string? Summary);

public sealed record ReloadMapFilesResponse(ReloadMapFileResult[] Results);

public sealed record SingleEditResult(
    bool Saved,
    string? ChangedFile,
    MapEditProblemDto[] Problems,
    uint? EntityId = null,
    string? Error = null);

public sealed record MapProblemCounts(int Errors, int Warnings);

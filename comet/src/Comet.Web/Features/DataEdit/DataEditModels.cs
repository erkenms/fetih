namespace Comet.Web.Features.DataEdit;

public sealed record DataProblemDto(
    string File,
    int? Entry,
    int? Line,
    string Message,
    string Level);

public sealed record LevelEntryDto(
    byte Level,
    ulong Experience,
    uint UpLevTime,
    int Line);

public sealed record LevelEntryInput(
    byte Level,
    ulong Experience,
    uint UpLevTime);

public sealed record LevelsFileResponse(
    string Hash,
    LevelEntryDto[] Entries,
    DataProblemDto[] Problems);

public sealed record SaveLevelsRequest(
    string Hash,
    LevelEntryInput[] Entries);

public sealed record GoalEntryDto(
    uint Id,
    ushort Lookface,
    byte Role,
    byte Level,
    uint Life,
    uint Map,
    ushort X,
    ushort Y,
    int Line);

public sealed record GoalEntryInput(
    uint Id,
    ushort Lookface,
    byte Role,
    byte Level,
    uint Life,
    uint Map,
    ushort X,
    ushort Y);

public sealed record GoalsFileResponse(
    string Hash,
    GoalEntryDto[] Entries,
    DataProblemDto[] Problems);

public sealed record SaveGoalsRequest(
    string Hash,
    GoalEntryInput[] Entries);

public sealed record TeleporterDestinationDto(
    string Name,
    uint Map,
    ushort X,
    ushort Y);

public sealed record TeleporterEntryDto(
    uint Id,
    uint Fare,
    byte Face,
    string[] Text,
    TeleporterDestinationDto[] Destinations,
    string? Close,
    int Line,
    uint? NpcMap,
    ushort? NpcX,
    ushort? NpcY);

public sealed record TeleporterEntryInput(
    uint Id,
    uint Fare,
    byte Face,
    string[] Text,
    TeleporterDestinationDto[] Destinations,
    string? Close);

public sealed record TeleportersFileResponse(
    string Hash,
    TeleporterEntryDto[] Entries,
    DataProblemDto[] Problems);

public sealed record SaveTeleportersRequest(
    string Hash,
    TeleporterEntryInput[] Entries);

public sealed record TalkerEntryDto(
    uint Id,
    byte Face,
    string[] Text,
    string Reply,
    int Line,
    uint? NpcMap,
    ushort? NpcX,
    ushort? NpcY);

public sealed record TalkerEntryInput(
    uint Id,
    byte Face,
    string[] Text,
    string Reply);

public sealed record TalkersFileResponse(
    string Hash,
    TalkerEntryDto[] Entries,
    DataProblemDto[] Problems);

public sealed record SaveTalkersRequest(
    string Hash,
    TalkerEntryInput[] Entries);

public sealed record SaveDataResponse(
    string Hash,
    bool Saved,
    DataProblemDto[] Problems,
    int Added,
    int Updated,
    int Removed);

public sealed record MapPickInfoResponse(
    uint MapId,
    string? Name,
    int? Width,
    int? Height,
    string? Minimap,
    string? Background);

public sealed record CellCheckResponse(
    uint MapId,
    int X,
    int Y,
    bool HasGrid,
    bool InBounds,
    bool Walkable);

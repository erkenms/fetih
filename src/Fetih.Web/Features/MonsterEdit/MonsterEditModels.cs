namespace Fetih.Web.Features.MonsterEdit;

public sealed record MonsterDto(
    uint Id,
    string Name,
    uint Lookface,
    byte Level,
    uint Life,
    int AttackMin,
    int AttackMax,
    int Defense,
    int Dexterity,
    int Dodge,
    int AttackRange,
    int ViewRange,
    int AttackSpeed,
    int MoveSpeed,
    uint AttackUser,
    uint DropMoney,
    uint DropHp,
    uint DropMp,
    bool Boss,
    ushort MagicType,
    int MagicHitRate,
    ushort[] Spells);

public sealed record MonsterProblemDto(
    string File,
    int? Entry,
    int? Line,
    string Message,
    string Level);

public sealed record MonsterListRow(
    MonsterDto Monster,
    int Line,
    int SpawnAreaCount,
    int TotalSpawnAmount,
    int BossScheduleCount,
    bool HasCustomDrops);

public sealed record MonsterListResponse(
    string Hash,
    bool GameOnline,
    MonsterListRow[] Monsters,
    MonsterProblemDto[] Problems);

public sealed record MonsterSpawnRef(
    uint Id,
    uint MapId,
    string? MapName,
    int X,
    int Y,
    int Spread,
    int Amount,
    int RespawnSeconds);

public sealed record MonsterBossRef(
    string Id,
    uint MapId,
    string? MapName,
    int X,
    int Y,
    int Spread,
    string[] Times,
    int IntervalSeconds,
    string? Broadcast);

public sealed record MonsterDropRef(
    uint ItemId,
    double Chance,
    int Count);

public sealed record MonsterDetailResponse(
    string Hash,
    MonsterDto Monster,
    int Line,
    MonsterSpawnRef[] Spawns,
    MonsterBossRef[] BossSpawns,
    MonsterDropRef[] Drops,
    MonsterProblemDto[] Problems);

public sealed record SaveMonsterRequest(
    string? Hash,
    MonsterDto Monster);

public sealed record SaveMonsterResponse(
    string Hash,
    MonsterDto Monster,
    MonsterProblemDto[] Warnings);

public sealed record DeleteMonsterResponse(
    string Hash,
    uint Id);

public sealed record MonsterValidationError(
    string Error,
    MonsterProblemDto[] Problems);

public sealed record MonsterReloadResponse(
    string File,
    bool Applied,
    string[] Errors,
    string[] Warnings,
    string? Summary);

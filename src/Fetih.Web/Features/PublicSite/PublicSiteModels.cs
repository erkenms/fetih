namespace Fetih.Web.Features.PublicSite;

public record PublicHomeResponse(
    bool GameOnline,
    int? OnlinePlayers,
    GuildWarSummary? GuildWar,
    LevelRankEntry[] TopPlayers,
    NobilityRankEntry[] TopNobility,
    GuildRankEntry[] TopGuilds,
    ArenaRankEntry[] TopArena,
    NewsEntry[] LatestNews);

public record GuildWarSummary(
    uint? HolderGuildId,
    string? HolderGuildName,
    uint? WinnerGuildId,
    string? WinnerGuildName,
    DateTime? WonAt);

public record RankingPage<T>(T[] Items, int Total, int Page, int PageSize, DateTime CachedAt);

public record LevelRankEntry(
    int Rank,
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Rebirths,
    byte Class,
    uint? GuildId,
    string? GuildName);

public record NobilityRankEntry(
    int Rank,
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Class,
    string NobilityRank,
    ulong Donation);

public record GuildRankEntry(
    int Rank,
    uint GuildId,
    string Name,
    byte Level,
    uint LeaderId,
    string? LeaderName,
    int MemberCount,
    ulong Fund,
    uint ConquerPointsFund,
    bool IsGuildWarHolder,
    bool IsGuildWarWinner);

public record ArenaRankEntry(
    int Rank,
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Class,
    uint Points,
    uint HonorTotal,
    uint WinsToday,
    uint LossesToday,
    uint WinsTotal,
    uint LossesTotal);

public record ArenaSeasonInfo(uint Season, int Entries);

public record ArenaSeasonRankEntry(
    uint Rank,
    uint CharacterId,
    string Name,
    byte Level,
    byte Class,
    uint Points,
    uint Wins,
    uint Losses);

public record FlowerRankEntry(
    int Rank,
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Class,
    uint Amount,
    uint RedRoses,
    uint Lilies,
    uint Orchids,
    uint Tulips);

public record PkRankEntry(
    int Rank,
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Rebirths,
    byte Class,
    ushort PkPoints,
    uint? GuildId,
    string? GuildName);

public record FamilyRankEntry(
    int Rank,
    uint FamilyId,
    string Name,
    byte Level,
    uint LeaderId,
    string? LeaderName,
    int MemberCount,
    ulong Fund,
    int OccupiedMaps);

public record PublicCharacterResponse(
    uint CharacterId,
    string Name,
    ushort Avatar,
    uint Mesh,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    byte PreviousClass,
    byte AncestorClass,
    string? Spouse,
    ushort PkPoints,
    uint Virtue,
    uint QuizPoints,
    DateTime Registered,
    PublicCharacterGuild? Guild,
    PublicCharacterFamily? Family,
    PublicCharacterNobility? Nobility,
    PublicCharacterFlowers? Flowers,
    PublicCharacterArena? Arena,
    PublicCharacterSubClass[] SubClasses);

public record PublicCharacterGuild(uint GuildId, string Name, ushort Rank);

public record PublicCharacterFamily(uint FamilyId, string Name, ushort Rank);

public record PublicCharacterNobility(string Rank, int? Position, ulong Donation);

public record PublicCharacterFlowers(uint RedRoses, uint Lilies, uint Orchids, uint Tulips);

public record PublicCharacterArena(uint Points, uint HonorTotal, uint WinsTotal, uint LossesTotal);

public record PublicCharacterSubClass(byte SubClassId, byte Level, byte Phase, bool Active);

public record PublicGuildResponse(
    uint GuildId,
    string Name,
    byte Level,
    uint LeaderId,
    string? LeaderName,
    string? Announcement,
    ulong Fund,
    uint ConquerPointsFund,
    uint Created,
    bool IsGuildWarHolder,
    bool IsGuildWarWinner,
    PublicGuildRelation[] Allies,
    PublicGuildRelation[] Enemies,
    PublicGuildMember[] Members);

public record PublicGuildRelation(uint GuildId, string Name);

public record PublicGuildMember(
    uint CharacterId,
    string Name,
    ushort Avatar,
    byte Level,
    byte Class,
    ushort Rank,
    uint Joined);

public record NewsEntry(
    string Hash,
    string ShortHash,
    DateTime Date,
    string Subject,
    string? Body);

public record NewsPage(NewsEntry[] Items, int Total);

public record DownloadInfoResponse(string? Url, string Version);

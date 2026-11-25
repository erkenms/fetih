namespace Comet.Web.Features.Social;

using Comet.Shared.Models.Admin;

public record CharacterRef(uint Id, string Name, byte Level, byte Class);

public record GuildSummary(uint Id, string Name, CharacterRef? Leader, byte Level, ulong Fund,
    uint ConquerPointsFund, int Members, DateOnly? Created);

public record GuildMemberView(CharacterRef Character, ushort Rank, uint SilverDonation,
    uint ConquerPointsDonation, DateOnly? Joined);

public record GuildRef(uint Id, string Name);

public record GuildDetail(GuildSummary Guild, string Announcement, uint RequiredLevel, uint RequiredRebirths,
    GuildMemberView[] Members, GuildRef[] Allies, GuildRef[] Enemies);

public record FamilySummary(uint Id, string Name, CharacterRef? Leader, byte Level, ulong Fund, int Members,
    DateOnly? Created, int Territories);

public record FamilyMemberView(CharacterRef Character, ushort Rank, ulong Donation, DateOnly? Joined);

// A city the family holds (or signed up to fight for) in the clan war
public record FamilyTerritory(uint NpcId, string Name, uint Days, bool RevenueClaimed);

public record FamilyDetail(FamilySummary Family, string Announcement, FamilyMemberView[] Members,
    GuildRef[] Allies, GuildRef[] Enemies, FamilyTerritory[] Occupied, FamilyTerritory[] SignedUp);

public record NobilityEntry(int Position, CharacterRef Character, ulong Donation, string Rank);

public record ArenaEntry(int Position, CharacterRef Character, uint Points, uint WinsToday, uint LossesToday,
    uint WinsTotal, uint LossesTotal, uint HonorPoints, uint HonorTotal);

public record ArenaSeasonEntry(uint Rank, uint CharacterId, string Name, byte Level, byte Class, uint Points,
    uint Wins, uint Losses);

public record ArenaResponse(ArenaEntry[] Current, uint[] Seasons);

public record FlowerEntryView(int Position, CharacterRef Character, uint Amount, uint Today);

public record GuildWarSaved(GuildRef? Winner, GuildRef? Holder, bool RewardClaimed, DateTime? WonAt);

public record GuildWarResponse(
    // From the game while it runs, else null
    GuildWarState? Live,
    // What the database keeps between wars
    GuildWarSaved Saved,
    GuildRef? LiveHolder,
    GuildRef? LiveWinner);

public record BoardPost(string Channel, ushort ChannelId, uint CharacterId, CharacterRef? Author, string AuthorName,
    GuildRef? Guild, string Message, DateTime PostedAt);

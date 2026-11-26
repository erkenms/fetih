namespace Fetih.Web.Features.Live;

using Fetih.Shared.Models.Admin;

public record LiveOnlinePlayer(
    uint CharacterId,
    uint AccountId,
    string Name,
    byte Level,
    byte Class,
    byte Rebirths,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    ushort PkPoints,
    ushort Authority,
    string IpAddress,
    DateTime? LoggedInAt);

public record OnlinePlayersResponse(
    bool GameOnline,
    DateTime TakenAt,
    LiveOnlinePlayer[] Players);

public record LiveGroundItem(
    uint Id,
    uint TypeId,
    string? ItemName,
    string? Icon,
    ushort X,
    ushort Y,
    uint Silver,
    DateTime ExpiresAt);

public record LiveMapSnapshot(
    uint MapId,
    int Width,
    int Height,
    DateTime TakenAt,
    bool GameOnline,
    bool Exists,
    MapPlayer[] Players,
    MapMonster[] Monsters,
    LiveGroundItem[] GroundItems,
    MapBooth[] Booths);

public record LiveMapDelta(
    uint MapId,
    DateTime TakenAt,
    bool GameOnline,
    MapPlayer[] PlayersUpsert,
    uint[] PlayersRemoved,
    MapMonster[] MonstersUpsert,
    uint[] MonstersRemoved,
    LiveGroundItem[] GroundItemsUpsert,
    uint[] GroundItemsRemoved,
    MapBooth[] BoothsUpsert,
    uint[] BoothsRemoved)
{
    public bool IsEmpty =>
        this.PlayersUpsert.Length == 0 &&
        this.PlayersRemoved.Length == 0 &&
        this.MonstersUpsert.Length == 0 &&
        this.MonstersRemoved.Length == 0 &&
        this.GroundItemsUpsert.Length == 0 &&
        this.GroundItemsRemoved.Length == 0 &&
        this.BoothsUpsert.Length == 0 &&
        this.BoothsRemoved.Length == 0;
}

public static class LiveDeltaCalculator
{
    public static LiveMapDelta ComputeDelta(LiveMapSnapshot previous, LiveMapSnapshot current)
    {
        var (playersUpsert, playersRemoved) = Diff(
            previous.Players, current.Players, p => p.CharacterID, PlayerEquals);
        var (monstersUpsert, monstersRemoved) = Diff(
            previous.Monsters, current.Monsters, m => m.ID, MonsterEquals);
        var (itemsUpsert, itemsRemoved) = Diff(
            previous.GroundItems, current.GroundItems, i => i.Id, GroundItemEquals);
        var (boothsUpsert, boothsRemoved) = Diff(
            previous.Booths, current.Booths, b => b.ID, BoothEquals);

        return new LiveMapDelta(
            current.MapId,
            current.TakenAt,
            current.GameOnline,
            playersUpsert,
            playersRemoved,
            monstersUpsert,
            monstersRemoved,
            itemsUpsert,
            itemsRemoved,
            boothsUpsert,
            boothsRemoved);
    }

    public static bool OnlinePlayersChanged(LiveOnlinePlayer[] previous, LiveOnlinePlayer[] current)
    {
        if (previous.Length != current.Length)
            return true;
        for (int i = 0; i < previous.Length; i++)
        {
            if (previous[i] != current[i])
                return true;
        }
        return false;
    }

    private static (T[] Upsert, uint[] Removed) Diff<T>(
        T[] previous,
        T[] current,
        Func<T, uint> keyOf,
        Func<T, T, bool> equals)
    {
        var prevById = new Dictionary<uint, T>(previous.Length);
        foreach (var item in previous)
            prevById[keyOf(item)] = item;

        var upsert = new List<T>();
        var currentIds = new HashSet<uint>(current.Length);
        foreach (var item in current)
        {
            uint id = keyOf(item);
            currentIds.Add(id);
            if (!prevById.TryGetValue(id, out var old) || !equals(old, item))
                upsert.Add(item);
        }

        var removed = new List<uint>();
        foreach (var id in prevById.Keys)
        {
            if (!currentIds.Contains(id))
                removed.Add(id);
        }

        return ([.. upsert], [.. removed]);
    }

    private static bool PlayerEquals(MapPlayer a, MapPlayer b) =>
        a.CharacterID == b.CharacterID &&
        a.X == b.X &&
        a.Y == b.Y &&
        a.Level == b.Level &&
        a.Class == b.Class &&
        a.PkPoints == b.PkPoints &&
        a.Dead == b.Dead &&
        a.Health == b.Health &&
        a.MaxHealth == b.MaxHealth &&
        a.Mesh == b.Mesh &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
        string.Equals(a.NameColor, b.NameColor, StringComparison.Ordinal);

    private static bool MonsterEquals(MapMonster a, MapMonster b) =>
        a.ID == b.ID &&
        a.TypeID == b.TypeID &&
        a.X == b.X &&
        a.Y == b.Y &&
        a.Alive == b.Alive &&
        a.Health == b.Health &&
        a.MaxHealth == b.MaxHealth &&
        a.Level == b.Level &&
        a.Guard == b.Guard &&
        a.Lookface == b.Lookface &&
        string.Equals(a.Name, b.Name, StringComparison.Ordinal);

    private static bool GroundItemEquals(LiveGroundItem a, LiveGroundItem b) =>
        a.Id == b.Id &&
        a.TypeId == b.TypeId &&
        a.X == b.X &&
        a.Y == b.Y &&
        a.Silver == b.Silver &&
        a.ExpiresAt == b.ExpiresAt;

    private static bool BoothEquals(MapBooth a, MapBooth b) =>
        a.ID == b.ID &&
        a.OwnerID == b.OwnerID &&
        a.X == b.X &&
        a.Y == b.Y &&
        a.Items == b.Items &&
        string.Equals(a.OwnerName, b.OwnerName, StringComparison.Ordinal) &&
        string.Equals(a.Hawk, b.Hawk, StringComparison.Ordinal);
}

namespace Fetih.Web.Features.Players;

using Fetih.Web.Features.Characters;

// An account and its character (an account holds at most one), as one row of the players page.
// The character fields are null for an account that never made one.
public record PlayerListItem(
    uint AccountId,
    string Username,
    string? Email,
    ushort AuthorityId,
    ushort StatusId,
    string? IpAddress,
    DateTime Registered,
    DateTime? LastLogin,
    uint? CharacterId,
    string? Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    uint Silver,
    uint ConquerPoints,
    ushort KillPoints,
    bool Online);

public record PlayerListResponse(
    PlayerListItem[] Items,
    int Total,
    int Page,
    int PageSize,
    bool GameConnected);

public record PlayerIdsResponse(uint[] AccountIds);

public record PlayerBulkRequest(
    string Action,
    uint[] AccountIds,
    string? Reason = null,
    int Minutes = 0,
    string? Text = null,
    GiveItemRequest? Item = null,
    long Amount = 0,
    byte Level = 0,
    uint MapId = 0,
    ushort X = 0,
    ushort Y = 0);

public record PlayerBulkFailure(uint AccountId, string? Name, string Error);

public record PlayerBulkResponse(int Succeeded, PlayerBulkFailure[] Failures);

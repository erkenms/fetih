namespace Comet.Web.Features.Accounts;

public record AccountCharacterSummary(
    uint CharacterId,
    string Name,
    byte Level,
    byte Rebirths,
    byte CurrentClass,
    uint MapId,
    string? MapName,
    uint Silver,
    uint ConquerPoints,
    bool Online);

public record AccountLoginEntry(
    DateTime Timestamp,
    string IpAddress);

public record SameIpAccountSummary(
    uint AccountId,
    string Username,
    ushort AuthorityId,
    ushort StatusId,
    DateTime Registered,
    string[] SharedIps);

public record AccountDetailResponse(
    uint AccountId,
    string Username,
    string? Email,
    string? Name,
    ushort AuthorityId,
    ushort StatusId,
    string? IpAddress,
    DateTime Registered,
    bool GameConnected,
    bool PacketTraced,
    AccountCharacterSummary[] Characters,
    AccountLoginEntry[] Logins,
    SameIpAccountSummary[] SameIpAccounts);

public record AccountBanRequest(string? Reason = null);

public record SetAccountStatusRequest(ushort StatusId, string? Reason = null);

public record SetAccountAuthorityRequest(ushort AuthorityId);

public record ResetAccountPasswordRequest(string NewPassword);

public record SetPacketTraceRequest(bool Enabled);

public record AccountActionResponse(bool Ok, bool Online);

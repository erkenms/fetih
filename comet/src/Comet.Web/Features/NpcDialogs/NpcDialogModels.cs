namespace Comet.Web.Features.NpcDialogs;

// Kind: "choice" (answers with Option), "close" (255, closes the window) or "textbox". Option
// is null when a script computes it.
public sealed record NpcDialogOption(
    string Text,
    byte? Option,
    string Kind,
    uint? TargetMapId = null,
    string? TargetMapName = null,
    ushort? TargetX = null,
    ushort? TargetY = null);

// One window the NPC shows: its lines, then its options, and the face (0 for none)
public sealed record NpcDialogPage(string[] Lines, NpcDialogOption[] Options, int Face, string? FacePath = null);

public sealed record NpcTalkStats(
    bool LokiAvailable,
    int TotalTalks,
    int UniquePlayers,
    DateTime? LastTalkAt,
    Dictionary<string, int> ByOption);

// Kind: what answers a click on the NPC, in the order the game checks: "script" (its own
// method), "teleporter", "talker", "shop", "warehouse" or "default"
public sealed record NpcDialogSummary(
    uint NpcId,
    string? Name,
    uint MapId,
    string? MapName,
    ushort X,
    ushort Y,
    ushort Lookface,
    string? FacePath,
    string Kind,
    int? TalkCount);

public sealed record NpcDialogListResponse(NpcDialogSummary[] Npcs, bool LokiAvailable, string Range);

// Pages: every page for a script as read from its method (Handler, in SourceFile), else the
// first page exactly as the game builds it. A shop or warehouse opens its window and has none.
public sealed record NpcDialogDetailResponse(
    NpcDialogSummary Npc,
    ushort Role,
    string? Handler,
    string? SourceFile,
    uint? Fare,
    int? ShopItemCount,
    NpcDialogPage[] Pages,
    NpcTalkStats Stats);

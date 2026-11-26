namespace Fetih.Web.Features.Board;

using System.Globalization;
using System.Security.Claims;
using Fetih.Game.Database;
using Fetih.Game.Database.Models;
using Fetih.Shared.Models.Admin;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.EntityFrameworkCore;

public sealed record BoardPostView(
    ushort ChannelId,
    string ChannelText,
    uint CharacterId,
    string AuthorName,
    byte? AuthorLevel,
    string? GuildName,
    string Message,
    DateTime PostedAt);

// In-game message board posts (MsgTalk channels 2201-2205). Reads from the game database;
// deletes go through the game's RPC so connected players see the removal immediately.
public sealed class BoardService(GameRpc rpc, AuditLog audit)
{
    public const int ListLimit = 200;

    public static readonly (ushort Id, string Key, string Text)[] Channels =
    [
        (2201, "trade", "Ticaret"),
        (2202, "friend", "Arkadaş"),
        (2203, "team", "Takım"),
        (2204, "guild", "Lonca"),
        (2205, "others", "Diğer"),
    ];

    public bool GameConnected => rpc.Connected;

    public static string ChannelKey(ushort id) =>
        Channels.FirstOrDefault(c => c.Id == id).Key ?? id.ToString(CultureInfo.InvariantCulture);

    public static string ChannelText(ushort id) =>
        Channels.FirstOrDefault(c => c.Id == id).Text ?? id.ToString(CultureInfo.InvariantCulture);

    public static bool IsValidChannel(ushort id) => Channels.Any(c => c.Id == id);

    public async Task<BoardPostView[]> ListAsync(ushort? channel, CancellationToken ct = default)
    {
        await using var db = new ServerDbContext();
        var query = db.MessageBoard.AsNoTracking();
        if (channel is ushort c)
            query = query.Where(p => p.Channel == c);
        var posts = await query.OrderByDescending(p => p.PostedAt).Take(ListLimit).ToListAsync(ct);

        var characterIds = posts.Select(p => p.CharacterID).Where(i => i != 0).Distinct().ToList();
        var characters = await db.Characters.AsNoTracking().Where(c => characterIds.Contains(c.CharacterID))
            .Select(c => new { c.CharacterID, c.Name, c.Level })
            .ToDictionaryAsync(c => c.CharacterID, ct);

        var guildIds = posts.Select(p => p.SyndicateID).Where(i => i != 0).Distinct().ToList();
        var guilds = await db.Syndicates.AsNoTracking().Where(g => guildIds.Contains(g.SyndicateID))
            .ToDictionaryAsync(g => g.SyndicateID, g => g.Name, ct);

        return posts.Select(p =>
        {
            var author = characters.GetValueOrDefault(p.CharacterID);
            string? guildName = p.SyndicateID != 0
                ? guilds.GetValueOrDefault(p.SyndicateID) ?? $"#{p.SyndicateID}"
                : null;
            return new BoardPostView(
                p.Channel, ChannelText(p.Channel), p.CharacterID,
                author?.Name ?? p.AuthorName, author?.Level, guildName,
                p.Message, DateTime.SpecifyKind(p.PostedAt, DateTimeKind.Utc));
        }).ToArray();
    }

    public async Task<string?> DeleteAsync(ClaimsPrincipal user, ushort channel, uint characterId, CancellationToken ct = default)
    {
        if (!IsValidChannel(channel))
            return "not_found";

        DbMessageBoard? post;
        await using (var db = new ServerDbContext())
            post = await db.MessageBoard.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Channel == channel && p.CharacterID == characterId, ct);
        if (post == null)
            return "not_found";

        var result = await rpc.CallAsync<AdminResult>("DeleteBoardPost",
            new BoardPostArgs { ActorAccountID = user.AccountId(), Channel = channel, CharacterID = characterId });
        if (!result.Ok)
            return "not_found";

        await audit.WriteAsync("board.delete_post", "character", characterId.ToString(CultureInfo.InvariantCulture),
            before: new { post.Channel, post.AuthorName, post.Message, post.PostedAt },
            summary: $"Deleted {post.AuthorName}'s {ChannelKey(channel)} post");
        return null;
    }
}

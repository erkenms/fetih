namespace Comet.Web.Features.Chat;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Comet.Core.Logging;
using Comet.Web.Features.Logs;

public record ChatQueryFilter(
    string? Channel = null,
    uint? CharacterId = null,
    string? CharacterName = null,
    uint[]? AccountIds = null,
    string? Words = null);

public record GmCommandQueryFilter(
    uint? CharacterId = null,
    string? CharacterName = null,
    uint[]? AccountIds = null,
    string? Command = null,
    string? Words = null);

public static class ChatLogQl
{
    public const string CommandChannel = "Command";
    public const string GmCommandChannel = "GmCommand";

    public static readonly string[] TalkChannels =
    [
        "Talk", "Whisper", "Team", "Guild", "Clan", "Friend", "World", "Ghost", "Vendor",
    ];

    private static readonly Dictionary<string, string> CanonicalTalkChannels =
        TalkChannels.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex SafeCommand = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public static string? NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return null;
        string trimmed = channel.Trim();
        if (trimmed.Equals(CommandChannel, StringComparison.OrdinalIgnoreCase))
            return CommandChannel;
        return CanonicalTalkChannels.TryGetValue(trimmed, out var canonical) ? canonical : null;
    }

    public static string BuildChatQuery(ChatQueryFilter filter)
    {
        string? channel = NormalizeChannel(filter.Channel);
        var sb = new StringBuilder();

        if (channel == CommandChannel)
            sb.Append("{server=\"game\", event=~\"chat\\\\.command|gm\\\\.command\"}");
        else if (channel != null)
            sb.Append("{server=\"game\", event=\"chat.message\"}");
        else
            sb.Append("{server=\"game\", event=~\"chat\\\\.(message|command)|gm\\\\.command\"}");

        bool hasTalkChannel = channel != null && channel != CommandChannel;
        string? charName = SanitizeText(filter.CharacterName);
        bool hasAccounts = filter.AccountIds is { Length: > 0 };

        if (hasTalkChannel || filter.CharacterId != null || charName != null || hasAccounts)
        {
            sb.Append(" | json");
            if (hasTalkChannel)
                sb.Append(" | channel=\"").Append(channel).Append('"');
            if (filter.CharacterId != null)
                sb.Append(" | characterId=\"").Append(filter.CharacterId.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (charName != null)
                sb.Append(" | name=~\"(?i).*").Append(EscapeLogQlRegex(charName)).Append(".*\"");
            AppendAccountFilter(sb, filter.AccountIds);
        }

        string? words = SanitizeText(filter.Words);
        if (words != null)
            sb.Append(" |~ \"(?i)").Append(EscapeLogQlRegex(words)).Append('"');

        return sb.ToString();
    }

    public static string BuildGmCommandQuery(GmCommandQueryFilter filter)
    {
        var sb = new StringBuilder();
        sb.Append("{server=\"game\", event=\"gm.command\"}");

        string? charName = SanitizeText(filter.CharacterName);
        string? command = SanitizeCommand(filter.Command);
        bool hasAccounts = filter.AccountIds is { Length: > 0 };

        if (filter.CharacterId != null || charName != null || hasAccounts || command != null)
        {
            sb.Append(" | json");
            if (filter.CharacterId != null)
                sb.Append(" | characterId=\"").Append(filter.CharacterId.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (charName != null)
                sb.Append(" | name=~\"(?i).*").Append(EscapeLogQlRegex(charName)).Append(".*\"");
            AppendAccountFilter(sb, filter.AccountIds);
            if (command != null)
                sb.Append(" | command=~\"(?i).*").Append(EscapeLogQlRegex(command)).Append(".*\"");
        }

        string? words = SanitizeText(filter.Words);
        if (words != null)
            sb.Append(" |~ \"(?i)").Append(EscapeLogQlRegex(words)).Append('"');

        return sb.ToString();
    }

    public static ChatLogEntry ParseEntry(LogEntry entry)
    {
        entry.Fields.TryGetValue("name", out var sender);
        sender ??= "";

        if (entry.Event is LogEvents.ChatCommand or LogEvents.GmCommand)
        {
            entry.Fields.TryGetValue("command", out var command);
            entry.Fields.TryGetValue("arguments", out var arguments);
            entry.Fields.TryGetValue("result", out var result);
            command = NormalizeNullField(command);
            arguments = NormalizeNullField(arguments);
            result = NormalizeNullField(result);

            string text = command != null
                ? string.IsNullOrEmpty(arguments) ? $"/{command}" : $"/{command} {arguments}"
                : entry.Message;

            string channel = entry.Event == LogEvents.GmCommand ? GmCommandChannel : CommandChannel;
            return new ChatLogEntry(
                entry.Timestamp,
                entry.Event,
                channel,
                entry.CharacterId,
                entry.AccountId,
                sender,
                null,
                text,
                command,
                arguments,
                result,
                entry.Message);
        }

        entry.Fields.TryGetValue("channel", out var chatChannel);
        entry.Fields.TryGetValue("recipient", out var recipient);
        entry.Fields.TryGetValue("text", out var msgText);

        return new ChatLogEntry(
            entry.Timestamp,
            entry.Event,
            NormalizeNullField(chatChannel) ?? "Talk",
            entry.CharacterId,
            entry.AccountId,
            sender,
            NormalizeNullField(recipient),
            msgText ?? entry.Message,
            null,
            null,
            null,
            entry.Message);
    }

    private static void AppendAccountFilter(StringBuilder sb, uint[]? accountIds)
    {
        if (accountIds == null || accountIds.Length == 0)
            return;
        if (accountIds.Length == 1)
        {
            sb.Append(" | accountId=\"").Append(accountIds[0].ToString(CultureInfo.InvariantCulture)).Append('"');
            return;
        }
        sb.Append(" | accountId=~\"^(")
            .Append(string.Join('|', accountIds.Select(id => id.ToString(CultureInfo.InvariantCulture))))
            .Append(")$\"");
    }

    private static string? NormalizeNullField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.Ordinal))
            return null;
        return value;
    }

    private static string? SanitizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string? SanitizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        string trimmed = command.Trim().TrimStart('/').ToLowerInvariant();
        return trimmed.Length > 0 && SafeCommand.IsMatch(trimmed) ? trimmed : null;
    }

    // Escapes regex metacharacters, then escapes backslashes and quotes for a LogQL double-quoted string.
    private static string EscapeLogQlRegex(string value)
    {
        string regexEscaped = Regex.Escape(value);
        return regexEscaped.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

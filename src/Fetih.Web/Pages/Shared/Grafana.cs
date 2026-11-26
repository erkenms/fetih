namespace Fetih.Web.Pages.Shared;

using System.Globalization;
using System.Text;

public static class Grafana
{
    public const string BasePath = "/grafana";

    public static string OverviewUrl(string? env = null) =>
        Build("/d/fetih-overview/overview", ("var-env", env));

    public static string AdminUrl(string? env = null) =>
        Build("/d/fetih-admin/admin", ("var-env", env));

    public static string EconomyUrl(string? env = null) =>
        Build("/d/fetih-economy/economy", ("var-env", env));

    public static string ChatUrl(uint? characterId = null, uint? accountId = null, string? character = null,
        string? env = null) =>
        Build("/d/fetih-chat/chat",
            ("var-env", env),
            ("var-characterId", Id(characterId)),
            ("var-accountId", Id(accountId)),
            ("var-character", character));

    public static string ItemsUrl(uint? itemId = null, string? env = null) =>
        Build("/d/fetih-items/items",
            ("var-env", env),
            ("var-itemId", Id(itemId)));

    public static string AuditUrl(string? targetType = null, string? targetId = null, string? staff = null) =>
        Build("/d/fetih-audit/audit",
            ("var-targetType", targetType),
            ("var-targetId", targetId),
            ("var-staff", staff));

    public static string PlayerUrl(uint? characterId = null, uint? accountId = null, string? env = null) =>
        Build("/d/fetih-player/player",
            ("var-env", env),
            ("var-characterId", Id(characterId)),
            ("var-accountId", Id(accountId)));

    public static string PacketsUrl(uint? accountId = null, string? packetName = null, string? env = null) =>
        Build("/d/fetih-packets/packets",
            ("var-env", env),
            ("var-accountId", Id(accountId)),
            ("var-packetName", packetName));

    private static string? Id(uint? value) =>
        value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : null;

    private static string Build(string path, params (string Key, string? Value)[] vars)
    {
        var sb = new StringBuilder(BasePath).Append(path);
        bool first = true;
        foreach (var (key, value) in vars)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }
}

namespace Fetih.Web.Pages;

using System.Globalization;

// Pages show and take times in Turkey's time (UTC+3 all year); the database keeps UTC
public static class WebTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    public static DateTime FromLocal(DateTime local) => DateTime.SpecifyKind(local - Offset, DateTimeKind.Utc);

    // 26.11.2026 21:40
    public static string Format(DateTime? utc) =>
        utc == null ? "—" : ToLocal(utc.Value).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    // The value of an <input type="datetime-local"> for a local time
    public static string Input(DateTime? local) =>
        local?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) ?? "";
}

namespace Fetih.Web.Pages;

using System.Globalization;

public static class GameLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly (int Id, string Name)[] BaseClasses =
    [
        (10, "Trojan"),
        (20, "Warrior"),
        (40, "Archer"),
        (50, "Ninja"),
        (60, "Monk"),
        (70, "Pirate"),
        (100, "Taoist"),
    ];

    public static string ClassName(byte classId) => classId switch
    {
        >= 10 and <= 15 => "Trojan",
        >= 20 and <= 25 => "Warrior",
        >= 40 and <= 45 => "Archer",
        >= 50 and <= 55 => "Ninja",
        >= 60 and <= 65 => "Monk",
        >= 70 and <= 75 => "Pirate",
        >= 132 and <= 135 => "Water Taoist",
        >= 142 and <= 145 => "Fire Taoist",
        >= 100 and <= 101 => "Taoist",
        _ => "Bilinmiyor",
    };

    public static string NobilityName(string rank) => rank switch
    {
        "King" => "Kral",
        "Prince" => "Prens",
        "Duke" => "Dük",
        "Earl" => "Kont",
        "Baron" => "Baron",
        "Knight" => "Şövalye",
        "Commoner" => "Halktan",
        _ => rank,
    };

    public static string GuildRankName(ushort rank) => rank switch
    {
        >= 1000 => "Lonca Lideri",
        >= 990 => "Yardımcı Lider",
        >= 200 => "Üye",
        _ => "Yok",
    };

    public static string ClanRankName(ushort rank) => rank switch
    {
        >= 100 => "Klan Lideri",
        11 => "Eş",
        >= 10 => "Üye",
        _ => "Yok",
    };

    public static string SubClassName(byte id) => id switch
    {
        1 => "Dövüş Ustası",
        2 => "Büyücü",
        3 => "Chi Ustası",
        4 => "Bilge",
        5 => "Simyacı",
        6 => "Sanatçı",
        9 => "Terbiyeci",
        _ => "Bilinmiyor",
    };

    public static string AuthorityName(int authority) => authority switch
    {
        1 => "Oyuncu",
        2 => "Moderatör",
        3 => "Oyun Yöneticisi",
        4 => "Proje Yöneticisi",
        5 => "Yönetici",
        _ => $"#{authority}",
    };

    public static string Number(ulong value) => value.ToString("N0", Turkish);
    public static string Number(long value) => value.ToString("N0", Turkish);
    public static string Number(uint value) => value.ToString("N0", Turkish);
    public static string Number(int value) => value.ToString("N0", Turkish);

    public static string Date(DateTime utc) =>
        WebTime.ToLocal(utc).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string UnixDate(uint unixSeconds) =>
        unixSeconds == 0
            ? "—"
            : Date(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime);

    public static string AvatarUrl(ushort avatar) => $"/assets/faces/player/{avatar}.png";
}

namespace Fetih.Web.Pages.Admin.Players;

using System.Globalization;

// Turkish labels and formatting shared by the player list and detail pages.
public static class PlayerText
{
    public static readonly (ushort Id, string Name)[] Statuses =
    [
        (1, "Kayıtlı"),
        (2, "Aktif"),
        (3, "Sınırlı"),
        (4, "Kilitli"),
        (5, "Yasaklı"),
    ];

    public static readonly (ushort Id, string Name)[] NonBanStatuses =
    [
        (1, "Kayıtlı"),
        (2, "Aktif"),
        (3, "Sınırlı"),
        (4, "Kilitli"),
    ];

    public static readonly (ushort Id, string Name)[] Authorities =
    [
        (1, "Oyuncu"),
        (2, "Moderatör"),
        (3, "Oyun Yöneticisi"),
        (4, "Proje Yöneticisi"),
        (5, "Yönetici"),
    ];

    public static readonly (byte Line, string Name)[] ClassLines =
    [
        (1, "Trojan"),
        (2, "Savaşçı"),
        (4, "Okçu"),
        (5, "Ninja"),
        (6, "Keşiş"),
        (7, "Korsan"),
        (10, "Taoist"),
    ];

    public static readonly (string Key, string Name)[] Sorts =
    [
        ("newest", "En yeni hesap"),
        ("level", "Seviye"),
        ("name", "İsim"),
        ("silver", "Gümüş"),
        ("cp", "CP"),
        ("pk", "PK puanı"),
    ];

    public static readonly (string Value, string Name)[] GemOptions = BuildGemOptions();

    public static string Status(ushort id) => id switch
    {
        1 => "Kayıtlı",
        2 => "Aktif",
        3 => "Sınırlı",
        4 => "Kilitli",
        5 => "Yasaklı",
        _ => $"#{id}",
    };

    public static string StatusTag(ushort id) => id switch
    {
        2 => "good",
        4 or 5 => "bad",
        3 => "info",
        _ => "",
    };

    public static string Authority(ushort id) => id switch
    {
        1 => "Oyuncu",
        2 => "Moderatör",
        3 => "Oyun Yöneticisi",
        4 => "Proje Yöneticisi",
        5 => "Yönetici",
        _ => $"#{id}",
    };

    public static string AuthorityTag(ushort id) => id switch
    {
        >= 4 => "bad",
        >= 2 => "info",
        _ => "",
    };

    public static string PkTag(ushort points) => points >= 30 ? "bad" : "";

    public static string Profession(byte cls) => cls switch
    {
        0 => "—",
        10 => "Stajyer Trojan", 11 => "Trojan", 12 => "Kıdemli Trojan",
        13 => "Kaplan Trojan", 14 => "Ejderha Trojan", 15 => "Trojan Ustası",
        20 => "Stajyer Savaşçı", 21 => "Savaşçı", 22 => "Pirinç Savaşçı",
        23 => "Gümüş Savaşçı", 24 => "Altın Savaşçı", 25 => "Savaşçı Ustası",
        40 => "Stajyer Okçu", 41 => "Okçu", 42 => "Kartal Okçu",
        43 => "Kaplan Okçu", 44 => "Ejderha Okçu", 45 => "Okçu Ustası",
        50 => "Stajyer Ninja", 51 => "Ninja", 52 => "Orta Ninja",
        53 => "Karanlık Ninja", 54 => "Mistik Ninja", 55 => "Ninja Ustası",
        60 => "Stajyer Keşiş", 61 => "Keşiş", 62 => "Dhyana Keşişi",
        63 => "Dharma Keşişi", 64 => "Prajna Keşişi", 65 => "Nirvana Keşişi",
        70 => "Stajyer Korsan", 71 => "Korsan", 72 => "Korsan Nişancı",
        73 => "Levazım Subayı", 74 => "Korsan Kaptanı", 75 => "Korsan Lordu",
        100 => "Stajyer Taoist", 101 => "Taoist",
        132 => "Su Taoisti", 133 => "Su Büyücüsü", 134 => "Su Ustası", 135 => "Su Azizi",
        142 => "Ateş Taoisti", 143 => "Ateş Büyücüsü", 144 => "Ateş Ustası", 145 => "Ateş Azizi",
        _ => $"#{cls}",
    };

    public static string Slot(byte position) => position switch
    {
        1 => "Başlık",
        2 => "Kolye",
        3 => "Zırh",
        4 => "Sağ El",
        5 => "Sol El",
        6 => "Yüzük",
        7 => "Şişe",
        8 => "Çizme",
        9 => "Kostüm",
        12 => "Binek",
        18 => "Kırbaç",
        _ => $"Yuva {position}",
    };

    public static string Socket(byte code)
    {
        if (code == 0) return "—";
        if (code == 255) return "Boş yuva";
        int kind = code / 10;
        int grade = code % 10;
        string gradeName = grade switch { 1 => "Normal", 2 => "Rafine", 3 => "Süper", _ => "" };
        string kindName = kind switch
        {
            0 => "Anka", 1 => "Ejderha", 2 => "Öfke", 3 => "Gökkuşağı",
            4 => "Kylin", 5 => "Menekşe", 6 => "Ay", 7 => "Kaplumbağa",
            10 => "Yıldırım", 12 => "Zafer",
            _ => $"#{code}",
        };
        return string.IsNullOrEmpty(gradeName) ? kindName : $"{gradeName} {kindName}";
    }

    public static string Number(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"));
    public static string Number(ulong value) => value.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"));

    public static string Error(string code) => code switch
    {
        "self_ban" => "Kendi hesabını yasaklayamazsın.",
        "self_lock" => "Kendi hesabını kilitleyemezsin.",
        "self_demotion" => "Kendi yönetici yetkini düşüremezsin.",
        "use_ban_endpoint" => "Hesap yasaklamak için Yasakla işlemini kullan.",
        "invalid_status" => "Geçersiz hesap durumu.",
        "invalid_authority" => "Geçersiz yetki seviyesi.",
        "not_banned" => "Bu hesap yasaklı değil.",
        "password_too_short" => "Şifre çok kısa.",
        "password_too_long" => "Şifre çok uzun.",
        "password_bad_chars" => "Şifre desteklenmeyen karakterler içeriyor.",
        "outranked" => "Bu hesabın yetkisi seninkine eşit ya da daha yüksek.",
        "not_found" => "Bulunamadı.",
        "offline" => "Karakter çevrimiçi değil.",
        "busy" => "Karakter şu an giriş yapıyor. Birazdan tekrar dene.",
        "invalid" => "Geçersiz işlem parametreleri.",
        "unknown_item_type" => "Bilinmeyen eşya türü.",
        "inventory_full" => "Karakterin envanteri dolu.",
        "out_of_range" => "Değer izin verilen aralığın dışında.",
        "bad_destination" => "Hedef harita veya koordinatlar yürünebilir değil.",
        "invalid_message" => "Mesaj boş olamaz ve en çok 512 karakter olabilir.",
        "no_character" => "Hesabın karakteri yok.",
        "not_in_jail" => "Karakter hapishanede değil.",
        "game_offline" => "Oyun sunucusu kapalı; işlem yapılamadı.",
        _ => "İşlem başarısız: " + code,
    };

    private static (string Value, string Name)[] BuildGemOptions()
    {
        byte[] codes = [1, 2, 3, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 42, 43, 51, 52, 53, 61, 62, 63, 71, 72, 73, 101, 102, 103, 121, 122, 123];
        var list = new List<(string, string)>
        {
            ("", "Varsayılan (eşya türünden)"),
            ("0", "Soket yok"),
            ("255", "Boş soket"),
        };
        foreach (byte c in codes)
            list.Add((c.ToString(CultureInfo.InvariantCulture), Socket(c)));
        return list.ToArray();
    }
}

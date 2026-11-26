namespace Fetih.Web.Pages.Shared;

using System.Globalization;
using Fetih.Web.Features.Items;

// Turkish labels and client-accurate formatting for items, tooltips, and shops.
public static class ItemLabels
{
    public static readonly (ItemCategory Category, string Text)[] Categories =
    [
        (ItemCategory.Weapon, "Silah"),
        (ItemCategory.Armor, "Zırh"),
        (ItemCategory.Headwear, "Başlık"),
        (ItemCategory.Necklace, "Kolye"),
        (ItemCategory.Ring, "Yüzük"),
        (ItemCategory.Boots, "Bot"),
        (ItemCategory.Shield, "Kalkan"),
        (ItemCategory.Garment, "Kostüm"),
        (ItemCategory.Steed, "Binek"),
        (ItemCategory.Crop, "Kırbaç"),
        (ItemCategory.Bottle, "Şişe"),
        (ItemCategory.Arrow, "Ok"),
        (ItemCategory.Gem, "Cevher"),
        (ItemCategory.Consumable, "Tüketilebilir"),
        (ItemCategory.Other, "Diğer"),
    ];

    public static string Category(ItemCategory category) =>
        Categories.FirstOrDefault(c => c.Category == category).Text ?? category.ToString();

    public static string Quality(ItemQuality quality) => quality switch
    {
        ItemQuality.None => "Genel",
        ItemQuality.Normal => "Normal",
        ItemQuality.Refined => "Rafine",
        ItemQuality.Unique => "Benzersiz",
        ItemQuality.Elite => "Elit",
        ItemQuality.Super => "Süper",
        _ => quality.ToString(),
    };

    // The 5619 client's item name colors (CItem::GetColorOfItemName)
    public static string QualityColor(ItemQuality quality) => quality switch
    {
        ItemQuality.Refined => "#5e87ca",
        ItemQuality.Unique => "#00ff00",
        ItemQuality.Elite => "#e804fd",
        ItemQuality.Super => "#ebe57f",
        _ => "#ffffff",
    };

    public static string? Profession(byte profession) => profession switch
    {
        0 => null,
        10 => "Stajyer Trojan",
        11 => "Trojan",
        12 => "Kıdemli Trojan",
        13 => "Kaplan Trojan",
        14 => "Ejderha Trojan",
        15 => "Trojan Ustası",
        20 => "Stajyer Savaşçı",
        21 => "Savaşçı",
        22 => "Pirinç Savaşçı",
        23 => "Gümüş Savaşçı",
        24 => "Altın Savaşçı",
        25 => "Savaşçı Ustası",
        40 => "Stajyer Okçu",
        41 => "Okçu",
        42 => "Kartal Okçu",
        43 => "Kaplan Okçu",
        44 => "Ejderha Okçu",
        45 => "Okçu Ustası",
        50 => "Stajyer Ninja",
        51 => "Ninja",
        52 => "Orta Ninja",
        53 => "Karanlık Ninja",
        54 => "Mistik Ninja",
        55 => "Ninja Ustası",
        60 => "Stajyer Keşiş",
        61 => "Keşiş",
        62 => "Dhyana Keşişi",
        63 => "Dharma Keşişi",
        64 => "Prajna Keşişi",
        65 => "Nirvana Keşişi",
        70 => "Stajyer Korsan",
        71 => "Korsan",
        72 => "Korsan Nişancı",
        73 => "Levazım Subayı",
        74 => "Korsan Kaptanı",
        75 => "Korsan Lordu",
        100 => "Stajyer Taoist",
        101 => "Taoist",
        132 => "Su Taoisti",
        133 => "Su Büyücüsü",
        134 => "Su Ustası",
        135 => "Su Azizi",
        142 => "Ateş Taoisti",
        143 => "Ateş Büyücüsü",
        144 => "Ateş Ustası",
        145 => "Ateş Azizi",
        _ => (profession >= 100 ? 10 : profession / 10) switch
        {
            1 => "Trojan",
            2 => "Savaşçı",
            4 => "Okçu",
            5 => "Ninja",
            6 => "Keşiş",
            7 => "Korsan",
            10 => "Taoist",
            _ => profession.ToString(CultureInfo.InvariantCulture),
        },
    };

    public static string? Gender(byte gender) => gender switch
    {
        1 => "Erkek",
        2 => "Kadın",
        0 => null,
        _ => gender.ToString(CultureInfo.InvariantCulture),
    };

    public static string? Socket(byte socket)
    {
        if (socket == 0)
            return null;
        if (socket == 255)
            return "Boş Yuva";
        int grade = socket % 10;
        int kind = socket / 10;
        string? gradeText = grade switch
        {
            1 => "Normal",
            2 => "Rafine",
            3 => "Süper",
            _ => null,
        };
        string? kindText = kind switch
        {
            0 => "Anka Cevheri",
            1 => "Ejderha Cevheri",
            2 => "Öfke Cevheri",
            3 => "Gökkuşağı Cevheri",
            4 => "Kylin Cevheri",
            5 => "Menekşe Cevheri",
            6 => "Ay Cevheri",
            7 => "Kaplumbağa Cevheri",
            10 => "Yıldırım Cevheri",
            12 => "Zafer Cevheri",
            _ => null,
        };
        if (gradeText == null || kindText == null)
            return $"Yuva ({socket})";

        int percent = (kind, grade) switch
        {
            (0 or 1 or 2, _) => 5 * grade,
            (3, 1) => 10,
            (3, 2) => 15,
            (3, 3) => 25,
            (4, 1) => 50,
            (4, 2) => 100,
            (4, 3) => 200,
            (5, 1) => 30,
            (5, 2) => 50,
            (5, 3) => 100,
            (6, 1) => 15,
            (6, 2) => 30,
            (6, 3) => 50,
            (7, _) => 2 * grade,
            _ => 0,
        };
        string? effect = percent > 0
            ? kind switch
            {
                0 => $"Büyü Saldırısı +{percent}%",
                1 => $"Fiziksel Saldırı +{percent}%",
                2 => $"İsabet +{percent}%",
                3 => $"Tecrübe +{percent}%",
                4 => $"Dayanıklılık +{percent}%",
                5 => $"Silah Tecrübesi +{percent}%",
                6 => $"Büyü Tecrübesi +{percent}%",
                7 => $"Alınan Hasar -{percent}%",
                _ => null,
            }
            : null;
        return effect == null ? $"{gradeText} {kindText}" : $"{gradeText} {kindText} ({effect})";
    }

    public static (string Label, string Value)? Durability(CatalogItem item)
    {
        if (item.AmountLimit == 0)
            return null;
        bool wearable = item.Category is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Headwear
            or ItemCategory.Necklace or ItemCategory.Ring or ItemCategory.Boots or ItemCategory.Shield
            or ItemCategory.Garment or ItemCategory.Steed or ItemCategory.Crop or ItemCategory.Arrow;
        if (!wearable)
            return null;
        ushort current = item.Amount > 0 ? item.Amount : item.AmountLimit;
        if (item.Category == ItemCategory.Arrow)
            return ("Ok", $"{current}/{item.AmountLimit}");
        int max = item.AmountLimit / 100;
        if (max <= 0)
            return null;
        return ("Dayanıklılık", $"{current / 100}/{max}");
    }

    public static string Currency(string? currency) => currency switch
    {
        "silver" => "Gümüş",
        "cp" => "CP",
        "bound_cp" => "Bağlı CP",
        "honor" => "Onur",
        "race_points" => "Yarış Puanı",
        null or "" => "—",
        _ => currency,
    };

    public static string SpellPower(int power) =>
        power >= 30000
            ? $"{power - 30000}%"
            : power.ToString(CultureInfo.InvariantCulture);
}

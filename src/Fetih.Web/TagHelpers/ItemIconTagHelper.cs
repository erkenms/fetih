namespace Fetih.Web.TagHelpers;

using System.Globalization;
using System.Net;
using System.Text;
using Fetih.Web.Features.Items;
using Fetih.Web.Pages.Shared;
using Microsoft.AspNetCore.Razor.TagHelpers;

// <item-icon item="1088000" />: the item's icon, with its name and stats in a tooltip on hover
public class ItemIconTagHelper(ItemCatalog catalog) : TagHelper
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public uint Item { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "item-icon");
        if (!catalog.TryGet(this.Item, out var item) || item == null)
        {
            output.Content.SetHtmlContent(
                $"<img src=\"/assets/icons/items/default.png\" alt=\"\"><span class=\"tip\">#{this.Item}</span>");
            return;
        }

        string color = ItemLabels.QualityColor(item.Quality);
        var tip = new StringBuilder($"<b style=\"color:{color}\">{WebUtility.HtmlEncode(item.Name)}</b>");
        void Line(string text) => tip.Append("<br>").Append(WebUtility.HtmlEncode(text));
        void Pair(string label, object value) => Line($"{label}: {value}");

        Pair("Tür", item.Type ?? ItemLabels.Category(item.Category));
        if (item.RequiredLevel > 0)
            Pair("Seviye", item.RequiredLevel);
        if (ItemLabels.Profession(item.RequiredProfession) is { } prof)
            Pair("Sınıf", prof);
        if (ItemLabels.Gender(item.RequiredGender) is { } gender)
            Pair("Cinsiyet", gender);
        if (item.RequiredWeaponSkill > 0)
            Pair("Silah Becerisi", item.RequiredWeaponSkill);
        if (item.RequiredStrength > 0)
            Pair("Güç", item.RequiredStrength);
        if (item.RequiredAgility > 0)
            Pair("Çeviklik", item.RequiredAgility);
        if (item.RequiredVitality > 0)
            Pair("Dayanıklılık", item.RequiredVitality);
        if (item.RequiredSpirit > 0)
            Pair("Ruh", item.RequiredSpirit);

        if (item.MinAttack > 0 || item.MaxAttack > 0)
            Pair("Saldırı", $"{item.MinAttack}–{item.MaxAttack}");
        if (item.MagicAttack > 0)
            Pair("Büyü Saldırısı", $"+{item.MagicAttack}");
        if (item.Defense > 0)
            Pair("Savunma", $"+{item.Defense}");
        if (item.MagicDefense > 0)
            Pair("Büyü Savunması", $"+{item.MagicDefense}");
        if (item.Life > 0)
            Pair("Maks Can", $"+{item.Life}");
        if (item.Mana > 0)
            Pair("Maks Mana", $"+{item.Mana}");
        if (item.Dexterity > 0)
            Pair("Çeviklik", $"+{item.Dexterity}");
        if (item.Dodge > 0)
            Pair("Kaçınma", $"+{item.Dodge}");
        if (item.Category == ItemCategory.Weapon && item.AttackRange > 0)
            Pair("Saldırı Menzili", item.AttackRange);
        if (item.Category == ItemCategory.Weapon && item.AttackSpeed > 0)
            Pair("Saldırı Hızı", item.AttackSpeed);

        if (ItemLabels.Durability(item) is var (durLabel, durValue))
            Pair(durLabel, durValue);
        if (ItemLabels.Socket(item.Gem1) is { } gem1)
            Line(gem1);
        if (ItemLabels.Socket(item.Gem2) is { } gem2)
            Line(gem2);
        if (item.Price > 0)
            Line($"Fiyat: {item.Price.ToString("N0", Tr)} Gümüş");
        if (item.EmoneyPrice > 0)
            Line($"CP Fiyatı: {item.EmoneyPrice.ToString("N0", Tr)} CP");
        if (item.StackLimit > 1)
            Pair("Maks Yığın", item.StackLimit);
        if (!string.IsNullOrWhiteSpace(item.Description))
            tip.Append("<div class=\"tip-desc\">").Append(WebUtility.HtmlEncode(item.Description)).Append("</div>");

        string icon = string.IsNullOrEmpty(item.Icon) ? "/assets/icons/items/default.png" : "/assets/" + item.Icon;
        output.Content.SetHtmlContent(
            $"<img src=\"{WebUtility.HtmlEncode(icon)}\" alt=\"\"><span class=\"tip\">{tip}</span>");
    }
}

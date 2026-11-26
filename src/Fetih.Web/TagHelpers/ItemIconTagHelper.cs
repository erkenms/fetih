namespace Fetih.Web.TagHelpers;

using System.Net;
using System.Text;
using Fetih.Web.Features.Items;
using Microsoft.AspNetCore.Razor.TagHelpers;

// <item-icon item="1088000" />: the item's icon, with its name and stats in a tooltip on hover
public class ItemIconTagHelper(ItemCatalog catalog) : TagHelper
{
    public uint Item { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "item-icon");
        if (!catalog.TryGet(this.Item, out var item) || item == null)
        {
            output.Content.SetHtmlContent($"<img src=\"\" alt=\"\"><span class=\"tip\">#{this.Item}</span>");
            return;
        }

        var tip = new StringBuilder($"<b>{WebUtility.HtmlEncode(item.Name)}</b> #{item.Id}");
        void Line(string label, object value) =>
            tip.Append("<br>").Append(label).Append(": ").Append(WebUtility.HtmlEncode(value.ToString()));
        if (item.Type != null)
            Line("Tür", item.Type);
        if (item.MaxAttack > 0)
            Line("Saldırı", $"{item.MinAttack}-{item.MaxAttack}");
        if (item.MagicAttack > 0)
            Line("Büyü saldırısı", item.MagicAttack);
        if (item.Defense > 0)
            Line("Savunma", item.Defense);
        if (item.MagicDefense > 0)
            Line("Büyü savunması", item.MagicDefense);
        if (item.RequiredLevel > 0)
            Line("Gereken seviye", item.RequiredLevel);
        if (item.Price > 0)
            Line("Fiyat", item.Price);
        string icon = item.Icon == null ? "" : "/assets/" + item.Icon;
        output.Content.SetHtmlContent(
            $"<img src=\"{WebUtility.HtmlEncode(icon)}\" alt=\"\"><span class=\"tip\">{tip}</span>");
    }
}

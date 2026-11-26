namespace Fetih.Web.TagHelpers;

using Microsoft.AspNetCore.Razor.TagHelpers;

// <panel title="Duyurular" icon="2000247">…</panel>: a box with a leather title bar and a
// parchment body. The icon is a file in /assets/icons/items/.
public class PanelTagHelper : TagHelper
{
    public string Title { get; set; } = "";

    public string? Icon { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "section";
        output.Attributes.SetAttribute("class", "box");
        var title = new System.Text.StringBuilder("<div class=\"box-title\">");
        if (this.Icon != null)
            title.Append($"<img src=\"/assets/icons/items/{Encode(this.Icon)}.png\" alt=\"\">");
        title.Append(Encode(this.Title)).Append("</div><div class=\"box-body\">");
        output.PreContent.SetHtmlContent(title.ToString());
        output.Content.SetHtmlContent(await output.GetChildContentAsync());
        output.PostContent.SetHtmlContent("</div>");
    }

    private static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);
}

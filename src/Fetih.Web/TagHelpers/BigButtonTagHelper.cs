namespace Fetih.Web.TagHelpers;

using Microsoft.AspNetCore.Razor.TagHelpers;

// <big-button kind="gold" href="/indir" note="İstemci, sürüm 5619">Oyunu İndir</big-button>:
// the big glossy link, gold for download and red for sign-up.
public class BigButtonTagHelper : TagHelper
{
    public string Kind { get; set; } = "red";

    public string Href { get; set; } = "#";

    public string? Note { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "big " + this.Kind);
        output.Attributes.SetAttribute("href", this.Href);
        output.PreContent.SetHtmlContent("<b>");
        output.Content.SetHtmlContent(await output.GetChildContentAsync());
        output.PostContent.SetHtmlContent(this.Note == null ? "</b>"
            : $"</b><span>{System.Net.WebUtility.HtmlEncode(this.Note)}</span>");
    }
}

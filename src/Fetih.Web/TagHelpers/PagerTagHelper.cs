namespace Fetih.Web.TagHelpers;

using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

// <pager page="Model.Page" pages="Model.Pages" />: « Önceki 1 2 3 … Sonraki », as links to
// the same URL with ?sayfa=N and the rest of the query string kept. Pages count from 1.
public class PagerTagHelper : TagHelper
{
    public const string Parameter = "sayfa";

    public int Page { get; set; } = 1;

    public int Pages { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "pager");
        if (this.Pages <= 1)
        {
            output.SuppressOutput();
            return;
        }

        var html = new StringBuilder();
        if (this.Page > 1)
            html.Append(this.Link(this.Page - 1, "« Önceki")).Append(' ');
        int last = 0;
        foreach (int n in Enumerable.Range(1, this.Pages)
                     .Where(n => n <= 2 || n > this.Pages - 2 || Math.Abs(n - this.Page) <= 2))
        {
            if (n > last + 1)
                html.Append("<span>…</span>");
            html.Append(n == this.Page ? $"<span class=\"on\">{n}</span>" : this.Link(n, n.ToString()));
            last = n;
        }
        if (this.Page < this.Pages)
            html.Append(' ').Append(this.Link(this.Page + 1, "Sonraki »"));
        output.Content.SetHtmlContent(html.ToString());
    }

    private string Link(int page, string text)
    {
        var request = this.ViewContext.HttpContext.Request;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.QueryString.Value);
        var builder = new QueryBuilder(query.Where(q => q.Key != Parameter && q.Key != "handler"));
        builder.Add(Parameter, page.ToString());
        return $"<a href=\"{System.Net.WebUtility.HtmlEncode(request.Path + builder.ToQueryString())}\">{text}</a>";
    }
}

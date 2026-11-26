namespace Fetih.Web.Pages;

using Microsoft.AspNetCore.Mvc.RazorPages;

// The one-line message after a post: a handler sets it and redirects, the layout shows it
// once (Pages/Shared/_Flash.cshtml).
public static class FlashMessages
{
    public const string Key = "flash";
    public const string ErrorKey = "flash-error";

    public static void Flash(this PageModel page, string text) => page.TempData[Key] = text;

    public static void FlashError(this PageModel page, string text) => page.TempData[ErrorKey] = text;
}

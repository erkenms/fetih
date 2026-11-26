namespace Fetih.Web.Pages;

using System.Reflection;
using Fetih.Web.Auth;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

// Runs around every page handler: checks [Requires] on the page model and the handler, and
// turns GameOfflineException into the "oyun sunucusu kapalı" box instead of an error page.
public sealed class PageChecks(WebConfiguration config) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var requirements = new[]
        {
            context.HandlerInstance.GetType().GetCustomAttribute<RequiresAttribute>(),
            context.HandlerMethod?.MethodInfo.GetCustomAttribute<RequiresAttribute>(),
        };
        foreach (var requires in requirements.OfType<RequiresAttribute>())
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();
                return;
            }

            if ((requires.Permission is { } permission && !user.Has(permission)) ||
                (requires.StudioWrite && config.Web.StudioReadOnly))
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        var executed = await next();
        if (executed.Exception is GameOfflineException)
        {
            executed.ExceptionHandled = true;
            var request = context.HttpContext.Request;
            if (HttpMethods.IsGet(request.Method))
            {
                // Shown by Pages/Hata with the red box
                executed.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                return;
            }

            if (executed.HandlerInstance is Microsoft.AspNetCore.Mvc.RazorPages.PageModel page)
                page.FlashError("Oyun sunucusu kapalı; işlem yapılamadı.");
            executed.Result = new RedirectResult(request.PathBase + request.Path);
        }
    }
}

# Web pages

Razor Pages in `src/Fetih.Web`, rendered on the server, Turkish text written in the page.
React serves every URL without a Razor page; moving one deletes its line in
`web/src/app/routes.tsx`. Copy Broadcasts: `Pages/Admin/Broadcasts/`.

## A page

- `Index.cshtml` shows, `Index.cshtml.cs` loads (`OnGet`) and acts (`OnPost…`). The work is a
  service in `Features/` (`BroadcastService`). The folder is the URL. Public URLs and query
  names are Turkish (`/siralama?sayfa=2`); admin URLs stay English.
- A button is `<form method="post" asp-page-handler="Delete">` (always `asp-page-handler`:
  it adds the anti-forgery token). The handler acts, then
  `this.Flash("Duyuru silindi."); return this.RedirectToPage();` (`FlashError` for failures).
- Wrong input: reload the data and `return this.Page()` with the error, so the form keeps it.
- Times: `WebTime` (Turkey's time on the page, UTC in the database).
- Don't catch `GameOfflineException`: a GET shows the red "oyun sunucusu kapalı" box, a POST
  comes back with that flash. `NotFound()`/`Forbid()` render `Pages/Hata`.

## Permissions

`/admin` needs `AdminArea`; signed out goes to `/giris`. More is `[Requires]`, on the page model
(all its GETs) and on **every** `OnPost…` (`PagesTests` fails without it):

```csharp
[Requires(Permission.Moderate)]
public async Task<IActionResult> OnPostDeleteAsync(uint id)

[Requires(Permission.Studio, StudioWrite = true)]   // also refused when StudioReadOnly
```

`[Requires]` alone: any signed-in user. `[AllowAnonymous]` pages are exempt. Checks on the
target (rank, ownership) go in the handler. Hide buttons with `User.Has(...)`.

## Pieces

- `<panel title="…" icon="2000247">` (icon from `/assets/icons/items/`),
  `<big-button kind="gold|red" href note>`, `<pager page pages>` (`?sayfa=N`),
  `<item-icon item="@id">`.
- Plain `<table class="grid">`, `<button>` (`class="plain"` for the quiet one), `.form-row`,
  tabs as `<nav class="tabs"><a class="on" href="?sekme=…">`, `<span class="tag good">`.
- `<dialog id="x">` opened by `data-dialog="x"`, closed by `data-close`, reopened after a
  failed post with `class="open"`. Dangerous forms: `data-confirm="… silinsin mi?"`.
- htmx swaps a piece of the page: `hx-get="@Url.Page("Index", "Queue")"` →
  `OnGetQueueAsync` → `this.Partial("_Queue", this)`.
- Pickers post an ID:
  `<partial name="_Picker" model='new PickerField(PickerKind.Item, "ItemId", Model.ItemId)' />`
  (`Item`, `Monster`, `Look`, `Map`).
- CSS: `wwwroot/css/site.css` (shared) and `admin.css`, by what a thing is, not by page. No
  `<style>` blocks. Script: `wwwroot/js/site.js`; a page that needs more gets
  `wwwroot/js/<page>.js`. No npm.

## Tests

`fixture.SignInAsync(...)`, then `WebFixture.PostFormAsync(...)` (it fetches the token). Test the
403 for a player and each action: `BroadcastsPageTests`.

`cd src/Fetih.Web && dotnet watch`, open `:5820`.

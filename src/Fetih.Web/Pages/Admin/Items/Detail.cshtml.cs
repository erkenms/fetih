namespace Fetih.Web.Pages.Admin.Items;

using Fetih.Web.Auth;
using Fetih.Web.Features.Items;
using Fetih.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class DetailModel(ItemCatalog catalog) : PageModel
{
    public CatalogItem Item { get; private set; } = null!;

    // Everything ItemCatalog knows, zero values left out
    public (string Label, string Value)[] Rows { get; private set; } = [];

    public IActionResult OnGet(long id)
    {
        if (id is < 0 or > uint.MaxValue || !catalog.TryGet((uint)id, out var item) || item == null)
            return this.NotFound();
        this.Item = item;
        var rows = new List<(string, string)>
        {
            ("Tür", ItemLabels.Category(item.Category) + (item.Type == null ? "" : " / " + item.Type)),
            ("Kalite", ItemLabels.Quality(item.Quality)),
        };
        void Add(string label, object? value)
        {
            string text = value?.ToString() ?? "";
            if (text is not ("0" or ""))
                rows.Add((label, text));
        }

        Add("Gereken meslek", ItemLabels.Profession(item.RequiredProfession));
        Add("Gereken silah becerisi", item.RequiredWeaponSkill);
        Add("Gereken seviye", item.RequiredLevel);
        Add("Gereken cinsiyet", ItemLabels.Gender(item.RequiredGender));
        Add("Gereken güç", item.RequiredStrength);
        Add("Gereken çeviklik", item.RequiredAgility);
        Add("Gereken dayanıklılık", item.RequiredVitality);
        Add("Gereken ruh", item.RequiredSpirit);
        Add("Devir yasağı", item.Monopoly == 0 ? null : $"Bağlı ({item.Monopoly})");
        Add("Fiyat", item.Price);
        Add("CP fiyatı", item.EmoneyPrice);
        Add("Eylem", item.ActionId);
        Add("Saldırı", item.MinAttack > 0 || item.MaxAttack > 0 ? $"{item.MinAttack}-{item.MaxAttack}" : null);
        Add("Büyü saldırısı", item.MagicAttack);
        Add("Savunma", item.Defense);
        Add("Büyü savunması", item.MagicDefense);
        Add("Hassasiyet", item.Dexterity);
        Add("Kaçınma", item.Dodge);
        Add("Can", item.Life);
        Add("Mana", item.Mana);
        Add("Miktar", item.Amount);
        Add("Azami miktar", item.AmountLimit);
        Add("Taş 1", ItemLabels.Socket(item.Gem1));
        Add("Taş 2", ItemLabels.Socket(item.Gem2));
        Add("Menzil", item.AttackRange);
        Add("Saldırı hızı", item.AttackSpeed);
        Add("Yığın sınırı", item.StackLimit);
        Add("Simge", item.Icon);
        Add("Zemin simgesi", item.GroundIcon);
        this.Rows = [.. rows];
        return this.Page();
    }
}

namespace Fetih.Web.Pages.Shared;

using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Items;
using Fetih.Web.Features.Looks;

public enum PickerKind { Item, Monster, Look, Map }

// One picker in a form: <partial name="_Picker" model="new PickerField(PickerKind.Item, "ItemId", Model.ItemId)" />.
// The form posts the chosen ID under Name.
public sealed record PickerField(PickerKind Kind, string Name, uint? Value);

// Image is a path under /assets
public sealed record PickerOption(uint Value, string Label, string? Image);

// What the pickers find as you type: an ID, or part of a name
public sealed class PickerSearch(ItemCatalog items, CatalogStore catalogs, LookStore looks)
{
    public const int Limit = 20;

    public IEnumerable<PickerOption> Find(PickerKind kind, string? q)
    {
        q = q?.Trim() ?? "";
        uint.TryParse(q, out uint id);
        bool Match(uint value, string? name) =>
            q.Length == 0 || value == id || (name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

        return (kind switch
        {
            PickerKind.Item => items.Search(q, null, null, null, null, null, null, 1, Limit).Items
                .Select(i => new PickerOption(i.Id, $"{i.Name} #{i.Id}", i.Icon)),
            PickerKind.Monster => catalogs.Monsters.Where(m => m.InServer && Match(m.Id, m.Name))
                .Select(m => new PickerOption(m.Id, $"{m.Name} #{m.Id}", this.MonsterImage(m.Lookface))),
            PickerKind.Look => looks.Looks.Npc.Select(n => new PickerOption(n.Lookface, $"NPC {n.Lookface} {n.Name}", n.Image))
                .Concat(looks.Looks.Monster.Select(m => new PickerOption(m.Look,
                    $"Canavar {m.Look} {string.Join(", ", m.Monsters.Select(x => x.Name).Distinct())}", m.Image)))
                .Where(o => Match(o.Value, o.Label)),
            PickerKind.Map => catalogs.Maps
                .Select(m => new PickerOption(m.Id, $"{m.Name ?? m.ServerName} #{m.Id}", null))
                .Where(o => Match(o.Value, o.Label)),
            _ => [],
        }).Take(Limit);
    }

    public string? LabelOf(PickerKind kind, uint? value) =>
        value == null ? null : this.Find(kind, value.ToString()).FirstOrDefault(o => o.Value == value)?.Label
            ?? $"#{value}";

    private string? MonsterImage(uint? lookface) =>
        lookface == null ? null : looks.Looks.Monster.FirstOrDefault(m => m.Look == lookface)?.Image;
}

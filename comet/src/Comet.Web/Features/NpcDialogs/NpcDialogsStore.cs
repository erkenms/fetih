namespace Comet.Web.Features.NpcDialogs;

using System.Globalization;
using System.Reflection;
using Comet.Game.Npcs;
using Comet.Game.World;
using Comet.Web.Features.Catalogs;
using Comet.Web.Features.Maps;

public sealed class NpcDialogsStore
{
    private readonly MapDataStore maps;
    private readonly CatalogStore catalogs;
    private readonly string? gameSourceDir;

    public NpcDialogsStore(WebConfiguration config, MapDataStore maps, CatalogStore catalogs)
        : this(maps, catalogs, StudioData.ResolveDataDirectory(config) is { } data ? Path.GetDirectoryName(data.TrimEnd('/', '\\')) : null)
    {
    }

    internal NpcDialogsStore(MapDataStore maps, CatalogStore catalogs, string? gameSourceDir)
    {
        this.maps = maps;
        this.catalogs = catalogs;
        this.gameSourceDir = gameSourceDir;
    }

    public NpcDialogSummary[] List(IReadOnlyDictionary<uint, int>? talks)
    {
        var shops = this.ShopItemCounts();
        return this.maps.AllNpcs()
            .Select(n => Summary(n, Kind(n, shops), talks == null ? null : talks.GetValueOrDefault(n.Npc.Id)))
            .OrderBy(n => n.MapId).ThenBy(n => n.NpcId)
            .ToArray();
    }

    public async Task<NpcDialogDetailResponse?> GetAsync(uint npcId, NpcTalkStats stats)
    {
        var shops = this.ShopItemCounts();
        var npc = this.maps.AllNpcs().FirstOrDefault(n => n.Npc.Id == npcId);
        if (npc == null)
            return null;

        string kind = Kind(npc, shops);
        string? handler = null, sourceFile = null;
        NpcDialogPage[] pages = [];
        switch (kind)
        {
            case "script":
                var method = Dialogs.Scripted[npcId].Method;
                string typeName = method.DeclaringType?.Name ?? "";
                handler = $"{typeName}.{method.Name}";
                if (NpcScriptExtractor.FindSourceFile(this.gameSourceDir, typeName) is { } path)
                {
                    sourceFile = Path.GetRelativePath(this.gameSourceDir!, path);
                    pages = NpcScriptExtractor.FromSource(await File.ReadAllTextAsync(path), method.Name,
                        name => Constant(method.DeclaringType, name));
                }
                break;
            case "teleporter":
                pages = await PreviewAsync(talk => Teleporters.TalkAsync(talk, npc.Teleporter!));
                foreach (var page in pages)
                    for (int i = 0; i < page.Options.Length; i++)
                        if (page.Options[i] is { Kind: "choice", Option: byte number } option && number <= npc.Teleporter!.Destinations.Count)
                        {
                            var to = npc.Teleporter.Destinations[number - 1];
                            page.Options[i] = option with
                            {
                                TargetMapId = to.MapID,
                                TargetMapName = this.catalogs.MapDisplayName(to.MapID),
                                TargetX = to.X,
                                TargetY = to.Y,
                            };
                        }
                break;
            case "talker":
                pages = await PreviewAsync(talk => Talkers.TalkAsync(talk, npc.Talker!));
                break;
            case "default":
                pages = await PreviewAsync(Dialogs.DefaultAsync);
                break;
        }

        var faces = this.catalogs.Faces.Npc.ToDictionary(f => f.Id, f => f.Path);
        pages = [.. pages.Select(p => p with { FacePath = faces.GetValueOrDefault((uint)p.Face) })];
        return new NpcDialogDetailResponse(Summary(npc, kind, stats.LokiAvailable ? stats.TotalTalks : null),
            npc.Npc.Role, handler, sourceFile, npc.Teleporter?.Fare, shops.TryGetValue(npcId, out int items) ? items : null,
            pages, stats);
    }

    private static int? Constant(Type? type, string name) =>
        type?.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) is { IsLiteral: true } field
            ? Convert.ToInt32(field.GetRawConstantValue(), CultureInfo.InvariantCulture)
            : null;

    private static async Task<NpcDialogPage[]> PreviewAsync(Func<NpcTalk, Task> dialog)
    {
        var talk = NpcTalk.Preview();
        await dialog(talk);
        return NpcScriptExtractor.FromPreview(talk);
    }

    // The order Dialogs.RunAsync checks them in
    private static string Kind(MapNpcData npc, IReadOnlyDictionary<uint, int> shops) =>
        Dialogs.Has(npc.Npc.Id) ? "script"
        : npc.Teleporter != null ? "teleporter"
        : npc.Talker != null ? "talker"
        : shops.ContainsKey(npc.Npc.Id) ? "shop"
        : npc.Npc.Role == Npc.StorageRole ? "warehouse"
        : "default";

    private static NpcDialogSummary Summary(MapNpcData npc, string kind, int? talks) => new(
        npc.Npc.Id, npc.Npc.Name, npc.MapId, npc.MapName, npc.Npc.X, npc.Npc.Y, npc.Npc.Lookface, npc.Npc.FacePath,
        kind, talks);

    private Dictionary<uint, int> ShopItemCounts() => this.catalogs.Shops
        .Where(s => s.NpcId.HasValue)
        .GroupBy(s => s.NpcId!.Value)
        .ToDictionary(g => g.Key, g => g.First().ItemCount);
}

namespace Fetih.Web.Pages.Admin.Maps;

using System.Text.Json;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.Catalogs;
using Fetih.Web.Features.Looks;
using Fetih.Web.Features.MapEdit;
using Fetih.Web.Features.Maps;
using Fetih.Web.Features.NpcDialogs;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class DetailModel(
    MapDataStore mapData,
    MapEditService mapEdit,
    LookStore looks,
    CatalogStore catalogs,
    NpcDialogsStore npcDialogs,
    WebConfiguration config,
    GameRpc gameRpc,
    AuditLog audit) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly (byte Role, string Label)[] RolePresets =
    [
        (1, "1 · Dükkân"),
        (2, "2 · Konuşmacı / Görev"),
        (3, "3 · Depo"),
        (5, "5 · Eşya yükseltme"),
        (10, "10 · Kumaş / Boya"),
        (14, "14 · Lonca"),
        (19, "19 · Sandık"),
        (26, "26 · Arazi NPC"),
    ];

    public uint MapId { get; private set; }

    public MapDetail Map { get; private set; } = null!;

    public MapEditResponse Edit { get; private set; } = null!;

    public string DisplayName => this.Map.Name ?? this.Map.ServerName ?? $"Harita #{this.MapId}";

    public bool CanEdit => this.User.Has(Permission.Studio) && !config.Web.StudioReadOnly;

    public bool GameConnected => gameRpc.Connected;

    public string MapDataJson { get; private set; } = "{}";

    // Side panel selection state
    public string PanelKind { get; private set; } = "map";

    public EditableNpc? SelectedNpc { get; private set; }

    public MapNpcEntry? SelectedGoal { get; private set; }

    public NpcDialogDetailResponse? SelectedNpcDialog { get; private set; }

    public EditableSpawn? SelectedSpawn { get; private set; }

    public EditablePortal? SelectedPortal { get; private set; }

    public bool IsNewEntity { get; private set; }

    public string? PanelNotice { get; private set; }

    public string? PanelError { get; private set; }

    public MapEditProblemDto[] PanelProblems { get; private set; } = [];

    // Included in partial responses so map.js can sync markers without a full reload
    public string? SyncDataJson { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, string? type, uint? entityId)
    {
        if (!this.TryLoadMap(id))
            return this.NotFound();

        await this.SelectPanelAsync(type, entityId, null, null);
        this.MapDataJson = this.SerializeMapData();
        return this.Page();
    }

    public IActionResult OnGetGrid(long id)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();
        var grid = mapData.GetMapGrid((uint)id);
        return grid == null ? this.NotFound() : new JsonResult(grid, JsonOptions);
    }

    public async Task<IActionResult> OnGetPanelAsync(long id, string? type, uint? entityId, ushort? x, ushort? y, string? error)
    {
        if (!this.TryLoadMap(id))
            return this.NotFound();

        this.PanelError = error;
        await this.SelectPanelAsync(type, entityId, x, y);
        return this.Partial("_Panel", this);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostMoveAsync(long id, string kind, uint entityId, ushort x, ushort y, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        SingleEditResult result = kind switch
        {
            "npc" => await mapEdit.MoveNpcAsync(mapId, entityId, x, y, audit, ct),
            "spawn" => await mapEdit.MoveSpawnAsync(mapId, entityId, x, y, audit, ct),
            "entry" => await mapEdit.MoveEntryPointAsync(mapId, x, y, audit, ct),
            _ => new SingleEditResult(false, null, [], Error: "invalid_kind"),
        };

        return new JsonResult(result, JsonOptions);
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveSettingsAsync(long id, MapSettingsForm form, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var aliases = (form.Aliases ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        MapCellPoint? portal = form.PortalX.HasValue && form.PortalY.HasValue
            ? new MapCellPoint(form.PortalX.Value, form.PortalY.Value)
            : null;

        var settings = new EditableMapSettings(
            InMapsJson: true,
            Name: form.Name,
            Aliases: aliases,
            Flags: form.Flags ?? [],
            Portal: portal,
            RebornMap: form.RebornMap,
            ScrollTypeId: form.ScrollTypeId,
            RidePlus: form.RidePlus);

        var result = await mapEdit.SaveSettingsAsync(mapId, settings, audit, ct);
        return await this.RespondAfterEditAsync(mapId, result, "map", null, "Harita ayarları kaydedildi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveNpcAsync(long id, NpcForm form, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var editable = new EditableNpc(form.Id, form.Lookface, form.Role, form.X, form.Y, form.Life, form.Level);
        var result = await mapEdit.SaveNpcAsync(mapId, form.OriginalId, editable, audit, ct);

        if (!result.Saved && this.IsHtmx())
        {
            this.TryLoadMap(id);
            this.PanelKind = "npc";
            this.IsNewEntity = form.OriginalId == null;
            this.SelectedNpc = editable;
            this.SetEditFailure(result);
            return this.Partial("_Panel", this);
        }

        return await this.RespondAfterEditAsync(mapId, result, "npc", result.EntityId ?? form.Id,
            form.OriginalId == null ? "NPC eklendi." : "NPC kaydedildi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostDeleteNpcAsync(long id, uint npcId, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var result = await mapEdit.DeleteNpcAsync(mapId, npcId, audit, ct);
        return await this.RespondAfterEditAsync(mapId, result, "map", null, $"NPC #{npcId} silindi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveSpawnAsync(long id, SpawnForm form, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var editable = new EditableSpawn(form.Id, form.X, form.Y, form.Spread, form.Amount, form.Respawn, form.MonsterId);
        var result = await mapEdit.SaveSpawnAsync(mapId, form.OriginalId, editable, audit, ct);

        if (!result.Saved && this.IsHtmx())
        {
            this.TryLoadMap(id);
            this.PanelKind = "spawn";
            this.IsNewEntity = form.OriginalId == null;
            this.SelectedSpawn = editable;
            this.SetEditFailure(result);
            return this.Partial("_Panel", this);
        }

        return await this.RespondAfterEditAsync(mapId, result, "spawn", result.EntityId ?? form.Id,
            form.OriginalId == null ? "Doğma noktası eklendi." : "Doğma noktası kaydedildi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostDeleteSpawnAsync(long id, uint spawnId, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var result = await mapEdit.DeleteSpawnAsync(mapId, spawnId, audit, ct);
        return await this.RespondAfterEditAsync(mapId, result, "map", null, $"Doğma noktası #{spawnId} silindi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSavePortalAsync(long id, PortalForm form, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var result = await mapEdit.SavePortalAsync(mapId, form.OriginalIndex, form.PortalIndex,
            form.ToMap, form.ToX, form.ToY, audit, ct);

        if (!result.Saved && this.IsHtmx())
        {
            this.TryLoadMap(id);
            this.PanelKind = "portal";
            this.IsNewEntity = form.OriginalIndex == null;
            this.SelectedPortal = new EditablePortal(form.PortalIndex, null, null, form.ToMap, null, form.ToX, form.ToY);
            this.SetEditFailure(result);
            return this.Partial("_Panel", this);
        }

        return await this.RespondAfterEditAsync(mapId, result, "portal", form.PortalIndex, "Geçit kaydedildi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostDeletePortalAsync(long id, uint portalIndex, CancellationToken ct)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        uint mapId = (uint)id;
        var result = await mapEdit.DeletePortalAsync(mapId, portalIndex, audit, ct);
        return await this.RespondAfterEditAsync(mapId, result, "map", null, $"Geçit #{portalIndex} silindi.");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostReloadAsync(long id)
    {
        if (id is < 0 or > uint.MaxValue)
            return this.NotFound();

        var errors = new List<string>();
        foreach (string file in MapEditService.MapDataFiles)
        {
            var res = await StudioData.ReloadFileAsync(file, gameRpc, audit);
            if (!res.Applied)
                errors.Add($"{file}: {string.Join("; ", res.Errors)}");
        }

        if (errors.Count > 0)
            this.FlashError("Yükleme hatası: " + string.Join(" | ", errors));
        else
            this.Flash("Harita verileri oyuna yüklendi.");

        return this.RedirectToPage(new { id });
    }

    private async Task<IActionResult> RespondAfterEditAsync(
        uint mapId,
        SingleEditResult result,
        string nextPanelKind,
        uint? nextEntityId,
        string successMessage)
    {
        if (this.IsHtmx())
        {
            this.TryLoadMap(mapId);
            if (result.Saved)
            {
                await this.SelectPanelAsync(nextPanelKind, nextEntityId, null, null);
                this.PanelNotice = successMessage;
                this.PanelProblems = result.Problems;
                this.SyncDataJson = this.SerializeMapData();
            }
            else
            {
                await this.SelectPanelAsync(nextPanelKind, nextEntityId, null, null);
                this.SetEditFailure(result);
            }
            return this.Partial("_Panel", this);
        }

        if (result.Saved)
            this.Flash(successMessage);
        else
            this.FlashError(FormatFailure(result));

        return this.RedirectToPage(new { id = mapId, type = nextPanelKind == "map" ? null : nextPanelKind, entityId = nextEntityId });
    }

    private void SetEditFailure(SingleEditResult result)
    {
        this.PanelProblems = result.Problems;
        this.PanelError = FormatFailure(result);
    }

    private static string FormatFailure(SingleEditResult result)
    {
        if (result.Error == "not_found")
            return "Kayıt bulunamadı; silinmiş olabilir.";
        var first = result.Problems.FirstOrDefault(p => p.Level == "error");
        return first != null ? first.Message : "Kayıt doğrulanamadı.";
    }

    private bool IsHtmx() => this.Request.Headers.ContainsKey("HX-Request");

    private bool TryLoadMap(long id)
    {
        if (id is < 0 or > uint.MaxValue)
            return false;
        uint mapId = (uint)id;
        var detail = mapData.GetMapDetail(mapId);
        if (detail == null)
            return false;

        this.MapId = mapId;
        this.Map = detail;
        this.Edit = mapEdit.ReadMap(mapId);
        return true;
    }

    private async Task SelectPanelAsync(string? type, uint? entityId, ushort? x, ushort? y)
    {
        switch (type)
        {
            case "npc" when entityId.HasValue:
                this.SelectedNpc = this.Edit.Npcs.FirstOrDefault(n => n.Id == entityId.Value);
                if (this.SelectedNpc != null)
                {
                    this.PanelKind = "npc";
                    this.SelectedNpcDialog = await npcDialogs.GetAsync(entityId.Value,
                        new NpcTalkStats(false, 0, 0, null, []));
                }
                else
                {
                    this.PanelKind = "map";
                }
                break;

            case "new-npc":
                this.PanelKind = "npc";
                this.IsNewEntity = true;
                this.SelectedNpc = new EditableNpc(this.Edit.NextNpcId, 20, 2, x ?? 0, y ?? 0, 0, 0);
                break;

            case "goal" when entityId.HasValue:
                this.SelectedGoal = this.Map.Npcs.FirstOrDefault(n => n.IsGoal && n.Id == entityId.Value);
                this.PanelKind = this.SelectedGoal != null ? "goal" : "map";
                break;

            case "spawn" when entityId.HasValue:
                this.SelectedSpawn = this.Edit.Spawns.FirstOrDefault(s => s.Id == entityId.Value);
                this.PanelKind = this.SelectedSpawn != null ? "spawn" : "map";
                break;

            case "new-spawn":
                this.PanelKind = "spawn";
                this.IsNewEntity = true;
                this.SelectedSpawn = new EditableSpawn(this.Edit.NextSpawnId, x ?? 0, y ?? 0, 10, 5, 15, 0);
                break;

            case "portal" when entityId.HasValue:
                this.SelectedPortal = this.Edit.Portals.FirstOrDefault(p => p.Index == entityId.Value);
                this.PanelKind = this.SelectedPortal != null ? "portal" : "map";
                break;

            case "new-portal":
                uint nextIdx = this.Edit.Portals.Length > 0 ? this.Edit.Portals.Max(p => p.Index) + 1 : 0;
                this.PanelKind = "portal";
                this.IsNewEntity = true;
                this.SelectedPortal = new EditablePortal(nextIdx, x, y, null, null, null, null);
                break;

            default:
                this.PanelKind = "map";
                break;
        }
    }

    private string SerializeMapData()
    {
        var npcLooksByFace = looks.Looks.Npc.ToDictionary(n => n.Lookface);
        var monsterLooksById = looks.Looks.Monster.ToDictionary(m => m.Look);

        var npcs = this.Edit.Npcs.Select(n =>
        {
            npcLooksByFace.TryGetValue(n.Lookface, out var sprite);
            var detailNpc = this.Map.Npcs.FirstOrDefault(x => !x.IsGoal && x.Id == n.Id);
            return new
            {
                id = n.Id,
                name = n.Name ?? $"NPC #{n.Id}",
                lookface = n.Lookface,
                role = n.Role,
                x = n.X,
                y = n.Y,
                isGoal = false,
                kind = detailNpc?.Kind ?? "npc",
                look = sprite == null ? null : new { image = sprite.Image, x = sprite.X, y = sprite.Y, zoom = 1.0 },
            };
        }).Concat(this.Map.Npcs.Where(n => n.IsGoal).Select(g =>
        {
            npcLooksByFace.TryGetValue(g.Lookface, out var sprite);
            return new
            {
                id = g.Id,
                name = g.Name ?? $"Hedef #{g.Id}",
                lookface = g.Lookface,
                role = (byte)g.Role,
                x = g.X,
                y = g.Y,
                isGoal = true,
                kind = "goal",
                look = sprite == null ? null : new { image = sprite.Image, x = sprite.X, y = sprite.Y, zoom = 1.0 },
            };
        })).ToArray();

        var spawns = this.Edit.Spawns.Select(s =>
        {
            bool single = s.Spread == 0 && s.Amount == 1;
            object? lookObj = null;
            if (single && catalogs.MonstersById.TryGetValue(s.MonsterId, out var monster) && monster.Lookface is uint lf
                && monsterLooksById.TryGetValue(lf, out var mSprite))
            {
                lookObj = new
                {
                    image = mSprite.Image,
                    x = mSprite.X,
                    y = mSprite.Y,
                    zoom = (monster.ZoomPercent ?? 100) / 100.0,
                };
            }

            return new
            {
                id = s.Id,
                x = s.X,
                y = s.Y,
                spread = s.Spread,
                amount = s.Amount,
                respawn = s.Respawn,
                monsterId = s.MonsterId,
                monsterName = s.MonsterName ?? $"Canavar #{s.MonsterId}",
                monsterBoss = s.MonsterBoss,
                singleFigure = single,
                look = lookObj,
            };
        }).ToArray();

        var portals = this.Edit.Portals.Select(p => new
        {
            index = p.Index,
            x = p.X,
            y = p.Y,
            toMap = p.ToMap,
            toMapName = p.ToMapName,
            toX = p.ToX,
            toY = p.ToY,
        }).ToArray();

        var payload = new
        {
            mapId = this.MapId,
            canEdit = this.CanEdit,
            hasGrid = this.Map.HasGrid,
            width = this.Map.Width ?? 500,
            height = this.Map.Height ?? 500,
            meta = this.Map.Meta,
            entryPoint = this.Edit.Settings.Portal,
            npcs,
            spawns,
            portals,
            urls = new
            {
                grid = this.Url.Page("Detail", "Grid", new { id = this.MapId }),
                panel = this.Url.Page("Detail", "Panel", new { id = this.MapId }),
                move = this.Url.Page("Detail", "Move", new { id = this.MapId }),
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string DialogKindText(string kind) => kind switch
    {
        "script" => "Betik",
        "teleporter" => "Işınlayıcı",
        "talker" => "Konuşmacı",
        "shop" => "Dükkân",
        "warehouse" => "Depo",
        _ => "Varsayılan",
    };

    public sealed class MapSettingsForm
    {
        public string? Name { get; set; }
        public string? Aliases { get; set; }
        public string[]? Flags { get; set; }
        public ushort? PortalX { get; set; }
        public ushort? PortalY { get; set; }
        public uint? RebornMap { get; set; }
        public uint? ScrollTypeId { get; set; }
        public byte RidePlus { get; set; }
    }

    public sealed class NpcForm
    {
        public uint? OriginalId { get; set; }
        public uint Id { get; set; }
        public ushort Lookface { get; set; }
        public byte Role { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Life { get; set; }
        public byte Level { get; set; }
    }

    public sealed class SpawnForm
    {
        public uint? OriginalId { get; set; }
        public uint Id { get; set; }
        public uint MonsterId { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Spread { get; set; }
        public int Amount { get; set; } = 1;
        public int Respawn { get; set; } = 10;
    }

    public sealed class PortalForm
    {
        public uint? OriginalIndex { get; set; }
        public uint PortalIndex { get; set; }
        public uint ToMap { get; set; }
        public ushort ToX { get; set; }
        public ushort ToY { get; set; }
    }
}

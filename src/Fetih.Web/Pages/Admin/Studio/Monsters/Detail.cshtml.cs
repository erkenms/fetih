namespace Fetih.Web.Pages.Admin.Studio.Monsters;

using Fetih.Game.DataEditing;
using Fetih.Web.Audit;
using Fetih.Web.Auth;
using Fetih.Web.Features.Looks;
using Fetih.Web.Features.MonsterEdit;
using Fetih.Web.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Requires(Permission.ViewCatalogs)]
public class DetailModel(
    WebConfiguration config,
    MonsterDataService data,
    LookStore looks,
    GameRpc gameRpc,
    AuditLog audit) : PageModel
{
    public bool StudioReadOnly => config.Web.StudioReadOnly;
    public bool HasStudio => this.User.Has(Permission.Studio);
    public bool CanEdit => this.HasStudio && !this.StudioReadOnly;
    public bool GameConnected => gameRpc.Connected;
    public bool IsNew { get; private set; }
    public string RouteId { get; private set; } = "";
    public string? LookImage { get; private set; }

    [BindProperty]
    public string Hash { get; set; } = "";

    [BindProperty]
    public MonsterForm Input { get; set; } = new();

    public MonsterSpawnRef[] Spawns { get; private set; } = [];
    public MonsterBossRef[] BossSpawns { get; private set; } = [];
    public MonsterDropRef[] Drops { get; private set; } = [];
    public MonsterProblemDto[] Problems { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        this.RouteId = id;
        if (string.Equals(id, "new", StringComparison.OrdinalIgnoreCase))
        {
            if (!this.CanEdit)
                return this.Forbid();

            this.IsNew = true;
            await data.WithLockAsync(dataDir =>
            {
                var file = data.LoadMonstersFile(dataDir);
                this.Hash = file.Hash;
                uint nextId = file.All().Select(m => m.ID).DefaultIfEmpty(0u).Max() + 1;
                this.Input = new MonsterForm { Id = nextId };
                return Task.FromResult(true);
            }, ct);
            return this.Page();
        }

        if (!uint.TryParse(id, out uint monsterId))
            return this.NotFound();

        bool found = await this.LoadExistingAsync(monsterId, ct);
        return found ? this.Page() : this.NotFound();
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostSaveAsync(string id, CancellationToken ct)
    {
        this.RouteId = id;
        this.IsNew = string.Equals(id, "new", StringComparison.OrdinalIgnoreCase);
        uint existingId = 0;
        if (!this.IsNew && !uint.TryParse(id, out existingId))
            return this.NotFound();

        if (!TryParseSpells(this.Input.SpellsText, out var spells, out var spellError))
        {
            this.ErrorMessage = spellError;
            await this.ReloadRefsIfExistingAsync(existingId, ct);
            return this.Page();
        }

        var dto = this.Input.ToDto(spells);
        var preErrors = CheckBasicFields(dto);
        if (preErrors.Length > 0)
        {
            this.Problems = preErrors;
            await this.ReloadRefsIfExistingAsync(existingId, ct);
            return this.Page();
        }

        var outcome = await data.WithLockAsync<(bool Saved, bool NotFound, bool Conflict, string? NewHash, MonsterProblemDto[] Problems)>(async dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            if (!string.IsNullOrEmpty(this.Hash))
            {
                try { file.EnsureHash(this.Hash); }
                catch (DataFileConflictException) { return (false, false, true, file.Hash, []); }
            }

            if (this.IsNew)
            {
                var entity = MonsterDataService.FromDto(dto);
                file.Add(entity);
                var problems = data.ValidateMonsters(dataDir, file, checkDependents: false);
                if (problems.Any(p => p.Level == "error"))
                    return (false, false, false, file.Hash, problems);

                try { file.Save(); }
                catch (DataFileConflictException) { return (false, false, true, file.Hash, []); }

                var saved = MonsterDataService.ToDto(entity);
                await audit.WriteAsync("monster.create", "monster", saved.Id.ToString(),
                    after: saved, summary: $"Created monster {saved.Name} (#{saved.Id})");
                return (true, false, false, file.Hash, problems);
            }
            else
            {
                int index = file.IndexOf(m => m.ID == existingId);
                if (index < 0)
                    return (false, true, false, file.Hash, []);

                var before = MonsterDataService.ToDto(file[index]);
                var updated = MonsterDataService.FromDto(dto);
                file[index] = updated;

                bool idChanged = existingId != updated.ID;
                var problems = data.ValidateMonsters(dataDir, file, checkDependents: idChanged);
                if (problems.Any(p => p.Level == "error"))
                    return (false, false, false, file.Hash, problems);

                try { file.Save(); }
                catch (DataFileConflictException) { return (false, false, true, file.Hash, []); }

                var after = MonsterDataService.ToDto(updated);
                await audit.WriteAsync("monster.update", "monster", after.Id.ToString(),
                    before: before, after: after, summary: $"Updated monster {after.Name} (#{after.Id})");
                return (true, false, false, file.Hash, problems);
            }
        }, ct);

        if (outcome.NotFound)
            return this.NotFound();

        if (outcome.Conflict)
        {
            this.ErrorMessage = "monsters.json başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar dene.";
            await this.ReloadRefsIfExistingAsync(existingId, ct);
            return this.Page();
        }

        if (!outcome.Saved)
        {
            this.Problems = outcome.Problems;
            await this.ReloadRefsIfExistingAsync(existingId, ct);
            return this.Page();
        }

        this.Flash(this.IsNew ? $"Canavar #{dto.Id} oluşturuldu." : $"Canavar #{dto.Id} kaydedildi.");
        return this.RedirectToPage(new { id = dto.Id.ToString() });
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken ct)
    {
        if (!uint.TryParse(id, out uint monsterId))
            return this.NotFound();

        this.RouteId = id;
        var outcome = await data.WithLockAsync<(bool Deleted, bool NotFound, bool Conflict, MonsterProblemDto[] Problems)>(async dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            if (!string.IsNullOrEmpty(this.Hash))
            {
                try { file.EnsureHash(this.Hash); }
                catch (DataFileConflictException) { return (false, false, true, []); }
            }

            int index = file.IndexOf(m => m.ID == monsterId);
            if (index < 0)
                return (false, true, false, []);

            var before = MonsterDataService.ToDto(file[index]);
            file.RemoveAt(index);

            var problems = data.ValidateMonsters(dataDir, file, checkDependents: true);
            var errors = problems.Where(p => p.Level == "error").ToArray();
            if (errors.Length > 0)
                return (false, false, false, errors);

            try { file.Save(); }
            catch (DataFileConflictException) { return (false, false, true, []); }

            await audit.WriteAsync("monster.delete", "monster", monsterId.ToString(),
                before: before, summary: $"Deleted monster {before.Name} (#{monsterId})");
            return (true, false, false, []);
        }, ct);

        if (outcome.NotFound)
            return this.NotFound();

        if (outcome.Conflict)
        {
            this.FlashError("monsters.json başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar dene.");
            return this.RedirectToPage(new { id });
        }

        if (!outcome.Deleted)
        {
            this.Problems = outcome.Problems;
            this.ErrorMessage = "Canavar başka dosyalarda kullanıldığı için silinemedi.";
            await this.LoadExistingAsync(monsterId, ct);
            return this.Page();
        }

        this.Flash($"Canavar #{monsterId} silindi.");
        return this.RedirectToPage("Index");
    }

    [Requires(Permission.Studio, StudioWrite = true)]
    public async Task<IActionResult> OnPostReloadAsync(string id)
    {
        var result = await StudioData.ReloadFileAsync(DataFileKinds.Monsters.FileName, gameRpc, audit);
        if (result.Applied)
            this.Flash(string.IsNullOrWhiteSpace(result.Summary)
                ? "monsters.json yeniden yüklendi."
                : $"monsters.json: {result.Summary}");
        else
            this.FlashError($"monsters.json yüklenemedi: {string.Join("; ", result.Errors)}");

        return this.RedirectToPage(new { id });
    }

    private Task<bool> LoadExistingAsync(uint monsterId, CancellationToken ct) =>
        data.WithLockAsync(dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            int index = file.IndexOf(m => m.ID == monsterId);
            if (index < 0)
                return Task.FromResult(false);

            this.Hash = file.Hash;
            var dto = MonsterDataService.ToDto(file[index]);
            this.Input = MonsterForm.FromDto(dto);
            this.LookImage = looks.Looks.Monster.FirstOrDefault(l => l.Look == dto.Lookface)?.Image;
            this.LoadReferences(dataDir, monsterId);
            if (this.Problems.Length == 0)
                this.Problems = data.ValidateMonsters(dataDir, file, checkDependents: false)
                    .Where(p => p.Entry == index).ToArray();
            return Task.FromResult(true);
        }, ct);

    private async Task ReloadRefsIfExistingAsync(uint existingId, CancellationToken ct)
    {
        this.LookImage = looks.Looks.Monster.FirstOrDefault(l => l.Look == this.Input.Lookface)?.Image;
        if (this.IsNew)
            return;
        await data.WithLockAsync(dataDir =>
        {
            this.LoadReferences(dataDir, existingId);
            return Task.FromResult(true);
        }, ct);
    }

    private void LoadReferences(string dataDir, uint monsterId)
    {
        this.Spawns = data.LoadSpawns(dataDir)
            .Where(s => s.MonsterID == monsterId)
            .Select(s => new MonsterSpawnRef(
                s.ID, s.MapID, data.MapName(s.MapID), s.X, s.Y, s.Spread, s.Amount, s.RespawnSeconds))
            .ToArray();
        this.BossSpawns = data.LoadBosses(dataDir)
            .Where(b => b.MonsterID == monsterId)
            .Select(b => new MonsterBossRef(
                b.ID, b.MapID, data.MapName(b.MapID), b.X, b.Y, b.Spread,
                b.Times?.ToArray() ?? [], b.IntervalSeconds, b.Broadcast))
            .ToArray();
        this.Drops = data.LoadDrops(dataDir)
            .Where(d => d.MonsterID == monsterId)
            .SelectMany(d => d.Entries ?? [])
            .Select(e => new MonsterDropRef(e.ItemID, e.Chance, e.Count))
            .ToArray();
    }

    private static MonsterProblemDto[] CheckBasicFields(MonsterDto m)
    {
        var errors = new List<MonsterProblemDto>();
        if (m.Id == 0)
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "Canavar ID 0'dan büyük olmalı.", "error"));
        if (string.IsNullOrWhiteSpace(m.Name))
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "Ad boş olamaz.", "error"));
        if (m.MagicHitRate < 0 || m.MagicHitRate > 100)
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "Büyü isabet oranı 0 ile 100 arasında olmalı.", "error"));
        return [.. errors];
    }

    private static bool TryParseSpells(string? raw, out ushort[] spells, out string? error)
    {
        spells = [];
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var list = new List<ushort>();
        foreach (var part in raw.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ushort.TryParse(part.Trim(), out ushort val))
            {
                error = $"Geçersiz büyü numarası: '{part}'. Virgülle ayrılmış sayılar girin.";
                return false;
            }
            list.Add(val);
        }
        spells = [.. list];
        return true;
    }

    public sealed class MonsterForm
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public uint Lookface { get; set; }
        public byte Level { get; set; } = 1;
        public uint Life { get; set; } = 100;
        public int AttackMin { get; set; } = 5;
        public int AttackMax { get; set; } = 10;
        public int Defense { get; set; }
        public int Dexterity { get; set; }
        public int Dodge { get; set; }
        public int AttackRange { get; set; } = 1;
        public int ViewRange { get; set; } = 8;
        public int AttackSpeed { get; set; } = 1000;
        public int MoveSpeed { get; set; } = 500;
        public uint AttackUser { get; set; } = 3;
        public uint DropMoney { get; set; }
        public uint DropHp { get; set; }
        public uint DropMp { get; set; }
        public bool Boss { get; set; }
        public ushort MagicType { get; set; }
        public int MagicHitRate { get; set; }
        public string? SpellsText { get; set; }

        public static MonsterForm FromDto(MonsterDto d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Lookface = d.Lookface,
            Level = d.Level,
            Life = d.Life,
            AttackMin = d.AttackMin,
            AttackMax = d.AttackMax,
            Defense = d.Defense,
            Dexterity = d.Dexterity,
            Dodge = d.Dodge,
            AttackRange = d.AttackRange,
            ViewRange = d.ViewRange,
            AttackSpeed = d.AttackSpeed,
            MoveSpeed = d.MoveSpeed,
            AttackUser = d.AttackUser,
            DropMoney = d.DropMoney,
            DropHp = d.DropHp,
            DropMp = d.DropMp,
            Boss = d.Boss,
            MagicType = d.MagicType,
            MagicHitRate = d.MagicHitRate,
            SpellsText = d.Spells is { Length: > 0 } ? string.Join(", ", d.Spells) : "",
        };

        public MonsterDto ToDto(ushort[] spells) => new(
            Id: this.Id,
            Name: this.Name ?? "",
            Lookface: this.Lookface,
            Level: this.Level,
            Life: this.Life,
            AttackMin: this.AttackMin,
            AttackMax: this.AttackMax,
            Defense: this.Defense,
            Dexterity: this.Dexterity,
            Dodge: this.Dodge,
            AttackRange: this.AttackRange,
            ViewRange: this.ViewRange,
            AttackSpeed: this.AttackSpeed,
            MoveSpeed: this.MoveSpeed,
            AttackUser: this.AttackUser,
            DropMoney: this.DropMoney,
            DropHp: this.DropHp,
            DropMp: this.DropMp,
            Boss: this.Boss,
            MagicType: this.MagicType,
            MagicHitRate: this.MagicHitRate,
            Spells: spells);
    }
}

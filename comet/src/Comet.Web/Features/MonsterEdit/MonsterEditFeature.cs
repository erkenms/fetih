namespace Comet.Web.Features.MonsterEdit;

using Comet.Game.DataEditing;
using Comet.Shared.Models;
using Comet.Web.Audit;
using Comet.Web.Auth;
using Comet.Web.Game;
using Microsoft.AspNetCore.Http.HttpResults;

public class MonsterEditFeature : IFeature
{
    public void AddServices(IServiceCollection services, WebConfiguration config)
    {
        services.AddSingleton<MonsterDataService>();
    }

    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/studio/monsters").RequirePermission(Permission.Studio);
        group.MapGet("/", ListAsync).WithName("listStudioMonsters");
        group.MapGet("/{id:int}", GetAsync).WithName("getStudioMonster");
        group.MapPost("/", CreateAsync).WithName("createStudioMonster").RequireStudioWritable();
        group.MapPut("/{id:int}", UpdateAsync).WithName("updateStudioMonster").RequireStudioWritable();
        group.MapDelete("/{id:int}", DeleteAsync).WithName("deleteStudioMonster").RequireStudioWritable();
        group.MapPost("/reload", ReloadAsync).WithName("reloadStudioMonsters").RequireStudioWritable();
    }

    private static Task<Ok<MonsterListResponse>> ListAsync(
        MonsterDataService data, GameRpc gameRpc, CancellationToken ct) =>
        data.WithLockAsync(dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            var monsters = file.All();
            var spawns = data.LoadSpawns(dataDir)
                .GroupBy(s => s.MonsterID)
                .ToDictionary(g => g.Key, g => (Areas: g.Count(), Total: g.Sum(x => x.Amount)));
            var bosses = data.LoadBosses(dataDir)
                .GroupBy(b => b.MonsterID)
                .ToDictionary(g => g.Key, g => g.Count());
            var drops = data.LoadDrops(dataDir)
                .Select(d => d.MonsterID)
                .ToHashSet();

            var rows = new MonsterListRow[monsters.Count];
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                spawns.TryGetValue(m.ID, out var spawnInfo);
                bosses.TryGetValue(m.ID, out int bossCount);
                rows[i] = new MonsterListRow(
                    Monster: MonsterDataService.ToDto(m),
                    Line: file.LineOf(i),
                    SpawnAreaCount: spawnInfo.Areas,
                    TotalSpawnAmount: spawnInfo.Total,
                    BossScheduleCount: bossCount,
                    HasCustomDrops: drops.Contains(m.ID));
            }

            var problems = data.ValidateMonsters(dataDir, file, checkDependents: false);
            return Task.FromResult(TypedResults.Ok(new MonsterListResponse(
                file.Hash, gameRpc.Connected, rows, problems)));
        }, ct);

    private static Task<Results<Ok<MonsterDetailResponse>, NotFound<ApiError>>> GetAsync(
        uint id, MonsterDataService data, CancellationToken ct) =>
        data.WithLockAsync<Results<Ok<MonsterDetailResponse>, NotFound<ApiError>>>(dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            int index = file.IndexOf(m => m.ID == id);
            if (index < 0)
                return Task.FromResult<Results<Ok<MonsterDetailResponse>, NotFound<ApiError>>>(
                    TypedResults.NotFound(new ApiError("not_found")));

            var monster = file[index];
            var spawns = data.LoadSpawns(dataDir)
                .Where(s => s.MonsterID == id)
                .Select(s => new MonsterSpawnRef(
                    s.ID, s.MapID, data.MapName(s.MapID), s.X, s.Y, s.Spread, s.Amount, s.RespawnSeconds))
                .ToArray();
            var bossSpawns = data.LoadBosses(dataDir)
                .Where(b => b.MonsterID == id)
                .Select(b => new MonsterBossRef(
                    b.ID, b.MapID, data.MapName(b.MapID), b.X, b.Y, b.Spread,
                    b.Times?.ToArray() ?? [], b.IntervalSeconds, b.Broadcast))
                .ToArray();
            var drops = data.LoadDrops(dataDir)
                .Where(d => d.MonsterID == id)
                .SelectMany(d => d.Entries ?? [])
                .Select(e => new MonsterDropRef(e.ItemID, e.Chance, e.Count))
                .ToArray();
            var problems = data.ValidateMonsters(dataDir, file, checkDependents: false)
                .Where(p => p.Entry == index)
                .ToArray();

            return Task.FromResult<Results<Ok<MonsterDetailResponse>, NotFound<ApiError>>>(
                TypedResults.Ok(new MonsterDetailResponse(
                    file.Hash,
                    MonsterDataService.ToDto(monster),
                    file.LineOf(index),
                    spawns,
                    bossSpawns,
                    drops,
                    problems)));
        }, ct);

    private static Task<Results<Created<SaveMonsterResponse>, BadRequest<MonsterValidationError>, Conflict<ApiError>>> CreateAsync(
        SaveMonsterRequest request, MonsterDataService data, AuditLog audit, CancellationToken ct) =>
        data.WithLockAsync<Results<Created<SaveMonsterResponse>, BadRequest<MonsterValidationError>, Conflict<ApiError>>>(async dataDir =>
        {
            var preErrors = CheckBasicFields(request.Monster);
            if (preErrors.Length > 0)
                return TypedResults.BadRequest(new MonsterValidationError("validation_failed", preErrors));

            var file = data.LoadMonstersFile(dataDir);
            if (!string.IsNullOrEmpty(request.Hash))
            {
                try { file.EnsureHash(request.Hash); }
                catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }
            }

            var entity = MonsterDataService.FromDto(request.Monster);
            file.Add(entity);

            var problems = data.ValidateMonsters(dataDir, file, checkDependents: false);
            var errors = problems.Where(p => p.Level == "error").ToArray();
            if (errors.Length > 0)
                return TypedResults.BadRequest(new MonsterValidationError("validation_failed", problems));

            try { file.Save(); }
            catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }

            var savedDto = MonsterDataService.ToDto(entity);
            await audit.WriteAsync(
                "monster.create",
                "monster",
                savedDto.Id.ToString(),
                after: savedDto,
                summary: $"Created monster {savedDto.Name} (#{savedDto.Id})");

            var warnings = problems.Where(p => p.Level == "warning" && p.Entry == file.Count - 1).ToArray();
            return TypedResults.Created($"/api/studio/monsters/{savedDto.Id}", new SaveMonsterResponse(file.Hash, savedDto, warnings));
        }, ct);

    private static Task<Results<Ok<SaveMonsterResponse>, NotFound<ApiError>, BadRequest<MonsterValidationError>, Conflict<ApiError>>> UpdateAsync(
        uint id, SaveMonsterRequest request, MonsterDataService data, AuditLog audit, CancellationToken ct) =>
        data.WithLockAsync<Results<Ok<SaveMonsterResponse>, NotFound<ApiError>, BadRequest<MonsterValidationError>, Conflict<ApiError>>>(async dataDir =>
        {
            var preErrors = CheckBasicFields(request.Monster);
            if (preErrors.Length > 0)
                return TypedResults.BadRequest(new MonsterValidationError("validation_failed", preErrors));

            var file = data.LoadMonstersFile(dataDir);
            if (!string.IsNullOrEmpty(request.Hash))
            {
                try { file.EnsureHash(request.Hash); }
                catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }
            }

            int index = file.IndexOf(m => m.ID == id);
            if (index < 0)
                return TypedResults.NotFound(new ApiError("not_found"));

            var before = MonsterDataService.ToDto(file[index]);
            var updated = MonsterDataService.FromDto(request.Monster);
            file[index] = updated;

            bool idChanged = id != updated.ID;
            var problems = data.ValidateMonsters(dataDir, file, checkDependents: idChanged);
            var errors = problems.Where(p => p.Level == "error").ToArray();
            if (errors.Length > 0)
                return TypedResults.BadRequest(new MonsterValidationError("validation_failed", problems));

            try { file.Save(); }
            catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }

            var after = MonsterDataService.ToDto(updated);
            await audit.WriteAsync(
                "monster.update",
                "monster",
                after.Id.ToString(),
                before: before,
                after: after,
                summary: $"Updated monster {after.Name} (#{after.Id})");

            var warnings = problems.Where(p => p.Level == "warning" && p.Entry == index).ToArray();
            return TypedResults.Ok(new SaveMonsterResponse(file.Hash, after, warnings));
        }, ct);

    private static Task<Results<Ok<DeleteMonsterResponse>, NotFound<ApiError>, BadRequest<MonsterValidationError>, Conflict<ApiError>>> DeleteAsync(
        uint id, string? hash, MonsterDataService data, AuditLog audit, CancellationToken ct) =>
        data.WithLockAsync<Results<Ok<DeleteMonsterResponse>, NotFound<ApiError>, BadRequest<MonsterValidationError>, Conflict<ApiError>>>(async dataDir =>
        {
            var file = data.LoadMonstersFile(dataDir);
            if (!string.IsNullOrEmpty(hash))
            {
                try { file.EnsureHash(hash); }
                catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }
            }

            int index = file.IndexOf(m => m.ID == id);
            if (index < 0)
                return TypedResults.NotFound(new ApiError("not_found"));

            var before = MonsterDataService.ToDto(file[index]);
            file.RemoveAt(index);

            var problems = data.ValidateMonsters(dataDir, file, checkDependents: true);
            var errors = problems.Where(p => p.Level == "error").ToArray();
            if (errors.Length > 0)
                return TypedResults.BadRequest(new MonsterValidationError("monster_in_use", errors));

            try { file.Save(); }
            catch (DataFileConflictException) { return TypedResults.Conflict(new ApiError("conflict")); }

            await audit.WriteAsync(
                "monster.delete",
                "monster",
                id.ToString(),
                before: before,
                summary: $"Deleted monster {before.Name} (#{id})");

            return TypedResults.Ok(new DeleteMonsterResponse(file.Hash, id));
        }, ct);

    private static async Task<Ok<MonsterReloadResponse>> ReloadAsync(GameRpc gameRpc, AuditLog audit)
    {
        var result = await StudioData.ReloadFileAsync(DataFileKinds.Monsters.FileName, gameRpc, audit);

        return TypedResults.Ok(new MonsterReloadResponse(
            result.File ?? DataFileKinds.Monsters.FileName,
            result.Applied,
            [.. result.Errors],
            [.. result.Warnings],
            result.Summary));
    }

    private static MonsterProblemDto[] CheckBasicFields(MonsterDto m)
    {
        var errors = new List<MonsterProblemDto>();
        if (m.Id == 0)
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "Monster ID must be above 0", "error"));
        if (string.IsNullOrWhiteSpace(m.Name))
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "Name is required", "error"));
        if (m.MagicHitRate < 0 || m.MagicHitRate > 100)
            errors.Add(new(DataFileKinds.Monsters.FileName, null, null, "magicHitRate must be between 0 and 100", "error"));
        return [.. errors];
    }
}

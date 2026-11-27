using System.Net;
using System.Net.Http.Headers;

namespace Fetih.Patch;

// Downloads a plan's objects (several at a time, resuming and retrying), verifies each file,
// renames it into place, deletes what the plan says and records the new state.
public sealed class PatchSync(HttpClient http, Uri patchBase, string root)
{
    public int Parallel { get; init; } = 16;

    // Objects up to this size are fetched into memory; bigger ones resume from .launcher/download/
    public long SmallObject { get; init; } = 1 << 20;
    public int Attempts { get; init; } = 5;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    long _done;

    // Compressed bytes received so far, for the progress bar
    public long DoneBytes => Interlocked.Read(ref _done);

    public static async Task<PatchManifest> FetchManifestAsync(HttpClient http, Uri patchBase, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(patchBase, "manifest.json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return PatchManifestJson.Deserialize(await response.Content.ReadAsByteArrayAsync(ct));
    }

    public async Task<LocalState> ApplyAsync(PatchManifest manifest, SyncPlan plan, LocalState state, CancellationToken ct)
    {
        var done = new List<LocalFile>();
        try
        {
            var byObject = plan.Download.GroupBy(e => e.Sha256, StringComparer.OrdinalIgnoreCase).ToList();
            await System.Threading.Tasks.Parallel.ForEachAsync(
                byObject,
                new ParallelOptions { MaxDegreeOfParallelism = this.Parallel, CancellationToken = ct },
                async (group, token) =>
                {
                    var files = await this.InstallObjectAsync(group.ToList(), token);
                    lock (done)
                        done.AddRange(files);
                });

            foreach (var path in plan.Delete)
                File.Delete(SyncPlan.FullPath(root, path));
        }
        catch
        {
            // Keep what did arrive, and the old manifest's list, so the next start knows both
            var merged = state.Files.ToDictionary(f => PatchManifest.NormalizePath(f.Path), StringComparer.OrdinalIgnoreCase);
            lock (done)
                foreach (var file in plan.Current.Concat(done))
                    merged[PatchManifest.NormalizePath(file.Path)] = file;
            (state with { Version = null, Files = merged.Values.ToList() }).Save(root);
            throw;
        }

        var next = state with
        {
            Version = manifest.Version,
            Files = plan.Current.Concat(done).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        next.Save(root);
        return next;
    }

    // Fetches one object and writes the file at destination, verified; for the launcher itself
    public async Task DownloadFileAsync(PatchFileEntry entry, string destination, CancellationToken ct)
    {
        var part = await this.FetchObjectAsync(entry, ct);
        try
        {
            PatchObjectStore.DecompressAndVerify(part, entry, destination);
        }
        finally
        {
            File.Delete(part);
        }
    }

    async Task<List<LocalFile>> InstallObjectAsync(List<PatchFileEntry> entries, CancellationToken ct)
    {
        var first = entries[0];
        for (var attempt = 1; ; attempt++)
        {
            // Small objects stay in memory: one new file per game file instead of three
            byte[]? small = null;
            string? part = null;
            if (first.CompressedSize <= SmallObject)
                small = await this.FetchSmallAsync(first, ct);
            else
                part = await this.FetchObjectAsync(first, ct);
            try
            {
                var installed = new List<LocalFile>();
                foreach (var entry in entries)
                {
                    var path = SyncPlan.FullPath(root, entry.Path);
                    using (var raw = small != null ? new MemoryStream(small, writable: false) : (Stream)File.OpenRead(part!))
                        PatchObjectStore.DecompressAndVerify(raw, entry, path);
                    if (!entry.PlayerOwned)
                        installed.Add(LocalFile.Of(PatchManifest.NormalizePath(entry.Path), new FileInfo(path), entry.Sha256));
                }
                if (part != null)
                    File.Delete(part);
                return installed;
            }
            catch (InvalidDataException) when (attempt < this.Attempts)
            {
                // A corrupt download: start this object over
                Interlocked.Add(ref _done, -(small?.Length ?? new FileInfo(part!).Length));
                if (part != null)
                    File.Delete(part);
            }
        }
    }

    async Task<byte[]> FetchSmallAsync(PatchFileEntry entry, CancellationToken ct)
    {
        if (entry.CompressedSize == 0)
            return [];
        var url = new Uri(patchBase, "objects/" + entry.Sha256.ToLowerInvariant());
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(url, ct);
                if (bytes.Length != entry.CompressedSize)
                    throw new IOException($"Short download of {entry.Path}");
                Interlocked.Add(ref _done, bytes.Length);
                return bytes;
            }
            catch (Exception ex) when (attempt < this.Attempts && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(this.RetryDelay * attempt, ct);
            }
        }
    }

    // Downloads objects/<sha256> into .launcher/download/, resuming a partial one
    async Task<string> FetchObjectAsync(PatchFileEntry entry, CancellationToken ct)
    {
        var dir = Path.Combine(root, LocalState.Folder, "download");
        Directory.CreateDirectory(dir);
        var part = Path.Combine(dir, entry.Sha256.ToLowerInvariant());
        var url = new Uri(patchBase, "objects/" + entry.Sha256.ToLowerInvariant());
        var counted = 0L;
        if (entry.CompressedSize == 0)
        {
            File.WriteAllBytes(part, []);
            return part;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > entry.CompressedSize)
                {
                    File.Delete(part);
                    have = 0;
                }
                Interlocked.Add(ref _done, have - counted);
                counted = have;
                if (have == entry.CompressedSize)
                    return part;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0)
                    request.Headers.Range = new RangeHeaderValue(have, null);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(part);
                    throw new IOException("Range not satisfiable");
                }
                response.EnsureSuccessStatusCode();
                var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!append)
                {
                    Interlocked.Add(ref _done, -counted);
                    counted = 0;
                }

                await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        Interlocked.Add(ref _done, read);
                        counted += read;
                    }
                }

                if (new FileInfo(part).Length == entry.CompressedSize)
                    return part;
                throw new IOException($"Short download of {entry.Path}");
            }
            catch (Exception ex) when (attempt < this.Attempts && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(this.RetryDelay * attempt, ct);
            }
        }
    }
}

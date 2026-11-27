using System.Net;
using System.Net.Http.Headers;

namespace Fetih.Patch;

// Downloads a plan's objects (several at a time, retrying), verifies each file,
// renames it into place, deletes what the plan says and records the new state.
public sealed class PatchSync(HttpClient http, Uri patchBase, string root)
{
    public int Parallel { get; init; } = 16;
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
                    var files = await this.InstallObjectAsync(group.ToList(), null, token);
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
    public Task DownloadFileAsync(PatchFileEntry entry, string destination, CancellationToken ct) =>
        this.InstallObjectAsync([entry], destination, ct);

    // Streams the object through the decompressor into each entry's file. A failure starts the
    // object over; the biggest (data.wdf) is under 200 MB.
    async Task<List<LocalFile>> InstallObjectAsync(List<PatchFileEntry> entries, string? destination, CancellationToken ct)
    {
        var first = entries[0];
        var url = new Uri(patchBase, "objects/" + first.Sha256.ToLowerInvariant());
        for (var attempt = 1; ; attempt++)
        {
            var counted = new Counter(this);
            try
            {
                var firstPath = destination ?? SyncPlan.FullPath(root, first.Path);
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var body = await response.Content.ReadAsStreamAsync(ct);
                    using var raw = new CountingStream(body, counted);
                    PatchObjectStore.DecompressAndVerify(raw, first, firstPath);
                    if (counted.Bytes != first.CompressedSize)
                        throw new InvalidDataException($"Size mismatch for the object of {first.Path}");
                }

                // The same content elsewhere: copy what's already verified
                var installed = new List<LocalFile>();
                foreach (var entry in entries)
                {
                    var path = destination ?? SyncPlan.FullPath(root, entry.Path);
                    if (path != firstPath)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.Copy(firstPath, path, overwrite: true);
                    }
                    if (!entry.PlayerOwned)
                        installed.Add(LocalFile.Of(PatchManifest.NormalizePath(entry.Path), new FileInfo(path), entry.Sha256));
                }
                return installed;
            }
            catch (Exception ex) when (attempt < this.Attempts && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                counted.Undo();
                await Task.Delay(this.RetryDelay * attempt, ct);
            }
        }
    }

    // Progress for one attempt, taken back when it fails
    sealed class Counter(PatchSync sync)
    {
        public long Bytes;

        public void Add(int n)
        {
            this.Bytes += n;
            Interlocked.Add(ref sync._done, n);
        }

        public void Undo() => Interlocked.Add(ref sync._done, -this.Bytes);
    }

    sealed class CountingStream(Stream inner, Counter counter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            counter.Add(read);
            return read;
        }
    }
}

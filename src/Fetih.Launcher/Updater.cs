using System.Diagnostics;
using System.Net;
using System.Text;
using Fetih.Patch;
using static Fetih.Launcher.Native;

namespace Fetih.Launcher;

enum Phase { Checking, Updating, Ready, Failed, Closing }

// Every start: launcher.json, the manifest, self-update, the files, then the game
sealed class Updater(string root, string selfPath, LauncherSettings settings, string channel, bool repair, string[] args)
{
    readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli | DecompressionMethods.Deflate,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    readonly CancellationTokenSource _stop = new();
    Task? _work;
    PatchSync? _sync;
    PatchManifest? _manifest;

    public Action? Changed { get; set; }
    public nint Owner { get; set; }
    public volatile Phase Phase = Phase.Checking;
    public volatile string Status = "Sunucuya bağlanılıyor...";
    public long Total;
    public long Done => _sync?.DoneBytes ?? 0;
    public volatile IReadOnlyList<(string Date, string Subject)>? News;
    public volatile bool NewsFailed;

    string GameSetUpPath => SyncPlan.FullPath(root, GameSetUp.RelativePath);

    public void Start()
    {
        _ = Task.Run(this.LoadNewsAsync);
        this.Begin(repair);
    }

    public void Retry() => this.Begin(false);

    public void Repair() => this.Begin(true);

    public void Stop()
    {
        _stop.Cancel();
        try
        {
            _work?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
    }

    void Begin(bool checkAll)
    {
        if (_work is { IsCompleted: false })
            return;
        this.Set(Phase.Checking, checkAll ? "Dosyalar denetleniyor..." : "Sunucuya bağlanılıyor...");
        _work = Task.Run(() => this.SyncAsync(checkAll));
    }

    async Task SyncAsync(bool checkAll)
    {
        var ct = _stop.Token;
        try
        {
            var patchBase = settings.PatchBase(channel);
            PatchManifest manifest;
            while (true)
            {
                try
                {
                    manifest = await PatchSync.FetchManifestAsync(_http, patchBase, ct);
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
                {
                    var answer = MessageBox(this.Owner, $"Sunucuya ulaşılamadı.\n\n{patchBase}\n{ex.Message}", Install.Name, MB_RETRYCANCEL | MB_ICONWARNING);
                    if (answer != IDRETRY)
                    {
                        this.Set(Phase.Closing, "");
                        return;
                    }
                }
            }
            _manifest = manifest;
            var sync = new PatchSync(_http, patchBase, root);
            _sync = sync;

            foreach (var folder in manifest.Folders)
                Directory.CreateDirectory(SyncPlan.FullPath(root, folder));

            var state = LocalState.Load(root);
            if (!SyncPlan.LauncherIsCurrent(selfPath, manifest, state, out var self))
            {
                this.Set(Phase.Checking, "Başlatıcı güncelleniyor...");
                var next = Path.Combine(root, "Fetih.new.exe");
                await sync.DownloadFileAsync(manifest.Launcher!, next, ct);
                var start = new ProcessStartInfo(next) { UseShellExecute = false, WorkingDirectory = root };
                start.ArgumentList.Add("--replace");
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                foreach (var arg in args)
                    start.ArgumentList.Add(arg);
                Process.Start(start);
                this.Set(Phase.Closing, "");
                return;
            }
            state = state with { Launcher = self, Channel = channel };

            if (checkAll)
                this.Set(Phase.Checking, "Dosyalar denetleniyor...");
            var plan = SyncPlan.Create(root, manifest, state, checkAll);
            while (plan.HasWork && this.GameIsRunning())
            {
                var answer = MessageBox(this.Owner, "Oyun açık. Güncellemek için oyunu kapat.", Install.Name, MB_RETRYCANCEL | MB_ICONINFORMATION);
                if (answer != IDRETRY)
                {
                    this.Set(Phase.Failed, "Oyun açıkken güncellenemez. Tekrar denemek için Oyna'ya bas.");
                    return;
                }
            }

            if (plan.HasWork)
            {
                Interlocked.Exchange(ref this.Total, plan.DownloadBytes);
                this.Set(Phase.Updating, plan.Download.Count > 0 ? $"{plan.Download.Count} dosya indiriliyor..." : "Dosyalar güncelleniyor...");
            }
            state = await sync.ApplyAsync(manifest, plan, state, ct);

            if (!state.GameSetUp && File.Exists(this.GameSetUpPath))
            {
                GameSetUp.SetHighQuality(this.GameSetUpPath);
                GameSetUp.WriteScreen(this.GameSetUpPath, new ScreenMode(GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), FullScreen: true));
                state = state with { GameSetUp = true };
                state.Save(root);
            }

            this.Set(Phase.Ready, channel == "prod" ? "Oyun hazır." : $"Oyun hazır ({channel}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            this.Set(Phase.Failed, $"Güncelleme yarıda kaldı: {ex.Message}\nTekrar denemek için Oyna'ya bas.");
        }
    }

    bool GameIsRunning()
    {
        var exe = _manifest == null ? null : SyncPlan.FullPath(root, _manifest.Start);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe ?? "Conquer.exe")))
        {
            try
            {
                if (exe == null || string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception)
            {
                return true; // Can't see its path; better ask than break a running game
            }
        }
        return false;
    }

    public bool StartGame()
    {
        if (this.Phase != Phase.Ready || _manifest == null)
            return false;
        var exe = SyncPlan.FullPath(root, _manifest.Start);
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = root });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox(this.Owner, $"Oyun başlatılamadı.\n\n{exe}\n{ex.Message}", Install.Name, MB_ICONERROR);
            return false;
        }
    }

    public ScreenMode ReadScreen() => GameSetUp.ReadScreen(this.GameSetUpPath);

    public void WriteScreen(ScreenMode mode) => GameSetUp.WriteScreen(this.GameSetUpPath, mode);

    // The site's latest news as "date<TAB>subject" lines
    async Task LoadNewsAsync()
    {
        try
        {
            var text = await _http.GetStringAsync(settings.News, _stop.Token);
            this.News = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split('\t', 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => (parts[0], parts[1]))
                .ToList();
        }
        catch (Exception)
        {
            this.NewsFailed = true;
        }
        this.Changed?.Invoke();
    }

    void Set(Phase phase, string status)
    {
        this.Status = status;
        this.Phase = phase;
        this.Changed?.Invoke();
    }
}

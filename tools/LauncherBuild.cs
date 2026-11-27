using System.Diagnostics;
using Fetih.Patch;

// Publishes src/Fetih.Launcher for Windows with launcher.json and the old Play art baked in.
// Returns the files the client build adds: Fetih.exe and launcher.json.
static class LauncherBuild
{
    public static Dictionary<string, byte[]>? Run(string repoRoot, string artDir, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            Console.Error.WriteLine($"The site's address is required for the launcher: pass --url https://<domain> or set FETIH_URL (got '{url}')");
            return null;
        }
        if (!File.Exists(Path.Combine(artDir, "Play", "Main.bmp")))
        {
            Console.Error.WriteLine($"No Play/Main.bmp under {artDir}: pass --art <old Turkish client>");
            return null;
        }

        var work = Path.Combine(repoRoot, "src", "Fetih.Launcher", "obj", "build");
        Directory.CreateDirectory(work);
        var settings = new LauncherSettings { Url = url.TrimEnd('/'), Channel = "prod" }.ToJson();
        var settingsPath = Path.Combine(work, LauncherSettings.FileName);
        File.WriteAllBytes(settingsPath, settings);

        var output = Path.Combine(work, "publish");
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "publish", Path.Combine("src", "Fetih.Launcher", "Fetih.Launcher.csproj"), "-c", "Release", "-o", output,
                     $"-p:FetihArt={Path.GetFullPath(artDir)}", $"-p:FetihLauncherJson={settingsPath}", "-nologo", "-v:q",
                 })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(stdout.Result + stderr.Result);
            Console.Error.WriteLine("Launcher build failed");
            return null;
        }

        var exe = File.ReadAllBytes(Path.Combine(output, "Fetih.exe"));
        Console.WriteLine($"Launcher: {exe.Length / 1024 / 1024} MB, {url}");
        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fetih.exe"] = exe,
            [LauncherSettings.FileName] = settings,
        };
    }
}

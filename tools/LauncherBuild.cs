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

        // The game's radio buttons are DDS; the launcher reads only BMP
        var built = Path.Combine(work, "art");
        Directory.CreateDirectory(built);
        foreach (var (dds, name) in new[] { ("Radio_Button1Pic.dds", "RadioOff.bmp"), ("Radio_Button2Pic.dds", "RadioOn.bmp") })
        {
            var path = Path.Combine(artDir, "data", "interface", "Style01", "faction", dds);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No {path}");
                return null;
            }
            File.WriteAllBytes(Path.Combine(built, name), Bmp32(Dds.Decode(File.ReadAllBytes(path))));
        }

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
                     $"-p:FetihArt={Path.GetFullPath(artDir)}", $"-p:FetihBuiltArt={built}", $"-p:FetihLauncherJson={settingsPath}", "-nologo", "-v:q",
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

    // Top-down 32-bit BGRA, straight alpha
    static byte[] Bmp32(Rgba image)
    {
        var size = image.Width * image.Height * 4;
        var bmp = new byte[54 + size];
        "BM"u8.CopyTo(bmp);
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), image.Width);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), -image.Height);
        BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)32);
        BitConverter.TryWriteBytes(bmp.AsSpan(34), size);
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            bmp[54 + i * 4] = image.Pixels[i * 4 + 2];
            bmp[54 + i * 4 + 1] = image.Pixels[i * 4 + 1];
            bmp[54 + i * 4 + 2] = image.Pixels[i * 4];
            bmp[54 + i * 4 + 3] = image.Pixels[i * 4 + 3];
        }
        return bmp;
    }
}

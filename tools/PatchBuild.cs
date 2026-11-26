using System.Diagnostics;
using Comet.Patch;

static class PatchBuild
{
    public static int Run(
        string repoRoot,
        string clientDir,
        string patchDir,
        bool buildZip,
        int keepVersions,
        string minLauncherVersion)
    {
        if (!Directory.Exists(clientDir))
        {
            Console.Error.WriteLine($"No built client at {clientDir}");
            return 1;
        }
        if (keepVersions < 1)
        {
            Console.Error.WriteLine($"--keep must be at least 1, got {keepVersions}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var gitSha = ReadGitShortSha(repoRoot);
        var result = PatchPublisher.Publish(new PatchPublishOptions
        {
            ClientDirectory = clientDir,
            PatchDirectory = patchDir,
            GitShortSha = gitSha,
            MinLauncherVersion = minLauncherVersion,
            KeepVersions = keepVersions,
            BuildZip = buildZip,
        });

        var manifest = result.Manifest;
        var diff = result.Diff;
        var status = result.PublishedNewManifest ? $"published {manifest.Version}" : $"unchanged ({manifest.Version})";
        Console.WriteLine(
            $"{patchDir}: {status}; {manifest.Files.Count} files" +
            (manifest.Launcher != null ? " + launcher" : "") +
            $" (+{diff.Added.Count} ~{diff.Changed.Count} -{diff.Removed.Count}); " +
            $"objects: {result.ObjectsWritten} written, {result.ObjectsReused} reused, {result.ObjectsGarbageCollected} gc'd; " +
            $"manifests gc'd: {result.ManifestsGarbageCollected}" +
            (result.ZipBuilt ? $"; zip: {result.ZipPath}" : "") +
            $" ({sw.Elapsed.TotalSeconds:F1}s)");

        return 0;
    }

    static string ReadGitShortSha(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return "nogit";
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 && output.Length > 0 ? output : "nogit";
        }
        catch
        {
            return "nogit";
        }
    }
}

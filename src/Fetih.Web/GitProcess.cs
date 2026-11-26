namespace Fetih.Web;

using System.Diagnostics;

public static class GitProcess
{
    public static async Task<(bool Ok, string Stdout)> RunAsync(
        string workingDirectory, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(workingDirectory))
            return (false, "");

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return (false, "");

            CancellationTokenSource? linked = null;
            if (timeout is { } t)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(t);
            }
            using (linked)
            {
                var token = linked?.Token ?? ct;
                var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
                var stderrTask = process.StandardError.ReadToEndAsync(token);
                await Task.WhenAll(stdoutTask, stderrTask);
                await process.WaitForExitAsync(token);
                return (process.ExitCode == 0, stdoutTask.Result);
            }
        }
        catch
        {
            return (false, "");
        }
    }
}

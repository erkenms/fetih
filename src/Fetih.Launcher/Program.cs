using System.Diagnostics;
using Fetih.Launcher;
using Fetih.Patch;
using static Fetih.Launcher.Native;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

// Fetih.exe [--channel prod|test] [--repair] | --uninstall | --replace <pid> [args]
var self = Environment.ProcessPath!;
var root = Path.GetDirectoryName(self)!;

try
{
    if (args is ["--uninstall"])
    {
        Install.Uninstall();
        return 0;
    }

    // Self-update: Fetih.new.exe waits for the old launcher, takes its place and starts it
    if (args is ["--replace", var pidText, .. var rest])
    {
        try
        {
            using var old = Process.GetProcessById(int.Parse(pidText));
            old.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // Already gone
        }
        var target = Path.Combine(root, "Fetih.exe");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(self, target, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(250);
            }
        }
        Restart(target, rest);
        return 0;
    }

    var embedded = Art.Bytes(LauncherSettings.FileName);

    // First start, from wherever the browser saved it
    if (!Install.IsInstalledCopy(self))
    {
        Install.Run(self, embedded);
        Restart(Install.Exe, args);
        return 0;
    }

    var leftover = Path.Combine(root, "Fetih.new.exe");
    for (var attempt = 0; attempt < 20 && File.Exists(leftover); attempt++)
    {
        try
        {
            File.Delete(leftover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Thread.Sleep(250);
        }
    }

    var settingsPath = Path.Combine(root, LauncherSettings.FileName);
    var settings = LauncherSettings.Parse(File.Exists(settingsPath)
        ? File.ReadAllBytes(settingsPath)
        : embedded ?? throw new FileNotFoundException("launcher.json yok.", settingsPath));

    var channel = settings.Channel;
    var repair = false;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--channel" && i + 1 < args.Length)
            channel = args[++i];
        else if (args[i] == "--repair")
            repair = true;
    }
    if (channel is not ("prod" or "test"))
        throw new ArgumentException($"Bilinmeyen kanal: {channel}");

    return LauncherWindow.Run(new Updater(root, self, settings, channel, repair, args));
}
catch (Exception ex)
{
    MessageBox(0, ex.Message, Install.Name, MB_ICONERROR);
    return 1;
}

static void Restart(string exe, IEnumerable<string> args)
{
    var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
    foreach (var arg in args)
        start.ArgumentList.Add(arg);
    Process.Start(start);
}

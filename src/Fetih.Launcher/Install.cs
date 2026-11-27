using System.Diagnostics;
using Microsoft.Win32;

namespace Fetih.Launcher;

// %LOCALAPPDATA%\Programs\Fetih, shortcuts and an uninstall entry under HKCU; no admin
static class Install
{
    public const string Name = "Fetih Online";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Fetih";

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Fetih");

    public static string Exe => Path.Combine(Folder, "Fetih.exe");

    static string[] Shortcuts =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"),
    ];

    public static bool IsInstalledCopy(string exePath) =>
        string.Equals(Path.GetFullPath(Path.GetDirectoryName(exePath)!).TrimEnd('\\'), Folder, StringComparison.OrdinalIgnoreCase);

    // Copies this exe into the folder (unless a running copy holds it), writes launcher.json
    // when missing, and (re)creates the shortcuts and the uninstall entry
    public static void Run(string selfPath, byte[]? embeddedSettings)
    {
        Directory.CreateDirectory(Folder);
        try
        {
            File.Copy(selfPath, Exe, overwrite: true);
        }
        catch (IOException) when (File.Exists(Exe))
        {
            // The installed launcher is open; it updates itself from the manifest anyway
        }

        var settings = Path.Combine(Folder, Fetih.Patch.LauncherSettings.FileName);
        if (!File.Exists(settings) && embeddedSettings != null)
            File.WriteAllBytes(settings, embeddedSettings);

        foreach (var link in Shortcuts)
            Shortcut.Create(link, Exe, "", Folder, Name);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", Name);
        key.SetValue("DisplayIcon", Exe);
        key.SetValue("Publisher", Name);
        key.SetValue("InstallLocation", Folder);
        key.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Uninstall()
    {
        var answer = Native.MessageBox(0,
            $"{Name} bilgisayardan kaldırılsın mı?\n\nOyun klasörü ({Folder}) ekran görüntüleri ve ayarlarla birlikte silinir.",
            Name, Native.MB_YESNO | Native.MB_ICONWARNING);
        if (answer != Native.IDYES)
            return;

        foreach (var link in Shortcuts)
            File.Delete(link);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        // This exe lives in the folder, so a shell outlives it and removes the folder
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{Folder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }
}

static unsafe class Shortcut
{
    // IShellLinkW and IPersistFile by vtable slot
    const int SetDescription = 7, SetWorkingDirectory = 9, SetArguments = 11, SetIconLocation = 17, SetPath = 20;
    const int QueryInterface = 0, Release = 2, PersistSave = 6;

    public static void Create(string linkPath, string target, string arguments, string workingDirectory, string description)
    {
        Native.CoInitializeEx(0, 2);
        var clsid = new Guid("00021401-0000-0000-C000-000000000046");
        var iidShellLink = new Guid("000214F9-0000-0000-C000-000000000046");
        var iidPersistFile = new Guid("0000010B-0000-0000-C000-000000000046");
        nint link = 0, file = 0;
        Check(Native.CoCreateInstance(&clsid, 0, 1, &iidShellLink, &link));
        try
        {
            CallString(link, SetPath, target);
            CallString(link, SetArguments, arguments);
            CallString(link, SetWorkingDirectory, workingDirectory);
            CallString(link, SetDescription, description);
            fixed (char* icon = target)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(link, SetIconLocation))(link, icon, 0));
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(link, QueryInterface))(link, &iidPersistFile, &file));
            fixed (char* path = linkPath)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(file, PersistSave))(file, path, 1));
        }
        finally
        {
            if (file != 0)
                ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(file, Release))(file);
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(link, Release))(link);
        }
    }

    static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    static void CallString(nint obj, int slot, string value)
    {
        fixed (char* p = value)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot(obj, slot))(obj, p));
    }

    static void Check(int hr)
    {
        if (hr < 0)
            throw new IOException($"Shortcut failed: 0x{hr:x8}");
    }
}

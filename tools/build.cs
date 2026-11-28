#!/usr/bin/env dotnet
// Builds the Fetih client (with the launcher), web assets, and launcher patches:
//
//   dotnet run tools/build.cs <command> [options]
//
// Commands:
//   client                         Build the client from the 5619 base, with the launcher
//   assets [--maps all|ids]        Build web assets from the built client
//   patch [--keep N]               Publish the patch files and download/Fetih.exe
//   all [--maps all|ids]           Build client, assets, and patch
//   decrypt <file>                 Decrypt an RSA-signed client .dat file to stdout
//   extract <client> <path> [dest] Copy one client file (loose or .wdf), or convert to .png
//
// Paths:
//   --base <dir>       Base 5619 client (default: _CLIENT/co_5619)
//   --art <dir>        Old Turkish client with the launcher's Play/ art (default: _CLIENT/fo_3105)
//   --out <dir>        Built client (default: _CLIENT/fetih)
//   --assets <dir>     Web assets output (default: web/assets)
//   --patch <dir>      Patch output (default: _CLIENT/patch)
//   --ip <ipv4>        Account server IPv4 for Server.dat (or FETIH_ACCOUNT_IP; required
//                      for client and all)
//   --url <url>        The production site, baked into launcher.json (or FETIH_URL; required
//                      for client and all, used by every realm)
//   --maps all|ids     Render map tiles (all, or comma-separated map IDs like 1002,1036)
//   --keep <N>         Number of manifest versions to retain during GC (default: 10)
//
// Client build: mirrors the base client, minus client/delete.txt, plus client/files/,
// generates Server.dat from client/servers.json with the given IP, edits ini/itemtype.dat
// from client/itemtype.json, swaps Flash bitmaps listed in client/flash.json, applies byte
// patches from client/patches.json, swaps the RSA modulus in Conquer.exe for client/rsa.pem
// (committed in git so every build signs with the same key), and re-signs the RSA-signed .dat
// files. Adds Fetih.exe (src/Fetih.Launcher) and launcher.json. Unchanged files are skipped.
#:project ../src/Fetih.Game/Fetih.Game.csproj
#:project ../src/Fetih.Patch/Fetih.Patch.csproj
#:package StbImageSharp@2.30.15
#:package SkiaSharp@3.119.1
#:package SkiaSharp.NativeAssets.Linux.NoDependencies@3.119.1
#:property PublishAot=false
#:property Nullable=enable
#:include ClientBuild.cs
#:include AssetsBuild.cs
#:include PatchBuild.cs
#:include LauncherBuild.cs
#:include assets/lib/Wdf.cs
#:include assets/lib/Dds.cs
#:include assets/lib/ClientFiles.cs
#:include assets/lib/Png.cs
#:include assets/lib/Output.cs
#:include assets/lib/Catalogs.cs
#:include assets/lib/C3.cs
#:include assets/Looks/Raster.cs
#:include assets/Looks/Looks.cs
#:include assets/Maps/MapFiles.cs
#:include assets/Maps/MapTiles.cs
#:include assets/Maps/TerrainNpcs.cs
#:include ../src/Fetih.Web/Features/Maps/MapTileRenderer.cs
using System.Runtime.CompilerServices;

var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptPath())!, ".."));
var clientInputsDir = Path.Combine(root, "client");
var dataDir = Path.Combine(root, "src", "Fetih.Game", "Data");

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

var command = args[0];
var rest = args[1..];

if (command == "extract")
{
    if (rest.Length is < 2 or > 3)
        return Fail("Usage: dotnet run tools/build.cs extract <client> <path> [dest]");
    return AssetsBuild.Extract(rest[0], rest[1], rest.Length > 2 ? rest[2] : null);
}

string? baseOverride = null;
string? artOverride = null;
string? outOverride = null;
string? assetsOverride = null;
string? patchOverride = null;
string? ip = Environment.GetEnvironmentVariable("FETIH_ACCOUNT_IP");
string? url = Environment.GetEnvironmentVariable("FETIH_URL");
HashSet<uint>? onlyMaps = null;
var keepVersions = 10;
var positionals = new List<string>();

for (var i = 0; i < rest.Length; i++)
{
    string Next(string opt) => i + 1 < rest.Length ? rest[++i] : throw new ArgumentException($"Missing value for {opt}");
    try
    {
        switch (rest[i])
        {
            case "--base":
                baseOverride = Next("--base");
                break;
            case "--art":
                artOverride = Next("--art");
                break;
            case "--out":
                outOverride = Next("--out");
                break;
            case "--assets":
                assetsOverride = Next("--assets");
                break;
            case "--patch":
                patchOverride = Next("--patch");
                break;
            case "--ip":
                ip = Next("--ip");
                break;
            case "--url":
                url = Next("--url");
                break;
            case "--maps":
                var mapsArg = Next("--maps");
                onlyMaps = mapsArg == "all" ? null : mapsArg.Split(',').Select(uint.Parse).ToHashSet();
                break;
            case "--keep":
                keepVersions = int.Parse(Next("--keep"));
                break;
            case "-h" or "--help":
                PrintUsage();
                return 0;
            default:
                if (rest[i].StartsWith('-'))
                    return Fail($"Unknown option: {rest[i]}");
                positionals.Add(rest[i]);
                break;
        }
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException)
    {
        return Fail(ex.Message);
    }
}

var baseDir = baseOverride ?? Path.Combine(root, "_CLIENT", "co_5619");
var artDir = artOverride ?? Path.Combine(root, "_CLIENT", "fo_3105");
var outDir = outOverride ?? Path.Combine(root, "_CLIENT", "fetih");
var assetsDir = assetsOverride ?? Path.Combine(root, "web", "assets");
var patchDir = patchOverride ?? Path.Combine(root, "_CLIENT", "patch");

switch (command)
{
    case "decrypt":
        if (positionals.Count != 1)
            return Fail("Usage: dotnet run tools/build.cs decrypt <file> [--base <dir>]");
        return ClientBuild.Decrypt(clientInputsDir, baseDir, positionals[0]);

    case "client":
        if (positionals.Count != 0)
            return Fail($"Unexpected arguments for client: {string.Join(' ', positionals)}");
        return BuildClient();

    case "assets":
        if (positionals.Count != 0)
            return Fail($"Unexpected arguments for assets: {string.Join(' ', positionals)}");
        return AssetsBuild.Run(outDir, assetsDir, dataDir, onlyMaps);

    case "patch":
        if (positionals.Count != 0)
            return Fail($"Unexpected arguments for patch: {string.Join(' ', positionals)}");
        return PatchBuild.Run(root, outDir, patchDir, keepVersions);

    case "all":
        if (positionals.Count != 0)
            return Fail($"Unexpected arguments for all: {string.Join(' ', positionals)}");
        var clientExit = BuildClient();
        if (clientExit != 0)
            return clientExit;
        var assetsExit = AssetsBuild.Run(outDir, assetsDir, dataDir, onlyMaps);
        if (assetsExit != 0)
            return assetsExit;
        return PatchBuild.Run(root, outDir, patchDir, keepVersions);

    default:
        return Fail($"Unknown command '{command}'. Run with --help for usage.");
}

int BuildClient()
{
    var launcher = LauncherBuild.Run(root, artDir, url);
    return launcher == null ? 1 : ClientBuild.Run(clientInputsDir, baseDir, outDir, ip, launcher);
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage: dotnet run tools/build.cs <command> [options]

        Commands:
          client                         Build the client from the 5619 base, with the launcher
          assets [--maps all|ids]        Build web assets from the built client
          patch [--keep N]               Publish the patch files and download/Fetih.exe
          all [--maps all|ids]           Build client, assets, and patch
          decrypt <file>                 Decrypt an RSA-signed client .dat file to stdout
          extract <client> <path> [dest] Copy one client file, or convert to .png

        Options:
          --base <dir>                   Base 5619 client (default: _CLIENT/5619)
          --art <dir>                    Old Turkish client with Play/ (default: _CLIENT/tr/app)
          --out <dir>                    Built client (default: _CLIENT/fetih)
          --assets <dir>                 Web assets output (default: web/assets)
          --patch <dir>                  Patch output (default: _CLIENT/patch)
          --ip <ipv4>                    Account server IPv4 for Server.dat (or FETIH_ACCOUNT_IP)
          --url <url>                    Production site for launcher.json (or FETIH_URL)
          --maps all|ids                 Map tiles to render (all or comma-separated IDs)
          --keep <N>                     Manifest versions to retain during GC (default: 10)
        """);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string ScriptPath([CallerFilePath] string path = "") => path;

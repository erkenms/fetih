#!/usr/bin/env dotnet
// Builds the web app's assets from the client:
//
//   dotnet run tools/assets/build.cs [client] [output]
//
// Defaults: _CLIENT/fetih into web/assets, both in the repository root. The output is the
// client's art, so it stays out of git. README.md lists what it holds. Runs again skip the
// images whose inputs haven't changed, and delete outputs the client no longer has.
//
// --maps all renders the map tiles too, --maps 1002,1036 only those maps'. Without it the tiles
// earlier runs wrote are kept. Rendering every map takes most of an hour and a few GB the
// first time; later runs skip the maps whose files haven't changed.
//
//   dotnet run tools/assets/build.cs extract <client> <path> [destination]
//
// copies one client file (loose or from a .wdf package) out, as it is, or as PNG when the
// destination ends in .png.
#:project ../../comet/src/Comet.Game/Comet.Game.csproj
#:package StbImageSharp@2.30.15
#:package SkiaSharp@3.119.1
#:property PublishAot=false
#:property Nullable=enable
#:include lib/Wdf.cs
#:include lib/Dds.cs
#:include lib/ClientFiles.cs
#:include lib/Png.cs
#:include lib/Output.cs
#:include lib/Catalogs.cs
#:include Maps/MapFiles.cs
#:include Maps/MapTiles.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;

var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptPath())!, "..", ".."));

if (args is ["extract", var clientDir, var file, ..])
{
    using var extractFiles = new ClientFiles(Path.GetFullPath(clientDir));
    var data = extractFiles.Read(file);
    if (data == null)
        return Fail($"{file} isn't in {clientDir}");
    var destination = args.Length > 3 ? args[3] : Path.GetFileName(file);
    File.WriteAllBytes(destination, destination.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? Images.Png(Images.Decode(data))
        : data);
    Console.WriteLine($"{file} -> {destination}");
    return 0;
}

HashSet<uint>? onlyMaps = [];
if (Array.IndexOf(args, "--maps") is var mapsAt and >= 0 && mapsAt + 1 < args.Length)
{
    onlyMaps = args[mapsAt + 1] == "all" ? null : args[mapsAt + 1].Split(',').Select(uint.Parse).ToHashSet();
    args = [.. args[..mapsAt], .. args[(mapsAt + 2)..]];
}

var client = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "_CLIENT", "fetih"));
var output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(root, "web", "assets"));
if (!File.Exists(Path.Combine(client, "ini", "itemtype.dat")))
    return Fail($"No client at {client}; build it with client/build.cs or pass its folder");

var clock = Stopwatch.StartNew();
using var files = new ClientFiles(client);
Directory.CreateDirectory(output);
var outputs = new Output(output, Catalogs.Version);
var catalogs = new Catalogs(files, outputs, Path.Combine(root, "comet", "src", "Comet.Game", "Data"));
catalogs.BuildAll();
var mapTiles = new MapTiles(files, outputs);
mapTiles.Build(onlyMaps);
var removed = outputs.Finish();

foreach (var error in outputs.Errors.Order().Take(20))
    Console.Error.WriteLine($"  can't read {error}");
if (outputs.Errors.Count > 20)
    Console.Error.WriteLine($"  ...and {outputs.Errors.Count - 20} more");
foreach (var error in mapTiles.Errors.Distinct().Order().Take(20))
    Console.Error.WriteLine($"  map: {error}");
foreach (var line in catalogs.Summary.Concat(mapTiles.Summary))
    Console.WriteLine(line);
Console.WriteLine($"{outputs.Written} written, {outputs.Skipped} unchanged, {removed} removed, " +
    $"{outputs.Errors.Count} unreadable, in {clock.Elapsed.TotalSeconds:0.0}s -> {output}");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string ScriptPath([CallerFilePath] string path = "") => path;

using System.Diagnostics;

static class AssetsBuild
{
    public static int Extract(string clientDir, string file, string? destination)
    {
        using var extractFiles = new ClientFiles(Path.GetFullPath(clientDir));
        var data = extractFiles.Read(file);
        if (data == null)
        {
            Console.Error.WriteLine($"{file} isn't in {clientDir}");
            return 1;
        }
        var dest = destination ?? Path.GetFileName(file);
        File.WriteAllBytes(dest, dest.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? Images.Png(Images.Decode(data))
            : data);
        Console.WriteLine($"{file} -> {dest}");
        return 0;
    }

    public static int Run(string client, string output, string dataDir, HashSet<uint>? onlyMaps)
    {
        client = Path.GetFullPath(client);
        output = Path.GetFullPath(output);
        if (!File.Exists(Path.Combine(client, "ini", "itemtype.dat")))
        {
            Console.Error.WriteLine($"No client at {client}; build it with 'dotnet run tools/build.cs client' or pass --out");
            return 1;
        }

        var clock = Stopwatch.StartNew();
        using var files = new ClientFiles(client);
        Directory.CreateDirectory(output);
        var outputs = new Output(output, Catalogs.Version);
        var catalogs = new Catalogs(files, outputs, dataDir);
        catalogs.BuildAll();
        var looks = new Looks(files, outputs, dataDir);
        looks.Build();
        var terrainNpcs = new TerrainNpcs(files, outputs);
        terrainNpcs.Build();
        var mapTiles = new MapTiles(files, outputs);
        mapTiles.Build(onlyMaps);
        var removed = outputs.Finish();

        foreach (var error in outputs.Errors.Order().Take(20))
            Console.Error.WriteLine($"  can't read {error}");
        if (outputs.Errors.Count > 20)
            Console.Error.WriteLine($"  ...and {outputs.Errors.Count - 20} more");
        foreach (var error in mapTiles.Errors.Distinct().Order().Take(20))
            Console.Error.WriteLine($"  map: {error}");
        foreach (var line in catalogs.Summary.Concat(looks.Summary).Concat(terrainNpcs.Summary).Concat(mapTiles.Summary))
            Console.WriteLine(line);
        Console.WriteLine($"{outputs.Written} written, {outputs.Skipped} unchanged, {removed} removed, " +
            $"{outputs.Errors.Count} unreadable, in {clock.Elapsed.TotalSeconds:0.0}s -> {output}");
        return 0;
    }
}

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The NPC and monster looks as pictures: each model in the first frame of its standing
// motion, seen the way the client draws roles.
//
// An NPC's lookface is its npc.ini [NpcType<lookface / 10>] and its direction (lookface % 10).
// The type names a 3DSimpleObj.ini [ObjIDType<SimpleObjID>], whose Part<n> and Texture<n> are
// keys of 3dobj.ini (the C3 mesh) and 3dtexture.ini, and a StandByMotion. A monster's look L
// is the mesh and texture L * 1000000 and the motion 0LLL000100 (look, weapon 000, action
// 100, standing) in 3dmotion.ini.
//
// The camera is the client's: orthographic, one model unit to a pixel, the role turned by
// -45 * (direction + 1) degrees around z and tilted by -30 degrees around x
// (g_nObliqueAngle), then seen along +y with -z up.
sealed partial class Looks(ClientFiles files, Output output, string serverData)
{
    public List<string> Summary { get; } = new();

    // The direction a look is shown in when nothing asks for another: facing down left
    public const int PreviewDirection = 0;

    private const int Margin = 4;
    private const int MaxSize = 1024;
    private const float Oblique = -30;

    private Dictionary<ulong, string> objects = new(), textures = new(), motions = new();
    private readonly Dictionary<string, C3File?> c3Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rgba?> textureCache = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Sprite(string Image, int X, int Y);

    public void Build()
    {
        objects = Table("ini/3dobj.ini");
        textures = Table("ini/3dtexture.ini");
        motions = Table("ini/3dmotion.ini");

        var npcTypes = Sections("ini/npc.ini", "NpcType");
        var simpleObjects = Sections("ini/3DSimpleObj.ini", "ObjIDType");

        // Every NPC type in its preview direction, and every lookface the server's data uses
        var wanted = new SortedSet<uint>(npcTypes.Keys.Select(type => type * 10 + PreviewDirection));
        foreach (var lookface in ServerLookfaces())
            if (npcTypes.ContainsKey(lookface / 10))
                wanted.Add(lookface);
        var npc = new SortedDictionary<uint, Sprite>();
        var npcFailed = 0;
        foreach (var lookface in wanted)
        {
            var type = npcTypes[lookface / 10];
            if (!uint.TryParse(type.GetValueOrDefault("SimpleObjID"), out var objectId)
                || !simpleObjects.TryGetValue(objectId, out var parts))
            {
                npcFailed++;
                continue;
            }
            var meshes = new List<(string Mesh, string Texture)>();
            var amount = int.TryParse(parts.GetValueOrDefault("PartAmount"), out var n) ? n : 1;
            for (var i = 0; i < amount; i++)
                if (ulong.TryParse(parts.GetValueOrDefault($"Part{i}"), out var part) && objects.TryGetValue(part, out var mesh)
                    && ulong.TryParse(parts.GetValueOrDefault($"Texture{i}"), out var texture) && textures.TryGetValue(texture, out var picture))
                    meshes.Add((mesh, picture));
            var motion = ulong.TryParse(type.GetValueOrDefault("StandByMotion"), out var motionId) ? NpcMotion(motionId) : null;
            if (Render($"looks/npc/{lookface}.png", meshes, motion, (int)(lookface % 10)) is { } sprite)
                npc[lookface] = sprite;
            else
                npcFailed++;
        }

        var monster = new SortedDictionary<uint, Sprite>();
        var monsterLooks = MonsterLooks();
        foreach (var look in monsterLooks)
        {
            ulong key = look * 1000000UL;
            var motion = motions.GetValueOrDefault(key + 100);
            // Many looks are another look recolored or renamed: no mesh or texture of their
            // own, and a motion from the other look's folder (c3/monster/125/100.c3). Their
            // missing parts come from that look, else from the motion's file, which often
            // carries a mesh. The build's guess, not traced in the client.
            ulong borrowed = motion != null && MonsterFolder().Match(motion) is { Success: true } folder
                ? ulong.Parse(folder.Groups[1].Value, CultureInfo.InvariantCulture) * 1000000UL
                : key;
            var mesh = objects.GetValueOrDefault(key) ?? objects.GetValueOrDefault(borrowed) ?? motion;
            var texture = textures.GetValueOrDefault(key) ?? textures.GetValueOrDefault(borrowed);
            if (mesh == null || texture == null)
                continue;
            if (Render($"looks/monster/{look}.png", [(mesh, texture)], motion, PreviewDirection) is { } sprite)
                monster[look] = sprite;
        }

        output.Write("looks.json", new { PreviewDirection, Npc = npc, Monster = monster });
        Summary.Add($"looks.json: {npc.Count} NPC looks ({npcFailed} without a model), " +
            $"{monster.Count} of {monsterLooks.Count} monster looks");
    }

    // npc.ini writes a motion as look, then a 4-digit action (9990010100); 3dmotion.ini keys
    // it with a 3-digit action (0999001100). Some types name a key as it is.
    private string? NpcMotion(ulong id) =>
        motions.GetValueOrDefault(id) ?? motions.GetValueOrDefault(id / 10000 * 1000 + id % 10000 % 1000);

    private Sprite? Render(string path, List<(string Mesh, string Texture)> parts, string? motion, int direction)
    {
        if (parts.Count == 0)
            return null;
        var stamps = parts.SelectMany(p => new[] { p.Mesh, p.Texture }).Append(motion ?? "").Select(f => $"{f}@{files.Stamp(f)}");
        var stamp = $"{direction}|{string.Join('|', stamps)}";

        // The size and anchor are needed for looks.json even when the picture is current, so
        // the model is always read; only drawing it is skipped
        List<Raster.Triangle> triangles;
        try
        {
            triangles = Triangles(parts, motion, direction);
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            output.Errors.Add($"{path}: {e.Message}");
            return null;
        }
        if (triangles.Count == 0)
            return null;
        var points = triangles.SelectMany(t => new[] { t.A.Screen, t.B.Screen, t.C.Screen }).ToList();
        float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        // The anchor, where the role stands, is the model's origin
        int left = (int)MathF.Floor(minX) - Margin, top = (int)MathF.Floor(minY) - Margin;
        int width = (int)MathF.Ceiling(maxX) + Margin - left, height = (int)MathF.Ceiling(maxY) + Margin - top;
        if (width > MaxSize || height > MaxSize)
        {
            output.Errors.Add($"{path}: {width}x{height}, too big to be a role");
            return null;
        }
        var shift = new Vector3(-left, -top, 0);
        var moved = triangles.Select(t => t with
        {
            A = t.A with { Screen = t.A.Screen + shift },
            B = t.B with { Screen = t.B.Screen + shift },
            C = t.C with { Screen = t.C.Screen + shift },
        }).ToList();
        return output.Image(path, stamp, () => Raster.Draw(moved, width, height)) ? new Sprite(path, -left, -top) : null;
    }

    private List<Raster.Triangle> Triangles(List<(string Mesh, string Texture)> parts, string? motionPath, int direction)
    {
        var standing = motionPath != null ? C3(motionPath)?.Motions.FirstOrDefault() : null;
        var view = Matrix4x4.CreateRotationZ(-MathF.PI / 4 * (direction + 1)) * Matrix4x4.CreateRotationX(Oblique * MathF.PI / 180);
        var triangles = new List<Raster.Triangle>();
        foreach (var (meshPath, texturePath) in parts)
        {
            var file = C3(meshPath);
            var texture = Texture(texturePath);
            if (file == null || texture == null)
                continue;
            var motion = standing ?? file.Motions.FirstOrDefault();
            foreach (var mesh in file.Meshes)
            {
                var screen = new Vector3[mesh.Vertices.Length];
                for (var i = 0; i < screen.Length; i++)
                {
                    var v = mesh.Vertices[i];
                    var skinned = Vector3.Transform(v.Position, mesh.Initial * Bone(motion, v.Bone0)) * v.Weight0;
                    if (v.Weight1 > 0)
                        skinned += Vector3.Transform(v.Position, mesh.Initial * Bone(motion, v.Bone1)) * v.Weight1;
                    var p = Vector3.Transform(skinned, view);
                    // Screen x right, y down (+z), nearer larger (the camera looks along +y)
                    screen[i] = new Vector3(p.X, p.Z, -p.Y);
                }
                for (var t = 0; t < mesh.Triangles.Length / 3; t++)
                {
                    int a = mesh.Triangles[t * 3], b = mesh.Triangles[t * 3 + 1], c = mesh.Triangles[t * 3 + 2];
                    triangles.Add(new Raster.Triangle(
                        new(screen[a], mesh.Vertices[a].Uv), new(screen[b], mesh.Vertices[b].Uv), new(screen[c], mesh.Vertices[c].Uv),
                        texture, Alpha: t >= mesh.NormalTriangles));
                }
            }
        }
        return triangles;
    }

    private static Matrix4x4 Bone(C3Motion? motion, int bone) =>
        motion != null && bone >= 0 && bone < motion.Pose.Length ? motion.Pose[bone] : Matrix4x4.Identity;

    private C3File? C3(string path)
    {
        if (!c3Cache.TryGetValue(path, out var file))
            c3Cache[path] = file = files.Read(path) is { } data && C3File.IsC3(data) ? C3File.Read(data) : null;
        return file;
    }

    private Rgba? Texture(string path)
    {
        if (!textureCache.TryGetValue(path, out var texture))
        {
            try
            {
                texture = files.Read(path) is { } data ? Images.Decode(data) : null;
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                texture = null;
            }
            textureCache[path] = texture;
        }
        return texture;
    }

    // "key=value" lines with numeric keys, as 3dobj.ini, 3dtexture.ini and 3dmotion.ini have
    private Dictionary<ulong, string> Table(string path)
    {
        var table = new Dictionary<ulong, string>();
        foreach (var raw in ClientFiles.Gbk.GetString(files.Read(path) ?? []).Split('\n'))
        {
            var eq = raw.IndexOf('=');
            if (eq > 0 && ulong.TryParse(raw.AsSpan(0, eq).Trim(), CultureInfo.InvariantCulture, out var key))
                table.TryAdd(key, raw[(eq + 1)..].Trim().Replace('\\', '/'));
        }
        return table;
    }

    // [<prefix><n>] sections of an ini file by n
    private Dictionary<uint, Dictionary<string, string>> Sections(string path, string prefix)
    {
        var sections = new Dictionary<uint, Dictionary<string, string>>();
        Dictionary<string, string>? section = null;
        foreach (var raw in ClientFiles.Gbk.GetString(files.Read(path) ?? []).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = null;
                if (line[1..^1].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && uint.TryParse(line[(1 + prefix.Length)..^1], out var id))
                    sections.TryAdd(id, section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                continue;
            }
            var eq = line.IndexOf('=');
            if (section != null && eq > 0)
                section.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        return sections;
    }

    // Every "lookface" in the server's data files: NPCs, training goals, teleporters
    private IEnumerable<uint> ServerLookfaces()
    {
        if (!Directory.Exists(serverData))
            yield break;
        foreach (var file in Directory.EnumerateFiles(serverData, "*.json"))
        {
            if (Path.GetFileName(file).Equals("monsters.json", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (Match match in LookfaceField().Matches(File.ReadAllText(file)))
                yield return uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }

    // The server's monster looks, and every other look with a standing monster motion
    private SortedSet<uint> MonsterLooks()
    {
        var looks = new SortedSet<uint>();
        var path = Path.Combine(serverData, "monsters.json");
        if (File.Exists(path))
            foreach (Match match in LookfaceField().Matches(File.ReadAllText(path)))
                looks.Add(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        foreach (var (key, file) in motions)
            if (key % 1000000 == 100 && key / 1000000 is > 0 and < 10000 && file.StartsWith("c3/monster/", StringComparison.OrdinalIgnoreCase))
                looks.Add((uint)(key / 1000000));
        return looks;
    }

    [GeneratedRegex("^c3/monster/(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MonsterFolder();

    [GeneratedRegex("\"lookface\"\\s*:\\s*(\\d+)")]
    private static partial Regex LookfaceField();
}

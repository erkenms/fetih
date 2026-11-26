using System.Buffers.Binary;
using System.Numerics;
using System.Text;

// A C3 file: "MAXFILE C3 00001", then chunks of a 4-byte tag, a uint32 size and the data.
// PHY chunks are skinned meshes, MOTI chunks bone animations, CAME a camera path. Other chunks
// (particles, shapes) are skipped. The same file can hold a mesh and its motion.
sealed class C3File
{
    public List<C3Mesh> Meshes { get; } = new();
    public List<C3Motion> Motions { get; } = new();

    public static bool IsC3(ReadOnlySpan<byte> data) => data.Length >= 16 && data[..10].SequenceEqual("MAXFILE C3"u8);

    public static C3File Read(byte[] data)
    {
        if (!IsC3(data))
            throw new InvalidDataException("not a C3 file");
        var file = new C3File();
        var at = 16;
        while (at + 8 <= data.Length)
        {
            var tag = Encoding.ASCII.GetString(data, at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 4));
            if (size < 0 || at + 8 + size > data.Length)
                throw new InvalidDataException($"chunk {tag} runs past the end");
            var chunk = new C3Reader(data.AsMemory(at + 8, size));
            if (tag.StartsWith("PHY"))
                file.Meshes.Add(C3Mesh.Read(chunk, morphs: tag == "PHY4" ? 1 : 4));
            else if (tag == "MOTI")
                file.Motions.Add(C3Motion.Read(chunk));
            at += 8 + size;
        }
        return file;
    }
}

// PHY: name, bones per vertex, normal and alpha vertex counts, then the vertices:
//   float3 position[4] (morph targets, only the first is used), float u, v, uint32 color,
//   uint32 bone[2], float weight[2]                                         = 76 bytes
// (PHY4 has one position, 40 bytes),
// then normal and alpha triangle counts and uint16[3] per triangle, the texture's file name
// (unused: the ini files name the texture), the bounding box and the mesh's initial matrix.
// Alpha triangles index the alpha vertices, which follow the normal ones.
sealed class C3Mesh
{
    public required string Name { get; init; }
    public required C3Vertex[] Vertices { get; init; }
    public required int NormalVertices { get; init; }
    public required ushort[] Triangles { get; init; }
    public required int NormalTriangles { get; init; }
    public required Matrix4x4 Initial { get; init; }

    public static C3Mesh Read(C3Reader r, int morphs)
    {
        var name = r.String();
        r.UInt32();
        int normal = r.Int32(), alpha = r.Int32();
        var vertices = new C3Vertex[normal + alpha];
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = r.Vector3();
            r.Skip(12 * (morphs - 1));
            var uv = new Vector2(r.Single(), r.Single());
            r.UInt32();
            vertices[i] = new C3Vertex(position, uv, r.Int32(), r.Int32(), r.Single(), r.Single());
        }
        int normalTriangles = r.Int32(), alphaTriangles = r.Int32();
        var triangles = new ushort[(normalTriangles + alphaTriangles) * 3];
        for (var i = 0; i < triangles.Length; i++)
            triangles[i] = r.UInt16();
        r.String();
        r.Skip(24);
        var initial = r.Matrix();
        return new C3Mesh
        {
            Name = name, Vertices = vertices, NormalVertices = normal,
            Triangles = triangles, NormalTriangles = normalTriangles, Initial = initial,
        };
    }
}

readonly record struct C3Vertex(Vector3 Position, Vector2 Uv, int Bone0, int Bone1, float Weight0, float Weight1);

// MOTI: bone and frame counts, then the keyframes in one of four layouts:
//   (no tag)  a 4x4 matrix per bone per frame, bone by bone
//   KKEY      uint32 count, then per keyframe uint32 frame and a 4x4 matrix per bone
//   XKEY      uint32 count, then per keyframe uint16 frame and a 4x3 matrix per bone
//   ZKEY      uint32 count, then per keyframe uint16 frame and per bone a quaternion and a
//             translation
// then uint32 morph count and a float per morph per frame.
sealed class C3Motion
{
    public required int Bones { get; init; }
    public required int Frames { get; init; }
    // The first keyframe: one matrix per bone
    public required Matrix4x4[] Pose { get; init; }

    public static C3Motion Read(C3Reader r)
    {
        int bones = r.Int32(), frames = r.Int32();
        var pose = new Matrix4x4[bones];
        var tag = Encoding.ASCII.GetString(r.Peek(4));
        switch (tag)
        {
            case "KKEY":
                r.Skip(4);
                if (r.Int32() > 0)
                {
                    r.UInt32();
                    for (var b = 0; b < bones; b++)
                        pose[b] = r.Matrix();
                }
                break;
            case "XKEY":
                r.Skip(4);
                if (r.Int32() > 0)
                {
                    r.UInt16();
                    for (var b = 0; b < bones; b++)
                        pose[b] = new Matrix4x4(
                            r.Single(), r.Single(), r.Single(), 0,
                            r.Single(), r.Single(), r.Single(), 0,
                            r.Single(), r.Single(), r.Single(), 0,
                            r.Single(), r.Single(), r.Single(), 1);
                }
                break;
            case "ZKEY":
                r.Skip(4);
                if (r.Int32() > 0)
                {
                    r.UInt16();
                    for (var b = 0; b < bones; b++)
                    {
                        var rotation = new Quaternion(r.Single(), r.Single(), r.Single(), r.Single());
                        var translation = r.Vector3();
                        pose[b] = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
                    }
                }
                break;
            default:
                for (var b = 0; b < bones; b++)
                {
                    pose[b] = r.Matrix();
                    r.Skip(64 * (frames - 1));
                }
                break;
        }
        return new C3Motion { Bones = bones, Frames = frames, Pose = pose };
    }
}

sealed class C3Reader(ReadOnlyMemory<byte> data)
{
    private int at;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (at + count > data.Length)
            throw new InvalidDataException("C3 chunk ends early");
        var span = data.Span.Slice(at, count);
        at += count;
        return span;
    }

    public ReadOnlySpan<byte> Peek(int count) => data.Span.Slice(at, Math.Min(count, data.Length - at));
    public void Skip(int count) => Take(count);
    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public Vector3 Vector3() => new(Single(), Single(), Single());
    public string String() => Encoding.Latin1.GetString(Take(Int32()));

    public Matrix4x4 Matrix() => new(
        Single(), Single(), Single(), Single(),
        Single(), Single(), Single(), Single(),
        Single(), Single(), Single(), Single(),
        Single(), Single(), Single(), Single());
}

using System.Numerics;

// A small software rasterizer for the look sprites: textured triangles with a depth buffer,
// no lighting (the client draws roles unlit). Opaque triangles drop texels under half alpha;
// alpha triangles blend over them afterwards, farthest first. Drawn at Supersample times the
// size and averaged down, which smooths the edges.
sealed class Raster
{
    public const int Supersample = 3;

    public readonly record struct Point(Vector3 Screen, Vector2 Uv);
    public readonly record struct Triangle(Point A, Point B, Point C, Rgba Texture, bool Alpha);

    // Draws the triangles (screen x right, y down, z larger = nearer, in output pixels) into a
    // width x height picture
    public static Rgba Draw(IReadOnlyList<Triangle> triangles, int width, int height)
    {
        int w = width * Supersample, h = height * Supersample;
        var color = new float[w * h * 4];
        var depth = new float[w * h];
        Array.Fill(depth, float.NegativeInfinity);
        foreach (var triangle in triangles)
            if (!triangle.Alpha)
                Fill(triangle, w, h, color, depth, blend: false);
        foreach (var triangle in triangles.Where(t => t.Alpha)
                     .OrderBy(t => t.A.Screen.Z + t.B.Screen.Z + t.C.Screen.Z))
            Fill(triangle, w, h, color, depth, blend: true);

        var pixels = new byte[width * height * 4];
        const int samples = Supersample * Supersample;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var sy = 0; sy < Supersample; sy++)
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var i = ((y * Supersample + sy) * w + x * Supersample + sx) * 4;
                        r += color[i];
                        g += color[i + 1];
                        b += color[i + 2];
                        a += color[i + 3];
                    }
                var o = (y * width + x) * 4;
                if (a <= 0)
                    continue;
                // Premultiplied while summing, straight in the PNG
                pixels[o] = (byte)Math.Clamp(r / a * 255, 0, 255);
                pixels[o + 1] = (byte)Math.Clamp(g / a * 255, 0, 255);
                pixels[o + 2] = (byte)Math.Clamp(b / a * 255, 0, 255);
                pixels[o + 3] = (byte)Math.Clamp(a / samples * 255, 0, 255);
            }
        return new Rgba(width, height, pixels);
    }

    private static void Fill(Triangle t, int w, int h, float[] color, float[] depth, bool blend)
    {
        Vector3 a = t.A.Screen * new Vector3(Supersample, Supersample, 1),
            b = t.B.Screen * new Vector3(Supersample, Supersample, 1),
            c = t.C.Screen * new Vector3(Supersample, Supersample, 1);
        var area = Edge(a, b, c.X, c.Y);
        if (Math.Abs(area) < 1e-6f)
            return;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))),
            maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)))),
            minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))),
            maxY = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        var texture = t.Texture;
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float wa = Edge(b, c, px, py) / area, wb = Edge(c, a, px, py) / area, wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0)
                    continue;
                var z = wa * a.Z + wb * b.Z + wc * c.Z;
                var i = y * w + x;
                if (z <= depth[i])
                    continue;
                var uv = wa * t.A.Uv + wb * t.B.Uv + wc * t.C.Uv;
                var (r, g, bl, al) = Sample(texture, uv);
                if (!blend)
                {
                    if (al < 0.5f)
                        continue;
                    depth[i] = z;
                    color[i * 4] = r;
                    color[i * 4 + 1] = g;
                    color[i * 4 + 2] = bl;
                    color[i * 4 + 3] = 1;
                }
                else
                {
                    if (al <= 0)
                        continue;
                    var keep = 1 - al;
                    color[i * 4] = r * al + color[i * 4] * keep;
                    color[i * 4 + 1] = g * al + color[i * 4 + 1] * keep;
                    color[i * 4 + 2] = bl * al + color[i * 4 + 2] * keep;
                    color[i * 4 + 3] = al + color[i * 4 + 3] * keep;
                }
            }
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    // Nearest texel, texture coordinates wrapping
    private static (float R, float G, float B, float A) Sample(Rgba texture, Vector2 uv)
    {
        var u = uv.X - MathF.Floor(uv.X);
        var v = uv.Y - MathF.Floor(uv.Y);
        var x = Math.Min(texture.Width - 1, (int)(u * texture.Width));
        var y = Math.Min(texture.Height - 1, (int)(v * texture.Height));
        var i = (y * texture.Width + x) * 4;
        var p = texture.Pixels;
        return (p[i] / 255f, p[i + 1] / 255f, p[i + 2] / 255f, p[i + 3] / 255f);
    }
}

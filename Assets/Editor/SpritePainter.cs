using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Draws the sprites of the 2D shorts in code, the way the 3D shorts make their textures: shapes as signed distance
// fields (negative inside), filled with flat colours and an even dark outline, anti-aliased over one pixel. A painter
// covers an area in units around the sprite's pivot (0, 0); Save writes the PNG and imports it as a sprite with that
// pivot, so a part's joint (a shoulder, a hip) can be its pivot.
public class SpritePainter
{
    readonly Rect area;
    readonly float ppu;
    readonly int width, height;
    readonly Color[] pixels;

    public SpritePainter(Rect area, float pixelsPerUnit)
    {
        this.area = area;
        ppu = pixelsPerUnit;
        width = Mathf.CeilToInt(area.width * ppu);
        height = Mathf.CeilToInt(area.height * ppu);
        pixels = new Color[width * height];
    }

    // Paints the shape over what is there
    public void Fill(Shape shape, Color color)
    {
        var b = shape.bounds;
        int x0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - area.xMin) * ppu) - 2), x1 = Mathf.Min(width - 1, Mathf.CeilToInt((b.xMax - area.xMin) * ppu) + 2);
        int y0 = Mathf.Max(0, Mathf.FloorToInt((b.yMin - area.yMin) * ppu) - 2), y1 = Mathf.Min(height - 1, Mathf.CeilToInt((b.yMax - area.yMin) * ppu) + 2);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(area.xMin + (x + 0.5f) / ppu, area.yMin + (y + 0.5f) / ppu);
                float a = Mathf.Clamp01(0.5f - shape.sdf(p) * ppu) * color.a;
                if (a <= 0f) continue;
                ref var dst = ref pixels[y * width + x];
                float outA = a + dst.a * (1f - a);
                dst = new Color((color.r * a + dst.r * dst.a * (1f - a)) / outA, (color.g * a + dst.g * dst.a * (1f - a)) / outA,
                    (color.b * a + dst.b * dst.a * (1f - a)) / outA, outA);
            }
    }

    // The whole area, `bottom` colour at the bottom to `top` at the top (a sky)
    public void VerticalGradient(Color bottom, Color top)
    {
        for (int y = 0; y < height; y++)
        {
            var c = Color.Lerp(bottom, top, (y + 0.5f) / height);
            for (int x = 0; x < width; x++) pixels[y * width + x] = c;
        }
    }

    // An outlined shape: the line all round it, then the fill inside
    public void Draw(Shape shape, Color fill, Color line, float lineWidth)
    {
        Fill(shape.Grow(lineWidth), line);
        Fill(shape, fill);
    }

    // Writes the PNG and imports it as a sprite pivoted on (0, 0) of the area
    public Sprite Save(string path)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 4096;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(-area.xMin / area.width, -area.yMin / area.height);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}

// A shape: its signed distance (units, negative inside) and the box it lies in
public readonly struct Shape
{
    public readonly Func<Vector2, float> sdf;
    public readonly Rect bounds;

    public Shape(Func<Vector2, float> sdf, Rect bounds)
    {
        this.sdf = sdf;
        this.bounds = bounds;
    }

    static Rect Around(Vector2 center, Vector2 half) => new Rect(center - half, half * 2f);

    public static Shape Circle(Vector2 c, float r) => new Shape(p => (p - c).magnitude - r, Around(c, Vector2.one * r));

    // An ellipse (a close approximation of its distance, exact on the outline), turned `angle` degrees
    public static Shape Ellipse(Vector2 c, float rx, float ry, float angle = 0f)
    {
        float cos = Mathf.Cos(-angle * Mathf.Deg2Rad), sin = Mathf.Sin(-angle * Mathf.Deg2Rad);
        float r = Mathf.Max(rx, ry);
        return new Shape(p =>
        {
            var d = p - c;
            var q = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            float k0 = new Vector2(q.x / rx, q.y / ry).magnitude, k1 = new Vector2(q.x / (rx * rx), q.y / (ry * ry)).magnitude;
            return k1 < 1e-6f ? -Mathf.Min(rx, ry) : k0 * (k0 - 1f) / k1;
        }, Around(c, Vector2.one * r));
    }

    // A box `half` its size each way from `c`, corners rounded to `radius`, turned `angle` degrees
    public static Shape Box(Vector2 c, Vector2 half, float radius = 0f, float angle = 0f)
    {
        float cos = Mathf.Cos(-angle * Mathf.Deg2Rad), sin = Mathf.Sin(-angle * Mathf.Deg2Rad);
        return new Shape(p =>
        {
            var d = p - c;
            var q = new Vector2(Mathf.Abs(d.x * cos - d.y * sin), Mathf.Abs(d.x * sin + d.y * cos)) - half + Vector2.one * radius;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }, Around(c, Vector2.one * half.magnitude));
    }

    // A rod with round ends from a to b
    public static Shape Capsule(Vector2 a, Vector2 b, float r) => Stroke(new[] { a, b }, r * 2f);

    // A line through the points, `width` thick, round at the ends and corners
    public static Shape Stroke(IList<Vector2> points, float width)
    {
        var pts = new List<Vector2>(points);
        var min = pts[0];
        var max = pts[0];
        foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        float half = width / 2f;
        return new Shape(p =>
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], e = pts[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / Mathf.Max(e.sqrMagnitude, 1e-9f));
                best = Mathf.Min(best, (p - a - e * t).magnitude);
            }
            return best - half;
        }, Rect.MinMaxRect(min.x - half, min.y - half, max.x + half, max.y + half));
    }

    // Points along a circular arc (degrees, counter-clockwise from +X), for strokes
    public static Vector2[] Arc(Vector2 c, float r, float from, float to, int steps = 16)
    {
        var points = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
            points[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return points;
    }

    // Any simple polygon, convex or not
    public static Shape Polygon(IList<Vector2> points)
    {
        var v = new List<Vector2>(points);
        var min = v[0];
        var max = v[0];
        foreach (var p in v) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return new Shape(p =>
        {
            float d = (p - v[0]).sqrMagnitude, s = 1f;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    // A star with `spikes` points between the two radii
    public static Shape Star(Vector2 c, float outer, float inner, int spikes, float angle = 90f)
    {
        var points = new Vector2[spikes * 2];
        for (int i = 0; i < points.Length; i++)
        {
            float a = (angle + i * 180f / spikes) * Mathf.Deg2Rad;
            points[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? outer : inner);
        }
        return Polygon(points);
    }

    // Everything on the side of the line through `point` that `inside` points to
    public static Shape HalfPlane(Vector2 point, Vector2 inside)
    {
        var n = inside.normalized;
        return new Shape(p => -Vector2.Dot(p - point, n), new Rect(-1e4f, -1e4f, 2e4f, 2e4f));
    }

    public Shape Grow(float by)
    {
        var f = sdf;
        return new Shape(p => f(p) - by, new Rect(bounds.position - Vector2.one * by, bounds.size + Vector2.one * by * 2f));
    }

    public static Shape Union(params Shape[] shapes)
    {
        var min = shapes[0].bounds.min;
        var max = shapes[0].bounds.max;
        foreach (var s in shapes) { min = Vector2.Min(min, s.bounds.min); max = Vector2.Max(max, s.bounds.max); }
        return new Shape(p =>
        {
            float d = float.MaxValue;
            foreach (var s in shapes) d = Mathf.Min(d, s.sdf(p));
            return d;
        }, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    public Shape Intersect(Shape other)
    {
        Func<Vector2, float> a = sdf, b = other.sdf;
        var box = Rect.MinMaxRect(Mathf.Max(bounds.xMin, other.bounds.xMin), Mathf.Max(bounds.yMin, other.bounds.yMin),
            Mathf.Min(bounds.xMax, other.bounds.xMax), Mathf.Min(bounds.yMax, other.bounds.yMax));
        return new Shape(p => Mathf.Max(a(p), b(p)), box);
    }

    public Shape Minus(Shape other)
    {
        Func<Vector2, float> a = sdf, b = other.sdf;
        return new Shape(p => Mathf.Max(a(p), -b(p)), bounds);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using E = RobloxFace.Emotion;

// Draws Faces_Atlas.png: 8x8 cells of 256 px = 16 emotions x {mouth closed, talking} x {eyes open, blinking}.
// Shapes are signed distance functions in cell space ([-1, 1], y up), so edges come out anti-aliased.
// The cell layout must match RobloxFace.Apply.
public static class FaceAtlasBuilder
{
    public const string AtlasPath = "Assets/Characters/Textures/Faces_Atlas.png";
    const int Cell = 256;
    const float Line = 0.055f, Outline = 0.032f;

    static readonly Color Ink = new Color(0.11f, 0.11f, 0.12f);
    static readonly Color White = Color.white;
    static readonly Color MouthDark = new Color(0.36f, 0.07f, 0.1f);
    static readonly Color Tongue = new Color(0.96f, 0.45f, 0.52f);
    static readonly Color Tear = new Color(0.38f, 0.72f, 1f, 0.9f);
    static readonly Color Blush = new Color(1f, 0.38f, 0.45f, 0.45f);
    static readonly Color HeartRed = new Color(0.93f, 0.13f, 0.3f);

    class Layer { public Func<Vector2, float> sdf; public Color color; public Vector2 min, max; }

    class Face
    {
        public readonly List<Layer> layers = new List<Layer>();
        public void Add(Color color, Vector2 min, Vector2 max, Func<Vector2, float> sdf) =>
            layers.Add(new Layer { color = color, min = min, max = max, sdf = sdf });
    }

    enum Eye { Dot, Big, Wide, Closed, Happy, HalfLid, Bruh, Angry, AngryClosed, Sad, Heart, Cross, Narrow, Squeeze }
    enum Brow { None, Angry, Worried, Raised, OneRaised }

    [MenuItem("Tools/Characters/Build Face Atlas")]
    public static Texture2D Build()
    {
        int size = RobloxFace.Columns * Cell;
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(Ink.r, Ink.g, Ink.b, 0f);
        for (int e = 0; e < RobloxFace.EmotionCount; e++)
            for (int talk = 0; talk < 2; talk++)
                for (int blink = 0; blink < 2; blink++)
                {
                    int cell = e * 4 + talk * 2 + blink;
                    Rasterize(Draw((E)e, talk == 1, blink == 1), px, size, cell % RobloxFace.Columns * Cell, cell / RobloxFace.Columns * Cell);
                }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(AtlasPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = size;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
    }

    static void Rasterize(Face face, Color[] px, int stride, int ox, int oy)
    {
        float pixel = 2f / Cell;
        foreach (var l in face.layers)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((l.min.x + 1f) / pixel) - 2), x1 = Mathf.Min(Cell - 1, Mathf.CeilToInt((l.max.x + 1f) / pixel) + 2);
            int y0 = Mathf.Max(0, Mathf.FloorToInt((l.min.y + 1f) / pixel) - 2), y1 = Mathf.Min(Cell - 1, Mathf.CeilToInt((l.max.y + 1f) / pixel) + 2);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                    float a = Mathf.Clamp01(0.5f - l.sdf(p) / pixel) * l.color.a;
                    if (a <= 0f) continue;
                    int i = (oy + y) * stride + ox + x;
                    Color d = px[i];
                    float outA = a + d.a * (1f - a);
                    Color rgb = (l.color * a + d * (d.a * (1f - a))) / outA;
                    px[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
                }
        }
    }

    // ---------------------------------------------------------------- emotions

    static Face Draw(E emotion, bool talk, bool blink)
    {
        var f = new Face();
        switch (emotion)
        {
            case E.Smile:
                Eyes(f, blink ? Eye.Closed : Eye.Dot);
                if (talk) DMouth(f, 0.2f, 0.2f, -0.27f); else SmileArc(f);
                break;
            case E.Grin:
                Eyes(f, blink ? Eye.Closed : Eye.Dot);
                if (talk) DMouth(f, 0.27f, 0.19f, -0.23f); else DMouth(f, 0.3f, 0.3f, -0.2f);
                break;
            case E.Neutral:
                Eyes(f, blink ? Eye.Closed : Eye.Dot);
                if (talk) OMouth(f, new Vector2(0f, -0.36f), new Vector2(0.1f, 0.075f), false); else Stroke(f, Line, Ink, new Vector2(-0.16f, -0.36f), new Vector2(0.16f, -0.36f));
                break;
            case E.Shocked:
                Brows(f, Brow.Raised);
                Eyes(f, blink ? Eye.Closed : Eye.Wide);
                OMouth(f, new Vector2(0f, -0.42f), talk ? new Vector2(0.12f, 0.13f) : new Vector2(0.15f, 0.21f), true);
                break;
            case E.Scared:
                Brows(f, Brow.Worried);
                Eyes(f, blink ? Eye.Closed : Eye.Wide);
                if (talk) OMouth(f, new Vector2(0f, -0.38f), new Vector2(0.15f, 0.1f), false); else Wavy(f, -0.37f, 0.035f);
                Drop(f, new Vector2(0.62f, 0.32f), 0.075f, Tear);
                break;
            case E.Angry:
                Brows(f, Brow.Angry);
                Eyes(f, blink ? Eye.AngryClosed : Eye.Angry);
                if (talk) ShoutMouth(f); else Stroke(f, Line, Ink, Arc(new Vector2(0f, -0.62f), new Vector2(0.27f, 0.25f), 40f, 140f));
                break;
            case E.Sad:
                Brows(f, Brow.Worried);
                Eyes(f, blink ? Eye.Closed : Eye.Sad);
                if (talk) InvDMouth(f, 0.15f, 0.12f, -0.46f); else Stroke(f, Line, Ink, Arc(new Vector2(0f, -0.6f), new Vector2(0.2f, 0.2f), 45f, 135f));
                break;
            case E.Crying:
                foreach (float s in new[] { -1f, 1f })
                {
                    var c = new Vector2(s * 0.3f, 0.02f);
                    var top = c + new Vector2(0f, -0.04f);
                    f.Add(Tear, new Vector2(c.x - 0.06f, -0.82f), new Vector2(c.x + 0.06f, top.y), p => RoundBox(p - new Vector2(c.x, (top.y - 0.82f) * 0.5f), new Vector2(0.05f, (top.y + 0.82f) * 0.5f), 0.05f));
                }
                Brows(f, Brow.Worried);
                Eyes(f, Eye.Squeeze);
                if (talk) InvDMouth(f, 0.2f, 0.17f, -0.5f); else InvDMouth(f, 0.27f, 0.25f, -0.52f);
                break;
            case E.Smug:
                Brows(f, Brow.OneRaised);
                Eyes(f, blink ? Eye.Closed : Eye.HalfLid);
                if (talk) OMouth(f, new Vector2(0.08f, -0.35f), new Vector2(0.1f, 0.075f), false);
                else Stroke(f, Line, Ink, Bezier(new Vector2(-0.2f, -0.38f), new Vector2(0.08f, -0.43f), new Vector2(0.25f, -0.25f)));
                break;
            case E.Bruh:
                Eyes(f, blink ? Eye.Closed : Eye.Bruh);
                if (talk) OMouth(f, new Vector2(0f, -0.37f), new Vector2(0.12f, 0.045f), false);
                else Stroke(f, Line, Ink, new Vector2(-0.18f, -0.37f), new Vector2(0.18f, -0.37f));
                break;
            case E.Laugh:
                Eyes(f, Eye.Happy);
                if (talk) DMouth(f, 0.3f, 0.24f, -0.2f); else DMouth(f, 0.34f, 0.36f, -0.17f);
                Drop(f, new Vector2(-0.56f, 0.06f), 0.055f, Tear);
                Drop(f, new Vector2(0.56f, 0.06f), 0.055f, Tear);
                break;
            case E.Surprised:
                Brows(f, Brow.Raised);
                Eyes(f, blink ? Eye.Closed : Eye.Big);
                OMouth(f, new Vector2(0f, -0.38f), talk ? new Vector2(0.11f, 0.15f) : new Vector2(0.07f, 0.09f), talk);
                break;
            case E.Love:
                BlushCheeks(f);
                Eyes(f, blink ? Eye.Closed : Eye.Heart);
                if (talk) DMouth(f, 0.2f, 0.2f, -0.27f); else SmileArc(f);
                break;
            case E.Suspicious:
                Brows(f, Brow.OneRaised);
                Eyes(f, blink ? Eye.Closed : Eye.Narrow);
                if (talk) OMouth(f, new Vector2(0.05f, -0.36f), new Vector2(0.09f, 0.06f), false);
                else Stroke(f, Line, Ink, new Vector2(-0.15f, -0.33f), new Vector2(0.16f, -0.38f));
                break;
            case E.Evil:
                Brows(f, Brow.Angry);
                Eyes(f, blink ? Eye.AngryClosed : Eye.Angry);
                EvilGrin(f, talk);
                break;
            case E.Dizzy:
                Eyes(f, Eye.Cross);
                var tongueC = new Vector2(0.08f, talk ? -0.47f : -0.44f);
                var tongueR = talk ? new Vector2(0.08f, 0.12f) : new Vector2(0.07f, 0.09f);
                f.Add(Ink, tongueC - tongueR - Vector2.one * Outline, tongueC + tongueR + Vector2.one * Outline, p => Ellipse(p - tongueC, tongueR) - Outline);
                f.Add(Tongue, tongueC - tongueR, tongueC + tongueR, p => Ellipse(p - tongueC, tongueR));
                Wavy(f, -0.36f, talk ? 0.05f : 0.035f);
                break;
        }
        return f;
    }

    // ---------------------------------------------------------------- features

    static void Eyes(Face f, Eye kind)
    {
        foreach (float s in new[] { -1f, 1f })
        {
            var c = new Vector2(s * 0.3f, 0.12f);
            float inX = c.x - s * 0.13f, outX = c.x + s * 0.13f;
            switch (kind)
            {
                case Eye.Dot:
                    Fill(f, c, new Vector2(0.085f, 0.15f), Ink);
                    Fill(f, c + new Vector2(0.025f, 0.06f), new Vector2(0.03f, 0.04f), White);
                    break;
                case Eye.Big:
                    Fill(f, c, new Vector2(0.1f, 0.17f), Ink);
                    Fill(f, c + new Vector2(0.03f, 0.07f), new Vector2(0.035f, 0.045f), White);
                    break;
                case Eye.Wide:
                    Fill(f, c, new Vector2(0.16f, 0.2f), Ink);
                    Fill(f, c, new Vector2(0.125f, 0.165f), White);
                    Fill(f, c + new Vector2(0f, -0.02f), new Vector2(0.05f, 0.06f), Ink);
                    Fill(f, c + new Vector2(0.018f, 0.005f), new Vector2(0.017f, 0.017f), White);
                    break;
                case Eye.Closed:
                    Stroke(f, 0.045f, Ink, Arc(c + new Vector2(0f, 0.06f), new Vector2(0.1f, 0.08f), 200f, 340f));
                    break;
                case Eye.Happy:
                    Stroke(f, Line, Ink, Arc(c + new Vector2(0f, -0.04f), new Vector2(0.11f, 0.11f), 20f, 160f));
                    break;
                case Eye.HalfLid:
                case Eye.Bruh:
                {
                    float lid = kind == Eye.Bruh ? c.y - 0.03f : c.y + 0.01f;
                    var r = new Vector2(0.1f, 0.15f);
                    f.Add(Ink, c - r, c + r, p => Mathf.Max(Ellipse(p - c, r), p.y - lid));
                    Stroke(f, 0.04f, Ink, new Vector2(c.x - 0.13f, lid), new Vector2(c.x + 0.13f, lid));
                    break;
                }
                case Eye.Angry:
                case Eye.Sad:
                {
                    // Lid line cuts off the inner top (angry) or the outer top (sad) of the eye
                    var low = kind == Eye.Angry ? new Vector2(inX, c.y - 0.01f) : new Vector2(outX, c.y + 0.0f);
                    var high = kind == Eye.Angry ? new Vector2(outX, c.y + 0.12f) : new Vector2(inX, c.y + 0.12f);
                    var r = new Vector2(0.095f, 0.15f);
                    Vector2 dir = high - low, n = new Vector2(-dir.y, dir.x).normalized;
                    if (n.y < 0f) n = -n;
                    f.Add(Ink, c - r, c + r, p => Mathf.Max(Ellipse(p - c, r), Vector2.Dot(p - low, n)));
                    Stroke(f, 0.04f, Ink, low, high);
                    if (kind == Eye.Sad) Fill(f, c + new Vector2(0.02f, -0.02f), new Vector2(0.028f, 0.035f), White);
                    break;
                }
                case Eye.AngryClosed:
                    Stroke(f, 0.045f, Ink, new Vector2(inX, c.y - 0.04f), new Vector2(outX, c.y + 0.03f));
                    break;
                case Eye.Heart:
                {
                    var pts = HeartPoints(c, 0.17f);
                    f.Add(HeartRed, c - Vector2.one * 0.19f, c + Vector2.one * 0.19f, p => Polygon(p, pts));
                    Fill(f, c + new Vector2(-0.06f, 0.06f), new Vector2(0.03f, 0.03f), White);
                    break;
                }
                case Eye.Cross:
                    Stroke(f, Line, Ink, c + new Vector2(-0.1f, -0.1f), c + new Vector2(0.1f, 0.1f));
                    Stroke(f, Line, Ink, c + new Vector2(-0.1f, 0.1f), c + new Vector2(0.1f, -0.1f));
                    break;
                case Eye.Narrow:
                {
                    var r = new Vector2(0.11f, 0.15f);
                    float top = c.y + 0.03f, bottom = c.y - 0.05f;
                    f.Add(Ink, c - r, c + r, p => Mathf.Max(Ellipse(p - c, r), Mathf.Max(p.y - top, bottom - p.y)));
                    Stroke(f, 0.04f, Ink, new Vector2(c.x - 0.14f, top), new Vector2(c.x + 0.14f, top));
                    break;
                }
                case Eye.Squeeze:
                    // > <, pointing at the nose
                    Stroke(f, Line, Ink, new Vector2(outX, c.y + 0.09f), new Vector2(c.x - s * 0.08f, c.y), new Vector2(outX, c.y - 0.09f));
                    break;
            }
        }
    }

    static void Brows(Face f, Brow kind)
    {
        foreach (float s in new[] { -1f, 1f })
        {
            var c = new Vector2(s * 0.3f, 0.12f);
            float inX = c.x - s * 0.12f, outX = c.x + s * 0.14f;
            switch (kind)
            {
                case Brow.Angry: Stroke(f, Line, Ink, new Vector2(outX, c.y + 0.34f), new Vector2(inX, c.y + 0.21f)); break;
                case Brow.Worried: Stroke(f, Line, Ink, new Vector2(outX, c.y + 0.25f), new Vector2(inX, c.y + 0.34f)); break;
                case Brow.Raised: Stroke(f, 0.045f, Ink, Arc(c + new Vector2(0f, 0.24f), new Vector2(0.13f, 0.08f), 30f, 150f)); break;
                case Brow.OneRaised:
                    if (s > 0f) Stroke(f, 0.045f, Ink, Arc(c + new Vector2(0f, 0.27f), new Vector2(0.13f, 0.09f), 30f, 150f));
                    else Stroke(f, 0.045f, Ink, new Vector2(c.x - 0.13f, c.y + 0.23f), new Vector2(c.x + 0.13f, c.y + 0.21f));
                    break;
            }
        }
    }

    static void SmileArc(Face f) => Stroke(f, Line, Ink, Arc(new Vector2(0f, 0.02f), new Vector2(0.4f, 0.4f), 215f, 325f));

    static void Wavy(Face f, float y, float amplitude)
    {
        var pts = new Vector2[25];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (pts.Length - 1f);
            pts[i] = new Vector2(Mathf.Lerp(-0.22f, 0.22f, t), y + amplitude * Mathf.Sin(t * 3f * Mathf.PI));
        }
        Stroke(f, Line, Ink, pts);
    }

    // Open mouth with a flat top and round bottom
    static void DMouth(Face f, float halfWidth, float depth, float top)
    {
        var pts = Arc(new Vector2(0f, top), new Vector2(halfWidth, depth), 0f, -180f);
        OpenMouth(f, p => Polygon(p, pts), new Vector2(-halfWidth, top - depth), new Vector2(halfWidth, top), true, true);
    }

    // Upside-down D: wailing / sad talking
    static void InvDMouth(Face f, float halfWidth, float height, float bottom)
    {
        var pts = Arc(new Vector2(0f, bottom), new Vector2(halfWidth, height), 180f, 0f);
        OpenMouth(f, p => Polygon(p, pts), new Vector2(-halfWidth, bottom), new Vector2(halfWidth, bottom + height), false, true);
    }

    static void OMouth(Face f, Vector2 c, Vector2 r, bool tongue) =>
        OpenMouth(f, p => Ellipse(p - c, r), c - r, c + r, false, tongue);

    static void ShoutMouth(Face f)
    {
        var c = new Vector2(0f, -0.37f);
        var half = new Vector2(0.24f, 0.13f);
        Func<Vector2, float> shape = p => RoundBox(p - c, half, 0.07f);
        OpenMouth(f, shape, c - half, c + half, true, false);
        f.Add(White, c - half, c + half, p => Mathf.Max(shape(p), p.y - (c.y - half.y + 0.06f)));
    }

    static void OpenMouth(Face f, Func<Vector2, float> shape, Vector2 min, Vector2 max, bool teeth, bool tongue)
    {
        var grow = Vector2.one * Outline;
        f.Add(Ink, min - grow, max + grow, p => shape(p) - Outline);
        f.Add(MouthDark, min, max, shape);
        if (tongue)
        {
            var tc = new Vector2((min.x + max.x) * 0.5f, min.y + (max.y - min.y) * 0.12f);
            var tr = new Vector2((max.x - min.x) * 0.3f, (max.y - min.y) * 0.32f);
            f.Add(Tongue, min, max, p => Mathf.Max(shape(p), Ellipse(p - tc, tr)));
        }
        if (teeth)
        {
            float y = max.y - Mathf.Min(0.07f, (max.y - min.y) * 0.3f);
            f.Add(White, min, max, p => Mathf.Max(shape(p), y - p.y));
        }
    }

    static void EvilGrin(Face f, bool talk)
    {
        Vector2 l = new Vector2(-0.42f, -0.1f), r = new Vector2(0.42f, -0.1f);
        var topCurve = Bezier(l, new Vector2(0f, -0.32f), r);
        var bottomCurve = Bezier(r, new Vector2(0f, talk ? -0.85f : -0.72f), l);
        var pts = new Vector2[topCurve.Length + bottomCurve.Length];
        topCurve.CopyTo(pts, 0);
        bottomCurve.CopyTo(pts, topCurve.Length);
        Func<Vector2, float> shape = p => Polygon(p, pts);
        Vector2 min = new Vector2(-0.42f, talk ? -0.5f : -0.43f), max = new Vector2(0.42f, -0.1f);
        var grow = Vector2.one * Outline;
        f.Add(Ink, min - grow, max + grow, p => shape(p) - Outline);
        // Top curve as y(x): quadratic with control point (0, -0.32)
        Func<float, float> topY = x => { float t = (x + 0.42f) / 0.84f; return -0.1f - 0.44f * t * (1f - t); };
        if (talk)
        {
            f.Add(MouthDark, min, max, shape);
            f.Add(White, min, max, p => Mathf.Max(shape(p), topY(p.x) - 0.1f - p.y));
        }
        else
        {
            f.Add(White, min, max, shape);
            var mid = Bezier(l, new Vector2(0f, -0.52f), r);
            f.Add(Ink, min, max, p => Mathf.Max(shape(p), Polyline(p, mid) - 0.012f));
        }
        float[] gaps = { -0.26f, -0.13f, 0f, 0.13f, 0.26f };
        f.Add(Ink, min, max, p =>
        {
            float g = float.MaxValue;
            foreach (float x in gaps) g = Mathf.Min(g, Mathf.Abs(p.x - x));
            float band = talk ? topY(p.x) - 0.1f - p.y : -1f; // talking: lines only through the top teeth
            return Mathf.Max(shape(p), Mathf.Max(g - 0.012f, band));
        });
    }

    static void BlushCheeks(Face f)
    {
        foreach (float s in new[] { -1f, 1f })
            Fill(f, new Vector2(s * 0.48f, -0.12f), new Vector2(0.12f, 0.06f), Blush);
    }

    // Teardrop pointing up
    static void Drop(Face f, Vector2 c, float r, Color color)
    {
        var tri = new[] { c + new Vector2(-r * 0.9f, r * 0.3f), c + new Vector2(0f, r * 2.3f), c + new Vector2(r * 0.9f, r * 0.3f) };
        f.Add(color, c - new Vector2(r, r), c + new Vector2(r, r * 2.3f), p => Mathf.Min((p - c).magnitude - r, Polygon(p, tri)));
        Fill(f, c + new Vector2(-r * 0.35f, r * 0.2f), Vector2.one * r * 0.28f, new Color(1f, 1f, 1f, 0.8f));
    }

    // ---------------------------------------------------------------- shapes

    static void Fill(Face f, Vector2 c, Vector2 r, Color color) => f.Add(color, c - r, c + r, p => Ellipse(p - c, r));

    static void Stroke(Face f, float width, Color color, params Vector2[] pts)
    {
        Vector2 min = pts[0], max = pts[0];
        foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        var pad = Vector2.one * width;
        f.Add(color, min - pad, max + pad, p => Polyline(p, pts) - width * 0.5f);
    }

    static Vector2[] Arc(Vector2 c, Vector2 r, float fromDeg, float toDeg, int n = 28)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Lerp(fromDeg, toDeg, i / (n - 1f)) * Mathf.Deg2Rad;
            pts[i] = c + new Vector2(Mathf.Cos(a) * r.x, Mathf.Sin(a) * r.y);
        }
        return pts;
    }

    static Vector2[] Bezier(Vector2 a, Vector2 ctrl, Vector2 b, int n = 24)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            pts[i] = (1f - t) * (1f - t) * a + 2f * t * (1f - t) * ctrl + t * t * b;
        }
        return pts;
    }

    static Vector2[] HeartPoints(Vector2 c, float size)
    {
        var pts = new Vector2[48];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)pts.Length * 2f * Mathf.PI, s = Mathf.Sin(t);
            float x = 16f * s * s * s;
            float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
            pts[i] = c + new Vector2(x, y + 2f) * (size / 16f);
        }
        return pts;
    }

    // Approximate ellipse distance (exact enough for anti-aliasing)
    static float Ellipse(Vector2 p, Vector2 r)
    {
        float k0 = new Vector2(p.x / r.x, p.y / r.y).magnitude;
        float k1 = new Vector2(p.x / (r.x * r.x), p.y / (r.y * r.y)).magnitude;
        return k1 < 1e-6f ? -Mathf.Min(r.x, r.y) : k0 * (k0 - 1f) / k1;
    }

    static float RoundBox(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    static float Polyline(Vector2 p, Vector2[] pts)
    {
        float d = float.MaxValue;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 pa = p - pts[i], ba = pts[i + 1] - pts[i];
            float len = Vector2.Dot(ba, ba);
            float h = len > 1e-12f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / len) : 0f;
            d = Mathf.Min(d, (pa - ba * h).magnitude);
        }
        return pts.Length == 1 ? (p - pts[0]).magnitude : d;
    }

    // Signed distance to a closed polygon (negative inside)
    static float Polygon(Vector2 p, Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]), s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i], w = p - v[i];
            float len = Vector2.Dot(e, e);
            Vector2 b = w - e * (len > 1e-12f ? Mathf.Clamp01(Vector2.Dot(w, e) / len) : 0f);
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
}

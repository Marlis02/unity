using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using E = RobloxFace.Emotion;

// Builds Faces_Atlas_Roblox.png from the Roblox face decals in RobloxFaces/ (in the project folder, 2000x2000 PNGs) in
// RobloxFace's 8x8 layout (16 emotions x {mouth closed, talking} x {eyes open, blinking}), so RobloxFace drives it
// unchanged. Each emotion shows one decal; talking swaps in another decal's mouth and blinking another decal's eyes,
// each decal being cut across the empty band between its eyes and its mouth. Change the Decals table and re-run
// Tools/Characters/Build Roblox Face Atlas.
public static class RobloxDecalAtlasBuilder
{
    public const string AtlasPath = "Assets/Characters/Textures/Faces_Atlas_Roblox.png";
    public const string MaterialPath = "Assets/Characters/Materials/Face_Roblox.mat";
    const string DecalFolder = "RobloxFaces";
    const int Cell = 256;
    const int DecalSize = 2000;
    const int Crop = 1640; // decal pixels across the face: the face grid covers ~82% of the head, a decal the whole front

    // Per emotion: the decal, the decal whose mouth it talks with, the decal whose eyes it blinks with (null = its own)
    static readonly Dictionary<E, (string face, string talk, string blink)> Decals = new Dictionary<E, (string, string, string)>
    {
        [E.Smile] = ("Classic Roblox Face", "Cheerful Grin", "-_-"),
        [E.Grin] = ("Cheerful Grin", "Laughing Fun Face", "-_-"),
        [E.Neutral] = ("Stare", "Singing", "-_-"),
        [E.Shocked] = ("Shocked", null, "-_-"),
        [E.Scared] = ("Frightful", "Shocked", "-_-"),
        [E.Angry] = ("Grr!", null, "-_-"),
        [E.Sad] = ("Big Sad Eyes", "Singing", "-_-"),
        [E.Crying] = ("Crybaby", null, null),
        [E.Smug] = ("Smug", "Cheerful Grin", "-_-"),
        [E.Bruh] = ("Meh", "Singing", "-_-"),
        [E.Laugh] = ("Laughing Fun Face", null, null),
        [E.Surprised] = ("Surprise", null, "-_-"),
        [E.Love] = ("Love", "Cheerful Grin", null),
        [E.Suspicious] = ("Suspicious", "Singing", "-_-"),
        [E.Evil] = ("Sinister", "Laughing Fun Face", null),
        [E.Dizzy] = ("Dizzy", "Singing", null),
        [E.Troll] = ("Trollface", null, null), // drawn for the mirror short, not a Roblox decal
    };

    // A decal shrunk to one cell, premultiplied, rows bottom-up, and the cell row where its eyes end and its mouth begins
    class Decal
    {
        public Color[] px;
        public int split;
    }

    [MenuItem("Tools/Characters/Build Roblox Face Atlas")]
    public static Texture2D Build()
    {
        var decals = new Dictionary<string, Decal>();
        Decal Load(string name)
        {
            if (!decals.TryGetValue(name, out var decal)) decals[name] = decal = Shrink(Path.Combine(DecalFolder, name + ".png"));
            return decal;
        }

        int size = RobloxFace.Columns * Cell, height = RobloxFace.Rows * Cell;
        var atlas = new Color[size * height];
        for (int e = 0; e < RobloxFace.EmotionCount; e++)
        {
            var (face, talk, blink) = Decals[(E)e];
            for (int t = 0; t < 2; t++)
                for (int b = 0; b < 2; b++)
                {
                    var eyes = Load(b == 1 && blink != null ? blink : face);
                    var mouth = Load(t == 1 && talk != null ? talk : face);
                    int cell = e * 4 + t * 2 + b;
                    int ox = cell % RobloxFace.Columns * Cell, oy = cell / RobloxFace.Columns * Cell;
                    for (int y = 0; y < Cell; y++)
                        for (int x = 0; x < Cell; x++)
                        {
                            // Rows are bottom-up: the eyes are above the split, the mouth below it
                            int i = y * Cell + x;
                            var top = y > eyes.split ? eyes.px[i] : Color.clear;
                            var bottom = y <= mouth.split ? mouth.px[i] : Color.clear;
                            var c = top + bottom * (1f - top.a); // premultiplied "over"
                            atlas[(oy + y) * size + ox + x] = c.a > 0f ? new Color(c.r / c.a, c.g / c.a, c.b / c.a, c.a) : Color.clear;
                        }
                }
        }

        var tex = new Texture2D(size, height, TextureFormat.RGBA32, false);
        tex.SetPixels(atlas);
        File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(AtlasPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(size, height));
        importer.npotScale = TextureImporterNPOTScale.None; // 9 rows of cells: not a power of two
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        Debug.Log($"[Faces] Built {AtlasPath} from {decals.Count} Roblox decals");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
    }

    // The face material showing the Roblox atlas: R15_Face.mat's lit, alpha-blended setup with this texture
    // Lit and alpha-blended, so the face picks up the same light as the head
    public static Material FaceMaterial()
    {
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath) ?? Build();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ReceiveShadows", 1f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", atlas);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Box-filters the centre Crop x Crop of a decal down to one cell and finds the emptiest row between eyes and mouth
    static Decal Shrink(string path)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(File.ReadAllBytes(path)) || source.width != DecalSize || source.height != DecalSize)
            throw new IOException($"{path} is not a {DecalSize}x{DecalSize} PNG");
        var src = source.GetPixels32();
        Object.DestroyImmediate(source);

        int margin = (DecalSize - Crop) / 2;
        var px = new Color[Cell * Cell];
        var rowAlpha = new float[Cell];
        for (int y = 0; y < Cell; y++)
        {
            int sy0 = margin + y * Crop / Cell, sy1 = margin + (y + 1) * Crop / Cell;
            for (int x = 0; x < Cell; x++)
            {
                int sx0 = margin + x * Crop / Cell, sx1 = margin + (x + 1) * Crop / Cell;
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = sy0; sy < sy1; sy++)
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        var c = src[sy * DecalSize + sx];
                        float ca = c.a / 255f;
                        r += c.r / 255f * ca; g += c.g / 255f * ca; b += c.b / 255f * ca; a += ca;
                    }
                float n = (sy1 - sy0) * (sx1 - sx0);
                px[y * Cell + x] = new Color(r / n, g / n, b / n, a / n);
                rowAlpha[y] += a / n;
            }
        }

        // Eyes and mouth are split at the emptiest run of rows in the middle band of the face
        int split = Cell / 2, from = Cell * 35 / 100, to = Cell * 60 / 100;
        float best = float.MaxValue;
        for (int y = from; y <= to; y++)
        {
            float sum = 0f;
            for (int k = -3; k <= 3; k++) sum += rowAlpha[y + k];
            if (sum < best) { best = sum; split = y; }
        }
        return new Decal { px = px, split = split };
    }
}

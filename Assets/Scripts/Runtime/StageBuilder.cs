using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Builds the stage at runtime: a clean floor with a 1 m grid for judging sizes, plus
    /// world-space labels that face the camera.
    /// </summary>
    public static class StageBuilder
    {
        public static Transform Build(Transform parent, Material baseMaterial)
        {
            var root = new GameObject("Stage").transform;
            root.SetParent(parent, false);

            var floorMaterial = new Material(baseMaterial) { mainTexture = CreateGridTexture(), color = Color.white };
            floorMaterial.mainTextureScale = new Vector2(100f, 100f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            return root;
        }

        public static TextMesh Label(Transform parent, Vector3 position, string text, float scale)
        {
            Font labelFont = PlaygroundFont.Get();
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = labelFont;
            mesh.fontSize = 64;
            mesh.characterSize = 0.05f * scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.1f, 0.1f, 0.15f);
            go.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;
            go.AddComponent<Billboard>();
            return mesh;
        }

        static Texture2D CreateGridTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            var pixels = new Color32[size * size];
            var fill = new Color32(196, 202, 208, 255);
            var line = new Color32(150, 156, 166, 255);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < 3 || y < 3;
                    pixels[y * size + x] = edge ? line : fill;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}

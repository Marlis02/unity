using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Builds the test course at runtime: a 1 m grid floor plus labelled obstacles for
    /// measuring jump height, jump distance, step height, slope limit and clearance.
    /// Spawn is at the origin facing +Z.
    /// </summary>
    public static class ArenaBuilder
    {
        static Font labelFont;

        public static Transform Build(Transform parent, Material baseMaterial)
        {
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);

            var floorMaterial = new Material(baseMaterial) { mainTexture = CreateGridTexture(), color = Color.white };
            floorMaterial.mainTextureScale = new Vector2(100f, 100f);
            var blockMaterial = new Material(baseMaterial) { color = new Color(0.82f, 0.82f, 0.86f) };
            var accentMaterial = new Material(baseMaterial) { color = new Color(0.95f, 0.75f, 0.3f) };
            var rampMaterial = new Material(baseMaterial) { color = new Color(0.6f, 0.75f, 0.9f) };
            var propMaterial = new Material(baseMaterial) { color = new Color(0.85f, 0.35f, 0.35f) };

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            BuildJumpHeightTest(root, blockMaterial);
            BuildJumpDistanceTest(root, accentMaterial);
            BuildStairs(root, blockMaterial);
            BuildRamps(root, rampMaterial);
            BuildDoorways(root, blockMaterial);
            BuildProps(root, propMaterial);

            return root;
        }

        // Blocks of increasing height in front of the spawn.
        static void BuildJumpHeightTest(Transform root, Material material)
        {
            float[] heights = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f };
            for (int i = 0; i < heights.Length; i++)
            {
                float h = heights[i];
                var x = -7.5f + i * 3f;
                Block(root, "JumpBlock", new Vector3(x, h * 0.5f, 10f), new Vector3(2f, h, 2f), material);
                Label(root, new Vector3(x, h + 0.6f, 10f), $"{h:0.0} м", 1f);
            }
            Label(root, new Vector3(0f, 4.2f, 10f), "Высота прыжка", 1.3f);
        }

        // Platforms separated by growing gaps, to the left-back of the spawn.
        static void BuildJumpDistanceTest(Transform root, Material material)
        {
            float[] gaps = { 1f, 2f, 3f, 4f, 5f, 6f };
            float z = -10f;
            float x = -8f;
            const float size = 2f;
            const float height = 0.4f;
            Block(root, "GapPlatform", new Vector3(x, height * 0.5f, z), new Vector3(size, height, size), material);
            for (int i = 0; i < gaps.Length; i++)
            {
                float labelX = x + size * 0.5f + gaps[i] * 0.5f;
                x += size + gaps[i];
                Block(root, "GapPlatform", new Vector3(x, height * 0.5f, z), new Vector3(size, height, size), material);
                Label(root, new Vector3(labelX, 0.8f, z), $"{gaps[i]:0} м", 0.9f);
            }
            Label(root, new Vector3(x * 0.5f - 4f, 2.6f, z), "Дальность прыжка", 1.3f);
        }

        // Two staircases: 0.2 m steps (passable) and 0.4 m steps (above the default step offset).
        static void BuildStairs(Transform root, Material material)
        {
            BuildStaircase(root, material, new Vector3(-16f, 0f, -4f), 0.2f, 10);
            Label(root, new Vector3(-16f, 2.9f, -4.5f), "Ступени 0.2 м", 1f);
            BuildStaircase(root, material, new Vector3(-21f, 0f, -4f), 0.4f, 5);
            Label(root, new Vector3(-21f, 2.9f, -4.5f), "Ступени 0.4 м", 1f);
        }

        static void BuildStaircase(Transform root, Material material, Vector3 start, float rise, int steps)
        {
            const float run = 0.45f;
            const float width = 3f;
            for (int i = 0; i < steps; i++)
            {
                float h = rise * (i + 1);
                Block(root, "Step", start + new Vector3(0f, h * 0.5f, run * i), new Vector3(width, h, run), material);
            }
            float topHeight = rise * steps;
            Block(root, "Landing", start + new Vector3(0f, topHeight * 0.5f, run * steps + 1.5f), new Vector3(width, topHeight, 3f), material);
        }

        // Ramps at 20°, 35° and 50°; the default slope limit is 45°.
        static void BuildRamps(Transform root, Material material)
        {
            float[] angles = { 20f, 35f, 50f };
            const float length = 7f;
            for (int i = 0; i < angles.Length; i++)
            {
                float angle = angles[i];
                float x = 14f + i * 4.5f;
                float rad = angle * Mathf.Deg2Rad;
                var ramp = Block(root, "Ramp", Vector3.zero, new Vector3(3f, 0.2f, length), material);
                ramp.rotation = Quaternion.Euler(-angle, 0f, 0f);
                ramp.position = new Vector3(x, Mathf.Sin(rad) * length * 0.5f, -2f + Mathf.Cos(rad) * length * 0.5f);
                Label(root, new Vector3(x, Mathf.Sin(rad) * length + 1f, -2f + Mathf.Cos(rad) * length), $"{angle:0}°", 1f);
            }
            Label(root, new Vector3(18.5f, 7f, 3f), "Уклоны (лимит 45°)", 1.3f);
        }

        // Frames with decreasing clearance: tall characters stop at the low ones.
        static void BuildDoorways(Transform root, Material material)
        {
            float[] clearances = { 2.6f, 2.0f, 1.6f, 1.2f };
            const float opening = 1.4f;
            const float post = 0.3f;
            const float frameHeight = 3.2f;
            for (int i = 0; i < clearances.Length; i++)
            {
                float x = -6f + i * 4f;
                const float z = 20f;
                float c = clearances[i];
                Block(root, "DoorPost", new Vector3(x - (opening + post) * 0.5f, frameHeight * 0.5f, z), new Vector3(post, frameHeight, 0.4f), material);
                Block(root, "DoorPost", new Vector3(x + (opening + post) * 0.5f, frameHeight * 0.5f, z), new Vector3(post, frameHeight, 0.4f), material);
                float lintel = frameHeight - c;
                Block(root, "Lintel", new Vector3(x, c + lintel * 0.5f, z), new Vector3(opening + post * 2f, lintel, 0.4f), material);
                Label(root, new Vector3(x, frameHeight + 0.5f, z), $"{c:0.0} м", 1f);
            }
            Label(root, new Vector3(0f, 4.6f, 20f), "Проходы по высоте", 1.3f);
        }

        // Rigidbodies the character can push around.
        static void BuildProps(Transform root, Material material)
        {
            for (int i = 0; i < 6; i++)
            {
                var crate = Block(root, "Crate", new Vector3(6f + (i % 3) * 1.1f, 0.5f + (i / 3) * 1.05f, -6f), Vector3.one, material);
                var body = crate.gameObject.AddComponent<Rigidbody>();
                body.mass = 2f;
            }
            for (int i = 0; i < 3; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Ball";
                ball.transform.SetParent(root, false);
                ball.transform.localPosition = new Vector3(10f + i * 1.5f, 0.6f, -6f);
                ball.transform.localScale = Vector3.one * 1.2f;
                ball.GetComponent<Renderer>().sharedMaterial = material;
                var body = ball.AddComponent<Rigidbody>();
                body.mass = 1f;
            }
            Label(root, new Vector3(9.5f, 2.8f, -6f), "Можно толкать", 1f);
        }

        static Transform Block(Transform root, string name, Vector3 center, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(root, false);
            block.transform.localPosition = center;
            block.transform.localScale = size;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block.transform;
        }

        public static TextMesh Label(Transform parent, Vector3 position, string text, float scale)
        {
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

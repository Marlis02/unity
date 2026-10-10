using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds characters from textured models made in one static mesh with no rig and a bare head (Meshy AI, Tripo exports),
// as prefabs in Prefabs next to each model. Each model is rigged here: skinned to a 16-bone skeleton named after Unity's
// humanoid bones, so every prefab gets a Humanoid avatar and plays the same clips as the Tripo characters. It stands on
// its origin facing +Z, gets a RobloxFace grid wrapped onto the bare skin of the head, showing the Roblox decal atlas
// (RobloxDecalAtlasBuilder), and optionally one of Tripo's hair styles stretched onto the head. Shared rigging code is
// in RigUtility.
// Add a Spec and re-run Tools/Characters/Build Textured Characters; prefabs are rebuilt in place.
public static class TexturedCharacterBuilder
{
    const float FaceLift = 0.004f;    // metres off the head, clear of the faceted surface
    const float HairInflate = 1.03f;  // room at the corners of a boxy head for the stretched hair

    // Half-widths (metres) of the soft weight bands at each joint, and how far above the armpit the shoulder band widens
    const float HipBlendBelow = 0.09f, HipBlendAbove = 0.05f, SpineBlend = 0.04f, NeckBlend = 0.03f;
    const float ElbowBlend = 0.04f, WristBlend = 0.025f, KneeBlend = 0.04f, AnkleBlend = 0.03f, ShoulderRise = 0.08f;

    class Spec
    {
        public string name, model, texture; // name: the prefab's
        public bool facesMinusZ;            // Meshy exports face -Z
        public float height;                // metres to scale the model to; 0 keeps it as modelled
        // Joint positions in metres (after scaling), the body standing on the origin facing +Z (its left side is -X).
        // Only the middle and the left side are listed; the right side is the mirror image.
        public (string bone, string parent, Vector3 pos)[] joints;
        public float armpitY;               // below this the arms hang free of the body
        public float armpitX;               // where arm meets body sideways above the armpit; 0 = midway across the gap below it
        // Above the armpit the arm hands over to the body across a band that widens up the shoulder (sleeves that flow
        // into the shirt); a blocky figure instead keeps the armpit's narrow split straight up, so its arms turn whole
        public bool blockyShoulders;
        // Drawn from both sides: where the model's sleeves are open shells (cut off at the shoulder they're hollow
        // tubes), a raised arm would show their inside as gaps
        public bool doubleSided;
        public float faceCenterY, faceSize; // metres above the feet; the atlas cell's brows sit at 0.9, its mouth at 0.12
        public RobloxFace.Emotion face;
        public string hair;                 // a Tripo hair style (Tripo_Hair_<hair>), or null for none
        public Color hairColor;
    }

    static readonly Spec[] Specs =
    {
        // A-pose with the arms hanging a little forward. Face: eyes a little above the middle of the bare face, between
        // the chin (~1.43 m) and the fringe (~1.66 m in the middle).
        new Spec
        {
            name = "Meshy_Hero", model = "Assets/Characters/Meshy/Meshy_Hero.fbx", texture = "Assets/Characters/Meshy/Meshy_Hero_BaseColor.png",
            facesMinusZ = true,
            joints = new[]
            {
                ("Hips", (string)null, new Vector3(0f, 0.86f, 0f)),
                ("Spine", "Hips", new Vector3(0f, 0.98f, 0f)),
                ("Chest", "Spine", new Vector3(0f, 1.14f, 0f)),
                ("Head", "Chest", new Vector3(0f, 1.39f, -0.005f)),
                ("LeftUpperArm", "Chest", new Vector3(-0.27f, 1.25f, 0.01f)),
                ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.37f, 1.03f, 0.025f)),
                ("LeftHand", "LeftLowerArm", new Vector3(-0.415f, 0.84f, 0.08f)),
                ("LeftUpperLeg", "Hips", new Vector3(-0.12f, 0.84f, 0f)),
                ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.125f, 0.46f, 0f)),
                ("LeftFoot", "LeftLowerLeg", new Vector3(-0.125f, 0.1f, -0.01f)),
            },
            armpitY = 1.1f, faceCenterY = 1.55f, faceSize = 0.25f, face = RobloxFace.Emotion.Smile,
        },
        // Tripo's black minifigure (black tee with the logo, black trousers, white skin and shoes), scaled up to the
        // Tripo characters' height, arms a little out, with blue bacon hair. Face a little above the middle of the head
        // (1.45-1.80 m), its brows under the fringe.
        new Spec
        {
            name = "Minifig", model = "Assets/Characters/Minifig/Minifig.fbx", texture = "Assets/Characters/Minifig/Minifig_BaseColor.png",
            height = TripoCharacterBuilder.Height,
            joints = new[]
            {
                ("Hips", (string)null, new Vector3(0f, 0.66f, -0.015f)),
                ("Spine", "Hips", new Vector3(0f, 0.82f, -0.015f)),
                ("Chest", "Spine", new Vector3(0f, 1.08f, -0.015f)),
                ("Head", "Chest", new Vector3(0f, 1.42f, -0.01f)),
                ("LeftUpperArm", "Chest", new Vector3(-0.335f, 1.36f, -0.015f)), // at the top of the arm, Roblox-style
                ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.36f, 1.04f, -0.015f)),
                ("LeftHand", "LeftLowerArm", new Vector3(-0.385f, 0.81f, -0.015f)),
                ("LeftUpperLeg", "Hips", new Vector3(-0.125f, 0.6f, -0.015f)),
                ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.125f, 0.33f, -0.015f)),
                ("LeftFoot", "LeftLowerLeg", new Vector3(-0.125f, 0.13f, -0.02f)),
            },
            // The sleeves all but touch the body just under the armpit: split where the gap is clear and cut up the side of the shirt
            armpitY = 1.0f, armpitX = 0.258f, blockyShoulders = true, doubleSided = true, faceCenterY = 1.625f, faceSize = 0.3f, face = RobloxFace.Emotion.Smile,
            hair = "Bacon", hairColor = R15CharacterBuilder.Hex("1F4FD6"),
        },
    };

    [MenuItem("Tools/Characters/Build Textured Characters")]
    public static void BuildAll()
    {
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        if (faceMaterial == null) return;

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs) BuildCharacter(spec, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Textured] Built {Specs.Length} characters: " + string.Join(", ", System.Array.ConvertAll(Specs, s => s.name)));
    }

    static void BuildCharacter(Spec s, Material faceMaterial, Scene stage)
    {
        // Everything made goes next to the model
        string folder = System.IO.Path.GetDirectoryName(s.model).Replace('\\', '/');
        string prefabFolder = folder + "/Prefabs", meshFolder = folder + "/Meshes", avatarFolder = folder + "/Avatars";
        foreach (var f in new[] { prefabFolder, meshFolder, avatarFolder }) RigUtility.EnsureFolder(f);

        var joints = new List<(string bone, string parent, Vector3 pos)>(s.joints);
        foreach (var (bone, parent, pos) in s.joints)
            if (bone.StartsWith("Left"))
                joints.Add(("Right" + bone.Substring(4), parent.StartsWith("Left") ? "Right" + parent.Substring(4) : parent,
                    new Vector3(-pos.x, pos.y, pos.z)));

        var root = new GameObject(s.name);
        SceneManager.MoveGameObjectToScene(root, stage);
        var bones = new Dictionary<string, Transform>();
        foreach (var (bone, parent, pos) in joints)
        {
            var t = new GameObject(bone).transform;
            t.SetParent(parent == null ? root.transform : bones[parent], false);
            t.position = pos;
            bones[bone] = t;
        }

        // The model's vertices in character space: turned to face +Z, feet on the origin, scaled to height
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(s.model);
        var filter = model.GetComponentInChildren<MeshFilter>();
        var source = filter.sharedMesh;
        var toCharacter = Matrix4x4.Rotate(Quaternion.Euler(0f, s.facesMinusZ ? 180f : 0f, 0f)) * filter.transform.localToWorldMatrix;
        var vertices = source.vertices;
        var normals = source.normals;
        float floor = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = toCharacter.MultiplyPoint3x4(vertices[i]);
            normals[i] = toCharacter.MultiplyVector(normals[i]).normalized;
            floor = Mathf.Min(floor, vertices[i].y);
            top = Mathf.Max(top, vertices[i].y);
        }
        float scale = s.height > 0f ? s.height / (top - floor) : 1f;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - Vector3.up * floor) * scale;
        var uvs = source.uv;
        var triangles = source.triangles;

        var boneNames = joints.ConvertAll(j => j.bone);
        var bodyVertices = new List<Vector3>(vertices);
        var bodyNormals = new List<Vector3>(normals);
        var bodyUvs = new List<Vector2>(uvs);
        var bodyTriangles = new List<int>(triangles);
        var bodyWeights = new List<BoneWeight>(Weights(s, vertices, triangles, joints, boneNames));
        if (s.blockyShoulders)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(System.IO.File.ReadAllBytes(s.texture));
            SplitShoulders(bodyVertices, bodyNormals, bodyUvs, bodyTriangles, bodyWeights, boneNames, texture);
            Object.DestroyImmediate(texture);
        }
        var body = new Mesh { indexFormat = bodyVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        body.SetVertices(bodyVertices);
        body.SetNormals(bodyNormals);
        body.SetUVs(0, bodyUvs);
        body.SetTriangles(bodyTriangles, 0);
        body.boneWeights = bodyWeights.ToArray();
        // The renderer sits on the character origin; the bones are unrotated and unscaled
        body.bindposes = joints.ConvertAll(j => Matrix4x4.Translate(-j.pos)).ToArray();
        body.RecalculateBounds();

        var bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(root.transform, false);
        var skin = bodyObject.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = RigUtility.SaveMesh(body, $"{meshFolder}/{s.name}_Body.asset");
        skin.bones = boneNames.ConvertAll(b => bones[b]).ToArray();
        skin.rootBone = bones["Hips"];
        skin.sharedMaterials = model.GetComponentInChildren<MeshRenderer>().sharedMaterials;
        if (s.doubleSided)
            foreach (var material in skin.sharedMaterials)
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.doubleSidedGI = true;
                EditorUtility.SetDirty(material);
            }

        // Rigid on the head, modelled in character space like the body
        var face = HeadPart(bones["Head"], "Face", RigUtility.SaveMesh(FaceMesh(s, vertices, uvs, triangles), $"{meshFolder}/{s.name}_Face.asset"), faceMaterial);
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)s.face;
        if (s.hair != null)
        {
            // The head is everything above the neck
            var head = new Bounds();
            bool first = true;
            foreach (var v in vertices)
            {
                if (v.y < bones["Head"].position.y + 0.03f) continue;
                if (first) head = new Bounds(v, Vector3.zero);
                else head.Encapsulate(v);
                first = false;
            }
            var hair = RigUtility.SaveMesh(RigUtility.StretchTripoHair(s.hair, head, HairInflate), $"{meshFolder}/{s.name}_Hair.asset");
            HeadPart(bones["Head"], "Hair", hair, ColorMaterial($"{folder}/{s.name}_Hair.mat", s.hairColor));
        }

        var avatar = RigUtility.SaveAvatar(RigUtility.BuildAvatar(root, bones, boneNames), $"{avatarFolder}/{s.name}_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{prefabFolder}/{s.name}.prefab");
        Object.DestroyImmediate(root);
    }

    static GameObject HeadPart(Transform head, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(head, false);
        go.transform.SetPositionAndRotation(head.root.position, Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static Material ColorMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.3f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------- skin weights

    // One mesh holds the whole body. Below the armpit the arms hang free, so they are told apart from the body by
    // connectivity; above it the shoulder fades from body to arm across a band that widens towards the top (or stays as
    // narrow as at the armpit, for blocky shoulders). Along each
    // limb and up the trunk the weight hands over from bone to bone in a soft band around each joint.
    static BoneWeight[] Weights(Spec s, Vector3[] vertices, int[] triangles, List<(string bone, string parent, Vector3 pos)> joints,
        List<string> boneNames)
    {
        var at = new Dictionary<string, Vector3>();
        foreach (var (bone, _, pos) in joints) at[bone] = pos;
        float shoulderX = at["RightUpperArm"].x;

        // Weld by position (vertices are split along UV seams), then join the triangles below the armpit
        var ids = new Dictionary<Vector3, int>();
        var weld = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!ids.TryGetValue(vertices[i], out int id)) ids[vertices[i]] = id = ids.Count;
            weld[i] = id;
        }
        var group = new int[ids.Count];
        for (int i = 0; i < group.Length; i++) group[i] = i;
        int Find(int a)
        {
            while (group[a] != a) a = group[a] = group[group[a]];
            return a;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (Mathf.Max(vertices[a].y, vertices[b].y, vertices[c].y) >= s.armpitY) continue;
            group[Find(weld[b])] = Find(weld[a]);
            group[Find(weld[c])] = Find(weld[a]);
        }
        // Arm pieces: everything below the armpit that isn't joined to the body (the piece that reaches the floor). Scraps
        // cut off by the armpit line itself are a vertex or two: they go by the side of the gap they are on.
        int bodyGroup = -1;
        float lowest = float.PositiveInfinity;
        var size = new Dictionary<int, int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].y >= s.armpitY) continue;
            int g = Find(weld[i]);
            size[g] = size.TryGetValue(g, out int count) ? count + 1 : 1;
            if (vertices[i].y < lowest) (lowest, bodyGroup) = (vertices[i].y, g);
        }
        bool Piece(int i) => size[Find(weld[i])] >= 50;
        bool OnArm(int i) => Find(weld[i]) != bodyGroup;

        // Where body meets arm at the armpit, the middle of the gap between them
        float bodyEdge = 0f, armEdge = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].y < s.armpitY - 0.05f || vertices[i].y >= s.armpitY || !Piece(i)) continue;
            float x = Mathf.Abs(vertices[i].x);
            if (OnArm(i)) armEdge = Mathf.Min(armEdge, x);
            else bodyEdge = Mathf.Max(bodyEdge, x);
        }
        float armpitX = s.armpitX > 0f ? s.armpitX : (bodyEdge + armEdge) / 2f, armpitGap = Mathf.Max(0.005f, (armEdge - bodyEdge) / 2f);

        float Below(float y, float joint, float half) => RigUtility.Below(y, joint, half);

        var weights = new BoneWeight[vertices.Length];
        var share = new Dictionary<string, float>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i];
            string side = p.x < 0f ? "Left" : "Right";
            float x = Mathf.Abs(p.x);

            float arm;
            if (p.y < s.armpitY && Piece(i)) arm = OnArm(i) ? 1f : 0f;
            else if (p.y < s.armpitY) arm = x > armpitX ? 1f : 0f;
            else if (p.y >= at["Head"].y) arm = 0f;
            else if (s.blockyShoulders) arm = x > armpitX ? 1f : 0f; // SplitShoulders cuts along here
            else
            {
                float k = s.blockyShoulders ? 0f : Mathf.InverseLerp(s.armpitY, s.armpitY + ShoulderRise, p.y);
                float from = Mathf.Lerp(armpitX - armpitGap, shoulderX - 0.07f, k), to = Mathf.Lerp(armpitX + armpitGap, shoulderX + 0.03f, k);
                arm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));
            }
            float leg = (1f - arm) * Below(p.y, at["Hips"].y + (HipBlendAbove - HipBlendBelow) / 2f, (HipBlendAbove + HipBlendBelow) / 2f);
            float trunk = 1f - arm - leg;

            share.Clear();
            RigUtility.Chain(share, arm, new[] { side + "UpperArm", side + "LowerArm", side + "Hand" }, new[]
            {
                Below(p.y, at[side + "LowerArm"].y, ElbowBlend), Below(p.y, at[side + "Hand"].y, WristBlend),
            });
            RigUtility.Chain(share, leg, new[] { side + "UpperLeg", side + "LowerLeg", side + "Foot" }, new[]
            {
                Below(p.y, at[side + "LowerLeg"].y, KneeBlend), Below(p.y, at[side + "Foot"].y, AnkleBlend),
            });
            RigUtility.Chain(share, trunk, new[] { "Hips", "Spine", "Chest", "Head" }, new[]
            {
                1f - Below(p.y, (at["Hips"].y + at["Spine"].y) / 2f, SpineBlend),
                1f - Below(p.y, (at["Spine"].y + at["Chest"].y) / 2f, SpineBlend),
                1f - Below(p.y, at["Head"].y, NeckBlend),
            });
            weights[i] = RigUtility.Pack(share, boneNames);
        }
        return weights;
    }

    // Blocky shoulders: cuts the mesh where arm meets body, so no triangle spans the two (it would stretch right across as
    // the arm swings up), and caps the openings on both sides so neither shows a hole once they part. Each triangle across
    // the cut goes to the side most of its corners are on, with copies of the other corners riding on that side's bone.
    static void SplitShoulders(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
        List<BoneWeight> weights, List<string> boneNames, Texture2D texture)
    {
        int chest = boneNames.IndexOf("Chest");
        bool OnArm(int v)
        {
            string bone = boneNames[weights[v].boneIndex0];
            return bone.EndsWith("Arm") || bone.EndsWith("Hand");
        }

        var copies = new Dictionary<(int, bool), int>();
        var onArm = new List<bool>(); // per triangle
        var seam = new HashSet<Vector3>();
        for (int t = 0; t < triangles.Count; t += 3)
        {
            int arms = 0, armBone = -1;
            for (int k = 0; k < 3; k++)
                if (OnArm(triangles[t + k]))
                {
                    arms++;
                    armBone = weights[triangles[t + k]].boneIndex0;
                }
            bool arm = arms >= 2;
            onArm.Add(arm);
            if (arms == 0 || arms == 3) continue;
            for (int k = 0; k < 3; k++)
            {
                int v = triangles[t + k];
                if (OnArm(v) == arm) continue;
                if (!copies.TryGetValue((v, arm), out int copy))
                {
                    copy = vertices.Count;
                    vertices.Add(vertices[v]);
                    normals.Add(normals[v]);
                    uvs.Add(uvs[v]);
                    weights.Add(new BoneWeight { boneIndex0 = arm ? armBone : chest, weight0 = 1f });
                    copies[(v, arm)] = copy;
                }
                triangles[t + k] = copy;
                seam.Add(vertices[v]);
            }
        }

        // In each piece, walk the boundary loops along the cut (welded by position, as vertices are split along UV seams)
        // against the triangles' winding, so the caps face outwards, and close each with a fan round its middle
        var ids = new Dictionary<Vector3, int>();
        int Weld(int v)
        {
            if (!ids.TryGetValue(vertices[v], out int id)) ids[vertices[v]] = id = ids.Count;
            return id;
        }
        int triangleCount = onArm.Count;
        foreach (bool armPiece in new[] { true, false })
        {
            var edgeUse = new Dictionary<(int, int), int>();
            var corner = new Dictionary<int, int>(); // welded id -> one of this piece's vertices there
            for (int t = 0; t < triangleCount; t++)
            {
                if (onArm[t] != armPiece) continue;
                for (int k = 0; k < 3; k++)
                {
                    int va = triangles[3 * t + k], vb = triangles[3 * t + (k + 1) % 3];
                    int a = Weld(va), b = Weld(vb);
                    corner[a] = va;
                    corner[b] = vb;
                    var key = a < b ? (a, b) : (b, a);
                    edgeUse[key] = edgeUse.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            var next = new Dictionary<int, int>();
            for (int t = 0; t < triangleCount; t++)
            {
                if (onArm[t] != armPiece) continue;
                for (int k = 0; k < 3; k++)
                {
                    int a = Weld(triangles[3 * t + k]), b = Weld(triangles[3 * t + (k + 1) % 3]);
                    if (edgeUse[a < b ? (a, b) : (b, a)] == 1) next[b] = a;
                }
            }
            var visited = new HashSet<int>();
            foreach (int start in new List<int>(next.Keys))
            {
                var loop = new List<int>();
                int v = start;
                while (!visited.Contains(v) && next.ContainsKey(v))
                {
                    visited.Add(v);
                    loop.Add(v);
                    v = next[v];
                }
                if (v != start || loop.Count < 3 || !loop.Exists(w => seam.Contains(vertices[corner[w]]))) continue;

                var center = Vector3.zero;
                foreach (int w in loop) center += vertices[corner[w]];
                center /= loop.Count;
                var normal = Vector3.zero;
                for (int k = 0; k < loop.Count; k++)
                    normal += Vector3.Cross(vertices[corner[loop[k]]] - center, vertices[corner[loop[(k + 1) % loop.Count]]] - center);
                normal = RigUtility.CapNormal(normal, loop.ConvertAll(w => normals[corner[w]]));

                // Own vertices with the cap's normal, all one colour so the texture isn't smeared across it (the edge's
                // middling shade, the cloth it closes rather than a stray light or dark spot), each riding on the bone
                // of the edge it closes
                int c = vertices.Count;
                var shades = loop.ConvertAll(w => (uv: uvs[corner[w]], shade: texture.GetPixelBilinear(uvs[corner[w]].x, uvs[corner[w]].y).grayscale));
                shades.Sort((a, b) => a.shade.CompareTo(b.shade));
                var colour = shades[shades.Count / 2].uv;
                vertices.Add(center);
                normals.Add(normal);
                uvs.Add(colour);
                weights.Add(weights[corner[loop[0]]]);
                foreach (int w in loop)
                {
                    vertices.Add(vertices[corner[w]]);
                    normals.Add(normal);
                    uvs.Add(colour);
                    weights.Add(weights[corner[w]]);
                }
                for (int k = 0; k < loop.Count; k++) triangles.AddRange(new[] { c, c + 1 + k, c + 1 + (k + 1) % loop.Count });
            }
        }
    }

    // ---------------------------------------------------------------- face

    // The RobloxFace grid on the front of the head. Hair and clothes are one mesh with the skin here, so the grid lands on
    // skin-coloured triangles only: a fringe in front of the forehead covers the face instead of carrying it.
    static Mesh FaceMesh(Spec s, Vector3[] sourceVertices, Vector2[] uvs, int[] sourceTriangles)
    {
        var texture = new Texture2D(2, 2);
        texture.LoadImage(System.IO.File.ReadAllBytes(s.texture));

        float half = s.faceSize * 0.6f;
        var head = new List<Vector3>();
        for (int i = 0; i < sourceTriangles.Length; i += 3)
        {
            Vector3 a = sourceVertices[sourceTriangles[i]], b = sourceVertices[sourceTriangles[i + 1]], c = sourceVertices[sourceTriangles[i + 2]];
            if (Mathf.Max(a.y, b.y, c.y) < s.faceCenterY - half || Mathf.Min(a.y, b.y, c.y) > s.faceCenterY + half) continue;
            if (Mathf.Max(a.z, b.z, c.z) < 0f) continue;
            var uv = (uvs[sourceTriangles[i]] + uvs[sourceTriangles[i + 1]] + uvs[sourceTriangles[i + 2]]) / 3f;
            if (!IsSkin(texture.GetPixelBilinear(uv.x, uv.y))) continue;
            head.Add(a);
            head.Add(b);
            head.Add(c);
        }
        Object.DestroyImmediate(texture);
        return RigUtility.FaceGrid(head, s.faceCenterY, s.faceSize, FaceLift);
    }

    // Light and unsaturated: the grey Roblox skin, not the blue hair or shirt
    static bool IsSkin(Color c) => c.maxColorComponent - Mathf.Min(c.r, c.g, c.b) < 0.12f && c.r > 0.45f;
}

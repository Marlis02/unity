using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools/Characters/Build Lineup Scene: a studio scene (Roblox baseplate, key/fill/rim light, post-processing) with every
// R15 character posed and emoting, to check the look. Poses are muscle values, the same space Mixamo clips use.
public static class R15LineupBuilder
{
    const string ScenePath = "Assets/Scenes/Characters_Lineup.unity";
    const string VolumePath = "Assets/Characters/Lineup_Volume.asset";
    const string GridPath = "Assets/Characters/Textures/Baseplate_Grid.png";
    static readonly Color Sky = new Color(0.6f, 0.79f, 0.97f);

    // Bone rotations are local Euler angles in degrees. Every bone starts at identity (character facing +Z, arms down),
    // so: arm out to the side = Z (+ right arm, - left arm), arm/elbow/knee forward = -X, twist = Y.
    struct Entry
    {
        public string name;
        public float x, z;
        public RobloxFace.Emotion emotion;
        public (string bone, Vector3 euler)[] pose;
    }

    static readonly Entry[] Lineup =
    {
        new Entry
        {
            name = "Dad", x = -3.1f, z = -0.6f, emotion = RobloxFace.Emotion.Bruh,
            pose = new[] // shrug
            {
                ("RightUpperArm", new Vector3(0f, 40f, 15f)), ("RightLowerArm", new Vector3(-90f, 0f, 0f)),
                ("LeftUpperArm", new Vector3(0f, -40f, -15f)), ("LeftLowerArm", new Vector3(-90f, 0f, 0f)),
                ("Spine", new Vector3(-4f, 0f, 0f)), ("Head", new Vector3(0f, 0f, 10f)),
            },
        },
        new Entry
        {
            name = "Mom", x = -1.55f, z = -0.1f, emotion = RobloxFace.Emotion.Angry,
            pose = new[] // hands on hips
            {
                ("RightUpperArm", new Vector3(10f, 0f, 40f)), ("RightLowerArm", new Vector3(0f, 0f, -95f)),
                ("LeftUpperArm", new Vector3(10f, 0f, -40f)), ("LeftLowerArm", new Vector3(0f, 0f, 95f)),
                ("Head", new Vector3(8f, 0f, 0f)),
            },
        },
        new Entry
        {
            name = "Noob", x = 0f, z = 0.2f, emotion = RobloxFace.Emotion.Grin,
            pose = new[] // wave
            {
                ("RightUpperArm", new Vector3(-10f, 0f, 115f)), ("RightLowerArm", new Vector3(0f, 0f, 35f)),
                ("LeftUpperArm", new Vector3(0f, 0f, -5f)), ("Spine", new Vector3(0f, 0f, -3f)), ("Head", new Vector3(0f, 0f, -8f)),
            },
        },
        new Entry
        {
            name = "Sister", x = 1.55f, z = -0.1f, emotion = RobloxFace.Emotion.Laugh,
            pose = new[] // cheering, one leg kicked back
            {
                ("RightUpperArm", new Vector3(0f, 0f, 150f)), ("RightLowerArm", new Vector3(0f, 0f, -20f)),
                ("LeftUpperArm", new Vector3(0f, 0f, -150f)), ("LeftLowerArm", new Vector3(0f, 0f, 20f)),
                ("LeftUpperLeg", new Vector3(-10f, 0f, 0f)), ("LeftLowerLeg", new Vector3(70f, 0f, 0f)), ("Head", new Vector3(-10f, 0f, 0f)),
            },
        },
        new Entry
        {
            name = "Pro", x = 3.1f, z = -0.6f, emotion = RobloxFace.Emotion.Smug,
            pose = new[] // points at the camera
            {
                ("RightUpperArm", new Vector3(-85f, 0f, 8f)), ("LeftUpperArm", new Vector3(0f, 0f, -8f)),
                ("Spine", new Vector3(0f, -10f, 0f)), ("Head", new Vector3(0f, 10f, 0f)),
            },
        },
    };

    [MenuItem("Tools/Characters/Build Lineup Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[R15] Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 1.1f, 10f);
        camera.transform.LookAt(new Vector3(0f, 0.95f, 0f));
        camera.fieldOfView = 26f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Sky;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        AddLight("Key Light", new Color(1f, 0.95f, 0.88f), 1.5f, new Vector3(38f, 205f, 0f), LightShadows.Soft);
        AddLight("Fill Light", new Color(0.75f, 0.85f, 1f), 0.35f, new Vector3(15f, 140f, 0f), LightShadows.None);
        AddLight("Rim Light", new Color(1f, 0.95f, 0.9f), 0.8f, new Vector3(25f, 15f, 0f), LightShadows.None);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.57f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.32f);
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 14f;
        RenderSettings.fogEndDistance = 38f;
        RenderSettings.fogColor = Sky;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = BuildVolumeProfile();

        AddBaseplate();

        foreach (var e in Lineup)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{R15CharacterBuilder.PrefabFolder}/R15_{e.name}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var pos = new Vector3(e.x, 0f, e.z);
            var toCamera = camera.transform.position - pos;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(toCamera.x, 0f, toCamera.z)));
            ApplyBonePose(go, e.pose);
            var face = go.GetComponentInChildren<RobloxFace>();
            face.Current = e.emotion;
            face.Apply();
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[R15] Built " + ScenePath);
    }

    // Sets local bone rotations (Euler degrees) on a character instance; bones not listed keep their rotation
    public static void ApplyBonePose(GameObject character, (string bone, Vector3 euler)[] pose)
    {
        var bones = new Dictionary<string, Transform>();
        foreach (var t in character.GetComponentsInChildren<Transform>()) bones[t.name] = t;
        foreach (var (bone, euler) in pose)
        {
            if (bones.TryGetValue(bone, out var t)) t.localRotation = Quaternion.Euler(euler);
            else Debug.LogWarning("[R15] Unknown bone " + bone);
        }
    }

    // Sets humanoid muscles (-1..1) on a character instance, keeping every muscle not listed
    public static void ApplyPose(GameObject character, (string muscle, float value)[] pose)
    {
        var list = new List<KeyValuePair<string, float>>();
        foreach (var (muscle, value) in pose) list.Add(new KeyValuePair<string, float>(muscle, value));
        ApplyPose(character, list);
    }

    public static void ApplyPose(GameObject character, IEnumerable<KeyValuePair<string, float>> pose)
    {
        // HumanPoseHandler reads the body position in world space but writes it relative to the root: pose at the origin
        var root = character.transform;
        root.GetPositionAndRotation(out var position, out var rotation);
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = character.GetComponent<Animator>();
        var handler = new HumanPoseHandler(animator.avatar, root);
        var human = new HumanPose();
        handler.GetHumanPose(ref human);
        var index = new Dictionary<string, int>();
        for (int i = 0; i < HumanTrait.MuscleCount; i++) index[HumanTrait.MuscleName[i]] = i;
        foreach (var m in pose)
        {
            if (index.TryGetValue(m.Key, out int i)) human.muscles[i] = m.Value;
            else Debug.LogWarning("[R15] Unknown muscle " + m.Key);
        }
        handler.SetHumanPose(ref human);
        handler.Dispose();
        root.SetPositionAndRotation(position, rotation);
    }

    // Renders a camera to a PNG (outside the Game view, at any size)
    public static void RenderToPng(Camera camera, int width, int height, string path)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.aspect = width / (float)height; // otherwise it keeps the Game view's aspect
        camera.Render();
        camera.targetTexture = previous;
        camera.ResetAspect();
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = active;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    public static void AddLight(string name, Color color, float intensity, Vector3 euler, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows;
        light.shadowStrength = 0.85f;
        light.transform.rotation = Quaternion.Euler(euler);
    }

    public static void AddBaseplate()
    {
        var go = new GameObject("Baseplate");
        go.transform.position = new Vector3(0f, -0.2f, 0f);
        const string meshPath = "Assets/Characters/Meshes/Baseplate.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = RoundedBoxMesh.Create(new Vector3(80f, 0.4f, 80f), 0.05f, 2);
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        const string matPath = "Assets/Characters/Materials/Baseplate.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, matPath);
        }
        material.SetTexture("_BaseMap", GridTexture());
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.15f);
        material.SetTextureScale("_BaseMap", Vector2.one * 80f / (4f * R15CharacterBuilder.Stud)); // a grid square every 4 studs
        EditorUtility.SetDirty(material);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Roblox baseplate tile: gray with a lighter outline
    static Texture2D GridTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GridPath);
        if (existing != null) return existing;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var fill = new Color(0.56f, 0.57f, 0.6f);
        var line = new Color(0.66f, 0.67f, 0.7f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, x < 2 || y < 2 || x >= size - 2 || y >= size - 2 ? line : fill);
        File.WriteAllBytes(GridPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(GridPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(GridPath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GridPath);
    }

    static VolumeProfile BuildVolumeProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath) != null) AssetDatabase.DeleteAsset(VolumePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumePath);

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.2f);
        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.Override(10f);
        color.saturation.Override(12f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.15f);
        vignette.smoothness.Override(0.45f);
        foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }
}

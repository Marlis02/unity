using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// The characters of the project side by side, nothing else: the prefabs under Assets/Characters named in Shown (all of
// them, bacon first, when it is empty), standing easy with the arms along the body, on a plain studio floor that
// fades into a plain backdrop. No props, no Timeline. The camera keeps the whole row in frame at any Game view aspect
// (overscan gate fit), so a new character only needs a rebuild.
// Tools/Characters/Build Lineup -> Assets/Scenes/Characters_Lineup.unity
public static class CharacterLineupBuilder
{
    const string ScenePath = "Assets/Scenes/Characters_Lineup.unity";
    const string Folder = "Assets/Characters/Lineup";
    const string VolumePath = "Assets/Shorts/Shorts_Volume.asset";
    static readonly string[] Order = { "bacon" };
    // Who stands in the scene, empty for everyone
    static readonly string[] Shown = { };

    const float Spacing = 1.3f;     // between the characters' feet, metres (the R15 bacon is broad)
    const float Margin = 0.75f;     // free floor left and right of the row
    const float MinFrameWidth = 2f, FrameHeight = 2.5f; // the least the camera shows around the row
    const float CameraDistance = 6f, CameraHeight = 1.25f, AimHeight = 0.95f;
    static readonly Color Backdrop = new Color(0.72f, 0.74f, 0.78f);
    static readonly string[] Sides = { "Left", "Right" };

    [MenuItem("Tools/Characters/Build Lineup")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Lineup] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RigUtility.EnsureFolder(Folder);

        var prefabs = Characters();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        float row = (prefabs.Count - 1) * Spacing;
        var camera = BuildCamera(Mathf.Max(row + 2f * Margin, MinFrameWidth), FrameHeight);
        BuildStudio();

        var cast = new GameObject("Characters").transform;
        for (int i = 0; i < prefabs.Count; i++)
        {
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
            actor.transform.SetParent(cast, false);
            var feet = new Vector3(i * Spacing - row / 2f, 0f, 0f);
            var toCamera = camera.transform.position - feet;
            toCamera.y = 0f;
            actor.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(toCamera));
            StandEasy(actor);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Lineup] Built {ScenePath}: " + string.Join(", ", prefabs.Select(p => p.name)));
    }

    // Character prefabs (those with an Animator) under Assets/Characters that are Shown, in Order, any others
    // after them
    static List<GameObject> Characters()
    {
        var found = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Characters" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(p => p != null && p.GetComponent<Animator>() != null)
            .Where(p => Shown.Length == 0 || Shown.Contains(p.name));
        return found.OrderBy(p => Array.IndexOf(Order, p.name) is int k && k >= 0 ? k : Order.Length)
            .ThenBy(p => p.name).ToList();
    }

    // The arms hang along the body, elbows a touch bent, as in the shorts' Idle (bones in the rest pose are unrotated:
    // arm out = Z with the side's sign)
    static void StandEasy(GameObject actor)
    {
        var hang = actor.transform.InverseTransformVector(Find(actor, "RightHand").position - Find(actor, "RightLowerArm").position);
        float restOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        foreach (var side in Sides)
        {
            float o = side == "Left" ? -1f : 1f;
            Find(actor, side + "UpperArm").localRotation = Quaternion.Euler(0f, 0f, o * (3f - restOut));
            Find(actor, side + "LowerArm").localRotation = Quaternion.Euler(-8f, 0f, o * 3f);
        }
    }

    // Looking at the row from in front, a little above the middle of the bodies
    public static Camera BuildCamera(float frameWidth, float frameHeight)
    {
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, CameraHeight, -CameraDistance);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, AimHeight, 0f) - camera.transform.position);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Backdrop;
        camera.nearClipPlane = 0.05f;
        // The sensor is the frame around the row; overscan keeps all of it in view whatever the aspect
        camera.usePhysicalProperties = true;
        camera.sensorSize = new Vector2(36f, 36f * frameHeight / frameWidth);
        camera.gateFit = Camera.GateFitMode.Overscan;
        camera.focalLength = camera.sensorSize.x * CameraDistance / frameWidth;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze
        return camera;
    }

    // A plain floor fogged into the backdrop colour, a soft key light with shadows, a fill and a rim
    public static void BuildStudio()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.8f, 0.83f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.64f, 0.68f);
        RenderSettings.ambientGroundColor = new Color(0.4f, 0.41f, 0.44f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Backdrop;
        RenderSettings.fogStartDistance = 10f;
        RenderSettings.fogEndDistance = 28f;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.transform.localScale = Vector3.one * 8f;
        floor.GetComponent<MeshRenderer>().sharedMaterial = SavedMaterial("Lineup_Floor", Backdrop, 0.15f);

        AddLight("Key Light", new Color(1f, 0.97f, 0.92f), 1.3f, new Vector3(0.45f, -0.75f, 0.5f), LightShadows.Soft);
        AddLight("Fill Light", new Color(0.85f, 0.9f, 1f), 0.35f, new Vector3(-0.6f, -0.3f, 0.75f), LightShadows.None);
        AddLight("Rim Light", Color.white, 0.6f, new Vector3(0.2f, -0.5f, -0.85f), LightShadows.None);

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.transform.position = new Vector3(0f, 4f, 4f); // global, so anywhere: its icon is kept off the characters
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
    }

    static Material SavedMaterial(string name, Color color, float smoothness)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void AddLight(string name, Color color, float intensity, Vector3 direction, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows;
        light.shadowStrength = 0.75f;
        light.transform.rotation = Quaternion.LookRotation(direction.normalized);
        // Out where it shines from, so its Scene view icon doesn't cover the characters' feet
        light.transform.position = -direction.normalized * 6f;
    }

    static Transform Find(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        throw new ArgumentException($"No {name} under {root.name}");
    }
}

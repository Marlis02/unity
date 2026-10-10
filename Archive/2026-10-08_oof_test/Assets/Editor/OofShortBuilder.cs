using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using E = RobloxFace.Emotion;
using Object = UnityEngine.Object;
using Random = System.Random;

// "Oof": the bacon from Roblox Studio (obj_bacon, ObjBaconBuilder) runs an obby off the baseplate, hops two platforms,
// shows off, takes a big jump for the finish and lands on the lava block. He falls apart into his parts, R15 style; a
// moment later he respawns on the SpawnLocation in a forcefield, unimpressed. The fall is Unity physics, simulated once
// at build time and baked into a clip. Builds Shorts_Oof with a Timeline; Build(record: true) adds a Recorder track
// writing 1080x1920 at 30 fps in Play Mode (PNG frames -> MP4 via ShortsFrameEncoder). Tools/Shorts/Build Oof Short.
public static class OofShortBuilder
{
    const float Fps = 30f;
    // Beats, seconds
    const float RunFrom = 0.55f, RunTo = 1.3f, FlexFrom = 2.75f, FlexTo = 3.55f, WindUp = 3.65f, Oof = 4.75f;
    const float Respawn = 7.7f, LookAtUs = 8.6f, Duration = 10f;
    // Jumps: take-off, landing, from x, to x, height (metres)
    static readonly (float up, float down, float from, float to, float height)[] Jumps =
    {
        (1.35f, 1.85f, 1.2f, 2.6f, 0.55f),
        (2.1f, 2.6f, 2.6f, 4.0f, 0.55f),
        (3.95f, 4.6f, 4.0f, 5.4f, 0.75f), // for the finish, past the lava; he comes down on it
    };

    const string ScenePath = "Assets/Scenes/Shorts_Oof.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_Oof.playable";
    const string Folder = "Assets/Shorts/Oof";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string TextureFolder = Folder + "/Textures";
    const string ClipFolder = "Assets/Animations";
    const string VolumePath = "Assets/Shorts/Shorts_Volume.asset";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_Oof_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The course runs along +X, every top at y = 0: the baseplate ends at BaseplateEdge, then platforms over the void
    const float BaseplateEdge = 1.55f, BlockSize = 1.3f, BlockHeight = 0.5f;
    static readonly float[] Platforms = { 2.6f, 4.0f };
    const float LavaX = 5.4f, FinishX = 7.0f;
    static readonly Vector3 CameraOffset = new Vector3(1.5f, 1.95f, -4.9f);  // from the point it follows: ahead of him, so his face shows
    static readonly Vector3 CameraLook = new Vector3(0.45f, 0.8f, 0f);
    const float CameraFov = 46f;

    static readonly string[] Bones = ObjBaconBuilder.BoneNames;
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f;

    static float armRestOut, thigh, shin;
    static string facePath;
    static readonly List<Matrix4x4> partsAtOof = new List<Matrix4x4>(); // each part's world matrix as he falls apart

    static readonly (float time, E emotion)[] Faces =
    {
        (0f, E.Smug), (RunFrom, E.Grin), (FlexFrom, E.Laugh), (WindUp, E.Smug), (4.62f, E.Shocked), (Respawn, E.Bruh),
    };

    // ---------------------------------------------------------------- the act

    // Local bone rotations in degrees added to the rest pose (body facing +Z): arm out = Z with Out(side), arm forward =
    // -X, knee back = +X, spine/head forward = +X, turn right = +Y. `hips` moves the hips in his frame, `move` the whole
    // of him in the world.
    class Pose
    {
        public readonly Vector3[] e = new Vector3[Bones.Length];
        public Vector3 hips, move;

        public Pose Rot(string bone, float x, float y, float z)
        {
            e[Array.IndexOf(Bones, bone)] += new Vector3(x, y, z);
            return this;
        }

        public Pose Add(Pose other, float weight = 1f)
        {
            for (int i = 0; i < e.Length; i++) e[i] += other.e[i] * weight;
            hips += other.hips * weight;
            move += other.move * weight;
            return this;
        }
    }

    // He starts on the spawn facing +X (yaw 90), so his own +Z is the world's +X
    static Pose Act(float t)
    {
        var p = Idle(t);
        if (t >= Respawn) return p.Add(Unimpressed(t));
        p.move.x = X(t);
        // Smug at the camera (on his right) before he sets off
        p.Rot("Head", 0f, 28f * (1f - Smooth(RunFrom - 0.1f, RunFrom + 0.15f, t)), 0f);
        p.Add(Gait(t, RunFrom, RunTo));
        foreach (var j in Jumps) p.Add(Jump(t, j.up, j.down, j.height));
        // Showing off on the second platform: both arms up, pumping
        float flex = Env(t, FlexFrom, FlexTo, 0.18f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", -20f * flex, 0f, Out(s) * (120f + 12f * Wave(t, 3f)) * flex).Rot(s + "LowerArm", 0f, 0f, Out(s) * (35f + 30f * Wave(t, 3f)) * flex);
        p.Rot("Head", -10f * flex, 0f, 0f).Rot("Spine", -6f * flex, 0f, 0f);
        // Lands on the lava: a jolt
        float hit = Env(t, Jumps[2].down, Oof, 0.05f);
        p.Rot("Head", -12f * hit, 35f * hit, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", 0f, 0f, Out(s) * 40f * hit);
        return p;
    }

    // Where along the course he is
    static float X(float t)
    {
        float x = Mathf.Lerp(0f, Jumps[0].from, Along(t, RunFrom, RunTo));
        foreach (var j in Jumps)
            if (t > j.up) x = Mathf.Lerp(j.from, j.to, Mathf.Clamp01((t - j.up) / (j.down - j.up)));
        return x;
    }

    // Crouch, spring up with the arms thrown up in a V, land and soak it up
    static Pose Jump(float t, float up, float down, float height)
    {
        var p = new Pose();
        float start = up - 0.22f, settled = down + 0.25f;
        if (t < start || t > settled + 0.3f) return p;
        float air = Mathf.Clamp01((t - up) / (down - up));
        float arms = Smooth(up - 0.08f, up + 0.08f, t) * (1f - Smooth(down - 0.05f, settled, t));
        foreach (var s in Sides) p.Rot(s + "UpperArm", -25f * arms, 0f, Out(s) * 135f * arms).Rot(s + "LowerArm", 0f, 0f, Out(s) * 15f * arms);
        if (t < up) return Crouch(p, 30f * Smooth(start, up - 0.05f, t) * (1f - Smooth(up - 0.05f, up, t)));
        if (t < down)
        {
            p.hips.y += 4f * height * air * (1f - air);
            foreach (var s in Sides) p.Rot(s + "UpperLeg", -30f * Mathf.Sin(air * Mathf.PI), 0f, 0f).Rot(s + "LowerLeg", 50f * Mathf.Sin(air * Mathf.PI), 0f, 0f);
            return p;
        }
        return Crouch(p, 26f * Mathf.Sin(Mathf.Clamp01((t - down) / (settled - down)) * Mathf.PI));
    }

    // Thighs forward by a degrees, knees bent by 2a, feet flat; the hips drop and shift back to keep the feet planted
    static Pose Crouch(Pose p, float a)
    {
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -a, 0f, 0f).Rot(s + "LowerLeg", 2f * a, 0f, 0f).Rot(s + "Foot", -a, 0f, 0f);
        float r = a * Mathf.Deg2Rad;
        p.hips += new Vector3(0f, -(thigh + shin) * (1f - Mathf.Cos(r)), -(thigh - shin) * Mathf.Sin(r));
        return p.Rot("Spine", 0.4f * a, 0f, 0f);
    }

    // Back on the spawn: a pop up out of the forcefield, a look at the camera (on his right), a shrug
    static Pose Unimpressed(float t)
    {
        var p = new Pose();
        float pop = Mathf.Clamp01((t - Respawn) / 0.3f);
        p.hips.y += 0.08f * Mathf.Sin(pop * Mathf.PI);
        float look = Smooth(LookAtUs, LookAtUs + 0.3f, t);
        p.Rot("Head", 0f, 55f * look, 0f).Rot("Spine", 0f, 18f * look, 0f);
        float shrug = Env(t, LookAtUs + 0.35f, Duration + 1f, 0.3f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", -10f * shrug, 0f, Out(s) * 28f * shrug).Rot(s + "LowerArm", -70f * shrug, 0f, 0f);
        p.Rot("Head", 0f, 0f, 10f * shrug);
        return p;
    }

    // Standing easy, arms hanging along the body
    static Pose Idle(float t)
    {
        float breathe = Wave(t, 0.4f);
        var p = new Pose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (3f - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -8f, 0f, Out(s) * 3f);
        return p;
    }

    // Running: legs and arms swinging hard, leaning in
    static Pose Gait(float t, float start, float end)
    {
        var p = new Pose();
        float gait = Env(t, start, end, 0.12f);
        if (gait <= 0f) return p;
        float phase = (t - start) * 2.4f;
        float swing = Mathf.Sin(phase * 2f * Mathf.PI);
        for (int k = 0; k < 2; k++)
        {
            string s = Sides[k];
            float side = k == 0 ? 1f : -1f;
            float knee = Mathf.Max(0f, Mathf.Sin((phase + k * 0.5f) * 2f * Mathf.PI));
            p.Rot(s + "UpperLeg", -34f * swing * side * gait, 0f, 0f).Rot(s + "LowerLeg", 50f * knee * gait, 0f, 0f);
            p.Rot(s + "UpperArm", 30f * swing * side * gait, 0f, 0f).Rot(s + "LowerArm", -40f * gait, 0f, 0f);
        }
        p.Rot("Spine", 8f * gait, 0f, 0f);
        p.hips.y += 0.03f * Mathf.Abs(swing) * gait;
        return p;
    }

    static float Along(float t, float start, float end)
    {
        float a = Mathf.Clamp01((t - start) / (end - start));
        return a * a * (1.6f - 0.6f * a);
    }

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Shorts/Build Oof Short")]
    public static void Build() => Build(record: false);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Oof] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var folder in new[] { MeshFolder, MaterialFolder, TextureFolder, ClipFolder, "Assets/Timelines" }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ObjBaconBuilder.PrefabPath);
        if (prefab == null) { Debug.LogError("[Oof] obj_bacon missing: run Tools/Characters/Build OBJ Bacon"); return; }
        var hang = Find(prefab, "RightHand").position - Find(prefab, "RightLowerArm").position;
        armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        thigh = Vector3.Distance(Find(prefab, "LeftUpperLeg").position, Find(prefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(prefab, "LeftLowerLeg").position, Find(prefab, "LeftFoot").position);
        facePath = AnimationUtility.CalculateTransformPath(Find(prefab, "Face"), prefab.transform);

        AnimationClip clip;
        var stage = EditorSceneManager.NewPreviewScene();
        try { clip = BakeClip(prefab, stage); }
        finally { EditorSceneManager.ClosePreviewScene(stage); }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = BuildStage();
        var colliders = BuildCourse();
        var forcefield = Forcefield();

        var bacon = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        bacon.name = "Bacon";
        var rotation = Quaternion.Euler(0f, 90f, 0f);
        bacon.transform.SetPositionAndRotation(Vector3.zero, rotation);
        var (debris, debrisClip) = FallApart(bacon, colliders);
        foreach (var c in colliders) Object.DestroyImmediate(c);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        // Keep the take counter across rebuilds so old recordings aren't overwritten
        int take = 1;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings old) take = Mathf.Max(take, old.Take);
        foreach (var track in new List<TrackAsset>(timeline.GetOutputTracks())) timeline.DeleteTrack(track);
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings orphan)
            {
                AssetDatabase.RemoveObjectFromAsset(orphan);
                Object.DestroyImmediate(orphan, true);
            }
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;

        var director = new GameObject("Timeline_Oof").AddComponent<PlayableDirector>();
        var baconTrack = timeline.CreateTrack<AnimationTrack>(null, "Bacon");
        baconTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
        baconTrack.position = Vector3.zero;
        baconTrack.rotation = rotation;
        var baconClip = baconTrack.CreateClip(clip);
        baconClip.start = 0;
        baconClip.duration = Duration;
        ((AnimationPlayableAsset)baconClip.asset).applyFootIK = false; // the baked clip has no IK goals
        director.SetGenericBinding(baconTrack, bacon.GetComponent<Animator>());

        // He is there until he falls apart, and again from the respawn; his parts in between
        var shown = timeline.CreateTrack<ActivationTrack>(null, "Bacon shown");
        foreach (var (from, to) in new[] { (0f, Oof), (Respawn, Duration) })
        {
            var c = shown.CreateDefaultClip();
            c.start = from;
            c.duration = to - from;
        }
        director.SetGenericBinding(shown, bacon);
        Activate(timeline, director, "Parts", debris, Oof, Duration);
        Bind(timeline, director, "Parts fall", debris, debrisClip);
        Activate(timeline, director, "Forcefield", forcefield, Respawn, Respawn + 1.3f);
        Bind(timeline, director, "Forcefield pulse", forcefield, ForcefieldClip());
        Bind(timeline, director, "Camera", camera.transform.parent.gameObject, CameraClip());

        if (record) AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Oof] Built {ScenePath}" + (record ? ": press Play to record" : ""));
    }

    // A generic clip keyed on children of `target` (given an Animator), over the whole short
    static void Bind(TimelineAsset timeline, PlayableDirector director, string name, GameObject target, AnimationClip clip)
    {
        var animator = target.GetComponent<Animator>();
        if (animator == null) animator = target.AddComponent<Animator>();
        var track = timeline.CreateTrack<AnimationTrack>(null, name);
        var c = track.CreateClip(clip);
        c.start = 0;
        c.duration = Duration;
        director.SetGenericBinding(track, animator);
    }

    static void Activate(TimelineAsset timeline, PlayableDirector director, string name, GameObject target, float from, float to)
    {
        var track = timeline.CreateTrack<ActivationTrack>(null, name);
        var clip = track.CreateDefaultClip();
        clip.start = from;
        clip.duration = to - from;
        director.SetGenericBinding(track, target);
        target.SetActive(false);
    }

    // ---------------------------------------------------------------- stage

    // Following him along the course from ahead and to his right, a little above; a sunny Roblox sky
    static Camera BuildStage()
    {
        var rig = new GameObject("Camera Rig");
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.transform.localPosition = CameraOffset;
        camera.transform.rotation = Quaternion.LookRotation(CameraLook - CameraOffset);
        camera.fieldOfView = CameraFov;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.05f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze

        var sky = SavedMaterial("Sky", "Skybox/Procedural", m =>
        {
            m.SetFloat("_SunSize", 0.035f);
            m.SetFloat("_AtmosphereThickness", 0.75f);
            m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
            m.SetColor("_GroundColor", new Color(0.55f, 0.66f, 0.82f));
            m.SetFloat("_Exposure", 1.25f);
        });
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.95f);
        RenderSettings.ambientEquatorColor = new Color(0.66f, 0.68f, 0.72f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.43f, 0.46f);
        RenderSettings.fog = false;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.97f, 0.9f);
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.7f;
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.35f, -0.85f, 0.45f));

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        return camera;
    }

    // The grey studded baseplate with a SpawnLocation, two stone platforms over the void, the neon lava block and a
    // green finish pad. Returns the colliders the falling parts land on (removed again after the simulation).
    static List<Collider> BuildCourse()
    {
        var course = new GameObject("Obby").transform;
        var colliders = new List<Collider>();
        var baseplate = Lit("Baseplate", Color.white, 0.15f);
        baseplate.SetTexture("_BaseMap", GridTexture());
        const float plate = 24f;
        baseplate.SetTextureScale("_BaseMap", Vector2.one * plate / 2f);
        var basePart = Box(course, "Baseplate", new Vector3(plate, 0.4f, plate), new Vector3(BaseplateEdge - plate / 2f, -0.2f, 0f), baseplate);
        colliders.Add(basePart.AddComponent<BoxCollider>());

        var stone = Lit("Medium Stone Grey", Hex("A3A2A5"), 0.2f);
        foreach (float x in Platforms)
            colliders.Add(Box(course, "Platform", new Vector3(BlockSize, BlockHeight, BlockSize), new Vector3(x, -BlockHeight / 2f, 0f), stone, 0.02f).AddComponent<BoxCollider>());
        var lava = Emissive("Lava", Hex("FF1A1A"), 2.2f);
        colliders.Add(Box(course, "Lava", new Vector3(BlockSize, BlockHeight, BlockSize), new Vector3(LavaX, -BlockHeight / 2f, 0f), lava, 0.02f).AddComponent<BoxCollider>());
        var finish = Emissive("Finish", Hex("2BE36B"), 1.4f);
        Box(course, "Finish", new Vector3(1.8f, BlockHeight, 1.8f), new Vector3(FinishX + 0.25f, -BlockHeight / 2f, 0f), finish, 0.02f);

        // SpawnLocation: a dark pad with the spawn ring on it
        var pad = Lit("Spawn Pad", Hex("6B6D72"), 0.3f);
        Box(course, "SpawnLocation", new Vector3(1.5f, 0.1f, 1.5f), new Vector3(0f, 0.05f, 0f), pad, 0.01f);
        var ring = Part(course, "Spawn Ring", SaveMesh(FlatQuad(1.25f), "Oof_SpawnQuad"), SpawnMaterial());
        ring.transform.position = new Vector3(0f, 0.102f, 0f);
        ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        return colliders;
    }

    // The bluish bubble he respawns in
    static GameObject Forcefield()
    {
        var holder = new GameObject("Forcefield");
        holder.transform.position = new Vector3(0f, 0.9f, 0f);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.name = "Bubble";
        sphere.transform.SetParent(holder.transform, false);
        sphere.transform.localScale = Vector3.one * 2.1f;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = SavedMaterial("Forcefield", "Universal Render Pipeline/Unlit", m =>
        {
            m.SetColor("_BaseColor", new Color(0.45f, 0.75f, 1f, 0.28f));
            Transparent(m);
        });
        sphere.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return holder;
    }

    // ---------------------------------------------------------------- falling apart

    // Copies of his parts where they are at Oof, given a pop, a spin and gravity, simulated with Unity physics on the
    // course's colliders and baked into a clip on the parts' holder
    static (GameObject holder, AnimationClip clip) FallApart(GameObject bacon, List<Collider> ground)
    {
        var holder = new GameObject("Parts");
        var bodies = new List<(Transform t, Rigidbody rb)>();
        var parts = new List<MeshRenderer>();
        foreach (var r in bacon.GetComponentsInChildren<MeshRenderer>(true)) if (r.name != "Face") parts.Add(r);
        var rng = new Random(4);
        var actorToWorld = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
        for (int i = 0; i < parts.Count; i++)
        {
            var source = parts[i];
            var m = actorToWorld * partsAtOof[i];
            var copy = new GameObject(source.name);
            copy.transform.SetParent(holder.transform, false);
            copy.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            copy.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            // The face goes with the head, shocked
            if (source.name == "HeadMesh")
            {
                var face = Object.Instantiate(Find(bacon, "Face").gameObject, copy.transform, false);
                face.name = "Face";
                face.transform.localPosition = Find(bacon, "Face").localPosition;
                face.GetComponent<RobloxFace>().emotion = (int)E.Shocked;
                face.GetComponent<RobloxFace>().autoBlink = false;
            }
            var box = copy.AddComponent<BoxCollider>();
            var rb = copy.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.linearVelocity = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.7f, 0.4f + 0.8f * (float)rng.NextDouble(), ((float)rng.NextDouble() - 0.5f) * 0.7f);
            rb.angularVelocity = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 7f;
            bodies.Add((copy.transform, rb));
        }

        // R15 parts overlap at the joints: left to collide they would blast each other apart. They only meet the course.
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
                Physics.IgnoreCollision(bodies[i].t.GetComponent<Collider>(), bodies[j].t.GetComponent<Collider>());

        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        int frames = Mathf.RoundToInt(Duration * Fps);
        var keys = new Dictionary<Transform, List<Keyframe>[]>();
        foreach (var (t, _) in bodies) keys[t] = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        try
        {
            Physics.SyncTransforms();
            for (int f = 0; f <= frames; f++)
            {
                float time = f / Fps;
                if (time > Oof) for (int s = 0; s < 4; s++) Physics.Simulate(1f / (Fps * 4f));
                foreach (var (t, _) in bodies)
                {
                    var p = t.localPosition;
                    var q = t.localRotation;
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                    for (int k = 0; k < 7; k++) keys[t][k].Add(new Keyframe(time, values[k]));
                }
            }
        }
        finally
        {
            Physics.simulationMode = mode;
        }
        foreach (var (t, rb) in bodies)
        {
            Object.DestroyImmediate(rb);
            Object.DestroyImmediate(t.GetComponent<BoxCollider>());
        }

        var clip = SavedClip("Oof_Parts");
        string[] names = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        foreach (var (t, _) in bodies)
        {
            for (int k = 0; k < 7; k++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(t.name, typeof(Transform), names[k]), new AnimationCurve(keys[t][k].ToArray()));
        }
        EditorUtility.SetDirty(clip);
        return (holder, clip);
    }

    // ---------------------------------------------------------------- clips

    // Follows him along the course with a little lag; cuts back to the spawn for the respawn
    static AnimationClip CameraClip()
    {
        var clip = SavedClip("Oof_Camera");
        var keys = new List<Keyframe>();
        float follow = 0f;
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++)
        {
            float t = f / Fps;
            float target = t >= Respawn ? 0f : Mathf.Min(X(t), LavaX);
            follow = t >= Respawn && t - 1f / Fps < Respawn ? 0f : Mathf.Lerp(follow, target, 1f - Mathf.Exp(-6f / Fps));
            keys.Add(new Keyframe(t, CameraOffset.x + follow));
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Main Camera", typeof(Transform), "m_LocalPosition.x"), new AnimationCurve(keys.ToArray()));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Main Camera", typeof(Transform), "m_LocalPosition.y"), AnimationCurve.Constant(0f, Duration, CameraOffset.y));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Main Camera", typeof(Transform), "m_LocalPosition.z"), AnimationCurve.Constant(0f, Duration, CameraOffset.z));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // The bubble swells in and wobbles
    static AnimationClip ForcefieldClip()
    {
        var clip = SavedClip("Oof_Forcefield");
        var keys = new List<Keyframe>();
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++)
        {
            float t = f / Fps, s = Mathf.Clamp01((t - Respawn) / 0.25f);
            keys.Add(new Keyframe(t, 2.1f * (0.6f + 0.4f * s) * (1f + 0.04f * Mathf.Sin(t * 20f))));
        }
        foreach (var axis in new[] { "x", "y", "z" })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Bubble", typeof(Transform), "m_LocalScale." + axis), new AnimationCurve(keys.ToArray()));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // ---------------------------------------------------------------- baking

    // Poses a scratch instance frame by frame and reads it back as a humanoid pose; notes where each part is at Oof
    static AnimationClip BakeClip(GameObject prefab, Scene stage)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, stage);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bones = Array.ConvertAll(Bones, b => Find(go, b));
        var hipsRest = bones[0].localPosition;
        var toActor = Quaternion.Inverse(Quaternion.Euler(0f, 90f, 0f));
        var handler = new HumanPoseHandler(go.GetComponent<Animator>().avatar, go.transform);
        var human = new HumanPose();
        var muscles = new List<int>();
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
        {
            int bone = HumanTrait.BoneFromMuscle(m);
            if (bone < (int)HumanBodyBones.LeftThumbProximal || bone > (int)HumanBodyBones.RightLittleDistal) muscles.Add(m);
        }
        var names = new List<string> { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        foreach (int m in muscles) names.Add(HumanTrait.MuscleName[m]);

        int frames = Mathf.RoundToInt(Duration * Fps), oofFrame = Mathf.RoundToInt(Oof * Fps);
        var samples = new float[names.Count, frames + 1];
        var previous = Quaternion.identity;
        partsAtOof.Clear();
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            var pose = Act(t);
            for (int b = 0; b < bones.Length; b++) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
            bones[0].localPosition = hipsRest + pose.hips + toActor * pose.move;
            if (f == oofFrame)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.name != "Face") partsAtOof.Add(r.transform.localToWorldMatrix);
            handler.GetHumanPose(ref human);
            var q = human.bodyRotation;
            if (Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            previous = q;
            var p = human.bodyPosition;
            float[] root = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
            for (int i = 0; i < root.Length; i++) samples[i, f] = root[i];
            for (int i = 0; i < muscles.Count; i++) samples[root.Length + i, f] = human.muscles[muscles[i]];
        }
        handler.Dispose();
        Object.DestroyImmediate(go);

        var clip = SavedClip("Oof_Bacon");
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int i = 0; i < names.Count; i++)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), names[i]));
            var keys = new Keyframe[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, frames);
                // No smoothing across the jump back to the spawn while he is hidden
                float slope = (f == Mathf.RoundToInt(Respawn * Fps) || f + 1 == Mathf.RoundToInt(Respawn * Fps)) ? 0f : (samples[i, b] - samples[i, a]) * Fps / (b - a);
                keys[f] = new Keyframe(f / Fps, samples[i, f], slope, slope);
            }
            curves.Add(new AnimationCurve(keys));
        }
        var emotion = new AnimationCurve();
        foreach (var (time, e) in Faces) emotion.AddKey(new Keyframe(time, RobloxFace.EmotionKey(e), float.PositiveInfinity, float.PositiveInfinity));
        bindings.Add(EditorCurveBinding.DiscreteCurve(facePath, typeof(RobloxFace), "emotion"));
        curves.Add(emotion);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // ---------------------------------------------------------------- textures, meshes, materials

    // Roblox's baseplate: light grey squares with darker seams (one texture repeat = 2 m)
    static Texture2D GridTexture()
    {
        const int size = 256;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
                int edge = Mathf.Min(dx, dy);
                byte g = (byte)(edge < 2 ? 120 : edge < 5 ? 150 : 166 + ((x * 7 + y * 13) % 5));
                px[y * size + x] = new Color32(g, g, (byte)(g + 3), 255);
            }
        return SaveTexture(px, size, "Oof_Baseplate", TextureWrapMode.Repeat);
    }

    // The spawn ring: a white ring round a white dot on transparent
    static Material SpawnMaterial()
    {
        const int size = 256;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude / (size / 2f);
                bool on = (r > 0.62f && r < 0.8f) || r < 0.28f;
                px[y * size + x] = on ? new Color32(235, 238, 242, 255) : new Color32(0, 0, 0, 0);
            }
        var texture = SaveTexture(px, size, "Oof_SpawnRing", TextureWrapMode.Clamp);
        return SavedMaterial("Spawn Ring", "Universal Render Pipeline/Lit", m =>
        {
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
        });
    }

    static Texture2D SaveTexture(Color32[] px, int size, string name, TextureWrapMode wrap)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(px);
        texture.Apply();
        string path = $"{TextureFolder}/{name}.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = wrap;
        importer.alphaIsTransparency = true;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // A square in the XY plane facing -Z, UVs 0..1
    static Mesh FlatQuad(float size)
    {
        float h = size / 2f;
        var mesh = new Mesh();
        mesh.SetVertices(new List<Vector3> { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static readonly Dictionary<string, Mesh> boxes = new Dictionary<string, Mesh>();

    static GameObject Box(Transform parent, string name, Vector3 size, Vector3 position, Material material, float radius = 0f)
    {
        string key = $"Oof_Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}";
        if (!boxes.TryGetValue(key, out var mesh) || mesh == null)
            boxes[key] = mesh = SaveMesh(RoundedBoxMesh.Create(size, radius, radius > 0f ? 3 : 1), key);
        var go = Part(parent, name, mesh, material);
        go.transform.localPosition = position;
        return go;
    }

    static GameObject Part(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static Mesh SaveMesh(Mesh mesh, string name) => RigUtility.SaveMesh(mesh, $"{MeshFolder}/{name}.asset");

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    static Material Lit(string name, Color color, float smoothness) =>
        SavedMaterial(name, "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
        });

    static Material Emissive(string name, Color color, float intensity) =>
        SavedMaterial(name, "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", color * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });

    static void Transparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    static Material SavedMaterial(string name, string shader, Action<Material> setup)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        string path = $"{MaterialFolder}/Oof_{name.Replace(' ', '_')}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null || material.shader.name != shader)
        {
            material = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(material, path);
        }
        setup(material);
        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    static AnimationClip SavedClip(string name)
    {
        string path = $"{ClipFolder}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = Fps;
        return clip;
    }

    static Transform Find(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        throw new ArgumentException($"No {name} under {root.name}");
    }

    static Color Hex(string hex) => RigUtility.Hex(hex);

    // Recorder clips only record in Play Mode: pressing Play runs the Timeline (playOnAwake) and writes the frames
    static void AddRecorderTrack(TimelineAsset timeline, int take)
    {
        var track = timeline.CreateTrack<RecorderTrack>(null, "Recorder");
        var clip = track.CreateClip<RecorderClip>();
        clip.start = 0;
        clip.duration = Duration;
        clip.displayName = "1080x1920 30fps -> MP4";

        var settings = ScriptableObject.CreateInstance<ImageRecorderSettings>();
        settings.name = "Shorts Frames";
        settings.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
        settings.CaptureAlpha = false;
        settings.imageInputSettings = new GameViewInputSettings { OutputWidth = VideoWidth, OutputHeight = VideoHeight };
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = Fps;
        settings.CapFrameRate = true;
        settings.FileNameGenerator.Root = OutputPath.Root.Project;
        settings.FileNameGenerator.Leaf = RecordingFolder;
        settings.FileNameGenerator.FileName = "frame_<Frame>";
        settings.Take = take;
        AssetDatabase.AddObjectToAsset(settings, timeline);
        ((RecorderClip)clip.asset).settings = settings;
    }
}

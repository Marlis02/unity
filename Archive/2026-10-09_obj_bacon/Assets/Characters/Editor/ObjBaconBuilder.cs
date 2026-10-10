using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// The main character since 2026-10-08, from the R15 bacon exported by Roblox Studio (Imports/bacon.obj: Export
// Selection, no .mtl, so no colours or face came with it), built the way main_hero is. The OBJ is in studs,
// right-handed, facing -Z: here it is scaled by StudToMetre, mirrored to Unity's left-handed axes and turned to face +Z,
// standing on the origin.
//
// R15 parts run deep into each other (a forearm 15 cm up into the upper arm, a shin 23 cm up into the thigh), and every
// bend brought the hidden ends out. But each pair is made to meet on a seam where their outlines match (to under a
// centimetre). Each part is cut SeamGap off that seam on its own side, past the bevelled ends the R15 parts have there
// (where two bevels met, the seam creased into a dark fold as it bent), and the gap is bridged, so the torso and each
// limb are one closed mesh with no caps, steps or pinholes (Limbs). The head and the hair stay separate and rigid,
// Roblox-style. Every vertex moves with its own part's bone of a 17-bone skeleton named like main_hero's humanoid one,
// clavicles included; at the waist, elbows, wrists, hips, knees and ankles it hands over to the bone across the joint
// within JointBand of it, half and half on the seam, so the joint bends like cloth; knees and elbows pivot near the
// inside of the bend, like hinges (Hinges), so that side doesn't crush into folds. The shoulders are rigid and built
// like main_hero's: the upper arm is a straight sleeve (Sleeve) hanging from the side of the torso, and the clavicle
// lifts a raised arm (in the pose code).
//
// Each Spec dresses this one body: bacon_base is the bare grey base; obj_bacon wears a black T-shirt with the play
// logo, sleeves to just over the elbows, dark blue jeans and white trainers, and keeps the grey hair the model came with
// (the user found the plain grey the most Roblox-like). Edit Specs and re-run Tools/Characters/Build OBJ Bacon; prefabs
// are rebuilt in place.
public static class ObjBaconBuilder
{
    const string Source = "Imports/bacon.obj";
    public const string Folder = "Assets/Characters/ObjBacon";
    const string PrefabFolder = Folder + "/Prefabs";
    public const string BasePrefabPath = PrefabFolder + "/bacon_base.prefab";
    public const string PrefabPath = PrefabFolder + "/obj_bacon.prefab";
    const string MeshFolder = Folder + "/Meshes", MaterialFolder = Folder + "/Materials";
    const string LogoPath = Folder + "/PlayLogo.png"; // from Imports/play_logo_source.png, cropped to the D
    const float StudToMetre = 0.33f;

    // Which bone each OBJ group rides on. Roblox's character faces -Z with its right on +X, so the right limbs are the
    // groups on the +X side.
    static readonly (string group, string bone)[] Parts =
    {
        ("Rig10", "Head"), ("Handle1", "Head"),
        ("Rig15", "Spine"), ("Rig14", "Hips"),
        ("Rig9", "LeftUpperArm"), ("Rig8", "LeftLowerArm"), ("Rig7", "LeftHand"),
        ("Rig13", "RightUpperArm"), ("Rig12", "RightLowerArm"), ("Rig11", "RightHand"),
        ("Rig6", "LeftUpperLeg"), ("Rig5", "LeftLowerLeg"), ("Rig4", "LeftFoot"),
        ("Rig3", "RightUpperLeg"), ("Rig2", "RightLowerLeg"), ("Rig1", "RightFoot"),
    };

    // Where two parts are cut to stand end to end: (the part above, the part below, the bone whose joint is the seam)
    static readonly (string upper, string lower, string bone)[] Seams =
    {
        ("Rig15", "Rig14", "Spine"),
        ("Rig9", "Rig8", "LeftLowerArm"), ("Rig8", "Rig7", "LeftHand"),
        ("Rig13", "Rig12", "RightLowerArm"), ("Rig12", "Rig11", "RightHand"),
        ("Rig6", "Rig5", "LeftLowerLeg"), ("Rig5", "Rig4", "LeftFoot"),
        ("Rig3", "Rig2", "RightLowerLeg"), ("Rig2", "Rig1", "RightFoot"),
    };

    public static readonly string[] BoneNames =
    {
        "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };
    static readonly Dictionary<string, string> Parents = new Dictionary<string, string>
    {
        ["Hips"] = null, ["Spine"] = "Hips", ["Head"] = "Spine",
        ["LeftShoulder"] = "Spine", ["LeftUpperArm"] = "LeftShoulder", ["LeftLowerArm"] = "LeftUpperArm", ["LeftHand"] = "LeftLowerArm",
        ["RightShoulder"] = "Spine", ["RightUpperArm"] = "RightShoulder", ["RightLowerArm"] = "RightUpperArm", ["RightHand"] = "RightLowerArm",
        ["LeftUpperLeg"] = "Hips", ["LeftLowerLeg"] = "LeftUpperLeg", ["LeftFoot"] = "LeftLowerLeg",
        ["RightUpperLeg"] = "Hips", ["RightLowerLeg"] = "RightUpperLeg", ["RightFoot"] = "RightLowerLeg",
    };

    // The shoulder, as main_hero's (the user: "shoulders like main_hero"). The R15 upper arm swells into a rounded deltoid
    // towards the top, and rounded further into a dome it looked "over-pumped"; instead it is a sleeve (Sleeve): the
    // arm's own cross-section at the elbow carried straight up, leaning in to touch the side of the torso at the top,
    // rounded over SleeveRound to a flat crown SleeveCrown of its width. It pivots ShoulderOut outside the side of the
    // torso, low enough that an arm held out level lines up with the top of the torso; past that the clavicle (ClavicleIn
    // from the middle, ClavicleDrop under the torso's top) lifts it, so a raised arm comes up out of the shoulder.
    // (Tried before: pivoting on the arm's inner edge 19 cm down, a raised arm sank into the torso, "a gnome's arm";
    // 7 cm down, it stood 9 cm over the shoulders; soft shoulders stretched into flaps.)
    const float ClavicleIn = 0.09f, ClavicleDrop = 0.06f;
    const float ShoulderOut = 0.014f, SleeveTuck = 0.005f; // the sleeve's top reaches SleeveTuck into the torso's side
    const float SleeveRound = 0.06f, SleeveCrown = 0.35f, SleeveStep = 0.02f;
    const int SleeveSides = 40;
    const float HipDrop = 0.08f; // the leg pivots this far under the top of the thigh

    // Soft joints: within JointBand of these bones' joints the two parts meeting there hand over to each other's bone.
    // A soft joint is named by the bone below it; Chain is the bone below each one, along the body.
    const float JointBand = 0.05f;

    // Knees and elbows bend one way. Pivoting in the middle of the limb (28 cm deep at the knee), the inside half of the
    // bend had nowhere to go and folded over itself, poking out at the sides in jagged bumps; so they pivot HingeIn of
    // the way from the middle to the inside of the bend (the back of the knee, the front of the elbow), like a hinge,
    // and the outside stretches round it instead. Hinges: which way the inside is, along Z (the character faces +Z)
    const float HingeIn = 0.85f;
    static readonly Dictionary<string, float> Hinges = new Dictionary<string, float>
    {
        ["LeftLowerLeg"] = -1f, ["RightLowerLeg"] = -1f, ["LeftLowerArm"] = 1f, ["RightLowerArm"] = 1f,
    };

    // R15 parts are sparse (a shin is 80 triangles, long ones from knee to ankle), so a bend would kink: edge loops every
    // LoopStep across each soft joint's band give it vertices to spread over
    const float LoopStep = 0.008f;
    static readonly HashSet<string> SoftJoints = new HashSet<string>
    {
        "Spine", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand",
        "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
    };
    static readonly Dictionary<string, string> Chain = new Dictionary<string, string>
    {
        ["Hips"] = "Spine", ["Spine"] = "Head",
        ["LeftUpperArm"] = "LeftLowerArm", ["LeftLowerArm"] = "LeftHand", ["RightUpperArm"] = "RightLowerArm", ["RightLowerArm"] = "RightHand",
        ["LeftUpperLeg"] = "LeftLowerLeg", ["LeftLowerLeg"] = "LeftFoot", ["RightUpperLeg"] = "RightLowerLeg", ["RightLowerLeg"] = "RightFoot",
    };

    // Clothes: the sleeves end SleeveAboveElbow over the elbow, the T-shirt comes down over the waist to ShirtBelowTorso
    // under the bottom of the upper torso as it came (a band of jeans shows under it)
    const float SleeveAboveElbow = 0.03f, ShirtBelowTorso = 0.01f;
    const float LogoWidth = 0.36f, LogoHeight = 0.67f; // of the upper torso's width; centre up its height
    const string Grey = "C7C7C7";

    class Spec
    {
        public string name, path, meshPrefix;
        public string skin = Grey, hair, shirt, pants, shoes; // null: none (bald, bare)
        public bool logo, face;
    }

    static readonly Spec[] Specs =
    {
        // The bare base: grey all over, bald, faceless
        new Spec { name = "bacon_base", path = BasePrefabPath, meshPrefix = "BaconBase" },
        // The main character. Our first colouring (the short-4 test) was skin EBC79E, hair A64F17, jeans 2A2B31.
        new Spec
        {
            name = "obj_bacon", path = PrefabPath, meshPrefix = "ObjBacon",
            hair = Grey, shirt = "1E1E22", pants = "223A64", shoes = "F2F2F2", logo = true,
        },
    };

    [MenuItem("Tools/Characters/Build OBJ Bacon")]
    public static void Build()
    {
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder }) RigUtility.EnsureFolder(folder);
        materials.Clear();
        var groups = ReadObj(Source);
        foreach (var (_, parts) in Limbs)
            foreach (var group in parts) groups[group] = CloseCracks(groups[group]);
        Bounds B(string g) => groups[g].bounds;

        // The seams, and the joints: on the seams, else in the shoulders (arms) and under the top of the thigh (legs)
        var seamY = new Dictionary<string, float>();
        var seamMiddle = new Dictionary<string, Vector2>();
        var joints = new Dictionary<string, Vector3>();
        foreach (var (upper, lower, bone) in Seams)
        {
            var (y, middle) = Seam(groups[upper], groups[lower]);
            // Nothing of the lower part may be cut off on its own: a bit standing up out of the upper part's outline
            // (the R15 foot's toe, in front of the shin) keeps the seam above it, the cut just clear of it (the foot
            // block narrows from 1 mm under the toe's top, and a cut higher up its slope showed as a bright line)
            for (int pass = 0; pass < 5; pass++)
            {
                float island = IslandTop(groups[lower], groups[upper], y, middle);
                if (island <= y - SeamGap) break;
                y = island + SeamGap + 0.0005f;
            }
            seamY[bone] = y;
            seamMiddle[bone] = middle;
            float z = middle.y;
            if (Hinges.TryGetValue(bone, out float inside))
            {
                var outline = Outline(groups[lower], y);
                z = Mathf.Lerp(middle.y, inside > 0f ? outline.Max(p => p.y) : outline.Min(p => p.y), HingeIn);
            }
            joints[bone] = new Vector3(bone == "Spine" ? 0f : middle.x, y, z);
        }
        var sleeves = new Dictionary<string, Mesh>();
        foreach (var (side, upperArm, upperLeg) in new[] { ("Left", "Rig9", "Rig6"), ("Right", "Rig13", "Rig3") })
        {
            float sign = side == "Left" ? -1f : 1f;
            // The sleeve, and its pivot: an arm held out level has its top on the torso's
            float torsoSide = sign > 0f ? B("Rig15").max.x : B("Rig15").min.x, torsoTop = B("Rig15").max.y;
            var sleeve = sleeves[upperArm] = Sleeve(groups[upperArm], seamY[side + "LowerArm"] + SeamGap, B(upperArm).max.y, torsoSide);
            float pivotX = torsoSide + sign * ShoulderOut;
            float outer = sleeve.vertices.Where(p => p.y >= sleeve.bounds.max.y - SleeveRound - 0.001f).Max(p => sign * p.x) * sign;
            joints[side + "UpperArm"] = new Vector3(pivotX, torsoTop - Mathf.Abs(outer - pivotX), sleeve.bounds.center.z);
            joints[side + "Shoulder"] = new Vector3(sign * ClavicleIn, torsoTop - ClavicleDrop, B("Rig15").center.z);
            joints[side + "UpperLeg"] = new Vector3(B(upperLeg).center.x, B(upperLeg).max.y - HipDrop, B(upperLeg).center.z);
        }
        joints["Hips"] = new Vector3(0f, joints["LeftUpperLeg"].y, B("Rig14").center.z);
        joints["Head"] = new Vector3(0f, (B("Rig15").max.y + B("Rig10").min.y) / 2f, B("Rig10").center.z);

        // Where the clothes change colour, in the character's space
        float sleeveLeft = joints["LeftLowerArm"].y + SleeveAboveElbow, sleeveRight = joints["RightLowerArm"].y + SleeveAboveElbow;
        float shirtHem = B("Rig15").min.y - ShirtBelowTorso;
        var hems = new Dictionary<string, float> { ["Rig9"] = sleeveLeft, ["Rig13"] = sleeveRight, ["Rig14"] = shirtHem };

        // The parts, in the character's space: the upper arms sleeves, each part cut SeamGap off its seams (past the R15
        // parts' bevelled ends, whose meeting creased into dark folds), edge loops across its soft joints and along its hem
        var shapes = new Dictionary<string, Mesh>();
        foreach (var (group, _) in Parts) shapes[group] = sleeves.TryGetValue(group, out var made) ? made : groups[group];
        foreach (var (upper, lower, bone) in Seams)
        {
            shapes[upper] = Clip(shapes[upper], seamY[bone] + SeamGap, keepAbove: true);
            shapes[lower] = Clip(shapes[lower], seamY[bone] - SeamGap, keepAbove: false);
        }
        foreach (var (group, partBone) in Parts)
        {
            if (group == "Handle1" || group == "Rig10") continue;
            foreach (var (_, joint, _) in Across(partBone, joints, groups[group].bounds.center))
                for (float d = -JointBand - LoopStep; d <= JointBand + LoopStep + 1e-4f; d += LoopStep)
                    if (Mathf.Abs(d) > SeamGap) shapes[group] = Clip(shapes[group], joint.y + d, keepAbove: null);
            if (hems.TryGetValue(group, out float hem)) shapes[group] = Clip(shapes[group], hem, keepAbove: null);
        }

        // Each limb, and the torso, one mesh: its parts stitched together across each seam's gap
        var limbs = new List<(string name, Limb limb)>();
        foreach (var (name, parts) in Limbs)
        {
            var limb = new Limb();
            var start = new Dictionary<string, int>();
            foreach (var group in parts)
            {
                var mesh = shapes[group];
                start[group] = limb.vertices.Count;
                limb.vertices.AddRange(mesh.vertices);
                limb.normals.AddRange(mesh.normals);
                for (int i = 0; i < mesh.vertexCount; i++) limb.groups.Add(group);
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3) limb.Add(t[i] + start[group], t[i + 1] + start[group], t[i + 2] + start[group], group);
            }
            for (int k = 0; k + 1 < parts.Length; k++)
            {
                var (upper, lower, bone) = Seams.First(s => s.upper == parts[k] && s.lower == parts[k + 1]);
                var middle = seamMiddle[bone];
                var top = BoundaryLoop(shapes[upper], seamY[bone] + SeamGap, middle).ConvertAll(i => i + start[upper]);
                var bottom = BoundaryLoop(shapes[lower], seamY[bone] - SeamGap, middle).ConvertAll(i => i + start[lower]);
                Bridge(limb, top, bottom, middle, upper);
            }
            RingNormals(limb);
            limbs.Add((name, limb));
        }

        foreach (var spec in Specs)
        {
            var root = new GameObject(spec.name);
            var bones = new Dictionary<string, Transform>();
            foreach (var name in BoneNames)
            {
                var t = new GameObject(name).transform;
                t.SetParent(Parents[name] == null ? root.transform : bones[Parents[name]], false);
                t.position = joints[name];
                bones[name] = t;
            }

            foreach (var (name, limb) in limbs)
            {
                // Bones: the parts' own and those they hand over to
                var names = new List<string>();
                foreach (var group in limb.groups.Distinct())
                {
                    string own = BoneOf(group);
                    if (!names.Contains(own)) names.Add(own);
                    foreach (var (other, _, _) in Across(own, joints, groups[group].bounds.center))
                        if (!names.Contains(other)) names.Add(other);
                }
                // Colours: each triangle's from its part and which side of the part's hem it lies
                var colors = new List<string>();
                var bySubmesh = new List<List<int>>();
                for (int k = 0; k < limb.triangles.Count; k += 3)
                {
                    var middle = (limb.vertices[limb.triangles[k]] + limb.vertices[limb.triangles[k + 1]] + limb.vertices[limb.triangles[k + 2]]) / 3f;
                    var (hem, below, above) = Colors(limb.parts[k / 3], spec, sleeveLeft, sleeveRight, shirtHem);
                    string color = middle.y > hem ? above : below;
                    int sub = colors.IndexOf(color);
                    if (sub < 0) { colors.Add(color); bySubmesh.Add(new List<int>()); sub = colors.Count - 1; }
                    bySubmesh[sub].AddRange(new[] { limb.triangles[k], limb.triangles[k + 1], limb.triangles[k + 2] });
                }
                var mesh = new Mesh { indexFormat = limb.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(limb.vertices);
                mesh.SetNormals(limb.normals);
                mesh.subMeshCount = bySubmesh.Count;
                for (int sub = 0; sub < bySubmesh.Count; sub++) mesh.SetTriangles(bySubmesh[sub], sub);
                mesh.boneWeights = LimbWeights(limb, joints, groups, names);
                mesh.bindposes = names.ConvertAll(n => bones[n].worldToLocalMatrix).ToArray();
                mesh.RecalculateBounds();
                var part = new GameObject(name);
                part.transform.SetParent(root.transform, false);
                var skin = part.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = RigUtility.SaveMesh(mesh, $"{MeshFolder}/{spec.meshPrefix}_{name}.asset");
                skin.bones = names.ConvertAll(n => bones[n]).ToArray();
                skin.rootBone = bones[names[0]];
                skin.sharedMaterials = colors.ConvertAll(ColorMaterial).ToArray();
            }

            // The head and the hair, rigid on the Head bone
            foreach (var (group, label) in new[] { ("Rig10", "HeadMesh"), ("Handle1", "Hair") })
            {
                var part = new GameObject(label);
                part.transform.SetParent(bones["Head"], false);
                var (_, color, _) = Colors(group, spec, sleeveLeft, sleeveRight, shirtHem);
                part.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(Offset(shapes[group], -joints["Head"]), $"{MeshFolder}/{spec.meshPrefix}_{group}.asset");
                part.AddComponent<MeshRenderer>().sharedMaterial = ColorMaterial(color);
                if (group == "Handle1") part.SetActive(spec.hair != null);
            }

            // The logo wrapped onto the chest of the T-shirt, the face onto the front of the head
            if (spec.logo)
            {
                var torso = groups["Rig15"];
                var logoMesh = RigUtility.FaceGrid(FrontTriangles(torso), torso.bounds.min.y + torso.bounds.size.y * LogoHeight,
                    torso.bounds.size.x * LogoWidth, 0.003f);
                var logo = Decal(bones["Spine"], "Logo", Offset(logoMesh, -joints["Spine"]), $"{spec.meshPrefix}_Logo", LogoMaterial());
                logo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var head = groups["Rig10"];
            var faceMesh = RigUtility.FaceGrid(FrontTriangles(head), head.bounds.center.y, head.bounds.size.x * 0.84f, 0.003f);
            var face = Decal(bones["Head"], "Face", Offset(faceMesh, -joints["Head"]), $"{spec.meshPrefix}_Face", RobloxDecalAtlasBuilder.FaceMaterial());
            face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            face.AddComponent<RobloxFace>().emotion = (int)RobloxFace.Emotion.Smile;
            face.SetActive(spec.face);

            // A generic Animator, no humanoid avatar: clips key the bones' rotations straight (see
            // BaconAnimationTestBuilder.BakeClip). Muscle space would spread each limb's twist over its bones, turning
            // the blocky thighs and upper arms 15-35 degrees about themselves, and a humanoid Animator ignores bone keys.
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(root, spec.path);
            Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ObjBacon] Built " + string.Join(", ", Specs.Select(s => s.path)) + ": seams at "
            + string.Join(", ", Seams.Where(s => !s.bone.StartsWith("Right")).Select(s => $"{s.bone} {seamY[s.bone]:F3}")));
    }

    // A part's colour below its hem and above it (the same when it has none), for a spec
    static (float hem, string below, string above) Colors(string group, Spec s, float sleeveLeft, float sleeveRight, float shirtHem)
    {
        string skin = s.skin, shirt = s.shirt ?? skin, pants = s.pants ?? skin;
        switch (group)
        {
            case "Handle1": return (0f, s.hair ?? skin, s.hair ?? skin);
            case "Rig15": return (0f, shirt, shirt);
            case "Rig14": return (shirtHem, pants, shirt);
            case "Rig9": return (sleeveLeft, skin, shirt);
            case "Rig13": return (sleeveRight, skin, shirt);
            case "Rig6": case "Rig5": case "Rig3": case "Rig2": return (0f, pants, pants);
            case "Rig4": case "Rig1": return (0f, s.shoes ?? skin, s.shoes ?? skin);
            default: return (0f, skin, skin);
        }
    }

    // ---------------------------------------------------------------- seams

    // Where two parts that run into each other meet: the height in their overlap at which their outlines match best,
    // and the middle of the outline there (x, z)
    static (float y, Vector2 middle) Seam(Mesh upper, Mesh lower)
    {
        float from = upper.bounds.min.y, to = lower.bounds.max.y;
        float best = float.MaxValue;
        var seam = ((from + to) / 2f, new Vector2(lower.bounds.center.x, lower.bounds.center.z));
        for (float y = from + 0.005f; y < to - 0.004f; y += 0.0025f)
        {
            var a = Outline(upper, y);
            var b = Outline(lower, y);
            if (a.Count < 6 || b.Count < 6) continue;
            var middle = Vector2.zero;
            foreach (var p in a) middle += p;
            foreach (var p in b) middle += p;
            middle /= a.Count + b.Count;
            float[] ra = Radii(a, middle), rb = Radii(b, middle);
            float sum = 0f;
            int n = 0;
            for (int k = 0; k < ra.Length; k++)
                if (ra[k] >= 0f && rb[k] >= 0f) { sum += Mathf.Abs(ra[k] - rb[k]); n++; }
            if (n < 6 || sum / n >= best) continue;
            best = sum / n;
            seam = (y, middle);
        }
        return seam;
    }

    // The upper arm from `bottom` (its cut at the elbow, open there for the stitch) to `top`: the arm's outline at the
    // cut, resampled evenly round, carried straight up and leaning in to reach SleeveTuck into the torso's side
    // (`torsoSide`, its x) at the top, rounded over the last SleeveRound to a flat crown SleeveCrown of its size
    static Mesh Sleeve(Mesh arm, float bottom, float top, float torsoSide)
    {
        var cut = Clip(arm, bottom, keepAbove: true);
        var outline = Outline(arm, bottom);
        var middle = Vector2.zero;
        foreach (var p in outline) middle += p;
        middle /= outline.Count;
        var loop = BoundaryLoop(cut, bottom, middle).ConvertAll(i => new Vector2(cut.vertices[i].x, cut.vertices[i].z));

        // Evenly round by length
        var lengths = new float[loop.Count + 1];
        for (int k = 0; k < loop.Count; k++) lengths[k + 1] = lengths[k] + Vector2.Distance(loop[k], loop[(k + 1) % loop.Count]);
        var ring = new List<Vector2>();
        for (int k = 0, at = 0; k < SleeveSides; k++)
        {
            float l = lengths[loop.Count] * k / SleeveSides;
            while (lengths[at + 1] < l) at++;
            ring.Add(Vector2.Lerp(loop[at], loop[(at + 1) % loop.Count], (l - lengths[at]) / (lengths[at + 1] - lengths[at])));
        }
        var centre = Vector2.zero;
        foreach (var p in ring) centre += p;
        centre /= ring.Count;
        float inner = torsoSide < 0f ? ring.Max(p => p.x) : ring.Min(p => p.x);
        float lean = torsoSide - Mathf.Sign(torsoSide) * SleeveTuck - inner;

        // Rings up the straight part, then round the top
        var rings = new List<(float y, float scale)>();
        for (float y = bottom; y < top - SleeveRound - 0.001f; y += SleeveStep) rings.Add((y, 1f));
        for (int k = 0; k <= 6; k++)
        {
            float a = k * 15f * Mathf.Deg2Rad;
            rings.Add((top - SleeveRound + SleeveRound * Mathf.Sin(a), 1f - (1f - SleeveCrown) * (1f - Mathf.Cos(a))));
        }
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var (y, scale) in rings)
        {
            float shift = lean * (y - bottom) / (top - bottom);
            foreach (var p in ring)
            {
                var q = centre + (p - centre) * scale;
                vertices.Add(new Vector3(q.x + shift, y, q.y));
            }
        }
        int n = SleeveSides;
        for (int r = 0; r + 1 < rings.Count; r++)
            for (int k = 0; k < n; k++)
            {
                int a = r * n + k, b = r * n + (k + 1) % n, c = a + n, d = b + n;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        int apex = vertices.Count;
        vertices.Add(new Vector3(centre.x + lean, top, centre.y));
        for (int k = 0; k < n; k++) triangles.AddRange(new[] { apex - n + k, apex, apex - n + (k + 1) % n });
        // Facing out
        var t0 = vertices[triangles[1]] - vertices[triangles[0]];
        var t1 = vertices[triangles[2]] - vertices[triangles[0]];
        var out0 = vertices[triangles[0]] - new Vector3(centre.x, vertices[triangles[0]].y, centre.y);
        if (Vector3.Dot(Vector3.Cross(t0, t1), out0) < 0f)
            for (int k = 0; k < triangles.Count; k += 3) (triangles[k + 1], triangles[k + 2]) = (triangles[k + 2], triangles[k + 1]);
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Where the mesh's edges cross the plane at height y, as (x, z)
    static List<Vector2> Outline(Mesh mesh, float y)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        var points = new List<Vector2>();
        for (int i = 0; i < t.Length; i += 3)
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = v[t[i + k]], b = v[t[i + (k + 1) % 3]];
                if ((a.y - y) * (b.y - y) >= 0f) continue;
                var p = Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y));
                points.Add(new Vector2(p.x, p.z));
            }
        return points;
    }

    // The outline's reach from `middle` in each of 24 directions (-1 where it has no point)
    static float[] Radii(List<Vector2> points, Vector2 middle)
    {
        var r = Enumerable.Repeat(-1f, 24).ToArray();
        foreach (var p in points)
        {
            var d = p - middle;
            int k = Mathf.Clamp((int)((Mathf.Atan2(d.y, d.x) + Mathf.PI) / (2f * Mathf.PI) * 24f), 0, 23);
            r[k] = Mathf.Max(r[k], d.magnitude);
        }
        return r;
    }

    // The part of a mesh on one side of the plane at height y (keepAbove), triangles crossing it cut along it; the opening
    // left on the plane is bridged to the next part's (Bridge). With keepAbove null both sides are kept: an edge loop.
    // `axis` 0 or 2 cuts across x or z instead of y.
    // A point on the cut comes from the two corners' positions alone, so copies of a corner split along a UV or normal
    // seam land on the same point.
    static Mesh Clip(Mesh source, float y, bool? keepAbove, int axis = 1)
    {
        var v = source.vertices;
        var n = source.normals;
        var t = source.triangles;
        // A corner just off the plane goes onto it: cut there, it would leave a sliver of a triangle
        for (int i = 0; i < v.Length; i++)
            if (Mathf.Abs(v[i][axis] - y) < ClipSnap) v[i][axis] = y;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        var kept = new Dictionary<int, int>();
        int Keep(int i)
        {
            if (kept.TryGetValue(i, out int k)) return k;
            kept[i] = k = vertices.Count;
            vertices.Add(v[i]);
            normals.Add(n[i]);
            return k;
        }
        var cuts = new Dictionary<(int, int), int>();
        int Cut(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (cuts.TryGetValue(key, out int k)) return k;
            int lo = v[a][axis] <= v[b][axis] ? a : b, hi = lo == a ? b : a;
            if (v[lo][axis] == y) return cuts[key] = Keep(lo);
            float f = (y - v[lo][axis]) / (v[hi][axis] - v[lo][axis]);
            var p = v[lo] + (v[hi] - v[lo]) * f;
            p[axis] = y;
            cuts[key] = k = vertices.Count;
            vertices.Add(p);
            normals.Add(Vector3.Lerp(n[lo], n[hi], f).normalized);
            return k;
        }
        for (int i = 0; i < t.Length; i += 3)
        {
            int[] c = { t[i], t[i + 1], t[i + 2] };
            bool[] up = { v[c[0]][axis] > y, v[c[1]][axis] > y, v[c[2]][axis] > y };
            if (up[0] == up[1] && up[1] == up[2])
            {
                if (keepAbove == null || up[0] == keepAbove) triangles.AddRange(new[] { Keep(c[0]), Keep(c[1]), Keep(c[2]) });
                continue;
            }
            // The corner alone on its side, then the other two in winding order
            int lone = up[0] != up[1] && up[0] != up[2] ? 0 : up[1] != up[0] ? 1 : 2;
            int p0 = c[lone], q = c[(lone + 1) % 3], r = c[(lone + 2) % 3];
            int pq = Cut(p0, q), pr = Cut(p0, r);
            if (keepAbove == null || up[lone] == keepAbove) Add(Keep(p0), pq, pr);
            if (keepAbove == null || up[lone] != keepAbove)
            {
                int kq = Keep(q), kr = Keep(r);
                Add(pq, kq, kr);
                Add(pq, kr, pr);
            }
        }
        void Add(int a, int b, int c)
        {
            if (a != b && b != c && c != a) triangles.AddRange(new[] { a, b, c });
        }
        var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- soft joints

    // The soft joints of a part on `bone`: (the bone across the joint, the joint, the way from it into this part)
    static List<(string bone, Vector3 joint, Vector3 into)> Across(string bone, Dictionary<string, Vector3> joints, Vector3 partCenter)
    {
        var across = new List<(string, Vector3, Vector3)>();
        Chain.TryGetValue(bone, out var below);
        if (SoftJoints.Contains(bone))
            across.Add((Parents[bone], joints[bone], ((below != null ? joints[below] : partCenter) - joints[bone]).normalized));
        if (below != null && SoftJoints.Contains(below))
            across.Add((below, joints[below], (joints[bone] - joints[below]).normalized));
        return across;
    }

    // Bone weights for a limb's vertices (bone order as in `names`): all on the vertex's own part's bone, except within
    // JointBand of a soft joint, where it hands over to the bone across it: half at the joint, none JointBand inside the
    // part, all JointBand beyond. Measured straight up or down from the joint (the limbs stand upright), so a seam is
    // half and half all round.
    static BoneWeight[] LimbWeights(Limb limb, Dictionary<string, Vector3> joints, Dictionary<string, Mesh> groups, List<string> names)
    {
        var across = new Dictionary<string, List<(string bone, Vector3 joint, Vector3 into)>>();
        var weights = new BoneWeight[limb.vertices.Count];
        var share = new Dictionary<string, float>();
        for (int i = 0; i < weights.Length; i++)
        {
            string group = limb.groups[i], own = BoneOf(group);
            if (!across.TryGetValue(group, out var list)) across[group] = list = Across(own, joints, groups[group].bounds.center);
            var p = limb.vertices[i];
            share.Clear();
            float handed = 0f;
            foreach (var (other, joint, into) in list)
            {
                float x = Mathf.Clamp((p.y - joint.y) * Mathf.Sign(into.y) / JointBand, -1f, 1f);
                float h = Mathf.SmoothStep(0f, 1f, (1f - x) / 2f);
                if (h > 0f) share[other] = (share.TryGetValue(other, out float w) ? w : 0f) + h;
                handed += h;
            }
            share[own] = Mathf.Max(0f, 1f - handed);
            weights[i] = RigUtility.Pack(share, names);
        }
        return weights;
    }

    // ---------------------------------------------------------------- stitching

    // The torso and each limb, as their parts top to bottom, stitched into one mesh
    static readonly (string name, string[] parts)[] Limbs =
    {
        ("TorsoMesh", new[] { "Rig15", "Rig14" }),
        ("LeftArmMesh", new[] { "Rig9", "Rig8", "Rig7" }),
        ("RightArmMesh", new[] { "Rig13", "Rig12", "Rig11" }),
        ("LeftLegMesh", new[] { "Rig6", "Rig5", "Rig4" }),
        ("RightLegMesh", new[] { "Rig3", "Rig2", "Rig1" }),
    };
    // Each part is cut this far off its seam and the gap bridged; a bit of a part standing out of its neighbour's outline
    // by more than IslandReach is not cut off
    const float SeamGap = 0.015f, IslandReach = 0.005f;
    const float ClipSnap = 0.0008f; // a corner this close to a cutting plane is moved onto it
    // The corners this close in height to a seam's ring take their normals from their faces, where theirs tip further off
    const float RingStrip = 0.004f, RingTilt = 30f;

    class Limb
    {
        public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        public readonly List<string> groups = new List<string>(); // per vertex: its part
        public readonly List<int> triangles = new List<int>();
        public readonly List<string> parts = new List<string>(); // per triangle
        public readonly HashSet<int> rings = new HashSet<int>(); // the vertices round the bridged seams

        public void Add(int a, int b, int c, string part)
        {
            triangles.AddRange(new[] { a, b, c });
            parts.Add(part);
        }
    }

    static string BoneOf(string group) => Parts.First(p => p.group == group).bone;

    // The top of whatever of `lower` the cut SeamGap under the seam would take off while it stands out of `upper`'s
    // outline at the seam by more than IslandReach; -infinity if nothing does
    static float IslandTop(Mesh lower, Mesh upper, float y, Vector2 middle)
    {
        var radii = Radii(Outline(upper, y), middle);
        float top = float.NegativeInfinity;
        foreach (var p in lower.vertices)
        {
            if (p.y <= y - SeamGap) continue;
            var d = new Vector2(p.x, p.z) - middle;
            int k = Mathf.Clamp((int)((Mathf.Atan2(d.y, d.x) + Mathf.PI) / (2f * Mathf.PI) * 24f), 0, 23);
            if (radii[k] >= 0f && d.magnitude > radii[k] + IslandReach) top = Mathf.Max(top, p.y);
        }
        return top;
    }

    // The vertices round a part's open edge on the plane at height y (one per point, the piece going furthest round
    // `middle`), in order of angle about it
    static List<int> BoundaryLoop(Mesh mesh, float y, Vector2 middle)
    {
        var v = mesh.vertices;
        var t = mesh.triangles;
        var welded = new Dictionary<Vector3, int>();
        int W(int i)
        {
            if (!welded.TryGetValue(v[i], out int r)) welded[v[i]] = r = i;
            return r;
        }
        var uses = new Dictionary<(int, int), int>();
        for (int k = 0; k < t.Length; k += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = W(t[k + e]), b = W(t[k + (e + 1) % 3]);
                var key = a < b ? (a, b) : (b, a);
                uses[key] = uses.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        var parent = new Dictionary<int, int>();
        int Root(int i)
        {
            while (parent[i] != i) i = parent[i];
            return i;
        }
        foreach (var kv in uses)
        {
            var (a, b) = kv.Key;
            if (kv.Value != 1 || Mathf.Abs(v[a].y - y) > 1e-5f || Mathf.Abs(v[b].y - y) > 1e-5f) continue;
            if (!parent.ContainsKey(a)) parent[a] = a;
            if (!parent.ContainsKey(b)) parent[b] = b;
            int ra = Root(a), rb = Root(b);
            if (ra != rb) parent[ra] = rb;
        }
        float Angle(int i) => Mathf.Atan2(v[i].z - middle.y, v[i].x - middle.x);
        var around = new Dictionary<int, HashSet<int>>();
        foreach (int i in parent.Keys.ToList())
        {
            int root = Root(i);
            if (!around.TryGetValue(root, out var steps)) around[root] = steps = new HashSet<int>();
            steps.Add(Mathf.FloorToInt((Angle(i) + Mathf.PI) / (2f * Mathf.PI) * 36f));
        }
        if (around.Count == 0) throw new ArgumentException($"No open edge at {y:F3} in {mesh.name}");
        int main = around.OrderByDescending(kv => kv.Value.Count).First().Key;
        var loop = parent.Keys.Where(i => Root(i) == main).ToList();
        loop.Sort((p, q) => Angle(p).CompareTo(Angle(q)));
        return loop;
    }

    // Triangles zipped round between two loops (each in order of angle about `middle`), always stepping along the loop
    // whose next point comes sooner round, each facing outwards
    static void Bridge(Limb limb, List<int> top, List<int> bottom, Vector2 middle, string part)
    {
        float Angle(List<int> loop, int k) =>
            Mathf.Atan2(limb.vertices[loop[k % loop.Count]].z - middle.y, limb.vertices[loop[k % loop.Count]].x - middle.x)
            + 2f * Mathf.PI * (k / loop.Count);
        // A loop has one vertex per point, but on a hard edge of the R15 part (along its bevels) two share the point, each
        // with its own side's normal: a corner of the bridge takes the one facing the bridge's way (with the loop's own
        // one, a flat face of the bridge was shaded with the bevel's slant, a pale band round the seam)
        Vector3Int Point(int v) => Vector3Int.RoundToInt(limb.vertices[v] * 2000f); // to half a millimetre
        var twins = new Dictionary<Vector3Int, List<int>>();
        foreach (var loop in new[] { top, bottom })
        {
            float y = limb.vertices[loop[0]].y;
            string group = limb.groups[loop[0]];
            for (int v = 0; v < limb.vertices.Count; v++)
            {
                if (limb.vertices[v].y != y || limb.groups[v] != group) continue;
                if (!twins.TryGetValue(Point(v), out var list)) twins[Point(v)] = list = new List<int>();
                list.Add(v);
                limb.rings.Add(v);
            }
        }
        // Twins a hair apart (their part cut along edges a fraction off each other) are put on one point: a bridge
        // corner moved to the other twin left a slit
        foreach (var list in twins.Values)
            foreach (int v in list) limb.vertices[v] = limb.vertices[list[0]];
        int Facing(int v, Vector3 normal) =>
            twins.TryGetValue(Point(v), out var list) ? list.OrderByDescending(k => Vector3.Dot(limb.normals[k], normal)).First() : v;
        void Face(int a, int b, int c)
        {
            Vector3 pa = limb.vertices[a], pb = limb.vertices[b], pc = limb.vertices[c];
            var centre = (pa + pb + pc) / 3f;
            var outward = new Vector3(centre.x - middle.x, 0f, centre.z - middle.y);
            var normal = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(normal, outward) < 0f) { (b, c) = (c, b); normal = -normal; }
            limb.Add(Facing(a, normal), Facing(b, normal), Facing(c, normal), part);
        }
        int i = 0, j = 0;
        while (i < top.Count || j < bottom.Count)
        {
            bool alongTop = j >= bottom.Count || (i < top.Count && Angle(top, i + 1) <= Angle(bottom, j + 1));
            if (alongTop) { Face(top[i % top.Count], bottom[j % bottom.Count], top[(i + 1) % top.Count]); i++; }
            else { Face(top[i % top.Count], bottom[j % bottom.Count], bottom[(j + 1) % bottom.Count]); j++; }
        }
    }

    // The R15 foot has slits where its toe meets the block: a corner of one lies along an edge of the other (a
    // T-junction), and the background showed through in a dotted line over the shoes. Each such edge is split there.
    static Mesh CloseCracks(Mesh source)
    {
        var v = new List<Vector3>(source.vertices);
        var n = new List<Vector3>(source.normals);
        var t = new List<int>(source.triangles);
        Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 10000f); // to a tenth of a millimetre
        for (int pass = 0; pass < 16; pass++)
        {
            // The open edges (used by one triangle, corners matched by position) and the points round them
            var uses = new Dictionary<(Vector3Int, Vector3Int), int>();
            (Vector3Int, Vector3Int) Edge(int a, int b)
            {
                Vector3Int ka = Key(v[a]), kb = Key(v[b]);
                return ka.x < kb.x || ka.x == kb.x && (ka.y < kb.y || ka.y == kb.y && ka.z < kb.z) ? (ka, kb) : (kb, ka);
            }
            for (int k = 0; k < t.Count; k += 3)
                for (int e = 0; e < 3; e++)
                {
                    var key = Edge(t[k + e], t[k + (e + 1) % 3]);
                    uses[key] = uses.TryGetValue(key, out int u) ? u + 1 : 1;
                }
            var open = new List<(int k, int e)>();
            var points = new Dictionary<Vector3Int, Vector3>();
            for (int k = 0; k < t.Count; k += 3)
                for (int e = 0; e < 3; e++)
                    if (uses[Edge(t[k + e], t[k + (e + 1) % 3])] == 1)
                    {
                        open.Add((k, e));
                        points[Key(v[t[k + e]])] = v[t[k + e]];
                        points[Key(v[t[k + (e + 1) % 3]])] = v[t[k + (e + 1) % 3]];
                    }
            // Split each open edge (one per triangle a pass) at a point lying along it
            var done = new HashSet<int>();
            foreach (var (k, e) in open)
            {
                if (done.Contains(k)) continue;
                int a = t[k + e], b = t[k + (e + 1) % 3], c = t[k + (e + 2) % 3];
                var ab = v[b] - v[a];
                foreach (var p in points.Values)
                {
                    float f = Vector3.Dot(p - v[a], ab) / ab.sqrMagnitude;
                    if (f < 0.001f || f > 0.999f || (v[a] + ab * f - p).sqrMagnitude > 1e-8f) continue;
                    int m = v.Count;
                    v.Add(p);
                    n.Add(Vector3.Lerp(n[a], n[b], f).normalized);
                    t[k] = a; t[k + 1] = m; t[k + 2] = c;
                    t.AddRange(new[] { m, b, c });
                    done.Add(k);
                    break;
                }
            }
            if (done.Count == 0) break;
        }
        var mesh = new Mesh { indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // The seam rings' normals from their faces: a cut through a long R15 triangle gave its vertex a normal partway to the
    // far corner's (on a bevel, tipped up or down), and the bridge shaded as a pale band round the joint
    static void RingNormals(Limb limb)
    {
        // The ring and the corners next to it within RingStrip of its height (a bevel the cut only just missed)
        var strip = new HashSet<int>(limb.rings);
        for (int k = 0; k < limb.triangles.Count; k += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = limb.triangles[k + e], b = limb.triangles[k + (e + 1) % 3];
                if (limb.rings.Contains(a) && Mathf.Abs(limb.vertices[b].y - limb.vertices[a].y) < RingStrip) strip.Add(b);
                if (limb.rings.Contains(b) && Mathf.Abs(limb.vertices[a].y - limb.vertices[b].y) < RingStrip) strip.Add(a);
            }
        var sums = strip.ToDictionary(v => v, v => Vector3.zero);
        for (int k = 0; k < limb.triangles.Count; k += 3)
        {
            int a = limb.triangles[k], b = limb.triangles[k + 1], c = limb.triangles[k + 2];
            var n = Vector3.Cross(limb.vertices[b] - limb.vertices[a], limb.vertices[c] - limb.vertices[a]); // by area
            foreach (int v in new[] { a, b, c })
                if (sums.ContainsKey(v)) sums[v] += n;
        }
        // Only where the two disagree: on a smooth part (an arm, a hand) the R15 normals are the better ones
        foreach (var kv in sums)
            if (kv.Value.sqrMagnitude > 1e-12f && Vector3.Angle(limb.normals[kv.Key], kv.Value) > RingTilt)
                limb.normals[kv.Key] = kv.Value.normalized;
    }

    // ---------------------------------------------------------------- the OBJ

    // One mesh per OBJ group, in metres, standing on the origin facing +Z
    internal static Dictionary<string, Mesh> ReadObj(string path)
    {
        var v = new List<Vector3>();
        var vt = new List<Vector2>();
        var vn = new List<Vector3>();
        var faces = new Dictionary<string, List<string[]>>();
        string current = "default";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v": v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                case "vt": vt.Add(new Vector2(F(p[1]), F(p[2]))); break;
                case "vn": vn.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                case "g": case "o": current = p.Length > 1 ? p[1] : "default"; break;
                case "f":
                    if (!faces.ContainsKey(current)) faces[current] = new List<string[]>();
                    var corners = new string[p.Length - 1];
                    Array.Copy(p, 1, corners, 0, corners.Length);
                    faces[current].Add(corners);
                    break;
            }
        }

        var center = ObjCenter(v);
        Vector3 Point(Vector3 p) => ToUnity(p, center);

        var meshes = new Dictionary<string, Mesh>();
        foreach (var kv in faces)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var index = new Dictionary<string, int>();
            int Corner(string token)
            {
                if (index.TryGetValue(token, out int i)) return i;
                var s = token.Split('/');
                i = vertices.Count;
                vertices.Add(Point(v[int.Parse(s[0]) - 1]));
                uvs.Add(s.Length > 1 && s[1].Length > 0 ? vt[int.Parse(s[1]) - 1] : Vector2.zero);
                var n = s.Length > 2 && s[2].Length > 0 ? vn[int.Parse(s[2]) - 1] : Vector3.up;
                normals.Add(new Vector3(n.x, n.y, -n.z));
                index[token] = i;
                return i;
            }
            foreach (var f in kv.Value)
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    // The flip reverses handedness, so each triangle's winding is reversed too
                    int a = Corner(f[0]), b = Corner(f[k]), c = Corner(f[k + 1]);
                    triangles.AddRange(new[] { a, c, b });
                }
            var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshes[kv.Key] = mesh;
        }
        return meshes;
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    // Where the OBJ stands: centred across and front to back, on its lowest point
    internal static Vector3 ObjCenter(string path)
    {
        var v = new List<Vector3>();
        foreach (var raw in File.ReadLines(path))
        {
            var p = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 3 && p[0] == "v") v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
        }
        return ObjCenter(v);
    }

    static Vector3 ObjCenter(List<Vector3> v)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return new Vector3((min.x + max.x) / 2f, min.y, (min.z + max.z) / 2f);
    }

    // An OBJ point (studs, right-handed, Y up, facing -Z) in Unity metres, standing on the origin facing +Z: X mirrored
    // (right- to left-handed) and then turned half round, which comes to flipping Z
    internal static Vector3 ToUnity(Vector3 p, Vector3 center) { p -= center; return new Vector3(p.x, p.y, -p.z) * StudToMetre; }

    // ---------------------------------------------------------------- helpers

    // The triangles facing +Z (front), as corner triples
    internal static List<Vector3> FrontTriangles(Mesh mesh)
    {
        var front = new List<Vector3>();
        var v = mesh.vertices;
        var t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            if (Vector3.Cross(b - a, c - a).z > 0f) front.AddRange(new[] { a, b, c }); // the cross product is the face normal
        }
        return front;
    }

    internal static Mesh Offset(Mesh source, Vector3 by)
    {
        var mesh = Object.Instantiate(source);
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] += by;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject Decal(Transform bone, string name, Mesh mesh, string asset, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(bone, false);
        go.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(mesh, $"{MeshFolder}/{asset}.asset");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    internal static Material ColorMaterial(string hex)
    {
        if (materials.TryGetValue(hex, out var material) && material != null) return material;
        string path = $"{MaterialFolder}/ObjBacon_{hex}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", RigUtility.Hex(hex));
        material.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(material);
        materials[hex] = material;
        return material;
    }

    // The logo, cut out along its alpha
    static Material LogoMaterial()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(LogoPath);
        if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        string path = $"{MaterialFolder}/ObjBacon_Logo.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.25f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(material);
        return material;
    }
}

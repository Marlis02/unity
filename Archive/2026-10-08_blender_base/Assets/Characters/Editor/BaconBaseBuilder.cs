using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// The bare base body of the main character: the R15 bacon from Roblox Studio with its body parts fused into one
// smooth skinned surface, so no hidden part end shows at a bend. Imports/Blender/bacon_base.py fuses them (voxel
// remesh, in an A-pose with the arms ArmOut degrees out from hanging so they don't fuse to the torso) and weights them
// by bone heat into Imports/bacon_base.json; this builds the prefab: the skeleton (named like main_hero's, no
// clavicles, bones unrotated at rest), the body as one SkinnedMeshRenderer, and the head as its own rigid part on the
// Head bone, Roblox-style. The hair and the face are there, switched off: the base is bare, grey all over.
// Tools/Characters/Build Bacon Base -> Assets/Characters/ObjBacon/Prefabs/bacon_base.prefab
public static class BaconBaseBuilder
{
    const string Source = "Imports/bacon.obj";             // the head and the hair, rigid, as they came
    const string BaseSource = "Imports/bacon_base.json";   // the fused body, from Imports/Blender/bacon_base.py
    const string Folder = ObjBaconBuilder.Folder;
    public const string PrefabPath = Folder + "/Prefabs/bacon_base.prefab";
    const string MeshFolder = Folder + "/Meshes";
    const string Skin = "C7C7C7";

    // Imports/bacon_base.json
    [Serializable]
    class BaseData
    {
        public float armOut;
        public string[] bones;      // the skeleton, parents first
        public float[] joints;      // each bone's joint, OBJ space
        public string[] skinBones;  // the bones the body is weighted to
        public float[] v, n;        // OBJ space
        public int[] t;
        public float[] weights;     // four per vertex
        public int[] weightBones;   // into skinBones, four per vertex
    }

    static readonly Dictionary<string, string> Parents = new Dictionary<string, string>
    {
        ["Hips"] = null, ["Spine"] = "Hips", ["Head"] = "Spine",
        ["LeftUpperArm"] = "Spine", ["LeftLowerArm"] = "LeftUpperArm", ["LeftHand"] = "LeftLowerArm",
        ["RightUpperArm"] = "Spine", ["RightLowerArm"] = "RightUpperArm", ["RightHand"] = "RightLowerArm",
        ["LeftUpperLeg"] = "Hips", ["LeftLowerLeg"] = "LeftUpperLeg", ["LeftFoot"] = "LeftLowerLeg",
        ["RightUpperLeg"] = "Hips", ["RightLowerLeg"] = "RightUpperLeg", ["RightFoot"] = "RightLowerLeg",
    };

    [MenuItem("Tools/Characters/Build Bacon Base")]
    public static void Build()
    {
        foreach (var folder in new[] { Folder + "/Prefabs", MeshFolder }) RigUtility.EnsureFolder(folder);
        if (!File.Exists(BaseSource)) { Debug.LogError($"[BaconBase] {BaseSource} missing: run blender -b -P Imports/Blender/bacon_base.py"); return; }
        var data = JsonUtility.FromJson<BaseData>(File.ReadAllText(BaseSource));
        var center = ObjBaconBuilder.ObjCenter(Source);
        var groups = ObjBaconBuilder.ReadObj(Source);

        // The skeleton, unrotated, each bone at its joint
        var root = new GameObject("bacon_base");
        var bones = new Dictionary<string, Transform>();
        var joints = new Dictionary<string, Vector3>();
        for (int b = 0; b < data.bones.Length; b++)
        {
            string name = data.bones[b];
            joints[name] = ObjBaconBuilder.ToUnity(new Vector3(data.joints[3 * b], data.joints[3 * b + 1], data.joints[3 * b + 2]), center);
            var t = new GameObject(name).transform;
            t.SetParent(Parents[name] == null ? root.transform : bones[Parents[name]], false);
            t.position = joints[name];
            bones[name] = t;
        }

        // The body: one skinned mesh on the character's origin
        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        var skin = body.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = RigUtility.SaveMesh(BodyMesh(data, center, bones), $"{MeshFolder}/BaconBase_Body.asset");
        skin.bones = Array.ConvertAll(data.skinBones, n => bones[n]);
        skin.rootBone = bones["Hips"];
        skin.sharedMaterial = ObjBaconBuilder.ColorMaterial(Skin);
        skin.updateWhenOffscreen = true; // the bounds follow the pose (arms up, jumps)

        // The head and the hair rigid on the Head bone, the face wrapped onto the head's front
        var head = Rigid(bones["Head"], "HeadMesh", ObjBaconBuilder.Offset(groups["Rig10"], -joints["Head"]), "BaconBase_Head");
        head.GetComponent<MeshRenderer>().sharedMaterial = ObjBaconBuilder.ColorMaterial(Skin);
        var hair = Rigid(bones["Head"], "Hair", ObjBaconBuilder.Offset(groups["Handle1"], -joints["Head"]), "BaconBase_Hair");
        hair.GetComponent<MeshRenderer>().sharedMaterial = ObjBaconBuilder.ColorMaterial(Skin);
        hair.SetActive(false);
        var headMesh = groups["Rig10"];
        var faceMesh = RigUtility.FaceGrid(ObjBaconBuilder.FrontTriangles(headMesh), headMesh.bounds.center.y, headMesh.bounds.size.x * 0.84f, 0.003f);
        var face = Rigid(bones["Head"], "Face", ObjBaconBuilder.Offset(faceMesh, -joints["Head"]), "BaconBase_Face");
        var faceRenderer = face.GetComponent<MeshRenderer>();
        faceRenderer.sharedMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        faceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)RobloxFace.Emotion.Smile;
        face.SetActive(false);

        var avatar = RigUtility.SaveAvatar(RigUtility.BuildAvatar(root, bones, ObjBaconBuilder.BoneNames), $"{Folder}/Prefabs/BaconBase_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BaconBase] Built {PrefabPath}: {data.v.Length / 3} vertices, arms {data.armOut} degrees out at rest");
    }

    // The fused body in Unity space (the OBJ's Z flipped, so the winding turns round too), bound to the bones as they
    // stand
    static Mesh BodyMesh(BaseData data, Vector3 center, Dictionary<string, Transform> bones)
    {
        int count = data.v.Length / 3;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var weights = new BoneWeight[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = ObjBaconBuilder.ToUnity(new Vector3(data.v[3 * i], data.v[3 * i + 1], data.v[3 * i + 2]), center);
            normals[i] = new Vector3(data.n[3 * i], data.n[3 * i + 1], -data.n[3 * i + 2]);
            weights[i] = new BoneWeight
            {
                boneIndex0 = data.weightBones[4 * i], weight0 = data.weights[4 * i],
                boneIndex1 = data.weightBones[4 * i + 1], weight1 = data.weights[4 * i + 1],
                boneIndex2 = data.weightBones[4 * i + 2], weight2 = data.weights[4 * i + 2],
                boneIndex3 = data.weightBones[4 * i + 3], weight3 = data.weights[4 * i + 3],
            };
        }
        var triangles = new int[data.t.Length];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            triangles[i] = data.t[i];
            triangles[i + 1] = data.t[i + 2];
            triangles[i + 2] = data.t[i + 1];
        }
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.boneWeights = weights;
        mesh.bindposes = Array.ConvertAll(data.skinBones, n => bones[n].worldToLocalMatrix);
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject Rigid(Transform bone, string name, Mesh mesh, string asset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(bone, false);
        go.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(mesh, $"{MeshFolder}/{asset}.asset");
        go.AddComponent<MeshRenderer>();
        return go;
    }
}

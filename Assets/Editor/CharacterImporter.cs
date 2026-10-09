using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace CharacterPlayground.EditorTools
{
    /// <summary>
    /// Turns every folder under Assets/Characters into a playable prefab in
    /// Assets/Resources/Characters. A folder holds one character: a model (.fbx/.obj/.dae or a
    /// ready .prefab), optional animation files (Mixamo style: Idle.fbx, Walking.fbx, Running.fbx,
    /// Jump.fbx, Falling.fbx) and an optional character.json with stats.
    /// </summary>
    public static class CharacterImporter
    {
        public const string SourceRoot = "Assets/Characters";
        public const string OutputRoot = "Assets/Resources/Characters";
        const string GeneratedFolderName = "Generated";
        const string RigMarker = "playground-rig";
        const string ClipMarker = "playground-clips";

        static readonly string[] ModelExtensions = { ".fbx", ".obj", ".dae" };

        enum ClipKind { Other, Idle, Walk, Run, Jump, Fall }

        [Serializable]
        class CharacterConfig
        {
            public string displayName;
            public float walkSpeed = 3f;
            public float runSpeed = 6f;
            public float jumpHeight = 1.2f;
            public float gravity = 20f;
            [Tooltip("Scale the model to this height in metres; 0 keeps the imported size.")]
            public float height = 0f;
            [Tooltip("Extra rotation around Y if the model does not face +Z.")]
            public float yawOffset = 0f;
            [Tooltip("Model file inside the folder to use; picked automatically when empty.")]
            public string model = "";
            [Tooltip("Body parts of a model made of separate pieces, to animate it without a skeleton.")]
            public SegmentConfig segments = new SegmentConfig();
            [Tooltip("How far around each joint the skin bends, as a share of the joint's size; 0 turns parts like hinges.")]
            public float softness = 0.8f;
            [Tooltip("How many times to round the body parts' mesh (0 keeps the export's hard edges).")]
            public int smooth = 2;
            [Tooltip("Colours for parts of the model, e.g. when an .obj comes without its .mtl.")]
            public PartColor[] colors = new PartColor[0];
        }

        /// <summary>
        /// Object names (comma separated) of each body part, Roblox R15 style. Hands and feet move
        /// with the lower arm and leg; anything not listed moves with the body.
        /// </summary>
        [Serializable]
        class SegmentConfig
        {
            public string head = "";
            public string upperTorso = "";
            public string lowerTorso = "";
            public string leftUpperArm = "";
            public string leftLowerArm = "";
            public string leftHand = "";
            public string rightUpperArm = "";
            public string rightLowerArm = "";
            public string rightHand = "";
            public string leftUpperLeg = "";
            public string leftLowerLeg = "";
            public string leftFoot = "";
            public string rightUpperLeg = "";
            public string rightLowerLeg = "";
            public string rightFoot = "";

            public bool IsEmpty => string.IsNullOrWhiteSpace(head + upperTorso + lowerTorso
                + leftUpperArm + leftLowerArm + leftHand + rightUpperArm + rightLowerArm + rightHand
                + leftUpperLeg + leftLowerLeg + leftFoot + rightUpperLeg + rightLowerLeg + rightFoot);
        }

        [Serializable]
        class PartColor
        {
            public string parts = "";
            public string color = "#FFFFFF";
        }

        [MenuItem("Playground/Import Characters")]
        public static void ImportAll()
        {
            EnsureFolder(OutputRoot);
            var produced = new HashSet<string>();

            if (Directory.Exists(SourceRoot))
            {
                foreach (string directory in Directory.GetDirectories(SourceRoot).OrderBy(d => d, StringComparer.Ordinal))
                {
                    string folder = directory.Replace('\\', '/');
                    string folderName = Path.GetFileName(folder);
                    if (folderName.StartsWith(".") || folderName.StartsWith("_")) continue;
                    try
                    {
                        string prefabPath = ImportFolder(folder);
                        if (prefabPath != null) produced.Add(prefabPath);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"[Playground] Не удалось импортировать {folder}: {exception}");
                    }
                }
            }

            // Drop prefabs whose source folder disappeared.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { OutputRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!produced.Contains(path)) AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Playground] Импортировано персонажей: {produced.Count}");
        }

        static string ImportFolder(string folder)
        {
            string name = Path.GetFileName(folder);
            CharacterConfig config = LoadConfig(folder);
            if (string.IsNullOrEmpty(config.displayName)) config.displayName = name;

            List<string> files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/'))
                .Where(f => !f.Contains("/" + GeneratedFolderName + "/") && !f.EndsWith(".meta"))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            List<string> models = files.Where(f => ModelExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            List<string> prefabs = files.Where(f => f.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();

            string mainPath = !string.IsNullOrEmpty(config.model)
                ? folder + "/" + config.model
                : prefabs.FirstOrDefault() ?? PickMainModel(models);
            if (mainPath == null || AssetDatabase.LoadAssetAtPath<GameObject>(mainPath) == null)
            {
                Debug.LogWarning($"[Playground] В {folder} нет модели (.fbx/.obj/.dae/.prefab) — пропускаю.");
                return null;
            }

            Avatar avatar;
            bool humanoid;
            if (AssetImporter.GetAtPath(mainPath) is ModelImporter mainImporter)
            {
                avatar = ConfigureMainModel(mainImporter);
            }
            else
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(mainPath);
                var sourceAnimator = source.GetComponentInChildren<Animator>();
                avatar = sourceAnimator != null ? sourceAnimator.avatar : null;
            }
            humanoid = avatar != null && avatar.isValid && avatar.isHuman;

            var clips = new Dictionary<ClipKind, AnimationClip>();
            var others = new List<AnimationClip>();
            foreach (string path in models)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                if (path != mainPath && avatar != null) ConfigureAnimationModel(importer, avatar, humanoid);
                ConfigureClips(importer, humanoid);
                CollectClips(path, clips, others);
            }
            foreach (string path in files.Where(f => f.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)))
            {
                CollectClips(path, clips, others);
            }
            if (!clips.ContainsKey(ClipKind.Idle) && others.Count > 0) clips[ClipKind.Idle] = others[0];

            AnimatorController controller = clips.Count > 0 ? BuildController(folder, name, clips) : null;
            return BuildPrefab(folder, name, mainPath, config, avatar, controller);
        }

        static CharacterConfig LoadConfig(string folder)
        {
            string path = folder + "/character.json";
            if (!File.Exists(path)) return new CharacterConfig();
            try
            {
                return JsonUtility.FromJson<CharacterConfig>(File.ReadAllText(path)) ?? new CharacterConfig();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Playground] Ошибка в {path}: {exception.Message}");
                return new CharacterConfig();
            }
        }

        static string PickMainModel(List<string> models)
        {
            // Prefer a file that is not named like an animation and actually contains geometry.
            IEnumerable<string> withMeshes = models.Where(HasRenderers);
            return withMeshes.FirstOrDefault(m => Classify(Path.GetFileNameWithoutExtension(m)) == ClipKind.Other)
                ?? withMeshes.FirstOrDefault(m => Classify(Path.GetFileNameWithoutExtension(m)) == ClipKind.Idle)
                ?? withMeshes.FirstOrDefault();
        }

        static bool HasRenderers(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return model != null && model.GetComponentsInChildren<Renderer>(true).Length > 0;
        }

        static Avatar ConfigureMainModel(ModelImporter importer)
        {
            if (!HasMarker(importer, RigMarker))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                AddMarker(importer, RigMarker);
                importer.SaveAndReimport();

                Avatar human = LoadAvatar(importer.assetPath);
                if (human == null || !human.isValid || !human.isHuman)
                {
                    // Not a humanoid skeleton (creature, prop, static mesh): fall back to Generic.
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.SaveAndReimport();
                }
            }
            return LoadAvatar(importer.assetPath);
        }

        static void ConfigureAnimationModel(ModelImporter importer, Avatar sourceAvatar, bool humanoid)
        {
            if (HasMarker(importer, RigMarker)) return;
            importer.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = sourceAvatar;
            AddMarker(importer, RigMarker);
            importer.SaveAndReimport();
        }

        static void ConfigureClips(ModelImporter importer, bool humanoid)
        {
            if (HasMarker(importer, ClipMarker)) return;
            ModelImporterClipAnimation[] clipSettings = importer.clipAnimations;
            if (clipSettings == null || clipSettings.Length == 0) clipSettings = importer.defaultClipAnimations;
            string fileName = Path.GetFileNameWithoutExtension(importer.assetPath);

            foreach (ModelImporterClipAnimation clip in clipSettings)
            {
                ClipKind kind = Classify(clip.name + " " + fileName);
                if (kind == ClipKind.Idle || kind == ClipKind.Walk || kind == ClipKind.Run || kind == ClipKind.Fall)
                {
                    clip.loopTime = true;
                }
                if (humanoid)
                {
                    // In-place playback: physics moves the character, the clip only animates the body.
                    clip.lockRootRotation = true;
                    clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = true;
                }
            }

            if (clipSettings.Length > 0) importer.clipAnimations = clipSettings;
            AddMarker(importer, ClipMarker);
            importer.SaveAndReimport();
        }

        static void CollectClips(string path, Dictionary<ClipKind, AnimationClip> clips, List<AnimationClip> others)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            IEnumerable<AnimationClip> found = path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
                ? new[] { AssetDatabase.LoadAssetAtPath<AnimationClip>(path) }
                : AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>();

            foreach (AnimationClip clip in found)
            {
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                ClipKind kind = Classify(clip.name + " " + fileName);
                if (kind == ClipKind.Other) others.Add(clip);
                else if (!clips.ContainsKey(kind)) clips[kind] = clip;
            }
        }

        static ClipKind Classify(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("jump") || n.Contains("прыж")) return ClipKind.Jump;
            if (n.Contains("fall") || n.Contains("in air") || n.Contains("inair") || n.Contains("airborne") || n.Contains("пад")) return ClipKind.Fall;
            if (n.Contains("run") || n.Contains("sprint") || n.Contains("jog") || n.Contains("бег")) return ClipKind.Run;
            if (n.Contains("walk") || n.Contains("ходьб") || n.Contains("шаг")) return ClipKind.Walk;
            if (n.Contains("idle") || n.Contains("stand") || n.Contains("breath") || n.Contains("стой") || n.Contains("покой")) return ClipKind.Idle;
            return ClipKind.Other;
        }

        static AnimatorController BuildController(string folder, string name, Dictionary<ClipKind, AnimationClip> clips)
        {
            string generated = folder + "/" + GeneratedFolderName;
            EnsureFolder(generated);
            string path = generated + "/" + name + ".controller";
            AssetDatabase.DeleteAsset(path);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            AnimatorControllerParameter[] parameters = controller.parameters;
            foreach (AnimatorControllerParameter parameter in parameters)
            {
                if (parameter.name == "Grounded") parameter.defaultBool = true;
            }
            controller.parameters = parameters;

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // Locomotion: Speed 0 = idle, 1 = walk, 2 = run.
            var locomotion = new List<(AnimationClip clip, float threshold)>();
            if (clips.TryGetValue(ClipKind.Idle, out AnimationClip idle)) locomotion.Add((idle, 0f));
            if (clips.TryGetValue(ClipKind.Walk, out AnimationClip walk)) locomotion.Add((walk, 1f));
            if (clips.TryGetValue(ClipKind.Run, out AnimationClip run)) locomotion.Add((run, 2f));

            AnimatorState ground;
            if (locomotion.Count >= 2)
            {
                ground = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = "Speed";
                tree.useAutomaticThresholds = false;
                foreach (var (clip, threshold) in locomotion) tree.AddChild(clip, threshold);
            }
            else
            {
                ground = machine.AddState("Locomotion");
                ground.motion = locomotion.Count == 1 ? locomotion[0].clip : null;
            }
            machine.defaultState = ground;

            clips.TryGetValue(ClipKind.Jump, out AnimationClip jump);
            clips.TryGetValue(ClipKind.Fall, out AnimationClip fall);
            AnimationClip airClip = fall != null ? fall : jump;
            if (airClip == null) return controller;

            AnimatorState air = machine.AddState("Airborne");
            air.motion = airClip;
            AddTransition(ground, air, "Grounded", false, 0.15f);
            AddTransition(air, ground, "Grounded", true, 0.1f);

            if (jump != null && fall != null)
            {
                AnimatorState jumpState = machine.AddState("Jump");
                jumpState.motion = jump;
                AnimatorStateTransition start = machine.AddAnyStateTransition(jumpState);
                start.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                start.hasExitTime = false;
                start.duration = 0.1f;
                start.canTransitionToSelf = false;

                AnimatorStateTransition toAir = jumpState.AddTransition(air);
                toAir.hasExitTime = true;
                toAir.exitTime = 0.9f;
                toAir.duration = 0.15f;
                AddTransition(jumpState, ground, "Grounded", true, 0.1f);
            }
            else if (jump != null)
            {
                AnimatorStateTransition restart = machine.AddAnyStateTransition(air);
                restart.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                restart.hasExitTime = false;
                restart.duration = 0.1f;
                restart.canTransitionToSelf = false;
            }

            return controller;
        }

        static void AddTransition(AnimatorState from, AnimatorState to, string boolParameter, bool value, float duration)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, boolParameter);
            transition.hasExitTime = false;
            transition.duration = duration;
        }

        static string BuildPrefab(string folder, string name, string mainPath, CharacterConfig config, Avatar avatar, AnimatorController controller)
        {
            var root = new GameObject(name);
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(mainPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                // A plain copy, so body parts can be regrouped under joints.
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.transform.SetParent(root.transform, false);
                ApplyColors(folder, name, model, config.colors);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, config.yawOffset, 0f);

                Bounds bounds = CharacterMetrics.CalculateBounds(model);
                float height = bounds.size.y;
                float scale = 1f;
                if (config.height > 0f && height > 0.0001f) scale = config.height / height;
                else if (height > 0.0001f && (height < 0.3f || height > 10f)) scale = 1.8f / height; // centimetre or tiny exports
                model.transform.localScale *= scale;

                bounds = CharacterMetrics.CalculateBounds(model);
                height = Mathf.Max(0.3f, bounds.size.y);
                // Feet on the ground, body centred under the root (exports keep their world offset).
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                var characterController = root.AddComponent<CharacterController>();
                characterController.height = height;
                // T-poses make X wide, so use the narrower horizontal extent.
                characterController.radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f, 0.2f, height * 0.5f);
                characterController.center = new Vector3(0f, height * 0.5f + characterController.skinWidth, 0f);
                characterController.stepOffset = Mathf.Min(0.3f, height * 0.25f);

                var character = root.AddComponent<PlayableCharacter>();
                character.profile = new CharacterProfile
                {
                    displayName = config.displayName,
                    walkSpeed = config.walkSpeed,
                    runSpeed = config.runSpeed,
                    jumpHeight = config.jumpHeight,
                    gravity = config.gravity,
                };

                bool rigged = !config.segments.IsEmpty && BuildSegmentRig(root, model, config.segments, character, folder, Mathf.Clamp(config.softness, 0f, 2f), Mathf.Clamp(config.smooth, 0, 3));
                if (!rigged && controller == null) LogPartsHint(name, model);

                Animator animator = model.GetComponentInChildren<Animator>();
                if (animator != null && rigged && controller == null)
                {
                    // Joints are swung by ProceduralGait; an empty Animator would only cost time.
                    UnityEngine.Object.DestroyImmediate(animator);
                    animator = null;
                }
                if (animator == null && controller != null) animator = model.AddComponent<Animator>();
                if (animator != null)
                {
                    if (controller != null) animator.runtimeAnimatorController = controller;
                    if (animator.avatar == null && avatar != null) animator.avatar = avatar;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    character.animator = animator;
                }

                string prefabPath = OutputRoot + "/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                string animation = controller != null
                    ? string.Join(", ", controller.animationClips.Select(c => c.name).Distinct())
                    : rigged ? "процедурная по частям тела" : "нет";
                Debug.Log($"[Playground] {name}: модель {mainPath}, рост {height:0.00} м, " +
                          $"скелет {(avatar != null && avatar.isHuman ? "Humanoid" : "Generic")}, анимации: {animation}");
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void ApplyColors(string folder, string name, GameObject model, PartColor[] colors)
        {
            if (colors == null || colors.Length == 0) return;
            string generated = folder + "/" + GeneratedFolderName;
            EnsureFolder(generated);
            Dictionary<string, Transform> parts = PartsByName(model);
            for (int i = 0; i < colors.Length; i++)
            {
                if (!ColorUtility.TryParseHtmlString(colors[i].color, out Color color))
                {
                    Debug.LogWarning($"[Playground] {name}: не понял цвет \"{colors[i].color}\" — нужен вид #RRGGBB.");
                    continue;
                }
                string path = $"{generated}/{name}-color{i + 1}.mat";
                AssetDatabase.DeleteAsset(path);
                var material = new Material(Shader.Find("Standard")) { color = color };
                material.SetFloat("_Glossiness", 0.2f);
                AssetDatabase.CreateAsset(material, path);

                foreach (Transform part in FindParts(name, parts, colors[i].parts))
                {
                    foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    }
                }
            }
        }

        /// <summary>
        /// Regroups a model made of separate body parts under joints (waist, neck, shoulders,
        /// elbows, hips, knees), melts the parts into one mesh skinned to those joints so they
        /// bend like skin instead of turning like hinges, and adds ProceduralGait to move them.
        /// </summary>
        static bool BuildSegmentRig(GameObject root, GameObject model, SegmentConfig segments, PlayableCharacter character, string folder, float softness, int smooth)
        {
            string name = root.name;
            Dictionary<string, Transform> parts = PartsByName(model);
            List<Transform> head = FindParts(name, parts, segments.head);
            List<Transform> upperTorso = FindParts(name, parts, segments.upperTorso);
            List<Transform> lowerTorso = FindParts(name, parts, segments.lowerTorso);
            var leftArm = new Limb(FindParts(name, parts, segments.leftUpperArm), FindParts(name, parts, segments.leftLowerArm), FindParts(name, parts, segments.leftHand));
            var rightArm = new Limb(FindParts(name, parts, segments.rightUpperArm), FindParts(name, parts, segments.rightLowerArm), FindParts(name, parts, segments.rightHand));
            var leftLeg = new Limb(FindParts(name, parts, segments.leftUpperLeg), FindParts(name, parts, segments.leftLowerLeg), FindParts(name, parts, segments.leftFoot));
            var rightLeg = new Limb(FindParts(name, parts, segments.rightUpperLeg), FindParts(name, parts, segments.rightLowerLeg), FindParts(name, parts, segments.rightFoot));
            if (leftArm.IsEmpty && rightArm.IsEmpty && leftLeg.IsEmpty && rightLeg.IsEmpty)
            {
                Debug.LogWarning($"[Playground] {name}: в segments не найдено ни одной руки или ноги — анимации не будет.");
                return false;
            }

            // The character faces +Z, so its left is -X; swap sides that were named the other way round.
            if (!leftArm.IsEmpty && !rightArm.IsEmpty && leftArm.Center.x > rightArm.Center.x)
            {
                (leftArm, rightArm) = (rightArm, leftArm);
                Debug.LogWarning($"[Playground] {name}: левая и правая рука перепутаны в segments — поменял местами.");
            }
            if (!leftLeg.IsEmpty && !rightLeg.IsEmpty && leftLeg.Center.x > rightLeg.Center.x)
            {
                (leftLeg, rightLeg) = (rightLeg, leftLeg);
                Debug.LogWarning($"[Playground] {name}: левая и правая нога перепутаны в segments — поменял местами.");
            }

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            model.transform.SetParent(body, true);
            Attach(lowerTorso, body);
            var blends = new List<Blend>();
            var bones = new List<Transform> { body };

            Transform chest = null;
            if (upperTorso.Count > 0)
            {
                Bounds upper = BoundsOf(upperTorso);
                if (!FitCap(name, "Waist", upperTorso, upper, false, out Vector3 waist, out float waistRadius))
                {
                    float waistY = lowerTorso.Count > 0 ? (upper.min.y + BoundsOf(lowerTorso).max.y) * 0.5f : upper.min.y + upper.size.y * 0.15f;
                    waist = new Vector3(upper.center.x, waistY, upper.center.z);
                    waistRadius = upper.size.z * 0.5f;
                }
                chest = Joint("Waist", body, waist, upperTorso);
                Transform waistMid = HalfJointOf(chest, body, waist);
                bones.Add(chest);
                bones.Add(waistMid);
                blends.Add(new Blend(body, waistMid, chest, waist, (upper.center - waist).normalized, waistRadius * softness, false));
            }
            Transform torso = chest != null ? chest : body;

            Transform neck = null;
            if (head.Count > 0)
            {
                // Hair and hats listed with the head often reach lower than the head itself; use the lowest piece.
                Bounds headBounds = BoundsOf(head);
                Bounds main = BoundsOf(head.Take(1).ToList());
                neck = Joint("Neck", torso, new Vector3(main.center.x, Mathf.Min(main.min.y, headBounds.min.y), main.center.z), head);
            }

            if (neck != null) bones.Add(neck);
            Bounds armSocket = upperTorso.Count > 0 ? BoundsOf(upperTorso) : CharacterMetrics.CalculateBounds(model);
            Bounds legSocket = lowerTorso.Count > 0 ? BoundsOf(lowerTorso) : armSocket;
            // Shoulders: rigid, turning on the side of the torso near the top of the arm. Hips: blended, the thigh's top already sits inside the pelvis.
            Transform leftShoulder = leftArm.Build(name, "Left", "Shoulder", "Elbow", torso, blends, bones, softness, ShoulderSoftness, true, ShoulderLift, armSocket, out Transform leftElbow);
            Transform rightShoulder = rightArm.Build(name, "Right", "Shoulder", "Elbow", torso, blends, bones, softness, ShoulderSoftness, true, ShoulderLift, armSocket, out Transform rightElbow);
            Transform leftHip = leftLeg.Build(name, "Left", "Hip", "Knee", body, blends, bones, softness, 1f, false, 0f, legSocket, out Transform leftKnee);
            Transform rightHip = rightLeg.Build(name, "Right", "Hip", "Knee", body, blends, bones, softness, 1f, false, 0f, legSocket, out Transform rightKnee);

            // How much to round each piece: body and limbs fully, hands and feet a little less; the
            // head, hats and hair not at all (a face has small details that welding would damage).
            var rounding = new Dictionary<Transform, int>();
            foreach (Transform part in upperTorso.Concat(lowerTorso).Concat(leftArm.Upper).Concat(leftArm.Lower).Concat(rightArm.Upper).Concat(rightArm.Lower)
                         .Concat(leftLeg.Upper).Concat(leftLeg.Lower).Concat(rightLeg.Upper).Concat(rightLeg.Lower)) rounding[part] = smooth;
            foreach (Transform part in leftArm.End.Concat(rightArm.End).Concat(leftLeg.End).Concat(rightLeg.End)) rounding[part] = Mathf.Max(0, smooth - 1);
            BuildSkin(name, folder, root, bones, blends, rounding);

            float hipHeight = Mathf.Max(leftHip != null ? leftHip.position.y : 0f, rightHip != null ? rightHip.position.y : 0f);
            var gait = root.AddComponent<ProceduralGait>();
            gait.character = character;
            gait.body = body;
            gait.chest = chest;
            gait.head = neck;
            gait.leftArm = leftShoulder;
            gait.rightArm = rightShoulder;
            gait.leftForearm = leftElbow;
            gait.rightForearm = rightElbow;
            gait.leftLeg = leftHip;
            gait.rightLeg = rightHip;
            gait.leftShin = leftKnee;
            gait.rightShin = rightKnee;
            gait.strideLength = Mathf.Max(0.2f, hipHeight * 1.4f);
            return true;
        }

        /// <summary>
        /// A joint where skin weights pass from one bone to the next along the limb: a vertex
        /// on the body side of the zone follows the parent bone, one past the zone follows the
        /// child, and in between the weight changes smoothly.
        /// </summary>
        class Blend
        {
            public readonly Transform parent;
            public readonly Transform mid;     // HalfJoint helper on the pivot, turning by half the child's rotation
            public readonly Transform child;
            public readonly Vector3 pivot;
            public readonly Vector3 axis;      // unit vector from the pivot into the child piece
            public readonly float halfWidth;   // half the length of the zone along the axis
            public readonly bool parentSide;   // vertices of the parent bone's piece blend too (an upper arm towards its elbow)

            public Blend(Transform parent, Transform mid, Transform child, Vector3 pivot, Vector3 axis, float halfWidth, bool parentSide)
            {
                this.parent = parent;
                this.mid = mid;
                this.child = child;
                this.pivot = pivot;
                this.axis = axis;
                this.halfWidth = Mathf.Max(0.01f, halfWidth);
                this.parentSide = parentSide;
            }

            /// <summary>
            /// Shares of the three bones at a point: the parent before the zone, the helper on the
            /// pivot, the child past the zone, blending smoothly in between.
            /// </summary>
            public void Shares(Vector3 point, out float parentShare, out float midShare, out float childShare)
            {
                float d = Vector3.Dot(point - pivot, axis);
                if (d < 0f)
                {
                    float t = Smooth((d + halfWidth) / halfWidth);
                    parentShare = 1f - t;
                    midShare = t;
                    childShare = 0f;
                }
                else
                {
                    float t = Smooth(d / halfWidth);
                    parentShare = 0f;
                    midShare = 1f - t;
                    childShare = t;
                }
            }

            static float Smooth(float t)
            {
                t = Mathf.Clamp01(t);
                return t * t * (3f - 2f * t);
            }
        }

        /// <summary>A helper bone on a joint that turns by half of the joint's rotation (see HalfJoint).</summary>
        static Transform HalfJointOf(Transform joint, Transform parent, Vector3 pivot)
        {
            Transform mid = Joint(joint.name + "Mid", parent, pivot, new List<Transform>());
            mid.gameObject.AddComponent<HalfJoint>().joint = joint;
            return mid;
        }

        /// <summary>
        /// Melts the body parts into one mesh skinned to the joints. Every vertex follows the bone
        /// of its own part, except near a joint, where its weight passes smoothly to the next bone
        /// over a zone about as long as the joint is round. Pieces nested inside each other are
        /// moved by the same rule, so they bend together and nothing shows through.
        /// </summary>
        static void BuildSkin(string character, string folder, GameObject root, List<Transform> bones, List<Blend> blends, Dictionary<Transform, int> rounding)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var materials = new List<Material>();
            var triangles = new List<List<int>>();
            var parts = new List<GameObject>();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (source == null || renderer == null || renderer.sharedMaterials.Length == 0) continue;
                parts.Add(filter.gameObject);
                Transform home = HomeBone(filter.transform, bones);
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix; // the root sits at the origin, so world is root space
                int offset = vertices.Count;

                var v = new List<Vector3>();
                foreach (Vector3 local in source.vertices) v.Add(toWorld.MultiplyPoint3x4(local));
                List<Vector3> n = source.normals.Select(normal => toWorld.MultiplyVector(normal).normalized).ToList();
                List<Vector2> uv = source.uv.ToList();
                var tris = new List<int>(source.triangles);

                // Round the piece unless it is textured (welding would break the texture seams).
                rounding.TryGetValue(filter.transform, out int rounds);
                bool textured = renderer.sharedMaterials.Any(m => m != null && m.mainTexture != null);
                if (rounds > 0 && !textured && source.subMeshCount == 1)
                {
                    MeshSmoothing.Weld(v, tris);
                    for (int i = 0; i < rounds; i++) MeshSmoothing.Subdivide(v, tris);
                    n = MeshSmoothing.Normals(v, tris);
                    uv = Enumerable.Repeat(Vector2.zero, v.Count).ToList();
                }

                for (int i = 0; i < v.Count; i++)
                {
                    vertices.Add(v[i]);
                    normals.Add(n.Count == v.Count ? n[i] : Vector3.up);
                    uvs.Add(uv.Count == v.Count ? uv[i] : Vector2.zero);
                    weights.Add(Weights(v[i], home, bones, blends));
                }
                Material material = renderer.sharedMaterials[0];
                int slot = materials.IndexOf(material);
                if (slot < 0)
                {
                    slot = materials.Count;
                    materials.Add(material);
                    triangles.Add(new List<int>());
                }
                foreach (int index in tris) triangles[slot].Add(index + offset);
            }
            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = character + " skin" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix).ToArray();
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds();

            string generated = folder + "/" + GeneratedFolderName;
            EnsureFolder(generated);
            string path = $"{generated}/{character}-skin.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var skin = new GameObject("Skin");
            skin.transform.SetParent(root.transform, false);
            var skinned = skin.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.bones = bones.ToArray();
            skinned.rootBone = bones[0];
            skinned.sharedMaterials = materials.ToArray();
            skinned.updateWhenOffscreen = true;
            skinned.localBounds = mesh.bounds;

            foreach (GameObject part in parts) UnityEngine.Object.DestroyImmediate(part);
            string zones = string.Join(", ", blends.Select(b => $"{b.child.name} {b.halfWidth * 2f:0.00} м"));
            Debug.Log($"[Playground] {character}: кожа из {vertices.Count} вершин на {bones.Count} костях; зоны сгиба: {zones}");
        }

        /// <summary>The bone a part is attached to: its nearest ancestor among the bones.</summary>
        static Transform HomeBone(Transform part, List<Transform> bones)
        {
            for (Transform t = part; t != null; t = t.parent)
            {
                if (bones.Contains(t)) return t;
            }
            return bones[0];
        }

        static BoneWeight Weights(Vector3 point, Transform home, List<Transform> bones, List<Blend> blends)
        {
            var share = new Dictionary<Transform, float>();
            float remaining = 1f;
            foreach (Blend blend in blends)
            {
                if (blend.child == home)
                {
                    blend.Shares(point, out float parent, out float mid, out float child);
                    Add(share, blend.parent, remaining * parent);
                    Add(share, blend.mid, remaining * mid);
                    remaining *= child;
                }
                else if (blend.parentSide && blend.parent == home)
                {
                    blend.Shares(point, out float parent, out float mid, out float child);
                    Add(share, blend.child, remaining * child);
                    Add(share, blend.mid, remaining * mid);
                    remaining *= parent;
                }
            }
            Add(share, home, remaining);

            List<KeyValuePair<Transform, float>> strongest = share.OrderByDescending(s => s.Value).Take(4).ToList();
            float total = strongest.Sum(s => s.Value);
            var result = new BoneWeight();
            for (int i = 0; i < strongest.Count; i++)
            {
                int index = bones.IndexOf(strongest[i].Key);
                float value = strongest[i].Value / total;
                switch (i)
                {
                    case 0: result.boneIndex0 = index; result.weight0 = value; break;
                    case 1: result.boneIndex1 = index; result.weight1 = value; break;
                    case 2: result.boneIndex2 = index; result.weight2 = value; break;
                    default: result.boneIndex3 = index; result.weight3 = value; break;
                }
            }
            return result;
        }

        static void Add(Dictionary<Transform, float> share, Transform bone, float value)
        {
            if (value <= 0f) return;
            share.TryGetValue(bone, out float current);
            share[bone] = current + value;
        }

        /// <summary>An arm or a leg: upper piece, lower piece and hand or foot.</summary>
        class Limb
        {
            readonly List<Transform> upper;
            readonly List<Transform> lower;
            readonly List<Transform> end;

            public Limb(List<Transform> upper, List<Transform> lower, List<Transform> end)
            {
                this.upper = upper;
                this.lower = lower;
                this.end = end;
            }

            public bool IsEmpty => upper.Count == 0 && lower.Count == 0 && end.Count == 0;
            public Vector3 Center => BoundsOf(upper.Concat(lower).Concat(end).ToList()).center;
            public List<Transform> Upper => upper;
            public List<Transform> Lower => lower;
            public List<Transform> End => end;

            /// <summary>
            /// Creates the root joint (shoulder or hip) and, with a lower piece, the middle joint
            /// (elbow or knee), each at the centre of the rounded end of the piece it moves, and
            /// records the skin blend zone of each joint, as long as that end is round.
            /// </summary>
            public Transform Build(string character, string side, string rootName, string middleName, Transform parent, List<Blend> blends, List<Transform> bones,
                float softness, float rootSoftness, bool rootOnSocket, float rootLift, Bounds socket, out Transform middle)
            {
                middle = null;
                if (IsEmpty) return null;
                List<Transform> top = upper.Count > 0 ? upper : lower.Count > 0 ? lower : end;
                Bounds topBounds = BoundsOf(top);
                if (!FitCap(character, side + rootName, top, topBounds, true, out Vector3 rootPosition, out float rootRadius))
                {
                    // A ball joint sits about half the limb's thickness below its top.
                    float thickness = Mathf.Min(topBounds.size.x, topBounds.size.z);
                    rootPosition = new Vector3(topBounds.center.x, topBounds.max.y - thickness * 0.5f, topBounds.center.z);
                    rootRadius = thickness * 0.5f;
                }
                if (rootOnSocket)
                {
                    // A shoulder turns on the side of the torso, near the top of the arm, as the joint of
                    // the original model does: the flat inner side of the arm then swings around its own
                    // plane and becomes the underside of a raised arm, the rounded top swings into the
                    // body, and nothing of the root is left outside to show a cut or a bulge.
                    rootPosition.x = socket.center.x + Mathf.Sign(rootPosition.x - socket.center.x) * socket.extents.x;
                    rootPosition.y = Mathf.Lerp(rootPosition.y, topBounds.max.y, rootLift);
                }
                Transform rootJoint = Joint(side + rootName, parent, rootPosition, top);

                Vector3 middlePosition = rootPosition;
                float middleRadius = 0f;
                if (upper.Count > 0 && lower.Count > 0)
                {
                    Bounds lowerBounds = BoundsOf(lower);
                    if (!FitCap(character, side + middleName, lower, lowerBounds, true, out middlePosition, out middleRadius))
                    {
                        float y = (topBounds.min.y + lowerBounds.max.y) * 0.5f; // middle of the overlap
                        middlePosition = new Vector3(lowerBounds.center.x, y, lowerBounds.center.z);
                        middleRadius = Mathf.Min(lowerBounds.size.x, lowerBounds.size.z) * 0.5f;
                    }
                    middle = Joint(side + middleName, rootJoint, middlePosition, lower);
                }
                if (end.Count > 0 && top != end) Attach(end, middle != null ? middle : rootJoint);

                Vector3 axis = middle != null ? (middlePosition - rootPosition).normalized : Vector3.down;
                bones.Add(rootJoint);
                if (rootSoftness > 0f)
                {
                    Transform rootMid = HalfJointOf(rootJoint, parent, rootPosition);
                    bones.Add(rootMid);
                    blends.Add(new Blend(parent, rootMid, rootJoint, rootPosition, axis, rootRadius * softness * rootSoftness, false));
                }
                if (middle != null)
                {
                    Transform middleMid = HalfJointOf(middle, rootJoint, middlePosition);
                    bones.Add(middle);
                    bones.Add(middleMid);
                    blends.Add(new Blend(rootJoint, middleMid, middle, middlePosition, axis, middleRadius * softness, true));
                }
                return rootJoint;
            }
        }

        const float CapFraction = 0.3f;
        // A shoulder is rigid: skin blended between the torso and a raised arm has nowhere to go
        // but outside the body, where it hangs as a pouch or a lip. The arm turns as one piece
        // around a point on the side of the torso, near its top (see Limb.Build).
        const float ShoulderSoftness = 0f; // share of the usual blend zone at a shoulder; 0 is rigid
        const float ShoulderLift = 0.6f;   // where the pivot sits between the arm's fitted cap centre (0) and its top (1)

        /// <summary>
        /// Pivot for the rounded end of a piece, from its mesh: the point on the piece's axis that
        /// keeps the end's vertices closest (the smallest enclosing circle in the YZ plane), so a
        /// swing around it sweeps the smallest cylinder and the end stays inside its socket. The
        /// X of the pivot is found the same way in the XY plane for sideways swings. Fails on
        /// pieces without a rounded end, such as plain boxes.
        /// </summary>
        static bool FitCap(string character, string joint, List<Transform> parts, Bounds bounds, bool top, out Vector3 center, out float radius)
        {
            center = bounds.center;
            radius = 0f;
            List<Vector3> points = CapPoints(parts, bounds, top);
            if (points.Count < 8) return false;

            float minZ = float.MaxValue, maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vector3 p in points)
            {
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            }
            float zc = (minZ + maxZ) * 0.5f;
            float low = bounds.min.y - bounds.size.y, high = bounds.max.y + bounds.size.y;
            float cy = Minimise(low, high, y => Reach(points, 1, 2, y, zc));
            radius = Reach(points, 1, 2, cy, zc);
            float cx = Minimise(minX - bounds.size.x, maxX + bounds.size.x, x => Reach(points, 0, 1, x, cy));

            // Only trust a fit that lands inside the piece with a plausible radius.
            float extent = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
            Bounds allowed = bounds;
            allowed.Expand(bounds.size * 0.5f);
            var candidate = new Vector3(cx, cy, zc);
            if (!allowed.Contains(candidate) || radius < extent * 0.3f || radius > extent * 1.6f) return false;

            center = candidate;
            Debug.Log($"[Playground] {character}: сустав {joint} — радиус {radius:0.000} м, центр {candidate}");
            return true;
        }

        /// <summary>World-space vertices in the top or bottom part of the pieces.</summary>
        static List<Vector3> CapPoints(List<Transform> parts, Bounds bounds, bool top)
        {
            float cut = top ? bounds.max.y - bounds.size.y * CapFraction : bounds.min.y + bounds.size.y * CapFraction;
            var points = new List<Vector3>();
            foreach (Transform part in parts)
            {
                foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    {
                        Vector3 p = toWorld.MultiplyPoint3x4(vertex);
                        if (top ? p.y >= cut : p.y <= cut) points.Add(p);
                    }
                }
            }
            return points;
        }

        /// <summary>Farthest distance from (a, b) to the points, in the plane of axes i and j.</summary>
        static float Reach(List<Vector3> points, int i, int j, float a, float b)
        {
            float farthest = 0f;
            foreach (Vector3 p in points)
            {
                float da = p[i] - a, db = p[j] - b;
                farthest = Mathf.Max(farthest, da * da + db * db);
            }
            return Mathf.Sqrt(farthest);
        }

        /// <summary>Ternary search for the minimum of a convex function on [low, high].</summary>
        static float Minimise(float low, float high, Func<float, float> f)
        {
            for (int i = 0; i < 80; i++)
            {
                float a = low + (high - low) / 3f, b = high - (high - low) / 3f;
                if (f(a) < f(b)) high = b; else low = a;
            }
            return (low + high) * 0.5f;
        }

        static Transform Joint(string name, Transform parent, Vector3 worldPosition, List<Transform> parts)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.position = worldPosition;
            joint.rotation = parent.root.rotation;
            Attach(parts, joint);
            return joint;
        }

        static void Attach(List<Transform> parts, Transform parent)
        {
            foreach (Transform part in parts) part.SetParent(parent, true);
        }

        static Bounds BoundsOf(List<Transform> parts)
        {
            var bounds = new Bounds();
            bool found = false;
            foreach (Transform part in parts)
            {
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                {
                    if (!found) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    found = true;
                }
            }
            return bounds;
        }

        static Dictionary<string, Transform> PartsByName(GameObject model)
        {
            var parts = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t != model.transform && !parts.ContainsKey(t.name)) parts[t.name] = t;
            }
            return parts;
        }

        static List<Transform> FindParts(string character, Dictionary<string, Transform> parts, string names)
        {
            var found = new List<Transform>();
            if (string.IsNullOrWhiteSpace(names)) return found;
            foreach (string raw in names.Split(','))
            {
                string partName = raw.Trim();
                if (partName.Length == 0) continue;
                if (parts.TryGetValue(partName, out Transform part)) found.Add(part);
                else Debug.LogWarning($"[Playground] {character}: в модели нет части \"{partName}\". Есть: {string.Join(", ", parts.Keys)}");
            }
            return found;
        }

        /// <summary>Lists the pieces of a model without animations, to help write "segments" for it.</summary>
        static void LogPartsHint(string name, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length < 2) return;
            IEnumerable<string> lines = renderers
                .OrderByDescending(r => r.bounds.center.y)
                .Select(r => $"{r.name}: центр ({r.bounds.center.x:0.00}, {r.bounds.center.y:0.00}, {r.bounds.center.z:0.00}), размер ({r.bounds.size.x:0.00}, {r.bounds.size.y:0.00}, {r.bounds.size.z:0.00})");
            Debug.Log($"[Playground] {name}: анимаций нет, модель из {renderers.Length} частей. " +
                      "Чтобы оживить её, перечислите части в \"segments\" в character.json:\n" + string.Join("\n", lines));
        }

        static Avatar LoadAvatar(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
        }

        static bool HasMarker(AssetImporter importer, string marker)
        {
            return importer.userData != null && importer.userData.Contains(marker);
        }

        static void AddMarker(AssetImporter importer, string marker)
        {
            if (HasMarker(importer, marker)) return;
            importer.userData = string.IsNullOrEmpty(importer.userData) ? marker : importer.userData + ";" + marker;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

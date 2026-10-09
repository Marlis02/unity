using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
                model.transform.localPosition -= new Vector3(0f, bounds.min.y, 0f); // feet on the ground

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

                bool rigged = !config.segments.IsEmpty && BuildSegmentRig(root, model, config.segments, character);
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
        /// elbows, hips, knees) and adds ProceduralGait to swing them.
        /// </summary>
        static bool BuildSegmentRig(GameObject root, GameObject model, SegmentConfig segments, PlayableCharacter character)
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

            Transform chest = null;
            if (upperTorso.Count > 0)
            {
                Bounds upper = BoundsOf(upperTorso);
                float waistY = lowerTorso.Count > 0 ? BoundsOf(lowerTorso).max.y : upper.min.y + upper.size.y * 0.15f;
                chest = Joint("Waist", body, new Vector3(upper.center.x, waistY, upper.center.z), upperTorso);
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

            Transform leftShoulder = leftArm.Build("Left", "Shoulder", "Elbow", torso, out Transform leftElbow);
            Transform rightShoulder = rightArm.Build("Right", "Shoulder", "Elbow", torso, out Transform rightElbow);
            Transform leftHip = leftLeg.Build("Left", "Hip", "Knee", body, out Transform leftKnee);
            Transform rightHip = rightLeg.Build("Right", "Hip", "Knee", body, out Transform rightKnee);

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

            /// <summary>Creates the root joint (shoulder or hip) and, with a lower piece, the middle joint (elbow or knee).</summary>
            public Transform Build(string side, string rootName, string middleName, Transform parent, out Transform middle)
            {
                middle = null;
                if (IsEmpty) return null;
                List<Transform> top = upper.Count > 0 ? upper : lower.Count > 0 ? lower : end;
                Bounds topBounds = BoundsOf(top);
                // A ball joint sits about half the limb's thickness below its top.
                float thickness = Mathf.Min(topBounds.size.x, topBounds.size.z);
                var rootPosition = new Vector3(topBounds.center.x, topBounds.max.y - thickness * 0.5f, topBounds.center.z);
                Transform rootJoint = Joint(side + rootName, parent, rootPosition, top);

                if (upper.Count > 0 && lower.Count > 0)
                {
                    Bounds lowerBounds = BoundsOf(lower);
                    float y = (topBounds.min.y + lowerBounds.max.y) * 0.5f; // middle of the overlap
                    middle = Joint(side + middleName, rootJoint, new Vector3(lowerBounds.center.x, y, lowerBounds.center.z), lower);
                }
                if (end.Count > 0 && top != end) Attach(end, middle != null ? middle : rootJoint);
                return rootJoint;
            }
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

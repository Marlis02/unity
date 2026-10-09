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
            public float turnSpeed = 720f;
            public float airControl = 0.4f;
            public float acceleration = 30f;
            [Tooltip("Scale the model to this height in metres; 0 keeps the imported size.")]
            public float height = 0f;
            [Tooltip("Extra rotation around Y if the model does not face +Z.")]
            public float yawOffset = 0f;
            [Tooltip("Model file inside the folder to use; picked automatically when empty.")]
            public string model = "";
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
            return BuildPrefab(name, mainPath, config, avatar, controller);
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

        static string BuildPrefab(string name, string mainPath, CharacterConfig config, Avatar avatar, AnimatorController controller)
        {
            var root = new GameObject(name);
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(mainPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(root.transform, false);
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
                    turnSpeed = config.turnSpeed,
                    airControl = config.airControl,
                    acceleration = config.acceleration,
                };

                Animator animator = model.GetComponentInChildren<Animator>();
                if (animator == null && (controller != null || avatar != null)) animator = model.AddComponent<Animator>();
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
                Debug.Log($"[Playground] {name}: модель {mainPath}, рост {height:0.00} м, " +
                          $"скелет {(avatar != null && avatar.isHuman ? "Humanoid" : "Generic")}, " +
                          $"анимации: {(controller != null ? string.Join(", ", controller.animationClips.Select(c => c.name).Distinct()) : "нет")}");
                return prefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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

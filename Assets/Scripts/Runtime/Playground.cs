using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CharacterPlayground
{
    /// <summary>
    /// Entry point of the viewer scene: a clean stage with the imported characters
    /// (Resources/Characters), a free camera for touch and mouse, and a small menu to pick a
    /// character, the motion it shows on the spot, the playback speed and a pause. The menu
    /// hides completely so the frame can be recorded clean.
    /// </summary>
    public class Playground : MonoBehaviour
    {
        public const string CharacterResourcesFolder = "Characters";
        const float RowGap = 0.6f;
        const float SpawnLift = 0.05f;

        static readonly CharacterMotion[][] MotionRows =
        {
            new[] { CharacterMotion.Idle, CharacterMotion.Walk, CharacterMotion.Run, CharacterMotion.Jump },
            new[] { CharacterMotion.ArmsOut, CharacterMotion.ArmsForward, CharacterMotion.Squat },
        };
        static readonly string[][] MotionTitleRows =
        {
            new[] { "Покой", "Шаг", "Бег", "Прыжок" },
            new[] { "Руки в стороны", "Руки вперёд", "Присед" },
        };
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
        static readonly string[] SpeedTitles = { "0.25×", "0.5×", "1×" };

        public Material baseMaterial;

        class Entry
        {
            public string name;
            public GameObject prefab;
            public CharacterProfile profile;
            public CharacterMotion motion;
            public PlayableCharacter instance;
            public float height;
            public GameObject label;
        }

        readonly List<Entry> entries = new List<Entry>();
        Transform charactersRoot;
        OrbitCamera orbit;
        int currentIndex;
        bool showAll = true;
        bool menuVisible = true;
        float playbackSpeed = 0.5f; // slowed down so the motions are easy to follow
        bool paused;
        float fps;
        float fpsTimer;
        int fpsFrames;

        GUIStyle panelStyle;
        GUIStyle labelStyle;
        GUIStyle titleStyle;
        GUIStyle smallStyle;
        GUIStyle buttonStyle;
        GUIStyle activeButtonStyle;
        Texture2D panelBackground;

        void Awake()
        {
            if (baseMaterial == null) baseMaterial = Resources.Load<Material>("PlaygroundBase");
            if (baseMaterial == null) baseMaterial = new Material(Shader.Find("Standard"));

            SetupEnvironment();
            StageBuilder.Build(transform, baseMaterial);
            charactersRoot = new GameObject("Characters").transform;
            charactersRoot.SetParent(transform, false);

            CollectEntries();
            Arrange();
            FrameCurrent(true);
            SetPlaybackSpeed(playbackSpeed);
        }

        void OnDestroy()
        {
            if (panelBackground != null) Destroy(panelBackground);
        }

        void SetupEnvironment()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.76f, 0.9f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 60f;
            orbit = cameraObject.AddComponent<OrbitCamera>();
            orbit.Tapped += () => SetPaused(!paused);
            orbit.DoubleTapped += () => FrameCurrent(false);
            orbit.LongPressed += () => SetMenuVisible(true);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(50f, 145f, 0f); // from the front, where the camera starts

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.63f);
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(RenderSettings.ambientLight);
            RenderSettings.ambientProbe = ambient;
            QualitySettings.shadowDistance = 60f;
        }

        void CollectEntries()
        {
            GameObject[] prefabs = Resources.LoadAll<GameObject>(CharacterResourcesFolder);
            System.Array.Sort(prefabs, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var prefab in prefabs)
            {
                var character = prefab.GetComponent<PlayableCharacter>();
                if (character == null) continue;
                CharacterProfile profile = character.profile.Clone();
                if (string.IsNullOrEmpty(profile.displayName)) profile.displayName = prefab.name;
                entries.Add(new Entry { name = profile.displayName, prefab = prefab, profile = profile });
            }
        }

        // ---------------------------------------------------------------- stage

        /// <summary>Puts every character in a row facing the camera, or only the current one.</summary>
        void Arrange()
        {
            foreach (var entry in entries)
            {
                if (entry.instance != null) Destroy(entry.instance.gameObject);
                entry.instance = null;
                entry.label = null;
            }
            if (entries.Count == 0) return;

            if (!showAll)
            {
                Spawn(entries[currentIndex], Vector3.zero);
                return;
            }

            var widths = new float[entries.Count];
            var offsets = new float[entries.Count];
            float total = -RowGap;
            for (int i = 0; i < entries.Count; i++)
            {
                Spawn(entries[i], Vector3.zero);
                Bounds bounds = CharacterMetrics.CalculateBounds(entries[i].instance.gameObject);
                widths[i] = Mathf.Max(0.3f, bounds.size.x);
                offsets[i] = bounds.center.x - entries[i].instance.transform.position.x;
                total += widths[i] + RowGap;
            }

            // The camera looks along -Z, so the first character ends up on the left of the screen.
            float x = total * 0.5f;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                Place(entry, new Vector3(x - widths[i] * 0.5f - offsets[i], 0f, 0f));
                x -= widths[i] + RowGap;
                if (entries.Count < 2) continue; // the menu already names a lone character
                entry.label = StageBuilder.Label(entry.instance.transform, new Vector3(offsets[i], entry.height + 0.3f, 0f),
                    $"{entry.name}\n{entry.height:0.00} м", 0.7f).gameObject;
                entry.label.SetActive(menuVisible);
            }
        }

        void Spawn(Entry entry, Vector3 position)
        {
            GameObject instance = Instantiate(entry.prefab, charactersRoot);
            instance.name = entry.name;
            instance.transform.SetParent(charactersRoot, false);

            entry.instance = instance.GetComponent<PlayableCharacter>();
            entry.instance.profile = entry.profile;
            entry.instance.motion = entry.motion;
            Place(entry, position);
            entry.height = CharacterMetrics.MeasureHeight(instance);
        }

        static void Place(Entry entry, Vector3 position)
        {
            entry.instance.Teleport(position + Vector3.up * SpawnLift, Quaternion.identity);
        }

        void Select(int index)
        {
            if (entries.Count == 0) return;
            currentIndex = (index % entries.Count + entries.Count) % entries.Count;
            if (!showAll) Arrange();
            FrameCurrent(false);
        }

        void FrameCurrent(bool instant)
        {
            if (entries.Count == 0 || entries[currentIndex].instance == null) return;
            Entry entry = entries[currentIndex];
            Bounds bounds = CharacterMetrics.CalculateBounds(entry.instance.gameObject);
            var point = new Vector3(bounds.center.x, entry.height * 0.55f, bounds.center.z);
            orbit.Focus(point, Mathf.Max(entry.height, bounds.size.x * 0.8f), true, instant);
        }

        void SetMotion(CharacterMotion motion)
        {
            Entry entry = entries[currentIndex];
            entry.motion = motion;
            if (entry.instance != null) entry.instance.motion = motion;
        }

        void SetShowAll(bool value)
        {
            showAll = value;
            Arrange();
            FrameCurrent(false);
        }

        /// <summary>Speed and pause act on the characters only; the camera keeps moving freely.</summary>
        void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = speed;
            paused = false;
            Time.timeScale = playbackSpeed;
        }

        void SetPaused(bool value)
        {
            paused = value;
            Time.timeScale = paused ? 0f : playbackSpeed;
        }

        void SetMenuVisible(bool visible)
        {
            menuVisible = visible;
            foreach (var entry in entries)
            {
                if (entry.label != null) entry.label.SetActive(visible);
            }
        }

        void Update()
        {
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fps = fpsFrames / fpsTimer;
                fpsFrames = 0;
                fpsTimer = 0f;
            }

            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.RightBracket)) Select(currentIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) Select(currentIndex - 1);
            for (int i = 0; i < Mathf.Min(9, entries.Count); i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(i);
            }
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1)) SetMenuVisible(!menuVisible);
            if (Input.GetKeyDown(KeyCode.L)) SetShowAll(!showAll);
            if (Input.GetKeyDown(KeyCode.Space)) SetPaused(!paused);
        }

        // ---------------------------------------------------------------- menu

        void OnGUI()
        {
            if (!menuVisible) return;
            PlaygroundFont.ApplyToGui();
            EnsureStyles();
            // Phones get a menu about as wide as the screen's short side, big enough for fingers.
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            float scale = Application.isMobilePlatform ? Mathf.Max(0.5f, shortSide / 360f) : Mathf.Clamp(shortSide / 620f, 1f, 3f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            if (entries.Count > 0) DrawMenu(scale, width);
            else DrawEmpty(width);
            DrawFps(width);
            DrawHelp(width, height, scale);

            GUI.matrix = previousMatrix;
        }

        void DrawMenu(float scale, float screenWidth)
        {
            Entry entry = entries[currentIndex];
            bool several = entries.Count > 1;
            var area = new Rect(10f, 10f, Mathf.Min(330f, screenWidth - 20f), 244f);
            PlaygroundInput.RegisterUiRect(new Rect(area.x * scale, area.y * scale, area.width * scale, area.height * scale));

            GUILayout.BeginArea(area, panelStyle);

            GUILayout.BeginHorizontal();
            if (several && GUILayout.Button("<", buttonStyle, GUILayout.Width(38f), GUILayout.Height(32f))) Select(currentIndex - 1);
            string title = several ? $"{entry.name}  ({currentIndex + 1}/{entries.Count})" : entry.name;
            GUILayout.Label(title, titleStyle, GUILayout.Height(32f));
            if (several && GUILayout.Button(">", buttonStyle, GUILayout.Width(38f), GUILayout.Height(32f))) Select(currentIndex + 1);
            GUILayout.EndHorizontal();

            GUILayout.Label($"рост {entry.height:0.00} м", smallStyle);

            for (int row = 0; row < MotionRows.Length; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < MotionRows[row].Length; i++)
                {
                    CharacterMotion motion = MotionRows[row][i];
                    GUIStyle style = entry.motion == motion ? activeButtonStyle : buttonStyle;
                    if (GUILayout.Button(MotionTitleRows[row][i], style, GUILayout.Height(34f))) SetMotion(motion);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Speeds.Length; i++)
            {
                GUIStyle style = !paused && Mathf.Approximately(playbackSpeed, Speeds[i]) ? activeButtonStyle : buttonStyle;
                if (GUILayout.Button(SpeedTitles[i], style, GUILayout.Height(34f))) SetPlaybackSpeed(Speeds[i]);
            }
            if (GUILayout.Button(paused ? "Дальше" : "Пауза", paused ? activeButtonStyle : buttonStyle, GUILayout.Height(34f))) SetPaused(!paused);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (several && GUILayout.Button(showAll ? "Все на сцене" : "Только этот", buttonStyle, GUILayout.Height(34f))) SetShowAll(!showAll);
            if (GUILayout.Button("К персонажу", buttonStyle, GUILayout.Height(34f))) FrameCurrent(false);
            if (GUILayout.Button("Скрыть меню", buttonStyle, GUILayout.Height(34f))) SetMenuVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        void DrawEmpty(float screenWidth)
        {
            var area = new Rect(10f, 10f, Mathf.Min(330f, screenWidth - 20f), 60f);
            GUI.Box(area, GUIContent.none, panelStyle);
            GUI.Label(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, area.height - 16f),
                "Персонажей нет: положите модель в папку Assets/Characters и соберите проект.", labelStyle);
        }

        void DrawFps(float width)
        {
            GUI.Label(new Rect(width - 90f, 8f, 82f, 22f), $"{fps:0} FPS", smallStyle);
        }

        void DrawHelp(float width, float height, float scale)
        {
            string help = Input.touchSupported
                ? "1 палец — вращать  ·  2 пальца — двигать, щипок — зум, поворот пальцев — крутить\n" +
                  "Тап — пауза  ·  двойной тап — к персонажу  ·  долгое нажатие — вернуть меню"
                : "Левая кнопка — вращать  ·  правая или Shift — двигать  ·  колесо — зум\n" +
                  "Пробел — пауза  ·  двойной клик или F — к персонажу  ·  H — меню";
            float boxWidth = Mathf.Min(560f, width - 20f);
            float textHeight = smallStyle.CalcHeight(new GUIContent(help), boxWidth - 20f);
            var area = new Rect(10f, height - textHeight - 22f, boxWidth, textHeight + 12f);
            PlaygroundInput.RegisterUiRect(new Rect(area.x * scale, area.y * scale, area.width * scale, area.height * scale));
            GUI.Box(area, GUIContent.none, panelStyle);
            GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, area.height - 12f), help, smallStyle);
        }

        void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.11f, 0.82f));
            panelBackground.Apply();

            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
            panelStyle.normal.background = panelBackground;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            labelStyle.normal.textColor = new Color(0.93f, 0.95f, 0.98f);

            titleStyle = new GUIStyle(labelStyle) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            smallStyle = new GUIStyle(labelStyle) { fontSize = 12 };
            smallStyle.normal.textColor = new Color(0.75f, 0.8f, 0.88f);

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            activeButtonStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold };
            var amber = new Color(0.98f, 0.76f, 0.3f);
            activeButtonStyle.normal.textColor = amber;
            activeButtonStyle.hover.textColor = amber;
            activeButtonStyle.active.textColor = amber;
        }
    }
}

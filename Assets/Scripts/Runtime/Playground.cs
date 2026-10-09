using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CharacterPlayground
{
    /// <summary>
    /// Entry point of the playground scene: builds camera, light and test course, lists the
    /// imported characters (Resources/Characters) plus the built-in ones, and draws the HUD
    /// with live stats and tuning sliders.
    /// </summary>
    public class Playground : MonoBehaviour
    {
        public const string CharacterResourcesFolder = "Characters";

        public Material baseMaterial;

        class Entry
        {
            public string name;
            public GameObject prefab;
            public DefaultCharacters.Spec? spec;
            public CharacterProfile defaults;
            public CharacterProfile tuned;
        }

        static readonly Vector3 SpawnPosition = new Vector3(0f, 0.05f, 0f);
        static readonly Vector3 LineupCenter = new Vector3(0f, 0.05f, 4.5f);

        readonly List<Entry> entries = new List<Entry>();
        readonly List<GameObject> lineup = new List<GameObject>();
        Transform charactersRoot;
        OrbitCamera orbit;
        PlayableCharacter current;
        int currentIndex;
        float currentHeight;
        int importedCount;

        bool panelOpen;
        bool helpVisible = true;
        bool lineupVisible;
        float timeScale = 1f;
        float fps;
        float fpsTimer;
        int fpsFrames;

        GUIStyle panelStyle;
        GUIStyle labelStyle;
        GUIStyle titleStyle;
        GUIStyle smallStyle;
        GUIStyle buttonStyle;
        Texture2D panelBackground;

        void Awake()
        {
            if (baseMaterial == null) baseMaterial = Resources.Load<Material>("PlaygroundBase");
            if (baseMaterial == null) baseMaterial = new Material(Shader.Find("Standard"));

            SetupEnvironment();
            ArenaBuilder.Build(transform, baseMaterial);
            charactersRoot = new GameObject("Characters").transform;
            charactersRoot.SetParent(transform, false);
            gameObject.AddComponent<TouchControls>();

            CollectEntries();
            panelOpen = !Application.isMobilePlatform && Screen.width >= 900;
            Select(0);
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

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

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
                entries.Add(new Entry { name = profile.displayName, prefab = prefab, defaults = profile, tuned = profile.Clone() });
            }
            importedCount = entries.Count;

            foreach (var spec in DefaultCharacters.All())
            {
                entries.Add(new Entry { name = spec.name, spec = spec, defaults = spec.profile.Clone(), tuned = spec.profile.Clone() });
            }
        }

        GameObject Spawn(Entry entry, Vector3 position, Quaternion rotation)
        {
            GameObject instance = entry.prefab != null
                ? Instantiate(entry.prefab, charactersRoot)
                : DefaultCharacters.Create(entry.spec.Value, baseMaterial);
            instance.name = entry.name;
            instance.transform.SetParent(charactersRoot, false);

            var character = instance.GetComponent<PlayableCharacter>();
            character.profile = entry.tuned; // shared, so slider changes survive switching characters
            character.Teleport(position, rotation);
            character.SetSpawn(position, rotation);
            return instance;
        }

        void Select(int index)
        {
            if (entries.Count == 0) return;
            index = (index % entries.Count + entries.Count) % entries.Count;
            if (current != null) Destroy(current.gameObject);

            currentIndex = index;
            GameObject instance = Spawn(entries[index], SpawnPosition, Quaternion.identity);
            current = instance.GetComponent<PlayableCharacter>();
            current.viewTransform = orbit.transform;
            current.acceptInput = true;
            currentHeight = CharacterMetrics.MeasureHeight(instance);
            orbit.Frame(instance.transform, currentHeight);

            if (lineupVisible) BuildLineup();
        }

        void ToggleLineup()
        {
            lineupVisible = !lineupVisible;
            if (lineupVisible) BuildLineup();
            else ClearLineup();
        }

        void BuildLineup()
        {
            ClearLineup();
            var others = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i != currentIndex) others.Add(entries[i]);
            }

            const float spacing = 2.6f;
            float startX = -(others.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < others.Count; i++)
            {
                Vector3 position = LineupCenter + new Vector3(startX + i * spacing, 0f, 0f);
                GameObject statue = Spawn(others[i], position, Quaternion.Euler(0f, 180f, 0f));
                var character = statue.GetComponent<PlayableCharacter>();
                character.acceptInput = false;
                character.enabled = false;
                float height = CharacterMetrics.MeasureHeight(statue);
                ArenaBuilder.Label(statue.transform, new Vector3(0f, height + 0.45f, 0f), $"{others[i].name}\n{height:0.00} м", 0.8f);
                lineup.Add(statue);
            }
        }

        void ClearLineup()
        {
            foreach (var statue in lineup)
            {
                if (statue != null) Destroy(statue);
            }
            lineup.Clear();
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
            if (Input.GetKeyDown(KeyCode.R) && current != null) current.Respawn();
            if (Input.GetKeyDown(KeyCode.L)) ToggleLineup();
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1)) helpVisible = !helpVisible;
            if (Input.GetKeyDown(KeyCode.P)) panelOpen = !panelOpen;

            Time.timeScale = timeScale;
        }

        // ---------------------------------------------------------------- HUD

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 620f, 1f, 3f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            DrawCharacterPanel(scale);
            DrawFps(width);
            if (helpVisible && !TouchControls.InUse) DrawHelp(height, scale);

            GUI.matrix = previousMatrix;
        }

        void DrawCharacterPanel(float scale)
        {
            if (current == null) return;
            var area = new Rect(10f, 10f, 310f, panelOpen ? 560f : 178f);
            PlaygroundInput.RegisterUiRect(new Rect(area.x * scale, area.y * scale, area.width * scale, area.height * scale));

            GUILayout.BeginArea(area, panelStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(36f), GUILayout.Height(30f))) Select(currentIndex - 1);
            GUILayout.Label($"{entries[currentIndex].name}  ({currentIndex + 1}/{entries.Count})", titleStyle, GUILayout.Height(30f));
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(36f), GUILayout.Height(30f))) Select(currentIndex + 1);
            GUILayout.EndHorizontal();

            string source = currentIndex < importedCount ? "свой персонаж" : "встроенный персонаж";
            GUILayout.Label(source, smallStyle);
            GUILayout.Label($"Скорость: {current.PlanarSpeed:0.0} м/с    На земле: {(current.Grounded ? "да" : "нет")}", labelStyle);
            GUILayout.Label($"Рост: {currentHeight:0.00} м    Последний прыжок: {current.LastJumpHeight:0.00} м", labelStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(panelOpen ? "Скрыть" : "Настройки", buttonStyle, GUILayout.Height(28f))) panelOpen = !panelOpen;
            if (GUILayout.Button(lineupVisible ? "Убрать строй" : "Строй", buttonStyle, GUILayout.Height(28f))) ToggleLineup();
            if (GUILayout.Button("На старт", buttonStyle, GUILayout.Height(28f))) current.Respawn();
            GUILayout.EndHorizontal();

            if (panelOpen)
            {
                CharacterProfile p = current.profile;
                GUILayout.Space(6f);
                p.walkSpeed = SliderRow("Ходьба", p.walkSpeed, 0.5f, 15f, "0.0", "м/с");
                p.runSpeed = SliderRow("Бег", p.runSpeed, 0.5f, 25f, "0.0", "м/с");
                p.jumpHeight = SliderRow("Высота прыжка", p.jumpHeight, 0f, 6f, "0.00", "м");
                p.gravity = SliderRow("Гравитация", p.gravity, 2f, 60f, "0.0", "м/с²");
                p.turnSpeed = SliderRow("Поворот", p.turnSpeed, 60f, 1440f, "0", "°/с");
                p.acceleration = SliderRow("Разгон", p.acceleration, 2f, 120f, "0", "м/с²");
                p.airControl = SliderRow("Управление в воздухе", p.airControl, 0f, 1f, "0.00", "");
                timeScale = SliderRow("Скорость времени", timeScale, 0.1f, 2f, "0.00", "×");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Сбросить", buttonStyle, GUILayout.Height(28f)))
                {
                    Entry entry = entries[currentIndex];
                    entry.tuned = entry.defaults.Clone();
                    current.profile = entry.tuned;
                    timeScale = 1f;
                }
                if (GUILayout.Button("В консоль (JSON)", buttonStyle, GUILayout.Height(28f)))
                {
                    Debug.Log($"[Playground] {entries[currentIndex].name}: {JsonUtility.ToJson(current.profile, true)}");
                }
                GUILayout.EndHorizontal();
            }

            if (importedCount == 0)
            {
                GUILayout.Label("Свои модели кладите в Assets/Characters", smallStyle);
            }

            GUILayout.EndArea();
        }

        float SliderRow(string title, float value, float min, float max, string format, string unit)
        {
            GUILayout.Label($"{title}: {value.ToString(format)} {unit}", smallStyle);
            return GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(18f));
        }

        void DrawFps(float width)
        {
            GUI.Label(new Rect(width - 90f, 8f, 82f, 22f), $"{fps:0} FPS", smallStyle);
        }

        void DrawHelp(float height, float scale)
        {
            const string help =
                "WASD / стрелки — идти,  Shift — бег,  Пробел — прыжок\n" +
                "Мышь с зажатой кнопкой — камера,  колесо — зум,  Q / E — поворот\n" +
                "Tab или 1–9 — сменить персонажа,  L — строй для сравнения роста\n" +
                "R — на старт,  P — настройки,  H — скрыть подсказку";
            var area = new Rect(10f, height - 94f, 520f, 84f);
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
        }
    }
}

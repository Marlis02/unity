using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Viewer.EditorTools
{
    // The phone viewer: bacon (the prefab built by MinifigCharacterBuilder) on the lineup's studio (its floor, lights and
    // post-processing), a free camera and a menu of his moves and faces (Assets/Viewer/Scripts), built for WebGL so it
    // opens in a phone browser.
    // Tools/Viewer/Build Viewer Scene -> Assets/Viewer/BaconViewer.unity
    // Tools/Viewer/Build WebGL        -> Build/WebGL (the scene first)
    // From the command line (tools/unity.sh build):
    //   -executeMethod Viewer.EditorTools.ViewerBuild.BuildWebGLFromCommandLine [-buildOutput Build/WebGL]
    public static class ViewerBuild
    {
        public const string ScenePath = "Assets/Viewer/BaconViewer.unity";
        const string ActorPrefab = MinifigCharacterBuilder.PrefabFolder + "/bacon.prefab";
        const string DefaultOutput = "Build/WebGL";
        static readonly Color Backdrop = new Color(0.72f, 0.74f, 0.78f); // the lineup's backdrop, which its fog fades into

        [MenuItem("Tools/Viewer/Build Viewer Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first: scene changes made in Play Mode are lost.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
            if (prefab == null) throw new InvalidOperationException($"{ActorPrefab} missing: build it first (Tools/Characters/Build Characters)");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterLineupBuilder.BuildStudio();

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Backdrop;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.Medium;
            cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze
            var viewCamera = camera.gameObject.AddComponent<Viewer.ViewerCamera>();

            // He faces +Z, and the camera starts in front of him, on +Z
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            actor.name = "Bacon";
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var motion = actor.AddComponent<Viewer.BaconMotion>();

            var viewer = new GameObject("Viewer").AddComponent<Viewer.BaconViewer>();
            viewer.viewCamera = viewCamera;
            viewer.actor = motion;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Viewer] Built {ScenePath}");
        }

        [MenuItem("Tools/Viewer/Build WebGL")]
        public static void BuildWebGL() => BuildWebGL(Argument("-buildOutput") ?? DefaultOutput);

        public static void BuildWebGLFromCommandLine()
        {
            int code = 0;
            try { BuildWebGL(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        // Gzip with the JavaScript fallback opens from any static host, even one that sends no Content-Encoding; managed
        // code the scene doesn't use is stripped, as a phone downloads all of it. The project's own settings are put
        // back afterwards.
        static void BuildWebGL(string output)
        {
            BuildScene();
            var compression = PlayerSettings.WebGL.compressionFormat;
            bool fallback = PlayerSettings.WebGL.decompressionFallback, caching = PlayerSettings.WebGL.dataCaching, background = PlayerSettings.runInBackground;
            var stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.dataCaching = false;
                PlayerSettings.runInBackground = true;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                });
                var summary = report.summary;
                Debug.Log($"[Viewer] WebGL build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalErrors} errors, " +
                          $"{summary.totalTime.TotalSeconds:0} s -> {output}");
                if (summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + summary.result);
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                PlayerSettings.WebGL.dataCaching = caching;
                PlayerSettings.runInBackground = background;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, stripping);
                AssetDatabase.SaveAssets();
            }
        }

        static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

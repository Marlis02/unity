using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CharacterPlayground.EditorTools
{
    /// <summary>
    /// Project setup and builds. Command line (see tools/unity.sh):
    ///   -executeMethod CharacterPlayground.EditorTools.PlaygroundBuild.PrepareFromCommandLine
    ///   -executeMethod CharacterPlayground.EditorTools.PlaygroundBuild.BuildWebGLFromCommandLine [-buildOutput Build/WebGL]
    /// </summary>
    public static class PlaygroundBuild
    {
        public const string ScenePath = "Assets/Scenes/Playground.unity";
        public const string MaterialPath = "Assets/Resources/PlaygroundBase.mat";
        const string DefaultWebGLOutput = "Build/WebGL";

        [MenuItem("Playground/Prepare Project")]
        public static void Prepare()
        {
            Material material = EnsureMaterial();
            CharacterImporter.ImportAll();
            EnsureScene(material);
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Playground/Build WebGL")]
        public static void BuildWebGL()
        {
            BuildWebGL(GetArgument("-buildOutput") ?? DefaultWebGLOutput);
        }

        public static void PrepareFromCommandLine()
        {
            RunAndExit(Prepare);
        }

        public static void BuildWebGLFromCommandLine()
        {
            RunAndExit(BuildWebGL);
        }

        static void BuildWebGL(string output)
        {
            Prepare();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Playground] WebGL build {summary.result}: {summary.totalSize / (1024f * 1024f):0.0} MB, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime.TotalSeconds:0}s -> {output}");
            if (summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + summary.result);
        }

        static void RunAndExit(Action action)
        {
            int code = 0;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            CharacterImporter.EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
            material = new Material(Shader.Find("Standard")) { name = "PlaygroundBase" };
            material.SetFloat("_Glossiness", 0.15f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static void EnsureScene(Material material)
        {
            CharacterImporter.EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var playground = new GameObject("Playground").AddComponent<Playground>();
            playground.baseMaterial = material;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "CharacterPlayground";
            PlayerSettings.productName = "Character Playground";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;

            PlayerSettings.WebGL.template = "PROJECT:Playground";
            // Gzip with a JavaScript fallback works on any static host, even without Content-Encoding headers.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        }

        static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}

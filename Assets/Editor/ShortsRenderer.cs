using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Playables;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Renders a short's Timeline frame by frame without Play Mode, for a machine with no desktop (the cloud): the director
// is set to each frame's time and evaluated, the camera renders into a texture, and the pixels go straight into ffmpeg
// (no PNGs on disk). Needs a graphics device: tools/unity.sh render (software OpenGL on a virtual display).
//   tools/unity.sh render -quit -executeMethod ShortsRenderer.RenderFromCommandLine -scene Assets/Scenes/Shorts_X.unity
//     -output out/preview/X.mp4 [-width 540 -height 960] [-from 0 -to 11.5] [-fps 30] [-crf 20]
// A preview is half size (540x960, about half a second a frame on 4 cores); the final video is 1080x1920.
public static class ShortsRenderer
{
    public static void RenderFromCommandLine()
    {
        int code = 0;
        try
        {
            Render(Argument("-scene") ?? throw new ArgumentException("-scene missing"),
                Argument("-output") ?? throw new ArgumentException("-output missing"),
                int.Parse(Argument("-width") ?? "540"), int.Parse(Argument("-height") ?? "960"),
                float.Parse(Argument("-from") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                Argument("-to") is string to ? float.Parse(to, System.Globalization.CultureInfo.InvariantCulture) : -1f,
                int.Parse(Argument("-fps") ?? "30"), int.Parse(Argument("-crf") ?? "20"));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    public static void Render(string scenePath, string output, int width, int height, float from, float to, int fps, int crf)
    {
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true); // PC: the quality the Recorder takes use
        var director = Object.FindFirstObjectByType<PlayableDirector>() ?? throw new InvalidOperationException("No PlayableDirector in " + scenePath);
        var camera = Camera.main ?? throw new InvalidOperationException("No MainCamera in " + scenePath);
        double duration = director.duration;
        if (to < 0f || to > duration) to = (float)duration;
        int first = Mathf.RoundToInt(from * fps), last = Mathf.CeilToInt(to * fps - 0.001f); // frames [first, last)

        var rt = new RenderTexture(width, height, 24, GraphicsFormat.R8G8B8A8_SRGB) { antiAliasing = 1 };
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        DynamicGI.UpdateEnvironment(); // the sky's ambient light; without it the first frames come out dark

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        var ffmpeg = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            // Unity reads the texture bottom row first: vflip puts it the right way up
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {fps} -i - -vf vflip " +
                        $"-c:v libx264 -preset {(width < 1000 ? "veryfast" : "slow")} -crf {crf} -pix_fmt yuv420p -movflags +faststart \"{output}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
        });
        var stdin = ffmpeg.StandardInput.BaseStream;
        var clock = Stopwatch.StartNew();
        try
        {
            Frame(director, camera, first / (double)fps, tex); // warm-up: shaders compile, the post-processing history fills
            for (int i = first; i < last; i++)
            {
                Frame(director, camera, i / (double)fps, tex);
                var bytes = tex.GetRawTextureData<byte>();
                stdin.Write(bytes.ToArray(), 0, bytes.Length);
                if ((i - first) % 30 == 0) Debug.Log($"[ShortsRenderer] frame {i}/{last} ({clock.Elapsed.TotalSeconds:0} s)");
            }
        }
        finally
        {
            stdin.Close();
            ffmpeg.WaitForExit();
            camera.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        if (ffmpeg.ExitCode != 0) throw new Exception("ffmpeg failed with code " + ffmpeg.ExitCode);
        Debug.Log($"[ShortsRenderer] {output}: frames {first}-{last - 1} at {width}x{height}, {clock.Elapsed.TotalSeconds:0} s " +
                  $"({clock.Elapsed.TotalSeconds / Math.Max(1, last - first):0.00} s a frame)");
    }

    static void Frame(PlayableDirector director, Camera camera, double time, Texture2D tex)
    {
        director.time = time;
        director.Evaluate();
        foreach (var face in Object.FindObjectsByType<LiveFace>(FindObjectsSortMode.None)) face.Apply();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
        tex.Apply(false);
        RenderTexture.active = previous;
    }

    static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
}

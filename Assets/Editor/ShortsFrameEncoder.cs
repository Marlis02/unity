using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Unity's built-in H.264 encoder isn't available on Linux, so Recorder tracks write PNG frames into
// Recordings/<name>_frames/. After Play Mode ends this encodes them to Recordings/<name>.mp4 with ffmpeg
// (H.264, yuv420p, 30 fps) and deletes the frames once the MP4 is written.
[InitializeOnLoad]
public static class ShortsFrameEncoder
{
    public const string FramesSuffix = "_frames";
    const int Fps = 30;

    static readonly HashSet<string> s_InProgress = new HashSet<string>();

    static string RecordingsDir => Path.Combine(Directory.GetCurrentDirectory(), "Recordings");

    static ShortsFrameEncoder()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EncodePending();
        };
    }

    [MenuItem("Tools/Shorts/Encode Recorded Frames to MP4")]
    public static void EncodePending()
    {
        if (!Directory.Exists(RecordingsDir)) return;
        foreach (var dir in Directory.GetDirectories(RecordingsDir, "*" + FramesSuffix))
        {
            if (s_InProgress.Contains(dir) || !Directory.EnumerateFiles(dir, "*.png").Any()) continue;
            Encode(dir, UniqueMp4Path(dir.Substring(0, dir.Length - FramesSuffix.Length)));
        }
    }

    // Never overwrite an earlier take: Shorts_Obby_Fail_001.mp4 -> Shorts_Obby_Fail_001_2.mp4
    static string UniqueMp4Path(string basePath)
    {
        string path = basePath + ".mp4";
        for (int i = 2; File.Exists(path); i++) path = basePath + "_" + i + ".mp4";
        return path;
    }

    static void Encode(string framesDir, string mp4)
    {
        string ffmpeg = FindFfmpeg();
        if (ffmpeg == null)
        {
            Debug.LogError("[Shorts] ffmpeg not found, frames kept in " + framesDir);
            return;
        }

        int frameCount = Directory.GetFiles(framesDir, "*.png").Length;
        string args = $"-y -loglevel error -framerate {Fps} -pattern_type glob -i \"{Path.Combine(framesDir, "*.png")}\" " +
                      $"-c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        var errors = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        process.Exited += (_, __) =>
        {
            // Runs on a worker thread; Debug.Log and file IO are safe here
            lock (s_InProgress) s_InProgress.Remove(framesDir);
            if (process.ExitCode == 0 && File.Exists(mp4))
            {
                Directory.Delete(framesDir, true);
                Debug.Log($"[Shorts] Encoded {frameCount} frames -> {mp4}");
            }
            else
            {
                Debug.LogError($"[Shorts] ffmpeg failed ({process.ExitCode}), frames kept in {framesDir}\n{errors}");
            }
            process.Dispose();
        };

        lock (s_InProgress) s_InProgress.Add(framesDir);
        process.Start();
        process.BeginErrorReadLine();
        Debug.Log($"[Shorts] Encoding {frameCount} frames to {mp4} ...");
    }

    static string FindFfmpeg()
    {
        foreach (var candidate in new[] { "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" })
            if (File.Exists(candidate)) return candidate;
        foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir, "ffmpeg");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

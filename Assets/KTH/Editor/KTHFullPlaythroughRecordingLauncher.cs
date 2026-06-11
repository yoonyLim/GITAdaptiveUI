using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KTHFullPlaythroughRecordingLauncher
{
    private static string outputDir;
    private static double startTime;

    public static void Run()
    {
        outputDir = ReadArgument("-kthOutputDir");
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "unity_recordings", "full_playthrough");
        }

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "started.txt"), DateTime.Now.ToString("O"));

        startTime = EditorApplication.timeSinceStartup;
        EditorSceneManager.OpenScene("Assets/Scenes/AdaptivePrototype.unity", OpenSceneMode.Single);
        CreateRecorderBeforePlayMode();
        EditorApplication.update += Watchdog;
        EditorApplication.isPlaying = true;
    }

    private static void CreateRecorderBeforePlayMode()
    {
        GameObject recorderObject = new GameObject("KTH Full Playthrough Recorder");
        KTHFullPlaythroughRecorder recorder = recorderObject.AddComponent<KTHFullPlaythroughRecorder>();
        recorder.outputDir = outputDir;
        recorder.captureFps = 15;
        recorder.captureWidth = 1280;
        recorder.captureHeight = 720;
        recorder.maxPlaySeconds = ReadFloatArgument("-kthMaxPlaySeconds", 120f);
        recorder.contextShowcaseHoldSeconds = ReadFloatArgument("-kthContextHoldSeconds", 2.25f);
        recorder.stopAfterContextShowcase = IsTrueArgument("-kthContextShowcaseOnly");
    }

    private static void Watchdog()
    {
        if (EditorApplication.timeSinceStartup - startTime > 300.0)
        {
            File.WriteAllText(Path.Combine(outputDir, "error.txt"), "Full playthrough recording timed out.");
            EditorApplication.Exit(1);
        }
    }

    private static bool IsTrueArgument(string name)
    {
        string value = ReadArgument(name);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static float ReadFloatArgument(string name, float defaultValue)
    {
        return float.TryParse(ReadArgument(name), out float value) && value > 0f
            ? value
            : defaultValue;
    }

    private static string ReadArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return "";
    }
}

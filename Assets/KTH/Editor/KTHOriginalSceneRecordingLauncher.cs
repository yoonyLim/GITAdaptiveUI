using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KTHOriginalSceneRecordingLauncher
{
    private static string outputDir;
    private static double startTime;

    public static void Run()
    {
        outputDir = ReadArgument("-kthOutputDir");
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "unity_recordings", "original_scene_gaussian_runtime");
        }

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "started.txt"), DateTime.Now.ToString("O"));

        startTime = EditorApplication.timeSinceStartup;

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        CreateRecorderBeforePlayMode();
        EditorApplication.update += Watchdog;
        EditorApplication.isPlaying = true;
    }

    private static void CreateRecorderBeforePlayMode()
    {
        GameObject recorderObject = new GameObject("KTH Original Scene Play Recorder");
        KTHOriginalScenePlayRecorder recorder = recorderObject.AddComponent<KTHOriginalScenePlayRecorder>();
        recorder.outputDir = outputDir;
        recorder.captureFps = 15;
        recorder.captureWidth = 1280;
        recorder.captureHeight = 720;
    }

    private static void Watchdog()
    {
        if (EditorApplication.timeSinceStartup - startTime > 180.0)
        {
            File.WriteAllText(Path.Combine(outputDir, "error.txt"), "Recording timed out.");
            EditorApplication.Exit(1);
        }
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

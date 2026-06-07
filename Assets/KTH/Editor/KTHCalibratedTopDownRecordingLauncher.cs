using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KTHCalibratedTopDownRecordingLauncher
{
    private static string outputDir;
    private static double startTime;

    public static void Run()
    {
        outputDir = ReadArgument("-kthOutputDir");
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "unity_recordings", "calibrated_topdown_runtime");
        }

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "started.txt"), DateTime.Now.ToString("O"));

        startTime = EditorApplication.timeSinceStartup;

        string scenePath = KTHCalibratedTopDownSceneBuilder.ScenePath;
        if (!File.Exists(scenePath))
        {
            KTHCalibratedTopDownSceneBuilder.CreateScene();
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        CreateRecorderBeforePlayMode();
        EditorApplication.update += Watchdog;
        EditorApplication.isPlaying = true;
    }

    private static void CreateRecorderBeforePlayMode()
    {
        GameObject recorderObject = new GameObject("KTH Calibrated TopDown Flow Recorder");
        KTHCalibratedTopDownFlowRecorder recorder = recorderObject.AddComponent<KTHCalibratedTopDownFlowRecorder>();
        recorder.outputDir = outputDir;
        recorder.captureFps = 15;
        recorder.captureWidth = 1280;
        recorder.captureHeight = 720;
    }

    private static void Watchdog()
    {
        if (EditorApplication.timeSinceStartup - startTime > 240.0)
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

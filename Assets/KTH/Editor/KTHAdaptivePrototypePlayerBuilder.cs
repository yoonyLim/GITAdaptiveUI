using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class KTHAdaptivePrototypePlayerBuilder
{
    private const string ScenePath = "Assets/Scenes/AdaptivePrototype.unity";
    private const string AndroidApplicationId = "com.kth.adaptiveui";

    public static void BuildWindows()
    {
        string buildDir = ReadArgument("-kthBuildDir");
        if (string.IsNullOrEmpty(buildDir))
        {
            buildDir = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "unity_builds", "adaptive_prototype_recorder");
        }

        Directory.CreateDirectory(buildDir);
        string exePath = Path.Combine(buildDir, "AdaptivePrototypeRecording.exe");

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"AdaptivePrototype player build failed: {report.summary.result}");
        }

        File.WriteAllText(Path.Combine(buildDir, "build_done.txt"), exePath);
        UnityEngine.Debug.Log($"[KTH] Built AdaptivePrototype player at {exePath}");
    }

    public static void BuildAndroid()
    {
        string buildDir = ReadArgument("-kthBuildDir");
        if (string.IsNullOrEmpty(buildDir))
        {
            buildDir = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "android_builds", "adaptive_prototype_android");
        }

        Directory.CreateDirectory(buildDir);
        string apkPath = ReadArgument("-kthApkPath");
        if (string.IsNullOrEmpty(apkPath))
        {
            apkPath = Path.Combine(buildDir, "AdaptivePrototype.apk");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(apkPath) ?? buildDir);
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, AndroidApplicationId);
        PlayerSettings.Android.bundleVersionCode = 1;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"AdaptivePrototype Android build failed: {report.summary.result}");
        }

        File.WriteAllText(Path.Combine(buildDir, "android_build_done.txt"), apkPath);
        UnityEngine.Debug.Log($"[KTH] Built AdaptivePrototype Android APK at {apkPath}");
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

using System;
using System.IO;
using UnityEngine;

public class MultigameVisionPriorConfig : MonoBehaviour
{
    public string streamingAssetsRelativePath = "ADUI/multigame_vision_prior_config.json";
    public string source = "unity_default_no_vision_prior";
    [Range(0f, 1f)] public float confidenceThreshold = 0.35f;
    public float temperature = 1f;

    [Header("Prior table")]
    public Vector2 safeLikeEngage = new Vector2(0.85f, 0.15f);
    public Vector2 warningButEngage = new Vector2(0.65f, 0.35f);
    public Vector2 avoidOrActiveThreat = new Vector2(0.15f, 0.85f);
    public Vector2 criticalThreat = new Vector2(0.05f, 0.95f);
    public Vector2 unknownOrLowConfidence = new Vector2(0.5f, 0.5f);

    [TextArea]
    public string warning = "Vision prior predicts abstract situation only. It must not directly execute Attack or Dodge.";

    public bool LoadConfig()
    {
        var persistentPath = Path.Combine(Application.persistentDataPath, "adui_public_defaults", "multigame_vision_prior_config.json");
        var streamingPath = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
        var path = File.Exists(persistentPath) ? persistentPath : streamingPath;
        if (!File.Exists(path)) return false;

        var json = File.ReadAllText(path);
        var config = JsonUtility.FromJson<MultigameVisionPriorConfigJson>(json);
        if (config == null) return false;

        source = string.IsNullOrEmpty(config.source) ? source : config.source;
        confidenceThreshold = config.confidence_threshold > 0f ? config.confidence_threshold : confidenceThreshold;
        temperature = config.temperature > 0f ? config.temperature : temperature;
        return true;
    }

    [Serializable]
    private class MultigameVisionPriorConfigJson
    {
        public string source;
        public float confidence_threshold;
        public float temperature;
    }
}


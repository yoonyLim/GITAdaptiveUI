using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VisionPriorReplayLoader : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;
    public string replayFileName = "vision_prior_predictions.jsonl";
    private readonly Dictionary<int, ADUIVisionSituation> predictionsByTrial = new Dictionary<int, ADUIVisionSituation>();

    public int Count => predictionsByTrial.Count;

    public bool LoadReplay()
    {
        predictionsByTrial.Clear();
        if (!sessionManager) return false;
        var sessionDir = sessionManager.EnsureSession();
        var path = Path.Combine(sessionDir, replayFileName);
        if (!File.Exists(path)) return false;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var row = JsonUtility.FromJson<VisionPriorPredictionJson>(line);
            if (row == null) continue;
            predictionsByTrial[row.trial_id] = new ADUIVisionSituation
            {
                threatLevel = ParseThreat(row.pred_threat_level),
                actionWindow = ParseActionWindow(row.pred_action_window),
                confidence = row.scene_confidence,
                source = string.IsNullOrEmpty(row.source) ? "offline_replay" : row.source
            };
        }
        return predictionsByTrial.Count > 0;
    }

    public bool TryGetSituation(int trialId, out ADUIVisionSituation situation)
    {
        return predictionsByTrial.TryGetValue(trialId, out situation);
    }

    private ADUIThreatLevel ParseThreat(string value)
    {
        switch ((value ?? "").ToLowerInvariant())
        {
            case "none": return ADUIThreatLevel.None;
            case "warning": return ADUIThreatLevel.Warning;
            case "active": return ADUIThreatLevel.Active;
            case "critical": return ADUIThreatLevel.Critical;
            default: return ADUIThreatLevel.Unknown;
        }
    }

    private ADUIActionWindow ParseActionWindow(string value)
    {
        switch ((value ?? "").ToLowerInvariant())
        {
            case "engage": return ADUIActionWindow.Engage;
            case "avoid": return ADUIActionWindow.Avoid;
            case "wait": return ADUIActionWindow.Wait;
            case "explore": return ADUIActionWindow.Explore;
            default: return ADUIActionWindow.Unknown;
        }
    }

    [Serializable]
    private class VisionPriorPredictionJson
    {
        public int trial_id;
        public string pred_threat_level;
        public string pred_action_window;
        public float scene_confidence;
        public string source;
    }
}


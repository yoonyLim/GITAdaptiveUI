using UnityEngine;

public enum ADUIThreatLevel
{
    None,
    Warning,
    Active,
    Critical,
    Unknown
}

public enum ADUIActionWindow
{
    Engage,
    Avoid,
    Wait,
    Explore,
    Unknown
}

public struct ADUIVisionSituation
{
    public ADUIThreatLevel threatLevel;
    public ADUIActionWindow actionWindow;
    public float confidence;
    public string source;
}

public struct ADUIActionPrior
{
    public float attack;
    public float dodge;
    public string source;
    public string reason;
    public float confidence;
}

public class VisionPriorBuilder : MonoBehaviour
{
    public MultigameVisionPriorConfig config;

    public ADUIActionPrior BuildPrior(ADUIVisionSituation situation)
    {
        var cfg = config ? config : GetComponent<MultigameVisionPriorConfig>();
        var threshold = cfg ? cfg.confidenceThreshold : 0.35f;
        var confidence = Mathf.Clamp01(situation.confidence);
        var basePrior = new Vector2(0.5f, 0.5f);
        var reason = "unknown_or_low_confidence";

        if (confidence < threshold)
        {
            basePrior = cfg ? cfg.unknownOrLowConfidence : new Vector2(0.5f, 0.5f);
            reason = "low_confidence_neutral";
        }
        else if (situation.threatLevel == ADUIThreatLevel.Critical)
        {
            basePrior = cfg ? cfg.criticalThreat : new Vector2(0.05f, 0.95f);
            reason = "critical_threat";
        }
        else if (situation.actionWindow == ADUIActionWindow.Avoid || situation.threatLevel == ADUIThreatLevel.Active)
        {
            basePrior = cfg ? cfg.avoidOrActiveThreat : new Vector2(0.15f, 0.85f);
            reason = "avoid_or_active_threat";
        }
        else if (situation.actionWindow == ADUIActionWindow.Engage && situation.threatLevel == ADUIThreatLevel.Warning)
        {
            basePrior = cfg ? cfg.warningButEngage : new Vector2(0.65f, 0.35f);
            reason = "warning_but_engage";
        }
        else if (situation.actionWindow == ADUIActionWindow.Engage && situation.threatLevel == ADUIThreatLevel.None)
        {
            basePrior = cfg ? cfg.safeLikeEngage : new Vector2(0.85f, 0.15f);
            reason = "safe_like_engage";
        }

        var attack = confidence * basePrior.x + (1f - confidence) * 0.5f;
        var dodge = confidence * basePrior.y + (1f - confidence) * 0.5f;
        var total = Mathf.Max(attack + dodge, 0.0001f);
        return new ADUIActionPrior
        {
            attack = attack / total,
            dodge = dodge / total,
            source = string.IsNullOrEmpty(situation.source) ? "multigame_vision_prior" : situation.source,
            reason = reason,
            confidence = confidence
        };
    }
}


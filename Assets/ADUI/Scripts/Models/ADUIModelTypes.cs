using System;
using UnityEngine;

public enum ADUIAction
{
    None,
    Attack,
    Dodge
}

public enum ADUIEnemyState
{
    Safe,
    Telegraph,
    Attacking,
    Urgent,
    Idle,
    Neutral
}

public enum ADUIInteractionMode
{
    ActionFirst,
    CognitiveFirst,
    GuidanceProcedure,
    LearningReview
}

public enum ADUIFeedbackLevel
{
    None,
    Subtle,
    Standard,
    Strong
}

[Serializable]
public class ADUIInteractionDemand
{
    [Range(0f, 1f)] public float actionIntensity;
    [Range(0f, 1f)] public float temporalUrgency;
    [Range(0f, 1f)] public float informationPriority;
    [Range(0f, 1f)] public float occlusionRisk;
    [Range(0f, 1f)] public float controlContinuity;
    [Range(0f, 1f)] public float uiSkill;

    public ADUIInteractionMode mode = ADUIInteractionMode.ActionFirst;

    public float ErrorToleranceNeed => Mathf.Clamp01((actionIntensity + temporalUrgency + controlContinuity + (1f - uiSkill)) * 0.25f);
    public float CognitiveNeed => Mathf.Clamp01((informationPriority + occlusionRisk) * 0.5f);
}

[Serializable]
public class ADUIAdjustmentPolicy
{
    public ADUIInteractionMode mode = ADUIInteractionMode.ActionFirst;
    [Range(0f, 1f)] public float visibility = 1f;
    [Range(0f, 1f)] public float emphasis = 0.6f;
    [Range(0f, 1f)] public float density = 0.5f;
    [Range(0f, 1f)] public float positionConstraint = 0.8f;
    [Range(0f, 1f)] public float interactionErrorTolerance = 0.6f;
    [Range(0f, 1f)] public float feedbackIntensity = 0.5f;
    [Range(0f, 1f)] public float correctionStrength = 0.5f;
    public float hitboxExpansionRatio = 1.25f;
    public float ambiguityMarginPx = 60f;
    public bool preserveClearInput = true;
    public bool hapticEnabled = false;
    public bool showGuidance = false;
    public bool showReview = false;
    public string policyReason = "";
}

[Serializable]
public class ADUIButtonGeometry
{
    public string action = "";
    public float centerX;
    public float centerY;
    public float visualRadius = 100f;
    public float hitboxRadius = 140f;

    public Vector2 Center => new Vector2(centerX, centerY);

    public static ADUIButtonGeometry FromRect(string actionName, RectTransform rectTransform, float hitboxExpansionRatio)
    {
        var size = rectTransform.rect.size;
        var radius = Mathf.Max(size.x, size.y) * 0.5f * rectTransform.lossyScale.x;
        return new ADUIButtonGeometry
        {
            action = actionName,
            centerX = rectTransform.position.x,
            centerY = rectTransform.position.y,
            visualRadius = Mathf.Max(radius, 1f),
            hitboxRadius = Mathf.Max(radius * Mathf.Max(hitboxExpansionRatio, 1f), 1f)
        };
    }
}

[Serializable]
public class ADUIDecodeInput
{
    public Vector2 touchPosition;
    public ADUIButtonGeometry attackButton;
    public ADUIButtonGeometry dodgeButton;
    public ADUIEnemyState enemyState = ADUIEnemyState.Neutral;
    public string condition = "context_bayesian_safety";
}

[Serializable]
public class ADUIDecodeResult
{
    public float likelihoodAttack;
    public float likelihoodDodge;
    public float priorAttack;
    public float priorDodge;
    public float posteriorAttack;
    public float posteriorDodge;
    public float posteriorGap;
    public float maxPosterior;
    public float tau;
    public float delta;
    public float varianceAttack;
    public float varianceDodge;
    public float priorStrength;
    public string publicPriorSource = "";
    public string publicVarianceSource = "";
    public ADUIAction visualBoundaryPrediction = ADUIAction.None;
    public ADUIAction expandedHitboxPrediction = ADUIAction.None;
    public ADUIAction userGaussianPrediction = ADUIAction.None;
    public ADUIAction contextPriorOnlyPrediction = ADUIAction.None;
    public ADUIAction bayesianPrediction = ADUIAction.None;
    public ADUIAction finalExecutedAction = ADUIAction.None;
    public bool invalidTouch;
    public bool safetyGatePassed;
    public string safetyGateReason = "";
    public bool isNearBoundary;
    public bool isAmbiguous;
    public float distanceToAttack;
    public float distanceToDodge;
}

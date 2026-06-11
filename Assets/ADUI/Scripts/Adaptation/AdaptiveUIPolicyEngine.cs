using UnityEngine;

public class AdaptiveUIPolicyEngine : MonoBehaviour
{
    [Header("Action First")]
    public float actionFirstHitboxExpansion = 1.35f;
    public float actionFirstCorrectionStrength = 0.65f;
    public float actionFirstFeedbackIntensity = 0.65f;

    [Header("Cognitive First")]
    public float cognitiveVisibility = 0.85f;
    public float cognitiveDensity = 0.35f;
    public float cognitivePositionConstraint = 0.95f;

    [Header("Guidance / Procedure")]
    public float guidanceEmphasis = 0.85f;
    public float guidanceFeedbackIntensity = 0.8f;

    [Header("Learning / Review")]
    public float reviewDensity = 0.7f;
    public float reviewFeedbackIntensity = 0.45f;

    public ADUIAdjustmentPolicy BuildPolicy(ADUIInteractionDemand demand)
    {
        ADUIInteractionMode runtimeMode = RuntimeMode(demand.mode);
        float errorTolerance = runtimeMode == ADUIInteractionMode.ActionFirst
            ? Mathf.Clamp01(Mathf.Max(demand.ErrorToleranceNeed, 0.55f + demand.temporalUrgency * 0.25f))
            : Mathf.Clamp01(Mathf.Min(demand.ErrorToleranceNeed, 0.32f + demand.CognitiveNeed * 0.12f));

        var policy = new ADUIAdjustmentPolicy
        {
            mode = runtimeMode,
            visibility = 1f,
            emphasis = 0.55f,
            density = 0.5f,
            positionConstraint = 0.8f,
            interactionErrorTolerance = errorTolerance,
            feedbackIntensity = 0.5f,
            correctionStrength = 0.5f,
            hitboxExpansionRatio = 1.25f,
            ambiguityMarginPx = Mathf.Lerp(34f, 96f, errorTolerance),
            preserveClearInput = true,
            hapticEnabled = false,
            showGuidance = false,
            showReview = false
        };

        switch (runtimeMode)
        {
            case ADUIInteractionMode.ActionFirst:
                policy.visibility = 1f;
                policy.emphasis = Mathf.Lerp(0.68f, 0.94f, Mathf.Max(demand.temporalUrgency, demand.actionIntensity));
                policy.density = Mathf.Lerp(0.32f, 0.46f, demand.temporalUrgency);
                policy.positionConstraint = 0.92f;
                policy.feedbackIntensity = actionFirstFeedbackIntensity;
                policy.correctionStrength = Mathf.Clamp01(Mathf.Max(actionFirstCorrectionStrength, 0.62f + demand.temporalUrgency * 0.18f));
                policy.hitboxExpansionRatio = Mathf.Lerp(1.18f, actionFirstHitboxExpansion, errorTolerance);
                policy.hapticEnabled = demand.temporalUrgency >= 0.75f;
                policy.policyReason = "action_first: high urgency or action density allows stronger ambiguity correction";
                break;

            case ADUIInteractionMode.CognitiveFirst:
                policy.visibility = Mathf.Clamp01(Mathf.Max(cognitiveVisibility, 0.88f + demand.informationPriority * 0.1f));
                policy.emphasis = Mathf.Lerp(0.46f, 0.68f, demand.CognitiveNeed);
                policy.density = Mathf.Lerp(cognitiveDensity, 0.58f, demand.CognitiveNeed);
                policy.positionConstraint = Mathf.Clamp(cognitivePositionConstraint, 0.94f, 1f);
                policy.feedbackIntensity = 0.35f;
                policy.correctionStrength = Mathf.Lerp(0.22f, 0.4f, errorTolerance);
                policy.hitboxExpansionRatio = Mathf.Lerp(1.04f, 1.14f, errorTolerance);
                policy.ambiguityMarginPx = Mathf.Lerp(28f, 52f, errorTolerance);
                policy.policyReason = "cognitive_first: preserve direct intent and prioritize readable combat information";
                break;

            case ADUIInteractionMode.GuidanceProcedure:
                policy.visibility = 1f;
                policy.emphasis = guidanceEmphasis;
                policy.density = 0.45f;
                policy.positionConstraint = 0.85f;
                policy.feedbackIntensity = guidanceFeedbackIntensity;
                policy.correctionStrength = 0.45f;
                policy.hitboxExpansionRatio = 1.2f;
                policy.showGuidance = true;
                policy.hapticEnabled = true;
                policy.policyReason = "guidance_procedure: repeated uncertainty asks for visible guidance";
                break;

            case ADUIInteractionMode.LearningReview:
                policy.visibility = 0.9f;
                policy.emphasis = 0.45f;
                policy.density = reviewDensity;
                policy.positionConstraint = 0.75f;
                policy.feedbackIntensity = reviewFeedbackIntensity;
                policy.correctionStrength = 0.25f;
                policy.hitboxExpansionRatio = 1.1f;
                policy.showReview = true;
                policy.policyReason = "learning_review: low-pressure state can expose review feedback";
                break;
        }

        return policy;
    }

    private ADUIInteractionMode RuntimeMode(ADUIInteractionMode mode)
    {
        return mode == ADUIInteractionMode.CognitiveFirst ||
               mode == ADUIInteractionMode.LearningReview
            ? ADUIInteractionMode.CognitiveFirst
            : ADUIInteractionMode.ActionFirst;
    }
}


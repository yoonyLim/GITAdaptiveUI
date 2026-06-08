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
        var policy = new ADUIAdjustmentPolicy
        {
            mode = demand.mode,
            visibility = 1f,
            emphasis = 0.55f,
            density = 0.5f,
            positionConstraint = 0.8f,
            interactionErrorTolerance = demand.ErrorToleranceNeed,
            feedbackIntensity = 0.5f,
            correctionStrength = 0.5f,
            hitboxExpansionRatio = 1.25f,
            ambiguityMarginPx = Mathf.Lerp(40f, 90f, demand.ErrorToleranceNeed),
            preserveClearInput = true,
            hapticEnabled = false,
            showGuidance = false,
            showReview = false
        };

        switch (demand.mode)
        {
            case ADUIInteractionMode.ActionFirst:
                policy.visibility = 1f;
                policy.emphasis = Mathf.Lerp(0.6f, 0.9f, demand.temporalUrgency);
                policy.density = 0.35f;
                policy.positionConstraint = 0.9f;
                policy.feedbackIntensity = actionFirstFeedbackIntensity;
                policy.correctionStrength = actionFirstCorrectionStrength;
                policy.hitboxExpansionRatio = Mathf.Lerp(1.15f, actionFirstHitboxExpansion, demand.ErrorToleranceNeed);
                policy.hapticEnabled = demand.temporalUrgency >= 0.75f;
                policy.policyReason = "action_first: urgency/action density requires conservative error tolerance";
                break;

            case ADUIInteractionMode.CognitiveFirst:
                policy.visibility = cognitiveVisibility;
                policy.emphasis = 0.5f;
                policy.density = cognitiveDensity;
                policy.positionConstraint = cognitivePositionConstraint;
                policy.feedbackIntensity = 0.35f;
                policy.correctionStrength = 0.35f;
                policy.hitboxExpansionRatio = 1.15f;
                policy.policyReason = "cognitive_first: reduce clutter and protect information visibility";
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
}


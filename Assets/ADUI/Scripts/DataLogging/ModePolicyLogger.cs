using UnityEngine;

public class ModePolicyLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    public void Log(int trialId, ADUIInteractionDemand demand, ADUIAdjustmentPolicy policy)
    {
        if (!sessionManager || !sessionManager.exporter || demand == null || policy == null) return;
        var record = new ADUIModePolicyRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            interaction_mode = policy.mode.ToString(),
            demand_action_intensity = demand.actionIntensity,
            demand_temporal_urgency = demand.temporalUrgency,
            demand_information_priority = demand.informationPriority,
            demand_occlusion_risk = demand.occlusionRisk,
            demand_control_continuity = demand.controlContinuity,
            demand_ui_skill = demand.uiSkill,
            policy_visibility = policy.visibility,
            policy_emphasis = policy.emphasis,
            policy_density = policy.density,
            policy_position_constraint = policy.positionConstraint,
            policy_error_tolerance = policy.interactionErrorTolerance,
            policy_feedback_intensity = policy.feedbackIntensity,
            policy_correction_strength = policy.correctionStrength,
            policy_hitbox_expansion_ratio = policy.hitboxExpansionRatio,
            policy_ambiguity_margin_px = policy.ambiguityMarginPx,
            policy_preserve_clear_input = policy.preserveClearInput,
            policy_haptic_enabled = policy.hapticEnabled,
            policy_guidance_visible = policy.showGuidance,
            policy_review_visible = policy.showReview,
            policy_reason = policy.policyReason
        };
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "mode_policy_events.jsonl", record);
    }
}

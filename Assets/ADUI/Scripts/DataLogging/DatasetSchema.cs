using System;

public static class DatasetSchema
{
    public const string PhaseCalibration = "calibration";
    public const string PhaseTrain = "train";
    public const string PhaseValid = "valid";
    public const string PhaseTest = "test";
    public const string PhaseFreeplay = "freeplay";

    public const string ConditionVisualBoundary = "visual_boundary";
    public const string ConditionExpandedHitbox = "expanded_hitbox";
    public const string ConditionUserGaussian = "user_gaussian";
    public const string ConditionContextPriorOnly = "context_prior_only";
    public const string ConditionContextBayesianNoSafety = "context_bayesian_no_safety";
    public const string ConditionContextBayesianSafety = "context_bayesian_safety";
    public const string ConditionRawButton = "raw_button";
    public const string ConditionNoCalibrationContextBayesian = "no_calibration_context_bayesian";
    public const string ConditionCalibratedContextBayesian = "calibrated_context_bayesian";

    public static readonly string[] Conditions =
    {
        ConditionVisualBoundary,
        ConditionExpandedHitbox,
        ConditionUserGaussian,
        ConditionContextPriorOnly,
        ConditionContextBayesianNoSafety,
        ConditionContextBayesianSafety
    };
}

[Serializable]
public class ADUISessionMeta
{
    public string session_id = "";
    public string participant_id = "";
    public string device_model = "";
    public string platform = "";
    public int screen_width;
    public int screen_height;
    public float dpi;
    public string unity_version = "";
    public string app_version = "";
    public string timestamp_start = "";
    public string handedness = "";
    public string notes = "";
}

[Serializable]
public class ADUISessionFinalSummary
{
    public string session_id = "";
    public string participant_id = "";
    public string condition = "";
    public string completion_state = "";
    public int current_stage;
    public bool prototype_complete;
    public bool prototype_failed;
    public string timestamp_end = "";
    public string final_bundle_file = "";
}

[Serializable]
public class ADUICalibrationEventRecord
{
    public string session_id = "";
    public string participant_id = "";
    public string condition = "";
    public long timestamp_ms;
    public string event_type = "";
    public int trial_index;
    public int total_trials;
    public string trial_type = "";
    public string target_action = "";
    public string scenario = "";
    public string instruction = "";
    public bool accepted;
    public bool use_for_model;
    public bool affects_center_bias;
    public bool is_validation;
    public bool is_combat_scenario;
    public float touch_x;
    public float touch_y;
    public string resolved_action = "";
    public bool direct_hit;
    public float confidence;
    public string model_effect = "";
    public string message = "";
}

[Serializable]
public class ADUIShowcaseEventRecord
{
    public string session_id = "";
    public string participant_id = "";
    public string condition = "";
    public long timestamp_ms;
    public string event_type = "";
    public string scenario = "";
    public string mode = "";
    public bool live_combat;
    public int active_enemies;
    public int player_hp;
    public bool stage_running;
}

[Serializable]
public class ADUITrialRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public int block_id;
    public string phase = "";
    public string condition = "";
    public string interaction_mode = "";
    public long timestamp_trial_start_ms;
    public long timestamp_touch_ms;
    public long timestamp_action_ms;
    public long timestamp_trial_end_ms;

    public int screen_width;
    public int screen_height;
    public float attack_center_x;
    public float attack_center_y;
    public float attack_visual_radius;
    public float attack_hitbox_radius;
    public float dodge_center_x;
    public float dodge_center_y;
    public float dodge_visual_radius;
    public float dodge_hitbox_radius;
    public float dynamic_attack_radius;
    public float dynamic_dodge_radius;

    public string enemy_state = "";
    public bool danger_warning_visible;
    public float enemy_distance;
    public int player_hp_before;
    public int enemy_hp_before;
    public float cooldown_attack;
    public float cooldown_dodge;

    public string required_action = "";
    public string intended_action = "";
    public string label_source = "";
    public string trial_type = "";
    public string calibration_instruction = "";
    public bool calibration_used_for_touch_model;

    public float touch_x;
    public float touch_y;
    public string touch_phase = "";
    public float touch_pressure;
    public float touch_radius;
    public float distance_to_attack;
    public float distance_to_dodge;
    public float relative_attack_x;
    public float relative_attack_y;
    public float relative_dodge_x;
    public float relative_dodge_y;
    public float intended_center_x;
    public float intended_center_y;
    public float distance_to_intended;
    public float relative_intended_x;
    public float relative_intended_y;
    public bool is_inside_attack_visual;
    public bool is_inside_dodge_visual;
    public bool is_inside_attack_expanded;
    public bool is_inside_dodge_expanded;
    public bool is_near_boundary;
    public bool is_ambiguous;

    public float likelihood_attack;
    public float likelihood_dodge;
    public float prior_attack;
    public float prior_dodge;
    public float posterior_attack;
    public float posterior_dodge;
    public float posterior_gap;
    public float max_posterior;
    public float tau;
    public float delta;
    public float variance_attack;
    public float variance_dodge;
    public float prior_strength;
    public string public_prior_source = "";
    public string public_variance_source = "";

    public string visual_boundary_prediction = "";
    public string expanded_hitbox_prediction = "";
    public string user_gaussian_prediction = "";
    public string context_prior_only_prediction = "";
    public string bayesian_prediction = "";
    public string final_executed_action = "";
    public bool invalid_touch;
    public bool safety_gate_passed;
    public string safety_gate_reason = "";

    public bool action_success;
    public int hp_after;
    public int enemy_hp_after;
    public int damage_taken;
    public int damage_dealt;
    public bool survived;
    public bool cooldown_wasted;
    public float reaction_time_ms;
    public string feedback_type = "";
    public string button_feedback_color = "";
    public string feedback_message = "";
    public bool haptic_feedback_triggered;
    public bool hitbox_visualization_enabled;

    public float demand_action_intensity;
    public float demand_temporal_urgency;
    public float demand_information_priority;
    public float demand_occlusion_risk;
    public float demand_control_continuity;
    public float demand_ui_skill;
    public float policy_visibility;
    public float policy_emphasis;
    public float policy_density;
    public float policy_position_constraint;
    public float policy_error_tolerance;
    public float policy_feedback_intensity;
    public float policy_correction_strength;
    public float policy_hitbox_expansion_ratio;
    public float policy_ambiguity_margin_px;
    public bool policy_preserve_clear_input;
    public bool policy_haptic_enabled;
    public bool policy_guidance_visible;
    public bool policy_review_visible;
    public string policy_reason = "";
    public bool user_correction_enabled;
    public float user_correction_strength;
}

[Serializable]
public class ADUIRawTouchEvent
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_touch_ms;
    public float touch_x;
    public float touch_y;
    public string touch_phase = "";
    public float touch_pressure;
    public float touch_radius;
    public int finger_id;
}

[Serializable]
public class ADUIModelDecisionRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_ms;
    public string condition = "";
    public string enemy_state = "";
    public float likelihood_attack;
    public float likelihood_dodge;
    public float prior_attack;
    public float prior_dodge;
    public float posterior_attack;
    public float posterior_dodge;
    public float posterior_gap;
    public string bayesian_prediction = "";
    public string final_executed_action = "";
    public bool invalid_touch;
    public bool safety_gate_passed;
    public string safety_gate_reason = "";
}

[Serializable]
public class ADUIHPOutcomeRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_ms;
    public int player_hp_before;
    public int player_hp_after;
    public int enemy_hp_before;
    public int enemy_hp_after;
    public int damage_taken;
    public int damage_dealt;
    public bool survived;
    public bool action_success;
}

[Serializable]
public class ADUILayoutSnapshotRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_ms;
    public int screen_width;
    public int screen_height;
    public float attack_center_x;
    public float attack_center_y;
    public float attack_visual_radius;
    public float attack_hitbox_radius;
    public float dodge_center_x;
    public float dodge_center_y;
    public float dodge_visual_radius;
    public float dodge_hitbox_radius;
    public float dynamic_attack_radius;
    public float dynamic_dodge_radius;
}

[Serializable]
public class ADUIConditionOrder
{
    public string session_id = "";
    public string participant_id = "";
    public string[] condition_order;
}

[Serializable]
public class ADUISurveyRecord
{
    public string session_id = "";
    public string participant_id = "";
    public long timestamp_ms;
    public int trial_id;
    public int trust_score = -1;
    public int control_score = -1;
    public int predictability_score = -1;
    public string free_text = "";
    public string source = "";
}

[Serializable]
public class ADUIModePolicyRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_ms;
    public string interaction_mode = "";
    public float demand_action_intensity;
    public float demand_temporal_urgency;
    public float demand_information_priority;
    public float demand_occlusion_risk;
    public float demand_control_continuity;
    public float demand_ui_skill;
    public float policy_visibility;
    public float policy_emphasis;
    public float policy_density;
    public float policy_position_constraint;
    public float policy_error_tolerance;
    public float policy_feedback_intensity;
    public float policy_correction_strength;
    public float policy_hitbox_expansion_ratio;
    public float policy_ambiguity_margin_px;
    public bool policy_preserve_clear_input;
    public bool policy_haptic_enabled;
    public bool policy_guidance_visible;
    public bool policy_review_visible;
    public string policy_reason = "";
}

[Serializable]
public class ADUIVisionFrameRecord
{
    public string session_id = "";
    public string participant_id = "";
    public int trial_id;
    public long timestamp_ms;
    public string image_path = "";
    public int screen_width;
    public int screen_height;
    public string enemy_state = "";
    public string attack_bbox = "";
    public string dodge_bbox = "";
    public string label_source = "";
}

[Serializable]
public class ADUIEvaluationTouchRecord
{
    public string session_id = "";
    public string participant_id = "";
    public string condition = "";
    public int stage_number;
    public string stage_label = "";
    public int trial_id;
    public long timestamp_ms;
    public string scenario = "";
    public string interaction_mode = "";
    public string expected_action = "";
    public string final_action = "";
    public bool expected_match;
    public bool invalid_touch;
    public bool rejected;
    public bool preserved_clear_input;
    public bool corrected;
    public bool ambiguous;
    public bool cooldown_wasted;
    public bool safety_gate_passed;
    public string safety_gate_reason = "";
    public float posterior_attack;
    public float posterior_dodge;
    public float posterior_gap;
    public float max_posterior;
    public float policy_error_tolerance;
    public float policy_correction_strength;
    public float touch_x;
    public float touch_y;
    public int player_hp_before;
    public int player_hp_after;
    public int damage_taken;
    public int enemy_hp_before;
    public int enemy_hp_after;
    public int damage_dealt;
}

[Serializable]
public class ADUIEvaluationStageSummary
{
    public string session_id = "";
    public string participant_id = "";
    public string condition = "";
    public int stage_number;
    public string stage_label = "";
    public bool skipped;
    public bool failed;
    public float duration_seconds;
    public int button_presses;
    public int touch_events;
    public int expected_match_count;
    public int mis_touch_count;
    public int invalid_touch_count;
    public int rejected_count;
    public int preserved_count;
    public int corrected_count;
    public int ambiguous_count;
    public int cooldown_wasted_count;
    public int action_first_count;
    public int cognitive_first_count;
    public int damage_taken;
    public int healing_done;
    public int final_hp;
    public int enemies_remaining;
    public float avg_touch_error_px;
    public float avg_posterior_gap;
    public float avg_policy_error_tolerance;
    public float avg_policy_correction_strength;
}

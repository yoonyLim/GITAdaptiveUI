# Unity Dataset Protocol

Unity 로그의 기본 저장 위치는 `Application.persistentDataPath/adui_sessions/<session_id>/`이다. 세션마다 다음 파일을 생성한다.

- `session_meta.json`
- `calibration_trials.jsonl`
- `main_trials.jsonl`
- `raw_touch_events.jsonl`
- `model_decisions.jsonl`
- `hp_outcomes.jsonl`
- `ui_layout_snapshots.jsonl`
- `mode_policy_events.jsonl`
- `trust_control_survey.jsonl`
- `condition_order.json`

JSONL이 기본 포맷이며 CSV export도 지원한다. 핵심 trial schema는 participant/session, phase/condition, UI layout, enemy state, labels, raw touch, likelihood/prior/posterior, baseline predictions, safety gate, HP outcome을 포함한다.

추가 schema는 interaction mode와 policy도 포함한다.

- `interaction_mode`
- `demand_action_intensity`, `demand_temporal_urgency`, `demand_information_priority`, `demand_occlusion_risk`, `demand_control_continuity`, `demand_ui_skill`
- `policy_visibility`, `policy_emphasis`, `policy_density`, `policy_position_constraint`
- `policy_error_tolerance`, `policy_correction_strength`, `policy_hitbox_expansion_ratio`, `policy_ambiguity_margin_px`
- `policy_haptic_enabled`, `policy_guidance_visible`, `policy_review_visible`
- `user_correction_enabled`, `user_correction_strength`
- `feedback_message`, `haptic_feedback_triggered`

직접 검증은 `main_trials.jsonl`의 controlled trial에서 `required_action`, `final_executed_action`, posterior, safety gate, HP outcome을 함께 평가한다.

`trust_control_survey.jsonl`은 선택 설문이다. 실제 participant 응답이 있을 때만 신뢰/통제감 해석에 사용한다.

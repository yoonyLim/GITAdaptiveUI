from __future__ import annotations

import argparse
import base64
import csv
import json
import math
import random
import statistics
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ModuleNotFoundError:  # pragma: no cover - exercised in minimal environments
    plt = None

from analysis_unity.src.metrics import (
    accuracy,
    as_bool,
    correction_success_rate,
    decoder_metric_rows,
    invalid_touch_rate,
    mean_numeric,
    overcorrection_rate,
)
from analysis_unity.src.paths import (
    DEFAULT_SESSIONS_DIR,
    FIGURES_DIR,
    MODELS_DIR,
    PROCESSED_DIR,
    REPORTS_DIR,
    ensure_unity_dirs,
)
from analysis_unity.src.schemas import CONDITIONS, CONTROLLED_PHASES, TRIAL_FIELDS, VALID_LABEL_SOURCES


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str] | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if fieldnames is None:
        keys: list[str] = []
        for row in rows:
            for key in row:
                if key not in keys:
                    keys.append(key)
        fieldnames = keys or ["status"]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def read_csv(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [dict(row) for row in csv.DictReader(handle)]


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            rows.append(json.loads(line))
    return rows


def write_jsonl(path: Path, rows: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")


def placeholder_figure(path: Path, message: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if plt is None:
        tiny_png = (
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="
        )
        path.write_bytes(base64.b64decode(tiny_png))
        path.with_suffix(path.suffix + ".txt").write_text(message, encoding="utf-8")
        return
    plt.figure(figsize=(7, 4))
    plt.text(0.5, 0.5, message, ha="center", va="center", wrap=True)
    plt.axis("off")
    plt.tight_layout()
    plt.savefig(path, dpi=150)
    plt.close()


def nowish_ms(index: int) -> int:
    return 1_700_000_000_000 + index * 1_000


def prior_for_state(state: str) -> tuple[float, float]:
    if state in {"Safe", "Idle"}:
        return 0.9, 0.1
    if state == "Telegraph":
        return 0.1, 0.9
    if state in {"Attacking", "Urgent"}:
        return 0.05, 0.95
    return 0.5, 0.5


def fixture_policy_for_state(state: str, trial_index: int, phase: str) -> tuple[dict[str, Any], dict[str, Any]]:
    danger = state in {"Telegraph", "Attacking", "Urgent"}
    action_rate = 0.82 if danger else 0.34
    temporal_urgency = 0.92 if state in {"Attacking", "Urgent"} else 0.75 if state == "Telegraph" else 0.25
    information_priority = 0.8 if danger else 0.45
    occlusion_risk = min(1.0, 0.35 + action_rate * 0.35 + information_priority * 0.2)
    control_continuity = min(1.0, 0.4 + action_rate * 0.4)
    ui_skill = 0.72 if phase != "calibration" else 0.6

    if phase != "calibration" and trial_index % 19 == 0:
        mode = "CognitiveFirst"
        information_priority = 0.82
        occlusion_risk = 0.76
    elif temporal_urgency >= 0.65 or action_rate >= 0.7:
        mode = "ActionFirst"
    elif trial_index % 17 == 0:
        mode = "GuidanceProcedure"
        ui_skill = 0.32
    elif information_priority >= 0.65 or occlusion_risk >= 0.7:
        mode = "CognitiveFirst"
    else:
        mode = "LearningReview"

    error_tolerance = max(0.0, min(1.0, (action_rate + temporal_urgency + control_continuity + (1.0 - ui_skill)) * 0.25))
    demand = {
        "interaction_mode": mode,
        "demand_action_intensity": action_rate,
        "demand_temporal_urgency": temporal_urgency,
        "demand_information_priority": information_priority,
        "demand_occlusion_risk": occlusion_risk,
        "demand_control_continuity": control_continuity,
        "demand_ui_skill": ui_skill,
    }
    policy = {
        "policy_visibility": 1.0,
        "policy_emphasis": 0.55,
        "policy_density": 0.5,
        "policy_position_constraint": 0.8,
        "policy_error_tolerance": error_tolerance,
        "policy_feedback_intensity": 0.5,
        "policy_correction_strength": 0.5,
        "policy_hitbox_expansion_ratio": 1.25,
        "policy_ambiguity_margin_px": 40.0 + 50.0 * error_tolerance,
        "policy_preserve_clear_input": True,
        "policy_haptic_enabled": False,
        "policy_guidance_visible": False,
        "policy_review_visible": False,
        "policy_reason": "fixture_policy",
        "user_correction_enabled": True,
        "user_correction_strength": 0.65,
    }
    if mode == "ActionFirst":
        policy.update(
            {
                "policy_emphasis": 0.6 + 0.3 * temporal_urgency,
                "policy_density": 0.35,
                "policy_position_constraint": 0.9,
                "policy_feedback_intensity": 0.65,
                "policy_correction_strength": 0.65,
                "policy_hitbox_expansion_ratio": 1.15 + (1.35 - 1.15) * error_tolerance,
                "policy_haptic_enabled": temporal_urgency >= 0.75,
                "policy_reason": "action_first: urgency/action density requires conservative error tolerance",
            }
        )
    elif mode == "GuidanceProcedure":
        policy.update(
            {
                "policy_emphasis": 0.85,
                "policy_density": 0.45,
                "policy_position_constraint": 0.85,
                "policy_feedback_intensity": 0.8,
                "policy_correction_strength": 0.45,
                "policy_hitbox_expansion_ratio": 1.2,
                "policy_haptic_enabled": True,
                "policy_guidance_visible": True,
                "policy_reason": "guidance_procedure: repeated uncertainty asks for visible guidance",
            }
        )
    elif mode == "CognitiveFirst":
        policy.update(
            {
                "policy_visibility": 0.85,
                "policy_density": 0.35,
                "policy_position_constraint": 0.95,
                "policy_feedback_intensity": 0.35,
                "policy_correction_strength": 0.35,
                "policy_hitbox_expansion_ratio": 1.15,
                "policy_reason": "cognitive_first: reduce clutter and protect information visibility",
            }
        )
    elif mode == "LearningReview":
        policy.update(
            {
                "policy_visibility": 0.9,
                "policy_emphasis": 0.45,
                "policy_density": 0.7,
                "policy_position_constraint": 0.75,
                "policy_feedback_intensity": 0.45,
                "policy_correction_strength": 0.25,
                "policy_hitbox_expansion_ratio": 1.1,
                "policy_review_visible": True,
                "policy_reason": "learning_review: low-pressure state can expose review feedback",
            }
        )
    return demand, policy


def gaussian_likelihood(distance: float, variance: float) -> float:
    return math.exp(-(distance * distance) / (2 * max(variance, 1.0)))


def decode_touch(row: dict[str, Any]) -> dict[str, Any]:
    ax = float(row["attack_center_x"])
    ay = float(row["attack_center_y"])
    dx = float(row["dodge_center_x"])
    dy = float(row["dodge_center_y"])
    tx = float(row["touch_x"])
    ty = float(row["touch_y"])
    attack_radius = float(row["attack_visual_radius"])
    dodge_radius = float(row["dodge_visual_radius"])
    attack_hitbox = float(row["attack_hitbox_radius"])
    dodge_hitbox = float(row["dodge_hitbox_radius"])
    dist_a = math.hypot(tx - ax, ty - ay)
    dist_d = math.hypot(tx - dx, ty - dy)
    var_a = float(row.get("variance_attack") or 180.0**2)
    var_d = float(row.get("variance_dodge") or 180.0**2)
    like_a = gaussian_likelihood(dist_a, var_a)
    like_d = gaussian_likelihood(dist_d, var_d)
    prior_a, prior_d = prior_for_state(str(row.get("enemy_state", "Neutral")))
    score_a = like_a * prior_a
    score_d = like_d * prior_d
    total = score_a + score_d
    if total <= 0:
        post_a = 0.5
        post_d = 0.5
    else:
        post_a = score_a / total
        post_d = score_d / total
    visual = "None"
    if dist_a <= attack_radius and dist_d <= dodge_radius:
        visual = "Attack" if dist_a <= dist_d else "Dodge"
    elif dist_a <= attack_radius:
        visual = "Attack"
    elif dist_d <= dodge_radius:
        visual = "Dodge"
    expanded = "None"
    if dist_a <= attack_hitbox and dist_d <= dodge_hitbox:
        expanded = "Attack" if dist_a <= dist_d else "Dodge"
    elif dist_a <= attack_hitbox:
        expanded = "Attack"
    elif dist_d <= dodge_hitbox:
        expanded = "Dodge"
    gaussian = "Attack" if like_a >= like_d else "Dodge"
    bayes = "Attack" if post_a >= post_d else "Dodge"
    prior_only = "Attack" if prior_a >= prior_d else "Dodge"
    near_boundary = min(abs(dist_a - attack_radius), abs(dist_d - dodge_radius), abs(dist_a - dist_d)) <= min(attack_radius, dodge_radius) * 0.25
    ambiguous = visual == "None" or near_boundary or abs(post_a - post_d) < 0.24
    final = bayes
    invalid = like_a < 0.01 and like_d < 0.01
    safety_reason = "correction_allowed"
    if str(row.get("condition")) == "visual_boundary":
        final = visual
        invalid = final == "None"
        safety_reason = "visual_boundary_baseline"
    elif str(row.get("condition")) == "expanded_hitbox":
        final = expanded
        invalid = final == "None"
        safety_reason = "expanded_hitbox_baseline"
    elif str(row.get("condition")) == "user_gaussian":
        final = gaussian
        safety_reason = "user_gaussian_baseline"
    elif str(row.get("condition")) == "context_prior_only":
        final = prior_only
        safety_reason = "context_prior_only_baseline"
    elif str(row.get("condition")) == "context_bayesian_safety":
        if visual != "None" and not near_boundary:
            final = visual
            safety_reason = "preserve_clear_visual_input"
        elif invalid:
            final = "None"
            safety_reason = "invalid_far_touch"
        elif max(post_a, post_d) < float(row.get("tau") or 0.55):
            final = visual if visual != "None" else "None"
            invalid = final == "None"
            safety_reason = "posterior_below_tau"
        elif abs(post_a - post_d) < float(row.get("delta") or 0.12):
            final = visual if visual != "None" else "None"
            invalid = final == "None"
            safety_reason = "posterior_gap_below_delta"
    row.update(
        {
            "distance_to_attack": dist_a,
            "distance_to_dodge": dist_d,
            "is_inside_attack_visual": dist_a <= attack_radius,
            "is_inside_dodge_visual": dist_d <= dodge_radius,
            "is_inside_attack_expanded": dist_a <= attack_hitbox,
            "is_inside_dodge_expanded": dist_d <= dodge_hitbox,
            "is_near_boundary": near_boundary,
            "is_ambiguous": ambiguous,
            "likelihood_attack": like_a,
            "likelihood_dodge": like_d,
            "prior_attack": prior_a,
            "prior_dodge": prior_d,
            "posterior_attack": post_a,
            "posterior_dodge": post_d,
            "posterior_gap": abs(post_a - post_d),
            "max_posterior": max(post_a, post_d),
            "visual_boundary_prediction": visual,
            "expanded_hitbox_prediction": expanded,
            "user_gaussian_prediction": gaussian,
            "context_prior_only_prediction": prior_only,
            "bayesian_prediction": bayes,
            "final_executed_action": final,
            "invalid_touch": invalid,
            "safety_gate_passed": safety_reason == "correction_allowed",
            "safety_gate_reason": safety_reason,
        }
    )
    return row


def generate_smoke_session() -> Path:
    ensure_unity_dirs()
    random.seed(7)
    session_dir = DEFAULT_SESSIONS_DIR / "smoke_test_user_001"
    session_dir.mkdir(parents=True, exist_ok=True)
    session_id = "smoke_test_user_001"
    participant_id = "test_user_public"
    meta = {
        "session_id": session_id,
        "participant_id": participant_id,
        "device_model": "synthetic_unity_fixture",
        "platform": "editor_fixture",
        "screen_width": 1080,
        "screen_height": 1920,
        "dpi": 420,
        "unity_version": "fixture",
        "app_version": "0.1.0",
        "timestamp_start": "2026-05-18T00:00:00Z",
        "handedness": "unavailable",
        "notes": "Synthetic Unity-like smoke fixture for pipeline tests; not real user data.",
        "source_type": "synthetic_unity_fixture",
    }
    write_json(session_dir / "session_meta.json", meta)
    condition_order = {"session_id": session_id, "participant_id": participant_id, "condition_order": CONDITIONS}
    write_json(session_dir / "condition_order.json", condition_order)

    calibration_rows = []
    raw_rows = []
    decision_rows = []
    hp_rows = []
    layout_rows = []
    mode_policy_rows = []
    survey_rows = []
    attack_center = (360.0, 1650.0)
    dodge_center = (720.0, 1650.0)
    visual_radius = 110.0
    hitbox_radius = 150.0
    player_hp = 100
    enemy_hp = 500

    def base_row(i: int, phase: str, condition: str, state: str, required: str, tx: float, ty: float) -> dict[str, Any]:
        prior_a, prior_d = prior_for_state(state)
        demand, policy = fixture_policy_for_state(state, i, phase)
        current_hitbox_radius = visual_radius * float(policy["policy_hitbox_expansion_ratio"])
        row = {field: "" for field in TRIAL_FIELDS}
        row.update(
            {
                "session_id": session_id,
                "participant_id": participant_id,
                "trial_id": i,
                "block_id": i // 24,
                "phase": phase,
                "condition": condition,
                "interaction_mode": demand["interaction_mode"],
                "timestamp_trial_start_ms": nowish_ms(i),
                "timestamp_touch_ms": nowish_ms(i) + 300,
                "timestamp_action_ms": nowish_ms(i) + 330,
                "timestamp_trial_end_ms": nowish_ms(i) + 600,
                "screen_width": 1080,
                "screen_height": 1920,
                "attack_center_x": attack_center[0],
                "attack_center_y": attack_center[1],
                "attack_visual_radius": visual_radius,
                "attack_hitbox_radius": current_hitbox_radius,
                "dodge_center_x": dodge_center[0],
                "dodge_center_y": dodge_center[1],
                "dodge_visual_radius": visual_radius,
                "dodge_hitbox_radius": current_hitbox_radius,
                "dynamic_attack_radius": 180 * math.sqrt(-2 * math.log(0.01 / max(prior_a, 0.01))),
                "dynamic_dodge_radius": 180 * math.sqrt(-2 * math.log(0.01 / max(prior_d, 0.01))),
                "enemy_state": state,
                "danger_warning_visible": state in {"Telegraph", "Attacking", "Urgent"},
                "enemy_distance": 1.0,
                "player_hp_before": player_hp,
                "enemy_hp_before": enemy_hp,
                "cooldown_attack": 0,
                "cooldown_dodge": 0,
                "required_action": required,
                "intended_action": required,
                "label_source": "calibration_instruction" if phase == "calibration" else "scenario_rule",
                "touch_x": tx,
                "touch_y": ty,
                "touch_phase": "Began",
                "touch_pressure": 0.5,
                "touch_radius": 8,
                "relative_attack_x": (tx - attack_center[0]) / visual_radius,
                "relative_attack_y": (ty - attack_center[1]) / visual_radius,
                "relative_dodge_x": (tx - dodge_center[0]) / visual_radius,
                "relative_dodge_y": (ty - dodge_center[1]) / visual_radius,
                "tau": 0.7 + (0.45 - 0.7) * float(policy["policy_correction_strength"]),
                "delta": 0.22 + (0.08 - 0.22) * float(policy["policy_correction_strength"]),
                "variance_attack": 180.0**2,
                "variance_dodge": 180.0**2,
                "prior_strength": 0.6 + (1.4 - 0.6) * float(policy["policy_correction_strength"]),
                "public_prior_source": "synthetic_fixture_public_default",
                "public_variance_source": "synthetic_fixture_public_default",
                "reaction_time_ms": 330,
                "hitbox_visualization_enabled": True,
                **demand,
                **policy,
            }
        )
        return decode_touch(row)

    def mode_policy_record(row: dict[str, Any]) -> dict[str, Any]:
        record = {
            key: row[key]
            for key in row
            if key.startswith("demand_")
            or key.startswith("policy_")
            or key in {"session_id", "participant_id", "trial_id", "interaction_mode"}
        }
        record["timestamp_ms"] = row.get("timestamp_action_ms", "")
        return record

    trial_id = 0
    for action in ["Attack"] * 30 + ["Dodge"] * 30:
        trial_id += 1
        cx, cy = attack_center if action == "Attack" else dodge_center
        tx = cx + random.gauss(0, 28)
        ty = cy + random.gauss(0, 24)
        row = base_row(trial_id, "calibration", "user_gaussian", "Neutral", action, tx, ty)
        row["final_executed_action"] = action
        row["action_success"] = True
        row["hp_after"] = player_hp
        row["enemy_hp_after"] = enemy_hp
        row["damage_taken"] = 0
        row["damage_dealt"] = 0
        row["survived"] = True
        row["cooldown_wasted"] = False
        row["feedback_type"] = "calibration"
        row["button_feedback_color"] = "white"
        row["feedback_message"] = "calibration"
        row["haptic_feedback_triggered"] = False
        calibration_rows.append(row)
        raw_rows.append(
            {
                "session_id": session_id,
                "participant_id": participant_id,
                "trial_id": trial_id,
                "timestamp_touch_ms": row["timestamp_touch_ms"],
                "touch_x": row["touch_x"],
                "touch_y": row["touch_y"],
                "touch_phase": "Began",
                "touch_pressure": 0.5,
                "touch_radius": 8,
                "finger_id": 0,
            }
        )
        mode_policy_rows.append(mode_policy_record(row))

    main_rows = []
    states = [("Safe", "Attack"), ("Telegraph", "Dodge"), ("Attacking", "Dodge"), ("Neutral", "Attack")]
    trial_types = ["clear", "near_boundary", "ambiguous", "outside_recoverable", "invalid_far"]
    for i in range(240):
        trial_id += 1
        state, required = states[i % len(states)]
        condition = CONDITIONS[i % len(CONDITIONS)]
        trial_type = trial_types[i % len(trial_types)]
        if trial_type == "clear":
            cx, cy = attack_center if required == "Attack" else dodge_center
            tx = cx + random.gauss(0, 25)
            ty = cy + random.gauss(0, 25)
        elif trial_type == "near_boundary":
            cx, cy = attack_center if required == "Attack" else dodge_center
            tx = cx + (visual_radius * 0.92 if required == "Attack" else -visual_radius * 0.92)
            ty = cy + random.gauss(0, 15)
        elif trial_type == "ambiguous":
            tx = (attack_center[0] + dodge_center[0]) / 2 + random.gauss(0, 18)
            ty = attack_center[1] + random.gauss(0, 18)
        elif trial_type == "outside_recoverable":
            cx, cy = attack_center if required == "Attack" else dodge_center
            tx = cx + (hitbox_radius * 0.95)
            ty = cy + random.gauss(0, 18)
        else:
            tx = 100.0 + random.gauss(0, 10)
            ty = 300.0 + random.gauss(0, 10)
        row = base_row(trial_id, "test", condition, state, required, tx, ty)
        success = str(row["final_executed_action"]) == required and not as_bool(row["invalid_touch"])
        damage_taken = 0 if success or required == "Attack" else 10
        damage_dealt = 15 if success and required == "Attack" else 0
        row["player_hp_before"] = player_hp
        row["enemy_hp_before"] = enemy_hp
        player_hp = max(0, player_hp - damage_taken)
        enemy_hp = max(0, enemy_hp - damage_dealt)
        row["hp_after"] = player_hp
        row["enemy_hp_after"] = enemy_hp
        row["damage_taken"] = damage_taken
        row["damage_dealt"] = damage_dealt
        row["survived"] = player_hp > 0
        row["cooldown_wasted"] = row["final_executed_action"] == "Dodge" and required == "Attack"
        row["action_success"] = success
        row["feedback_type"] = "success" if success else "fail"
        row["button_feedback_color"] = "green" if success else "red"
        row["feedback_message"] = "Corrected to " + str(row["final_executed_action"]) if as_bool(row["safety_gate_passed"]) else str(row["final_executed_action"])
        row["haptic_feedback_triggered"] = as_bool(row.get("policy_haptic_enabled")) and str(row["final_executed_action"]) in {"Attack", "Dodge"}
        main_rows.append(row)
        raw_rows.append(
            {
                "session_id": session_id,
                "participant_id": participant_id,
                "trial_id": trial_id,
                "timestamp_touch_ms": row["timestamp_touch_ms"],
                "touch_x": row["touch_x"],
                "touch_y": row["touch_y"],
                "touch_phase": "Began",
                "touch_pressure": 0.5,
                "touch_radius": 8,
                "finger_id": 0,
            }
        )
        decision_rows.append(
            {
                "session_id": session_id,
                "participant_id": participant_id,
                "trial_id": trial_id,
                "timestamp_ms": row["timestamp_action_ms"],
                "condition": condition,
                "enemy_state": state,
                "likelihood_attack": row["likelihood_attack"],
                "likelihood_dodge": row["likelihood_dodge"],
                "prior_attack": row["prior_attack"],
                "prior_dodge": row["prior_dodge"],
                "posterior_attack": row["posterior_attack"],
                "posterior_dodge": row["posterior_dodge"],
                "posterior_gap": row["posterior_gap"],
                "bayesian_prediction": row["bayesian_prediction"],
                "final_executed_action": row["final_executed_action"],
                "invalid_touch": row["invalid_touch"],
                "safety_gate_passed": row["safety_gate_passed"],
                "safety_gate_reason": row["safety_gate_reason"],
            }
        )
        hp_rows.append(
            {
                "session_id": session_id,
                "participant_id": participant_id,
                "trial_id": trial_id,
                "timestamp_ms": row["timestamp_trial_end_ms"],
                "player_hp_before": row["player_hp_before"],
                "player_hp_after": row["hp_after"],
                "enemy_hp_before": row["enemy_hp_before"],
                "enemy_hp_after": row["enemy_hp_after"],
                "damage_taken": row["damage_taken"],
                "damage_dealt": row["damage_dealt"],
                "survived": row["survived"],
                "action_success": row["action_success"],
            }
        )
        layout_rows.append({key: row[key] for key in row if key.endswith("radius") or key.endswith("x") or key.endswith("y") or key in {"session_id", "participant_id", "trial_id", "screen_width", "screen_height"}})
        mode_policy_rows.append(mode_policy_record(row))

    survey_rows.append(
        {
            "session_id": session_id,
            "participant_id": participant_id,
            "timestamp_ms": nowish_ms(trial_id) + 1000,
            "trial_id": trial_id,
            "trust_score": 4,
            "control_score": 4,
            "predictability_score": 4,
            "free_text": "Synthetic fixture survey row for pipeline testing only.",
            "source": "synthetic_unity_fixture",
        }
    )

    write_jsonl(session_dir / "calibration_trials.jsonl", calibration_rows)
    write_jsonl(session_dir / "main_trials.jsonl", main_rows)
    write_jsonl(session_dir / "raw_touch_events.jsonl", raw_rows)
    write_jsonl(session_dir / "model_decisions.jsonl", decision_rows)
    write_jsonl(session_dir / "hp_outcomes.jsonl", hp_rows)
    write_jsonl(session_dir / "ui_layout_snapshots.jsonl", layout_rows)
    write_jsonl(session_dir / "mode_policy_events.jsonl", mode_policy_rows)
    write_jsonl(session_dir / "trust_control_survey.jsonl", survey_rows)
    return session_dir


def ingest_unity_logs(sessions: Path | None = None) -> list[dict[str, Any]]:
    ensure_unity_dirs()
    sessions = sessions or DEFAULT_SESSIONS_DIR
    if not sessions.exists() or not any(sessions.iterdir()):
        generate_smoke_session()
    trial_rows: list[dict[str, Any]] = []
    meta_rows: list[dict[str, Any]] = []
    for session_dir in sorted(path for path in sessions.iterdir() if path.is_dir()):
        meta_path = session_dir / "session_meta.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() else {}
        meta_rows.append(meta)
        for file_name in ["calibration_trials.jsonl", "main_trials.jsonl"]:
            for row in read_jsonl(session_dir / file_name):
                for field in TRIAL_FIELDS:
                    row.setdefault(field, "")
                trial_rows.append(row)
    write_jsonl(PROCESSED_DIR / "ingested_trials.jsonl", trial_rows)
    write_csv(PROCESSED_DIR / "ingested_trials.csv", trial_rows, TRIAL_FIELDS)
    write_csv(PROCESSED_DIR / "session_meta.csv", meta_rows)
    return trial_rows


def build_unity_dataset() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = read_jsonl(PROCESSED_DIR / "ingested_trials.jsonl")
    if not rows:
        rows = ingest_unity_logs()
    built = []
    for row in rows:
        for field in TRIAL_FIELDS:
            row.setdefault(field, "")
        if not row.get("posterior_attack") or not row.get("visual_boundary_prediction"):
            row = decode_touch(row)
        built.append(row)
    write_csv(PROCESSED_DIR / "unity_dataset.csv", built, TRIAL_FIELDS)
    phases = Counter(str(row.get("phase", "")) for row in built)
    conditions = Counter(str(row.get("condition", "")) for row in built)
    summary = [
        {"summary": "rows", "value": len(built)},
        {"summary": "participants", "value": len(set(str(row.get("participant_id", "")) for row in built))},
        {"summary": "calibration_rows", "value": phases.get("calibration", 0)},
        {"summary": "main_rows", "value": len(built) - phases.get("calibration", 0)},
    ]
    for condition, count in conditions.items():
        summary.append({"summary": f"condition_{condition}", "value": count})
    write_csv(REPORTS_DIR / "dataset_summary.csv", summary)
    write_data_quality_report(built)
    return built


def train_user_touch_model() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = read_csv(PROCESSED_DIR / "unity_dataset.csv")
    if not rows:
        rows = build_unity_dataset()
    calibration = [row for row in rows if str(row.get("phase")) == "calibration"]
    grouped: dict[tuple[str, str], list[tuple[float, float]]] = defaultdict(list)
    for row in calibration:
        action = str(row.get("intended_action") or row.get("required_action"))
        if action not in {"Attack", "Dodge"}:
            continue
        cx = float(row["attack_center_x"] if action == "Attack" else row["dodge_center_x"])
        cy = float(row["attack_center_y"] if action == "Attack" else row["dodge_center_y"])
        radius = float(row["attack_visual_radius"] if action == "Attack" else row["dodge_visual_radius"])
        tx = float(row["touch_x"])
        ty = float(row["touch_y"])
        grouped[(str(row.get("participant_id")), action)].append(((tx - cx) / radius, (ty - cy) / radius))
    model_rows = []
    for (participant, action), samples in grouped.items():
        xs = [sample[0] for sample in samples]
        ys = [sample[1] for sample in samples]
        mx = statistics.mean(xs)
        my = statistics.mean(ys)
        variance = statistics.mean([(x - mx) ** 2 + (y - my) ** 2 for x, y in samples]) if len(samples) > 1 else 1.0
        model_rows.append(
            {
                "participant_id": participant,
                "action": action,
                "sample_count": len(samples),
                "mean_x": mx,
                "mean_y": my,
                "relative_variance": variance,
                "screen_variance_proxy": variance * 180.0**2,
            }
        )
    write_csv(REPORTS_DIR / "calibration_summary.csv", model_rows or [{"status": "no_calibration"}])
    write_json(MODELS_DIR / "user_touch_models.json", model_rows)
    plot_touch_distribution(rows)
    plot_gaussian_contours(model_rows)
    return model_rows


def evaluate_decoders() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    if not rows:
        build_unity_dataset()
        rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    metrics = decoder_metric_rows(rows)
    write_csv(REPORTS_DIR / "decoder_metrics.csv", metrics)
    plot_decoder_accuracy(metrics)
    plot_correction(rows)
    plot_posterior_examples(rows)
    plot_dynamic_hitbox(rows)
    return metrics


def evaluate_hp_outcomes() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    if not rows:
        rows = build_unity_dataset()
    output = []
    for condition, group_rows in defaultdict(list, {}).items():
        _ = condition, group_rows
    by_condition: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        by_condition[str(row.get("condition", ""))].append(row)
    for condition, group_rows in by_condition.items():
        output.append(
            {
                "condition": condition,
                "n": len(group_rows),
                "hp_preservation": mean_numeric(group_rows, "hp_after"),
                "damage_taken_mean": mean_numeric(group_rows, "damage_taken"),
                "damage_dealt_mean": mean_numeric(group_rows, "damage_dealt"),
                "survival_rate": sum(1 for row in group_rows if as_bool(row.get("survived", False))) / max(len(group_rows), 1),
                "cooldown_waste_rate": sum(1 for row in group_rows if as_bool(row.get("cooldown_wasted", False))) / max(len(group_rows), 1),
            }
        )
    write_csv(REPORTS_DIR / "hp_outcome_metrics.csv", output or [{"status": "unavailable"}])
    plot_hp(output)
    return output


def evaluate_mode_policy_and_survey() -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    ensure_unity_dirs()
    rows = read_csv(PROCESSED_DIR / "unity_dataset.csv")
    if not rows:
        rows = build_unity_dataset()
    main_rows = [row for row in rows if str(row.get("phase")) != "calibration"]

    mode_summary: list[dict[str, Any]] = []
    by_mode: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in main_rows:
        mode = str(row.get("interaction_mode") or "unavailable")
        by_mode[mode].append(row)
    for mode, group_rows in sorted(by_mode.items()):
        mode_summary.append(
            {
                "interaction_mode": mode,
                "n": len(group_rows),
                "accuracy": accuracy(group_rows),
                "invalid_touch_rate": invalid_touch_rate(group_rows),
                "correction_success_rate": correction_success_rate(group_rows),
                "overcorrection_rate": overcorrection_rate(group_rows),
                "avg_error_tolerance": mean_numeric(group_rows, "policy_error_tolerance"),
                "avg_correction_strength": mean_numeric(group_rows, "policy_correction_strength"),
                "avg_hitbox_expansion_ratio": mean_numeric(group_rows, "policy_hitbox_expansion_ratio"),
                "haptic_trigger_rate": sum(1 for row in group_rows if as_bool(row.get("haptic_feedback_triggered", False))) / max(len(group_rows), 1),
                "guidance_visible_rate": sum(1 for row in group_rows if as_bool(row.get("policy_guidance_visible", False))) / max(len(group_rows), 1),
                "review_visible_rate": sum(1 for row in group_rows if as_bool(row.get("policy_review_visible", False))) / max(len(group_rows), 1),
            }
        )
    write_csv(REPORTS_DIR / "mode_policy_summary.csv", mode_summary or [{"status": "unavailable"}])
    plot_mode_policy_distribution(mode_summary)

    survey_rows: list[dict[str, Any]] = []
    if DEFAULT_SESSIONS_DIR.exists():
        for session_dir in sorted(path for path in DEFAULT_SESSIONS_DIR.iterdir() if path.is_dir()):
            survey_rows.extend(read_jsonl(session_dir / "trust_control_survey.jsonl"))
    survey_summary = [
        {
            "survey_rows": len(survey_rows),
            "trust_score_mean": mean_numeric(survey_rows, "trust_score"),
            "control_score_mean": mean_numeric(survey_rows, "control_score"),
            "predictability_score_mean": mean_numeric(survey_rows, "predictability_score"),
            "source_types": ";".join(sorted(set(str(row.get("source", "")) for row in survey_rows if row.get("source")))),
            "interpretation": "subjective trust/control can be reported only for real user-study rows, not synthetic fixtures",
        }
    ]
    write_csv(REPORTS_DIR / "trust_control_survey_summary.csv", survey_summary)
    return mode_summary, survey_summary


def run_ablation() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    output = []
    for condition in CONDITIONS:
        subset = [row for row in rows if str(row.get("condition")) == condition]
        output.append(
            {
                "ablation_type": "condition",
                "condition": condition,
                "n": len(subset),
                "accuracy": accuracy(subset),
                "invalid_touch_rate": invalid_touch_rate(subset),
                "correction_success_rate": correction_success_rate(subset),
                "overcorrection_rate": overcorrection_rate(subset),
                "damage_taken_mean": mean_numeric(subset, "damage_taken"),
            }
        )
    for label, field in [
        ("no_public_default_config", "visual_boundary_prediction"),
        ("public_default_only_no_calibration", "expanded_hitbox_prediction"),
        ("calibration_only", "user_gaussian_prediction"),
        ("public_default_plus_calibration", "final_executed_action"),
        ("safety_gate_off", "bayesian_prediction"),
        ("safety_gate_on", "final_executed_action"),
        ("clear_input_preservation_on", "final_executed_action"),
    ]:
        output.append(
            {
                "ablation_type": "model",
                "condition": label,
                "n": len(rows),
                "accuracy": accuracy(rows, field),
                "invalid_touch_rate": invalid_touch_rate(rows),
                "correction_success_rate": correction_success_rate(rows) if field == "final_executed_action" else "",
                "overcorrection_rate": overcorrection_rate(rows) if field == "final_executed_action" else "",
                "damage_taken_mean": mean_numeric(rows, "damage_taken"),
            }
        )
    for strength, field in [("weak_prior", "user_gaussian_prediction"), ("medium_prior", "bayesian_prediction"), ("strong_prior", "context_prior_only_prediction")]:
        output.append(
            {
                "ablation_type": "prior_strength_proxy",
                "condition": strength,
                "n": len(rows),
                "accuracy": accuracy(rows, field),
                "invalid_touch_rate": invalid_touch_rate(rows),
                "correction_success_rate": "",
                "overcorrection_rate": "",
                "damage_taken_mean": mean_numeric(rows, "damage_taken"),
            }
        )
    for value in [0.45, 0.55, 0.65, 0.75]:
        accepted = [row for row in rows if float(row.get("max_posterior") or 0.0) >= value]
        output.append(
            {
                "ablation_type": "tau_sweep",
                "condition": f"tau={value}",
                "n": len(accepted),
                "accuracy": accuracy(accepted),
                "invalid_touch_rate": invalid_touch_rate(accepted),
                "correction_success_rate": correction_success_rate(accepted),
                "overcorrection_rate": overcorrection_rate(accepted),
                "damage_taken_mean": mean_numeric(accepted, "damage_taken"),
            }
        )
    for value in [0.05, 0.12, 0.2, 0.3]:
        accepted = [row for row in rows if float(row.get("posterior_gap") or 0.0) >= value]
        output.append(
            {
                "ablation_type": "delta_sweep",
                "condition": f"delta={value}",
                "n": len(accepted),
                "accuracy": accuracy(accepted),
                "invalid_touch_rate": invalid_touch_rate(accepted),
                "correction_success_rate": correction_success_rate(accepted),
                "overcorrection_rate": overcorrection_rate(accepted),
                "damage_taken_mean": mean_numeric(accepted, "damage_taken"),
            }
        )
    write_csv(REPORTS_DIR / "ablation_summary.csv", output)
    return output


def compare_public_and_unity() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    public_config_path = Path("analysis_public") / "outputs" / "models" / "public_touch_prior_config.json"
    public_config = json.loads(public_config_path.read_text(encoding="utf-8")) if public_config_path.exists() else {}
    unity_models_path = MODELS_DIR / "user_touch_models.json"
    unity_models = json.loads(unity_models_path.read_text(encoding="utf-8")) if unity_models_path.exists() else train_user_touch_model()
    public_variance = float(public_config.get("recommended_default_variance") or public_config.get("default_touch_variance") or 180.0**2)
    public_margin = float(public_config.get("recommended_expanded_hitbox_margin") or public_config.get("recommended_hitbox_expansion_ratio") or 1.25)
    unity_variances = [float(row.get("screen_variance_proxy") or 0.0) for row in unity_models if float(row.get("sample_count") or 0) > 0]
    unity_variance = statistics.mean(unity_variances) if unity_variances else 0.0
    unity_rows = read_csv(PROCESSED_DIR / "unity_dataset.csv")
    unity_main = [row for row in unity_rows if str(row.get("phase")) != "calibration"]
    decoder_rows = read_csv(REPORTS_DIR / "decoder_metrics.csv")
    tsi_rows = read_csv(Path("analysis_public") / "outputs" / "reports" / "tsi_target_selection_metrics.csv")
    touch_dyn_rows = read_csv(Path("analysis_public") / "outputs" / "reports" / "public_touch_dynamics_summary.csv")
    tsi_user = next((row for row in tsi_rows if row.get("baseline") == "user_gaussian_with_ambiguity_gate"), {})
    unity_final = next((row for row in decoder_rows if row.get("group") == "overall" and row.get("baseline") == "final"), {})
    public_touch_rate = mean_numeric(touch_dyn_rows, "touch_rate_per_sec") if touch_dyn_rows else 0.0
    unity_touch_rate = len(unity_main) / max((mean_numeric(unity_main, "reaction_time_ms") / 1000.0) * max(len(unity_main), 1), 0.001) if unity_main else 0.0
    rows = [
        {
            "comparison": "public_recommended_variance_vs_unity_learned_variance",
            "public_value": public_variance,
            "unity_value": unity_variance,
            "interpretation_ko": "공개 데이터 기반 분산은 Unity calibration 전 기본값이며, Unity 학습 분산은 실제 Attack/Dodge 로그에서만 직접 해석한다.",
        },
        {
            "comparison": "public_hitbox_margin_vs_unity_default_margin",
            "public_value": public_margin,
            "unity_value": 1.25,
            "interpretation_ko": "공개 target-selection margin은 hitbox sanity check 용도이며, 게임 문맥 prior 검증은 아니다.",
        },
        {
            "comparison": "public_tsi_target_selection_vs_unity_attack_dodge",
            "public_value": tsi_user.get("accuracy", ""),
            "unity_value": unity_final.get("overall_accuracy", ""),
            "interpretation_ko": "TSI 정확도는 target-selection proxy이고 Unity 정확도는 synthetic Attack/Dodge fixture 평가라 직접 동등 비교가 아니다.",
        },
        {
            "comparison": "public_touch_dynamics_density_vs_unity_trial_density",
            "public_value": public_touch_rate,
            "unity_value": unity_touch_rate,
            "interpretation_ko": "공개 game touch density는 실제 모바일 조작 밀도 근거이며, Unity fixture의 trial density는 controlled logging sanity check다.",
        },
    ]
    write_csv(REPORTS_DIR / "public_vs_unity_comparison.csv", rows)
    (REPORTS_DIR / "public_vs_unity_interpretation_ko.md").write_text(
        "\n".join(
            [
                "# Public vs Unity 해석",
                "",
                "공개 데이터는 Unity Attack/Dodge Bayesian correction을 직접 검증하지 않는다.",
                "비교 목적은 Unity calibration touch 분포가 공개 touch-target 행동과 비교해 비현실적으로 벗어나지 않는지 확인하는 것이다.",
            ]
        ),
        encoding="utf-8",
    )
    plot_public_vs_unity(public_variance, unity_variance)
    return rows


def write_data_quality_report(rows: list[dict[str, Any]]) -> list[str]:
    issues: list[str] = []
    trial_ids_by_session: dict[str, set[str]] = defaultdict(set)
    duplicate_count = 0
    for row in rows:
        if not row.get("participant_id"):
            issues.append("missing participant_id")
        if not row.get("screen_width") or not row.get("screen_height"):
            issues.append("missing screen resolution")
        if not row.get("attack_center_x") or not row.get("dodge_center_x"):
            issues.append("missing Attack/Dodge centers")
        key = str(row.get("session_id"))
        trial_id = str(row.get("trial_id"))
        if trial_id in trial_ids_by_session[key] and str(row.get("phase")) != "calibration":
            duplicate_count += 1
        trial_ids_by_session[key].add(trial_id)
        if str(row.get("phase")) in CONTROLLED_PHASES and str(row.get("phase")) != "calibration" and not row.get("required_action"):
            issues.append("missing required_action in controlled trial")
        try:
            posterior_sum = float(row.get("posterior_attack") or 0.0) + float(row.get("posterior_dodge") or 0.0)
            if posterior_sum and abs(posterior_sum - 1.0) > 0.02:
                issues.append("posterior values do not sum to 1")
        except ValueError:
            issues.append("invalid posterior value")
        if not as_bool(row.get("safety_gate_passed", False)) and not row.get("safety_gate_reason"):
            issues.append("empty safety_gate_reason on safety failure")
        if str(row.get("label_source", "")) not in VALID_LABEL_SOURCES:
            issues.append("invalid label_source")
        try:
            if float(row.get("timestamp_trial_end_ms") or 0.0) < float(row.get("timestamp_trial_start_ms") or 0.0):
                issues.append("impossible timestamps")
        except ValueError:
            issues.append("invalid timestamp")
    counts = Counter(issues)
    lines = ["# Data Quality Report", "", f"- rows: {len(rows)}", f"- duplicate main trial ids: {duplicate_count}", ""]
    if counts:
        lines.append("## Issues")
        for issue, count in counts.most_common():
            lines.append(f"- {issue}: {count}")
    else:
        lines.append("No blocking data quality issues detected.")
    (REPORTS_DIR / "data_quality_report.md").write_text("\n".join(lines), encoding="utf-8")
    return lines


def report_builder() -> None:
    ensure_unity_dirs()
    rows = read_csv(PROCESSED_DIR / "unity_dataset.csv")
    if not rows:
        rows = build_unity_dataset()
    write_data_quality_report(rows)
    mode_policy_summary, survey_summary = evaluate_mode_policy_and_survey()
    decoder_metrics = read_csv(REPORTS_DIR / "decoder_metrics.csv")
    hp_metrics = read_csv(REPORTS_DIR / "hp_outcome_metrics.csv")
    comparison = read_csv(REPORTS_DIR / "public_vs_unity_comparison.csv")
    public_sizes = read_csv(Path("analysis_public") / "outputs" / "reports" / "public_dataset_size_summary.csv")
    tsi_metrics = read_csv(Path("analysis_public") / "outputs" / "reports" / "tsi_target_selection_metrics.csv")
    calibration = read_csv(REPORTS_DIR / "calibration_summary.csv")
    final_metric = next((row for row in decoder_metrics if row.get("group") == "overall" and row.get("baseline") == "final"), {})
    lines = [
        "# 최종 Unity + Public Dataset 결과 요약",
        "",
        "## 1. 확보한 공개 데이터셋",
    ]
    for row in public_sizes:
        lines.append(
            f"- {row.get('dataset_id')}: available={row.get('available')}, raw_files={row.get('raw_file_count')}, "
            f"raw_size={row.get('raw_size_bytes')} bytes, processed_rows={row.get('processed_rows')}, "
            f"users={row.get('users')}, games={row.get('games')}"
        )
    lines.extend(
        [
            "",
            "## 2. 공개 데이터셋의 역할과 한계",
            "- Touch-Dynamics/MC-Snake: 실제 모바일 게임 touch dynamics와 사용자 차이를 검증한다. Attack/Dodge correction 직접 검증은 아니다.",
            "- TSI: target-labeled touch-target modeling, Gaussian baseline, hitbox behavior를 검증한다. game-state prior 검증은 아니다.",
            "- Screen Annotation: UI grounding support다. combat/game context 검증은 아니다.",
            "- Henze/Rico: 현재 로컬 parseable data가 없어 unavailable로 기록했다.",
            "",
            "## 3. Public Target-Selection Benchmark",
        ]
    )
    for row in tsi_metrics:
        lines.append(
            f"- {row.get('baseline')}: accuracy={row.get('accuracy')}, macro_f1={row.get('macro_f1')}, "
            f"correction_success={row.get('correction_success_rate')}, overcorrection={row.get('overcorrection_rate')}"
        )
    lines.extend(
        [
            "",
            "## 4. Unity Telemetry Dataset",
            f"- 전체 Unity row 수: {len(rows)}",
            f"- calibration row 수: {sum(1 for row in rows if str(row.get('phase')) == 'calibration')}",
            f"- main/test row 수: {sum(1 for row in rows if str(row.get('phase')) != 'calibration')}",
            "- 현재 실행 환경에서는 Unity Editor를 실행하지 못해 `source_type=synthetic_unity_fixture` fixture를 사용했다. 실제 사용자 데이터가 아니다.",
            "",
            "## 5. Unity User Touch Model",
        ]
    )
    for row in calibration:
        lines.append(
            f"- participant={row.get('participant_id')}, action={row.get('action')}, samples={row.get('sample_count')}, "
            f"mean=({row.get('mean_x')}, {row.get('mean_y')}), variance={row.get('screen_variance_proxy')}"
        )
    lines.extend(
        [
            "",
            "## 6. Decoder Baselines",
        ]
    )
    for row in decoder_metrics:
        if row.get("group") == "overall":
            lines.append(
                f"- {row.get('baseline')}: accuracy={row.get('overall_accuracy')}, "
                f"ambiguous={row.get('ambiguous_subset_accuracy')}, invalid={row.get('invalid_touch_rate')}"
            )
    lines.extend(
        [
            "",
            "## 7. Correction / Overcorrection",
            f"- correction_success_rate: {final_metric.get('correction_success_rate')}",
            f"- overcorrection_rate: {final_metric.get('overcorrection_rate')}",
            f"- no_correction_when_uncertain_rate: {final_metric.get('no_correction_when_uncertain_rate')}",
            "",
            "## 8. HP Outcome",
        ]
    )
    for row in hp_metrics:
        lines.append(f"- {row.get('condition')}: damage_taken_mean={row.get('damage_taken_mean')}, survival_rate={row.get('survival_rate')}, cooldown_waste_rate={row.get('cooldown_waste_rate')}")
    lines.extend(
        [
            "",
            "## 9. Interaction Mode / Policy",
        ]
    )
    for row in mode_policy_summary:
        lines.append(
            f"- {row.get('interaction_mode')}: n={row.get('n')}, accuracy={row.get('accuracy')}, "
            f"correction_success={row.get('correction_success_rate')}, overcorrection={row.get('overcorrection_rate')}, "
            f"hitbox_expansion={row.get('avg_hitbox_expansion_ratio')}"
        )
    lines.extend(
        [
            "",
            "## 10. Trust / Control Survey",
        ]
    )
    for row in survey_summary:
        lines.append(
            f"- rows={row.get('survey_rows')}, trust_mean={row.get('trust_score_mean')}, "
            f"control_mean={row.get('control_score_mean')}, predictability_mean={row.get('predictability_score_mean')}. "
            f"{row.get('interpretation')}"
        )
    lines.extend(
        [
            "",
            "## 11. Public-to-Unity Transfer",
        ]
    )
    for row in comparison:
        lines.append(f"- {row.get('comparison')}: public={row.get('public_value')}, unity={row.get('unity_value')}. {row.get('interpretation_ko')}")
    lines.extend(
        [
            "",
            "## 12. Limitations",
            "- Public game touch logs validate touch dynamics, not Attack/Dodge correction.",
            "- Public target-selection datasets validate touch-target modeling and hitbox behavior, not game-state prior.",
            "- Public UI datasets support UI grounding, not game-specific combat context.",
            "- Unity controlled prototype validates the actual Attack/Dodge Bayesian pipeline only when real Unity telemetry is collected.",
            "- Cognitive-first/guidance/learning-review policies are now logged and adjustable, but subjective trust/control claims require real survey data.",
            "- This is not yet commercial mobile game validation.",
            "",
            "## 13. Next Steps",
            "- Unity Editor에서 3~5명 실제 participant의 calibration/main logs를 수집한다.",
            "- Android 실기기에서 `Application.persistentDataPath/adui_sessions` 로그를 회수해 같은 분석을 재실행한다.",
            "- 주관적 trust/control은 별도 user study와 설문을 수행한 경우에만 보고한다.",
        ]
    )
    (REPORTS_DIR / "final_unity_public_dataset_result_summary_ko.md").write_text("\n".join(lines), encoding="utf-8")


def plot_touch_distribution(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "touch_distribution_by_user.png", "No Unity touch data available.")
        return
    plt.figure(figsize=(6, 5))
    for participant, group in defaultdict(list).items():
        _ = participant, group
    participants = sorted(set(str(row.get("participant_id")) for row in rows))[:5]
    for participant in participants:
        subset = [row for row in rows if str(row.get("participant_id")) == participant]
        plt.scatter([float(row.get("touch_x") or 0) for row in subset], [float(row.get("touch_y") or 0) for row in subset], s=8, alpha=0.5, label=participant)
    plt.legend()
    plt.xlabel("touch_x")
    plt.ylabel("touch_y")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "touch_distribution_by_user.png", dpi=150)
    plt.close()


def plot_gaussian_contours(model_rows: list[dict[str, Any]]) -> None:
    if not model_rows or plt is None:
        placeholder_figure(FIGURES_DIR / "gaussian_contours_attack_dodge.png", "No Unity calibration model available.")
        return
    plt.figure(figsize=(6, 4))
    for row in model_rows:
        plt.scatter(float(row["mean_x"]), float(row["mean_y"]), s=max(float(row["sample_count"]), 1) * 6, label=f"{row['participant_id']} {row['action']}")
    plt.axhline(0, color="#64748b", linewidth=1)
    plt.axvline(0, color="#64748b", linewidth=1)
    plt.legend(fontsize=8)
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "gaussian_contours_attack_dodge.png", dpi=150)
    plt.close()


def plot_decoder_accuracy(metrics: list[dict[str, Any]]) -> None:
    rows = [row for row in metrics if row.get("group") == "overall" and row.get("baseline") not in {"correction_success_rate", "overcorrection_rate"}]
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "decoder_accuracy_comparison.png", "No decoder metrics available.")
        return
    plt.figure(figsize=(8, 4))
    plt.bar([str(row["baseline"]) for row in rows], [float(row.get("overall_accuracy") or 0.0) for row in rows])
    plt.xticks(rotation=25, ha="right")
    plt.ylim(0, 1)
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "decoder_accuracy_comparison.png", dpi=150)
    plt.close()


def plot_correction(rows: list[dict[str, Any]]) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "correction_vs_overcorrection.png", "matplotlib unavailable; correction metrics are in CSV.")
        return
    values = [correction_success_rate(rows), overcorrection_rate(rows)]
    plt.figure(figsize=(5, 4))
    plt.bar(["correction_success", "overcorrection"], values, color=["#059669", "#dc2626"])
    plt.ylim(0, 1)
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "correction_vs_overcorrection.png", dpi=150)
    plt.close()


def plot_hp(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "hp_preservation_ab_test.png", "No HP metrics available.")
        return
    plt.figure(figsize=(8, 4))
    plt.bar([str(row["condition"]) for row in rows], [float(row.get("hp_preservation") or 0.0) for row in rows])
    plt.xticks(rotation=25, ha="right")
    plt.ylabel("mean hp_after")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "hp_preservation_ab_test.png", dpi=150)
    plt.close()


def plot_mode_policy_distribution(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "mode_policy_distribution.png", "No mode/policy rows available.")
        return
    plt.figure(figsize=(7, 4))
    plt.bar([str(row.get("interaction_mode")) for row in rows], [float(row.get("n") or 0.0) for row in rows], color="#2563eb")
    plt.ylabel("trial count")
    plt.xticks(rotation=20, ha="right")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "mode_policy_distribution.png", dpi=150)
    plt.close()


def plot_posterior_examples(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "posterior_examples.png", "No posterior data available.")
        return
    subset = rows[:40]
    plt.figure(figsize=(7, 4))
    plt.plot([float(row.get("posterior_attack") or 0.0) for row in subset], label="Attack")
    plt.plot([float(row.get("posterior_dodge") or 0.0) for row in subset], label="Dodge")
    plt.legend()
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "posterior_examples.png", dpi=150)
    plt.close()


def plot_dynamic_hitbox(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "dynamic_hitbox_examples.png", "No hitbox data available.")
        return
    subset = rows[:40]
    plt.figure(figsize=(7, 4))
    plt.plot([float(row.get("dynamic_attack_radius") or 0.0) for row in subset], label="Attack dynamic radius")
    plt.plot([float(row.get("dynamic_dodge_radius") or 0.0) for row in subset], label="Dodge dynamic radius")
    plt.legend()
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "dynamic_hitbox_examples.png", dpi=150)
    plt.close()


def plot_public_vs_unity(public_variance: float, unity_variance: float) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "public_vs_unity_touch_distribution.png", "matplotlib unavailable; public vs Unity metrics are in CSV.")
        return
    plt.figure(figsize=(5, 4))
    plt.bar(["public_default", "unity_calibration"], [public_variance, unity_variance], color=["#2563eb", "#059669"])
    plt.ylabel("variance")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "public_vs_unity_touch_distribution.png", dpi=150)
    plt.close()


def parse_sessions_arg() -> Path | None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sessions", type=Path, default=None)
    args = parser.parse_args()
    return args.sessions

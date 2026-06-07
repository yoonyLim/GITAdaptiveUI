from __future__ import annotations

import argparse
import json
import math
import random
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.action_to_n_button_projector import ActionToNButtonProjector
from analysis_d2e.src.baselines import BASELINES
from analysis_d2e.src.bayesian_input_decoder import BayesianInputDecoder
from analysis_d2e.src.context_button_policy import ContextButtonPolicy
from analysis_d2e.src.d2e_action_prior_model import D2EActionPriorModel
from analysis_d2e.src.io_utils import read_jsonl, safe_float, write_csv
from analysis_d2e.src.paths import MODELS_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.schemas import ButtonSpec, PHASES, SKILL_LEVELS, SkillProfile, TouchEvent, TouchProfile


PHASE_SITUATIONS = {
    "phase_1": {"phase": "phase_1", "melee_enemy_count": 16, "ranged_enemy_count": 0, "player_hp_norm": 0.75, "boss_visible": False, "boss_telegraph": False},
    "phase_2": {"phase": "phase_2", "melee_enemy_count": 0, "ranged_enemy_count": 4, "player_hp_norm": 0.28, "boss_visible": False, "boss_telegraph": False},
    "phase_3": {"phase": "phase_3", "melee_enemy_count": 4, "ranged_enemy_count": 1, "player_hp_norm": 0.55, "boss_visible": True, "boss_telegraph": True},
}

PHASE_PRIORS = {
    "phase_1": {"attack": 0.34, "defense": 0.08, "dodge": 0.18, "skill": 0.25, "heal": 0.03, "escape": 0.12},
    "phase_2": {"attack": 0.16, "defense": 0.10, "dodge": 0.28, "skill": 0.08, "heal": 0.26, "escape": 0.12},
    "phase_3": {"attack": 0.12, "defense": 0.24, "dodge": 0.42, "skill": 0.10, "heal": 0.04, "escape": 0.08},
}


def load_action_prior_model() -> D2EActionPriorModel | None:
    path = MODELS_DIR / "d2e_action_prior_model.json"
    if not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("status") != "trained":
        return None
    return D2EActionPriorModel.from_dict(data)


def buttons_for_n(n_buttons: int, context: dict[str, Any] | None = None) -> list[ButtonSpec]:
    specs = [
        ("attack", "attack", 120.0, 360.0),
        ("defense", "defense", 220.0, 360.0),
        ("skill", "skill", 320.0, 360.0),
        ("context", str((context or {}).get("context_action", "context")), 420.0, 360.0),
    ]
    selected = specs[:n_buttons]
    if n_buttons == 2:
        selected = [specs[0], specs[1]]
    elif n_buttons == 3:
        selected = [specs[0], specs[1], specs[2]]
    return [
        ButtonSpec(
            button_id=button_id,
            action=action,
            center_x=x,
            center_y=y,
            visual_radius=34.0,
            hitbox_radius=48.0,
            visible_label=str((context or {}).get("visible_label", "")) if button_id == "context" else button_id.title(),
        )
        for button_id, action, x, y in selected
    ]


def touch_profile_for_skill(skill: str) -> TouchProfile:
    variance = {"beginner": 1150.0, "intermediate": 760.0, "expert": 420.0}[skill]
    near_miss = {"beginner": 0.22, "intermediate": 0.13, "expert": 0.06}[skill]
    return TouchProfile(touch_variance=variance, near_miss_rate=near_miss)


def skill_profile(skill: str) -> SkillProfile:
    return SkillProfile(
        skill_level=skill,
        reaction_time_mean={"beginner": 430.0, "intermediate": 310.0, "expert": 220.0}[skill],
        reinput_rate={"beginner": 0.18, "intermediate": 0.09, "expert": 0.03}[skill],
        joystick_drift={"beginner": 0.20, "intermediate": 0.08, "expert": 0.02}[skill],
        cooldown_misuse_rate={"beginner": 0.20, "intermediate": 0.08, "expert": 0.03}[skill],
        context_button_understanding={"beginner": 0.35, "intermediate": 0.62, "expert": 0.88}[skill],
    )


def intended_button(prior: dict[str, float], n_buttons: int, context: dict[str, Any] | None) -> str:
    if n_buttons == 4 and context and prior.get("context", 0.0) >= max(prior.get("attack", 0.0), prior.get("defense", 0.0), prior.get("skill", 0.0)):
        return "context"
    return max(prior, key=prior.get)


def simulate_touch(buttons: list[ButtonSpec], target: str, skill: str, trial_idx: int, rng: random.Random) -> TouchEvent:
    button = next(item for item in buttons if item.button_id == target)
    profile = touch_profile_for_skill(skill)
    ambiguous_period = max(3, int(10 - profile.near_miss_rate * 20))
    if trial_idx % ambiguous_period == 0 and len(buttons) > 1:
        neighbor = min([item for item in buttons if item.button_id != target], key=lambda item: abs(item.center_x - button.center_x))
        x = (button.center_x + neighbor.center_x) / 2 + rng.uniform(-10, 10)
        y = (button.center_y + neighbor.center_y) / 2 + rng.uniform(-8, 8)
    elif trial_idx % 17 == 0:
        x = button.center_x + rng.choice([-1, 1]) * button.hitbox_radius * 1.7
        y = button.center_y + rng.uniform(-12, 12)
    else:
        std = math.sqrt(profile.touch_variance)
        x = rng.gauss(button.center_x, std * 0.45)
        y = rng.gauss(button.center_y, std * 0.45)
    return TouchEvent(x=x, y=y)


def prediction_from_baseline(baseline: Any, touch: TouchEvent, buttons: list[ButtonSpec], prior: dict[str, float], touch_profile: TouchProfile, skill: SkillProfile) -> tuple[str | None, dict[str, Any]]:
    if baseline.name == "SituationUserSkillBayesian":
        result = BayesianInputDecoder().decode(touch, buttons, prior, touch_profile, skill, use_skill=True)
        return result.predicted_button, {"posterior_margin": _posterior_margin(result.posterior), "visual_prediction": result.visual_prediction}
    if baseline.name == "SituationUserBayesian":
        result = BayesianInputDecoder().decode(touch, buttons, prior, touch_profile, skill, use_skill=False)
        return result.predicted_button, {"posterior_margin": _posterior_margin(result.posterior), "visual_prediction": result.visual_prediction}
    return baseline.predict(touch, buttons, prior, touch_profile, skill), {"posterior_margin": 0.0, "visual_prediction": BayesianInputDecoder().touch_model.visual_prediction(touch, buttons)}


def _posterior_margin(posterior: dict[str, float]) -> float:
    values = sorted(posterior.values(), reverse=True)
    if not values:
        return 0.0
    return values[0] - (values[1] if len(values) > 1 else 0.0)


def evaluate(trials_per_condition: int = 36) -> list[dict[str, Any]]:
    ensure_dirs()
    rng = random.Random(42)
    projector = ActionToNButtonProjector(ContextButtonPolicy())
    model = load_action_prior_model()
    samples = read_jsonl(PROCESSED_DIR / "d2e_action_prior_samples.jsonl")
    phase_samples = defaultdict(list)
    for sample in samples:
        phase_samples[str(sample.get("phase", "phase_1"))].append(sample)
    rows = []
    for phase in PHASES:
        sample = phase_samples[phase][0] if phase_samples.get(phase) else {"phase": phase, "atomic_distribution": PHASE_PRIORS[phase]}
        uses_d2e_model = model is not None and bool(sample.get("frame_path"))
        atomic = model.predict(sample) if uses_d2e_model else dict(PHASE_PRIORS[phase])
        situation = dict(PHASE_SITUATIONS[phase])
        for n_buttons in (2, 3, 4):
            projected = projector.project(atomic, n_buttons, situation)
            prior = dict(projected["button_prior"])
            context = projected.get("context") if isinstance(projected.get("context"), dict) else None
            buttons = buttons_for_n(n_buttons, context)
            target = intended_button(prior, n_buttons, context)
            for skill_name in SKILL_LEVELS:
                touch_profile = touch_profile_for_skill(skill_name)
                skill = skill_profile(skill_name)
                for trial_idx in range(trials_per_condition):
                    touch = simulate_touch(buttons, target, skill_name, trial_idx, rng)
                    visual = BayesianInputDecoder().touch_model.visual_prediction(touch, buttons)
                    for baseline in BASELINES:
                        pred, extra = prediction_from_baseline(baseline, touch, buttons, prior, touch_profile, skill)
                        correct = pred == target
                        visual_correct = visual == target
                        context_misfire = n_buttons == 4 and pred == "context" and target != "context"
                        rows.append(
                            {
                                "baseline": baseline.name,
                                "phase": phase,
                                "n_buttons": n_buttons,
                                "skill_level": skill_name,
                                "target_button": target,
                                "predicted_button": pred or "invalid",
                                "visual_prediction": visual or "invalid",
                                "correct": correct,
                                "misinput": not correct,
                                "correction_success": (not visual_correct) and correct,
                                "overcorrection": visual_correct and pred != target,
                                "context_misfire": context_misfire,
                                "posterior_margin": extra.get("posterior_margin", 0.0),
                                "context_action": (context or {}).get("context_action", ""),
                                "context_visible_label": (context or {}).get("visible_label", ""),
                                "source_prior": "d2e_model" if uses_d2e_model else "phase_fallback",
                            }
                        )
    write_csv(REPORTS_DIR / "trial_level_predictions.csv", rows)
    aggregates = aggregate(rows)
    write_csv(REPORTS_DIR / "phase_n_skill_results.csv", aggregates)
    baseline_rows = aggregate(rows, keys=("baseline",))
    write_csv(REPORTS_DIR / "baseline_comparison.csv", baseline_rows)
    source_prior_rows = aggregate(rows, keys=("source_prior", "baseline", "phase"))
    write_csv(REPORTS_DIR / "source_prior_metrics.csv", source_prior_rows)
    write_csv(REPORTS_DIR / "derived_decoder_metrics.csv", derived_metrics(aggregates))
    confusion = confusion_matrix(rows)
    write_csv(REPORTS_DIR / "button_confusion_matrix.csv", confusion)
    return aggregates


def aggregate(rows: list[dict[str, Any]], keys: tuple[str, ...] = ("baseline", "phase", "n_buttons", "skill_level")) -> list[dict[str, Any]]:
    grouped: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        grouped[tuple(row[key] for key in keys)].append(row)
    out = []
    for key, group in sorted(grouped.items()):
        item = {name: value for name, value in zip(keys, key)}
        item.update(
            {
                "trials": len(group),
                "misinput_rate": sum(1 for row in group if row["misinput"]) / len(group),
                "correction_success_rate": sum(1 for row in group if row["correction_success"]) / max(1, sum(1 for row in group if row["visual_prediction"] != row["target_button"])),
                "overcorrection_rate": sum(1 for row in group if row["overcorrection"]) / max(1, sum(1 for row in group if row["visual_prediction"] == row["target_button"])),
                "context_misfire_rate": sum(1 for row in group if row["context_misfire"]) / len(group),
                "posterior_margin_mean": sum(safe_float(row["posterior_margin"], 0.0) for row in group) / len(group),
            }
        )
        out.append(item)
    return out


def confusion_matrix(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    counts = Counter((row["baseline"], row["target_button"], row["predicted_button"]) for row in rows)
    return [
        {"baseline": baseline, "target_button": target, "predicted_button": pred, "count": count}
        for (baseline, target, pred), count in sorted(counts.items())
    ]


def derived_metrics(aggregates: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_key = {(row["baseline"], row["phase"], str(row["n_buttons"]), row["skill_level"]): row for row in aggregates}
    out = []
    for baseline in sorted({row["baseline"] for row in aggregates}):
        n2 = [row for row in aggregates if row["baseline"] == baseline and str(row["n_buttons"]) == "2"]
        n4 = [row for row in aggregates if row["baseline"] == baseline and str(row["n_buttons"]) == "4"]
        n2_mis = sum(safe_float(row["misinput_rate"], 0.0) for row in n2) / max(1, len(n2))
        n4_mis = sum(safe_float(row["misinput_rate"], 0.0) for row in n4) / max(1, len(n4))
        out.append({"metric": "N_sensitivity_misinput_N4_minus_N2", "baseline": baseline, "value": n4_mis - n2_mis})
        expert = [row for row in aggregates if row["baseline"] == baseline and row["skill_level"] == "expert"]
        expert_over = sum(safe_float(row["overcorrection_rate"], 0.0) for row in expert) / max(1, len(expert))
        out.append({"metric": "expert_control_risk", "baseline": baseline, "value": expert_over})
    for phase in PHASES:
        for n_buttons in ("2", "3", "4"):
            key_e = ("SituationUserSkillBayesian", phase, n_buttons, "beginner")
            key_c = ("UserSpecificHitbox", phase, n_buttons, "beginner")
            if key_e in by_key and key_c in by_key:
                value = safe_float(by_key[key_e]["correction_success_rate"], 0.0) - safe_float(by_key[key_c]["correction_success_rate"], 0.0)
                out.append({"metric": "beginner_helpfulness_vs_user_specific", "baseline": key_e[0], "phase": phase, "n_buttons": n_buttons, "value": value})
    return out


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--trials-per-condition", type=int, default=36)
    args = parser.parse_args()
    rows = evaluate(args.trials_per_condition)
    print(f"aggregate_rows={len(rows)}")
    print(REPORTS_DIR / "baseline_comparison.csv")


if __name__ == "__main__":
    main()

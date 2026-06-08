from __future__ import annotations

from collections import defaultdict
from typing import Iterable


def as_bool(value) -> bool:
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in {"true", "1", "yes"}


def is_valid_label(row: dict) -> bool:
    return str(row.get("required_action", "")) in {"Attack", "Dodge"} and not as_bool(row.get("invalid_touch", False))


def prediction_correct(row: dict, field: str) -> bool:
    if not is_valid_label(row):
        return False
    return str(row.get(field, "")) == str(row.get("required_action", ""))


def accuracy(rows: Iterable[dict], prediction_field: str = "final_executed_action") -> float:
    selected = [row for row in rows if is_valid_label(row)]
    if not selected:
        return 0.0
    return sum(1 for row in selected if prediction_correct(row, prediction_field)) / len(selected)


def subset_accuracy(rows: list[dict], prediction_field: str, subset_field: str) -> float:
    selected = [row for row in rows if as_bool(row.get(subset_field, False)) and is_valid_label(row)]
    if not selected:
        return 0.0
    return sum(1 for row in selected if prediction_correct(row, prediction_field)) / len(selected)


def clear_subset_accuracy(rows: list[dict], prediction_field: str) -> float:
    selected = [row for row in rows if not as_bool(row.get("is_ambiguous", False)) and is_valid_label(row)]
    if not selected:
        return 0.0
    return sum(1 for row in selected if prediction_correct(row, prediction_field)) / len(selected)


def invalid_touch_rate(rows: list[dict]) -> float:
    if not rows:
        return 0.0
    return sum(1 for row in rows if as_bool(row.get("invalid_touch", False))) / len(rows)


def correction_success_rate(rows: list[dict]) -> float:
    candidates = [
        row
        for row in rows
        if as_bool(row.get("is_ambiguous", False))
        and str(row.get("required_action", "")) in {"Attack", "Dodge"}
        and str(row.get("visual_boundary_prediction", "")) != str(row.get("required_action", ""))
    ]
    if not candidates:
        return 0.0
    return sum(1 for row in candidates if str(row.get("final_executed_action", "")) == str(row.get("required_action", ""))) / len(candidates)


def overcorrection_rate(rows: list[dict]) -> float:
    candidates = [
        row
        for row in rows
        if str(row.get("required_action", "")) in {"Attack", "Dodge"}
        and str(row.get("visual_boundary_prediction", "")) == str(row.get("required_action", ""))
    ]
    if not candidates:
        return 0.0
    return sum(1 for row in candidates if str(row.get("final_executed_action", "")) != str(row.get("required_action", ""))) / len(candidates)


def no_correction_when_uncertain_rate(rows: list[dict]) -> float:
    uncertain = [
        row
        for row in rows
        if str(row.get("required_action", "")) in {"Attack", "Dodge"}
        and (float(row.get("max_posterior") or 0.0) < float(row.get("tau") or 0.0) or float(row.get("posterior_gap") or 0.0) < float(row.get("delta") or 0.0))
    ]
    if not uncertain:
        return 0.0
    return sum(1 for row in uncertain if not as_bool(row.get("safety_gate_passed", False))) / len(uncertain)


def mean_numeric(rows: Iterable[dict], field: str) -> float:
    values = []
    for row in rows:
        try:
            values.append(float(row.get(field) or 0.0))
        except (TypeError, ValueError):
            continue
    return sum(values) / len(values) if values else 0.0


def grouped(rows: Iterable[dict], field: str) -> dict[str, list[dict]]:
    result: dict[str, list[dict]] = defaultdict(list)
    for row in rows:
        result[str(row.get(field, ""))].append(row)
    return result


def decoder_metric_rows(rows: list[dict]) -> list[dict]:
    baselines = [
        ("visual_boundary", "visual_boundary_prediction"),
        ("expanded_hitbox", "expanded_hitbox_prediction"),
        ("user_gaussian", "user_gaussian_prediction"),
        ("context_prior_only", "context_prior_only_prediction"),
        ("context_bayesian", "bayesian_prediction"),
        ("final", "final_executed_action"),
    ]
    output = []
    for name, field in baselines:
        output.append(
            {
                "group": "overall",
                "value": "all",
                "baseline": name,
                "n": len(rows),
                "overall_accuracy": accuracy(rows, field),
                "ambiguous_subset_accuracy": subset_accuracy(rows, field, "is_ambiguous"),
                "clear_subset_accuracy": clear_subset_accuracy(rows, field),
                "invalid_touch_rate": invalid_touch_rate(rows),
                "correction_success_rate": correction_success_rate(rows) if field == "final_executed_action" else "",
                "overcorrection_rate": overcorrection_rate(rows) if field == "final_executed_action" else "",
                "no_correction_when_uncertain_rate": no_correction_when_uncertain_rate(rows) if field == "final_executed_action" else "",
                "reaction_time_mean": mean_numeric(rows, "reaction_time_ms"),
            }
        )
    for group_field in ["condition", "enemy_state"]:
        for value, group_rows in grouped(rows, group_field).items():
            output.append(
                {
                    "group": group_field,
                    "value": value,
                    "baseline": "final",
                    "n": len(group_rows),
                    "overall_accuracy": accuracy(group_rows),
                    "ambiguous_subset_accuracy": subset_accuracy(group_rows, "final_executed_action", "is_ambiguous"),
                    "clear_subset_accuracy": "",
                    "invalid_touch_rate": invalid_touch_rate(group_rows),
                    "correction_success_rate": correction_success_rate(group_rows),
                    "overcorrection_rate": overcorrection_rate(group_rows),
                    "no_correction_when_uncertain_rate": no_correction_when_uncertain_rate(group_rows),
                    "reaction_time_mean": mean_numeric(group_rows, "reaction_time_ms"),
                }
            )
    return output

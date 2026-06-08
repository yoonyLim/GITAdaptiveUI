from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from typing import Any

from analysis_multigame_scene.src.common import (
    REPORTS_DIR,
    TEACHER_LABELS_DIR,
    clamp01,
    read_jsonl,
    safe_float,
    write_csv,
    write_jsonl,
)


BLOCKING_FLAGS = {
    "low_confidence",
    "unclear_frame",
    "non_gameplay",
    "ambiguous_state",
}


def map_risk_state(threat_level: str) -> str:
    threat = (threat_level or "unknown").lower()
    if threat in {"none", "warning"}:
        return "safe_or_warning"
    if threat in {"active", "critical"}:
        return threat
    return "unknown"


def map_action_window_prior(action_window: str) -> str:
    action = (action_window or "unknown").lower()
    if action in {"engage", "avoid"}:
        return action
    return "neutral"


def exclusion_reasons(label: dict[str, Any], confidence_threshold: float) -> list[str]:
    reasons: list[str] = []
    if label.get("should_use_for_training") is False:
        reasons.append("should_use_for_training_false")
    if safe_float(label.get("confidence"), 0.0) < confidence_threshold:
        reasons.append("confidence_below_threshold")
    if label.get("schema_errors"):
        reasons.append("schema_errors_present")
    flags = {str(flag) for flag in label.get("quality_flags", [])}
    blocking = sorted(flags.intersection(BLOCKING_FLAGS))
    if blocking:
        reasons.append("blocking_quality_flags=" + "|".join(blocking))
    return reasons


def build_focused_label(label: dict[str, Any], confidence_threshold: float) -> dict[str, Any]:
    demand = label.get("interaction_demand", {}) or {}
    ui_phase = str(label.get("ui_phase", "unknown"))
    threat_level = str(label.get("threat_level", "unknown"))
    action_window = str(label.get("action_window", "unknown"))
    risk_state = map_risk_state(threat_level)
    action_window_prior = map_action_window_prior(action_window)
    temporal_urgency = clamp01(demand.get("temporal_urgency", 0.0))
    confidence = clamp01(label.get("confidence", 0.0))
    reasons = exclusion_reasons(label, confidence_threshold)
    if ui_phase != "gameplay":
        # Keep the row for ui_phase analysis, but do not use it for
        # Attack/Dodge situation-prior training.
        reasons.append("ui_phase_not_gameplay")

    return {
        "sample_id": label.get("sample_id", ""),
        "source_dataset": label.get("source_dataset", ""),
        "ui_phase": ui_phase,
        "risk_state": risk_state,
        "action_window_prior": action_window_prior,
        "temporal_urgency": temporal_urgency,
        "teacher_confidence": confidence,
        "should_use_for_training": not reasons,
        "exclude_reasons": reasons,
        "label_source": "mapped_from_codex_teacher_v1",
        "original_label_source": label.get("label_source", ""),
        "original_threat_level": threat_level,
        "original_action_window": action_window,
        "original_urgency_level": label.get("urgency_level", "unknown"),
        "original_dominant_mode": label.get("dominant_mode", "unknown"),
        "quality_flags": label.get("quality_flags", []),
        "schema_errors": label.get("schema_errors", []),
    }


def counts_by(rows: list[dict[str, Any]], key: str) -> list[dict[str, Any]]:
    counter = Counter(str(row.get(key, "")) for row in rows)
    return [{"field": key, "value": value, "count": count} for value, count in sorted(counter.items())]


def write_markdown_summary(rows: list[dict[str, Any]], train_rows: list[dict[str, Any]], threshold: float) -> None:
    source_counts = Counter(str(row.get("source_dataset", "")) for row in rows)
    train_source_counts = Counter(str(row.get("source_dataset", "")) for row in train_rows)
    risk_counts = Counter(str(row.get("risk_state", "")) for row in train_rows)
    action_counts = Counter(str(row.get("action_window_prior", "")) for row in train_rows)
    exclusion_counts: Counter[str] = Counter()
    for row in rows:
        for reason in row.get("exclude_reasons", []):
            exclusion_counts[str(reason)] += 1

    lines = [
        "# Focused scene-prior label mapping summary",
        "",
        f"- input teacher labels: {len(rows)}",
        f"- usable focused training labels: {len(train_rows)}",
        f"- confidence threshold: {threshold}",
        "",
        "## Training label distribution",
        "",
        "| risk_state | count |",
        "|---|---:|",
    ]
    for key, count in sorted(risk_counts.items()):
        lines.append(f"| {key} | {count} |")
    lines.extend(["", "| action_window_prior | count |", "|---|---:|"])
    for key, count in sorted(action_counts.items()):
        lines.append(f"| {key} | {count} |")
    lines.extend(["", "## Source coverage", "", "| source_dataset | all labels | usable labels |", "|---|---:|---:|"])
    for source in sorted(source_counts):
        lines.append(f"| {source} | {source_counts[source]} | {train_source_counts[source]} |")
    lines.extend(["", "## Exclusion reasons", "", "| reason | count |", "|---|---:|"])
    if exclusion_counts:
        for reason, count in sorted(exclusion_counts.items()):
            lines.append(f"| {reason} | {count} |")
    else:
        lines.append("| none | 0 |")
    lines.extend(
        [
            "",
            "## Interpretation",
            "",
            "기존 Codex teacher 라벨은 폐기하지 않고 focused schema로 재사용한다.",
            "`dominant_mode`와 넓은 interaction-demand 변수는 발표/해석용으로 남기고, 실제 student 학습 target은 `risk_state`, `action_window_prior`, `temporal_urgency`로 제한한다.",
            "`ui_phase != gameplay` 또는 낮은 confidence/불명확 프레임은 Attack/Dodge prior 학습에서 제외한다.",
        ]
    )
    (REPORTS_DIR / "focused_label_mapping_summary_ko.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--confidence-threshold", type=float, default=0.45)
    args = parser.parse_args()

    labels = read_jsonl(TEACHER_LABELS_DIR / "teacher_labels.jsonl")
    focused_rows = [build_focused_label(label, args.confidence_threshold) for label in labels]
    train_rows = [row for row in focused_rows if row.get("should_use_for_training")]

    write_jsonl(TEACHER_LABELS_DIR / "focused_labels.jsonl", focused_rows)
    write_jsonl(TEACHER_LABELS_DIR / "focused_training_labels.jsonl", train_rows)

    summary_rows: list[dict[str, Any]] = []
    for key in ["ui_phase", "risk_state", "action_window_prior", "source_dataset"]:
        for row in counts_by(focused_rows, key):
            row["scope"] = "all"
            summary_rows.append(row)
        for row in counts_by(train_rows, key):
            row["scope"] = "training"
            summary_rows.append(row)
    write_csv(REPORTS_DIR / "focused_label_distribution.csv", summary_rows)

    exclusion_rows = []
    grouped: dict[str, list[str]] = defaultdict(list)
    for row in focused_rows:
        for reason in row.get("exclude_reasons", []):
            grouped[reason].append(str(row.get("sample_id")))
    for reason, sample_ids in sorted(grouped.items()):
        exclusion_rows.append({"reason": reason, "count": len(sample_ids), "sample_ids_preview": "|".join(sample_ids[:12])})
    write_csv(REPORTS_DIR / "focused_label_exclusions.csv", exclusion_rows)
    write_markdown_summary(focused_rows, train_rows, args.confidence_threshold)

    print(f"focused_labels={len(focused_rows)}")
    print(f"focused_training_labels={len(train_rows)}")
    print(TEACHER_LABELS_DIR / "focused_labels.jsonl")
    print(REPORTS_DIR / "focused_label_mapping_summary_ko.md")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path


SUMMARY_FILE = "evaluation_stage_summary.jsonl"
TOUCH_FILE = "evaluation_touch_events.jsonl"


def iter_jsonl(path):
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, 1):
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{line_number}: invalid JSONL: {exc}") from exc


def key(record):
    return (
        record.get("participant_id", ""),
        record.get("condition", ""),
        str(record.get("stage_number", "")),
        record.get("stage_label", ""),
    )


def new_bucket(record):
    return {
        "participant_id": record.get("participant_id", ""),
        "condition": record.get("condition", ""),
        "stage_number": record.get("stage_number", ""),
        "stage_label": record.get("stage_label", ""),
        "runs": 0,
        "duration_seconds": 0.0,
        "button_presses": 0,
        "touch_events": 0,
        "expected_match_count": 0,
        "mis_touch_count": 0,
        "invalid_touch_count": 0,
        "rejected_count": 0,
        "preserved_count": 0,
        "corrected_count": 0,
        "ambiguous_count": 0,
        "cooldown_wasted_count": 0,
        "action_first_count": 0,
        "cognitive_first_count": 0,
        "damage_taken": 0,
        "healing_done": 0,
        "failed_count": 0,
        "skipped_count": 0,
        "avg_touch_error_px_sum": 0.0,
        "avg_posterior_gap_sum": 0.0,
        "avg_policy_error_tolerance_sum": 0.0,
        "avg_policy_correction_strength_sum": 0.0,
    }


def add_summary(bucket, record):
    bucket["runs"] += 1
    for field in [
        "duration_seconds",
        "avg_touch_error_px",
        "avg_posterior_gap",
        "avg_policy_error_tolerance",
        "avg_policy_correction_strength",
    ]:
        bucket[f"{field}_sum" if field.startswith("avg_") else field] += float(record.get(field) or 0)

    for field in [
        "button_presses",
        "touch_events",
        "expected_match_count",
        "mis_touch_count",
        "invalid_touch_count",
        "rejected_count",
        "preserved_count",
        "corrected_count",
        "ambiguous_count",
        "cooldown_wasted_count",
        "action_first_count",
        "cognitive_first_count",
        "damage_taken",
        "healing_done",
    ]:
        bucket[field] += int(record.get(field) or 0)

    bucket["failed_count"] += int(bool(record.get("failed", False)))
    bucket["skipped_count"] += int(bool(record.get("skipped", False)))


def flatten(bucket):
    runs = max(1, bucket["runs"])
    touches = max(1, bucket["touch_events"])
    row = dict(bucket)
    row["mean_duration_seconds"] = bucket["duration_seconds"] / runs
    row["mis_touch_rate"] = bucket["mis_touch_count"] / touches
    row["invalid_touch_rate"] = bucket["invalid_touch_count"] / touches
    row["rejected_rate"] = bucket["rejected_count"] / touches
    row["corrected_rate"] = bucket["corrected_count"] / touches
    row["preserved_rate"] = bucket["preserved_count"] / touches
    row["ambiguous_rate"] = bucket["ambiguous_count"] / touches
    row["cooldown_wasted_rate"] = bucket["cooldown_wasted_count"] / touches
    row["expected_match_rate"] = bucket["expected_match_count"] / touches
    row["mean_touch_error_px"] = bucket["avg_touch_error_px_sum"] / runs
    row["mean_posterior_gap"] = bucket["avg_posterior_gap_sum"] / runs
    row["mean_policy_error_tolerance"] = bucket["avg_policy_error_tolerance_sum"] / runs
    row["mean_policy_correction_strength"] = bucket["avg_policy_correction_strength_sum"] / runs
    return row


def summarize(input_dir, output_dir):
    input_dir = Path(input_dir)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    buckets = {}
    summary_files = sorted(input_dir.rglob(SUMMARY_FILE))
    touch_files = sorted(input_dir.rglob(TOUCH_FILE))

    for path in summary_files:
        for record in iter_jsonl(path):
            bucket = buckets.setdefault(key(record), new_bucket(record))
            add_summary(bucket, record)

    rows = [flatten(bucket) for bucket in buckets.values()]
    rows.sort(key=lambda row: (row["participant_id"], row["condition"], str(row["stage_number"])))

    csv_path = output_dir / "evaluation_summary_by_participant_condition_stage.csv"
    json_path = output_dir / "evaluation_summary_by_participant_condition_stage.json"
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")

    fieldnames = [
        "participant_id",
        "condition",
        "stage_number",
        "stage_label",
        "runs",
        "touch_events",
        "button_presses",
        "mean_duration_seconds",
        "expected_match_rate",
        "mis_touch_rate",
        "invalid_touch_rate",
        "rejected_rate",
        "corrected_rate",
        "preserved_rate",
        "ambiguous_rate",
        "cooldown_wasted_rate",
        "damage_taken",
        "healing_done",
        "failed_count",
        "skipped_count",
        "action_first_count",
        "cognitive_first_count",
        "mean_touch_error_px",
        "mean_posterior_gap",
        "mean_policy_error_tolerance",
        "mean_policy_correction_strength",
    ]

    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)

    return json_path, csv_path, len(summary_files), len(touch_files)


def main():
    parser = argparse.ArgumentParser(description="Summarize ADUI user-evaluation stage logs.")
    parser.add_argument("input_dir", help="Persistent adui_sessions folder or a parent folder containing session logs.")
    parser.add_argument("--output-dir", default=None, help="Output folder. Defaults to input_dir.")
    args = parser.parse_args()

    output_dir = args.output_dir or args.input_dir
    json_path, csv_path, summary_count, touch_count = summarize(args.input_dir, output_dir)
    print(f"wrote {json_path}")
    print(f"wrote {csv_path}")
    print(f"read stage_summary_files={summary_count} touch_event_files={touch_count}")


if __name__ == "__main__":
    main()

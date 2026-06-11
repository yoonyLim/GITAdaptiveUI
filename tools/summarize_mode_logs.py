#!/usr/bin/env python3
import argparse
import csv
import json
from pathlib import Path


POLICY_FIELDS = [
    "policy_visibility",
    "policy_emphasis",
    "policy_density",
    "policy_position_constraint",
    "policy_error_tolerance",
]


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


def mode_of(record):
    return record.get("interaction_mode") or record.get("mode") or "Unknown"


def new_bucket(mode):
    return {
        "mode": mode,
        "trial_count": 0,
        "policy_event_count": 0,
        "correction_allowed": 0,
        "correction_rejected": 0,
        "invalid_touch": 0,
        "posterior_gap_sum": 0.0,
        "posterior_gap_count": 0,
        "damage_taken": 0,
        "cooldown_wasted": 0,
        "policy_sums": {field: 0.0 for field in POLICY_FIELDS},
        "policy_counts": {field: 0 for field in POLICY_FIELDS},
    }


def add_policy(bucket, record):
    bucket["policy_event_count"] += 1
    for field in POLICY_FIELDS:
        value = record.get(field)
        if isinstance(value, (int, float)):
            bucket["policy_sums"][field] += float(value)
            bucket["policy_counts"][field] += 1


def add_trial(bucket, record):
    bucket["trial_count"] += 1
    safety_reason = str(record.get("safety_gate_reason", ""))
    safety_passed = bool(record.get("safety_gate_passed", False))
    invalid = bool(record.get("invalid_touch", False))

    if safety_passed or safety_reason.startswith("correction"):
        bucket["correction_allowed"] += 1
    if invalid or "rejected" in safety_reason or "blocked" in safety_reason or "below" in safety_reason or "outside" in safety_reason:
        bucket["correction_rejected"] += 1
    if invalid:
        bucket["invalid_touch"] += 1

    posterior_gap = record.get("posterior_gap")
    if isinstance(posterior_gap, (int, float)):
        bucket["posterior_gap_sum"] += float(posterior_gap)
        bucket["posterior_gap_count"] += 1

    bucket["damage_taken"] += int(record.get("damage_taken") or 0)
    bucket["cooldown_wasted"] += int(bool(record.get("cooldown_wasted", False)))
    add_policy(bucket, record)


def flatten_bucket(bucket):
    row = {
        "mode": bucket["mode"],
        "trial_count": bucket["trial_count"],
        "policy_event_count": bucket["policy_event_count"],
        "correction_allowed": bucket["correction_allowed"],
        "correction_rejected": bucket["correction_rejected"],
        "invalid_touch": bucket["invalid_touch"],
        "mean_posterior_gap": (
            bucket["posterior_gap_sum"] / bucket["posterior_gap_count"]
            if bucket["posterior_gap_count"]
            else 0.0
        ),
        "damage_taken": bucket["damage_taken"],
        "cooldown_wasted": bucket["cooldown_wasted"],
    }

    for field in POLICY_FIELDS:
        count = bucket["policy_counts"][field]
        row[f"mean_{field}"] = bucket["policy_sums"][field] / count if count else 0.0
    return row


def summarize(input_dir, output_dir):
    input_dir = Path(input_dir)
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    buckets = {}
    main_files = sorted(input_dir.rglob("main_trials.jsonl"))
    policy_files = sorted(input_dir.rglob("mode_policy_events.jsonl"))

    for path in main_files:
        for record in iter_jsonl(path):
            mode = mode_of(record)
            bucket = buckets.setdefault(mode, new_bucket(mode))
            add_trial(bucket, record)

    for path in policy_files:
        for record in iter_jsonl(path):
            mode = mode_of(record)
            bucket = buckets.setdefault(mode, new_bucket(mode))
            add_policy(bucket, record)

    rows = [flatten_bucket(bucket) for bucket in buckets.values()]
    rows.sort(key=lambda row: row["mode"])

    json_path = output_dir / "mode_summary.json"
    csv_path = output_dir / "mode_summary.csv"
    json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")

    fieldnames = [
        "mode",
        "trial_count",
        "policy_event_count",
        "correction_allowed",
        "correction_rejected",
        "invalid_touch",
        "mean_posterior_gap",
        "damage_taken",
        "cooldown_wasted",
    ] + [f"mean_{field}" for field in POLICY_FIELDS]

    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    return json_path, csv_path, len(main_files), len(policy_files)


def main():
    parser = argparse.ArgumentParser(description="Summarize ADUI mode-level runtime logs.")
    parser.add_argument("input_dir", help="Session output directory or parent directory containing JSONL logs.")
    parser.add_argument("--output-dir", default=None, help="Directory for mode_summary.json/csv. Defaults to input_dir.")
    args = parser.parse_args()

    output_dir = args.output_dir or args.input_dir
    json_path, csv_path, main_count, policy_count = summarize(args.input_dir, output_dir)
    print(f"wrote {json_path}")
    print(f"wrote {csv_path}")
    print(f"read main_trials={main_count} mode_policy_events={policy_count}")


if __name__ == "__main__":
    main()

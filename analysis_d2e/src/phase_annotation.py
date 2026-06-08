from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import read_csv, read_jsonl, write_csv, write_jsonl
from analysis_d2e.src.paths import PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.schemas import PHASES


ANNOTATION_FIELDS = [
    "sample_id",
    "game_id",
    "episode_id",
    "frame_idx",
    "frame_path",
    "current_phase",
    "current_phase_source",
    "has_action_input",
    "dominant_atomic_action",
    "action_label_confidence",
    "manual_phase",
    "notes",
]


def load_samples(samples_path: Path | None = None) -> list[dict[str, Any]]:
    return read_jsonl(samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl"))


def export_phase_annotation_template(
    output_path: Path | None = None,
    samples_path: Path | None = None,
    max_per_game: int = 80,
    include_unknown_only: bool = False,
) -> list[dict[str, Any]]:
    ensure_dirs()
    output_path = output_path or (REPORTS_DIR / "phase_annotation_template.csv")
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for sample in load_samples(samples_path):
        if include_unknown_only and sample.get("phase") != "unknown":
            continue
        grouped[str(sample.get("game_id", "unknown"))].append(sample)

    rows: list[dict[str, Any]] = []
    for _game, samples in sorted(grouped.items()):
        samples = sorted(
            samples,
            key=lambda row: (
                row.get("phase") != "unknown",
                -float(row.get("action_label_confidence", 0.0) or 0.0),
                int(row.get("frame_idx", 0) or 0),
            ),
        )
        for sample in samples[:max_per_game]:
            rows.append(
                {
                    "sample_id": sample.get("sample_id", ""),
                    "game_id": sample.get("game_id", ""),
                    "episode_id": sample.get("episode_id", ""),
                    "frame_idx": sample.get("frame_idx", ""),
                    "frame_path": sample.get("frame_path", ""),
                    "current_phase": sample.get("phase", ""),
                    "current_phase_source": sample.get("phase_source", ""),
                    "has_action_input": sample.get("has_action_input", ""),
                    "dominant_atomic_action": sample.get("dominant_atomic_action", ""),
                    "action_label_confidence": sample.get("action_label_confidence", ""),
                    "manual_phase": "",
                    "notes": "",
                }
            )
    write_csv(output_path, rows, fieldnames=ANNOTATION_FIELDS)
    return rows


def apply_phase_annotations(
    annotations_path: Path,
    samples_path: Path | None = None,
    output_path: Path | None = None,
) -> dict[str, Any]:
    ensure_dirs()
    samples_path = samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl")
    output_path = output_path or samples_path
    samples = load_samples(samples_path)
    annotations = read_csv(annotations_path)
    phase_by_id = {
        str(row.get("sample_id")): str(row.get("manual_phase", "")).strip().lower()
        for row in annotations
        if str(row.get("manual_phase", "")).strip().lower() in set(PHASES)
    }
    updated = 0
    for sample in samples:
        manual_phase = phase_by_id.get(str(sample.get("sample_id")))
        if manual_phase:
            sample["phase"] = manual_phase
            sample["phase_source"] = "manual"
            updated += 1
    write_jsonl(output_path, samples)
    counts = Counter((sample.get("game_id", ""), sample.get("phase", ""), sample.get("phase_source", "")) for sample in samples)
    rows = [
        {"game_id": game, "phase": phase, "phase_source": phase_source, "rows": count}
        for (game, phase, phase_source), count in sorted(counts.items())
    ]
    write_csv(REPORTS_DIR / "phase_annotation_apply_summary.csv", rows)
    return {"updated_rows": updated, "total_rows": len(samples), "output_path": str(output_path)}


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    export_parser = sub.add_parser("export")
    export_parser.add_argument("--output", type=Path, default=None)
    export_parser.add_argument("--samples", type=Path, default=None)
    export_parser.add_argument("--max-per-game", type=int, default=80)
    export_parser.add_argument("--unknown-only", action="store_true")
    apply_parser = sub.add_parser("apply")
    apply_parser.add_argument("--annotations", type=Path, required=True)
    apply_parser.add_argument("--samples", type=Path, default=None)
    apply_parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args()

    if args.command == "export":
        rows = export_phase_annotation_template(args.output, args.samples, args.max_per_game, args.unknown_only)
        print(f"annotation_rows={len(rows)}")
        print(args.output or (REPORTS_DIR / "phase_annotation_template.csv"))
    else:
        result = apply_phase_annotations(args.annotations, args.samples, args.output)
        print(result)


if __name__ == "__main__":
    main()


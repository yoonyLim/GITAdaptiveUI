from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import read_jsonl, write_csv, write_jsonl
from analysis_d2e.src.paths import PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.threat_state_features import estimate_threat_state


AUX_THREAT_GAMES = {"brotato", "vampire_survivors"}


def build_threat_auxiliary(samples_path: Path | None = None, output_path: Path | None = None, write_outputs: bool = True) -> list[dict[str, Any]]:
    ensure_dirs()
    samples_path = samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl")
    output_path = output_path or (PROCESSED_DIR / "threat_state_auxiliary_samples.jsonl")
    samples = [
        row
        for row in read_jsonl(samples_path)
        if str(row.get("game_id", "")).lower().replace(" ", "_") in AUX_THREAT_GAMES
    ]
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        grouped[(str(sample.get("game_id")), str(sample.get("episode_id")))].append(sample)
    rows: list[dict[str, Any]] = []
    for (_game, _episode), group in grouped.items():
        group = sorted(group, key=lambda item: int(float(item.get("frame_idx", 0) or 0)))
        previous = None
        for sample in group:
            state = estimate_threat_state(sample, previous)
            rows.append(
                {
                    "sample_id": sample.get("sample_id"),
                    "game_id": sample.get("game_id"),
                    "episode_id": sample.get("episode_id"),
                    "frame_idx": sample.get("frame_idx"),
                    "frame_path": sample.get("frame_path"),
                    **state,
                    "recommended_role": "threat_state_auxiliary_not_action_label",
                }
            )
            previous = sample
    if write_outputs:
        write_jsonl(output_path, rows)
        summary = []
        for game in sorted({str(row.get("game_id")) for row in rows}):
            group = [row for row in rows if str(row.get("game_id")) == game]
            summary.append(
                {
                    "game_id": game,
                    "rows": len(group),
                    "mean_enemy_count_proxy": round(sum(float(row["enemy_count_proxy"]) for row in group) / max(1, len(group)), 4),
                    "enemy_approaching_rate": round(sum(1 for row in group if row["enemy_approaching_bool"]) / max(1, len(group)), 4),
                    "projectile_visible_proxy_rate": round(sum(1 for row in group if row["projectile_visible_proxy"]) / max(1, len(group)), 4),
                    "mean_threat_score": round(sum(float(row["threat_score"]) for row in group) / max(1, len(group)), 4),
                    "use_as_action_label": False,
                }
            )
        write_csv(REPORTS_DIR / "threat_state_auxiliary_summary.csv", summary)
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=Path, default=None)
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args()
    rows = build_threat_auxiliary(args.samples, args.output)
    print(json.dumps({"status": "ok", "rows": len(rows)}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

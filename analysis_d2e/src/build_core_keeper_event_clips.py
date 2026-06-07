from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import read_jsonl, safe_float, write_csv, write_jsonl
from analysis_d2e.src.offense_defense_mapping import classify_2key_event
from analysis_d2e.src.paths import PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.threat_state_features import detect_ui_overlay_proxy, estimate_threat_state


def _is_core_keeper(game_id: Any) -> bool:
    return str(game_id).lower().replace(" ", "_") == "core_keeper"


def _group_by_episode(samples: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        grouped[str(sample.get("episode_id", "unknown"))].append(sample)
    return {key: sorted(value, key=lambda item: int(safe_float(item.get("frame_idx"), 0))) for key, value in grouped.items()}


def _clip_paths(group: list[dict[str, Any]], center_frame_idx: int, pre_frames: int, post_frames: int) -> list[str]:
    start = center_frame_idx - pre_frames
    end = center_frame_idx + post_frames
    paths = [str(row.get("frame_path")) for row in group if start <= int(safe_float(row.get("frame_idx"), 0)) <= end and row.get("frame_path")]
    return paths or []


def build_core_keeper_event_clips(
    samples_path: Path | None = None,
    output_path: Path | None = None,
    max_clips: int | None = None,
    pre_frames: int = 15,
    post_frames: int = 30,
    write_outputs: bool = True,
) -> list[dict[str, Any]]:
    ensure_dirs()
    samples_path = samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl")
    output_path = output_path or (PROCESSED_DIR / "core_keeper_2key_event_clips.jsonl")
    samples = [row for row in read_jsonl(samples_path) if _is_core_keeper(row.get("game_id"))]
    clips: list[dict[str, Any]] = []
    for episode_id, group in _group_by_episode(samples).items():
        previous: dict[str, Any] | None = None
        for sample in group:
            ui_overlay_open = detect_ui_overlay_proxy(sample.get("frame_path"))
            label = classify_2key_event(sample.get("raw_input"), game_id="Core_Keeper", ui_overlay_open=ui_overlay_open)
            state = estimate_threat_state(sample, previous)
            frame_idx = int(safe_float(sample.get("frame_idx"), 0))
            clip_frame_paths = _clip_paths(group, frame_idx, pre_frames, post_frames)
            selection_flags = {
                "has_enemy_visible_proxy": state["enemy_count_proxy"] > 0,
                "enemy_distance_decreases": state["enemy_distance_delta"] < -0.025,
                "has_offense_input": label.offense_score > 0,
                "has_defense_candidate": label.defense_score > 0,
                "has_movement_input": label.movement_score > 0,
            }
            clips.append(
                {
                    "clip_id": f"core_keeper_{episode_id}_{frame_idx:08d}",
                    "dataset_id": sample.get("dataset_id", "D2E-480p"),
                    "game_id": "Core_Keeper",
                    "episode_id": episode_id,
                    "center_sample_id": sample.get("sample_id"),
                    "center_frame_idx": frame_idx,
                    "clip_window_sec": "-0.5:+1.0",
                    "clip_frame_paths": clip_frame_paths,
                    "center_frame_path": sample.get("frame_path"),
                    "raw_input": sample.get("raw_input"),
                    "event_label": label.label,
                    "event_label_confidence": label.confidence,
                    "event_label_reason": label.label_reason,
                    "ui_overlay_open_proxy": ui_overlay_open,
                    "offense_score": label.offense_score,
                    "defense_score": label.defense_score,
                    "movement_score": label.movement_score,
                    "movement_dx": label.movement_dx,
                    "movement_dy": label.movement_dy,
                    "movement_direction": label.movement_direction,
                    **state,
                    **selection_flags,
                    "source_sample_path": str(samples_path),
                }
            )
            previous = sample
            if max_clips and len(clips) >= max_clips:
                break
        if max_clips and len(clips) >= max_clips:
            break

    if write_outputs:
        write_jsonl(output_path, clips)
        label_counts = Counter(row["event_label"] for row in clips)
        summary = [
            {
                "game_id": "Core_Keeper",
                "clips": len(clips),
                "offense": label_counts.get("offense", 0),
                "explicit_defense": label_counts.get("explicit_defense", 0),
                "movement_only": label_counts.get("movement_only", 0),
                "unknown_ignore": label_counts.get("unknown_ignore", 0),
                "mean_threat_score": round(sum(float(row["threat_score"]) for row in clips) / max(1, len(clips)), 4),
                "enemy_visible_proxy_rows": sum(1 for row in clips if row["has_enemy_visible_proxy"]),
                "approaching_rows": sum(1 for row in clips if row["enemy_distance_decreases"]),
            }
        ]
        write_csv(REPORTS_DIR / "core_keeper_2key_clip_summary.csv", summary)
        state_summary = [
            {
                "metric": "mean_enemy_count_proxy",
                "value": round(sum(float(row["enemy_count_proxy"]) for row in clips) / max(1, len(clips)), 4),
            },
            {
                "metric": "enemy_approaching_rate",
                "value": round(sum(1 for row in clips if row["enemy_approaching_bool"]) / max(1, len(clips)), 4),
            },
            {
                "metric": "projectile_visible_proxy_rate",
                "value": round(sum(1 for row in clips if row["projectile_visible_proxy"]) / max(1, len(clips)), 4),
            },
            {
                "metric": "mean_surroundedness",
                "value": round(sum(float(row["surroundedness"]) for row in clips) / max(1, len(clips)), 4),
            },
        ]
        write_csv(REPORTS_DIR / "core_keeper_state_feature_summary.csv", state_summary)
    return clips


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=Path, default=None)
    parser.add_argument("--output", type=Path, default=None)
    parser.add_argument("--max-clips", type=int, default=None)
    args = parser.parse_args()
    clips = build_core_keeper_event_clips(args.samples, args.output, args.max_clips)
    print(json.dumps({"status": "ok", "clips": len(clips)}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

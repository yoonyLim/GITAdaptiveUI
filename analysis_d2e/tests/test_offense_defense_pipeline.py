from __future__ import annotations

import json
from pathlib import Path

from analysis_d2e.src.build_core_keeper_event_clips import build_core_keeper_event_clips
from analysis_d2e.src.io_utils import read_jsonl, write_jsonl
from analysis_d2e.src.offense_defense_mapping import classify_2key_event
from analysis_d2e.src.synthetic_state_validation import run_synthetic_state_validation
from analysis_d2e.src.threat_state_features import estimate_threat_state
from analysis_d2e.src.train_offense_defense_prior import train_offense_defense_prior


def _image(path: Path, color: tuple[int, int, int]) -> str:
    from PIL import Image, ImageDraw

    image = Image.new("RGB", (96, 96), (30, 35, 45))
    draw = ImageDraw.Draw(image)
    draw.ellipse((20, 20, 35, 35), fill=color)
    draw.rectangle((70, 70, 83, 83), fill=(220, 160, 30))
    image.save(path)
    return str(path)


def test_2key_event_mapping_keeps_movement_and_unknown():
    assert classify_2key_event(["mouse_left"]).label == "offense"
    assert classify_2key_event(["space"]).label == "explicit_defense"
    assert classify_2key_event(["w", "a"]).label == "movement_only"
    assert classify_2key_event(["mouse_left", "space"]).label == "unknown_ignore"
    assert classify_2key_event(["mouse_right"], game_id="Core_Keeper").label == "unknown_ignore"
    assert classify_2key_event(["mouse_left"], game_id="Core_Keeper", ui_overlay_open=True).label == "unknown_ignore"


def test_threat_state_features_are_weak_proxy(tmp_path):
    frame = _image(tmp_path / "frame.png", (210, 40, 40))
    state = estimate_threat_state({"frame_path": frame, "player_hp_norm": 0.25})
    assert state["enemy_count_proxy"] >= 1
    assert state["low_hp_bool"] is True
    assert state["state_label_source"] == "vision_proxy_from_frame_salience_and_available_metadata"


def test_core_keeper_event_clips_and_training(tmp_path):
    rows = []
    for episode in ("ep_a", "ep_b"):
        for idx, keys in enumerate((["mouse_left"], ["space"], ["w"], ["mouse_left"], ["space"], ["d"])):
            frame = _image(tmp_path / f"{episode}_{idx}.png", (220, 50 + idx * 5, 40))
            rows.append(
                {
                    "sample_id": f"{episode}_{idx}",
                    "dataset_id": "D2E-480p",
                    "game_id": "Core_Keeper",
                    "episode_id": episode,
                    "frame_idx": idx * 30,
                    "frame_path": frame,
                    "raw_input": keys,
                    "player_hp_norm": 0.8,
                }
            )
    samples = tmp_path / "samples.jsonl"
    clips_path = tmp_path / "clips.jsonl"
    write_jsonl(samples, rows)
    clips = build_core_keeper_event_clips(samples, clips_path, write_outputs=False)
    assert {clip["event_label"] for clip in clips} >= {"offense", "explicit_defense", "movement_only"}
    write_jsonl(clips_path, clips)
    result = train_offense_defense_prior(clips_path, min_confidence=0.5, write_outputs=False)
    assert result["status"] == "trained"
    assert result["trainable_clip_count"] > 0


def test_synthetic_state_validation_reports_progressive_ablation():
    rows = run_synthetic_state_validation(rows_per_env=5, seed=3, write_outputs=False)
    by_policy = {row["policy"]: row for row in rows}
    assert by_policy["full_state"]["accuracy"] >= by_policy["majority"]["accuracy"]

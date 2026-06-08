from __future__ import annotations

import json
from pathlib import Path

from analysis_public.src.common import (
    evaluate_target_rows,
    load_tsi_records,
    normalize_touch_row,
    parse_henze_lines,
    summarize_target_metrics,
    target_rows_from_tsi,
)
from analysis_public.src.paths import REPORTS_DIR
from analysis_public.src.real_public import build_public_to_unity_config, inspect_public_datasets_real


def test_tsi_loader_parses_layout_and_touch_rows(tmp_path):
    touch_csv = tmp_path / "touch_data.csv"
    keyboard_json = tmp_path / "keyboard_data.json"
    touch_csv.write_text(
        "participant_id,task_id,trial_id,timestamp_ms,ref_char,first_frame_touch_x,first_frame_touch_y\n"
        "u1,t1,1,10,a,100,200\n",
        encoding="utf-8",
    )
    keyboard_json.write_text(
        json.dumps(
            {
                "keyboard_info": {"keyboard_height": 500, "top_left_y_position": 0},
                "keys_info": {"a": {"key_center_x": 100, "key_center_y": 200, "key_width": 80, "key_height": 80}},
            }
        ),
        encoding="utf-8",
    )
    touches, keyboard = load_tsi_records(touch_csv, keyboard_json)
    rows = target_rows_from_tsi(touches, keyboard)
    assert len(rows) == 1
    assert rows[0]["target_id"] == "a"
    assert rows[0]["offset_x"] == 0


def test_henze_parser_parses_tiny_sample():
    rows = parse_henze_lines(
        [
            "DEVICE_STATS;device1;en_US;DROID;code;8;UTC;480;854;SHIFT_ON;0;",
            "MICROLEVEL;1;100,100,30;",
            "TAP;110;110;100;HIT;0;110;110;",
        ]
    )
    assert len(rows) == 1
    assert rows[0]["visual_boundary_correct"] is True
    assert rows[0]["target_radius"] == 30


def test_touch_dynamics_common_schema_validation():
    row = normalize_touch_row(
        {"participant_id": "u1", "timestamp_ms": "100", "first_frame_touch_x": "10", "first_frame_touch_y": "20", "pressure": "0.4"},
        dataset_id="touch_dynamics",
    )
    assert row is not None
    assert row["user_id"] == "u1"
    assert row["x"] == 10
    assert row["pressure"] == 0.4


def test_public_target_selection_metrics():
    keyboard = {
        "keyboard_info": {"keyboard_height": 500, "top_left_y_position": 0},
        "keys_info": {
            "a": {"key_center_x": 100, "key_center_y": 100, "key_width": 80, "key_height": 80},
            "b": {"key_center_x": 200, "key_center_y": 100, "key_width": 80, "key_height": 80},
        },
    }
    rows = [
        {
            "participant_id": "u1",
            "target_id": "a",
            "touch_x": 102,
            "touch_y": 101,
            "target_center_x": 100,
            "target_center_y": 100,
            "target_width": 80,
            "target_height": 80,
            "offset_x": 2,
            "offset_y": 1,
            "distance_to_target": 2.2,
        }
    ]
    evaluated = evaluate_target_rows(rows, keyboard)
    metrics = summarize_target_metrics(evaluated, "tsi")
    assert evaluated[0]["visual_boundary_correct"] is True
    assert any(row["baseline"] == "user_gaussian" for row in metrics)


def test_public_availability_report_generated():
    inspect_public_datasets_real()
    assert (REPORTS_DIR / "public_dataset_inventory.csv").exists()
    assert (REPORTS_DIR / "public_dataset_availability.md").exists()


def test_real_required_public_processed_rows_if_available():
    paths = [
        Path("analysis_public/data/touch_dynamics/processed/touch_dynamics_events.parquet"),
        Path("analysis_public/data/mc_snake/processed/mc_snake_events.parquet"),
        Path("analysis_public/data/tsi/processed/tsi_touch_targets.parquet"),
    ]
    if not all(path.exists() for path in paths):
        return
    import pyarrow.parquet as pq

    assert pq.ParquetFile(paths[0]).metadata.num_rows > 0
    assert pq.ParquetFile(paths[1]).metadata.num_rows > 0
    assert pq.ParquetFile(paths[2]).metadata.num_rows > 0


def test_public_touch_prior_config_created_from_real_tsi_if_available():
    tsi_path = Path("analysis_public/data/tsi/processed/tsi_touch_targets.parquet")
    if not tsi_path.exists():
        return
    config = build_public_to_unity_config()
    assert "tsi" in config["source_datasets_used"]
    assert config["default_touch_variance"] > 0

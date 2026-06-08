from __future__ import annotations

from pathlib import Path

from analysis_d2e.src.io_utils import read_jsonl, write_csv, write_jsonl
from analysis_d2e.src.phase_annotation import apply_phase_annotations, export_phase_annotation_template


def test_phase_annotation_template_and_apply(tmp_path):
    samples_path = tmp_path / "samples.jsonl"
    annotation_path = tmp_path / "annotations.csv"
    output_path = tmp_path / "updated.jsonl"
    write_jsonl(
        samples_path,
        [
            {
                "sample_id": "s1",
                "game_id": "Barony",
                "episode_id": "e",
                "frame_idx": 1,
                "frame_path": "frame.png",
                "phase": "unknown",
                "phase_source": "unavailable",
                "has_action_input": True,
                "dominant_atomic_action": "attack",
                "action_label_confidence": 1.0,
            }
        ],
    )

    rows = export_phase_annotation_template(annotation_path, samples_path, max_per_game=5)
    assert len(rows) == 1
    write_csv(annotation_path, [{**rows[0], "manual_phase": "phase_2"}])
    result = apply_phase_annotations(annotation_path, samples_path, output_path)
    updated = read_jsonl(output_path)

    assert result["updated_rows"] == 1
    assert updated[0]["phase"] == "phase_2"
    assert updated[0]["phase_source"] == "manual"


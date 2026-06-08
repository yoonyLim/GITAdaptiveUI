from pathlib import Path

from analysis_vision.src.common import build_vision_grounding_dataset, inspect_vision_datasets


def test_vision_grounding_dataset_builds_from_public_screen_annotation():
    rows = build_vision_grounding_dataset()
    inventory = inspect_vision_datasets()

    assert len(rows) > 0
    screen = next(row for row in inventory if row["dataset_id"] == "screen_annotation")
    assert int(screen["processed_rows"]) > 0
    assert Path("analysis_vision/outputs/reports/vision_dataset_inventory.csv").exists()
    assert Path("analysis_vision/outputs/reports/vision_grounding_label_schema.json").exists()

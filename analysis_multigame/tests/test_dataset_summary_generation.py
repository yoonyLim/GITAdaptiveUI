from analysis_multigame.src.common import build_multigame_dataset, generate_vizdoom_dataset, read_table
from analysis_multigame.src.paths import PROCESSED_DIR


def test_dataset_summary_generation() -> None:
    if not read_table(PROCESSED_DIR / "vizdoom_frames.parquet"):
        generate_vizdoom_dataset(mode="fixture", output_images=False)
    rows = build_multigame_dataset()
    assert len(rows) >= 500
    assert "label_confidence" in rows[0]

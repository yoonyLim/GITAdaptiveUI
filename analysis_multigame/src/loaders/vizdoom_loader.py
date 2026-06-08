from __future__ import annotations

from analysis_multigame.src.common import read_table
from analysis_multigame.src.paths import PROCESSED_DIR


def load_vizdoom_rows() -> list[dict]:
    return read_table(PROCESSED_DIR / "vizdoom_frames.parquet")


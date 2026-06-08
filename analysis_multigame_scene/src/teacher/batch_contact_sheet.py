from pathlib import Path

from analysis_multigame_scene.src.common import placeholder_figure
from analysis_multigame_scene.src.paths import TEACHER_LABELS_DIR


def create_contact_sheet(sample_ids: list[str] | None = None) -> Path:
    path = TEACHER_LABELS_DIR / "contact_sheets" / "contact_sheet_placeholder.png"
    placeholder_figure(path, "Contact sheet placeholder. Use single-frame mode for validated label mapping.")
    return path


__all__ = ["create_contact_sheet"]


from __future__ import annotations

from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ROOT = REPO_ROOT / "analysis_multigame_scene"
DATA_DIR = PACKAGE_ROOT / "data"
RAW_DIR = DATA_DIR / "raw"
FRAMES_DIR = DATA_DIR / "frames"
PROCESSED_DIR = DATA_DIR / "processed"
TEACHER_LABELS_DIR = DATA_DIR / "teacher_labels"
OUTPUTS_DIR = PACKAGE_ROOT / "outputs"
REPORTS_DIR = OUTPUTS_DIR / "reports"
FIGURES_DIR = OUTPUTS_DIR / "figures"
MODELS_DIR = OUTPUTS_DIR / "models"
LATENCY_DIR = OUTPUTS_DIR / "latency"
FEATURES_DIR = OUTPUTS_DIR / "features"
STREAMING_ASSETS_DIR = REPO_ROOT / "Assets" / "StreamingAssets" / "ADUI"


def ensure_scene_dirs() -> None:
    for path in [
        RAW_DIR,
        FRAMES_DIR,
        PROCESSED_DIR,
        TEACHER_LABELS_DIR,
        REPORTS_DIR,
        FIGURES_DIR,
        MODELS_DIR,
        LATENCY_DIR,
        FEATURES_DIR,
        STREAMING_ASSETS_DIR,
    ]:
        path.mkdir(parents=True, exist_ok=True)


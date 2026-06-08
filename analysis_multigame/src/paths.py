from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = ROOT.parent
DATA_DIR = ROOT / "data"
RAW_DIR = DATA_DIR / "raw"
PROCESSED_DIR = DATA_DIR / "processed"
FRAMES_DIR = DATA_DIR / "frames"
LABELS_DIR = DATA_DIR / "labels"
OUTPUTS_DIR = ROOT / "outputs"
REPORTS_DIR = OUTPUTS_DIR / "reports"
FIGURES_DIR = OUTPUTS_DIR / "figures"
MODELS_DIR = OUTPUTS_DIR / "models"
FEATURES_DIR = OUTPUTS_DIR / "features"


def ensure_multigame_dirs() -> None:
    for path in [
        DATA_DIR,
        RAW_DIR,
        PROCESSED_DIR,
        FRAMES_DIR,
        LABELS_DIR,
        REPORTS_DIR,
        FIGURES_DIR,
        MODELS_DIR,
        FEATURES_DIR,
    ]:
        path.mkdir(parents=True, exist_ok=True)


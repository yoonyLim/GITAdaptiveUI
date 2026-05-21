from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = ROOT / "data"
OUTPUTS_DIR = ROOT / "outputs"
PROCESSED_DIR = OUTPUTS_DIR / "processed"
REPORTS_DIR = OUTPUTS_DIR / "reports"
FIGURES_DIR = OUTPUTS_DIR / "figures"
MODELS_DIR = OUTPUTS_DIR / "models"


def ensure_public_dirs() -> None:
    for path in [
        DATA_DIR,
        PROCESSED_DIR,
        REPORTS_DIR,
        FIGURES_DIR,
        MODELS_DIR,
        DATA_DIR / "touch_dynamics" / "raw",
        DATA_DIR / "touch_dynamics" / "manual",
        DATA_DIR / "mc_snake" / "raw",
        DATA_DIR / "mc_snake" / "manual",
        DATA_DIR / "tsi" / "raw",
        DATA_DIR / "henze" / "manual",
        DATA_DIR / "rico" / "manual",
        DATA_DIR / "screen_annotation" / "raw",
    ]:
        path.mkdir(parents=True, exist_ok=True)


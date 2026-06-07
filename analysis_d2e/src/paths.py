from __future__ import annotations

import os
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = ROOT.parent
DATA_DIR = ROOT / "data"
RAW_DIR = DATA_DIR / "raw"
FIXTURE_DIR = DATA_DIR / "fixtures"
PROCESSED_DIR = DATA_DIR / "processed"
OUTPUTS_DIR = ROOT / "outputs"
REPORTS_DIR = OUTPUTS_DIR / "reports"
MODELS_DIR = OUTPUTS_DIR / "models"
FIGURES_DIR = OUTPUTS_DIR / "figures"


def ensure_dirs() -> None:
    for path in [DATA_DIR, RAW_DIR, FIXTURE_DIR, PROCESSED_DIR, OUTPUTS_DIR, REPORTS_DIR, MODELS_DIR, FIGURES_DIR]:
        path.mkdir(parents=True, exist_ok=True)


def d2e_root() -> Path:
    override = os.environ.get("D2E_480P_ROOT")
    if override:
        return Path(override)
    return RAW_DIR / "d2e_480p"


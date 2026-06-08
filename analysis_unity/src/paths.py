from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUTS_DIR = ROOT / "outputs"
PROCESSED_DIR = OUTPUTS_DIR / "processed"
REPORTS_DIR = OUTPUTS_DIR / "reports"
FIGURES_DIR = OUTPUTS_DIR / "figures"
MODELS_DIR = OUTPUTS_DIR / "models"
FIXTURES_DIR = ROOT / "fixtures"
DEFAULT_SESSIONS_DIR = FIXTURES_DIR / "adui_sessions"


def ensure_unity_dirs() -> None:
    for path in [PROCESSED_DIR, REPORTS_DIR, FIGURES_DIR, MODELS_DIR, DEFAULT_SESSIONS_DIR]:
        path.mkdir(parents=True, exist_ok=True)


from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = ROOT.parent
DATA_DIR = ROOT / "data"
OUTPUTS_DIR = ROOT / "outputs"
PROCESSED_DIR = OUTPUTS_DIR / "processed"
REPORTS_DIR = OUTPUTS_DIR / "reports"
FIGURES_DIR = OUTPUTS_DIR / "figures"

PUBLIC_DATA_DIR = REPO_ROOT / "analysis_public" / "data"


def ensure_vision_dirs() -> None:
    for path in [DATA_DIR, PROCESSED_DIR, REPORTS_DIR, FIGURES_DIR]:
        path.mkdir(parents=True, exist_ok=True)

from __future__ import annotations

from analysis_multigame.src.common import write_csv
from analysis_multigame.src.paths import REPORTS_DIR, ensure_multigame_dirs


def main() -> None:
    ensure_multigame_dirs()
    write_csv(
        REPORTS_DIR / "unity_fewshot_domain_adaptation_status.csv",
        [
            {
                "status": "scaffolded",
                "note": "Few-shot Unity adaptation should use real Unity screenshots; fixture-only adaptation is not reported as real generalization.",
            }
        ],
    )


if __name__ == "__main__":
    main()


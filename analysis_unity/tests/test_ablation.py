from analysis_unity.src.common import build_unity_dataset, generate_smoke_session, ingest_unity_logs, run_ablation
from analysis_unity.src.paths import REPORTS_DIR


def test_ablation_report_generation():
    generate_smoke_session()
    ingest_unity_logs()
    build_unity_dataset()
    rows = run_ablation()
    assert rows
    assert (REPORTS_DIR / "ablation_summary.csv").exists()


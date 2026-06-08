from analysis_unity.src.common import compare_public_and_unity, generate_smoke_session, ingest_unity_logs, train_user_touch_model
from analysis_unity.src.paths import REPORTS_DIR


def test_public_transfer_comparison_report():
    generate_smoke_session()
    ingest_unity_logs()
    train_user_touch_model()
    rows = compare_public_and_unity()
    assert rows
    assert (REPORTS_DIR / "public_vs_unity_comparison.csv").exists()


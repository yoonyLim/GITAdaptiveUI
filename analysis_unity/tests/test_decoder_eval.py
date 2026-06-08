from analysis_unity.src.common import build_unity_dataset, evaluate_decoders, generate_smoke_session, ingest_unity_logs
from analysis_unity.src.paths import REPORTS_DIR


def test_decoder_eval_report():
    generate_smoke_session()
    ingest_unity_logs()
    build_unity_dataset()
    metrics = evaluate_decoders()
    assert metrics
    assert (REPORTS_DIR / "decoder_metrics.csv").exists()


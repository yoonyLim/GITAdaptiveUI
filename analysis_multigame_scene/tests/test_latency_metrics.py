from analysis_multigame_scene.src.common import summarize_latency


def test_latency_summary_percentiles():
    row = summarize_latency([1.0, 2.0, 3.0])
    assert row["count"] == 3
    assert row["p95_ms"] >= row["p50_ms"]


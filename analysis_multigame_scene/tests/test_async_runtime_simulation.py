from analysis_multigame_scene.src.common import benchmark_async_runtime


def test_async_runtime_summary_created():
    summary = benchmark_async_runtime(max_events=10)
    assert summary["events"] == 10
    assert 0.0 <= summary["cache_hit_rate"] <= 1.0

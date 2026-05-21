from analysis_multigame.src.common import evaluate_predictions


def test_scene_metric_accuracy() -> None:
    rows = [
        {"threat_level": "none", "pred_threat": "none"},
        {"threat_level": "active", "pred_threat": "warning"},
    ]
    metrics = evaluate_predictions(rows, "threat_level", "pred_threat", ["none", "active", "warning"])
    assert metrics["accuracy"] == 0.5


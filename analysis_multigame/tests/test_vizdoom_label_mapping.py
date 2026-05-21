from analysis_multigame.src.common import scenario_values, weak_labels
import random


def test_vizdoom_mapping_has_labels() -> None:
    values = scenario_values("projectile_hazard", random.Random(1))
    labels = weak_labels(values)
    assert labels["threat_level"] in {"active", "critical"}
    assert labels["action_window"] in {"avoid", "engage"}

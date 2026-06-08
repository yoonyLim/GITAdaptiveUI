from analysis_multigame_scene.src.labels.interaction_demand_schema import INTERACTION_DEMAND_SCHEMA


def test_interaction_demand_schema_has_required_modes():
    assert "action_first" in INTERACTION_DEMAND_SCHEMA["modes"]
    assert "temporal_urgency" in INTERACTION_DEMAND_SCHEMA["variables"]


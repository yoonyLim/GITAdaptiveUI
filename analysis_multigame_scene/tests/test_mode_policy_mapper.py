from analysis_multigame_scene.src.decoder.mode_policy_mapper import mode_scores_from_demands, policy_set_from_mode


def test_action_first_policy_mapping():
    scores = mode_scores_from_demands(0.9, 0.9, 0.2, 0.1, 0.7)
    dominant = max(scores, key=scores.get)
    policies = policy_set_from_mode(dominant, 0.9, 0.9, 0.2, 0.1)
    assert dominant == "action_first"
    assert "interaction_error_tolerance" in policies


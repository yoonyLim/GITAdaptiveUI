from analysis_unity.src.multigame_vision_common import decode_with_prior, strategy_prior


BASE_ROW = {
    "required_action": "Dodge",
    "enemy_state": "Attacking",
    "distance_to_attack": 135.0,
    "distance_to_dodge": 100.0,
    "attack_visual_radius": 110.0,
    "dodge_visual_radius": 110.0,
    "variance_attack": 32400.0,
    "variance_dodge": 32400.0,
    "visual_boundary_prediction": "Dodge",
    "is_near_boundary": False,
    "is_ambiguous": False,
    "tau": 0.55,
    "delta": 0.12,
}


def test_vision_prior_join_internal_strategy() -> None:
    attack, dodge, meta = strategy_prior(BASE_ROW, "multigame_public_vision_prior")
    assert dodge > attack
    assert meta["prior_source"] == "multigame_abstract_proxy_offline_replay"


def test_bayesian_with_multigame_prior_preserves_clear_input() -> None:
    decoded = decode_with_prior(BASE_ROW, "multigame_public_vision_prior", safety_gate=True)
    assert decoded["strategy_prediction"] == "Dodge"
    assert decoded["strategy_safety_reason"] == "preserve_clear_visual_input"


def test_low_confidence_neutral_prior() -> None:
    row = dict(BASE_ROW)
    row["enemy_state"] = "Neutral"
    attack, dodge, _ = strategy_prior(row, "multigame_public_vision_prior")
    assert abs(attack - 0.5) < 1e-6
    assert abs(dodge - 0.5) < 1e-6


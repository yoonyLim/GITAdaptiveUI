from analysis_multigame_scene.src.decoder.adaptation_state import build_adaptation_state


def test_adaptation_state_ttl_and_low_confidence_neutral():
    state = build_adaptation_state({"prior_attack": 0.05, "prior_dodge": 0.95, "confidence": 0.1, "ttl_ms": 123}, now_ms=1000)
    assert state["expires_at_ms"] == 1123
    assert state["prior_attack"] == 0.5
    assert state["prior_dodge"] == 0.5


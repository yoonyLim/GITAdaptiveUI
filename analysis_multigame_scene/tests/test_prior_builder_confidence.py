from analysis_multigame_scene.src.common import prior_from_state


def test_low_confidence_mixes_toward_neutral():
    attack, dodge = prior_from_state("action_first", "critical", "avoid", 0.2)
    assert 0.35 < attack < 0.5
    assert 0.5 < dodge < 0.65


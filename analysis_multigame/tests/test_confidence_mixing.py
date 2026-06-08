from analysis_multigame.src.models.confidence_calibration import mix_with_neutral


def test_confidence_mixing_moves_toward_neutral() -> None:
    attack, dodge = mix_with_neutral(0.05, 0.95, 0.5)
    assert 0.05 < attack < 0.5
    assert 0.5 < dodge < 0.95
    assert abs((attack + dodge) - 1.0) < 1e-6


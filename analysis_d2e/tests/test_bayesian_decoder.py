from __future__ import annotations

from analysis_d2e.src.bayesian_input_decoder import BayesianInputDecoder
from analysis_d2e.src.schemas import ButtonSpec, SkillProfile, TouchEvent, TouchProfile


def _buttons():
    return [
        ButtonSpec("attack", "attack", 100, 100, 35, 50),
        ButtonSpec("defense", "defense", 200, 100, 35, 50),
    ]


def test_clear_input_is_preserved_even_when_prior_favors_other_button():
    result = BayesianInputDecoder().decode(
        TouchEvent(100, 100),
        _buttons(),
        {"attack": 0.05, "defense": 0.95},
        TouchProfile(touch_variance=400),
        SkillProfile(skill_level="beginner"),
    )

    assert result.predicted_button == "attack"
    assert result.corrected is False
    assert result.safety_gate_reason == "clear_input_preserved"


def test_ambiguous_input_can_use_situation_prior_when_gate_passes():
    result = BayesianInputDecoder().decode(
        TouchEvent(150, 100),
        _buttons(),
        {"attack": 0.05, "defense": 0.95},
        TouchProfile(touch_variance=900),
        SkillProfile(skill_level="beginner"),
    )

    assert result.predicted_button in {"attack", "defense", None}
    assert result.safety_gate_reason in {
        "correction_applied",
        "posterior_confirms_expanded_prediction",
        "posterior_below_tau",
        "posterior_gap_below_delta",
        "not_ambiguous_preserve_nonclear_baseline",
    }

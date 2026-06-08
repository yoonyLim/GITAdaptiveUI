from __future__ import annotations

from analysis_d2e.src.action_to_n_button_projector import ActionToNButtonProjector


def test_projector_creates_context_button_with_visible_label():
    atomic = {"attack": 0.1, "defense": 0.1, "dodge": 0.45, "skill": 0.1, "heal": 0.15, "escape": 0.1}
    projected = ActionToNButtonProjector().project(
        atomic,
        4,
        {"phase": "phase_3", "boss_visible": True, "boss_telegraph": True, "player_hp_norm": 0.8},
    )

    assert set(projected["button_prior"]) == {"attack", "defense", "skill", "context"}
    assert projected["context"]["context_action"] in {"dodge", "defense"}
    assert projected["context"]["visible_label"]
    assert abs(sum(projected["button_prior"].values()) - 1.0) < 1e-6


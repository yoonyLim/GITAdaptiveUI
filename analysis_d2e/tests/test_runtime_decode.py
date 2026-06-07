from __future__ import annotations

from analysis_d2e.src.runtime_decode import decode_request


def _base_request():
    return {
        "n_buttons": 2,
        "atomic_distribution": {"attack": 0.05, "defense": 0.55, "dodge": 0.35, "skill": 0.02, "heal": 0.02, "escape": 0.01},
        "button_layout": [
            {"button_id": "attack", "action": "attack", "center_x": 100, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "defense", "action": "defense", "center_x": 180, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
        ],
        "touch_profile": {"touch_variance": 700, "near_miss_rate": 0.14},
        "skill_profile": {"skill_level": "beginner", "context_button_understanding": 0.4},
        "use_skill": True,
    }


def test_runtime_decode_preserves_clear_visual_input():
    request = {**_base_request(), "touch": {"x": 100, "y": 200}}

    response = decode_request(request, model=None)

    assert response["predicted_button"] == "attack"
    assert response["safety_gate_reason"] == "clear_input_preserved"
    assert response["prior_source"] == "client_atomic_distribution"


def test_runtime_decode_returns_context_visible_label_for_four_buttons():
    request = {
        **_base_request(),
        "n_buttons": 4,
        "touch": {"x": 220, "y": 200},
        "atomic_distribution": {"attack": 0.12, "defense": 0.22, "dodge": 0.42, "skill": 0.08, "heal": 0.08, "escape": 0.08},
        "situation": {"phase": "phase_3", "boss_visible": True, "boss_telegraph": True},
        "button_layout": [
            {"button_id": "attack", "action": "attack", "center_x": 100, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "defense", "action": "defense", "center_x": 180, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "skill", "action": "skill", "center_x": 260, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "context", "action": "context", "center_x": 340, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
        ],
    }

    response = decode_request(request, model=None)

    assert response["context"]["context_action"] in {"dodge", "defense"}
    assert response["context"]["visible_label"] in {"Dodge", "Guard"}
    assert "context" in response["button_prior"]

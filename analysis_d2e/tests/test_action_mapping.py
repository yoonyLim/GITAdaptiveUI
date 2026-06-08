from __future__ import annotations

from analysis_d2e.src.action_mapping import action_distribution_from_raw_input, action_label_confidence


def test_raw_input_maps_keyboard_mouse_to_atomic_distribution():
    dist = action_distribution_from_raw_input({"keys": ["mouse_left", "space", "h"]})

    assert abs(sum(dist.values()) - 1.0) < 1e-6
    assert dist["attack"] > dist["defense"]
    assert dist["dodge"] > dist["defense"]
    assert dist["heal"] > dist["defense"]


def test_movement_only_input_has_no_action_label_confidence():
    assert action_label_confidence(["w", "a"]) == 0.0


def test_escape_virtual_key_maps_to_escape_action():
    dist = action_distribution_from_raw_input(["vk_27"])

    assert dist["escape"] > dist["attack"]


def test_backpedal_shift_counts_as_escape_proxy():
    dist = action_distribution_from_raw_input(["s", "vk_160"])

    assert dist["escape"] > dist["attack"]

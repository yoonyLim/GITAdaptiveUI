from __future__ import annotations

from analysis_d2e.src.action_mapping import action_distribution_from_raw_input
from analysis_d2e.src.d2e_preprocess import infer_phase, preprocess_d2e
from analysis_d2e.src.generate_smoke_fixture import generate_fixture


def test_smoke_fixture_preprocesses_to_d2e_samples(tmp_path):
    root = generate_fixture(tmp_path / "fixture", rows_per_game_phase=1)
    rows = preprocess_d2e(root, history_len=2, write_outputs=False)

    assert len(rows) == 15
    assert any(row["game_id"] == "Barony" for row in rows)
    assert all(row["source_type"] == "synthetic_fixture" for row in rows)
    assert all(abs(sum(row["atomic_distribution"].values()) - 1.0) < 1e-6 for row in rows)


def test_action_distribution_has_all_atomic_actions():
    dist = action_distribution_from_raw_input(["mouse_left"])
    assert set(dist) == {"attack", "defense", "dodge", "skill", "heal", "escape"}


def test_phase_inference_marks_unavailable_without_action_signal():
    phase, source = infer_phase({}, {"attack": 1 / 6, "defense": 1 / 6, "dodge": 1 / 6, "skill": 1 / 6, "heal": 1 / 6, "escape": 1 / 6}, 0.0)

    assert phase == "unknown"
    assert source == "unavailable"


def test_phase_inference_uses_weak_action_proxy_for_heal():
    phase, source = infer_phase({}, {"attack": 0.1, "defense": 0.1, "dodge": 0.1, "skill": 0.1, "heal": 0.5, "escape": 0.1}, 0.8)

    assert phase == "phase_2"
    assert source == "weak_action_proxy"

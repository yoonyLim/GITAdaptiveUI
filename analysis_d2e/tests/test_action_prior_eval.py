from __future__ import annotations

from analysis_d2e.src.d2e_action_prior_model import heldout_episode_metrics


def test_heldout_episode_metrics_group_by_game_and_episode():
    samples = [
        {
            "game_id": "Barony",
            "episode_id": "ep_a",
            "frame_path": "missing_a.png",
            "atomic_distribution": {"attack": 0.9, "defense": 0.02, "dodge": 0.02, "skill": 0.02, "heal": 0.02, "escape": 0.02},
        },
        {
            "game_id": "Barony",
            "episode_id": "ep_b",
            "frame_path": "missing_b.png",
            "atomic_distribution": {"attack": 0.02, "defense": 0.9, "dodge": 0.02, "skill": 0.02, "heal": 0.02, "escape": 0.02},
        },
        {
            "game_id": "Brotato",
            "episode_id": "ep_c",
            "frame_path": "missing_c.png",
            "atomic_distribution": {"attack": 0.02, "defense": 0.02, "dodge": 0.9, "skill": 0.02, "heal": 0.02, "escape": 0.02},
        },
    ]

    rows = heldout_episode_metrics(samples)

    assert len(rows) == 3
    assert {row["heldout_episode_id"] for row in rows} == {"ep_a", "ep_b", "ep_c"}
    assert all(row["test_count"] == 1 for row in rows)

from __future__ import annotations

from analysis_multigame_scene.src.teacher.build_focused_labels import build_focused_label


def test_focused_label_mapping_keeps_training_ready_gameplay_label():
    row = build_focused_label(
        {
            "sample_id": "s1",
            "source_dataset": "fixture_game",
            "ui_phase": "gameplay",
            "threat_level": "active",
            "action_window": "avoid",
            "interaction_demand": {"temporal_urgency": 0.8},
            "confidence": 0.9,
            "should_use_for_training": True,
            "quality_flags": [],
            "schema_errors": [],
        },
        confidence_threshold=0.45,
    )

    assert row["risk_state"] == "active"
    assert row["action_window_prior"] == "avoid"
    assert row["temporal_urgency"] == 0.8
    assert row["should_use_for_training"] is True


def test_focused_label_mapping_excludes_low_confidence_label():
    row = build_focused_label(
        {
            "sample_id": "s2",
            "source_dataset": "fixture_game",
            "ui_phase": "gameplay",
            "threat_level": "warning",
            "action_window": "explore",
            "interaction_demand": {"temporal_urgency": 0.2},
            "confidence": 0.2,
            "should_use_for_training": True,
            "quality_flags": ["low_confidence"],
            "schema_errors": [],
        },
        confidence_threshold=0.45,
    )

    assert row["risk_state"] == "safe_or_warning"
    assert row["action_window_prior"] == "neutral"
    assert row["should_use_for_training"] is False
    assert "confidence_below_threshold" in row["exclude_reasons"]

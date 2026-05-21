from analysis_multigame_scene.src.common import interaction_label_from_weak, validate_teacher_label


def test_teacher_output_validation_accepts_heuristic_label():
    label = interaction_label_from_weak(
        {
            "sample_id": "s1",
            "source_dataset": "fixture",
            "ui_phase": "gameplay",
            "weak_threat_level": "active",
            "weak_action_window": "avoid",
            "weak_urgency_level": "high",
        }
    )
    assert validate_teacher_label(label) == []


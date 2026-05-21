from analysis_multigame.src.labels.abstract_label_schema import validate_abstract_row


def test_valid_abstract_row() -> None:
    row = {
        "ui_phase": "gameplay",
        "threat_level": "active",
        "action_window": "avoid",
        "urgency_level": "high",
        "label_confidence": 0.8,
        "label_source": "heuristic",
        "dataset_name": "fixture",
        "game_name": "vizdoom",
    }
    assert validate_abstract_row(row) == []


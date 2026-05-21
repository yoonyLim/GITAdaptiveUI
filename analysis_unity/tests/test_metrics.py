from analysis_unity.src.metrics import correction_success_rate, overcorrection_rate


def test_correction_success_rate():
    rows = [
        {
            "required_action": "Dodge",
            "visual_boundary_prediction": "Attack",
            "final_executed_action": "Dodge",
            "is_ambiguous": True,
            "invalid_touch": False,
        }
    ]
    assert correction_success_rate(rows) == 1.0


def test_overcorrection_rate():
    rows = [
        {
            "required_action": "Attack",
            "visual_boundary_prediction": "Attack",
            "final_executed_action": "Dodge",
            "is_ambiguous": True,
            "invalid_touch": False,
        }
    ]
    assert overcorrection_rate(rows) == 1.0


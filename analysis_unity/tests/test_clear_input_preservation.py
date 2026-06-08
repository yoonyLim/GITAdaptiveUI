from analysis_unity.src.teacher_student_common import evaluate_teacher_student_prior


def test_clear_input_preservation_metric_is_probability():
    rows = evaluate_teacher_student_prior()
    for row in rows:
        assert 0.0 <= float(row["clear_subset_accuracy"]) <= 1.0


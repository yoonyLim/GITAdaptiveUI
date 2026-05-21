from analysis_unity.src.teacher_student_common import evaluate_teacher_student_prior


def test_overcorrection_metric_is_probability():
    rows = evaluate_teacher_student_prior()
    for row in rows:
        assert 0.0 <= float(row["overcorrection_rate"]) <= 1.0


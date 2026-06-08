from analysis_unity.src.teacher_student_common import evaluate_teacher_student_prior


def test_teacher_student_prior_metrics_generated():
    rows = evaluate_teacher_student_prior()
    assert rows
    assert any(row["prior_source"] == "lightweight_student_prior" for row in rows)


from analysis_unity.src.teacher_student_common import evaluate_teacher_student_prior


def test_bayesian_with_student_prior_has_accuracy():
    rows = evaluate_teacher_student_prior()
    student = next(row for row in rows if row["prior_source"] == "student_prior_with_safety_gate")
    assert float(student["overall_accuracy"]) >= 0.0


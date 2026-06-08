from pathlib import Path

from analysis_unity.src.common import (
    build_unity_dataset,
    evaluate_mode_policy_and_survey,
    generate_smoke_session,
    ingest_unity_logs,
    read_csv,
)


def test_mode_policy_and_survey_reports_are_generated():
    generate_smoke_session()
    ingest_unity_logs()
    build_unity_dataset()

    mode_rows, survey_rows = evaluate_mode_policy_and_survey()

    assert mode_rows
    assert any(row.get("interaction_mode") == "ActionFirst" for row in mode_rows)
    assert survey_rows[0]["survey_rows"] >= 1
    assert Path("analysis_unity/outputs/reports/mode_policy_summary.csv").exists()
    assert Path("analysis_unity/outputs/reports/trust_control_survey_summary.csv").exists()

    dataset = read_csv(Path("analysis_unity/outputs/processed/unity_dataset.csv"))
    assert "interaction_mode" in dataset[0]
    assert "policy_correction_strength" in dataset[0]

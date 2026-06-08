from analysis_unity.src.common import build_unity_dataset, generate_smoke_session, ingest_unity_logs
from analysis_unity.src.schemas import TRIAL_FIELDS


def test_schema_fields_present():
    generate_smoke_session()
    ingest_unity_logs()
    rows = build_unity_dataset()
    assert rows
    for field in TRIAL_FIELDS:
        assert field in rows[0]


def test_posterior_normalization():
    generate_smoke_session()
    ingest_unity_logs()
    rows = build_unity_dataset()
    row = next(row for row in rows if row["phase"] != "calibration")
    total = float(row["posterior_attack"]) + float(row["posterior_dodge"])
    assert abs(total - 1.0) < 0.02


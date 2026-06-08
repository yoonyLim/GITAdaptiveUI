from analysis_unity.src.common import generate_smoke_session, ingest_unity_logs


def test_ingest_smoke_session():
    generate_smoke_session()
    rows = ingest_unity_logs()
    assert len(rows) >= 60
    assert any(row["phase"] == "calibration" for row in rows)
    assert any(row["phase"] == "test" for row in rows)


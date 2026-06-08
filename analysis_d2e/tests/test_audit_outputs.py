from __future__ import annotations

from analysis_d2e.src.audit_outputs import build_phase_coverage_audit, build_primary_subset_audit
from analysis_d2e.src.io_utils import write_jsonl


def test_primary_subset_audit_marks_barony_primary(tmp_path):
    samples = tmp_path / "samples.jsonl"
    write_jsonl(
        samples,
        [
            {"game_id": "Barony", "phase": "phase_1", "action_label_confidence": 0.4},
            {"game_id": "Brotato", "phase": "unknown", "action_label_confidence": 0.0},
        ],
    )

    rows = build_primary_subset_audit(samples_path=samples, root=tmp_path / "raw", write_outputs=False)
    by_game = {row["game_id"]: row for row in rows}

    assert by_game["Barony"]["role"] == "primary"
    assert by_game["Barony"]["usable_action_rows"] == 1
    assert by_game["Brotato"]["role"] == "auxiliary"


def test_phase_coverage_audit_flags_missing_manual_phase_labels(tmp_path):
    samples = tmp_path / "samples.jsonl"
    write_jsonl(samples, [{"game_id": "Barony", "phase": "phase_1", "phase_source": "weak_action_proxy", "action_label_confidence": 0.4}])

    rows = build_phase_coverage_audit(samples_path=samples, write_outputs=False)
    by_phase = {row["phase"]: row for row in rows}

    assert by_phase["phase_1"]["requires_manual_annotation"] is True
    assert by_phase["phase_2"]["rows"] == 0

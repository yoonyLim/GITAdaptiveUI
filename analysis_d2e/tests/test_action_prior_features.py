from __future__ import annotations

from analysis_d2e.src.d2e_action_prior_model import featurize_sample


def test_action_prior_features_do_not_use_phase_or_raw_input():
    base = {
        "frame_path": "same_missing_frame.png",
        "history_frame_paths": ["same_missing_frame.png"],
        "phase": "phase_1",
        "raw_input": {"keys": ["mouse_left"]},
    }
    changed_label_fields = {
        **base,
        "phase": "phase_3",
        "raw_input": {"keys": ["space", "h"]},
    }

    assert featurize_sample(base) == featurize_sample(changed_label_fields)

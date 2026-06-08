from __future__ import annotations

from analysis_multigame.src.common import ACTION_WINDOWS, THREAT_LEVELS, UI_PHASES, URGENCY_LEVELS


ABSTRACT_LABEL_SCHEMA = {
    "ui_phase": UI_PHASES,
    "threat_level": THREAT_LEVELS,
    "action_window": ACTION_WINDOWS,
    "urgency_level": URGENCY_LEVELS,
    "interaction_demand": [
        "action_intensity_score",
        "temporal_urgency_score",
        "information_priority_score",
        "occlusion_risk_score",
        "control_continuity_score",
        "ui_skill_proxy",
    ],
    "required_metadata": ["label_confidence", "label_source", "dataset_name", "game_name"],
}


def validate_abstract_row(row: dict) -> list[str]:
    issues = []
    for key, allowed in [
        ("ui_phase", UI_PHASES),
        ("threat_level", THREAT_LEVELS),
        ("action_window", ACTION_WINDOWS),
        ("urgency_level", URGENCY_LEVELS),
    ]:
        if row.get(key) not in allowed:
            issues.append(f"invalid {key}: {row.get(key)}")
    for key in ABSTRACT_LABEL_SCHEMA["required_metadata"]:
        if key not in row:
            issues.append(f"missing {key}")
    return issues


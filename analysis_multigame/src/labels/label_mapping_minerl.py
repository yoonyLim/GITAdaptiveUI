from __future__ import annotations


def map_minerl_state(row: dict) -> dict:
    reward = float(row.get("reward") or 0.0)
    action = str(row.get("action") or "").lower()
    if "attack" in action or reward > 0:
        return {"threat_level": "warning", "action_window": "engage", "urgency_level": "medium", "label_source": "action_proxy"}
    return {"threat_level": "unknown", "action_window": "explore", "urgency_level": "low", "label_source": "action_proxy"}


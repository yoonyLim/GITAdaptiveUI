from __future__ import annotations


def map_atari_reward_action(row: dict) -> dict:
    reward = float(row.get("reward") or 0.0)
    if reward < 0:
        return {"threat_level": "critical", "action_window": "avoid", "urgency_level": "high", "label_source": "reward_proxy"}
    if reward > 0:
        return {"threat_level": "warning", "action_window": "engage", "urgency_level": "medium", "label_source": "reward_proxy"}
    return {"threat_level": "unknown", "action_window": "unknown", "urgency_level": "unknown", "label_source": "unavailable"}


from __future__ import annotations


def map_dqn_transition(row: dict) -> dict:
    reward = float(row.get("reward") or 0.0)
    done = str(row.get("done") or "False").lower() in {"true", "1"}
    if done or reward < 0:
        return {"threat_level": "critical", "action_window": "avoid", "urgency_level": "high", "label_source": "terminal_reward_proxy"}
    if reward > 0:
        return {"threat_level": "warning", "action_window": "engage", "urgency_level": "medium", "label_source": "reward_proxy"}
    return {"threat_level": "none", "action_window": "wait", "urgency_level": "low", "label_source": "reward_proxy"}


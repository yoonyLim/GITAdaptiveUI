from __future__ import annotations

from typing import Any


LABELS = {
    "aoe_attack": "AOE",
    "escape": "Escape",
    "heal": "Heal",
    "dodge": "Dodge",
    "defense": "Guard",
    "skill": "Skill",
}


def context_action_probability(action: str, atomic: dict[str, float]) -> float:
    if action == "aoe_attack":
        return 0.65 * float(atomic.get("skill", 0.0)) + 0.35 * float(atomic.get("attack", 0.0))
    if action in atomic:
        return float(atomic[action])
    return 0.0


class ContextButtonPolicy:
    def choose(self, atomic: dict[str, float], situation: dict[str, Any]) -> dict[str, Any]:
        phase = str(situation.get("phase", "")).lower()
        hp = float(situation.get("player_hp_norm", 1.0))
        ranged = int(float(situation.get("ranged_enemy_count", 0)))
        melee = int(float(situation.get("melee_enemy_count", 0)))
        boss = bool(situation.get("boss_visible") or situation.get("boss_telegraph"))

        if phase == "phase_1" or melee >= 10:
            action = "aoe_attack" if atomic.get("skill", 0.0) >= atomic.get("escape", 0.0) else "escape"
        elif phase == "phase_2" or (ranged >= 3 and hp <= 0.4):
            action = "heal" if hp <= 0.35 and atomic.get("heal", 0.0) >= 0.05 else "dodge"
        elif phase == "phase_3" or boss:
            action = "dodge" if atomic.get("dodge", 0.0) >= atomic.get("defense", 0.0) else "defense"
        else:
            candidates = ["heal", "dodge", "escape", "skill"]
            action = max(candidates, key=lambda item: context_action_probability(item, atomic))

        return {
            "context_action": action,
            "visible_label": LABELS.get(action, action.replace("_", " ").title()),
            "confidence": context_action_probability(action, atomic),
        }


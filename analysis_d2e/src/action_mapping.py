from __future__ import annotations

import json
import re
from collections import Counter
from typing import Any

from analysis_d2e.src.io_utils import clamp
from analysis_d2e.src.schemas import ATOMIC_ACTIONS


ACTION_ALIASES: dict[str, tuple[str, ...]] = {
    "attack": ("mouse_left", "left_mouse", "lmb", "button0", "attack", "fire", "shoot", "ctrl"),
    "defense": ("mouse_right", "right_mouse", "rmb", "block", "shield", "parry", "defense"),
    "dodge": ("space", "roll", "dodge", "dash", "evade"),
    "skill": ("q", "e", "r", "f", "skill", "ability", "spell", "hotbar_1", "hotbar_2", "key_1", "key_2", "key_3"),
    "heal": ("heal", "potion", "h", "medkit", "consume"),
    "escape": ("escape", "esc", "vk_27", "retreat", "backstep", "sprint_away", "run_away"),
}


def parse_input_blob(raw_input: Any) -> Counter[str]:
    if isinstance(raw_input, dict):
        items: list[str] = []
        for key, value in raw_input.items():
            if isinstance(value, bool) and value:
                items.append(str(key))
            elif isinstance(value, (int, float)) and value > 0:
                items.append(str(key))
            elif isinstance(value, str) and value.strip():
                items.extend(re.split(r"[\s,;|]+", value))
            elif isinstance(value, list):
                items.extend(str(item) for item in value)
        return Counter(item.strip().lower() for item in items if str(item).strip())
    if isinstance(raw_input, list):
        return Counter(str(item).strip().lower() for item in raw_input if str(item).strip())
    text = str(raw_input or "").strip()
    if not text:
        return Counter()
    try:
        return parse_input_blob(json.loads(text))
    except Exception:
        return Counter(item.strip().lower() for item in re.split(r"[\s,;|]+", text) if item.strip())


def action_signal_counts(raw_input: Any) -> Counter[str]:
    tokens = parse_input_blob(raw_input)
    counts: Counter[str] = Counter()
    for action, aliases in ACTION_ALIASES.items():
        for alias in aliases:
            if alias in tokens:
                counts[action] += max(1.0, float(tokens[alias]))
    if tokens.get("s") and (tokens.get("shift") or tokens.get("vk_160") or tokens.get("vk_161") or tokens.get("sprint")):
        counts["escape"] += 0.8
    return counts


def action_distribution_from_raw_input(raw_input: Any, smoothing: float = 0.02) -> dict[str, float]:
    counts = action_signal_counts(raw_input)
    weights = {action: smoothing for action in ATOMIC_ACTIONS}
    for action, value in counts.items():
        weights[action] += value
    total = sum(weights.values())
    return {action: clamp(value / total, 0.0, 1.0) for action, value in weights.items()}


def action_label_confidence(raw_input: Any) -> float:
    counts = action_signal_counts(raw_input)
    total = sum(float(value) for value in counts.values())
    if total <= 0:
        return 0.0
    return max(float(value) for value in counts.values()) / total


def dominant_atomic_action(distribution: dict[str, float]) -> str:
    return max(ATOMIC_ACTIONS, key=lambda action: float(distribution.get(action, 0.0)))

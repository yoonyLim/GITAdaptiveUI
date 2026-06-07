from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import Any

from analysis_d2e.src.action_mapping import parse_input_blob


OFFENSE_TOKENS = {
    "mouse_left",
    "left_mouse",
    "lmb",
    "button0",
    "attack",
    "fire",
    "shoot",
    "ctrl",
    "q",
    "e",
    "r",
    "f",
    "skill",
    "ability",
    "spell",
    "hotbar_1",
    "hotbar_2",
    "key_1",
    "key_2",
    "key_3",
}

EXPLICIT_DEFENSE_TOKENS = {
    "mouse_right",
    "right_mouse",
    "rmb",
    "block",
    "shield",
    "parry",
    "defense",
    "space",
    "roll",
    "dodge",
    "dash",
    "evade",
    "escape",
    "esc",
    "vk_27",
}

MOVEMENT_TOKENS = {
    "w",
    "a",
    "s",
    "d",
    "up",
    "down",
    "left",
    "right",
    "arrow_up",
    "arrow_down",
    "arrow_left",
    "arrow_right",
    "vk_87",
    "vk_65",
    "vk_83",
    "vk_68",
}

SPRINT_TOKENS = {"shift", "vk_160", "vk_161", "sprint"}
EVENT_LABELS = ("offense", "explicit_defense", "movement_only", "unknown_ignore")


@dataclass(frozen=True)
class TwoKeyEventLabel:
    label: str
    confidence: float
    offense_score: float
    defense_score: float
    movement_score: float
    movement_dx: float
    movement_dy: float
    movement_direction: str
    label_reason: str


def _game_specific_tokens(tokens: Counter[str], game_id: str | None) -> Counter[str]:
    game = (game_id or "").lower().replace(" ", "_")
    if game == "core_keeper":
        # In Core Keeper, right click is often UI/item/use interaction. Treat it
        # as ambiguous unless later manual metadata proves a combat block/guard.
        tokens = Counter(tokens)
        for token in ("mouse_right", "right_mouse", "rmb"):
            tokens.pop(token, None)
        return tokens
    return tokens


def _score(tokens: Counter[str], vocabulary: set[str]) -> float:
    return float(sum(tokens.get(token, 0.0) for token in vocabulary))


def movement_vector(raw_input: Any) -> tuple[float, float, str]:
    tokens = parse_input_blob(raw_input)
    dx = 0.0
    dy = 0.0
    if tokens.get("a") or tokens.get("left") or tokens.get("arrow_left") or tokens.get("vk_65"):
        dx -= 1.0
    if tokens.get("d") or tokens.get("right") or tokens.get("arrow_right") or tokens.get("vk_68"):
        dx += 1.0
    if tokens.get("w") or tokens.get("up") or tokens.get("arrow_up") or tokens.get("vk_87"):
        dy -= 1.0
    if tokens.get("s") or tokens.get("down") or tokens.get("arrow_down") or tokens.get("vk_83"):
        dy += 1.0
    if dx == 0.0 and dy == 0.0:
        return 0.0, 0.0, "none"
    if dx != 0.0 and dy != 0.0:
        dx *= 0.70710678118
        dy *= 0.70710678118
    horiz = "left" if dx < 0 else "right" if dx > 0 else ""
    vert = "up" if dy < 0 else "down" if dy > 0 else ""
    direction = "_".join(part for part in (vert, horiz) if part) or "none"
    return dx, dy, direction


def classify_2key_event(raw_input: Any, game_id: str | None = None, ui_overlay_open: bool = False) -> TwoKeyEventLabel:
    if ui_overlay_open:
        dx, dy, direction = movement_vector(raw_input)
        tokens = parse_input_blob(raw_input)
        return TwoKeyEventLabel(
            label="unknown_ignore",
            confidence=0.0,
            offense_score=_score(tokens, OFFENSE_TOKENS),
            defense_score=_score(tokens, EXPLICIT_DEFENSE_TOKENS),
            movement_score=_score(tokens, MOVEMENT_TOKENS),
            movement_dx=dx,
            movement_dy=dy,
            movement_direction=direction,
            label_reason="ui_overlay_open_not_combat_event",
        )
    tokens = _game_specific_tokens(parse_input_blob(raw_input), game_id)
    offense = _score(tokens, OFFENSE_TOKENS)
    defense = _score(tokens, EXPLICIT_DEFENSE_TOKENS)
    movement = _score(tokens, MOVEMENT_TOKENS)
    if tokens.get("s") and any(tokens.get(token) for token in SPRINT_TOKENS):
        defense += 0.8
    dx, dy, direction = movement_vector(raw_input)

    if offense > 0.0 and defense == 0.0:
        label = "offense"
        confidence = min(1.0, 0.70 + offense * 0.15)
        reason = "offense_token_without_explicit_defense"
    elif defense > 0.0 and offense == 0.0:
        label = "explicit_defense"
        confidence = min(1.0, 0.70 + defense * 0.15)
        reason = "explicit_defense_token_without_offense"
    elif movement > 0.0 and offense == 0.0 and defense == 0.0:
        label = "movement_only"
        confidence = 0.60
        reason = "movement_tokens_without_action"
    elif offense > 0.0 and defense > 0.0:
        label = "unknown_ignore"
        confidence = 0.25
        reason = "mixed_offense_and_defense_tokens"
    else:
        label = "unknown_ignore"
        confidence = 0.0
        reason = "no_supported_event_token"

    return TwoKeyEventLabel(
        label=label,
        confidence=confidence,
        offense_score=offense,
        defense_score=defense,
        movement_score=movement,
        movement_dx=dx,
        movement_dy=dy,
        movement_direction=direction,
        label_reason=reason,
    )

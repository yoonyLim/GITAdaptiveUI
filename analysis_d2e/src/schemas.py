from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


ATOMIC_ACTIONS = ("attack", "defense", "dodge", "skill", "heal", "escape")
SUPPORTED_GAMES = ("Barony", "Brotato", "Skul", "Core Keeper", "Vampire Survivors")
PHASES = ("phase_1", "phase_2", "phase_3")
SKILL_LEVELS = ("beginner", "intermediate", "expert")


@dataclass(frozen=True)
class ButtonSpec:
    button_id: str
    action: str
    center_x: float
    center_y: float
    visual_radius: float
    hitbox_radius: float
    cooldown_ready: bool = True
    executable: bool = True
    visible_label: str = ""


@dataclass(frozen=True)
class TouchEvent:
    x: float
    y: float


@dataclass
class TouchProfile:
    button_mean_offset: dict[str, tuple[float, float]] = field(default_factory=dict)
    button_covariance: dict[str, tuple[float, float, float]] = field(default_factory=dict)
    near_miss_rate: float = 0.0
    touch_variance: float = 900.0


@dataclass
class SkillProfile:
    skill_level: str = "intermediate"
    reaction_time_mean: float = 300.0
    reinput_rate: float = 0.0
    joystick_drift: float = 0.0
    cooldown_misuse_rate: float = 0.0
    context_button_understanding: float = 0.5


@dataclass
class DecodeResult:
    predicted_button: str | None
    posterior: dict[str, float]
    corrected: bool
    invalid_touch: bool
    safety_gate_passed: bool
    safety_gate_reason: str
    visual_prediction: str | None
    expanded_prediction: str | None


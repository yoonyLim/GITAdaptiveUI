from __future__ import annotations

from analysis_d2e.src.context_button_policy import ContextButtonPolicy, context_action_probability
from analysis_d2e.src.io_utils import clamp


class ActionToNButtonProjector:
    def __init__(self, context_policy: ContextButtonPolicy | None = None, floor: float = 1e-4) -> None:
        self.context_policy = context_policy or ContextButtonPolicy()
        self.floor = floor

    def project(self, atomic: dict[str, float], n_buttons: int, situation: dict[str, object] | None = None) -> dict[str, object]:
        situation = situation or {}
        if n_buttons not in {2, 3, 4}:
            raise ValueError("n_buttons must be 2, 3, or 4")
        context = self.context_policy.choose(atomic, situation) if n_buttons == 4 else None
        if n_buttons == 2:
            raw = {
                "attack": float(atomic.get("attack", 0.0)) + 0.45 * float(atomic.get("skill", 0.0)),
                "defense": float(atomic.get("defense", 0.0))
                + float(atomic.get("dodge", 0.0))
                + float(atomic.get("heal", 0.0))
                + float(atomic.get("escape", 0.0))
                + 0.55 * float(atomic.get("skill", 0.0)),
            }
        elif n_buttons == 3:
            raw = {
                "attack": float(atomic.get("attack", 0.0)),
                "defense": float(atomic.get("defense", 0.0)) + float(atomic.get("dodge", 0.0)) + float(atomic.get("heal", 0.0)) + float(atomic.get("escape", 0.0)),
                "skill": float(atomic.get("skill", 0.0)),
            }
        else:
            context_action = str(context["context_action"])
            raw = {
                "attack": float(atomic.get("attack", 0.0)),
                "defense": float(atomic.get("defense", 0.0)) + 0.65 * float(atomic.get("dodge", 0.0)),
                "skill": float(atomic.get("skill", 0.0)),
                "context": context_action_probability(context_action, atomic),
            }
        total = sum(max(self.floor, value) for value in raw.values())
        prior = {button: clamp(max(self.floor, value) / total, 0.0, 1.0) for button, value in raw.items()}
        return {
            "n_buttons": n_buttons,
            "button_prior": prior,
            "context": context,
            "source_atomic_distribution": atomic,
        }


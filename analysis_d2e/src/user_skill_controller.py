from __future__ import annotations

from dataclasses import dataclass

from analysis_d2e.src.schemas import SkillProfile


@dataclass(frozen=True)
class SkillControlParams:
    prior_weight_lambda: float
    tau: float
    delta: float
    correction_radius: float
    feedback_intensity: float
    context_button_emphasis: float


class UserSkillController:
    DEFAULTS = {
        "beginner": SkillControlParams(0.8, 0.65, 0.15, 1.45, 0.85, 0.85),
        "intermediate": SkillControlParams(0.6, 0.72, 0.20, 1.25, 0.60, 0.55),
        "expert": SkillControlParams(0.3, 0.82, 0.28, 1.08, 0.30, 0.25),
    }

    def params_for(self, profile: SkillProfile) -> SkillControlParams:
        base = self.DEFAULTS.get(profile.skill_level, self.DEFAULTS["intermediate"])
        misuse = min(0.2, max(0.0, profile.cooldown_misuse_rate) * 0.1)
        drift = min(0.1, max(0.0, profile.joystick_drift) * 0.05)
        return SkillControlParams(
            prior_weight_lambda=max(0.0, min(1.0, base.prior_weight_lambda + misuse)),
            tau=max(0.50, min(0.95, base.tau - drift)),
            delta=max(0.05, min(0.40, base.delta - drift * 0.5)),
            correction_radius=base.correction_radius + drift,
            feedback_intensity=base.feedback_intensity,
            context_button_emphasis=base.context_button_emphasis * max(0.25, min(1.0, profile.context_button_understanding + 0.25)),
        )


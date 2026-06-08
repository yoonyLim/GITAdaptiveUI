from __future__ import annotations

import math

from analysis_d2e.src.schemas import ButtonSpec, DecodeResult, SkillProfile, TouchEvent, TouchProfile
from analysis_d2e.src.user_skill_controller import UserSkillController
from analysis_d2e.src.user_touch_likelihood_model import UserTouchLikelihoodModel


def softmax(scores: dict[str, float]) -> dict[str, float]:
    if not scores:
        return {}
    max_score = max(scores.values())
    exps = {key: math.exp(value - max_score) for key, value in scores.items()}
    total = sum(exps.values()) or 1.0
    return {key: value / total for key, value in exps.items()}


class BayesianInputDecoder:
    def __init__(
        self,
        touch_model: UserTouchLikelihoodModel | None = None,
        skill_controller: UserSkillController | None = None,
    ) -> None:
        self.touch_model = touch_model or UserTouchLikelihoodModel()
        self.skill_controller = skill_controller or UserSkillController()

    def decode(
        self,
        touch: TouchEvent,
        buttons: list[ButtonSpec],
        button_prior: dict[str, float],
        touch_profile: TouchProfile,
        skill_profile: SkillProfile,
        use_skill: bool = True,
        fixed_lambda: float = 0.6,
    ) -> DecodeResult:
        visual = self.touch_model.visual_prediction(touch, buttons)
        expanded = self.touch_model.expanded_prediction(touch, buttons)
        params = self.skill_controller.params_for(skill_profile)
        prior_weight = params.prior_weight_lambda if use_skill else fixed_lambda
        tau = params.tau if use_skill else 0.72
        delta = params.delta if use_skill else 0.20
        correction_radius = params.correction_radius if use_skill else 1.25

        button_by_id = {button.button_id: button for button in buttons}
        if visual and self.touch_model.is_clear_visual_input(touch, button_by_id[visual]):
            return DecodeResult(
                predicted_button=visual,
                posterior={button.button_id: 1.0 if button.button_id == visual else 0.0 for button in buttons},
                corrected=False,
                invalid_touch=False,
                safety_gate_passed=False,
                safety_gate_reason="clear_input_preserved",
                visual_prediction=visual,
                expanded_prediction=expanded,
            )

        if not self.touch_model.within_recoverable_radius(touch, buttons, correction_radius):
            return DecodeResult(None, {}, False, True, False, "invalid_far_touch", visual, expanded)

        scores = {}
        for button in buttons:
            prior = max(1e-6, float(button_prior.get(button.button_id, 1.0 / max(1, len(buttons)))))
            scores[button.button_id] = self.touch_model.log_likelihood(touch, button, touch_profile) + prior_weight * math.log(prior)
        posterior = softmax(scores)
        ranked = sorted(posterior.items(), key=lambda item: item[1], reverse=True)
        winner, max_prob = ranked[0]
        second = ranked[1][1] if len(ranked) > 1 else 0.0
        gap = max_prob - second

        if not self.touch_model.is_ambiguous(touch, buttons, correction_radius):
            return DecodeResult(expanded, posterior, False, expanded is None, False, "not_ambiguous_preserve_nonclear_baseline", visual, expanded)
        if max_prob < tau:
            return DecodeResult(expanded, posterior, False, expanded is None, False, "posterior_below_tau", visual, expanded)
        if gap < delta:
            return DecodeResult(expanded, posterior, False, expanded is None, False, "posterior_gap_below_delta", visual, expanded)
        if not button_by_id[winner].cooldown_ready or not button_by_id[winner].executable:
            return DecodeResult(expanded, posterior, False, expanded is None, False, "winner_not_executable", visual, expanded)
        reason = "correction_applied" if winner != expanded else "posterior_confirms_expanded_prediction"
        return DecodeResult(winner, posterior, winner != expanded, False, True, reason, visual, expanded)

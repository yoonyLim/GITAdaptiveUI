from __future__ import annotations

from analysis_d2e.src.bayesian_input_decoder import BayesianInputDecoder
from analysis_d2e.src.schemas import ButtonSpec, SkillProfile, TouchEvent, TouchProfile
from analysis_d2e.src.user_touch_likelihood_model import UserTouchLikelihoodModel


class FixedVisualBoundary:
    name = "FixedVisualBoundary"

    def predict(self, touch: TouchEvent, buttons: list[ButtonSpec], *_args, **_kwargs) -> str | None:
        return UserTouchLikelihoodModel().visual_prediction(touch, buttons)


class UniformExpandedHitbox:
    name = "UniformExpandedHitbox"

    def predict(self, touch: TouchEvent, buttons: list[ButtonSpec], *_args, **_kwargs) -> str | None:
        return UserTouchLikelihoodModel().expanded_prediction(touch, buttons)


class UserSpecificHitbox:
    name = "UserSpecificHitbox"

    def predict(self, touch: TouchEvent, buttons: list[ButtonSpec], _prior, touch_profile: TouchProfile, *_args, **_kwargs) -> str | None:
        model = UserTouchLikelihoodModel()
        scores = {button.button_id: model.log_likelihood(touch, button, touch_profile) for button in buttons}
        winner = max(scores, key=scores.get)
        return winner if model.within_recoverable_radius(touch, buttons, 1.15) else None


class SituationUserBayesian:
    name = "SituationUserBayesian"

    def predict(self, touch: TouchEvent, buttons: list[ButtonSpec], prior: dict[str, float], touch_profile: TouchProfile, skill_profile: SkillProfile) -> str | None:
        return BayesianInputDecoder().decode(touch, buttons, prior, touch_profile, skill_profile, use_skill=False).predicted_button


class SituationUserSkillBayesian:
    name = "SituationUserSkillBayesian"

    def predict(self, touch: TouchEvent, buttons: list[ButtonSpec], prior: dict[str, float], touch_profile: TouchProfile, skill_profile: SkillProfile) -> str | None:
        return BayesianInputDecoder().decode(touch, buttons, prior, touch_profile, skill_profile, use_skill=True).predicted_button


BASELINES = [FixedVisualBoundary(), UniformExpandedHitbox(), UserSpecificHitbox(), SituationUserBayesian(), SituationUserSkillBayesian()]


from __future__ import annotations

import math

from analysis_d2e.src.schemas import ButtonSpec, TouchEvent, TouchProfile


class UserTouchLikelihoodModel:
    def log_likelihood(self, touch: TouchEvent, button: ButtonSpec, profile: TouchProfile) -> float:
        offset = profile.button_mean_offset.get(button.button_id, (0.0, 0.0))
        var_x, var_y, cov = profile.button_covariance.get(
            button.button_id,
            (max(1.0, profile.touch_variance), max(1.0, profile.touch_variance), 0.0),
        )
        var_x = max(16.0, float(var_x))
        var_y = max(16.0, float(var_y))
        cov = float(cov)
        dx = touch.x - (button.center_x + offset[0])
        dy = touch.y - (button.center_y + offset[1])
        det = max(1e-6, var_x * var_y - cov * cov)
        quad = (var_y * dx * dx - 2 * cov * dx * dy + var_x * dy * dy) / det
        return -0.5 * (math.log(det) + quad)

    def visual_prediction(self, touch: TouchEvent, buttons: list[ButtonSpec]) -> str | None:
        hits = [button for button in buttons if self._distance(touch, button) <= button.visual_radius]
        if len(hits) == 1:
            return hits[0].button_id
        return None

    def expanded_prediction(self, touch: TouchEvent, buttons: list[ButtonSpec]) -> str | None:
        hits = [button for button in buttons if self._distance(touch, button) <= button.hitbox_radius]
        if len(hits) == 1:
            return hits[0].button_id
        if len(hits) > 1:
            return min(hits, key=lambda button: self._distance(touch, button)).button_id
        return None

    def is_clear_visual_input(self, touch: TouchEvent, button: ButtonSpec, margin_ratio: float = 0.78) -> bool:
        return self._distance(touch, button) <= button.visual_radius * margin_ratio

    def is_ambiguous(self, touch: TouchEvent, buttons: list[ButtonSpec], correction_radius_ratio: float) -> bool:
        distances = sorted(((self._distance(touch, button), button) for button in buttons), key=lambda item: item[0])
        if not distances:
            return False
        nearest_dist, nearest = distances[0]
        if nearest_dist > nearest.hitbox_radius * correction_radius_ratio:
            return False
        near_boundary = nearest.visual_radius * 0.78 < nearest_dist <= nearest.hitbox_radius * correction_radius_ratio
        if len(distances) < 2:
            return near_boundary
        second_dist, second = distances[1]
        inter_button_gap = abs(second_dist - nearest_dist) <= max(nearest.visual_radius, second.visual_radius) * 0.55
        return near_boundary or inter_button_gap

    def within_recoverable_radius(self, touch: TouchEvent, buttons: list[ButtonSpec], correction_radius_ratio: float) -> bool:
        return any(self._distance(touch, button) <= button.hitbox_radius * correction_radius_ratio for button in buttons)

    @staticmethod
    def _distance(touch: TouchEvent, button: ButtonSpec) -> float:
        return math.hypot(touch.x - button.center_x, touch.y - button.center_y)

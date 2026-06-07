from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from analysis_d2e.src.action_to_n_button_projector import ActionToNButtonProjector
from analysis_d2e.src.bayesian_input_decoder import BayesianInputDecoder
from analysis_d2e.src.context_button_policy import ContextButtonPolicy
from analysis_d2e.src.d2e_action_prior_model import D2EActionPriorModel
from analysis_d2e.src.io_utils import safe_float, write_json
from analysis_d2e.src.paths import MODELS_DIR
from analysis_d2e.src.schemas import ATOMIC_ACTIONS, ButtonSpec, SkillProfile, TouchEvent, TouchProfile
from analysis_d2e.src.user_skill_controller import UserSkillController


def _normalize_atomic(raw: dict[str, Any] | None) -> dict[str, float]:
    values = {action: max(0.0, safe_float((raw or {}).get(action), 0.0)) for action in ATOMIC_ACTIONS}
    total = sum(values.values())
    if total <= 0.0:
        return {action: 1.0 / len(ATOMIC_ACTIONS) for action in ATOMIC_ACTIONS}
    return {action: value / total for action, value in values.items()}


def _load_model(model_path: Path | None = None) -> D2EActionPriorModel | None:
    path = model_path or (MODELS_DIR / "d2e_action_prior_model.json")
    if not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("status") != "trained":
        return None
    return D2EActionPriorModel.from_dict(data)


def _atomic_prior_from_request(request: dict[str, Any], model: D2EActionPriorModel | None) -> tuple[dict[str, float], str]:
    if isinstance(request.get("atomic_distribution"), dict):
        return _normalize_atomic(request["atomic_distribution"]), "client_atomic_distribution"
    frame_sample = request.get("frame_sample")
    if isinstance(frame_sample, dict) and model is not None:
        return model.predict(frame_sample), "d2e_action_prior_model"
    return _normalize_atomic(None), "uniform_fallback_no_action_prior"


def _default_buttons(n_buttons: int, context: dict[str, Any] | None) -> list[ButtonSpec]:
    specs = [
        ("attack", "attack", 120.0, 360.0),
        ("defense", "defense", 220.0, 360.0),
        ("skill", "skill", 320.0, 360.0),
        ("context", str((context or {}).get("context_action", "context")), 420.0, 360.0),
    ]
    return [
        ButtonSpec(
            button_id=button_id,
            action=action,
            center_x=x,
            center_y=y,
            visual_radius=34.0,
            hitbox_radius=48.0,
            visible_label=str((context or {}).get("visible_label", "Context")) if button_id == "context" else button_id.title(),
        )
        for button_id, action, x, y in specs[:n_buttons]
    ]


def _buttons_from_request(request: dict[str, Any], n_buttons: int, context: dict[str, Any] | None) -> list[ButtonSpec]:
    layout = request.get("button_layout")
    if not isinstance(layout, list) or not layout:
        return _default_buttons(n_buttons, context)
    buttons = []
    for row in layout[:n_buttons]:
        button_id = str(row.get("button_id") or row.get("id") or row.get("action"))
        action = str(row.get("action") or button_id)
        visible_label = str(row.get("visible_label") or ((context or {}).get("visible_label") if button_id == "context" else button_id.title()))
        buttons.append(
            ButtonSpec(
                button_id=button_id,
                action=action,
                center_x=safe_float(row.get("center_x")),
                center_y=safe_float(row.get("center_y")),
                visual_radius=safe_float(row.get("visual_radius"), 34.0),
                hitbox_radius=safe_float(row.get("hitbox_radius"), 48.0),
                cooldown_ready=_bool(row.get("cooldown_ready", True)),
                executable=_bool(row.get("executable", True)),
                visible_label=visible_label,
            )
        )
    return buttons


def _bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() not in {"false", "0", "no", "off"}


def _touch_profile(data: dict[str, Any] | None) -> TouchProfile:
    data = data or {}
    return TouchProfile(
        button_mean_offset=data.get("button_mean_offset") or {},
        button_covariance=data.get("button_covariance") or {},
        near_miss_rate=safe_float(data.get("near_miss_rate"), 0.0),
        touch_variance=safe_float(data.get("touch_variance"), 900.0),
    )


def _skill_profile(data: dict[str, Any] | None) -> SkillProfile:
    data = data or {}
    return SkillProfile(
        skill_level=str(data.get("skill_level") or "intermediate"),
        reaction_time_mean=safe_float(data.get("reaction_time_mean"), 300.0),
        reinput_rate=safe_float(data.get("reinput_rate"), 0.0),
        joystick_drift=safe_float(data.get("joystick_drift"), 0.0),
        cooldown_misuse_rate=safe_float(data.get("cooldown_misuse_rate"), 0.0),
        context_button_understanding=safe_float(data.get("context_button_understanding"), 0.5),
    )


def decode_request(request: dict[str, Any], model: D2EActionPriorModel | None = None) -> dict[str, Any]:
    model = model if model is not None else _load_model()
    n_buttons = int(safe_float(request.get("n_buttons"), 2))
    if n_buttons not in {2, 3, 4}:
        raise ValueError("n_buttons must be 2, 3, or 4")

    atomic, prior_source = _atomic_prior_from_request(request, model)
    situation = request.get("situation") if isinstance(request.get("situation"), dict) else {}
    projected = ActionToNButtonProjector(ContextButtonPolicy()).project(atomic, n_buttons, situation)
    button_prior = dict(projected["button_prior"])
    context = projected.get("context") if isinstance(projected.get("context"), dict) else None
    buttons = _buttons_from_request(request, n_buttons, context)

    touch_data = request.get("touch") or {}
    result = BayesianInputDecoder().decode(
        TouchEvent(x=safe_float(touch_data.get("x")), y=safe_float(touch_data.get("y"))),
        buttons,
        button_prior,
        _touch_profile(request.get("touch_profile") if isinstance(request.get("touch_profile"), dict) else None),
        _skill_profile(request.get("skill_profile") if isinstance(request.get("skill_profile"), dict) else None),
        use_skill=bool(request.get("use_skill", True)),
    )
    skill_params = UserSkillController().params_for(_skill_profile(request.get("skill_profile") if isinstance(request.get("skill_profile"), dict) else None))
    return {
        "predicted_button": result.predicted_button,
        "invalid_touch": result.invalid_touch,
        "corrected": result.corrected,
        "safety_gate_passed": result.safety_gate_passed,
        "safety_gate_reason": result.safety_gate_reason,
        "visual_prediction": result.visual_prediction,
        "expanded_prediction": result.expanded_prediction,
        "posterior": result.posterior,
        "button_prior": button_prior,
        "atomic_distribution": atomic,
        "prior_source": prior_source,
        "context": context,
        "skill_control": {
            "lambda": skill_params.prior_weight_lambda,
            "tau": skill_params.tau,
            "delta": skill_params.delta,
            "correction_radius": skill_params.correction_radius,
            "feedback_intensity": skill_params.feedback_intensity,
            "context_button_emphasis": skill_params.context_button_emphasis,
        },
        "button_layout": [button.__dict__ for button in buttons],
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", type=Path, default=None, help="JSON request path. If omitted, reads stdin.")
    parser.add_argument("--output", type=Path, default=None)
    parser.add_argument("--model", type=Path, default=None)
    args = parser.parse_args()
    text = args.request.read_text(encoding="utf-8") if args.request else sys.stdin.read()
    response = decode_request(json.loads(text), model=_load_model(args.model))
    if args.output:
        write_json(args.output, response)
    print(json.dumps(response, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

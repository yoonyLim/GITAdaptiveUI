from __future__ import annotations

from types import SimpleNamespace

from analysis_d2e.src.d2e_owa_loader import token_from_keyboard, tokens_from_keyboard_state


def test_token_from_keyboard_maps_vk_press():
    token, pressed = token_from_keyboard(SimpleNamespace(vk=32, event_type="press"))

    assert token == "space"
    assert pressed is True


def test_token_from_keyboard_normalizes_escape_and_left_shift():
    escape, _ = token_from_keyboard(SimpleNamespace(vk=27, event_type="press"))
    shift, _ = token_from_keyboard(SimpleNamespace(vk=160, event_type="press"))

    assert escape == "escape"
    assert shift == "shift"


def test_tokens_from_keyboard_state_maps_vk_list():
    tokens = tokens_from_keyboard_state({"pressed": [87, 65, 32]})

    assert {"w", "a", "space"}.issubset(tokens)

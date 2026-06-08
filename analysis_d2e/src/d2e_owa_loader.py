from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any


VK_TO_TOKEN = {
    1: "mouse_left",
    2: "mouse_right",
    16: "shift",
    17: "ctrl",
    18: "alt",
    27: "escape",
    32: "space",
    49: "key_1",
    50: "key_2",
    51: "key_3",
    52: "key_4",
    53: "key_5",
    65: "a",
    68: "d",
    69: "e",
    70: "f",
    72: "h",
    81: "q",
    82: "r",
    83: "s",
    87: "w",
    88: "x",
    90: "z",
    160: "shift",
    161: "shift",
}


def import_owa_reader() -> Any:
    try:
        from mcap_owa.highlevel import OWAMcapReader

        return OWAMcapReader
    except ModuleNotFoundError as exc:
        raise RuntimeError(
            "Install D2E OWAMcap dependencies: uv run --with mcap-owa-support --with owa-msgs --with pillow ..."
        ) from exc


def message_timestamp_ns(msg: Any) -> int:
    for attr in ("log_time", "publish_time", "timestamp", "time_ns"):
        value = getattr(msg, attr, None)
        if value is not None:
            try:
                return int(value)
            except Exception:
                pass
    decoded = getattr(msg, "decoded", None)
    for attr in ("timestamp_ns", "time_ns", "timestamp"):
        value = getattr(decoded, attr, None)
        if value is not None:
            try:
                return int(value)
            except Exception:
                pass
    return 0


def decoded_to_dict(decoded: Any) -> dict[str, Any]:
    if decoded is None:
        return {}
    if isinstance(decoded, dict):
        return decoded
    if hasattr(decoded, "model_dump"):
        try:
            return dict(decoded.model_dump(mode="python"))
        except Exception:
            pass
    result: dict[str, Any] = {}
    for attr in dir(decoded):
        if attr.startswith("_") or attr.startswith("model_"):
            continue
        try:
            value = getattr(decoded, attr)
        except Exception:
            continue
        if callable(value):
            continue
        if isinstance(value, (str, int, float, bool, type(None), list, tuple, set, dict)):
            result[attr] = value
    if result:
        return result
    text = repr(decoded)
    for key, value in re.findall(r"(\w+)=('.*?'|\".*?\"|-?\d+\.?\d*)", text):
        result[key] = value.strip("'\"")
    return result


def token_from_keyboard(decoded: Any) -> tuple[str | None, bool | None]:
    data = decoded_to_dict(decoded)
    vk = data.get("vk") or data.get("virtual_key") or data.get("virtual_key_code") or data.get("key")
    try:
        token = VK_TO_TOKEN.get(int(vk), f"vk_{int(vk)}")
    except Exception:
        token = str(vk).lower() if vk is not None else None
    event_type = str(data.get("event_type") or data.get("type") or "").lower()
    pressed: bool | None
    if "release" in event_type or event_type in {"up", "keyup"}:
        pressed = False
    elif "press" in event_type or event_type in {"down", "keydown"}:
        pressed = True
    else:
        pressed = None
    return token, pressed


def tokens_from_keyboard_state(decoded: Any) -> set[str]:
    data = decoded_to_dict(decoded)
    tokens: set[str] = set()
    for key, value in data.items():
        key_lower = str(key).lower()
        if isinstance(value, bool) and value:
            tokens.add(key_lower)
        elif isinstance(value, (int, float)) and value > 0 and key_lower not in {"timestamp", "timestamp_ns", "time_ns"}:
            try:
                tokens.add(VK_TO_TOKEN.get(int(value), f"vk_{int(value)}"))
            except Exception:
                tokens.add(key_lower)
        elif isinstance(value, (list, tuple, set)):
            for item in value:
                try:
                    tokens.add(VK_TO_TOKEN.get(int(item), f"vk_{int(item)}"))
                except Exception:
                    text = str(item).strip().lower()
                    if text:
                        tokens.add(text)
        elif isinstance(value, dict):
            for nested_key, nested_value in value.items():
                if nested_value:
                    try:
                        tokens.add(VK_TO_TOKEN.get(int(nested_key), f"vk_{int(nested_key)}"))
                    except Exception:
                        text = str(nested_key).strip().lower()
                        if text:
                            tokens.add(text)
    return {token for token in tokens if token and token not in {"none", "false"}}


def update_mouse_tokens(tokens: set[str], decoded: Any) -> None:
    data = decoded_to_dict(decoded)
    button = str(data.get("button") or "").lower()
    pressed = data.get("pressed")
    if button in {"left", "right"} and pressed is True:
        tokens.add(f"mouse_{button}")
    elif button in {"left", "right"} and pressed is False:
        tokens.discard(f"mouse_{button}")
    buttons = data.get("buttons")
    if isinstance(buttons, (list, tuple, set)):
        for item in buttons:
            item_text = str(item).lower()
            if item_text in {"left", "right"}:
                tokens.add(f"mouse_{item_text}")
    flags = data.get("button_flags")
    try:
        flags_int = int(flags)
        if flags_int & 1:
            tokens.add("mouse_left")
        if flags_int & 2:
            tokens.add("mouse_right")
    except Exception:
        pass


def save_frame(decoded: Any, mcap_file: Path, output_path: Path) -> bool:
    try:
        decoded.resolve_relative_path(str(mcap_file))
    except Exception:
        try:
            decoded.resolve_relative_path(mcap_file)
        except Exception:
            pass
    try:
        array = decoded.load_frame_array()
    except Exception:
        return False
    try:
        from PIL import Image

        output_path.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(array).save(output_path)
        return True
    except Exception:
        return False


def extract_owa_samples(
    mcap_file: Path,
    output_frame_dir: Path,
    max_samples: int = 500,
    frame_stride: int = 30,
) -> list[dict[str, Any]]:
    OWAMcapReader = import_owa_reader()
    samples: list[dict[str, Any]] = []
    pressed_tokens: set[str] = set()
    screen_idx = 0
    topics = ["screen", "keyboard", "keyboard/state", "mouse", "mouse/state", "mouse/raw"]
    game_id = mcap_file.parent.name
    with OWAMcapReader(str(mcap_file)) as reader:
        for msg in reader.iter_messages(topics=topics):
            topic = str(getattr(msg, "topic", ""))
            decoded = getattr(msg, "decoded", None)
            if topic == "keyboard":
                token, pressed = token_from_keyboard(decoded)
                if token and pressed is True:
                    pressed_tokens.add(token)
                elif token and pressed is False:
                    pressed_tokens.discard(token)
            elif topic == "keyboard/state":
                state_tokens = tokens_from_keyboard_state(decoded)
                if state_tokens:
                    pressed_tokens = state_tokens
            elif topic.startswith("mouse"):
                update_mouse_tokens(pressed_tokens, decoded)
            elif topic == "screen":
                if screen_idx % frame_stride == 0:
                    frame_path = output_frame_dir / game_id / f"{mcap_file.stem}_{screen_idx:08d}.png"
                    if save_frame(decoded, mcap_file, frame_path):
                        samples.append(
                            {
                                "frame_path": str(frame_path),
                                "game_id": game_id,
                                "phase": "",
                                "episode_id": mcap_file.stem,
                                "frame_idx": screen_idx,
                                "timestamp_ns": message_timestamp_ns(msg),
                                "raw_input": sorted(pressed_tokens),
                                "source_mcap": str(mcap_file),
                            }
                        )
                    if len(samples) >= max_samples:
                        break
                screen_idx += 1
    return samples

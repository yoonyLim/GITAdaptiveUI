from __future__ import annotations

import math
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import clamp, safe_float


STATE_FEATURE_KEYS = (
    "enemy_count_proxy",
    "nearest_enemy_distance",
    "enemy_distance_delta",
    "enemy_approaching_bool",
    "nearby_enemy_count",
    "surroundedness",
    "projectile_visible_proxy",
    "player_hp_norm",
    "low_hp_bool",
    "threat_score",
)


def detect_ui_overlay_proxy(frame_path: str | Path | None) -> bool:
    if not frame_path:
        return False
    path = Path(str(frame_path))
    if not path.exists():
        return False
    try:
        from PIL import Image

        with Image.open(path) as img:
            rgb = img.convert("RGB").resize((160, 90))
            pixels = rgb.load()
    except Exception:
        return False

    bright = 0
    blueish = 0
    edge = 0
    count = 0
    edge_count = 0
    for y in range(10, 85):
        for x in range(35, 155):
            r, g, b = pixels[x, y]
            if max(r, g, b) > 95:
                bright += 1
            if b > 70 and b >= r * 1.05 and b >= g * 0.80:
                blueish += 1
            count += 1
    for y in range(10, 84):
        for x in range(35, 154):
            current = sum(pixels[x, y]) / 3.0
            right = sum(pixels[x + 1, y]) / 3.0
            down = sum(pixels[x, y + 1]) / 3.0
            if abs(current - right) > 30 or abs(current - down) > 30:
                edge += 1
            edge_count += 1

    bright_ratio = bright / max(1, count)
    blue_ratio = blueish / max(1, count)
    edge_ratio = edge / max(1, edge_count)
    return edge_ratio >= 0.22 and (bright_ratio >= 0.28 or blue_ratio >= 0.30)


def _grid_salience_positions(frame_path: str | Path | None) -> tuple[list[dict[str, float]], float]:
    if not frame_path:
        return [], 0.0
    path = Path(str(frame_path))
    if not path.exists():
        return [], 0.0
    try:
        from PIL import Image

        image = path.open("rb")
        with Image.open(image) as img:
            rgb = img.convert("RGB").resize((96, 96))
            pixels = list(rgb.getdata())
    except Exception:
        return [], 0.0

    cell = 12
    positions: list[dict[str, float]] = []
    edge_energy = 0.0
    brightness = [sum(pixel) / (255.0 * 3.0) for pixel in pixels]
    for y in range(95):
        for x in range(95):
            edge_energy += abs(brightness[y * 96 + x + 1] - brightness[y * 96 + x])
            edge_energy += abs(brightness[(y + 1) * 96 + x] - brightness[y * 96 + x])
    edge_energy /= max(1, 95 * 95 * 2)

    for gy in range(8):
        for gx in range(8):
            salient = 0
            total = 0
            red_or_yellow = 0
            for y in range(gy * cell, (gy + 1) * cell):
                for x in range(gx * cell, (gx + 1) * cell):
                    r, g, b = pixels[y * 96 + x]
                    mx = max(r, g, b)
                    mn = min(r, g, b)
                    saturation = (mx - mn) / max(1, mx)
                    value = mx / 255.0
                    is_salient = saturation > 0.42 and value > 0.20
                    if is_salient:
                        salient += 1
                    if is_salient and (r > g * 1.08 or (r > 135 and g > 80 and b < 100)):
                        red_or_yellow += 1
                    total += 1
            salient_ratio = salient / max(1, total)
            warm_ratio = red_or_yellow / max(1, total)
            if salient_ratio > 0.18 or warm_ratio > 0.06:
                positions.append(
                    {
                        "x_norm": (gx + 0.5) / 8.0,
                        "y_norm": (gy + 0.5) / 8.0,
                        "salience": round(max(salient_ratio, warm_ratio), 4),
                    }
                )
    positions = sorted(positions, key=lambda item: item["salience"], reverse=True)[:16]
    return positions, edge_energy


def estimate_threat_state(sample: dict[str, Any], previous_sample: dict[str, Any] | None = None) -> dict[str, Any]:
    positions, edge_energy = _grid_salience_positions(sample.get("frame_path"))
    previous_positions, _ = _grid_salience_positions(previous_sample.get("frame_path") if previous_sample else None)
    player_x = safe_float(sample.get("player_position_x_norm"), 0.5)
    player_y = safe_float(sample.get("player_position_y_norm"), 0.5)

    metadata_enemy_count = safe_float(sample.get("melee_enemy_count"), 0.0) + safe_float(sample.get("ranged_enemy_count"), 0.0)
    enemy_count = max(float(len(positions)), metadata_enemy_count)
    distances = [math.hypot(pos["x_norm"] - player_x, pos["y_norm"] - player_y) for pos in positions]
    nearest = min(distances) if distances else 1.0
    previous_distances = [math.hypot(pos["x_norm"] - player_x, pos["y_norm"] - player_y) for pos in previous_positions]
    previous_nearest = min(previous_distances) if previous_distances else nearest
    delta = nearest - previous_nearest
    approaching = delta < -0.025
    nearby = sum(1 for distance in distances if distance <= 0.38)
    quadrants = set()
    for pos in positions:
        if math.hypot(pos["x_norm"] - player_x, pos["y_norm"] - player_y) <= 0.55:
            quadrants.add(("right" if pos["x_norm"] >= player_x else "left", "down" if pos["y_norm"] >= player_y else "up"))
    surroundedness = len(quadrants) / 4.0
    hp = clamp(safe_float(sample.get("player_hp_norm"), 1.0), 0.0, 1.0)
    projectile = edge_energy > 0.085 and any(pos["salience"] > 0.30 for pos in positions[:4])
    threat_score = clamp(
        0.22 * min(enemy_count / 10.0, 1.0)
        + 0.22 * min(nearby / 4.0, 1.0)
        + 0.22 * float(approaching)
        + 0.18 * float(projectile)
        + 0.16 * float(hp <= 0.35),
        0.0,
        1.0,
    )
    return {
        "player_position_x_norm": player_x,
        "player_position_y_norm": player_y,
        "enemy_positions_proxy": positions,
        "enemy_count_proxy": round(enemy_count, 4),
        "nearest_enemy_distance": round(nearest, 4),
        "enemy_distance_delta": round(delta, 4),
        "enemy_approaching_bool": approaching,
        "nearby_enemy_count": nearby,
        "surroundedness": round(surroundedness, 4),
        "projectile_visible_proxy": projectile,
        "player_hp_norm": round(hp, 4),
        "low_hp_bool": hp <= 0.35,
        "threat_score": round(threat_score, 4),
        "state_label_source": "vision_proxy_from_frame_salience_and_available_metadata",
    }


def state_feature_vector(row: dict[str, Any]) -> list[float]:
    return [float(row.get(key, 0.0) is True) if isinstance(row.get(key), bool) else safe_float(row.get(key), 0.0) for key in STATE_FEATURE_KEYS]

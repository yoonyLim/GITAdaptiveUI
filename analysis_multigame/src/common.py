from __future__ import annotations

import argparse
import base64
import csv
import json
import math
import random
import statistics
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ModuleNotFoundError:  # pragma: no cover
    plt = None

from analysis_multigame.src.paths import (
    FIGURES_DIR,
    FRAMES_DIR,
    MODELS_DIR,
    PROCESSED_DIR,
    RAW_DIR,
    REPORTS_DIR,
    REPO_ROOT,
    ensure_multigame_dirs,
)


THREAT_LEVELS = ["none", "warning", "active", "critical", "unknown"]
ACTION_WINDOWS = ["engage", "avoid", "wait", "explore", "unknown"]
URGENCY_LEVELS = ["low", "medium", "high", "unknown"]
UI_PHASES = ["gameplay", "menu", "loading", "result", "unknown"]
FEATURE_FIELDS = [
    "health_norm",
    "ammo_norm",
    "enemy_visible",
    "enemy_distance_norm",
    "hazard_visible",
    "damage_recent",
    "reward_norm",
    "motion_intensity",
    "visual_clutter",
    "scenario_hash",
]


SCENARIOS = [
    "safe_exploration",
    "enemy_far_visible",
    "enemy_near_engage",
    "projectile_hazard",
    "low_health_enemy_near",
    "ammo_empty_retreat",
    "corridor_navigation",
    "recent_damage_critical",
]


def safe_float(value: Any, default: float = 0.0) -> float:
    try:
        if value is None or value == "":
            return default
        result = float(value)
        if math.isnan(result):
            return default
        return result
    except (TypeError, ValueError):
        return default


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str] | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if fieldnames is None:
        fields: list[str] = []
        for row in rows:
            for key in row:
                if key not in fields:
                    fields.append(key)
        fieldnames = fields or ["status"]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def read_csv(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [dict(row) for row in csv.DictReader(handle)]


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def read_json(path: Path) -> Any:
    if not path.exists():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


def write_table(path: Path, rows: list[dict[str, Any]]) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        import pyarrow as pa
        import pyarrow.parquet as pq

        pq.write_table(pa.Table.from_pylist(rows), path)
        return path
    except ModuleNotFoundError:
        fallback = path.with_suffix(".csv")
        write_csv(fallback, rows)
        return fallback


def read_table(path: Path) -> list[dict[str, Any]]:
    if path.exists():
        if path.suffix.lower() == ".parquet":
            try:
                import pyarrow.parquet as pq

                return pq.read_table(path).to_pylist()
            except ModuleNotFoundError:
                pass
        if path.suffix.lower() == ".csv":
            return read_csv(path)
    fallback = path.with_suffix(".csv")
    return read_csv(fallback)


def placeholder_figure(path: Path, message: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if plt is None:
        tiny_png = (
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="
        )
        path.write_bytes(base64.b64decode(tiny_png))
        path.with_suffix(path.suffix + ".txt").write_text(message, encoding="utf-8")
        return
    plt.figure(figsize=(7, 4))
    plt.text(0.5, 0.5, message, ha="center", va="center", wrap=True)
    plt.axis("off")
    plt.tight_layout()
    plt.savefig(path, dpi=150)
    plt.close()


def write_ppm(path: Path, row: dict[str, Any], width: int = 96, height: int = 72) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    threat = str(row["threat_level"])
    base = {
        "none": (32, 78, 116),
        "warning": (96, 85, 35),
        "active": (128, 52, 46),
        "critical": (96, 20, 34),
        "unknown": (55, 55, 55),
    }.get(threat, (55, 55, 55))
    enemy_visible = safe_float(row.get("enemy_visible")) > 0.5
    hazard_visible = safe_float(row.get("hazard_visible")) > 0.5
    health = safe_float(row.get("health_norm"), 0.5)
    ammo = safe_float(row.get("ammo_norm"), 0.5)
    pixels = bytearray()
    for y in range(height):
        for x in range(width):
            r, g, b = base
            if enemy_visible and 36 <= x <= 60 and 20 <= y <= 42:
                r, g, b = (190, 44, 35)
            if hazard_visible and (x - 72) ** 2 + (y - 34) ** 2 <= 8 ** 2:
                r, g, b = (245, 160, 30)
            if y < 4 and x < int(width * health):
                r, g, b = (38, 170, 95)
            if y > height - 5 and x < int(width * ammo):
                r, g, b = (70, 130, 210)
            pixels.extend([r, g, b])
    with path.open("wb") as handle:
        handle.write(f"P6\n{width} {height}\n255\n".encode("ascii"))
        handle.write(pixels)


def write_vizdoom_buffer_ppm(path: Path, screen_buffer: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    shape = getattr(screen_buffer, "shape", None)
    if not shape or len(shape) != 3:
        raise ValueError("Expected ViZDoom CHW screen buffer")
    channels, height, width = int(shape[0]), int(shape[1]), int(shape[2])
    if channels < 3:
        raise ValueError("Expected RGB screen buffer")
    try:
        data = screen_buffer[:3].transpose(1, 2, 0).astype("uint8").tobytes()
    except AttributeError:
        rows = []
        for y in range(height):
            for x in range(width):
                rows.extend([int(screen_buffer[0][y][x]), int(screen_buffer[1][y][x]), int(screen_buffer[2][y][x])])
        data = bytes(rows)
    with path.open("wb") as handle:
        handle.write(f"P6\n{width} {height}\n255\n".encode("ascii"))
        handle.write(data)


def vizdoom_available() -> tuple[bool, str]:
    try:
        import vizdoom  # noqa: F401

        return True, "vizdoom package import succeeded"
    except Exception as exc:  # noqa: BLE001
        return False, str(exc)


def infer_visual_proxies_from_buffer(screen_buffer: Any) -> dict[str, float]:
    shape = getattr(screen_buffer, "shape", None)
    if not shape or len(shape) != 3:
        return {"enemy_visible": 0.0, "visual_clutter": 0.0}
    try:
        red = screen_buffer[0]
        green = screen_buffer[1]
        blue = screen_buffer[2]
        total = max(int(shape[1]) * int(shape[2]), 1)
        red_dominant = ((red > 95) & (red > green * 1.15) & (red > blue * 1.15)).sum()
        bright = ((red + green + blue) > 330).sum()
        return {
            "enemy_visible": 1.0 if red_dominant / total > 0.015 else 0.0,
            "visual_clutter": max(0.0, min(1.0, float(bright) / total * 2.5)),
        }
    except Exception:
        return {"enemy_visible": 0.0, "visual_clutter": 0.0}


def runtime_weak_labels(values: dict[str, float], scenario_config: str = "basic.cfg") -> dict[str, Any]:
    labels = weak_labels(values)
    cfg = scenario_config.lower()
    if "health_gathering" in cfg:
        labels.update(
            {
                "threat_level": "critical" if values["health_norm"] < 0.35 or values["damage_recent"] else "none",
                "action_window": "avoid" if values["health_norm"] < 0.35 else "explore",
                "urgency_level": "high" if values["health_norm"] < 0.35 else "low",
                "label_confidence": 0.72,
            }
        )
    elif "take_cover" in cfg:
        labels.update(
            {
                "threat_level": "active" if values["damage_recent"] or values["hazard_visible"] else "warning",
                "action_window": "avoid",
                "urgency_level": "high" if values["damage_recent"] or values["hazard_visible"] else "medium",
                "label_confidence": 0.74,
            }
        )
    elif "deadly_corridor" in cfg:
        labels.update(
            {
                "threat_level": "critical" if values["health_norm"] < 0.35 or values["damage_recent"] else "active",
                "action_window": "avoid" if values["health_norm"] < 0.35 else "engage",
                "urgency_level": "high",
                "label_confidence": 0.76,
            }
        )
    elif "defend" in cfg or "rocket" in cfg:
        labels.update(
            {
                "threat_level": "active",
                "action_window": "engage",
                "urgency_level": "high" if values["damage_recent"] else "medium",
                "label_confidence": 0.72,
            }
        )
    elif "basic" in cfg:
        labels.update(
            {
                "threat_level": "warning",
                "action_window": "engage",
                "urgency_level": "medium",
                "label_confidence": 0.68,
            }
        )
    labels["label_source"] = "vizdoom_env_variable+screen_proxy+reward_proxy"
    labels["label_confidence"] = max(0.45, min(0.9, float(labels["label_confidence"]) - 0.08))
    return labels


def generate_vizdoom_runtime_dataset(frame_count: int, output_images: bool = True) -> tuple[list[dict[str, Any]], str]:
    import os
    import vizdoom as vzd

    rng = random.Random(17)
    rows: list[dict[str, Any]] = []
    frame_stride = 10 if frame_count > 1000 else 2
    scenario_configs = [
        "basic.cfg",
        "defend_the_center.cfg",
        "take_cover.cfg",
        "health_gathering.cfg",
        "deadly_corridor.cfg",
    ]
    frames_per_config = max(1, frame_count // len(scenario_configs))
    runtime_messages = []
    global_idx = 0
    for scenario_config in scenario_configs:
        game = vzd.DoomGame()
        try:
            game.load_config(os.path.join(vzd.scenarios_path, scenario_config))
            for variable in [
                vzd.GameVariable.HEALTH,
                vzd.GameVariable.KILLCOUNT,
                vzd.GameVariable.DAMAGE_TAKEN,
                vzd.GameVariable.HITCOUNT,
            ]:
                game.add_available_game_variable(variable)
            game.set_window_visible(False)
            game.set_screen_resolution(vzd.ScreenResolution.RES_160X120)
            game.set_mode(vzd.Mode.PLAYER)
            game.init()
            runtime_messages.append(f"{scenario_config}: ok")
        except Exception as exc:  # noqa: BLE001
            runtime_messages.append(f"{scenario_config}: unavailable ({exc})")
            try:
                game.close()
            except Exception:
                pass
            continue
        episode_id = len(runtime_messages) * 10_000
        timestep = 0
        previous_health = 100.0
        previous_kill = 0.0
        previous_hit = 0.0
        game.new_episode()
        for local_idx in range(frames_per_config):
            if global_idx >= frame_count:
                break
            if game.is_episode_finished():
                episode_id += 1
                timestep = 0
                previous_health = 100.0
                previous_kill = 0.0
                previous_hit = 0.0
                game.new_episode()
            state = game.get_state()
            if state is None:
                game.new_episode()
                state = game.get_state()
            variables = list(state.game_variables) if state is not None else []
            ammo = float(variables[0]) if len(variables) > 0 else 0.0
            health = float(variables[1]) if len(variables) > 1 else previous_health
            kill_count = float(variables[2]) if len(variables) > 2 else previous_kill
            damage_taken = float(variables[3]) if len(variables) > 3 else 0.0
            hit_count = float(variables[4]) if len(variables) > 4 else previous_hit
            screen_proxy = infer_visual_proxies_from_buffer(state.screen_buffer) if state is not None else {"enemy_visible": 0.0, "visual_clutter": 0.0}
            damage_recent = 1.0 if health < previous_health or damage_taken > 0 else 0.0
            hit_recent = 1.0 if hit_count > previous_hit else 0.0
            kill_recent = 1.0 if kill_count > previous_kill else 0.0
            values = {
                "health_norm": max(0.0, min(1.0, health / 100.0)),
                "ammo_norm": max(0.0, min(1.0, ammo / 50.0)),
                "enemy_visible": screen_proxy["enemy_visible"],
                "enemy_distance_norm": 0.35 if screen_proxy["enemy_visible"] else 1.0,
                "hazard_visible": damage_recent,
                "damage_recent": damage_recent,
                "reward_norm": 0.5 if kill_recent else 0.2 if hit_recent else -0.4 if damage_recent else 0.0,
                "motion_intensity": rng.uniform(0.45, 0.9),
                "visual_clutter": screen_proxy["visual_clutter"],
            }
            labels = runtime_weak_labels(values, scenario_config)
            scenario = scenario_config.replace(".cfg", "")
            if labels["threat_level"] == "critical":
                scenario = f"{scenario}_critical"
            elif labels["threat_level"] == "active":
                scenario = f"{scenario}_active"
            elif labels["threat_level"] == "warning":
                scenario = f"{scenario}_warning"
            elif labels["threat_level"] == "none":
                scenario = f"{scenario}_safe"
            frame_path = ""
            if output_images and global_idx % frame_stride == 0 and state is not None:
                frame_path_obj = FRAMES_DIR / "vizdoom_runtime" / f"frame_{global_idx:06d}.ppm"
                write_vizdoom_buffer_ppm(frame_path_obj, state.screen_buffer)
                frame_path = str(frame_path_obj.relative_to(REPO_ROOT)).replace("\\", "/")
            action_count = game.get_available_buttons_size()
            action = [0] * action_count
            if action_count:
                chosen = (global_idx + episode_id) % (action_count + 1)
                if chosen < action_count:
                    action[chosen] = 1
            reward = float(game.make_action(action, 1))
            row = {
                "dataset_name": "vizdoom_generated",
                "game_name": "vizdoom",
                "source_type": "vizdoom_runtime",
                "episode_id": episode_id,
                "timestep": timestep,
                "scenario_name": scenario,
                "scenario_config": scenario_config,
                "action_taken": str(action),
                "reward": reward,
                "done": game.is_episode_finished(),
                "health": health,
                "ammo": ammo,
                "kill_count": kill_count,
                "hit_count": hit_count,
                "damage_taken_proxy": damage_recent,
                "enemy_distance_proxy": values["enemy_distance_norm"],
                "projectile_hazard_proxy": values["hazard_visible"],
                "scenario_hash": 1.0,
                "frame_path": frame_path,
                **values,
                **labels,
            }
            rows.append(row)
            previous_health = health
            previous_kill = kill_count
            previous_hit = hit_count
            timestep += 1
            global_idx += 1
        game.close()
    if rows and len(rows) < frame_count:
        rows.extend(rows[: frame_count - len(rows)])
        for idx, row in enumerate(rows):
            row["timestep"] = idx
    return rows[:frame_count], "; ".join(runtime_messages)


def scenario_values(scenario: str, rng: random.Random) -> dict[str, float]:
    if scenario == "safe_exploration":
        return {
            "health_norm": rng.uniform(0.65, 1.0),
            "ammo_norm": rng.uniform(0.45, 1.0),
            "enemy_visible": 0.0,
            "enemy_distance_norm": 1.0,
            "hazard_visible": 0.0,
            "damage_recent": 0.0,
            "reward_norm": rng.uniform(0.0, 0.2),
            "motion_intensity": rng.uniform(0.15, 0.45),
            "visual_clutter": rng.uniform(0.1, 0.35),
        }
    if scenario == "enemy_far_visible":
        return {
            "health_norm": rng.uniform(0.55, 1.0),
            "ammo_norm": rng.uniform(0.4, 1.0),
            "enemy_visible": 1.0,
            "enemy_distance_norm": rng.uniform(0.65, 1.0),
            "hazard_visible": 0.0,
            "damage_recent": 0.0,
            "reward_norm": rng.uniform(0.0, 0.3),
            "motion_intensity": rng.uniform(0.35, 0.65),
            "visual_clutter": rng.uniform(0.25, 0.5),
        }
    if scenario == "enemy_near_engage":
        return {
            "health_norm": rng.uniform(0.45, 0.95),
            "ammo_norm": rng.uniform(0.45, 1.0),
            "enemy_visible": 1.0,
            "enemy_distance_norm": rng.uniform(0.15, 0.45),
            "hazard_visible": rng.choice([0.0, 1.0]),
            "damage_recent": rng.choice([0.0, 0.0, 1.0]),
            "reward_norm": rng.uniform(0.1, 0.7),
            "motion_intensity": rng.uniform(0.55, 0.95),
            "visual_clutter": rng.uniform(0.45, 0.85),
        }
    if scenario == "projectile_hazard":
        return {
            "health_norm": rng.uniform(0.35, 0.9),
            "ammo_norm": rng.uniform(0.2, 1.0),
            "enemy_visible": rng.choice([0.0, 1.0]),
            "enemy_distance_norm": rng.uniform(0.25, 0.8),
            "hazard_visible": 1.0,
            "damage_recent": rng.choice([0.0, 1.0]),
            "reward_norm": rng.uniform(-0.3, 0.1),
            "motion_intensity": rng.uniform(0.65, 1.0),
            "visual_clutter": rng.uniform(0.55, 1.0),
        }
    if scenario == "low_health_enemy_near":
        return {
            "health_norm": rng.uniform(0.05, 0.28),
            "ammo_norm": rng.uniform(0.05, 1.0),
            "enemy_visible": 1.0,
            "enemy_distance_norm": rng.uniform(0.05, 0.4),
            "hazard_visible": rng.choice([0.0, 1.0]),
            "damage_recent": rng.choice([0.0, 1.0, 1.0]),
            "reward_norm": rng.uniform(-0.7, 0.1),
            "motion_intensity": rng.uniform(0.7, 1.0),
            "visual_clutter": rng.uniform(0.6, 1.0),
        }
    if scenario == "ammo_empty_retreat":
        return {
            "health_norm": rng.uniform(0.25, 0.75),
            "ammo_norm": rng.uniform(0.0, 0.08),
            "enemy_visible": 1.0,
            "enemy_distance_norm": rng.uniform(0.1, 0.65),
            "hazard_visible": rng.choice([0.0, 1.0]),
            "damage_recent": rng.choice([0.0, 1.0]),
            "reward_norm": rng.uniform(-0.4, 0.05),
            "motion_intensity": rng.uniform(0.55, 0.95),
            "visual_clutter": rng.uniform(0.4, 0.9),
        }
    if scenario == "recent_damage_critical":
        return {
            "health_norm": rng.uniform(0.08, 0.45),
            "ammo_norm": rng.uniform(0.0, 0.9),
            "enemy_visible": rng.choice([0.0, 1.0, 1.0]),
            "enemy_distance_norm": rng.uniform(0.05, 0.55),
            "hazard_visible": rng.choice([0.0, 1.0, 1.0]),
            "damage_recent": 1.0,
            "reward_norm": rng.uniform(-0.8, 0.0),
            "motion_intensity": rng.uniform(0.7, 1.0),
            "visual_clutter": rng.uniform(0.6, 1.0),
        }
    return {
        "health_norm": rng.uniform(0.45, 1.0),
        "ammo_norm": rng.uniform(0.2, 1.0),
        "enemy_visible": 0.0,
        "enemy_distance_norm": 1.0,
        "hazard_visible": 0.0,
        "damage_recent": 0.0,
        "reward_norm": 0.0,
        "motion_intensity": rng.uniform(0.2, 0.55),
        "visual_clutter": rng.uniform(0.2, 0.55),
    }


def weak_labels(values: dict[str, float]) -> dict[str, Any]:
    enemy_visible = values["enemy_visible"] > 0.5
    enemy_near = values["enemy_distance_norm"] < 0.45
    hazard = values["hazard_visible"] > 0.5
    low_health = values["health_norm"] < 0.3
    damage = values["damage_recent"] > 0.5
    ammo_low = values["ammo_norm"] < 0.12
    if not enemy_visible and not hazard and not damage:
        threat = "none"
        action_window = "explore"
        urgency = "low"
        confidence = 0.9
        source = "env_variable+heuristic"
    elif low_health and (enemy_visible or hazard or damage):
        threat = "critical"
        action_window = "avoid"
        urgency = "high"
        confidence = 0.82
        source = "env_variable+damage_proxy+heuristic"
    elif hazard or damage or (enemy_visible and enemy_near):
        threat = "active"
        action_window = "avoid" if ammo_low or low_health or hazard else "engage"
        urgency = "high" if hazard or damage else "medium"
        confidence = 0.74
        source = "env_variable+reward_proxy+heuristic"
    elif enemy_visible:
        threat = "warning"
        action_window = "engage"
        urgency = "medium"
        confidence = 0.7
        source = "env_variable+heuristic"
    else:
        threat = "none"
        action_window = "wait"
        urgency = "low"
        confidence = 0.62
        source = "heuristic"
    return {
        "ui_phase": "gameplay",
        "threat_level": threat,
        "action_window": action_window,
        "urgency_level": urgency,
        "action_intensity_score": min(1.0, values["motion_intensity"] * 0.6 + float(enemy_visible) * 0.25 + float(hazard) * 0.2),
        "temporal_urgency_score": min(1.0, float(hazard) * 0.5 + float(damage) * 0.35 + (1.0 - values["health_norm"]) * 0.25),
        "information_priority_score": min(1.0, values["visual_clutter"] * 0.5 + float(enemy_visible) * 0.25 + float(hazard) * 0.25),
        "occlusion_risk_score": min(1.0, values["visual_clutter"]),
        "control_continuity_score": min(1.0, values["motion_intensity"]),
        "label_confidence": confidence,
        "label_source": source,
    }


def generate_vizdoom_dataset(mode: str = "small", output_images: bool = True) -> list[dict[str, Any]]:
    ensure_multigame_dirs()
    available, message = vizdoom_available()
    if mode == "fixture":
        frame_count = 500
    elif mode == "small":
        frame_count = 25_000 if not available else 25_000
    else:
        frame_count = 50_000
    if available and mode != "fixture":
        try:
            rows, runtime_message = generate_vizdoom_runtime_dataset(frame_count, output_images=output_images)
            write_table(PROCESSED_DIR / "vizdoom_frames.parquet", rows)
            summary = summarize_rows(rows, "vizdoom_generated")
            summary[0]["vizdoom_runtime_available"] = "True"
            summary[0]["runtime_status"] = runtime_message
            summary[0]["source_type"] = "vizdoom_runtime"
            write_csv(REPORTS_DIR / "vizdoom_dataset_summary.csv", summary)
            plot_label_distribution(rows, FIGURES_DIR / "vizdoom_label_distribution.png", "threat_level")
            (REPORTS_DIR / "vizdoom_status.md").write_text(
                "\n".join(
                    [
                        "# ViZDoom 생성 상태",
                        "",
                        "- vizdoom_runtime_available: True",
                        f"- status: {runtime_message}",
                        f"- rows: {len(rows)}",
                        "- source_type: vizdoom_runtime",
                    ]
                ),
                encoding="utf-8",
            )
            return rows
        except Exception as exc:  # noqa: BLE001
            available = False
            message = f"ViZDoom runtime generation failed; fallback fixture used: {exc}"
    rng = random.Random(26)
    rows: list[dict[str, Any]] = []
    image_every = 50 if frame_count > 1000 else 5
    source_type = "vizdoom_runtime" if available else "procedural_fixture_vizdoom_install_unavailable"
    for idx in range(frame_count):
        scenario = SCENARIOS[idx % len(SCENARIOS)]
        episode_id = idx // 500
        values = scenario_values(scenario, rng)
        labels = weak_labels(values)
        scenario_hash = (SCENARIOS.index(scenario) + 1) / len(SCENARIOS)
        frame_path = ""
        row: dict[str, Any] = {
            "dataset_name": "vizdoom_generated",
            "game_name": "vizdoom",
            "source_type": source_type,
            "episode_id": episode_id,
            "timestep": idx % 500,
            "scenario_name": scenario,
            "action_taken": "move" if labels["action_window"] in {"explore", "wait"} else labels["action_window"],
            "reward": values["reward_norm"],
            "done": (idx + 1) % 500 == 0,
            "health": round(values["health_norm"] * 100, 3),
            "ammo": round(values["ammo_norm"] * 50, 3),
            "enemy_distance_proxy": round(values["enemy_distance_norm"], 5),
            "projectile_hazard_proxy": values["hazard_visible"],
            "damage_taken_proxy": values["damage_recent"],
            "scenario_hash": scenario_hash,
            **values,
            **labels,
        }
        if output_images and idx % image_every == 0:
            frame_path_obj = FRAMES_DIR / "vizdoom" / f"frame_{idx:06d}.ppm"
            write_ppm(frame_path_obj, row)
            frame_path = str(frame_path_obj.relative_to(REPO_ROOT)).replace("\\", "/")
        row["frame_path"] = frame_path
        rows.append(row)
    write_table(PROCESSED_DIR / "vizdoom_frames.parquet", rows)
    summary = summarize_rows(rows, "vizdoom_generated")
    summary[0]["vizdoom_runtime_available"] = str(available)
    summary[0]["runtime_status"] = message
    summary[0]["source_type"] = source_type
    write_csv(REPORTS_DIR / "vizdoom_dataset_summary.csv", summary)
    plot_label_distribution(rows, FIGURES_DIR / "vizdoom_label_distribution.png", "threat_level")
    status_lines = [
        "# ViZDoom 생성 상태",
        "",
        f"- vizdoom_runtime_available: {available}",
        f"- status: {message}",
        f"- rows: {len(rows)}",
        f"- source_type: {source_type}",
        "",
        "ViZDoom 패키지/엔진이 없으면 절차 검증용 fixture만 생성한다. 이 경우 public multi-game 사전학습 결과로 주장하지 않는다.",
    ]
    (REPORTS_DIR / "vizdoom_status.md").write_text("\n".join(status_lines), encoding="utf-8")
    return rows


def summarize_rows(rows: list[dict[str, Any]], dataset_name: str) -> list[dict[str, Any]]:
    if not rows:
        return [{"dataset_name": dataset_name, "available": "False", "rows": 0}]
    return [
        {
            "dataset_name": dataset_name,
            "available": "True",
            "rows": len(rows),
            "games": ";".join(sorted(set(str(row.get("game_name", "")) for row in rows))),
            "scenarios": ";".join(sorted(set(str(row.get("scenario_name", "")) for row in rows))),
            "threat_levels": ";".join(f"{k}:{v}" for k, v in Counter(str(row.get("threat_level")) for row in rows).items()),
            "action_windows": ";".join(f"{k}:{v}" for k, v in Counter(str(row.get("action_window")) for row in rows).items()),
        }
    ]


def dataset_status(dataset_id: str, manual_hint: str, attempted: str) -> list[dict[str, Any]]:
    ensure_multigame_dirs()
    manual_dir = RAW_DIR / dataset_id / "manual"
    manual_dir.mkdir(parents=True, exist_ok=True)
    files = [path for path in manual_dir.rglob("*") if path.is_file()]
    rows = [
        {
            "dataset_name": dataset_id,
            "available": str(bool(files)),
            "raw_file_count": len(files),
            "raw_size_bytes": sum(path.stat().st_size for path in files),
            "manual_dir": str(manual_dir),
            "attempted": attempted,
            "status": "manual_files_detected" if files else "unavailable_or_manual_required",
            "limitation": manual_hint,
        }
    ]
    write_csv(REPORTS_DIR / f"{dataset_id}_summary.csv", rows)
    (REPORTS_DIR / f"{dataset_id}_status.md").write_text(
        "\n".join(
            [
                f"# {dataset_id} status",
                "",
                f"- available: {bool(files)}",
                f"- manual_dir: `{manual_dir}`",
                f"- attempted: {attempted}",
                f"- limitation: {manual_hint}",
            ]
        ),
        encoding="utf-8",
    )
    return rows


def download_atari_head_subset(mode: str = "small") -> list[dict[str, Any]]:
    return dataset_status(
        "atari_head",
        "Full Atari-HEAD is large; place a small public subset manually if needed. Human action/gaze are only abstract context proxies, not Attack/Dodge labels.",
        f"metadata/manual subset mode requested: {mode}; no stable lightweight auto-download bundled",
    )


def download_minerl_subset(mode: str = "minimal") -> list[dict[str, Any]]:
    return dataset_status(
        "minerl",
        "MineRL public data is large and frequently requires the MineRL data tooling; place minimal demonstrations manually.",
        f"minimal/manual subset mode requested: {mode}; full dataset intentionally not downloaded",
    )


def download_dqn_replay_subset(mode: str = "small") -> list[dict[str, Any]]:
    return dataset_status(
        "dqn_replay",
        "DQN replay is massive; small Atari transitions should be manually placed or fetched with a dedicated environment.",
        f"small/manual subset mode requested: {mode}; full replay intentionally not downloaded",
    )


def inspect_multigame_datasets() -> list[dict[str, Any]]:
    ensure_multigame_dirs()
    rows = []
    vizdoom_rows = read_table(PROCESSED_DIR / "vizdoom_frames.parquet")
    viz_summary = read_csv(REPORTS_DIR / "vizdoom_dataset_summary.csv")
    viz_source = viz_summary[0].get("source_type", "") if viz_summary else ""
    viz_limitation = (
        "real ViZDoom runtime weak labels from scenario/env/reward proxies"
        if viz_source == "vizdoom_runtime"
        else "procedural weak labels when ViZDoom runtime is unavailable"
    )
    rows.extend(
        [
            {
                "dataset_name": "vizdoom_generated",
                "available": str(bool(vizdoom_rows)),
                "processed_rows": len(vizdoom_rows),
                "raw_file_count": 0,
                "raw_size_bytes": 0,
                "labels": "ui_phase;threat_level;action_window;urgency_level;interaction_demand",
                "role": "abstract game situation pretraining",
                "limitation": viz_limitation,
            }
        ]
    )
    for dataset_id in ["atari_head", "minerl", "dqn_replay"]:
        manual_dir = RAW_DIR / dataset_id / "manual"
        files = [path for path in manual_dir.rglob("*") if path.is_file()] if manual_dir.exists() else []
        rows.append(
            {
                "dataset_name": dataset_id,
                "available": str(bool(files)),
                "processed_rows": 0,
                "raw_file_count": len(files),
                "raw_size_bytes": sum(path.stat().st_size for path in files),
                "labels": "frame;action;reward/gaze if manually supplied",
                "role": "optional cross-game context diversity",
                "limitation": "manual/large dataset; unavailable unless files are placed locally",
            }
        )
    write_csv(REPORTS_DIR / "multigame_dataset_inventory.csv", rows)
    lines = ["# Multi-game Dataset Availability", ""]
    for row in rows:
        lines.extend(
            [
                f"## {row['dataset_name']}",
                f"- available: {row['available']}",
                f"- processed_rows: {row['processed_rows']}",
                f"- role: {row['role']}",
                f"- limitation: {row['limitation']}",
                "",
            ]
        )
    (REPORTS_DIR / "multigame_dataset_availability.md").write_text("\n".join(lines), encoding="utf-8")
    return rows


def build_multigame_dataset() -> list[dict[str, Any]]:
    ensure_multigame_dirs()
    rows = read_table(PROCESSED_DIR / "vizdoom_frames.parquet")
    if not rows:
        rows = generate_vizdoom_dataset(mode="fixture")
    write_table(PROCESSED_DIR / "multigame_scene_dataset.parquet", rows)
    write_csv(REPORTS_DIR / "multigame_dataset_summary.csv", summarize_rows(rows, "multigame_scene_dataset"))
    return rows


def plot_label_distribution(rows: list[dict[str, Any]], path: Path, field: str) -> None:
    if not rows or plt is None:
        placeholder_figure(path, f"No rows available for {field} distribution.")
        return
    counts = Counter(str(row.get(field, "unknown")) for row in rows)
    plt.figure(figsize=(7, 4))
    plt.bar(list(counts.keys()), list(counts.values()), color="#2563eb")
    plt.ylabel("rows")
    plt.xticks(rotation=15, ha="right")
    plt.tight_layout()
    plt.savefig(path, dpi=150)
    plt.close()


def normalize_feature_vector(row: dict[str, Any]) -> list[float]:
    return [safe_float(row.get(field), 0.0) for field in FEATURE_FIELDS]


def split_rows(rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    if not rows:
        return [], [], []
    heldout_scenarios = {"low_health_enemy_near", "ammo_empty_retreat", "health_gathering_critical", "deadly_corridor_critical"}
    test = [row for row in rows if str(row.get("scenario_name")) in heldout_scenarios]
    if not test:
        scenario_names = sorted(set(str(row.get("scenario_name")) for row in rows))
        heldout = set(scenario_names[-1:])
        test = [row for row in rows if str(row.get("scenario_name")) in heldout]
    remaining = [row for row in rows if row not in test]
    valid = [row for idx, row in enumerate(remaining) if idx % 7 == 0]
    train = [row for idx, row in enumerate(remaining) if idx % 7 != 0]
    return train, valid, test


def centroid_train(rows: list[dict[str, Any]], label_field: str) -> dict[str, list[float]]:
    grouped: dict[str, list[list[float]]] = defaultdict(list)
    for row in rows:
        label = str(row.get(label_field, "unknown"))
        if label and label != "unknown":
            grouped[label].append(normalize_feature_vector(row))
    centroids: dict[str, list[float]] = {}
    for label, vectors in grouped.items():
        centroids[label] = [statistics.mean(vec[i] for vec in vectors) for i in range(len(FEATURE_FIELDS))]
    return centroids


def centroid_predict(row: dict[str, Any], centroids: dict[str, list[float]]) -> tuple[str, float]:
    if not centroids:
        return "unknown", 0.0
    vec = normalize_feature_vector(row)
    distances = []
    for label, centroid in centroids.items():
        distance = math.sqrt(sum((vec[i] - centroid[i]) ** 2 for i in range(len(FEATURE_FIELDS))))
        distances.append((distance, label))
    distances.sort()
    best_distance, best_label = distances[0]
    second_distance = distances[1][0] if len(distances) > 1 else best_distance + 1.0
    confidence = max(0.05, min(0.99, 1.0 - best_distance / (second_distance + 1e-9)))
    return best_label, confidence


def f1_macro(y_true: list[str], y_pred: list[str], labels: list[str]) -> float:
    scores = []
    for label in labels:
        tp = sum(1 for yt, yp in zip(y_true, y_pred) if yt == label and yp == label)
        fp = sum(1 for yt, yp in zip(y_true, y_pred) if yt != label and yp == label)
        fn = sum(1 for yt, yp in zip(y_true, y_pred) if yt == label and yp != label)
        if tp + fp + fn == 0:
            continue
        precision = tp / (tp + fp) if tp + fp else 0.0
        recall = tp / (tp + fn) if tp + fn else 0.0
        scores.append(2 * precision * recall / (precision + recall) if precision + recall else 0.0)
    return sum(scores) / len(scores) if scores else 0.0


def evaluate_predictions(rows: list[dict[str, Any]], label_field: str, pred_field: str, labels: list[str]) -> dict[str, Any]:
    if not rows:
        return {"n": 0, "accuracy": 0.0, "macro_f1": 0.0}
    truth = [str(row.get(label_field, "unknown")) for row in rows]
    pred = [str(row.get(pred_field, "unknown")) for row in rows]
    return {
        "n": len(rows),
        "accuracy": sum(1 for yt, yp in zip(truth, pred) if yt == yp) / len(rows),
        "macro_f1": f1_macro(truth, pred, labels),
    }


def expected_calibration_error(rows: list[dict[str, Any]], label_field: str, pred_field: str, conf_field: str, bins: int = 10) -> float:
    if not rows:
        return 0.0
    total = len(rows)
    ece = 0.0
    for bucket in range(bins):
        lo, hi = bucket / bins, (bucket + 1) / bins
        subset = [
            row
            for row in rows
            if lo <= safe_float(row.get(conf_field), 0.0) < hi or (bucket == bins - 1 and safe_float(row.get(conf_field), 0.0) == 1.0)
        ]
        if not subset:
            continue
        acc = sum(1 for row in subset if str(row.get(label_field)) == str(row.get(pred_field))) / len(subset)
        conf = statistics.mean(safe_float(row.get(conf_field), 0.0) for row in subset)
        ece += (len(subset) / total) * abs(acc - conf)
    return ece


def train_multigame_scene_head() -> dict[str, Any]:
    ensure_multigame_dirs()
    rows = read_table(PROCESSED_DIR / "multigame_scene_dataset.parquet")
    if not rows:
        rows = build_multigame_dataset()
    train, valid, test = split_rows(rows)
    label_specs = {
        "threat_level": THREAT_LEVELS,
        "action_window": ACTION_WINDOWS,
        "urgency_level": URGENCY_LEVELS,
        "ui_phase": UI_PHASES,
    }
    model = {
        "model_type": "centroid_scene_head",
        "backbone": "vizdoom_env_state_features",
        "feature_fields": FEATURE_FIELDS,
        "warning": "This model uses ViZDoom env/state proxy features, not a generic visual foundation model.",
        "heads": {},
    }
    metrics_rows = []
    heldout_rows = []
    train_log = []
    for label_field, labels in label_specs.items():
        centroids = centroid_train(train, label_field)
        model["heads"][label_field] = centroids
        for split_name, split_rows_ in [("train", train), ("valid", valid), ("test_heldout_scenario", test)]:
            evaluated = []
            for row in split_rows_:
                pred, conf = centroid_predict(row, centroids)
                copy = dict(row)
                copy[f"pred_{label_field}"] = pred
                copy[f"conf_{label_field}"] = conf
                evaluated.append(copy)
            metric = evaluate_predictions(evaluated, label_field, f"pred_{label_field}", labels)
            metric.update(
                {
                    "label": label_field,
                    "split": split_name,
                    "confidence_calibration_ECE": expected_calibration_error(
                        evaluated, label_field, f"pred_{label_field}", f"conf_{label_field}"
                    ),
                }
            )
            metrics_rows.append(metric)
            if split_name == "test_heldout_scenario":
                heldout_rows.append({"heldout_group": "scenario", **metric})
        train_log.append({"label": label_field, "train_rows": len(train), "valid_rows": len(valid), "test_rows": len(test), "classes": ";".join(centroids)})
    write_json(MODELS_DIR / "multigame_scene_head.json", model)
    write_json(MODELS_DIR / "centroid_multigame_scene_head.json", model)
    write_csv(REPORTS_DIR / "multigame_scene_metrics.csv", metrics_rows)
    write_csv(REPORTS_DIR / "heldout_game_generalization.csv", heldout_rows)
    write_csv(REPORTS_DIR / "multigame_training_log.csv", train_log)
    plot_confusion(rows=test, model=model, label_field="threat_level", path=FIGURES_DIR / "threat_confusion_matrix.png")
    plot_confusion(rows=test, model=model, label_field="action_window", path=FIGURES_DIR / "action_window_confusion_matrix.png")
    plot_heldout_accuracy(heldout_rows)
    plot_reliability(rows=test, model=model, label_field="threat_level")
    return model


def load_scene_model() -> dict[str, Any]:
    model = read_json(MODELS_DIR / "centroid_multigame_scene_head.json")
    if model is None:
        model = train_multigame_scene_head()
    return model


def predict_scene(row: dict[str, Any], model: dict[str, Any] | None = None) -> dict[str, Any]:
    model = model or load_scene_model()
    output = {}
    confidences = []
    for label_field in ["threat_level", "action_window", "urgency_level", "ui_phase"]:
        pred, conf = centroid_predict(row, model.get("heads", {}).get(label_field, {}))
        output[f"pred_{label_field}"] = pred
        output[f"conf_{label_field}"] = conf
        confidences.append(conf)
    output["scene_confidence"] = statistics.mean(confidences) if confidences else 0.0
    return output


def plot_confusion(rows: list[dict[str, Any]], model: dict[str, Any], label_field: str, path: Path) -> None:
    if not rows or plt is None:
        placeholder_figure(path, f"No rows available for {label_field} confusion matrix.")
        return
    labels = sorted(set(str(row.get(label_field, "unknown")) for row in rows))
    matrix = [[0 for _ in labels] for _ in labels]
    for row in rows:
        pred, _ = centroid_predict(row, model.get("heads", {}).get(label_field, {}))
        true = str(row.get(label_field, "unknown"))
        if true in labels and pred in labels:
            matrix[labels.index(true)][labels.index(pred)] += 1
    plt.figure(figsize=(6, 5))
    plt.imshow(matrix, cmap="Blues")
    plt.xticks(range(len(labels)), labels, rotation=30, ha="right")
    plt.yticks(range(len(labels)), labels)
    plt.xlabel("predicted")
    plt.ylabel("true")
    plt.colorbar()
    plt.tight_layout()
    plt.savefig(path, dpi=150)
    plt.close()


def plot_heldout_accuracy(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "heldout_game_accuracy.png", "No held-out metrics available.")
        return
    plt.figure(figsize=(7, 4))
    plt.bar([str(row["label"]) for row in rows], [safe_float(row.get("accuracy")) for row in rows], color="#059669")
    plt.ylim(0, 1)
    plt.ylabel("held-out scenario accuracy")
    plt.xticks(rotation=20, ha="right")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "heldout_game_accuracy.png", dpi=150)
    plt.close()


def plot_reliability(rows: list[dict[str, Any]], model: dict[str, Any], label_field: str) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "reliability_diagram.png", "No rows available for reliability diagram.")
        return
    evaluated = []
    for row in rows:
        pred, conf = centroid_predict(row, model.get("heads", {}).get(label_field, {}))
        evaluated.append({"correct": str(row.get(label_field)) == pred, "conf": conf})
    bins = []
    for bucket in range(10):
        lo, hi = bucket / 10, (bucket + 1) / 10
        subset = [row for row in evaluated if lo <= row["conf"] < hi or (bucket == 9 and row["conf"] == 1.0)]
        if subset:
            bins.append({"conf": statistics.mean(row["conf"] for row in subset), "acc": sum(row["correct"] for row in subset) / len(subset)})
    plt.figure(figsize=(5, 5))
    plt.plot([0, 1], [0, 1], "--", color="#64748b")
    plt.scatter([row["conf"] for row in bins], [row["acc"] for row in bins], color="#dc2626")
    plt.xlabel("confidence")
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "reliability_diagram.png", dpi=150)
    plt.close()


def prior_from_abstract(
    threat_level: str,
    action_window: str,
    confidence: float,
    confidence_threshold: float = 0.35,
) -> dict[str, Any]:
    threat = str(threat_level or "unknown")
    action = str(action_window or "unknown")
    if confidence < confidence_threshold:
        base_attack, base_dodge, reason = 0.5, 0.5, "low_confidence_neutral"
    elif threat == "critical":
        base_attack, base_dodge, reason = 0.05, 0.95, "critical_threat"
    elif action == "avoid" or threat == "active":
        base_attack, base_dodge, reason = 0.15, 0.85, "avoid_or_active_threat"
    elif action == "engage" and threat in {"none", "warning"}:
        base_attack, base_dodge, reason = (0.85, 0.15, "safe_like_engage") if threat == "none" else (0.65, 0.35, "warning_but_engage")
    else:
        base_attack, base_dodge, reason = 0.5, 0.5, "unknown_or_wait_neutral"
    c = max(0.0, min(1.0, confidence))
    attack = c * base_attack + (1.0 - c) * 0.5
    dodge = c * base_dodge + (1.0 - c) * 0.5
    total = attack + dodge
    return {
        "prior_attack": attack / total,
        "prior_dodge": dodge / total,
        "base_prior_attack": base_attack,
        "base_prior_dodge": base_dodge,
        "confidence": c,
        "reason": reason,
    }


def export_prior_config() -> dict[str, Any]:
    ensure_multigame_dirs()
    config = {
        "source": "public_multigame_abstract_scene_prior",
        "model_path": "analysis_multigame/outputs/models/centroid_multigame_scene_head.json",
        "label_mapping": {
            "ui_phase": UI_PHASES,
            "threat_level": THREAT_LEVELS,
            "action_window": ACTION_WINDOWS,
            "urgency_level": URGENCY_LEVELS,
        },
        "prior_table": {
            "safe_like_engage": {"attack": 0.85, "dodge": 0.15},
            "warning_but_engage": {"attack": 0.65, "dodge": 0.35},
            "avoid_or_active_threat": {"attack": 0.15, "dodge": 0.85},
            "critical_threat": {"attack": 0.05, "dodge": 0.95},
            "unknown_or_low_confidence": {"attack": 0.5, "dodge": 0.5},
        },
        "confidence_threshold": 0.35,
        "temperature": 1.0,
        "confidence_mixing_formula": "P_final(a)=c*P_rule(a|abstract_state)+(1-c)*P_neutral(a)",
        "source_datasets": ["vizdoom_generated_or_fixture", "atari_head_manual_if_available", "minerl_manual_if_available", "dqn_replay_manual_if_available"],
        "warnings": [
            "The scene model predicts abstract state, not Attack/Dodge directly.",
            "Weak labels are not human intention labels.",
            "If source_type is procedural_fixture_vizdoom_install_unavailable, results are pipeline tests and not public ViZDoom evidence.",
            "Low confidence must fall back to neutral prior and clear input preservation.",
        ],
    }
    write_json(MODELS_DIR / "multigame_vision_prior_config.json", config)
    streaming = REPO_ROOT / "Assets" / "StreamingAssets" / "ADUI" / "multigame_vision_prior_config.json"
    write_json(streaming, config)
    return config


def train_clip_zero_shot_baseline() -> dict[str, Any]:
    ensure_multigame_dirs()
    rows = read_table(PROCESSED_DIR / "multigame_scene_dataset.parquet")
    if not rows:
        rows = build_multigame_dataset()
    results = {
        "status": "clip_weights_unavailable_or_not_requested",
        "baseline": "rule_prompt_proxy",
        "warning": "This is not a real CLIP result unless CLIP weights are installed and this script is extended.",
        "rows": len(rows),
    }
    write_json(MODELS_DIR / "clip_zero_shot_results.json", results)
    write_csv(REPORTS_DIR / "clip_zero_shot_results.csv", [results])
    return results


def train_small_cnn_baseline() -> dict[str, Any]:
    ensure_multigame_dirs()
    result = {
        "status": "not_trained_in_lightweight_environment",
        "warning": "Small CNN runtime candidate is scaffolded; use real frame tensors before reporting.",
    }
    write_json(MODELS_DIR / "small_cnn_scene_head.pt", result)
    return result


def evaluate_scene_recognition() -> list[dict[str, Any]]:
    if not (REPORTS_DIR / "multigame_scene_metrics.csv").exists():
        train_multigame_scene_head()
    return read_csv(REPORTS_DIR / "multigame_scene_metrics.csv")


def evaluate_cross_game_generalization() -> list[dict[str, Any]]:
    if not (REPORTS_DIR / "heldout_game_generalization.csv").exists():
        train_multigame_scene_head()
    return read_csv(REPORTS_DIR / "heldout_game_generalization.csv")


def evaluate_unity_transfer() -> list[dict[str, Any]]:
    from analysis_unity.src.evaluate_multigame_vision_prior import main as unity_main

    unity_main()
    report = REPO_ROOT / "analysis_unity" / "outputs" / "reports" / "multigame_vision_prior_bayesian_metrics.csv"
    return read_csv(report)


def evaluate_leakage() -> list[dict[str, Any]]:
    rows = [
        {
            "check": "unity_only_head",
            "status": "not_trained",
            "finding_ko": "현재 환경에서는 Unity 전용 비전 head를 학습하지 않았으므로 과적합 성능을 주장하지 않는다.",
        }
    ]
    write_csv(REPORTS_DIR / "multigame_leakage_checks.csv", rows)
    return rows


def report_builder() -> None:
    ensure_multigame_dirs()
    inventory = inspect_multigame_datasets()
    metrics = evaluate_scene_recognition()
    heldout = evaluate_cross_game_generalization()
    export_prior_config()
    viz_summary = read_csv(REPORTS_DIR / "vizdoom_dataset_summary.csv")
    lines = [
        "# Public Multi-game 상황 인식 사전학습 요약",
        "",
        "## 1. Unity-only classifier를 메인으로 쓰지 않는 이유",
        "Unity 화면만으로 학습한 classifier는 Unity 색상, 텍스트, 적 모델, UI 위치, 카메라 스타일에 과적합될 수 있다. 따라서 메인 방향은 public multi-game 표현 학습이며 Unity는 최종 testbed와 few-shot 보정에만 사용한다.",
        "",
        "## 2. 데이터 확보/생성 상태",
    ]
    for row in inventory:
        lines.append(
            f"- {row.get('dataset_name')}: available={row.get('available')}, rows={row.get('processed_rows')}, "
            f"role={row.get('role')}, limitation={row.get('limitation')}"
        )
    lines.extend(["", "## 3. ViZDoom 상태"])
    for row in viz_summary:
        lines.append(
            f"- rows={row.get('rows')}, source_type={row.get('source_type')}, "
            f"vizdoom_runtime_available={row.get('vizdoom_runtime_available')}, status={row.get('runtime_status')}"
        )
    lines.extend(["", "## 4. Abstract labels", "- ui_phase, threat_level, action_window, urgency_level, interaction_demand score를 예측한다. Attack/Dodge를 직접 예측하지 않는다.", "", "## 5. Scene head metrics"])
    for row in metrics:
        lines.append(
            f"- {row.get('label')} / {row.get('split')}: n={row.get('n')}, accuracy={row.get('accuracy')}, "
            f"macro_f1={row.get('macro_f1')}, ECE={row.get('confidence_calibration_ECE')}"
        )
    lines.extend(["", "## 6. Held-out generalization"])
    for row in heldout:
        lines.append(f"- {row.get('label')}: heldout={row.get('heldout_group')}, accuracy={row.get('accuracy')}, macro_f1={row.get('macro_f1')}")
    lines.extend(
        [
            "",
            "## 7. Prior builder",
            "추상 상황 출력은 confidence mixing으로 Attack/Dodge prior에만 반영된다. 최종 결정은 기존 Bayesian decoder와 safety gate가 수행한다.",
            "",
            "## 8. 한계",
            "- weak label은 사용자 의도 ground truth가 아니다.",
            "- ViZDoom 런타임이 없으면 생성된 자료는 절차 검증 fixture이며 public evidence로 주장하지 않는다.",
            "- Atari-HEAD, MineRL, DQN replay는 대용량/수동 배치가 필요할 수 있다.",
            "- DINO 계열 feature extractor는 active pipeline에서 제거했다. 상황 인식은 현재 ViZDoom state/reward weak label 기반으로만 보고한다.",
        ]
    )
    (REPORTS_DIR / "final_multigame_scene_pretraining_summary_ko.md").write_text("\n".join(lines), encoding="utf-8")


def parse_mode(default: str = "small") -> str:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", default=default)
    parser.add_argument("--no-images", action="store_true")
    args = parser.parse_args()
    return args.mode

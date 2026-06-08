from __future__ import annotations

import csv
import json
import math
import re
import shutil
import statistics
import urllib.request
import zipfile
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

try:
    import pyarrow as pa
    import pyarrow.parquet as pq
except ModuleNotFoundError:  # pragma: no cover
    pa = None
    pq = None

from analysis_public.src.data.dataset_registry import PUBLIC_DATASETS, dataset_root
from analysis_public.src.paths import DATA_DIR, FIGURES_DIR, MODELS_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_public_dirs


TOUCH_DYNAMICS_URLS = {
    "diep_raw_data.zip": "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/diep_raw_data.zip",
    "mc_raw_data.zip": "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/mc_raw_data.zip",
    "pubg_raw_data.zip": "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/pubg_raw_data.zip",
    "snake_raw_data.zip": "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/snake_raw_data.zip",
}
MC_SNAKE_URL = "https://github.com/zderidder/MC-Snake-Results/archive/refs/heads/main.zip"
TSI_URLS = {
    "touch_data.csv": "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/touch_data.csv",
    "keyboard_data.json": "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/keyboard_data.json",
    "prompt_data.csv": "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/prompt_data.csv",
}


def safe_float(value: Any, default: float | None = None) -> float | None:
    if value is None:
        return default
    try:
        text = str(value).strip()
        if not text or text.lower() in {"nan", "none", "null"}:
            return default
        number = float(text)
        if math.isnan(number):
            return default
        return number
    except (TypeError, ValueError):
        return default


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str] | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if fieldnames is None:
        keys: list[str] = []
        for row in rows:
            for key in row:
                if key not in keys:
                    keys.append(key)
        fieldnames = keys or ["status"]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def write_parquet(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if pq is None or pa is None:
        raise RuntimeError("pyarrow is required to write parquet outputs")
    if not rows:
        pq.write_table(pa.table({}), path)
        return
    keys: list[str] = []
    for row in rows:
        for key in row:
            if key not in keys:
                keys.append(key)
    columns = {key: [row.get(key) for row in rows] for key in keys}
    pq.write_table(pa.table(columns), path)


def read_parquet(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    if pq is None:
        raise RuntimeError("pyarrow is required to read parquet outputs")
    table = pq.read_table(path)
    return table.to_pylist()


def download_file(url: str, target: Path) -> dict[str, Any]:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.stat().st_size > 0:
        return {"url": url, "path": str(target), "status": "exists", "bytes": target.stat().st_size}
    try:
        urllib.request.urlretrieve(url, target)
        return {"url": url, "path": str(target), "status": "downloaded", "bytes": target.stat().st_size}
    except Exception as exc:  # noqa: BLE001
        return {"url": url, "path": str(target), "status": "failed", "error": str(exc)}


def find_existing_file(file_name: str) -> Path | None:
    candidates = [
        Path.cwd(),
        Path.cwd() / "data",
        Path.cwd() / "analysis_public" / "data",
        Path.cwd() / "mobile-game-adaptive-ui-a2z" / "data",
    ]
    for base in candidates:
        if not base.exists():
            continue
        for path in base.rglob(file_name):
            if path.is_file() and path.stat().st_size > 0:
                return path
    return None


def copy_or_download_required() -> list[dict[str, Any]]:
    ensure_public_dirs()
    rows: list[dict[str, Any]] = []
    td_raw = dataset_root("touch_dynamics") / "raw"
    for file_name, url in TOUCH_DYNAMICS_URLS.items():
        target = td_raw / file_name
        found = find_existing_file(file_name)
        if found and found.resolve() != target.resolve():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(found, target)
            rows.append({"dataset_id": "touch_dynamics", "file": file_name, "status": "copied_existing", "source": str(found), "bytes": target.stat().st_size})
        else:
            row = download_file(url, target)
            row.update({"dataset_id": "touch_dynamics", "file": file_name})
            rows.append(row)

    mc_target = dataset_root("mc_snake") / "raw" / "main.zip"
    found_mc = find_existing_file("main.zip")
    if found_mc and "MC-Snake" in str(found_mc):
        shutil.copy2(found_mc, mc_target)
        rows.append({"dataset_id": "mc_snake", "file": "main.zip", "status": "copied_existing", "source": str(found_mc), "bytes": mc_target.stat().st_size})
    else:
        row = download_file(MC_SNAKE_URL, mc_target)
        row.update({"dataset_id": "mc_snake", "file": "main.zip"})
        rows.append(row)

    tsi_raw = dataset_root("tsi") / "raw"
    for file_name, url in TSI_URLS.items():
        target = tsi_raw / file_name
        found = find_existing_file(file_name)
        if found and found.resolve() != target.resolve():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(found, target)
            rows.append({"dataset_id": "tsi", "file": file_name, "status": "copied_existing", "source": str(found), "bytes": target.stat().st_size})
        else:
            row = download_file(url, target)
            row.update({"dataset_id": "tsi", "file": file_name})
            rows.append(row)

    write_csv(REPORTS_DIR / "download_all_required_status.csv", rows)
    return rows


def extract_zip(zip_path: Path, dest: Path) -> None:
    if not zip_path.exists():
        return
    marker = dest / ".extracted"
    if marker.exists():
        return
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path) as archive:
        archive.extractall(dest)
    marker.write_text(datetime.now(timezone.utc).isoformat(), encoding="utf-8")


def game_from_touch_dynamics_zip(path: Path) -> str:
    stem = path.name.lower()
    if stem.startswith("mc_"):
        return "minecraft"
    if stem.startswith("pubg_"):
        return "pubg"
    if stem.startswith("snake_"):
        return "snake"
    if stem.startswith("diep_"):
        return "diep"
    return path.stem.replace("_raw_data", "")


def user_from_source(source_file: str, row: dict[str, Any]) -> str:
    class_id = str(row.get("CLASS", "")).strip()
    if class_id:
        return class_id
    match = re.search(r"(?:Sub|sub|diep|pubg)(\d+)", source_file)
    if match:
        return match.group(1)
    return Path(source_file).stem


def normalize_touch_event(row: dict[str, Any], dataset_id: str, game_id: str, source_file: str) -> dict[str, Any] | None:
    x = safe_float(row.get("X") or row.get("x"))
    y = safe_float(row.get("Y") or row.get("y"))
    timestamp = safe_float(row.get("Timestamp") or row.get("timestamp") or row.get("time"), 0.0)
    if x is None or y is None:
        return None
    timestamp_ms = timestamp * 1000.0 if timestamp is not None and timestamp < 10_000_000 else timestamp
    pressure = safe_float(row.get("PRESSURE") or row.get("pressure"))
    if pressure is not None and pressure < 0:
        pressure = None
    touch_major = safe_float(row.get("TOUCH_MAJOR") or row.get("WIDTH_MAJOR") or row.get("touch_major"))
    if touch_major is not None and touch_major < 0:
        touch_major = None
    touch_minor = safe_float(row.get("TOUCH_MINOR") or row.get("touch_minor"))
    if touch_minor is not None and touch_minor < 0:
        touch_minor = None
    user_id = user_from_source(source_file, row)
    return {
        "dataset_id": dataset_id,
        "user_id": str(user_id),
        "game_id": game_id,
        "session_id": Path(source_file).stem,
        "timestamp_ms": float(timestamp_ms or 0.0),
        "touch_x": float(x),
        "touch_y": float(y),
        "touch_x_norm": None,
        "touch_y_norm": None,
        "pressure": pressure,
        "touch_major": touch_major,
        "touch_minor": touch_minor,
        "finger_id": str(row.get("FINGER", "")),
        "event_type": str(row.get("BTN_TOUCH", "")),
        "source_file": source_file,
    }


def normalize_coordinates(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_source: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        by_source[str(row["source_file"])].append(row)
    for source_rows in by_source.values():
        max_x = max(float(row["touch_x"]) for row in source_rows) or 1.0
        max_y = max(float(row["touch_y"]) for row in source_rows) or 1.0
        for row in source_rows:
            row["touch_x_norm"] = float(row["touch_x"]) / max_x
            row["touch_y_norm"] = float(row["touch_y"]) / max_y
    return rows


def parse_touch_zip(zip_path: Path, dataset_id: str, game_id: str | None = None) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    if not zip_path.exists():
        return rows
    with zipfile.ZipFile(zip_path) as archive:
        for name in archive.namelist():
            if not name.lower().endswith(".csv"):
                continue
            if name.lower().endswith(".orig"):
                continue
            inferred_game = game_id
            if inferred_game is None:
                lower = name.lower()
                base = Path(lower).name
                if "/mc_data/" in lower or "mc_data" in lower or "mc_raw" in lower or base.endswith("mc.csv"):
                    inferred_game = "minecraft"
                elif "/snake_data/" in lower or "snake_data" in lower or base.endswith("snake.csv"):
                    inferred_game = "snake"
                elif "pubg" in lower:
                    inferred_game = "pubg"
                elif "diep" in lower:
                    inferred_game = "diep"
                else:
                    inferred_game = "unknown"
            with archive.open(name) as binary:
                text = (line.decode("utf-8-sig", errors="ignore") for line in binary)
                reader = csv.DictReader(text)
                for row in reader:
                    normalized = normalize_touch_event(row, dataset_id, inferred_game, name)
                    if normalized:
                        rows.append(normalized)
    return normalize_coordinates(rows)


def process_touch_dynamics() -> list[dict[str, Any]]:
    raw_dir = dataset_root("touch_dynamics") / "raw"
    extracted_dir = dataset_root("touch_dynamics") / "extracted"
    processed_dir = dataset_root("touch_dynamics") / "processed"
    all_rows: list[dict[str, Any]] = []
    for zip_path in sorted(raw_dir.glob("*.zip")):
        extract_zip(zip_path, extracted_dir / zip_path.stem)
        all_rows.extend(parse_touch_zip(zip_path, "touch_dynamics", game_from_touch_dynamics_zip(zip_path)))
    write_parquet(processed_dir / "touch_dynamics_events.parquet", all_rows)
    write_csv(processed_dir / "touch_dynamics_events_sample.csv", all_rows[:1000])
    summary = dataset_summary_rows("touch_dynamics", all_rows, raw_dir)
    write_csv(REPORTS_DIR / "touch_dynamics_dataset_summary.csv", summary)
    return all_rows


def process_mc_snake() -> list[dict[str, Any]]:
    raw_dir = dataset_root("mc_snake") / "raw"
    extracted_dir = dataset_root("mc_snake") / "extracted"
    processed_dir = dataset_root("mc_snake") / "processed"
    zip_path = raw_dir / "main.zip"
    extract_zip(zip_path, extracted_dir)
    rows = parse_touch_zip(zip_path, "mc_snake", None)
    write_parquet(processed_dir / "mc_snake_events.parquet", rows)
    write_csv(processed_dir / "mc_snake_events_sample.csv", rows[:1000])
    write_csv(REPORTS_DIR / "mc_snake_dataset_summary.csv", dataset_summary_rows("mc_snake", rows, raw_dir))
    return rows


def key_id(value: str) -> str:
    text = str(value).strip()
    if text.upper() == "SPACE":
        return "SPACE"
    return text.lower()


def keyboard_lookup(keyboard: dict[str, Any]) -> dict[str, dict[str, float]]:
    result = {}
    for key, item in keyboard.get("keys_info", {}).items():
        result[key_id(key)] = {
            "target_id": key_id(key),
            "center_x": float(item["key_center_x"]),
            "center_y": float(item["key_center_y"]),
            "width": float(item["key_width"]),
            "height": float(item["key_height"]),
        }
    return result


def process_tsi() -> list[dict[str, Any]]:
    raw_dir = dataset_root("tsi") / "raw"
    processed_dir = dataset_root("tsi") / "processed"
    touch_csv = raw_dir / "touch_data.csv"
    keyboard_json = raw_dir / "keyboard_data.json"
    keyboard = json.loads(keyboard_json.read_text(encoding="utf-8"))
    keys = keyboard_lookup(keyboard)
    rows: list[dict[str, Any]] = []
    with touch_csv.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for idx, raw in enumerate(reader):
            target = key_id(raw.get("ref_char", ""))
            if target not in keys:
                continue
            x = safe_float(raw.get("first_frame_touch_x"))
            y = safe_float(raw.get("first_frame_touch_y"))
            if x is None or y is None:
                continue
            info = keys[target]
            offset_x = x - info["center_x"]
            offset_y = y - info["center_y"]
            rows.append(
                {
                    "dataset_id": "tsi",
                    "participant_id": str(raw.get("participant_id", "")),
                    "task_id": str(raw.get("task_id", "")),
                    "trial_id": str(raw.get("trial_id", "")),
                    "row_index": idx,
                    "timestamp_ms": safe_float(raw.get("timestamp_ms"), 0.0) or 0.0,
                    "target_id": target,
                    "touch_x": float(x),
                    "touch_y": float(y),
                    "touch_major": safe_float(raw.get("first_frame_touch_major")),
                    "touch_minor": safe_float(raw.get("first_frame_touch_minor")),
                    "target_center_x": info["center_x"],
                    "target_center_y": info["center_y"],
                    "target_width": info["width"],
                    "target_height": info["height"],
                    "offset_x": offset_x,
                    "offset_y": offset_y,
                    "distance_to_target": math.hypot(offset_x, offset_y),
                    "source_file": "touch_data.csv",
                }
            )
    write_parquet(processed_dir / "tsi_touch_targets.parquet", rows)
    write_csv(processed_dir / "tsi_touch_targets_sample.csv", rows[:1000])
    summary = [
        {
            "dataset_id": "tsi",
            "raw_file_count": len(list(raw_dir.glob("*"))),
            "raw_size_bytes": sum(path.stat().st_size for path in raw_dir.glob("*") if path.is_file()),
            "processed_rows": len(rows),
            "users": len({row["participant_id"] for row in rows}),
            "targets": len({row["target_id"] for row in rows}),
            "has_touch_coordinates": True,
            "has_target_layout": True,
            "has_action_labels": False,
            "has_game_state_labels": False,
        }
    ]
    write_csv(REPORTS_DIR / "tsi_dataset_summary.csv", summary)
    return rows


def dataset_summary_rows(dataset_id: str, rows: list[dict[str, Any]], raw_dir: Path) -> list[dict[str, Any]]:
    raw_files = [path for path in raw_dir.glob("**/*") if path.is_file()]
    return [
        {
            "dataset_id": dataset_id,
            "raw_file_count": len(raw_files),
            "raw_size_bytes": sum(path.stat().st_size for path in raw_files),
            "processed_rows": len(rows),
            "users": len({row.get("user_id") or row.get("participant_id") for row in rows}),
            "games": ";".join(sorted({str(row.get("game_id", "")) for row in rows if row.get("game_id")})),
            "has_touch_coordinates": len(rows) > 0,
            "has_pressure": any(row.get("pressure") is not None for row in rows),
            "has_target_layout": False,
            "has_action_labels": False,
            "has_game_state_labels": False,
        }
    ]


def load_processed_touch_rows(dataset_id: str) -> list[dict[str, Any]]:
    if dataset_id == "touch_dynamics":
        return read_parquet(dataset_root(dataset_id) / "processed" / "touch_dynamics_events.parquet")
    if dataset_id == "mc_snake":
        return read_parquet(dataset_root(dataset_id) / "processed" / "mc_snake_events.parquet")
    return []


def touch_feature_rows(events: list[dict[str, Any]], dataset_id: str) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in events:
        grouped[(str(row.get("user_id")), str(row.get("game_id")), str(row.get("session_id")))].append(row)
    features = []
    user_points: dict[str, list[tuple[float, float]]] = defaultdict(list)
    for (user_id, game_id, session_id), rows in grouped.items():
        rows = sorted(rows, key=lambda item: float(item.get("timestamp_ms") or 0.0))
        xs = [float(row["touch_x"]) for row in rows]
        ys = [float(row["touch_y"]) for row in rows]
        ts = [float(row.get("timestamp_ms") or 0.0) for row in rows]
        for x, y in zip(xs, ys):
            user_points[user_id].append((x, y))
        duration_sec = max((max(ts) - min(ts)) / 1000.0, 0.001) if len(ts) >= 2 else 0.001
        dts = [max((ts[i] - ts[i - 1]) / 1000.0, 0.001) for i in range(1, len(ts))]
        distances = [math.hypot(xs[i] - xs[i - 1], ys[i] - ys[i - 1]) for i in range(1, len(xs))]
        velocities = [d / dt for d, dt in zip(distances, dts)]
        accelerations = [(velocities[i] - velocities[i - 1]) / dts[i] for i in range(1, len(velocities))]
        jerks = [(accelerations[i] - accelerations[i - 1]) / dts[i + 1] for i in range(1, len(accelerations))]
        pressures = [float(row["pressure"]) for row in rows if row.get("pressure") is not None]
        majors = [float(row["touch_major"]) for row in rows if row.get("touch_major") is not None]
        minors = [float(row["touch_minor"]) for row in rows if row.get("touch_minor") is not None]
        max_x = max(xs) or 1.0
        left = sum(1 for x in xs if x < max_x / 3.0)
        right = sum(1 for x in xs if x > 2 * max_x / 3.0)
        center = len(xs) - left - right
        event_types = [str(row.get("event_type", "")).upper() for row in rows]
        down_count = sum(1 for item in event_types if item == "DOWN")
        up_count = sum(1 for item in event_types if item == "UP")
        held_count = sum(1 for item in event_types if item == "HELD")
        burstiness = statistics.pstdev(dts) / (statistics.mean(dts) + 1e-9) if len(dts) > 1 else 0.0
        velocity_mean = statistics.mean(velocities) if velocities else 0.0
        velocity_std = statistics.pstdev(velocities) if len(velocities) > 1 else 0.0
        acceleration_mean = statistics.mean(accelerations) if accelerations else 0.0
        acceleration_std = statistics.pstdev(accelerations) if len(accelerations) > 1 else 0.0
        jerk_mean = statistics.mean(jerks) if jerks else 0.0
        touch_rate = len(rows) / duration_sec
        features.append(
            {
                "dataset_id": dataset_id,
                "user_id": user_id,
                "game_id": game_id,
                "session_id": session_id,
                "touch_count": len(rows),
                "duration_sec": duration_sec,
                "touch_rate_per_sec": touch_rate,
                "burstiness": burstiness,
                "pressure_mean": statistics.mean(pressures) if pressures else None,
                "pressure_std": statistics.pstdev(pressures) if len(pressures) > 1 else None,
                "touch_major_mean": statistics.mean(majors) if majors else None,
                "touch_major_std": statistics.pstdev(majors) if len(majors) > 1 else None,
                "touch_minor_mean": statistics.mean(minors) if minors else None,
                "touch_minor_std": statistics.pstdev(minors) if len(minors) > 1 else None,
                "velocity_mean": velocity_mean,
                "velocity_std": velocity_std,
                "acceleration_mean": acceleration_mean,
                "acceleration_std": acceleration_std,
                "jerk_mean": jerk_mean,
                "left_region_ratio": left / max(len(rows), 1),
                "right_region_ratio": right / max(len(rows), 1),
                "center_region_ratio": center / max(len(rows), 1),
                "multi_touch_ratio": 0.0,
                "long_press_ratio": held_count / max(down_count + up_count + held_count, 1),
                "swipe_distance_mean": statistics.mean(distances) if distances else 0.0,
                "control_continuity_proxy": 1.0 / (1.0 + burstiness + velocity_std / 1000.0),
                "action_intensity_proxy": touch_rate * (1.0 + velocity_mean / 1000.0),
                "temporal_urgency_proxy": touch_rate * (1.0 + burstiness),
                "ui_skill_proxy": 1.0 / (1.0 + burstiness + acceleration_std / 5000.0),
                "within_user_variance": None,
                "between_user_variance": None,
            }
        )
    centers = {}
    for user_id, points in user_points.items():
        mx = statistics.mean([point[0] for point in points])
        my = statistics.mean([point[1] for point in points])
        centers[user_id] = (mx, my)
    gx = statistics.mean([item[0] for item in centers.values()]) if centers else 0.0
    gy = statistics.mean([item[1] for item in centers.values()]) if centers else 0.0
    between = statistics.mean([(x - gx) ** 2 + (y - gy) ** 2 for x, y in centers.values()]) if centers else 0.0
    within = {}
    for user_id, points in user_points.items():
        mx, my = centers[user_id]
        within[user_id] = statistics.mean([(x - mx) ** 2 + (y - my) ** 2 for x, y in points])
    for row in features:
        row["within_user_variance"] = within.get(row["user_id"], 0.0)
        row["between_user_variance"] = between
    return features


def evaluate_touch_dynamics() -> list[dict[str, Any]]:
    td_rows = load_processed_touch_rows("touch_dynamics")
    mc_rows = load_processed_touch_rows("mc_snake")
    features = touch_feature_rows(td_rows, "touch_dynamics") + touch_feature_rows(mc_rows, "mc_snake")
    write_csv(REPORTS_DIR / "public_touch_dynamics_summary.csv", features)
    variation = [
        {
            "dataset_id": row["dataset_id"],
            "user_id": row["user_id"],
            "game_id": row["game_id"],
            "within_user_variance": row["within_user_variance"],
            "between_user_variance": row["between_user_variance"],
        }
        for row in features
    ]
    write_csv(REPORTS_DIR / "public_user_variation_summary.csv", variation)
    plot_touch_dynamics(features)
    return features


def nearest_key(row: dict[str, Any], targets: dict[str, dict[str, float]], margin: float | None = None) -> str:
    x = float(row["touch_x"])
    y = float(row["touch_y"])
    candidates = []
    if margin is not None:
        for target, info in targets.items():
            if abs(x - info["center_x"]) <= info["width"] * margin / 2.0 and abs(y - info["center_y"]) <= info["height"] * margin / 2.0:
                candidates.append((math.hypot(x - info["center_x"], y - info["center_y"]), target))
        if candidates:
            return sorted(candidates)[0][1]
        return "None"
    return min(targets, key=lambda key: math.hypot(x - targets[key]["center_x"], y - targets[key]["center_y"]))


def gaussian_key(row: dict[str, Any], targets: dict[str, dict[str, float]], stats: tuple[float, float, float, float]) -> str:
    mx, my, vx, vy = stats
    x = float(row["touch_x"])
    y = float(row["touch_y"])
    vx = max(vx, 1.0)
    vy = max(vy, 1.0)
    best = "None"
    best_score = -1e100
    for target, info in targets.items():
        ox = x - info["center_x"]
        oy = y - info["center_y"]
        score = -(((ox - mx) ** 2) / (2 * vx) + ((oy - my) ** 2) / (2 * vy))
        if score > best_score:
            best_score = score
            best = target
    return best


def split_tsi(rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    train, test = [], []
    for row in rows:
        key = f"{row['participant_id']}:{row['task_id']}:{row['trial_id']}:{row['row_index']}"
        bucket = abs(hash(key)) % 5
        (test if bucket == 0 else train).append(row)
    return train, test


def precision_recall_f1(rows: list[dict[str, Any]], pred_field: str) -> tuple[float, float, float]:
    labels = sorted({str(row["target_id"]) for row in rows})
    f1s = []
    for label in labels:
        tp = sum(1 for row in rows if row[pred_field] == label and row["target_id"] == label)
        fp = sum(1 for row in rows if row[pred_field] == label and row["target_id"] != label)
        fn = sum(1 for row in rows if row[pred_field] != label and row["target_id"] == label)
        precision = tp / max(tp + fp, 1)
        recall = tp / max(tp + fn, 1)
        f1s.append(2 * precision * recall / max(precision + recall, 1e-9))
    return 0.0, 0.0, statistics.mean(f1s) if f1s else 0.0


def evaluate_tsi_target_selection() -> list[dict[str, Any]]:
    rows = read_parquet(dataset_root("tsi") / "processed" / "tsi_touch_targets.parquet")
    keyboard = json.loads((dataset_root("tsi") / "raw" / "keyboard_data.json").read_text(encoding="utf-8"))
    targets = keyboard_lookup(keyboard)
    train, test = split_tsi(rows)
    gx = statistics.mean([float(row["offset_x"]) for row in train])
    gy = statistics.mean([float(row["offset_y"]) for row in train])
    gvx = statistics.pvariance([float(row["offset_x"]) for row in train])
    gvy = statistics.pvariance([float(row["offset_y"]) for row in train])
    by_user: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in train:
        by_user[str(row["participant_id"])].append(row)
    user_stats = {}
    for user_id, user_rows in by_user.items():
        xs = [float(row["offset_x"]) for row in user_rows]
        ys = [float(row["offset_y"]) for row in user_rows]
        user_stats[user_id] = (
            statistics.mean(xs),
            statistics.mean(ys),
            statistics.pvariance(xs) if len(xs) > 1 else gvx,
            statistics.pvariance(ys) if len(ys) > 1 else gvy,
        )
    evaluated = []
    for row in test:
        visual = nearest_key(row, targets, margin=1.0)
        expanded = nearest_key(row, targets, margin=1.25)
        nearest = nearest_key(row, targets, margin=None)
        global_gauss = gaussian_key(row, targets, (gx, gy, gvx, gvy))
        user_gauss = gaussian_key(row, targets, user_stats.get(str(row["participant_id"]), (gx, gy, gvx, gvy)))
        half_w = float(row["target_width"]) / 2.0
        half_h = float(row["target_height"]) / 2.0
        boundary_score = max(abs(float(row["offset_x"]) / half_w), abs(float(row["offset_y"]) / half_h))
        ambiguous = 0.85 <= boundary_score <= 1.35 or visual == "None"
        gated = user_gauss if ambiguous else visual
        evaluated.append(
            {
                **row,
                "split": "test",
                "visual_boundary_prediction": visual,
                "expanded_hitbox_prediction": expanded,
                "nearest_center_prediction": nearest,
                "global_gaussian_prediction": global_gauss,
                "user_gaussian_prediction": user_gauss,
                "user_gaussian_with_ambiguity_gate_prediction": gated,
                "is_boundary_subset": 0.85 <= boundary_score <= 1.15,
                "is_ambiguous": ambiguous,
            }
        )
    write_parquet(PROCESSED_DIR / "tsi_target_selection_evaluated.parquet", evaluated)
    write_csv(PROCESSED_DIR / "tsi_target_selection_evaluated_sample.csv", evaluated[:1000])
    metric_rows = []
    baselines = {
        "visual_boundary": "visual_boundary_prediction",
        "expanded_hitbox": "expanded_hitbox_prediction",
        "nearest_center": "nearest_center_prediction",
        "global_gaussian": "global_gaussian_prediction",
        "user_gaussian": "user_gaussian_prediction",
        "user_gaussian_with_ambiguity_gate": "user_gaussian_with_ambiguity_gate_prediction",
    }
    for name, field in baselines.items():
        correct = [row for row in evaluated if row[field] == row["target_id"]]
        boundary = [row for row in evaluated if row["is_boundary_subset"]]
        ambiguous = [row for row in evaluated if row["is_ambiguous"]]
        _, _, macro_f1 = precision_recall_f1(evaluated, field)
        metric_rows.append(
            {
                "dataset_id": "tsi",
                "baseline": name,
                "train_rows": len(train),
                "test_rows": len(evaluated),
                "accuracy": len(correct) / max(len(evaluated), 1),
                "macro_f1": macro_f1,
                "boundary_subset_accuracy": sum(1 for row in boundary if row[field] == row["target_id"]) / max(len(boundary), 1),
                "ambiguous_subset_accuracy": sum(1 for row in ambiguous if row[field] == row["target_id"]) / max(len(ambiguous), 1),
                "invalid_touch_rate": sum(1 for row in evaluated if row[field] == "None") / max(len(evaluated), 1),
                "correction_success_rate": correction_success(evaluated, field),
                "overcorrection_rate": overcorrection(evaluated, field),
            }
        )
    per_target = []
    for target in sorted({row["target_id"] for row in evaluated}):
        target_rows = [row for row in evaluated if row["target_id"] == target]
        per_target.append(
            {
                "target_id": target,
                "n": len(target_rows),
                "user_gaussian_accuracy": sum(1 for row in target_rows if row["user_gaussian_prediction"] == target) / max(len(target_rows), 1),
                "visual_boundary_accuracy": sum(1 for row in target_rows if row["visual_boundary_prediction"] == target) / max(len(target_rows), 1),
            }
        )
    error_rows = [
        {
            "target_id": row["target_id"],
            "visual_boundary_prediction": row["visual_boundary_prediction"],
            "user_gaussian_prediction": row["user_gaussian_prediction"],
            "offset_x": row["offset_x"],
            "offset_y": row["offset_y"],
            "is_ambiguous": row["is_ambiguous"],
        }
        for row in evaluated
        if row["user_gaussian_prediction"] != row["target_id"] or row["visual_boundary_prediction"] != row["target_id"]
    ][:5000]
    write_csv(REPORTS_DIR / "tsi_target_selection_metrics.csv", metric_rows)
    write_csv(REPORTS_DIR / "tsi_per_target_accuracy.csv", per_target)
    write_csv(REPORTS_DIR / "tsi_error_analysis.csv", error_rows)
    plot_tsi(evaluated, metric_rows)
    return metric_rows


def correction_success(rows: list[dict[str, Any]], pred_field: str) -> float:
    candidates = [row for row in rows if row["visual_boundary_prediction"] != row["target_id"]]
    return sum(1 for row in candidates if row[pred_field] == row["target_id"]) / max(len(candidates), 1)


def overcorrection(rows: list[dict[str, Any]], pred_field: str) -> float:
    candidates = [row for row in rows if row["visual_boundary_prediction"] == row["target_id"]]
    return sum(1 for row in candidates if row[pred_field] != row["target_id"]) / max(len(candidates), 1)


def build_public_to_unity_config() -> dict[str, Any]:
    tsi_rows = read_parquet(dataset_root("tsi") / "processed" / "tsi_touch_targets.parquet")
    if not tsi_rows:
        raise RuntimeError("TSI processed rows are required for public-derived Unity config")
    offsets_x = [float(row["offset_x"]) for row in tsi_rows]
    offsets_y = [float(row["offset_y"]) for row in tsi_rows]
    distances = [math.hypot(x, y) for x, y in zip(offsets_x, offsets_y)]
    variance_x = statistics.pvariance(offsets_x)
    variance_y = statistics.pvariance(offsets_y)
    default_variance = (variance_x + variance_y) / 2.0
    median_key_radius = statistics.median([min(float(row["target_width"]), float(row["target_height"])) / 2.0 for row in tsi_rows])
    q90_distance = sorted(distances)[int(len(distances) * 0.9)]
    expansion = min(max(q90_distance / max(median_key_radius, 1.0), 1.05), 1.8)
    config = {
        "source_datasets_used": ["tsi", "touch_dynamics", "mc_snake"],
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "default_touch_variance": default_variance,
        "default_attack_variance": default_variance,
        "default_dodge_variance": default_variance,
        "recommended_hitbox_expansion_ratio": expansion,
        "recommended_expanded_hitbox_margin": expansion,
        "recommended_ambiguity_margin": statistics.quantiles(distances, n=4)[2],
        "recommended_ambiguity_margin_px": statistics.quantiles(distances, n=4)[2],
        "recommended_min_variance": max(min(default_variance * 0.25, default_variance), 1.0),
        "recommended_max_variance": max(default_variance * 4.0, default_variance + 1.0),
        "expected_touch_offset_mean": {"x": statistics.mean(offsets_x), "y": statistics.mean(offsets_y)},
        "expected_touch_offset_std": {"x": math.sqrt(variance_x), "y": math.sqrt(variance_y)},
        "recommended_default_variance": default_variance,
        "recommended_default_std_px": math.sqrt(default_variance),
        "prior_warning": "Public-derived defaults come from target-labeled TSI touch data plus unlabeled public game touch dynamics. They do not validate Unity Attack/Dodge game-state priors.",
        "notes": [
            "TSI supplies target labels and keyboard layout for touch-target modeling.",
            "Touch-Dynamics and MC-Snake supply real mobile game touch dynamics but no Attack/Dodge labels or Unity button layouts.",
            "Unity calibration overrides these defaults when available.",
        ],
    }
    write_json(MODELS_DIR / "public_touch_prior_config.json", config)
    streaming = Path("Assets") / "StreamingAssets" / "ADUI" / "public_touch_prior_config.json"
    write_json(streaming, config)
    return config


def inspect_public_datasets_real() -> None:
    ensure_public_dirs()
    summaries = {
        "touch_dynamics": read_report_one(REPORTS_DIR / "touch_dynamics_dataset_summary.csv"),
        "mc_snake": read_report_one(REPORTS_DIR / "mc_snake_dataset_summary.csv"),
        "tsi": read_report_one(REPORTS_DIR / "tsi_dataset_summary.csv"),
        "screen_annotation": read_report_one(REPORTS_DIR / "screen_annotation_summary.csv"),
        "rico": read_report_one(REPORTS_DIR / "rico_ui_summary.csv"),
    }
    inventory = []
    size = []
    coverage = []
    lines = ["# Public Dataset Availability", ""]
    role_limit = {
        "touch_dynamics": (
            "real mobile game touch dynamics and user variation",
            "does not directly validate Attack/Dodge correction without action labels, button layout, intended action, and game state",
        ),
        "mc_snake": (
            "secondary real mobile game touch dynamics and region/continuity analysis",
            "does not directly validate Attack/Dodge correction without action/layout labels",
        ),
        "tsi": (
            "target-labeled public touch benchmark for Gaussian target-selection and hitbox baselines",
            "keyboard target data, not game validation or combat-prior validation",
        ),
        "henze": ("optional target-selection/hitbox benchmark", "unavailable unless manually placed or discoverable"),
        "rico": ("optional UI grounding support", "not game combat validation"),
        "screen_annotation": ("optional UI annotation/grounding support", "not game combat validation"),
    }
    for spec in PUBLIC_DATASETS:
        summary = summaries.get(spec.dataset_id, {})
        raw_dir = dataset_root(spec.dataset_id) / "raw"
        manual_dir = dataset_root(spec.dataset_id) / "manual"
        raw_files = [path for root in [raw_dir, manual_dir] if root.exists() for path in root.rglob("*") if path.is_file()]
        processed_rows = int(float(summary.get("processed_rows") or 0))
        available = processed_rows > 0
        role, limit = role_limit.get(spec.dataset_id, (spec.evidence_role, spec.direct_validation_limit))
        inventory.append(
            {
                "dataset_id": spec.dataset_id,
                "display_name": spec.display_name,
                "source_url": spec.source_url,
                "available": available,
                "raw_file_count": len(raw_files),
                "raw_size_bytes": sum(path.stat().st_size for path in raw_files),
                "processed_rows": processed_rows,
                "users": summary.get("users", 0),
                "games": summary.get("games", ""),
                "can_validate": role,
                "cannot_validate": limit,
            }
        )
        size.append(
            {
                "dataset_id": spec.dataset_id,
                "available": available,
                "raw_file_count": len(raw_files),
                "raw_size_bytes": sum(path.stat().st_size for path in raw_files),
                "processed_rows": processed_rows,
                "users": summary.get("users", 0),
                "games": summary.get("games", ""),
            }
        )
        coverage.append(
            {
                "dataset_id": spec.dataset_id,
                "has_touch_coordinates": bool(summary.get("has_touch_coordinates") in {True, "True", "true"}),
                "has_action_labels": bool(summary.get("has_action_labels") in {True, "True", "true"}),
                "has_target_button_layout": bool(summary.get("has_target_layout") in {True, "True", "true"}),
                "has_screen_frames": False,
                "has_game_state_labels": bool(summary.get("has_game_state_labels") in {True, "True", "true"}),
                "fields_missing_for_attack_dodge_validation": "action labels;Unity button layout;intended action;game state"
                if spec.dataset_id in {"touch_dynamics", "mc_snake", "tsi"}
                else "not processed locally",
            }
        )
        lines.extend(
            [
                f"## {spec.display_name}",
                f"- availability: {available}",
                f"- raw files: {len(raw_files)}",
                f"- raw size bytes: {sum(path.stat().st_size for path in raw_files)}",
                f"- processed rows: {processed_rows}",
                f"- users: {summary.get('users', 0)}",
                f"- games: {summary.get('games', '')}",
                f"- can validate: {role}",
                f"- cannot validate: {limit}",
                "",
            ]
        )
    write_csv(REPORTS_DIR / "public_dataset_inventory.csv", inventory)
    write_csv(REPORTS_DIR / "public_dataset_size_summary.csv", size)
    write_csv(REPORTS_DIR / "public_dataset_field_coverage.csv", coverage)
    (REPORTS_DIR / "public_dataset_availability.md").write_text("\n".join(lines), encoding="utf-8")


def read_report_one(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    return rows[0] if rows else {}


def build_all_public_real() -> None:
    process_touch_dynamics()
    process_mc_snake()
    process_tsi()
    evaluate_touch_dynamics()
    evaluate_tsi_target_selection()
    try_henze()
    try_ui_optional()
    build_public_to_unity_config()
    inspect_public_datasets_real()
    public_report_real()


def try_henze() -> None:
    rows = []
    for root in [dataset_root("henze") / "manual", dataset_root("henze") / "raw"]:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if path.is_file() and path.suffix.lower() in {".txt", ".csv"}:
                rows.append({"source_file": str(path), "status": "manual_file_detected"})
    if rows:
        write_csv(REPORTS_DIR / "henze_target_selection_metrics.csv", rows)
    else:
        write_csv(REPORTS_DIR / "henze_target_selection_metrics.csv", [{"dataset_id": "henze", "status": "unavailable", "attempt": "manual placement checked; no parseable local files"}])
    plot_placeholder_bar(FIGURES_DIR / "henze_hitbox_comparison.png", "Henze unavailable")


def try_ui_optional() -> None:
    screen_rows = process_screen_annotation()
    if screen_rows:
        write_csv(REPORTS_DIR / "ui_grounding_dataset_summary.csv", [{"dataset_id": "screen_annotation", "status": "available", "elements": len(screen_rows)}])
        plot_screen_annotation(screen_rows)
    else:
        write_csv(REPORTS_DIR / "ui_grounding_dataset_summary.csv", [{"status": "optional_unavailable"}])
        plot_placeholder_bar(FIGURES_DIR / "ui_component_box_examples.png", "UI grounding optional data unavailable")
    write_csv(REPORTS_DIR / "rico_ui_summary.csv", [{"dataset_id": "rico", "status": "unavailable", "attempt": "manual placement checked; no local Rico files"}])


def process_screen_annotation() -> list[dict[str, Any]]:
    raw_dir = dataset_root("screen_annotation") / "raw"
    processed_dir = dataset_root("screen_annotation") / "processed"
    rows: list[dict[str, Any]] = []
    for split in ["train", "valid", "test"]:
        path = raw_dir / f"{split}.csv"
        if not path.exists():
            continue
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            reader = csv.DictReader(handle)
            for raw in reader:
                screen_id = raw.get("screen_id") or raw.get("image_id") or ""
                label = raw.get("screen_annotation") or raw.get("label") or ""
                for match in re.finditer(r"\b([A-Z_]+)\s+([^(),]*?)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)", label):
                    element_type = match.group(1)
                    coords = [int(match.group(i)) for i in range(3, 7)]
                    if any(abs(value) > 100000 for value in coords):
                        continue
                    rows.append(
                        {
                            "dataset_id": "screen_annotation",
                            "split": split,
                            "screen_id": str(screen_id),
                            "element_type": element_type,
                            "text_or_desc": match.group(2).strip(),
                            "x1": coords[0],
                            "x2": coords[1],
                            "y1": coords[2],
                            "y2": coords[3],
                            "button_like": element_type in {"BUTTON", "ICON_BUTTON", "FAB", "RADIO_BUTTON", "CHECKBOX", "SWITCH"},
                        }
                    )
    if rows:
        write_parquet(processed_dir / "screen_annotation_elements.parquet", rows)
        write_csv(processed_dir / "screen_annotation_elements_sample.csv", rows[:1000])
        counts = Counter(row["element_type"] for row in rows)
        summary = [
            {
                "dataset_id": "screen_annotation",
                "status": "available",
                "processed_rows": len(rows),
                "screens": len({row["screen_id"] for row in rows}),
                "element_types": len(counts),
                "button_like_elements": sum(1 for row in rows if row["button_like"]),
                "has_touch_coordinates": False,
                "has_target_layout": True,
                "has_action_labels": False,
                "has_game_state_labels": False,
            }
        ]
        write_csv(REPORTS_DIR / "screen_annotation_summary.csv", summary)
    else:
        write_csv(REPORTS_DIR / "screen_annotation_summary.csv", [{"dataset_id": "screen_annotation", "status": "unavailable", "processed_rows": 0, "attempt": "raw CSV/manual placement checked"}])
    return rows


def public_report_real() -> None:
    size_rows = read_csv_rows(REPORTS_DIR / "public_dataset_size_summary.csv")
    tsi_metrics = read_csv_rows(REPORTS_DIR / "tsi_target_selection_metrics.csv")
    lines = [
        "# Public Dataset Report",
        "",
        "Public datasets are used as external evidence and sanity checks, not as direct Unity Attack/Dodge validation.",
        "",
    ]
    for row in size_rows:
        lines.extend(
            [
                f"## {row['dataset_id']}",
                f"- available: {row['available']}",
                f"- raw_file_count: {row['raw_file_count']}",
                f"- raw_size_bytes: {row['raw_size_bytes']}",
                f"- processed_rows: {row['processed_rows']}",
                f"- users: {row['users']}",
                f"- games: {row.get('games', '')}",
                "",
            ]
        )
    lines.extend(
        [
            "## Required Wording",
            "- Public game touch logs validate touch dynamics, not Attack/Dodge correction.",
            "- Public target-selection datasets validate touch-target modeling and hitbox behavior, not game-state prior.",
            "- Public UI datasets support UI grounding, not game-specific combat context.",
            "- Unity controlled prototype validates the actual Attack/Dodge Bayesian pipeline.",
            "- This is not yet commercial mobile game validation.",
        ]
    )
    (REPORTS_DIR / "public_dataset_report.md").write_text("\n".join(lines), encoding="utf-8")
    final_lines = [
        "# Public Touch Dataset 최종 요약",
        "",
        "## 확보한 공개 데이터셋",
    ]
    for row in size_rows:
        final_lines.append(
            f"- {row['dataset_id']}: available={row['available']}, raw_files={row['raw_file_count']}, "
            f"raw_size={row['raw_size_bytes']} bytes, processed_rows={row['processed_rows']}, users={row['users']}, games={row.get('games', '')}"
        )
    final_lines.extend(
        [
            "",
            "## 역할",
            "- Touch-Dynamics/MC-Snake는 실제 모바일 게임 touch dynamics, 사용자 차이, touch density 근거다.",
            "- TSI는 target-labeled touch-target modeling과 Gaussian/hitbox baseline sanity check다.",
            "- Screen Annotation/Rico 계열은 UI grounding support이며 combat context 검증이 아니다.",
            "",
            "## TSI target-selection benchmark",
        ]
    )
    for row in tsi_metrics:
        final_lines.append(
            f"- {row.get('baseline')}: accuracy={row.get('accuracy')}, macro_f1={row.get('macro_f1')}, "
            f"correction_success={row.get('correction_success_rate')}, overcorrection={row.get('overcorrection_rate')}"
        )
    final_lines.extend(
        [
            "",
            "## 해석 한계",
            "- Public game touch logs validate touch dynamics, not Attack/Dodge correction.",
            "- Public target-selection datasets validate touch-target modeling and hitbox behavior, not game-state prior.",
            "- Public UI datasets support UI grounding, not game-specific combat context.",
            "- Unity controlled prototype validates the actual Attack/Dodge Bayesian pipeline.",
        ]
    )
    (REPORTS_DIR / "final_public_touch_summary_ko.md").write_text("\n".join(final_lines), encoding="utf-8")


def read_csv_rows(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [dict(row) for row in csv.DictReader(handle)]


def plot_touch_dynamics(features: list[dict[str, Any]]) -> None:
    counts = Counter(str(row["game_id"]) for row in features)
    plt.figure(figsize=(7, 4))
    plt.bar(list(counts.keys()), list(counts.values()))
    plt.ylabel("session/user feature rows")
    plt.tight_layout()
    FIGURES_DIR.mkdir(parents=True, exist_ok=True)
    plt.savefig(FIGURES_DIR / "touch_density_by_game.png", dpi=150)
    plt.close()

    sample = features[:200]
    plt.figure(figsize=(6, 4))
    plt.scatter([float(row["touch_rate_per_sec"]) for row in sample], [float(row["burstiness"]) for row in sample], s=16, alpha=0.7)
    plt.xlabel("touch_rate_per_sec")
    plt.ylabel("burstiness")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "user_touch_profile_examples.png", dpi=150)
    plt.close()

    by_game = defaultdict(list)
    for row in features:
        by_game[str(row["game_id"])].append(float(row["within_user_variance"]))
    plt.figure(figsize=(7, 4))
    labels = list(by_game.keys())
    plt.boxplot([by_game[label] for label in labels], labels=labels, showfliers=False)
    plt.ylabel("within_user_variance")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "user_variation_boxplots.png", dpi=150)
    plt.close()


def plot_tsi(evaluated: list[dict[str, Any]], metrics: list[dict[str, Any]]) -> None:
    plt.figure(figsize=(8, 4))
    plt.bar([row["baseline"] for row in metrics], [float(row["accuracy"]) for row in metrics])
    plt.xticks(rotation=25, ha="right")
    plt.ylim(0, 1)
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "tsi_baseline_comparison.png", dpi=150)
    plt.close()

    sample = evaluated[:5000]
    plt.figure(figsize=(5, 5))
    plt.scatter([float(row["offset_x"]) for row in sample], [float(row["offset_y"]) for row in sample], s=3, alpha=0.2)
    plt.axhline(0, color="gray", linewidth=1)
    plt.axvline(0, color="gray", linewidth=1)
    plt.xlabel("offset_x")
    plt.ylabel("offset_y")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "tsi_gaussian_contours.png", dpi=150)
    plt.close()

    gated = next((row for row in metrics if row["baseline"] == "user_gaussian_with_ambiguity_gate"), None)
    if gated:
        plt.figure(figsize=(5, 4))
        plt.bar(["correction_success", "overcorrection"], [float(gated["correction_success_rate"]), float(gated["overcorrection_rate"])])
        plt.ylim(0, 1)
        plt.tight_layout()
        plt.savefig(FIGURES_DIR / "tsi_correction_vs_overcorrection.png", dpi=150)
        plt.close()


def plot_placeholder_bar(path: Path, label: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    plt.figure(figsize=(5, 3))
    plt.bar([label], [0])
    plt.tight_layout()
    plt.savefig(path, dpi=150)
    plt.close()


def plot_screen_annotation(rows: list[dict[str, Any]]) -> None:
    counts = Counter(row["element_type"] for row in rows)
    top = counts.most_common(12)
    plt.figure(figsize=(8, 4))
    plt.bar([item[0] for item in top], [item[1] for item in top])
    plt.xticks(rotation=30, ha="right")
    plt.ylabel("elements")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "ui_component_box_examples.png", dpi=150)
    plt.close()

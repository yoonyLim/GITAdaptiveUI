from __future__ import annotations

import csv
import base64
import json
import math
import re
import statistics
import urllib.request
import zipfile
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ModuleNotFoundError:  # pragma: no cover - exercised in minimal environments
    plt = None

from analysis_public.src.data.dataset_registry import (
    PUBLIC_DATASETS,
    PublicDatasetSpec,
    dataset_available,
    dataset_root,
    get_spec,
    iter_expected_paths,
)
from analysis_public.src.paths import FIGURES_DIR, MODELS_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_public_dirs


TOUCH_FIELD_ALIASES = {
    "user_id": ("participant_id", "user_id", "userid", "user", "subject_id", "subject", "player_id", "device_id", "id"),
    "session_id": ("session_id", "session", "task_id", "trial_id", "game", "run"),
    "timestamp_ms": ("timestamp_ms", "time_ms", "timestamp", "time", "t", "ts", "event_time", "elapsed_ms"),
    "x": ("x", "touch_x", "screen_x", "pos_x", "position_x", "first_frame_touch_x", "client_x"),
    "y": ("y", "touch_y", "screen_y", "pos_y", "position_y", "first_frame_touch_y", "client_y"),
    "pressure": ("pressure", "touch_pressure", "force", "p"),
    "touch_major": ("touch_major", "major", "first_frame_touch_major", "radius_x"),
    "touch_minor": ("touch_minor", "minor", "first_frame_touch_minor", "radius_y"),
    "pointer_id": ("pointer_id", "finger_id", "touch_id", "id_touch"),
    "phase": ("phase", "event", "action", "touch_phase"),
    "screen_width": ("screen_width", "width", "display_width"),
    "screen_height": ("screen_height", "height", "display_height"),
}


def safe_float(value: Any, default: float | None = None) -> float | None:
    if value is None:
        return default
    if isinstance(value, (int, float)):
        if math.isnan(float(value)):
            return default
        return float(value)
    text = str(value).strip()
    if not text or text.lower() in {"nan", "none", "null"}:
        return default
    try:
        return float(text)
    except ValueError:
        return default


def safe_int(value: Any, default: int | None = None) -> int | None:
    number = safe_float(value)
    if number is None:
        return default
    return int(number)


def first_present(row: dict[str, Any], aliases: Iterable[str]) -> Any:
    lower = {str(k).lower(): v for k, v in row.items()}
    for alias in aliases:
        if alias.lower() in lower:
            return lower[alias.lower()]
    return None


def normalize_touch_row(row: dict[str, Any], dataset_id: str = "") -> dict[str, Any] | None:
    x = safe_float(first_present(row, TOUCH_FIELD_ALIASES["x"]))
    y = safe_float(first_present(row, TOUCH_FIELD_ALIASES["y"]))
    if x is None or y is None:
        return None
    timestamp = safe_float(first_present(row, TOUCH_FIELD_ALIASES["timestamp_ms"]), 0.0)
    if timestamp is not None and timestamp < 10_000_000 and "timestamp" in str(row).lower():
        timestamp_ms = timestamp
    else:
        timestamp_ms = timestamp
    user_id = first_present(row, TOUCH_FIELD_ALIASES["user_id"]) or "unknown_user"
    session_id = first_present(row, TOUCH_FIELD_ALIASES["session_id"]) or "unknown_session"
    screen_width = safe_float(first_present(row, TOUCH_FIELD_ALIASES["screen_width"]))
    screen_height = safe_float(first_present(row, TOUCH_FIELD_ALIASES["screen_height"]))
    return {
        "dataset_id": dataset_id,
        "user_id": str(user_id),
        "session_id": str(session_id),
        "timestamp_ms": timestamp_ms or 0.0,
        "x": x,
        "y": y,
        "pressure": safe_float(first_present(row, TOUCH_FIELD_ALIASES["pressure"])),
        "touch_major": safe_float(first_present(row, TOUCH_FIELD_ALIASES["touch_major"])),
        "touch_minor": safe_float(first_present(row, TOUCH_FIELD_ALIASES["touch_minor"])),
        "pointer_id": str(first_present(row, TOUCH_FIELD_ALIASES["pointer_id"]) or "0"),
        "phase": str(first_present(row, TOUCH_FIELD_ALIASES["phase"]) or ""),
        "screen_width": screen_width,
        "screen_height": screen_height,
    }


def read_csv_rows(path: Path, limit: int | None = None) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for idx, row in enumerate(reader):
            rows.append(dict(row))
            if limit is not None and idx + 1 >= limit:
                break
    return rows


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
        for row in rows:
            writer.writerow(row)


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


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


def download_dataset(dataset_id: str) -> list[dict[str, str]]:
    ensure_public_dirs()
    spec = get_spec(dataset_id)
    rows: list[dict[str, str]] = []
    raw_dir = dataset_root(dataset_id) / "raw"
    raw_dir.mkdir(parents=True, exist_ok=True)
    if not spec.auto_download_urls:
        return [
            {
                "dataset_id": dataset_id,
                "status": "manual_required",
                "message": "No stable public auto-download URL is configured; place files under the dataset manual/raw directory.",
            }
        ]
    for url in spec.auto_download_urls:
        name = url.rstrip("/").split("/")[-1]
        target = raw_dir / name
        if target.exists() and target.stat().st_size > 0:
            rows.append({"dataset_id": dataset_id, "url": url, "path": str(target), "status": "exists"})
            continue
        try:
            urllib.request.urlretrieve(url, target)
            rows.append({"dataset_id": dataset_id, "url": url, "path": str(target), "status": "downloaded"})
        except Exception as exc:  # noqa: BLE001
            rows.append({"dataset_id": dataset_id, "url": url, "path": str(target), "status": "unavailable", "error": str(exc)})
            if target.exists() and target.stat().st_size == 0:
                target.unlink()
    return rows


def dataset_file_stats(spec: PublicDatasetSpec) -> dict[str, Any]:
    paths = [path for path in iter_expected_paths(spec) if path.is_file()]
    row_count = 0
    fields: set[str] = set()
    bytes_total = 0
    for path in paths:
        bytes_total += path.stat().st_size
        try:
            if path.suffix.lower() == ".csv":
                with path.open("r", encoding="utf-8-sig", newline="") as handle:
                    reader = csv.reader(handle)
                    header = next(reader, [])
                    fields.update(header)
                    row_count += sum(1 for _ in reader)
            elif path.suffix.lower() == ".json":
                fields.update(["json"])
                row_count += 1
            elif path.suffix.lower() == ".zip":
                with zipfile.ZipFile(path) as archive:
                    for info in archive.infolist():
                        if info.filename.lower().endswith(".csv"):
                            with archive.open(info) as handle:
                                text = handle.read(65536).decode("utf-8", errors="ignore").splitlines()
                                if text:
                                    fields.update(next(csv.reader([text[0]])))
                                row_count += max(0, len(text) - 1)
            else:
                row_count += 1
        except Exception:
            continue
    return {
        "dataset_id": spec.dataset_id,
        "file_count": len(paths),
        "bytes": bytes_total,
        "row_count_observed": row_count,
        "fields_observed": ";".join(sorted(fields)),
    }


def inspect_public_datasets() -> None:
    ensure_public_dirs()
    inventory_rows = []
    size_rows = []
    coverage_rows = []
    availability_lines = ["# Public Dataset Availability", ""]
    for spec in PUBLIC_DATASETS:
        available = dataset_available(spec)
        stats = dataset_file_stats(spec)
        inventory_rows.append(
            {
                "dataset_id": spec.dataset_id,
                "display_name": spec.display_name,
                "source_url": spec.source_url,
                "available": str(available),
                "evidence_role": spec.evidence_role,
                "direct_validation_limit": spec.direct_validation_limit,
            }
        )
        size_rows.append(
            {
                "dataset_id": spec.dataset_id,
                "available": str(available),
                "file_count": stats["file_count"],
                "bytes": stats["bytes"],
                "row_count_observed": stats["row_count_observed"],
            }
        )
        observed = set(stats["fields_observed"].split(";")) if stats["fields_observed"] else set()
        required = set()
        if spec.dataset_id in {"touch_dynamics", "mc_snake"}:
            required = {"x", "y", "timestamp", "user_id"}
        elif spec.dataset_id == "tsi":
            required = {"participant_id", "ref_char", "first_frame_touch_x", "first_frame_touch_y"}
        elif spec.dataset_id == "screen_annotation":
            required = {"image_id", "label"}
        coverage_rows.append(
            {
                "dataset_id": spec.dataset_id,
                "available": str(available),
                "fields_observed": stats["fields_observed"],
                "required_fields_proxy": ";".join(sorted(required)),
                "coverage_note": "observed locally" if observed else "no local files observed",
            }
        )
        availability_lines.extend(
            [
                f"## {spec.display_name}",
                f"- status: {'available' if available else 'unavailable'}",
                f"- local_dir: `{dataset_root(spec.dataset_id)}`",
                f"- role: {spec.evidence_role}",
                f"- limit: {spec.direct_validation_limit}",
                "",
            ]
        )
    write_csv(REPORTS_DIR / "public_dataset_inventory.csv", inventory_rows)
    write_csv(REPORTS_DIR / "public_dataset_size_summary.csv", size_rows)
    write_csv(REPORTS_DIR / "public_dataset_field_coverage.csv", coverage_rows)
    (REPORTS_DIR / "public_dataset_availability.md").write_text("\n".join(availability_lines), encoding="utf-8")


def iter_touch_csv_rows(dataset_id: str) -> Iterable[dict[str, Any]]:
    root = dataset_root(dataset_id)
    for path in root.glob("**/*"):
        if not path.is_file():
            continue
        suffix = path.suffix.lower()
        if suffix == ".csv":
            for row in read_csv_rows(path):
                normalized = normalize_touch_row(row, dataset_id=dataset_id)
                if normalized:
                    yield normalized
        elif suffix == ".zip":
            try:
                with zipfile.ZipFile(path) as archive:
                    for info in archive.infolist():
                        if not info.filename.lower().endswith(".csv"):
                            continue
                        with archive.open(info) as handle:
                            text = handle.read().decode("utf-8", errors="ignore").splitlines()
                        if not text:
                            continue
                        reader = csv.DictReader(text)
                        for row in reader:
                            normalized = normalize_touch_row(dict(row), dataset_id=dataset_id)
                            if normalized:
                                yield normalized
            except zipfile.BadZipFile:
                continue


def compute_touch_dynamics_features(events: list[dict[str, Any]], dataset_id: str) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for event in events:
        grouped[(str(event.get("user_id", "unknown_user")), str(event.get("session_id", "unknown_session")))].append(event)
    rows: list[dict[str, Any]] = []
    user_points: dict[str, list[tuple[float, float]]] = defaultdict(list)
    for (user_id, session_id), group in grouped.items():
        group = sorted(group, key=lambda item: safe_float(item.get("timestamp_ms"), 0.0) or 0.0)
        xs = [safe_float(row.get("x"), 0.0) or 0.0 for row in group]
        ys = [safe_float(row.get("y"), 0.0) or 0.0 for row in group]
        ts = [safe_float(row.get("timestamp_ms"), 0.0) or 0.0 for row in group]
        for x, y in zip(xs, ys):
            user_points[user_id].append((x, y))
        duration_ms = max(ts) - min(ts) if len(ts) >= 2 else 0.0
        duration_sec = max(duration_ms / 1000.0, 0.001)
        distances = [math.hypot(xs[i] - xs[i - 1], ys[i] - ys[i - 1]) for i in range(1, len(xs))]
        deltas = [max((ts[i] - ts[i - 1]) / 1000.0, 0.001) for i in range(1, len(ts))]
        velocities = [dist / dt for dist, dt in zip(distances, deltas)]
        accelerations = [(velocities[i] - velocities[i - 1]) / deltas[i] for i in range(1, len(velocities))]
        jerks = [(accelerations[i] - accelerations[i - 1]) / deltas[i + 1] for i in range(1, len(accelerations))]
        width = max([safe_float(row.get("screen_width")) or max(xs or [1.0]) for row in group] or [1.0])
        left = sum(1 for x in xs if x < width / 3.0)
        right = sum(1 for x in xs if x > 2.0 * width / 3.0)
        center = max(0, len(xs) - left - right)
        pressures = [safe_float(row.get("pressure")) for row in group if safe_float(row.get("pressure")) is not None]
        majors = [safe_float(row.get("touch_major")) for row in group if safe_float(row.get("touch_major")) is not None]
        minors = [safe_float(row.get("touch_minor")) for row in group if safe_float(row.get("touch_minor")) is not None]
        time_counts = Counter(ts)
        multi_touch_ratio = sum(1 for count in time_counts.values() if count > 1) / max(len(time_counts), 1)
        burstiness = statistics.pstdev(deltas) / (statistics.mean(deltas) + 1e-9) if len(deltas) >= 2 else 0.0
        touch_rate = len(group) / duration_sec
        velocity_mean = statistics.mean(velocities) if velocities else 0.0
        acceleration_mean = statistics.mean(accelerations) if accelerations else 0.0
        jerk_mean = statistics.mean(jerks) if jerks else 0.0
        swipe_distance = sum(distances)
        rows.append(
            {
                "dataset_id": dataset_id,
                "user_id": user_id,
                "session_id": session_id,
                "touch_count": len(group),
                "session_duration": duration_sec,
                "touch_rate_per_sec": touch_rate,
                "burstiness": burstiness,
                "pressure_mean": statistics.mean(pressures) if pressures else "",
                "pressure_std": statistics.pstdev(pressures) if len(pressures) >= 2 else "",
                "touch_major_mean": statistics.mean(majors) if majors else "",
                "touch_major_std": statistics.pstdev(majors) if len(majors) >= 2 else "",
                "touch_minor_mean": statistics.mean(minors) if minors else "",
                "touch_minor_std": statistics.pstdev(minors) if len(minors) >= 2 else "",
                "velocity": velocity_mean,
                "acceleration": acceleration_mean,
                "jerk": jerk_mean,
                "left_region_ratio": left / max(len(xs), 1),
                "right_region_ratio": right / max(len(xs), 1),
                "center_region_ratio": center / max(len(xs), 1),
                "multi_touch_ratio": multi_touch_ratio,
                "long_press_ratio": 0.0,
                "swipe_distance": swipe_distance,
                "control_continuity_proxy": 1.0 / (1.0 + burstiness + abs(jerk_mean) / 1000.0),
                "action_intensity_proxy": touch_rate * (1.0 + velocity_mean / 1000.0),
                "ui_skill_proxy": 1.0 / (1.0 + burstiness + (statistics.pstdev(velocities) if len(velocities) > 1 else 0.0) / 1000.0),
                "within_user_variance": "",
                "between_user_variance": "",
            }
        )
    all_user_centers = []
    within_by_user = {}
    for user_id, pts in user_points.items():
        if not pts:
            continue
        mx = statistics.mean([p[0] for p in pts])
        my = statistics.mean([p[1] for p in pts])
        all_user_centers.append((mx, my))
        within_by_user[user_id] = statistics.mean([(x - mx) ** 2 + (y - my) ** 2 for x, y in pts])
    if all_user_centers:
        gx = statistics.mean([p[0] for p in all_user_centers])
        gy = statistics.mean([p[1] for p in all_user_centers])
        between = statistics.mean([(x - gx) ** 2 + (y - gy) ** 2 for x, y in all_user_centers])
    else:
        between = ""
    for row in rows:
        row["within_user_variance"] = within_by_user.get(str(row["user_id"]), "")
        row["between_user_variance"] = between
    return rows


def build_touch_dynamics_outputs() -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    ensure_public_dirs()
    all_rows: list[dict[str, Any]] = []
    for dataset_id in ("touch_dynamics", "mc_snake"):
        events = list(iter_touch_csv_rows(dataset_id))
        features = compute_touch_dynamics_features(events, dataset_id) if events else []
        all_rows.extend(features)
        write_csv(PROCESSED_DIR / f"{dataset_id}_touch_features.csv", features or [{"dataset_id": dataset_id, "status": "unavailable"}])
    variation_rows = [
        {
            "dataset_id": row.get("dataset_id"),
            "user_id": row.get("user_id"),
            "within_user_variance": row.get("within_user_variance"),
            "between_user_variance": row.get("between_user_variance"),
        }
        for row in all_rows
    ]
    write_csv(REPORTS_DIR / "public_touch_dynamics_summary.csv", all_rows or [{"status": "unavailable"}])
    write_csv(REPORTS_DIR / "public_user_variation_summary.csv", variation_rows or [{"status": "unavailable"}])
    plot_touch_density(all_rows)
    plot_user_examples(all_rows)
    return all_rows, variation_rows


def plot_touch_density(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "touch_density_by_game.png", "Public touch dynamics data not available locally.")
        return
    counts = Counter(str(row.get("dataset_id")) for row in rows)
    plt.figure(figsize=(7, 4))
    plt.bar(list(counts.keys()), list(counts.values()), color=["#2563eb", "#059669", "#d97706"])
    plt.ylabel("session feature rows")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "touch_density_by_game.png", dpi=150)
    plt.close()


def plot_user_examples(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "user_touch_profile_examples.png", "No public user touch profiles available locally.")
        return
    sample = rows[:20]
    plt.figure(figsize=(7, 4))
    plt.scatter(
        [safe_float(row.get("touch_rate_per_sec"), 0.0) or 0.0 for row in sample],
        [safe_float(row.get("burstiness"), 0.0) or 0.0 for row in sample],
        c=[safe_float(row.get("action_intensity_proxy"), 0.0) or 0.0 for row in sample],
        cmap="viridis",
    )
    plt.xlabel("touch_rate_per_sec")
    plt.ylabel("burstiness")
    plt.colorbar(label="action_intensity_proxy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "user_touch_profile_examples.png", dpi=150)
    plt.close()


def load_tsi_records(touch_csv: Path, keyboard_json: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    touches = read_csv_rows(touch_csv)
    keyboard = json.loads(keyboard_json.read_text(encoding="utf-8"))
    return touches, keyboard


def tsi_key_id(ref_char: str) -> str:
    text = str(ref_char).strip()
    if text.upper() == "SPACE":
        return "SPACE"
    if text == ".":
        return "."
    return text.lower()


def key_info_lookup(keyboard: dict[str, Any]) -> dict[str, dict[str, float]]:
    keys = keyboard.get("keys_info", {})
    result: dict[str, dict[str, float]] = {}
    for key, value in keys.items():
        key_id = tsi_key_id(key)
        result[key_id] = {
            "center_x": safe_float(value.get("key_center_x"), 0.0) or 0.0,
            "center_y": safe_float(value.get("key_center_y"), 0.0) or 0.0,
            "width": safe_float(value.get("key_width"), keyboard.get("keyboard_info", {}).get("most_common_key_width", 1.0)) or 1.0,
            "height": safe_float(value.get("key_height"), keyboard.get("keyboard_info", {}).get("most_common_key_height", 1.0)) or 1.0,
        }
    return result


def target_rows_from_tsi(touches: list[dict[str, Any]], keyboard: dict[str, Any]) -> list[dict[str, Any]]:
    keys = key_info_lookup(keyboard)
    keyboard_info = keyboard.get("keyboard_info", {})
    top_y = safe_float(keyboard_info.get("top_left_y_position"), 0.0) or 0.0
    keyboard_height = safe_float(keyboard_info.get("keyboard_height"), 0.0) or 0.0
    rows = []
    for touch in touches:
        key_id = tsi_key_id(str(touch.get("ref_char", "")))
        if key_id not in keys:
            continue
        x = safe_float(touch.get("first_frame_touch_x"))
        y = safe_float(touch.get("first_frame_touch_y"))
        if x is None or y is None:
            continue
        y_keyboard = y - top_y if top_y and y > keyboard_height + 100 else y
        info = keys[key_id]
        dx = x - info["center_x"]
        dy = y_keyboard - info["center_y"]
        rows.append(
            {
                "participant_id": touch.get("participant_id", "unknown_user"),
                "task_id": touch.get("task_id", ""),
                "trial_id": touch.get("trial_id", ""),
                "target_id": key_id,
                "touch_x": x,
                "touch_y": y_keyboard,
                "target_center_x": info["center_x"],
                "target_center_y": info["center_y"],
                "target_width": info["width"],
                "target_height": info["height"],
                "offset_x": dx,
                "offset_y": dy,
                "distance_to_target": math.hypot(dx, dy),
            }
        )
    return rows


def nearest_key_prediction(row: dict[str, Any], keys: dict[str, dict[str, float]], margin: float = 1.0) -> str:
    x = safe_float(row.get("touch_x"), 0.0) or 0.0
    y = safe_float(row.get("touch_y"), 0.0) or 0.0
    inside: list[tuple[float, str]] = []
    for key, info in keys.items():
        dx = abs(x - info["center_x"])
        dy = abs(y - info["center_y"])
        if dx <= info["width"] * margin / 2.0 and dy <= info["height"] * margin / 2.0:
            inside.append((math.hypot(dx, dy), key))
    if inside:
        return sorted(inside)[0][1]
    return min(keys, key=lambda key: math.hypot(x - keys[key]["center_x"], y - keys[key]["center_y"]))


def gaussian_prediction(
    row: dict[str, Any],
    keys: dict[str, dict[str, float]],
    mean_x: float,
    mean_y: float,
    var_x: float,
    var_y: float,
) -> str:
    x = safe_float(row.get("touch_x"), 0.0) or 0.0
    y = safe_float(row.get("touch_y"), 0.0) or 0.0
    vx = max(var_x, 1.0)
    vy = max(var_y, 1.0)
    best_key = ""
    best_score = -1e99
    for key, info in keys.items():
        ox = x - info["center_x"]
        oy = y - info["center_y"]
        score = -(((ox - mean_x) ** 2) / (2.0 * vx) + ((oy - mean_y) ** 2) / (2.0 * vy))
        if score > best_score:
            best_score = score
            best_key = key
    return best_key


def evaluate_target_rows(rows: list[dict[str, Any]], keyboard: dict[str, Any]) -> list[dict[str, Any]]:
    keys = key_info_lookup(keyboard)
    if not rows or not keys:
        return []
    offsets_x = [safe_float(row.get("offset_x"), 0.0) or 0.0 for row in rows]
    offsets_y = [safe_float(row.get("offset_y"), 0.0) or 0.0 for row in rows]
    global_mean_x = statistics.mean(offsets_x)
    global_mean_y = statistics.mean(offsets_y)
    global_var_x = statistics.pvariance(offsets_x) if len(offsets_x) > 1 else 1.0
    global_var_y = statistics.pvariance(offsets_y) if len(offsets_y) > 1 else 1.0
    by_user: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        by_user[str(row.get("participant_id"))].append(row)
    user_stats = {}
    for user_id, user_rows in by_user.items():
        ux = [safe_float(row.get("offset_x"), 0.0) or 0.0 for row in user_rows]
        uy = [safe_float(row.get("offset_y"), 0.0) or 0.0 for row in user_rows]
        user_stats[user_id] = (
            statistics.mean(ux),
            statistics.mean(uy),
            statistics.pvariance(ux) if len(ux) > 1 else global_var_x,
            statistics.pvariance(uy) if len(uy) > 1 else global_var_y,
        )
    evaluated = []
    for row in rows:
        target = str(row["target_id"])
        visual = nearest_key_prediction(row, keys, margin=1.0)
        expanded = nearest_key_prediction(row, keys, margin=1.25)
        global_gauss = gaussian_prediction(row, keys, global_mean_x, global_mean_y, global_var_x, global_var_y)
        stats = user_stats.get(str(row.get("participant_id")), (global_mean_x, global_mean_y, global_var_x, global_var_y))
        user_gauss = gaussian_prediction(row, keys, *stats)
        half_w = max(safe_float(row.get("target_width"), 1.0) or 1.0, 1.0) / 2.0
        half_h = max(safe_float(row.get("target_height"), 1.0) or 1.0, 1.0) / 2.0
        norm_boundary = max(abs((safe_float(row.get("offset_x"), 0.0) or 0.0) / half_w), abs((safe_float(row.get("offset_y"), 0.0) or 0.0) / half_h))
        is_ambiguous = 0.85 <= norm_boundary <= 1.25
        evaluated.append(
            {
                **row,
                "visual_boundary_prediction": visual,
                "expanded_hitbox_prediction": expanded,
                "global_gaussian_prediction": global_gauss,
                "user_gaussian_prediction": user_gauss,
                "visual_boundary_correct": visual == target,
                "expanded_hitbox_correct": expanded == target,
                "global_gaussian_correct": global_gauss == target,
                "user_gaussian_correct": user_gauss == target,
                "is_ambiguous": is_ambiguous,
            }
        )
    return evaluated


def summarize_target_metrics(evaluated: list[dict[str, Any]], dataset_id: str) -> list[dict[str, Any]]:
    if not evaluated:
        return [{"dataset_id": dataset_id, "status": "unavailable"}]
    baselines = [
        ("visual_boundary", "visual_boundary_correct"),
        ("expanded_hitbox", "expanded_hitbox_correct"),
        ("global_gaussian", "global_gaussian_correct"),
        ("user_gaussian", "user_gaussian_correct"),
    ]
    rows = []
    for name, field in baselines:
        acc = sum(1 for row in evaluated if row[field]) / max(len(evaluated), 1)
        ambiguous = [row for row in evaluated if row.get("is_ambiguous")]
        amb_acc = sum(1 for row in ambiguous if row[field]) / max(len(ambiguous), 1) if ambiguous else ""
        rows.append({"dataset_id": dataset_id, "baseline": name, "n": len(evaluated), "accuracy": acc, "ambiguous_subset_accuracy": amb_acc})
    correction_success = sum(
        1
        for row in evaluated
        if (not row["visual_boundary_correct"]) and row["user_gaussian_correct"] and row.get("is_ambiguous")
    )
    correction_den = sum(1 for row in evaluated if (not row["visual_boundary_correct"]) and row.get("is_ambiguous"))
    overcorrection = sum(1 for row in evaluated if row["visual_boundary_correct"] and not row["user_gaussian_correct"])
    visual_correct = sum(1 for row in evaluated if row["visual_boundary_correct"])
    rows.append(
        {
            "dataset_id": dataset_id,
            "baseline": "correction_success_rate",
            "n": correction_den,
            "accuracy": correction_success / max(correction_den, 1),
            "ambiguous_subset_accuracy": "",
        }
    )
    rows.append(
        {
            "dataset_id": dataset_id,
            "baseline": "overcorrection_rate",
            "n": visual_correct,
            "accuracy": overcorrection / max(visual_correct, 1),
            "ambiguous_subset_accuracy": "",
        }
    )
    return rows


def evaluate_tsi() -> list[dict[str, Any]]:
    ensure_public_dirs()
    root = dataset_root("tsi") / "raw"
    touch_csv = root / "touch_data.csv"
    keyboard_json = root / "keyboard_data.json"
    if not touch_csv.exists() or not keyboard_json.exists():
        write_csv(REPORTS_DIR / "tsi_target_selection_metrics.csv", [{"dataset_id": "tsi", "status": "unavailable"}])
        placeholder_figure(FIGURES_DIR / "tsi_gaussian_contours.png", "TSI files are not available locally.")
        placeholder_figure(FIGURES_DIR / "tsi_baseline_comparison.png", "TSI baseline metrics are unavailable.")
        return [{"dataset_id": "tsi", "status": "unavailable"}]
    touches, keyboard = load_tsi_records(touch_csv, keyboard_json)
    rows = target_rows_from_tsi(touches, keyboard)
    evaluated = evaluate_target_rows(rows, keyboard)
    write_csv(PROCESSED_DIR / "tsi_target_rows.csv", rows)
    write_csv(PROCESSED_DIR / "tsi_target_evaluated.csv", evaluated)
    metrics = summarize_target_metrics(evaluated, "tsi")
    write_csv(REPORTS_DIR / "tsi_target_selection_metrics.csv", metrics)
    plot_tsi_figures(rows, metrics)
    update_public_prior_config_from_tsi(rows, metrics)
    return metrics


def plot_tsi_figures(rows: list[dict[str, Any]], metrics: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "tsi_gaussian_contours.png", "No TSI target rows.")
        placeholder_figure(FIGURES_DIR / "tsi_baseline_comparison.png", "No TSI metrics.")
        return
    plt.figure(figsize=(5, 5))
    plt.scatter(
        [safe_float(row.get("offset_x"), 0.0) or 0.0 for row in rows[:5000]],
        [safe_float(row.get("offset_y"), 0.0) or 0.0 for row in rows[:5000]],
        s=4,
        alpha=0.25,
    )
    plt.axhline(0, color="#64748b", linewidth=1)
    plt.axvline(0, color="#64748b", linewidth=1)
    plt.xlabel("offset_x")
    plt.ylabel("offset_y")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "tsi_gaussian_contours.png", dpi=150)
    plt.close()
    metric_rows = [row for row in metrics if row.get("baseline") in {"visual_boundary", "expanded_hitbox", "global_gaussian", "user_gaussian"}]
    plt.figure(figsize=(7, 4))
    plt.bar([str(row["baseline"]) for row in metric_rows], [safe_float(row.get("accuracy"), 0.0) or 0.0 for row in metric_rows])
    plt.xticks(rotation=20, ha="right")
    plt.ylim(0, 1)
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "tsi_baseline_comparison.png", dpi=150)
    plt.close()


def update_public_prior_config_from_tsi(rows: list[dict[str, Any]], metrics: list[dict[str, Any]]) -> None:
    if rows:
        distances = [safe_float(row.get("distance_to_target"), 0.0) or 0.0 for row in rows]
        offsets_x = [safe_float(row.get("offset_x"), 0.0) or 0.0 for row in rows]
        offsets_y = [safe_float(row.get("offset_y"), 0.0) or 0.0 for row in rows]
        variance = statistics.mean(
            [
                statistics.pvariance(offsets_x) if len(offsets_x) > 1 else 180.0**2,
                statistics.pvariance(offsets_y) if len(offsets_y) > 1 else 180.0**2,
            ]
        )
        margin = min(max((statistics.quantiles(distances, n=20)[18] / (statistics.median(distances) + 1e-9)), 1.1), 1.8) if len(distances) >= 20 else 1.25
        ambiguity_margin = statistics.quantiles(distances, n=4)[2] if len(distances) >= 4 else 60.0
        mean_offset_x = statistics.mean(offsets_x)
        mean_offset_y = statistics.mean(offsets_y)
    else:
        variance = 180.0**2
        margin = 1.25
        ambiguity_margin = 60.0
        mean_offset_x = 0.0
        mean_offset_y = 0.0
    config = {
        "source": "tsi" if rows else "default_no_public_target_data",
        "recommended_default_variance": variance,
        "recommended_default_std_px": math.sqrt(max(variance, 1.0)),
        "recommended_expanded_hitbox_margin": margin,
        "recommended_ambiguity_margin_px": ambiguity_margin,
        "typical_touch_offset": {"mean_x": mean_offset_x, "mean_y": mean_offset_y},
        "prior_warning": (
            "Public touch target datasets do not contain Unity Attack/Dodge labels, Unity button layouts, or game state. "
            "Use this only as a pre-calibration prior and sanity-check source."
        ),
        "metrics_source": metrics,
    }
    write_json(MODELS_DIR / "public_touch_prior_config.json", config)


def parse_henze_lines(lines: Iterable[str]) -> list[dict[str, Any]]:
    events = []
    current_circles: list[tuple[float, float, float]] = []
    device_id = ""
    for raw in lines:
        parts = [part for part in raw.strip().split(";") if part != ""]
        if not parts:
            continue
        tag = parts[0]
        if tag == "DEVICE_STATS":
            device_id = parts[1] if len(parts) > 1 else device_id
        elif tag == "MICROLEVEL":
            current_circles = []
            for circle in parts[2:]:
                vals = circle.split(",")
                if len(vals) >= 3:
                    current_circles.append((safe_float(vals[0], 0.0) or 0.0, safe_float(vals[1], 0.0) or 0.0, safe_float(vals[2], 0.0) or 0.0))
        elif tag == "TAP" and len(parts) >= 5:
            x = safe_float(parts[1], 0.0) or 0.0
            y = safe_float(parts[2], 0.0) or 0.0
            hit_label = parts[4]
            nearest = min(current_circles, key=lambda circle: math.hypot(x - circle[0], y - circle[1])) if current_circles else (0.0, 0.0, 1.0)
            distance = math.hypot(x - nearest[0], y - nearest[1])
            events.append(
                {
                    "device_id": device_id,
                    "tap_x": x,
                    "tap_y": y,
                    "timestamp_ms": safe_float(parts[3], 0.0) or 0.0,
                    "hit_label": hit_label,
                    "target_x": nearest[0],
                    "target_y": nearest[1],
                    "target_radius": nearest[2],
                    "distance_to_target": distance,
                    "visual_boundary_correct": distance <= nearest[2],
                    "expanded_boundary_correct": distance <= nearest[2] * 1.25,
                    "distance_to_boundary": distance - nearest[2],
                }
            )
    return events


def evaluate_henze() -> list[dict[str, Any]]:
    ensure_public_dirs()
    root = dataset_root("henze")
    events: list[dict[str, Any]] = []
    for path in list(root.glob("manual/**/*.txt")) + list(root.glob("raw/**/*.txt")):
        try:
            events.extend(parse_henze_lines(path.read_text(encoding="utf-8", errors="ignore").splitlines()))
        except OSError:
            continue
    if not events:
        write_csv(REPORTS_DIR / "henze_target_selection_metrics.csv", [{"dataset_id": "henze", "status": "unavailable"}])
        placeholder_figure(FIGURES_DIR / "henze_hitbox_comparison.png", "Henze manual subset is not available locally.")
        return [{"dataset_id": "henze", "status": "unavailable"}]
    visual = sum(1 for row in events if row["visual_boundary_correct"]) / max(len(events), 1)
    expanded = sum(1 for row in events if row["expanded_boundary_correct"]) / max(len(events), 1)
    metrics = [
        {"dataset_id": "henze", "baseline": "visual_boundary", "n": len(events), "accuracy": visual},
        {"dataset_id": "henze", "baseline": "expanded_boundary", "n": len(events), "accuracy": expanded},
        {
            "dataset_id": "henze",
            "baseline": "target_radius_sensitivity",
            "n": len(events),
            "accuracy": statistics.mean([safe_float(row["distance_to_boundary"], 0.0) or 0.0 for row in events]),
        },
    ]
    write_csv(PROCESSED_DIR / "henze_taps.csv", events)
    write_csv(REPORTS_DIR / "henze_target_selection_metrics.csv", metrics)
    if plt is None:
        placeholder_figure(FIGURES_DIR / "henze_hitbox_comparison.png", "matplotlib unavailable; Henze metrics are in CSV.")
        return metrics
    plt.figure(figsize=(6, 4))
    plt.bar(["visual", "expanded"], [visual, expanded], color=["#2563eb", "#059669"])
    plt.ylim(0, 1)
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "henze_hitbox_comparison.png", dpi=150)
    plt.close()
    return metrics


def walk_rico_node(node: dict[str, Any], rows: list[dict[str, Any]]) -> None:
    bounds = node.get("bounds") or node.get("rel-bounds") or []
    class_name = str(node.get("class", ""))
    clickable = bool(node.get("clickable", False))
    text_values = node.get("text") or node.get("content-desc") or []
    if isinstance(text_values, list):
        text = " ".join(str(value) for value in text_values if value)
    else:
        text = str(text_values)
    if len(bounds) == 4:
        rows.append(
            {
                "dataset_id": "rico",
                "element_type": class_name.split(".")[-1] or "unknown",
                "button_like": clickable or "button" in class_name.lower(),
                "has_text": bool(text.strip()),
                "x1": bounds[0],
                "y1": bounds[1],
                "x2": bounds[2],
                "y2": bounds[3],
            }
        )
    for child in node.get("children", []) or []:
        if isinstance(child, dict):
            walk_rico_node(child, rows)


def parse_screen_annotation_label(label: str) -> list[dict[str, Any]]:
    rows = []
    for match in re.finditer(r"(button|icon|text|image|checkbox|switch|input)[^.\n]*", label, flags=re.IGNORECASE):
        rows.append({"dataset_id": "screen_annotation", "element_type": match.group(1).lower(), "button_like": match.group(1).lower() == "button", "has_text": "text" in match.group(0).lower()})
    return rows


def evaluate_ui_grounding() -> list[dict[str, Any]]:
    ensure_public_dirs()
    rows: list[dict[str, Any]] = []
    rico_root = dataset_root("rico")
    for path in list(rico_root.glob("manual/**/*.json")) + list(rico_root.glob("raw/**/*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8", errors="ignore"))
            root = data.get("activity", {}).get("root", data.get("root", data))
            if isinstance(root, dict):
                walk_rico_node(root, rows)
        except Exception:
            continue
    screen_root = dataset_root("screen_annotation")
    for path in list(screen_root.glob("raw/*.csv")) + list(screen_root.glob("manual/**/*.csv")):
        try:
            for row in read_csv_rows(path):
                rows.extend(parse_screen_annotation_label(str(row.get("label", ""))))
        except Exception:
            continue
    if not rows:
        write_csv(REPORTS_DIR / "ui_grounding_dataset_summary.csv", [{"status": "unavailable"}])
        placeholder_figure(FIGURES_DIR / "ui_component_box_examples.png", "Rico/Screen Annotation files are not available locally.")
        return [{"status": "unavailable"}]
    counts = Counter((str(row["dataset_id"]), str(row["element_type"]), str(row["button_like"])) for row in rows)
    summary = [
        {"dataset_id": dataset_id, "element_type": element_type, "button_like": button_like, "count": count}
        for (dataset_id, element_type, button_like), count in counts.items()
    ]
    write_csv(PROCESSED_DIR / "ui_grounding_elements.csv", rows)
    write_csv(REPORTS_DIR / "ui_grounding_dataset_summary.csv", summary)
    if plt is None:
        placeholder_figure(FIGURES_DIR / "ui_component_box_examples.png", "matplotlib unavailable; UI grounding metrics are in CSV.")
        return summary
    plt.figure(figsize=(8, 4))
    top = Counter(str(row["element_type"]) for row in rows).most_common(10)
    plt.bar([item[0] for item in top], [item[1] for item in top])
    plt.xticks(rotation=30, ha="right")
    plt.ylabel("count")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "ui_component_box_examples.png", dpi=150)
    plt.close()
    return summary


def build_all_public() -> None:
    ensure_public_dirs()
    inspect_public_datasets()
    build_touch_dynamics_outputs()
    evaluate_tsi()
    evaluate_henze()
    evaluate_ui_grounding()
    if not (MODELS_DIR / "public_touch_prior_config.json").exists():
        update_public_prior_config_from_tsi([], [])


def public_report_builder() -> None:
    ensure_public_dirs()
    lines = [
        "# Public Dataset Report",
        "",
        "This report separates external touch/UI evidence from direct Unity Attack/Dodge validation.",
        "",
    ]
    for spec in PUBLIC_DATASETS:
        stats = dataset_file_stats(spec)
        lines.extend(
            [
                f"## {spec.display_name}",
                f"- available: {dataset_available(spec)}",
                f"- observed_files: {stats['file_count']}",
                f"- observed_rows: {stats['row_count_observed']}",
                f"- use: {spec.evidence_role}",
                f"- limit: {spec.direct_validation_limit}",
                "",
            ]
        )
    lines.extend(
        [
            "## Interpretation Guardrails",
            "- Public game touch logs validate touch dynamics, not Attack/Dodge correction.",
            "- Public target-selection datasets validate touch-target modeling and hitbox behavior, not the game-state prior.",
            "- Public UI datasets support UI grounding, not game-specific combat context.",
            "- Unity controlled telemetry remains the primary direct validation source.",
        ]
    )
    (REPORTS_DIR / "public_dataset_report.md").write_text("\n".join(lines), encoding="utf-8")

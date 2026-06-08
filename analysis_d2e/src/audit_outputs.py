from __future__ import annotations

import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import read_jsonl, safe_float, write_csv, write_json
from analysis_d2e.src.paths import PROCESSED_DIR, REPORTS_DIR, d2e_root, ensure_dirs
from analysis_d2e.src.runtime_decode import decode_request
from analysis_d2e.src.schemas import PHASES, SUPPORTED_GAMES


def _raw_files_by_game(root: Path) -> dict[str, list[Path]]:
    out: dict[str, list[Path]] = defaultdict(list)
    if not root.exists():
        return out
    for path in root.rglob("*"):
        if not path.is_file() or ".cache" in path.parts:
            continue
        if path.suffix.lower() not in {".mcap", ".mkv", ".csv", ".json", ".jsonl"}:
            continue
        lowered = str(path).lower()
        matched = None
        for game in SUPPORTED_GAMES:
            token = game.lower().replace(" ", "_")
            if game.lower() in lowered or token in lowered:
                matched = game.replace(" ", "_")
                break
        out[matched or "unknown"].append(path)
    return out


def _mean_or_zero(values: list[float]) -> float:
    return sum(values) / max(1, len(values))


def build_primary_subset_audit(
    samples_path: Path | None = None,
    root: Path | None = None,
    write_outputs: bool = True,
) -> list[dict[str, Any]]:
    ensure_dirs()
    root = root or d2e_root()
    samples = read_jsonl(samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl"))
    raw_by_game = _raw_files_by_game(root)
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        grouped[str(sample.get("game_id", "unknown")).replace(" ", "_")].append(sample)

    game_ids = sorted(set(grouped) | set(raw_by_game))
    rows = []
    for game_id in game_ids:
        group = grouped.get(game_id, [])
        phase_counts = Counter(str(row.get("phase", "unknown")) for row in group)
        raw_files = raw_by_game.get(game_id, [])
        row = {
            "game_id": game_id,
            "role": "primary" if game_id.lower() == "barony" else "auxiliary",
            "raw_file_count": len(raw_files),
            "raw_size_mb": round(sum(path.stat().st_size for path in raw_files) / (1024 * 1024), 3),
            "processed_rows": len(group),
            "usable_action_rows": sum(1 for item in group if safe_float(item.get("action_label_confidence"), 0.0) >= 0.2),
            "no_action_or_low_confidence_rows": sum(1 for item in group if safe_float(item.get("action_label_confidence"), 0.0) < 0.2),
            "mean_action_label_confidence": round(_mean_or_zero([safe_float(item.get("action_label_confidence"), 0.0) for item in group]), 4),
            "phase_1_rows": phase_counts.get("phase_1", 0),
            "phase_2_rows": phase_counts.get("phase_2", 0),
            "phase_3_rows": phase_counts.get("phase_3", 0),
            "unknown_rows": phase_counts.get("unknown", 0),
            "primary_subset_ready": game_id.lower() == "barony" and sum(1 for item in group if safe_float(item.get("action_label_confidence"), 0.0) >= 0.2) > 0,
        }
        rows.append(row)
    if write_outputs:
        write_csv(REPORTS_DIR / "d2e_primary_subset_audit.csv", rows)
    return rows


def build_phase_coverage_audit(samples_path: Path | None = None, write_outputs: bool = True) -> list[dict[str, Any]]:
    ensure_dirs()
    samples = read_jsonl(samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl"))
    rows = []
    for phase in PHASES:
        group = [sample for sample in samples if sample.get("phase") == phase]
        source_counts = Counter(str(sample.get("phase_source", "unknown")) for sample in group)
        rows.append(
            {
                "phase": phase,
                "rows": len(group),
                "usable_action_rows": sum(1 for item in group if safe_float(item.get("action_label_confidence"), 0.0) >= 0.2),
                "provided_rows": source_counts.get("provided", 0),
                "weak_action_proxy_rows": source_counts.get("weak_action_proxy", 0),
                "metadata_heuristic_rows": source_counts.get("metadata_heuristic", 0),
                "requires_manual_annotation": source_counts.get("provided", 0) == 0,
            }
        )
    if write_outputs:
        write_csv(REPORTS_DIR / "d2e_phase_coverage_audit.csv", rows)
    return rows


def build_runtime_example() -> dict[str, Any]:
    ensure_dirs()
    request = {
        "n_buttons": 4,
        "atomic_distribution": {"attack": 0.12, "defense": 0.22, "dodge": 0.42, "skill": 0.08, "heal": 0.08, "escape": 0.08},
        "situation": {"phase": "phase_3", "player_hp_norm": 0.55, "melee_enemy_count": 4, "ranged_enemy_count": 1, "boss_visible": True, "boss_telegraph": True},
        "button_layout": [
            {"button_id": "attack", "action": "attack", "center_x": 100, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "defense", "action": "defense", "center_x": 180, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "skill", "action": "skill", "center_x": 260, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
            {"button_id": "context", "action": "context", "center_x": 340, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
        ],
        "touch": {"x": 220, "y": 200},
        "touch_profile": {"touch_variance": 700, "near_miss_rate": 0.14},
        "skill_profile": {
            "skill_level": "beginner",
            "reaction_time_mean": 430,
            "reinput_rate": 0.18,
            "joystick_drift": 0.20,
            "cooldown_misuse_rate": 0.20,
            "context_button_understanding": 0.35,
        },
        "use_skill": True,
    }
    response = decode_request(request, model=None)
    write_json(REPORTS_DIR / "d2e_runtime_example_request.json", request)
    write_json(REPORTS_DIR / "d2e_runtime_example_response.json", response)
    return response


def build_all_audits() -> dict[str, Any]:
    return {
        "primary_subset_rows": build_primary_subset_audit(),
        "phase_coverage_rows": build_phase_coverage_audit(),
        "runtime_example_response": build_runtime_example(),
    }


def main() -> None:
    result = build_all_audits()
    print(json.dumps({"status": "ok", "primary_rows": len(result["primary_subset_rows"]), "phase_rows": len(result["phase_coverage_rows"])}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

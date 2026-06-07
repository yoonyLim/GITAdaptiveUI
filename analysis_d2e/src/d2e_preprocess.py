from __future__ import annotations

import argparse
import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.action_mapping import action_distribution_from_raw_input, action_label_confidence, dominant_atomic_action
from analysis_d2e.src.io_utils import read_csv, read_jsonl, safe_float, write_csv, write_jsonl
from analysis_d2e.src.paths import FIGURES_DIR, PROCESSED_DIR, REPORTS_DIR, d2e_root, ensure_dirs
from analysis_d2e.src.schemas import SUPPORTED_GAMES


FRAME_FIELDS = ("frame_path", "image_path", "image", "path", "file", "screenshot")
INPUT_FIELDS = ("raw_input", "input", "inputs", "keys", "keyboard", "mouse", "mouse_buttons", "action")


def infer_game_id(path: Path, row: dict[str, Any]) -> str:
    for key in ("game_id", "game", "game_name", "title"):
        if row.get(key):
            return str(row[key])
    lowered = str(path).lower()
    for game in SUPPORTED_GAMES:
        if game.lower() in lowered:
            return game
    return "unknown"


def infer_phase(row: dict[str, Any], atomic: dict[str, float] | None = None, label_confidence: float = 0.0) -> tuple[str, str]:
    if row.get("phase"):
        phase = str(row["phase"]).lower().replace(" ", "_")
        if phase in {"phase_1", "phase_2", "phase_3"}:
            return phase, "provided"
    raw = " ".join(str(row.get(key, "")) for key in row)
    if re.search(r"boss|telegraph", raw, flags=re.IGNORECASE):
        return "phase_3", "metadata_heuristic"
    if re.search(r"low[_ ]?hp|heal|ranged", raw, flags=re.IGNORECASE):
        return "phase_2", "metadata_heuristic"
    atomic = atomic or {}
    if label_confidence <= 0.0:
        return "unknown", "unavailable"
    if float(atomic.get("heal", 0.0)) >= 0.20 or float(atomic.get("escape", 0.0)) >= 0.30:
        return "phase_2", "weak_action_proxy"
    if float(atomic.get("dodge", 0.0)) >= 0.20 or float(atomic.get("defense", 0.0)) >= 0.30:
        return "phase_3", "weak_action_proxy"
    return "phase_1", "weak_action_proxy"


def frame_path_from_row(manifest_path: Path, row: dict[str, Any]) -> Path | None:
    for field in FRAME_FIELDS:
        value = row.get(field)
        if value:
            candidate = Path(str(value))
            if not candidate.is_absolute():
                candidate = manifest_path.parent / candidate
            return candidate
    return None


def raw_input_from_row(row: dict[str, Any]) -> dict[str, Any] | str:
    merged: dict[str, Any] = {}
    for field in INPUT_FIELDS:
        value = row.get(field)
        if value is None:
            continue
        if isinstance(value, str) and value == "":
            continue
        merged[field] = value
    if len(merged) == 1:
        return next(iter(merged.values()))
    return merged


def read_manifest(path: Path) -> list[dict[str, Any]]:
    if path.suffix.lower() == ".csv":
        return read_csv(path)
    if path.suffix.lower() == ".jsonl":
        return read_jsonl(path)
    if path.suffix.lower() == ".json":
        data = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(data, list):
            return [dict(row) for row in data if isinstance(row, dict)]
        if isinstance(data, dict) and isinstance(data.get("samples"), list):
            return [dict(row) for row in data["samples"] if isinstance(row, dict)]
    return []


def discover_manifests(root: Path) -> list[Path]:
    if not root.exists():
        return []
    candidates = []
    for path in root.rglob("*"):
        if path.suffix.lower() not in {".csv", ".jsonl", ".json", ".mcap"}:
            continue
        if path.suffix.lower() == ".mcap":
            candidates.append(path)
            continue
        name = path.name.lower()
        if any(token in name for token in ("manifest", "input", "action", "d2e", "frame", "log")):
            candidates.append(path)
    return sorted(candidates)


def read_mcap_as_rows(path: Path, max_samples_per_mcap: int, frame_stride: int) -> list[dict[str, Any]]:
    try:
        from analysis_d2e.src.d2e_owa_loader import extract_owa_samples

        output_dir = FIGURES_DIR / "d2e_extracted_frames"
        return extract_owa_samples(path, output_dir, max_samples=max_samples_per_mcap, frame_stride=frame_stride)
    except Exception as exc:  # noqa: BLE001
        write_csv(
            REPORTS_DIR / "d2e_mcap_unavailable.csv",
            [
                {
                    "mcap_file": str(path),
                    "status": "unavailable",
                    "error": f"{type(exc).__name__}: {str(exc)[:300]}",
                }
            ],
        )
        return []


def build_windows(rows: list[dict[str, Any]], history_len: int) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        grouped[(str(row.get("game_id", "unknown")), str(row.get("episode_id", "default")))].append(row)
    out = []
    for (_game, _episode), group in grouped.items():
        group = sorted(group, key=lambda item: int(safe_float(item.get("frame_idx"), 0)))
        history: list[str] = []
        for row in group:
            history.append(str(row["frame_path"]))
            row["history_frame_paths"] = history[-history_len:]
            out.append(row)
    return out


def preprocess_d2e(
    root: Path | None = None,
    history_len: int = 8,
    max_samples_per_mcap: int = 500,
    frame_stride: int = 30,
    write_outputs: bool = True,
) -> list[dict[str, Any]]:
    ensure_dirs()
    root = root or d2e_root()
    manifests = discover_manifests(root)
    records: list[dict[str, Any]] = []
    for manifest in manifests:
        manifest_rows = (
            read_mcap_as_rows(manifest, max_samples_per_mcap=max_samples_per_mcap, frame_stride=frame_stride)
            if manifest.suffix.lower() == ".mcap"
            else read_manifest(manifest)
        )
        for idx, row in enumerate(manifest_rows):
            frame = frame_path_from_row(manifest, row)
            if frame is None or not frame.exists():
                continue
            raw_input = raw_input_from_row(row)
            atomic = action_distribution_from_raw_input(raw_input)
            label_confidence = action_label_confidence(raw_input)
            game_id = infer_game_id(manifest, row)
            phase, phase_source = infer_phase(row, atomic, label_confidence)
            records.append(
                {
                    "sample_id": f"{game_id}_{manifest.stem}_{idx:06d}",
                    "dataset_id": "D2E-480p",
                    "source_type": "real_d2e" if "fixture" not in str(root).lower() else "synthetic_fixture",
                    "game_id": game_id,
                    "is_primary_subset": game_id.lower() == "barony",
                    "phase": phase,
                    "phase_source": phase_source,
                    "episode_id": str(row.get("episode_id") or row.get("video_id") or manifest.stem),
                    "frame_idx": int(safe_float(row.get("frame_idx") or row.get("frame") or idx, idx)),
                    "frame_path": str(frame.resolve()),
                    "raw_input": raw_input,
                    "atomic_distribution": atomic,
                    "dominant_atomic_action": dominant_atomic_action(atomic),
                    "action_label_confidence": label_confidence,
                    "has_action_input": label_confidence > 0.0,
                    "player_hp_norm": safe_float(row.get("player_hp_norm") or row.get("hp_norm"), 1.0),
                    "melee_enemy_count": safe_float(row.get("melee_enemy_count"), 0.0),
                    "ranged_enemy_count": safe_float(row.get("ranged_enemy_count"), 0.0),
                    "boss_visible": str(row.get("boss_visible", "")).lower() in {"true", "1", "yes"},
                    "boss_telegraph": str(row.get("boss_telegraph", "")).lower() in {"true", "1", "yes"},
                    "source_manifest": str(manifest.resolve()),
                }
            )
    records = build_windows(records, history_len)
    if not write_outputs:
        return records
    write_jsonl(PROCESSED_DIR / "d2e_action_prior_samples.jsonl", records)
    counts = Counter((row["game_id"], row["phase"], row["phase_source"], row["source_type"]) for row in records)
    summary = [
        {"game_id": game, "phase": phase, "phase_source": phase_source, "source_type": source, "rows": count}
        for (game, phase, phase_source, source), count in sorted(counts.items())
    ]
    write_csv(REPORTS_DIR / "d2e_preprocess_summary.csv", summary)
    availability = [
        "# D2E-480p availability",
        "",
        f"- root: `{root}`",
        f"- manifest_count: {len(manifests)}",
        f"- processed_rows: {len(records)}",
        f"- primary_subset_Barony_rows: {sum(1 for row in records if row['is_primary_subset'])}",
        "",
        "D2E-480p is used for P(action | environment). It is not used as mobile touch-skill data.",
    ]
    (REPORTS_DIR / "d2e_availability.md").write_text("\n".join(availability), encoding="utf-8")
    return records


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=None)
    parser.add_argument("--history-len", type=int, default=8)
    parser.add_argument("--max-samples-per-mcap", type=int, default=500)
    parser.add_argument("--frame-stride", type=int, default=30)
    args = parser.parse_args()
    rows = preprocess_d2e(args.root, args.history_len, args.max_samples_per_mcap, args.frame_stride)
    print(f"processed_rows={len(rows)}")
    print(PROCESSED_DIR / "d2e_action_prior_samples.jsonl")


if __name__ == "__main__":
    main()

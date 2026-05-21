from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

from analysis_multigame_scene.src.common import REPORTS_DIR, load_scene_samples, read_jsonl, safe_float, write_csv
from analysis_multigame_scene.src.paths import DATA_DIR, ensure_scene_dirs


COGNITION_TERMS = [
    "menu",
    "map",
    "inventory",
    "dialog",
    "dialogue",
    "quest",
    "objective",
    "tutorial",
    "score",
    "result",
    "shop",
    "character",
    "standing",
    "landscape",
    "city",
    "room",
    "text",
    "select",
    "selection",
    "options",
    "cutscene",
    "card",
]

GUIDANCE_TERMS = [
    "map",
    "quest",
    "objective",
    "route",
    "navigation",
    "mission",
    "checkpoint",
    "timer",
    "minimap",
    "path",
]

LOW_ACTION_GAMES = {
    "Among Us": 2.0,
    "Minecraft": 1.7,
    "Roblox": 1.4,
    "Genshin Impact": 1.2,
    "Terraria": 1.1,
    "Forza Horizon": 1.0,
}


def score_sample(sample: dict[str, Any]) -> tuple[float, str]:
    text = " ".join(
        str(sample.get(key, ""))
        for key in ["caption", "game_name", "genre", "scenario_name", "weak_action_window", "weak_threat_level"]
    ).lower()
    score = 0.0
    reasons: list[str] = []
    cognition_hits = [term for term in COGNITION_TERMS if term in text]
    guidance_hits = [term for term in GUIDANCE_TERMS if term in text]
    if cognition_hits:
        score += 3.0 + min(3, len(cognition_hits)) * 0.5
        reasons.append("cognition_terms=" + "|".join(cognition_hits[:4]))
    if guidance_hits:
        score += 2.5 + min(2, len(guidance_hits)) * 0.5
        reasons.append("guidance_terms=" + "|".join(guidance_hits[:3]))
    game_bonus = LOW_ACTION_GAMES.get(str(sample.get("game_name")), 0.0)
    if game_bonus:
        score += game_bonus
        reasons.append(f"low_action_game={sample.get('game_name')}")
    if sample.get("weak_action_window") in {"wait", "explore", "unknown"}:
        score += 1.0
        reasons.append(f"weak_action_window={sample.get('weak_action_window')}")
    if sample.get("weak_threat_level") in {"none", "warning", "unknown"}:
        score += 0.8
        reasons.append(f"weak_threat={sample.get('weak_threat_level')}")
    if safe_float(sample.get("weak_temporal_urgency"), 0.5) <= 0.35:
        score += 0.8
        reasons.append("low_temporal_urgency")
    if safe_float(sample.get("weak_information_priority"), 0.0) >= 0.60:
        score += 0.6
        reasons.append("high_information_priority")
    if sample.get("source_dataset") == "gameplay_captions" and not cognition_hits:
        score -= 0.4
    if sample.get("source_dataset") in {"dota2_event_extraction_video", "vizdoom_generated", "bleeding_edge_gameplay_sample"}:
        score -= 2.0
    if sample.get("source_dataset") == "atari_head":
        score -= 0.8
    return score, ";".join(reasons)


def select_samples(max_samples: int) -> list[dict[str, Any]]:
    labels = read_jsonl(DATA_DIR / "teacher_labels" / "teacher_labels.jsonl")
    labeled_ids = {str(label.get("sample_id")) for label in labels}
    candidates: list[dict[str, Any]] = []
    for sample in load_scene_samples():
        sample_id = str(sample.get("sample_id"))
        frame = Path(str(sample.get("frame_path", "")))
        if sample_id in labeled_ids or not frame.is_file():
            continue
        if sample.get("source_type") not in {"actual_gameplay_image_frame", "actual_gameplay_video_frame", "vizdoom_runtime"}:
            continue
        score, reason = score_sample(sample)
        if score <= 0:
            continue
        candidates.append({**sample, "target_score": round(score, 4), "target_reason": reason})
    candidates.sort(key=lambda row: (-safe_float(row.get("target_score")), str(row.get("source_dataset")), str(row.get("sample_id"))))
    selected: list[dict[str, Any]] = []
    source_counts: dict[str, int] = {}
    game_counts: dict[str, int] = {}
    for row in candidates:
        source = str(row.get("source_dataset"))
        game = str(row.get("game_name"))
        source_limit = max(8, max_samples // 2)
        game_limit = max(4, max_samples // 5)
        if source_counts.get(source, 0) >= source_limit:
            continue
        if game_counts.get(game, 0) >= game_limit:
            continue
        selected.append(row)
        source_counts[source] = source_counts.get(source, 0) + 1
        game_counts[game] = game_counts.get(game, 0) + 1
        if len(selected) >= max_samples:
            break
    return selected


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-samples", type=int, default=60)
    parser.add_argument("--output", default=str(DATA_DIR / "teacher_labels" / "targeted_non_action_sample_ids.txt"))
    args = parser.parse_args()
    ensure_scene_dirs()
    selected = select_samples(args.max_samples)
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(str(row.get("sample_id")) for row in selected) + ("\n" if selected else ""), encoding="utf-8")
    write_csv(
        REPORTS_DIR / "targeted_non_action_sample_selection.csv",
        [
            {
                "sample_id": row.get("sample_id"),
                "source_dataset": row.get("source_dataset"),
                "game_name": row.get("game_name"),
                "scenario_name": row.get("scenario_name"),
                "target_score": row.get("target_score"),
                "target_reason": row.get("target_reason"),
                "caption": str(row.get("caption", ""))[:240],
            }
            for row in selected
        ],
    )
    print(f"targeted_samples={len(selected)}")
    print(out)


if __name__ == "__main__":
    main()

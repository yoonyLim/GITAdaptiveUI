from __future__ import annotations

import argparse
import json
import random
from collections import Counter
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import write_csv, write_jsonl
from analysis_d2e.src.paths import PROCESSED_DIR, REPORTS_DIR, ensure_dirs


SYNTHETIC_ENVS = ("procgen_bossfight", "procgen_chaser", "procgen_dodgeball", "craftax_combat", "crafter_combat")


def _label(row: dict[str, Any]) -> str:
    if row["projectile_visible"] or (row["enemy_approaching"] and row["nearest_enemy_distance"] < 0.28) or row["player_hp_norm"] <= 0.25:
        return "explicit_defense"
    if row["enemy_count"] > 0 or row["boss_vulnerable"]:
        return "offense"
    return "movement_only"


def generate_synthetic_state_rows(rows_per_env: int = 160, seed: int = 7) -> list[dict[str, Any]]:
    rng = random.Random(seed)
    rows = []
    for env in SYNTHETIC_ENVS:
        for idx in range(rows_per_env):
            enemy_count = rng.randint(0, 12)
            nearest = rng.random()
            approaching = rng.random() < (0.65 if env in {"procgen_chaser", "craftax_combat"} else 0.30)
            projectile = rng.random() < (0.55 if env == "procgen_dodgeball" else 0.18)
            boss_vulnerable = env == "procgen_bossfight" and rng.random() < 0.35
            hp = rng.uniform(0.15, 1.0)
            row = {
                "sample_id": f"{env}_{idx:05d}",
                "source_env": env,
                "enemy_count": enemy_count,
                "nearest_enemy_distance": round(nearest, 4),
                "enemy_approaching": approaching,
                "nearby_enemy_count": sum(1 for _ in range(enemy_count) if rng.random() < max(0.05, 1.0 - nearest)),
                "projectile_visible": projectile,
                "boss_vulnerable": boss_vulnerable,
                "player_hp_norm": round(hp, 4),
            }
            row["event_label"] = _label(row)
            rows.append(row)
    return rows


def _predict(row: dict[str, Any], policy: str, majority: str) -> str:
    if policy == "majority":
        return majority
    if policy == "density_only":
        if int(row["enemy_count"]) >= 8:
            return "explicit_defense"
        if int(row["enemy_count"]) > 0:
            return "offense"
        return "movement_only"
    if policy == "density_approach":
        if row["enemy_approaching"] and float(row["nearest_enemy_distance"]) < 0.35:
            return "explicit_defense"
        if int(row["enemy_count"]) > 0:
            return "offense"
        return "movement_only"
    if policy == "projectile_added":
        if row["projectile_visible"] or (row["enemy_approaching"] and float(row["nearest_enemy_distance"]) < 0.35):
            return "explicit_defense"
        if int(row["enemy_count"]) > 0:
            return "offense"
        return "movement_only"
    if policy == "full_state":
        return _label(row)
    raise ValueError(policy)


def run_synthetic_state_validation(rows_per_env: int = 160, seed: int = 7, write_outputs: bool = True) -> list[dict[str, Any]]:
    ensure_dirs()
    rows = generate_synthetic_state_rows(rows_per_env, seed)
    if write_outputs:
        write_jsonl(PROCESSED_DIR / "synthetic_state_validation_samples.jsonl", rows)
    counts = Counter(row["event_label"] for row in rows)
    majority = counts.most_common(1)[0][0]
    policies = ["majority", "density_only", "density_approach", "projectile_added", "full_state"]
    report = []
    for policy in policies:
        correct = sum(1 for row in rows if _predict(row, policy, majority) == row["event_label"])
        report.append(
            {
                "validation_source": "procgen_craftax_clean_state_fixture",
                "policy": policy,
                "rows": len(rows),
                "accuracy": correct / max(1, len(rows)),
                "offense_rows": counts.get("offense", 0),
                "explicit_defense_rows": counts.get("explicit_defense", 0),
                "movement_only_rows": counts.get("movement_only", 0),
                "interpretation": "clean ablation; not real D2E performance",
            }
        )
    if write_outputs:
        write_csv(REPORTS_DIR / "synthetic_state_ablation.csv", report)
    return report


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rows-per-env", type=int, default=160)
    parser.add_argument("--seed", type=int, default=7)
    args = parser.parse_args()
    print(json.dumps(run_synthetic_state_validation(args.rows_per_env, args.seed), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

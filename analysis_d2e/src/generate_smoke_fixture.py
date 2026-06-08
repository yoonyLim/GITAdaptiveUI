from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

from analysis_d2e.src.paths import FIXTURE_DIR, ensure_dirs
from analysis_d2e.src.schemas import SUPPORTED_GAMES


def _phase_input(phase: str, idx: int) -> dict[str, object]:
    if phase == "phase_1":
        keys = ["mouse_left", "q"] if idx % 3 else ["space"]
        return {"keys": keys, "melee_enemy_count": 14, "player_hp_norm": 0.75}
    if phase == "phase_2":
        keys = ["h"] if idx % 2 == 0 else ["space", "mouse_left"]
        return {"keys": keys, "ranged_enemy_count": 4, "player_hp_norm": 0.28}
    keys = ["space"] if idx % 2 == 0 else ["mouse_right"]
    return {"keys": keys, "boss_visible": True, "boss_telegraph": True, "player_hp_norm": 0.55}


def generate_fixture(root: Path | None = None, rows_per_game_phase: int = 8) -> Path:
    ensure_dirs()
    root = root or (FIXTURE_DIR / "d2e_smoke")
    frames = root / "frames"
    frames.mkdir(parents=True, exist_ok=True)
    manifest = root / "d2e_smoke_manifest.csv"
    from PIL import Image, ImageDraw

    rows = []
    for game_idx, game in enumerate(SUPPORTED_GAMES):
        for phase_idx, phase in enumerate(("phase_1", "phase_2", "phase_3")):
            for idx in range(rows_per_game_phase):
                image_path = frames / f"{game.replace(' ', '_')}_{phase}_{idx:03d}.png"
                color = (
                    45 + game_idx * 30,
                    45 + phase_idx * 70,
                    80 + idx * 10 % 120,
                )
                image = Image.new("RGB", (480, 270), color=color)
                draw = ImageDraw.Draw(image)
                if phase == "phase_1":
                    for enemy in range(12):
                        draw.ellipse((20 + enemy * 35 % 430, 30 + enemy * 19 % 210, 35 + enemy * 35 % 430, 45 + enemy * 19 % 210), fill=(180, 40, 40))
                elif phase == "phase_2":
                    for enemy in range(4):
                        draw.rectangle((330 + enemy * 22, 50 + enemy * 35, 345 + enemy * 22, 65 + enemy * 35), fill=(220, 180, 40))
                    draw.rectangle((20, 240, 120, 255), fill=(160, 20, 20))
                else:
                    draw.rectangle((360, 30, 470, 120), outline=(250, 250, 250), width=5)
                    draw.line((250, 120, 430, 80), fill=(255, 80, 80), width=4)
                image.save(image_path)
                inputs = _phase_input(phase, idx)
                rows.append(
                    {
                        "frame_path": str(image_path.relative_to(root)),
                        "game_id": game,
                        "phase": phase,
                        "episode_id": f"{game}_{phase}",
                        "frame_idx": idx,
                        "raw_input": json.dumps(inputs["keys"]),
                        "player_hp_norm": inputs.get("player_hp_norm", 1.0),
                        "melee_enemy_count": inputs.get("melee_enemy_count", 0),
                        "ranged_enemy_count": inputs.get("ranged_enemy_count", 0),
                        "boss_visible": inputs.get("boss_visible", False),
                        "boss_telegraph": inputs.get("boss_telegraph", False),
                    }
                )
    with manifest.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    return root


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rows-per-game-phase", type=int, default=8)
    args = parser.parse_args()
    root = generate_fixture(rows_per_game_phase=args.rows_per_game_phase)
    print(root)


if __name__ == "__main__":
    main()


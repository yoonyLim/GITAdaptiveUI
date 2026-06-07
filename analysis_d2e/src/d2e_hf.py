from __future__ import annotations


D2E_REPO_ID = "open-world-agents/D2E-480p"
D2E_REPO_TYPE = "dataset"
PRIMARY_GAME = "Barony"
AUXILIARY_GAMES = ("Brotato", "Skul", "Core_Keeper", "Vampire_Survivors")
TARGET_GAMES = (PRIMARY_GAME, *AUXILIARY_GAMES)


def normalize_game_name(name: str) -> str:
    return name.replace(" ", "_").replace("-", "_")


def is_target_game_path(path: str, games: tuple[str, ...] = TARGET_GAMES) -> bool:
    first = path.split("/", 1)[0]
    return normalize_game_name(first).lower() in {normalize_game_name(game).lower() for game in games}


def paired_video_path(mcap_path: str) -> str:
    if not mcap_path.endswith(".mcap"):
        raise ValueError("mcap_path must end with .mcap")
    return mcap_path[:-5] + ".mkv"


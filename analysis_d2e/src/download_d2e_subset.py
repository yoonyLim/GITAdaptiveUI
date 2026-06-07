from __future__ import annotations

import argparse
from collections import defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.d2e_hf import D2E_REPO_ID, D2E_REPO_TYPE, TARGET_GAMES, is_target_game_path, paired_video_path
from analysis_d2e.src.io_utils import write_csv
from analysis_d2e.src.paths import RAW_DIR, REPORTS_DIR, ensure_dirs


def import_hf() -> Any:
    try:
        from huggingface_hub import HfApi, hf_hub_download

        return HfApi, hf_hub_download
    except ModuleNotFoundError as exc:
        raise RuntimeError("Install huggingface_hub to download D2E: uv run --with huggingface_hub ...") from exc


def _size_from_info(info: Any) -> int | None:
    size = getattr(info, "size", None)
    if size is not None:
        try:
            return int(size)
        except Exception:
            pass
    lfs = getattr(info, "lfs", None)
    if isinstance(lfs, dict) and lfs.get("size") is not None:
        try:
            return int(lfs["size"])
        except Exception:
            pass
    return None


def fetch_file_sizes(api: Any, paths: list[str]) -> dict[str, int | None]:
    sizes: dict[str, int | None] = {path: None for path in paths}
    if not paths:
        return sizes
    try:
        infos = api.get_paths_info(repo_id=D2E_REPO_ID, paths=paths, repo_type=D2E_REPO_TYPE)
        for info in infos:
            path = getattr(info, "path", None) or getattr(info, "rfilename", None)
            if path in sizes:
                sizes[path] = _size_from_info(info)
    except Exception:
        return sizes
    return sizes


def size_mb(size_bytes: int | None) -> float | None:
    return None if size_bytes is None else round(size_bytes / (1024 * 1024), 2)


def select_recordings(files: list[str], games: tuple[str, ...], max_recordings_per_game: int) -> list[str]:
    grouped: dict[str, list[str]] = defaultdict(list)
    wanted = {game.lower().replace(" ", "_"): game for game in games}
    for path in sorted(files):
        if not path.endswith(".mcap") or not is_target_game_path(path, games):
            continue
        game_key = path.split("/", 1)[0].lower().replace(" ", "_")
        grouped[wanted.get(game_key, path.split("/", 1)[0])].append(path)
    selected = []
    for game in games:
        selected.extend(grouped.get(game, [])[:max_recordings_per_game])
    return selected


def download_subset(
    output_root: Path | None = None,
    games: tuple[str, ...] = TARGET_GAMES,
    max_recordings_per_game: int = 1,
    include_video: bool = True,
    dry_run: bool = False,
    max_file_mb: float | None = None,
    write_status: bool = True,
) -> list[dict[str, Any]]:
    ensure_dirs()
    output_root = output_root or (RAW_DIR / "d2e_480p")
    output_root.mkdir(parents=True, exist_ok=True)
    HfApi, hf_hub_download = import_hf()
    api = HfApi()
    files = api.list_repo_files(repo_id=D2E_REPO_ID, repo_type=D2E_REPO_TYPE)
    selected_mcaps = select_recordings(files, games, max_recordings_per_game)
    selected_files: list[str] = []
    for mcap_path in selected_mcaps:
        selected_files.append(mcap_path)
        if include_video and paired_video_path(mcap_path) in files:
            selected_files.append(paired_video_path(mcap_path))
    sizes = fetch_file_sizes(api, selected_files)
    rows: list[dict[str, Any]] = []
    for mcap_path in selected_mcaps:
        paths = [mcap_path]
        if include_video and paired_video_path(mcap_path) in files:
            paths.append(paired_video_path(mcap_path))
        for repo_file in paths:
            current_size_mb = size_mb(sizes.get(repo_file))
            if max_file_mb is not None and current_size_mb is not None and current_size_mb > max_file_mb:
                rows.append(
                    {
                        "repo_id": D2E_REPO_ID,
                        "repo_file": repo_file,
                        "local_path": str(output_root / repo_file),
                        "game_id": repo_file.split("/", 1)[0],
                        "status": "skipped_size_limit",
                        "size_bytes": sizes.get(repo_file),
                        "size_mb": current_size_mb,
                        "max_file_mb": max_file_mb,
                    }
                )
                continue
            if dry_run:
                rows.append(
                    {
                        "repo_id": D2E_REPO_ID,
                        "repo_file": repo_file,
                        "local_path": str(output_root / repo_file),
                        "game_id": repo_file.split("/", 1)[0],
                        "status": "dry_run_selected",
                        "size_bytes": sizes.get(repo_file),
                        "size_mb": current_size_mb,
                    }
                )
                continue
            try:
                local = hf_hub_download(
                    repo_id=D2E_REPO_ID,
                    repo_type=D2E_REPO_TYPE,
                    filename=repo_file,
                    local_dir=output_root,
                    local_dir_use_symlinks=False,
                )
                rows.append(
                    {
                        "repo_id": D2E_REPO_ID,
                        "repo_file": repo_file,
                        "local_path": local,
                        "game_id": repo_file.split("/", 1)[0],
                        "status": "downloaded",
                        "size_bytes": sizes.get(repo_file),
                        "size_mb": current_size_mb,
                    }
                )
            except Exception as exc:  # noqa: BLE001
                rows.append(
                    {
                        "repo_id": D2E_REPO_ID,
                        "repo_file": repo_file,
                        "local_path": "",
                        "game_id": repo_file.split("/", 1)[0],
                        "status": "failed",
                        "size_bytes": sizes.get(repo_file),
                        "size_mb": current_size_mb,
                        "error": f"{type(exc).__name__}: {str(exc)[:240]}",
                    }
                )
    if write_status:
        write_csv(REPORTS_DIR / "d2e_hf_subset_download_status.csv", rows)
    return rows


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-recordings-per-game", type=int, default=1)
    parser.add_argument("--games", nargs="*", default=list(TARGET_GAMES))
    parser.add_argument("--no-video", action="store_true", help="Download only .mcap files. Frame decoding requires the paired .mkv later.")
    parser.add_argument("--dry-run", action="store_true", help="List selected files without downloading them.")
    parser.add_argument("--max-file-mb", type=float, default=None, help="Skip files larger than this size.")
    parser.add_argument("--output-root", type=Path, default=None)
    args = parser.parse_args()
    rows = download_subset(
        output_root=args.output_root,
        games=tuple(args.games),
        max_recordings_per_game=args.max_recordings_per_game,
        include_video=not args.no_video,
        dry_run=args.dry_run,
        max_file_mb=args.max_file_mb,
        write_status=True,
    )
    print(f"download_rows={len(rows)}")
    print(REPORTS_DIR / "d2e_hf_subset_download_status.csv")


if __name__ == "__main__":
    main()

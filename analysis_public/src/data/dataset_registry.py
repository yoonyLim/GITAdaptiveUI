from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from analysis_public.src.paths import DATA_DIR


@dataclass(frozen=True)
class PublicDatasetSpec:
    dataset_id: str
    display_name: str
    source_url: str
    evidence_role: str
    direct_validation_limit: str
    expected_paths: tuple[str, ...] = field(default_factory=tuple)
    auto_download_urls: tuple[str, ...] = field(default_factory=tuple)


PUBLIC_DATASETS: tuple[PublicDatasetSpec, ...] = (
    PublicDatasetSpec(
        dataset_id="touch_dynamics",
        display_name="Touch-Dynamics-Research",
        source_url="https://github.com/Brprb08/Touch-Dynamics-Research",
        evidence_role="Real mobile-game touch dynamics and user variation evidence.",
        direct_validation_limit="No Unity Attack/Dodge labels, button layout, or game-state prior labels.",
        expected_paths=(
            "raw/diep_raw_data.zip",
            "raw/mc_raw_data.zip",
            "raw/pubg_raw_data.zip",
            "raw/snake_raw_data.zip",
            "processed/touch_dynamics_events.parquet",
        ),
        auto_download_urls=(
            "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/diep_raw_data.zip",
            "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/mc_raw_data.zip",
            "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/pubg_raw_data.zip",
            "https://github.com/Brprb08/Touch-Dynamics-Research/raw/main/snake_raw_data.zip",
        ),
    ),
    PublicDatasetSpec(
        dataset_id="mc_snake",
        display_name="MC-Snake-Results",
        source_url="https://github.com/zderidder/MC-Snake-Results",
        evidence_role="Secondary mobile-game touch dynamics source for Minecraft/Snake behavior.",
        direct_validation_limit="No direct Unity Attack/Dodge correction labels or combat-state prior labels.",
        expected_paths=("raw/main.zip", "processed/mc_snake_events.parquet"),
        auto_download_urls=("https://github.com/zderidder/MC-Snake-Results/archive/refs/heads/main.zip",),
    ),
    PublicDatasetSpec(
        dataset_id="tsi",
        display_name="Google TSI",
        source_url="https://github.com/google-research-datasets/tap-typing-with-touch-sensing-images",
        evidence_role="Target-labeled touch benchmark for Gaussian touch modeling and hitbox sanity checks.",
        direct_validation_limit="Keyboard tapping data, not game combat context or Attack/Dodge validation.",
        expected_paths=(
            "raw/touch_data.csv",
            "raw/keyboard_data.json",
            "raw/prompt_data.csv",
            "processed/tsi_touch_targets.parquet",
        ),
        auto_download_urls=(
            "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/touch_data.csv",
            "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/keyboard_data.json",
            "https://raw.githubusercontent.com/google-research-datasets/tap-typing-with-touch-sensing-images/main/prompt_data.csv",
        ),
    ),
    PublicDatasetSpec(
        dataset_id="henze",
        display_name="Henze / Hit It / 100M Taps",
        source_url="https://nhenze.net/data/touch-events-on-mobile-phones/",
        evidence_role="Optional target-selection and expanded-boundary benchmark.",
        direct_validation_limit="Target tapping only; no game-state prior or Attack/Dodge labels.",
        expected_paths=("manual", "raw", "processed/henze_taps.parquet"),
    ),
    PublicDatasetSpec(
        dataset_id="rico",
        display_name="Rico",
        source_url="https://www.interactionmining.org/rico",
        evidence_role="Optional mobile UI element grounding and button-like component distribution.",
        direct_validation_limit="Mobile app UI layouts, not game combat-context validation.",
        expected_paths=("manual", "raw", "processed/rico_ui_elements.parquet"),
    ),
    PublicDatasetSpec(
        dataset_id="screen_annotation",
        display_name="Screen Annotation Dataset",
        source_url="https://github.com/google-research-datasets/screen_annotation",
        evidence_role="Optional screen/UI annotation support for UI grounding.",
        direct_validation_limit="UI/screen understanding only, not Unity game-state or Attack/Dodge validation.",
        expected_paths=("raw", "manual", "processed/screen_annotation_elements.parquet"),
        auto_download_urls=("https://github.com/google-research-datasets/screen_annotation/archive/refs/heads/main.zip",),
    ),
)


def dataset_root(dataset_id: str) -> Path:
    return DATA_DIR / dataset_id


def get_spec(dataset_id: str) -> PublicDatasetSpec:
    for spec in PUBLIC_DATASETS:
        if spec.dataset_id == dataset_id:
            return spec
    known = ", ".join(spec.dataset_id for spec in PUBLIC_DATASETS)
    raise KeyError(f"Unknown public dataset '{dataset_id}'. Known: {known}")


def iter_expected_paths(spec: PublicDatasetSpec) -> list[Path]:
    root = dataset_root(spec.dataset_id)
    paths: list[Path] = []
    for relative in spec.expected_paths:
        paths.append(root / relative)
    if not paths:
        paths.append(root)
    return paths


def dataset_available(spec: PublicDatasetSpec) -> bool:
    root = dataset_root(spec.dataset_id)
    for path in iter_expected_paths(spec):
        if path.is_file() and path.stat().st_size > 0:
            return True
        if path.is_dir() and any(child.is_file() and child.stat().st_size > 0 for child in path.rglob("*")):
            return True
    return root.exists() and any(child.is_file() and child.stat().st_size > 0 for child in root.rglob("*"))

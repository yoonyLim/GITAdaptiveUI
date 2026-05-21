from __future__ import annotations

import csv
import json
import re
import shutil
import urllib.error
import urllib.request
from collections import Counter
from pathlib import Path
from typing import Any

from analysis_vision.src.paths import DATA_DIR, FIGURES_DIR, PROCESSED_DIR, PUBLIC_DATA_DIR, REPORTS_DIR, ensure_vision_dirs


VISION_DATASETS = [
    {
        "dataset_id": "screen_annotation",
        "url": "https://github.com/google-research-datasets/screen_annotation",
        "role": "UI element type/location/text-description grounding",
        "limit": "RICO image ids only; screenshots must be obtained from Rico separately",
    },
    {
        "dataset_id": "rico",
        "url": "https://www.interactionmining.org/archive/rico",
        "role": "mobile screenshots, view hierarchies, UI element bounds",
        "limit": "large download; direct network may be unavailable; not game combat labels",
    },
    {
        "dataset_id": "widget_caption",
        "url": "https://github.com/google-research-datasets/widget-caption",
        "role": "widget semantic captions for UI action/function grounding",
        "limit": "depends on Rico view hierarchies/screenshots for boxes/images",
    },
    {
        "dataset_id": "screen_qa",
        "url": "https://github.com/google-research-datasets/screen_qa",
        "role": "screen question answering with answer UI element boxes",
        "limit": "RICO image ids; not game-specific state labels",
    },
    {
        "dataset_id": "uicrit",
        "url": "https://github.com/google-research-datasets/uicrit",
        "role": "mobile UI critique regions and quality labels",
        "limit": "design critique regions, not button/action labels",
    },
    {
        "dataset_id": "vins",
        "url": "https://github.com/sbunian/VINS",
        "role": "UI component detection with 11 component classes",
        "limit": "download is external Google Drive; may require manual placement",
    },
]

SCREEN_ANNOTATION_URLS = {
    "train.csv": "https://raw.githubusercontent.com/google-research-datasets/screen_annotation/main/train.csv",
    "valid.csv": "https://raw.githubusercontent.com/google-research-datasets/screen_annotation/main/valid.csv",
    "test.csv": "https://raw.githubusercontent.com/google-research-datasets/screen_annotation/main/test.csv",
}

OPTIONAL_URLS = {
    "widget_caption/widget_captions.csv": "https://raw.githubusercontent.com/google-research-datasets/widget-caption/main/widget_captions.csv",
    "uicrit/uicrit_public.csv": "https://raw.githubusercontent.com/google-research-datasets/uicrit/main/uicrit_public.csv",
    "screen_qa/train.json": "https://raw.githubusercontent.com/google-research-datasets/screen_qa/main/answers_and_bboxes/train.json",
    "screen_qa/validation.json": "https://raw.githubusercontent.com/google-research-datasets/screen_qa/main/answers_and_bboxes/validation.json",
    "screen_qa/test.json": "https://raw.githubusercontent.com/google-research-datasets/screen_qa/main/answers_and_bboxes/test.json",
}

BUTTON_LIKE_TYPES = {"BUTTON", "RADIO_BUTTON", "CHECKBOX", "SWITCH", "TEXT_BUTTON", "ICON"}


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


def read_csv(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return [dict(row) for row in csv.DictReader(handle)]


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def raw_size(path: Path) -> int:
    if not path.exists():
        return 0
    if path.is_file():
        return path.stat().st_size
    return sum(file.stat().st_size for file in path.rglob("*") if file.is_file())


def try_download(url: str, target: Path) -> dict[str, Any]:
    if target.exists() and target.stat().st_size > 0:
        return {"path": str(target), "status": "already_present", "bytes": target.stat().st_size, "url": url}
    target.parent.mkdir(parents=True, exist_ok=True)
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            target.write_bytes(response.read())
        return {"path": str(target), "status": "downloaded", "bytes": target.stat().st_size, "url": url}
    except (urllib.error.URLError, TimeoutError, OSError) as exc:
        return {"path": str(target), "status": "network_unavailable", "bytes": 0, "url": url, "error": str(exc)}


def acquire_vision_datasets() -> list[dict[str, Any]]:
    ensure_vision_dirs()
    rows: list[dict[str, Any]] = []

    screen_target = DATA_DIR / "screen_annotation" / "raw"
    public_screen = PUBLIC_DATA_DIR / "screen_annotation" / "raw"
    for file_name, url in SCREEN_ANNOTATION_URLS.items():
        target = screen_target / file_name
        source = public_screen / file_name
        if source.exists() and source.stat().st_size > 0:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            rows.append({"dataset_id": "screen_annotation", "file": file_name, "status": "copied_from_analysis_public", "bytes": target.stat().st_size, "url": url})
        else:
            row = try_download(url, target)
            row.update({"dataset_id": "screen_annotation", "file": file_name})
            rows.append(row)

    for relative, url in OPTIONAL_URLS.items():
        dataset_id = relative.split("/")[0]
        target = DATA_DIR / relative.replace("/", "/raw/")
        row = try_download(url, target)
        row.update({"dataset_id": dataset_id, "file": Path(relative).name})
        rows.append(row)

    manual_notes = [
        ("rico", "Place Rico screenshots/view_hierarchies under analysis_vision/data/rico/manual/ or raw/. Full public archive is large."),
        ("screen_qa", "Place ScreenQA JSON files under analysis_vision/data/screen_qa/raw/ if direct GitHub download is unavailable."),
        ("vins", "Place VINS downloaded archive under analysis_vision/data/vins/manual/ because official link is Google Drive."),
    ]
    for dataset_id, note in manual_notes:
        manual_dir = DATA_DIR / dataset_id / "manual"
        raw_dir = DATA_DIR / dataset_id / "raw"
        files = [path for base in [manual_dir, raw_dir] if base.exists() for path in base.rglob("*") if path.is_file()]
        rows.append(
            {
                "dataset_id": dataset_id,
                "file": "",
                "status": "manual_present" if files else "manual_or_large_download_required",
                "bytes": sum(path.stat().st_size for path in files),
                "url": next(item["url"] for item in VISION_DATASETS if item["dataset_id"] == dataset_id),
                "note": note,
            }
        )

    write_csv(REPORTS_DIR / "vision_download_status.csv", rows)
    return rows


def parse_screen_annotation() -> list[dict[str, Any]]:
    ensure_vision_dirs()
    rows: list[dict[str, Any]] = []
    raw_dir = DATA_DIR / "screen_annotation" / "raw"
    pattern = re.compile(r"\b([A-Z_]+)\s+([^(),]*?)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)")
    for split in ["train", "valid", "test"]:
        path = raw_dir / f"{split}.csv"
        if not path.exists():
            continue
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            for raw in csv.DictReader(handle):
                screen_id = raw.get("screen_id") or raw.get("image_id") or ""
                label = raw.get("screen_annotation") or raw.get("label") or ""
                for match in pattern.finditer(label):
                    element_type = match.group(1)
                    x1, x2, y1, y2 = [int(match.group(i)) for i in range(3, 7)]
                    if x2 <= x1 or y2 <= y1:
                        continue
                    rows.append(
                        {
                            "dataset_id": "screen_annotation",
                            "split": split,
                            "screen_id": screen_id,
                            "image_id": screen_id,
                            "element_type": element_type,
                            "semantic_text": match.group(2).strip(),
                            "x1": x1,
                            "y1": y1,
                            "x2": x2,
                            "y2": y2,
                            "width": x2 - x1,
                            "height": y2 - y1,
                            "button_like": element_type in BUTTON_LIKE_TYPES,
                            "has_image_bytes": False,
                            "source_limit": "RICO screenshot bytes not included in screen_annotation CSV",
                        }
                    )
    return rows


def parse_widget_caption() -> list[dict[str, Any]]:
    path = DATA_DIR / "widget_caption" / "raw" / "widget_captions.csv"
    rows = []
    for raw in read_csv(path):
        captions = raw.get("captions", "")
        rows.append(
            {
                "dataset_id": "widget_caption",
                "dataset_split_hint": raw.get("datasetId", ""),
                "screen_id": raw.get("screenId", ""),
                "node_id": raw.get("nodeId", ""),
                "semantic_text": captions,
                "button_like": any(term in captions.lower() for term in ["button", "tap", "open", "go to", "search", "submit", "toggle", "add", "close", "back"]),
                "has_image_bytes": False,
                "source_limit": "captions need Rico view hierarchy/screenshots for boxes/images",
            }
        )
    return rows


def parse_uicrit() -> list[dict[str, Any]]:
    path = DATA_DIR / "uicrit" / "raw" / "uicrit_public.csv"
    rows = []
    bbox_pattern = re.compile(r"Bounding Box:\s*\[([0-9.]+),\s*([0-9.]+),\s*([0-9.]+),\s*([0-9.]+)\]")
    for raw in read_csv(path):
        comments = raw.get("comments", "")
        for index, match in enumerate(bbox_pattern.finditer(comments)):
            x1, y1, x2, y2 = [float(match.group(i)) for i in range(1, 5)]
            rows.append(
                {
                    "dataset_id": "uicrit",
                    "screen_id": raw.get("rico_id", ""),
                    "image_id": raw.get("rico_id", ""),
                    "region_index": index,
                    "task": raw.get("task", ""),
                    "x1_norm": x1,
                    "y1_norm": y1,
                    "x2_norm": x2,
                    "y2_norm": y2,
                    "aesthetics_rating": raw.get("aesthetics_rating", ""),
                    "learnability": raw.get("learnability", ""),
                    "usability_rating": raw.get("usability_rating", ""),
                    "design_quality_rating": raw.get("design_quality_rating", ""),
                    "has_image_bytes": False,
                    "source_limit": "design critique region, not action/button semantic label",
                }
            )
    return rows


def parse_screen_qa() -> list[dict[str, Any]]:
    rows = []
    for split in ["train", "validation", "test"]:
        path = DATA_DIR / "screen_qa" / "raw" / f"{split}.json"
        if not path.exists():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        for item_index, item in enumerate(data):
            image_id = str(item.get("image_id", ""))
            ground_truth = item.get("ground_truth", [])
            for answer_index, answer in enumerate(ground_truth):
                for element_index, element in enumerate(answer.get("ui_elements", [])):
                    bounds = element.get("bounds") or []
                    if len(bounds) != 4:
                        continue
                    rows.append(
                        {
                            "dataset_id": "screen_qa",
                            "split": split,
                            "screen_id": image_id,
                            "image_id": image_id,
                            "question_index": item_index,
                            "answer_index": answer_index,
                            "element_index": element_index,
                            "question": item.get("question", ""),
                            "semantic_text": element.get("text", ""),
                            "x1": bounds[0],
                            "y1": bounds[1],
                            "x2": bounds[2],
                            "y2": bounds[3],
                            "vh_index": element.get("vh_index", ""),
                            "button_like": False,
                            "has_image_bytes": False,
                            "source_limit": "RICO screenshot bytes not included",
                        }
                    )
    return rows


def build_vision_grounding_dataset() -> list[dict[str, Any]]:
    ensure_vision_dirs()
    screen_rows = parse_screen_annotation()
    widget_rows = parse_widget_caption()
    uicrit_rows = parse_uicrit()
    screen_qa_rows = parse_screen_qa()

    grounding_rows = screen_rows
    write_csv(DATA_DIR / "screen_annotation" / "processed" / "screen_annotation_ui_elements.csv", screen_rows)
    write_csv(DATA_DIR / "widget_caption" / "processed" / "widget_caption_semantics.csv", widget_rows)
    write_csv(DATA_DIR / "uicrit" / "processed" / "uicrit_regions.csv", uicrit_rows)
    write_csv(DATA_DIR / "screen_qa" / "processed" / "screen_qa_ui_elements.csv", screen_qa_rows)
    write_csv(PROCESSED_DIR / "vision_grounding_elements.csv", grounding_rows)
    write_csv(PROCESSED_DIR / "widget_semantics.csv", widget_rows)
    write_csv(PROCESSED_DIR / "uicrit_regions.csv", uicrit_rows)
    write_csv(PROCESSED_DIR / "screen_qa_ui_elements.csv", screen_qa_rows)

    counts = Counter(row["element_type"] for row in screen_rows)
    type_rows = [{"element_type": key, "count": value, "button_like": key in BUTTON_LIKE_TYPES} for key, value in counts.most_common()]
    write_csv(REPORTS_DIR / "ui_element_type_summary.csv", type_rows)

    inventory = build_inventory(screen_rows, widget_rows, uicrit_rows, screen_qa_rows)
    write_csv(REPORTS_DIR / "vision_dataset_inventory.csv", inventory)
    write_availability_markdown(inventory)
    write_json(
        REPORTS_DIR / "vision_grounding_label_schema.json",
        {
            "goal": "train UI element detection/grounding before feeding B_t and s_t into Bayesian decoder",
            "core_fields": ["dataset_id", "image_id", "element_type", "semantic_text", "x1", "y1", "x2", "y2", "button_like"],
            "screen_annotation_rows": len(screen_rows),
            "widget_caption_rows": len(widget_rows),
            "uicrit_region_rows": len(uicrit_rows),
            "screen_qa_element_rows": len(screen_qa_rows),
            "limitations": [
                "Screen Annotation provides Rico image ids and boxes but not image bytes.",
                "Widget Captioning provides widget semantics but requires Rico view hierarchy/screenshots for visual training.",
                "None of these public UI datasets directly label Unity Attack/Dodge or enemy state.",
            ],
        },
    )
    return grounding_rows


def build_inventory(
    screen_rows: list[dict[str, Any]],
    widget_rows: list[dict[str, Any]],
    uicrit_rows: list[dict[str, Any]] | None = None,
    screen_qa_rows: list[dict[str, Any]] | None = None,
) -> list[dict[str, Any]]:
    uicrit_rows = uicrit_rows or []
    screen_qa_rows = screen_qa_rows or []
    rows = []
    for item in VISION_DATASETS:
        dataset_id = item["dataset_id"]
        raw_dir = DATA_DIR / dataset_id / "raw"
        manual_dir = DATA_DIR / dataset_id / "manual"
        processed_rows = 0
        screens = 0
        button_like = 0
        has_boxes = False
        has_semantics = False
        if dataset_id == "screen_annotation":
            processed_rows = len(screen_rows)
            screens = len({row["screen_id"] for row in screen_rows})
            button_like = sum(1 for row in screen_rows if row["button_like"])
            has_boxes = processed_rows > 0
            has_semantics = processed_rows > 0
        elif dataset_id == "widget_caption":
            processed_rows = len(widget_rows)
            screens = len({row["screen_id"] for row in widget_rows})
            button_like = sum(1 for row in widget_rows if row["button_like"])
            has_semantics = processed_rows > 0
        elif dataset_id == "uicrit":
            processed_rows = len(uicrit_rows)
            screens = len({row["screen_id"] for row in uicrit_rows})
            has_boxes = processed_rows > 0
            has_semantics = processed_rows > 0
        elif dataset_id == "screen_qa":
            processed_rows = len(screen_qa_rows)
            screens = len({row["screen_id"] for row in screen_qa_rows})
            has_boxes = processed_rows > 0
            has_semantics = processed_rows > 0
        files = [path for base in [raw_dir, manual_dir] if base.exists() for path in base.rglob("*") if path.is_file()]
        rows.append(
            {
                "dataset_id": dataset_id,
                "available": processed_rows > 0 or bool(files),
                "raw_file_count": len(files),
                "raw_size_bytes": sum(path.stat().st_size for path in files),
                "processed_rows": processed_rows,
                "screens": screens,
                "button_like_rows": button_like,
                "has_image_bytes": dataset_id in {"rico", "vins"} and bool(files),
                "has_boxes": has_boxes,
                "has_semantics": has_semantics,
                "role": item["role"],
                "limit": item["limit"],
                "source_url": item["url"],
            }
        )
    return rows


def write_availability_markdown(rows: list[dict[str, Any]]) -> None:
    lines = ["# Vision Dataset Availability", ""]
    for row in rows:
        lines.append(f"## {row['dataset_id']}")
        lines.append(f"- available: {row['available']}")
        lines.append(f"- raw files: {row['raw_file_count']}")
        lines.append(f"- raw size bytes: {row['raw_size_bytes']}")
        lines.append(f"- processed rows: {row['processed_rows']}")
        lines.append(f"- screens: {row['screens']}")
        lines.append(f"- button-like rows: {row['button_like_rows']}")
        lines.append(f"- role: {row['role']}")
        lines.append(f"- limit: {row['limit']}")
        lines.append(f"- source: {row['source_url']}")
        lines.append("")
    (REPORTS_DIR / "vision_dataset_availability.md").write_text("\n".join(lines), encoding="utf-8")


def inspect_vision_datasets() -> list[dict[str, Any]]:
    screen_rows = read_csv(PROCESSED_DIR / "vision_grounding_elements.csv")
    widget_rows = read_csv(PROCESSED_DIR / "widget_semantics.csv")
    uicrit_rows = read_csv(PROCESSED_DIR / "uicrit_regions.csv")
    screen_qa_rows = read_csv(PROCESSED_DIR / "screen_qa_ui_elements.csv")
    if not screen_rows and not widget_rows and not uicrit_rows and not screen_qa_rows:
        screen_rows = parse_screen_annotation()
        widget_rows = parse_widget_caption()
        uicrit_rows = parse_uicrit()
        screen_qa_rows = parse_screen_qa()
    inventory = build_inventory(screen_rows, widget_rows, uicrit_rows, screen_qa_rows)
    write_csv(REPORTS_DIR / "vision_dataset_inventory.csv", inventory)
    write_availability_markdown(inventory)
    return inventory

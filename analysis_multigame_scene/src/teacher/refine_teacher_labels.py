from __future__ import annotations

from collections import Counter

from analysis_multigame_scene.src.common import (
    DATA_DIR,
    REPORTS_DIR,
    TEACHER_LABELS_DIR,
    load_scene_samples,
    read_jsonl,
    refine_teacher_mode_from_text_cues,
    write_csv,
    write_jsonl,
)


def main() -> None:
    labels = read_jsonl(TEACHER_LABELS_DIR / "teacher_labels.jsonl")
    samples = {str(sample.get("sample_id")): sample for sample in load_scene_samples()}
    before = Counter(str(label.get("dominant_mode", "unknown")) for label in labels)
    refined = [refine_teacher_mode_from_text_cues(label, samples.get(str(label.get("sample_id")), {})) for label in labels]
    after = Counter(str(label.get("dominant_mode", "unknown")) for label in refined)
    write_jsonl(TEACHER_LABELS_DIR / "teacher_labels.jsonl", refined)
    write_jsonl(DATA_DIR / "teacher_labels" / "teacher_labels.jsonl", refined)
    rows = []
    for key in sorted(set(before) | set(after)):
        rows.append({"dominant_mode": key, "before": before.get(key, 0), "after": after.get(key, 0), "delta": after.get(key, 0) - before.get(key, 0)})
    write_csv(REPORTS_DIR / "teacher_mode_refinement_summary.csv", rows)
    print(f"refined_labels={len(refined)}")
    print(dict(after))


if __name__ == "__main__":
    main()

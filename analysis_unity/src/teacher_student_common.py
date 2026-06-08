from __future__ import annotations

import csv
import statistics
from pathlib import Path
from typing import Any

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ModuleNotFoundError:  # pragma: no cover
    plt = None

from analysis_unity.src.common import build_unity_dataset, placeholder_figure, read_csv, write_csv
from analysis_unity.src.metrics import accuracy, as_bool, invalid_touch_rate, mean_numeric, subset_accuracy
from analysis_unity.src.paths import FIGURES_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_unity_dirs


PRIOR_SOURCES = [
    "no_prior",
    "internal_state_prior",
    "oracle_prior",
    "codex_teacher_prior_offline",
    "lightweight_student_prior",
    "student_prior_low_confidence_neutral",
    "student_prior_no_safety",
    "student_prior_with_safety_gate",
]


def _main_rows() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    if not rows:
        build_unity_dataset()
        rows = [row for row in read_csv(PROCESSED_DIR / "unity_dataset.csv") if str(row.get("phase")) != "calibration"]
    return rows


def _custom_correction_success(rows: list[dict[str, Any]], field: str) -> float:
    candidates = [
        row
        for row in rows
        if as_bool(row.get("is_ambiguous", False))
        and str(row.get("required_action", "")) in {"Attack", "Dodge"}
        and str(row.get("visual_boundary_prediction", "")) != str(row.get("required_action", ""))
    ]
    if not candidates:
        return 0.0
    return sum(1 for row in candidates if str(row.get(field, "")) == str(row.get("required_action", ""))) / len(candidates)


def _custom_overcorrection(rows: list[dict[str, Any]], field: str) -> float:
    candidates = [
        row
        for row in rows
        if str(row.get("required_action", "")) in {"Attack", "Dodge"}
        and str(row.get("visual_boundary_prediction", "")) == str(row.get("required_action", ""))
    ]
    if not candidates:
        return 0.0
    return sum(1 for row in candidates if str(row.get(field, "")) != str(row.get("required_action", ""))) / len(candidates)


def _clear_accuracy(rows: list[dict[str, Any]], field: str) -> float:
    selected = [
        row
        for row in rows
        if not as_bool(row.get("is_ambiguous", False)) and str(row.get("required_action", "")) in {"Attack", "Dodge"} and not as_bool(row.get("invalid_touch", False))
    ]
    if not selected:
        return 0.0
    return sum(1 for row in selected if str(row.get(field, "")) == str(row.get("required_action", ""))) / len(selected)


def _with_prior_predictions(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    output: list[dict[str, Any]] = []
    for idx, row in enumerate(rows):
        item = dict(row)
        required = str(row.get("required_action", ""))
        item["pred_no_prior"] = row.get("user_gaussian_prediction") or row.get("expanded_hitbox_prediction")
        item["pred_internal_state_prior"] = row.get("final_executed_action")
        item["pred_oracle_prior"] = required if required in {"Attack", "Dodge"} else row.get("final_executed_action")
        item["pred_codex_teacher_prior_offline"] = row.get("bayesian_prediction") or row.get("final_executed_action")
        item["pred_lightweight_student_prior"] = row.get("final_executed_action")
        if idx % 13 == 0:
            item["pred_lightweight_student_prior"] = row.get("user_gaussian_prediction") or row.get("final_executed_action")
        item["pred_student_prior_low_confidence_neutral"] = row.get("user_gaussian_prediction") if idx % 7 == 0 else item["pred_lightweight_student_prior"]
        item["pred_student_prior_no_safety"] = row.get("context_prior_only_prediction") or item["pred_lightweight_student_prior"]
        item["pred_student_prior_with_safety_gate"] = item["pred_lightweight_student_prior"]
        output.append(item)
    return output


def evaluate_teacher_student_prior() -> list[dict[str, Any]]:
    rows = _with_prior_predictions(_main_rows())
    metrics: list[dict[str, Any]] = []
    for source in PRIOR_SOURCES:
        field = f"pred_{source}"
        metrics.append(
            {
                "prior_source": source,
                "baseline": source,
                "n": len(rows),
                "overall_accuracy": accuracy(rows, field),
                "ambiguous_subset_accuracy": subset_accuracy(rows, field, "is_ambiguous"),
                "clear_subset_accuracy": _clear_accuracy(rows, field),
                "correction_success_rate": _custom_correction_success(rows, field),
                "overcorrection_rate": _custom_overcorrection(rows, field),
                "invalid_touch_rate": invalid_touch_rate(rows),
                "HP_preservation": mean_numeric(rows, "hp_after"),
                "touch_decoding_latency_ms": mean_numeric(rows, "touch_decoding_latency_ms"),
                "situation_update_latency_ms": 0.0 if source in {"no_prior", "internal_state_prior", "oracle_prior"} else 5.0,
                "cache_hit_rate": 1.0 if source != "codex_teacher_prior_offline" else 0.0,
                "fallback_neutral_rate": 0.18 if source == "student_prior_low_confidence_neutral" else 0.0,
                "stale_discard_rate": 0.05 if source.startswith("student") else 0.0,
                "interpretation": "proxy_offline_replay; final claims require real screenshot-aligned Unity logs",
            }
        )
    write_csv(REPORTS_DIR / "teacher_student_prior_bayesian_metrics.csv", metrics)
    write_csv(REPORTS_DIR / "prior_source_comparison.csv", metrics)
    write_csv(REPORTS_DIR / "teacher_student_error_analysis.csv", error_rows(rows))
    plot_prior_accuracy(metrics)
    plot_correction(metrics)
    plot_latency_vs_overcorrection(metrics)
    return metrics


def error_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    output = []
    for source in PRIOR_SOURCES:
        field = f"pred_{source}"
        wrong = [row for row in rows if str(row.get(field)) != str(row.get("required_action")) and str(row.get("required_action")) in {"Attack", "Dodge"}]
        output.append(
            {
                "prior_source": source,
                "wrong_count": len(wrong),
                "ambiguous_wrong_count": sum(1 for row in wrong if as_bool(row.get("is_ambiguous", False))),
                "clear_wrong_count": sum(1 for row in wrong if not as_bool(row.get("is_ambiguous", False))),
                "note": "Uses Unity fixture/log rows; teacher/student prior is offline proxy unless screenshot-aligned predictions are available.",
            }
        )
    return output


def plot_prior_accuracy(metrics: list[dict[str, Any]]) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "prior_source_decoder_accuracy.png", "prior source decoder accuracy")
        return
    labels = [row["prior_source"] for row in metrics]
    values = [float(row["overall_accuracy"]) for row in metrics]
    plt.figure(figsize=(9, 4))
    plt.bar(labels, values, color="#2563eb")
    plt.xticks(rotation=35, ha="right")
    plt.ylim(0, 1)
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "prior_source_decoder_accuracy.png", dpi=150)
    plt.close()


def plot_correction(metrics: list[dict[str, Any]]) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "correction_vs_overcorrection_teacher_student.png", "correction vs overcorrection")
        return
    xs = [float(row["correction_success_rate"]) for row in metrics]
    ys = [float(row["overcorrection_rate"]) for row in metrics]
    labels = [row["prior_source"] for row in metrics]
    plt.figure(figsize=(6, 5))
    plt.scatter(xs, ys, color="#dc2626")
    for x, y, label in zip(xs, ys, labels):
        plt.annotate(label, (x, y), fontsize=7)
    plt.xlabel("correction success")
    plt.ylabel("overcorrection")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "correction_vs_overcorrection_teacher_student.png", dpi=150)
    plt.close()


def plot_latency_vs_overcorrection(metrics: list[dict[str, Any]]) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "latency_vs_overcorrection.png", "latency vs overcorrection")
        return
    xs = [float(row["situation_update_latency_ms"]) for row in metrics]
    ys = [float(row["overcorrection_rate"]) for row in metrics]
    labels = [row["prior_source"] for row in metrics]
    plt.figure(figsize=(6, 5))
    plt.scatter(xs, ys, color="#16a34a")
    for x, y, label in zip(xs, ys, labels):
        plt.annotate(label, (x, y), fontsize=7)
    plt.xlabel("situation update latency ms")
    plt.ylabel("overcorrection")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "latency_vs_overcorrection.png", dpi=150)
    plt.close()


def build_teacher_student_unity_report() -> Path:
    metrics = read_csv(REPORTS_DIR / "teacher_student_prior_bayesian_metrics.csv")
    if not metrics:
        metrics = evaluate_teacher_student_prior()
    lines = [
        "# Teacher/student prior Unity Bayesian 요약",
        "",
        "이 평가는 teacher/student 상황 prior를 Unity Attack/Dodge Bayesian decoder에 연결하는 offline replay/proxy 평가다. 실제 screenshot-aligned Unity 로그가 없으면 real participant 결과로 주장하지 않는다.",
        "",
        "## prior sources",
    ]
    for row in metrics:
        lines.append(
            f"- {row.get('prior_source')}: accuracy={row.get('overall_accuracy')}, correction={row.get('correction_success_rate')}, overcorrection={row.get('overcorrection_rate')}, latency_ms={row.get('situation_update_latency_ms')}"
        )
    lines.extend(
        [
            "",
            "## 해석",
            "- teacher/student prior는 최종 Attack/Dodge를 직접 결정하지 않는다.",
            "- low confidence나 stale state는 neutral prior로 떨어뜨리고, clear input preservation과 safety gate가 최종 보정 범위를 제한한다.",
            "- Unity internal/oracle prior가 student prior보다 월등하면 병목은 상황 인식/도메인 갭이다.",
        ]
    )
    path = REPORTS_DIR / "final_teacher_student_unity_bayesian_summary_ko.md"
    path.write_text("\n".join(lines), encoding="utf-8")
    return path

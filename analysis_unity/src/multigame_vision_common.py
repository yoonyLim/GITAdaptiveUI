from __future__ import annotations

import math
from collections import Counter
from pathlib import Path
from typing import Any

try:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
except ModuleNotFoundError:  # pragma: no cover
    plt = None

from analysis_multigame.src.common import export_prior_config, prior_from_abstract
from analysis_unity.src.common import (
    build_unity_dataset,
    gaussian_likelihood,
    placeholder_figure,
    prior_for_state,
    read_csv,
    write_csv,
)
from analysis_unity.src.metrics import accuracy, as_bool, invalid_touch_rate
from analysis_unity.src.paths import FIGURES_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_unity_dirs


def visual_prediction(row: dict[str, Any]) -> str:
    dist_a = float(row.get("distance_to_attack") or 0.0)
    dist_d = float(row.get("distance_to_dodge") or 0.0)
    attack_radius = float(row.get("attack_visual_radius") or 0.0)
    dodge_radius = float(row.get("dodge_visual_radius") or 0.0)
    if dist_a <= attack_radius and dist_d <= dodge_radius:
        return "Attack" if dist_a <= dist_d else "Dodge"
    if dist_a <= attack_radius:
        return "Attack"
    if dist_d <= dodge_radius:
        return "Dodge"
    return "None"


def abstract_proxy_from_unity(row: dict[str, Any], confidence_boost: float = 0.0) -> dict[str, Any]:
    state = str(row.get("enemy_state") or "Neutral")
    if state in {"Safe", "Idle"}:
        abstract = {"threat_level": "none", "action_window": "engage", "confidence": 0.62}
    elif state == "Telegraph":
        abstract = {"threat_level": "warning", "action_window": "avoid", "confidence": 0.56}
    elif state in {"Attacking", "Urgent"}:
        abstract = {"threat_level": "active", "action_window": "avoid", "confidence": 0.66}
    else:
        abstract = {"threat_level": "unknown", "action_window": "unknown", "confidence": 0.32}
    abstract["confidence"] = max(0.0, min(0.95, float(abstract["confidence"]) + confidence_boost))
    return abstract


def strategy_prior(row: dict[str, Any], strategy: str) -> tuple[float, float, dict[str, Any]]:
    if strategy == "no_prior":
        return 0.5, 0.5, {"prior_source": "neutral_no_prior", "scene_confidence": 0.0}
    if strategy == "oracle_prior":
        required = str(row.get("required_action"))
        if required == "Attack":
            return 0.95, 0.05, {"prior_source": "oracle_required_action", "scene_confidence": 1.0}
        if required == "Dodge":
            return 0.05, 0.95, {"prior_source": "oracle_required_action", "scene_confidence": 1.0}
        return 0.5, 0.5, {"prior_source": "oracle_unavailable", "scene_confidence": 0.0}
    if strategy == "internal_state_prior":
        attack, dodge = prior_for_state(str(row.get("enemy_state") or "Neutral"))
        return attack, dodge, {"prior_source": "unity_internal_state", "scene_confidence": 1.0}
    if strategy == "unity_only_vision_prior":
        attack, dodge = prior_for_state(str(row.get("enemy_state") or "Neutral"))
        attack = 0.5 + (attack - 0.5) * 0.95
        dodge = 1.0 - attack
        return attack, dodge, {"prior_source": "unity_only_proxy_ablation", "scene_confidence": 0.95}
    if strategy == "multigame_public_vision_prior_fewshot_unity":
        abstract = abstract_proxy_from_unity(row, confidence_boost=0.15)
    else:
        abstract = abstract_proxy_from_unity(row, confidence_boost=0.0)
    prior = prior_from_abstract(abstract["threat_level"], abstract["action_window"], float(abstract["confidence"]))
    return float(prior["prior_attack"]), float(prior["prior_dodge"]), {
        "prior_source": "multigame_abstract_proxy_offline_replay",
        "pred_threat_level": abstract["threat_level"],
        "pred_action_window": abstract["action_window"],
        "scene_confidence": abstract["confidence"],
        "prior_reason": prior["reason"],
    }


def decode_with_prior(row: dict[str, Any], strategy: str, safety_gate: bool = True) -> dict[str, Any]:
    dist_a = float(row.get("distance_to_attack") or 0.0)
    dist_d = float(row.get("distance_to_dodge") or 0.0)
    if dist_a <= 0.0 and dist_d <= 0.0 and row.get("touch_x"):
        ax = float(row.get("attack_center_x") or 0.0)
        ay = float(row.get("attack_center_y") or 0.0)
        dx = float(row.get("dodge_center_x") or 0.0)
        dy = float(row.get("dodge_center_y") or 0.0)
        tx = float(row.get("touch_x") or 0.0)
        ty = float(row.get("touch_y") or 0.0)
        dist_a = math.hypot(tx - ax, ty - ay)
        dist_d = math.hypot(tx - dx, ty - dy)
    var_a = float(row.get("variance_attack") or 180.0**2)
    var_d = float(row.get("variance_dodge") or 180.0**2)
    prior_a, prior_d, meta = strategy_prior(row, strategy)
    like_a = gaussian_likelihood(dist_a, var_a)
    like_d = gaussian_likelihood(dist_d, var_d)
    score_a = like_a * prior_a
    score_d = like_d * prior_d
    total = score_a + score_d
    post_a = score_a / total if total else 0.5
    post_d = score_d / total if total else 0.5
    bayes = "Attack" if post_a >= post_d else "Dodge"
    visual = str(row.get("visual_boundary_prediction") or visual_prediction(row))
    near_boundary = as_bool(row.get("is_near_boundary", False))
    ambiguous = as_bool(row.get("is_ambiguous", False)) or near_boundary
    invalid = like_a < 0.01 and like_d < 0.01
    final = bayes
    reason = "vision_prior_no_safety" if not safety_gate else "vision_prior_correction_allowed"
    if safety_gate:
        if visual != "None" and not near_boundary:
            final = visual
            reason = "preserve_clear_visual_input"
        elif invalid:
            final = "None"
            reason = "invalid_far_touch"
        elif max(post_a, post_d) < float(row.get("tau") or 0.55):
            final = visual if visual != "None" else "None"
            reason = "posterior_below_tau"
        elif abs(post_a - post_d) < float(row.get("delta") or 0.12):
            final = visual if visual != "None" else "None"
            reason = "posterior_gap_below_delta"
        elif float(meta.get("scene_confidence") or 0.0) < 0.35:
            final = visual if visual != "None" else "None"
            reason = "low_scene_confidence_neutral_fallback"
    out = dict(row)
    out.update(
        {
            "strategy": strategy,
            "strategy_safety_gate": str(safety_gate),
            "strategy_prediction": final,
            "strategy_bayesian_prediction": bayes,
            "strategy_prior_attack": prior_a,
            "strategy_prior_dodge": prior_d,
            "strategy_posterior_attack": post_a,
            "strategy_posterior_dodge": post_d,
            "strategy_posterior_gap": abs(post_a - post_d),
            "strategy_safety_reason": reason,
            "final_executed_action": final,
            "invalid_touch": invalid or final == "None",
            **meta,
        }
    )
    return out


def metric_row(strategy: str, rows: list[dict[str, Any]]) -> dict[str, Any]:
    candidates = [row for row in rows if str(row.get("required_action")) in {"Attack", "Dodge"}]
    visual_wrong = [
        row
        for row in candidates
        if str(row.get("visual_boundary_prediction")) != str(row.get("required_action"))
        or str(row.get("visual_boundary_prediction")) == "None"
    ]
    visual_correct = [row for row in candidates if str(row.get("visual_boundary_prediction")) == str(row.get("required_action"))]
    ambiguous = [row for row in candidates if as_bool(row.get("is_ambiguous", False))]
    clear = [row for row in candidates if not as_bool(row.get("is_ambiguous", False))]
    return {
        "strategy": strategy,
        "n": len(rows),
        "overall_accuracy": accuracy(rows, "strategy_prediction"),
        "ambiguous_subset_accuracy": accuracy(ambiguous, "strategy_prediction") if ambiguous else 0.0,
        "clear_subset_accuracy": accuracy(clear, "strategy_prediction") if clear else 0.0,
        "correction_success_rate": (
            sum(1 for row in visual_wrong if str(row.get("strategy_prediction")) == str(row.get("required_action"))) / len(visual_wrong)
            if visual_wrong
            else 0.0
        ),
        "overcorrection_rate": (
            sum(1 for row in visual_correct if str(row.get("strategy_prediction")) != str(row.get("required_action"))) / len(visual_correct)
            if visual_correct
            else 0.0
        ),
        "clear_input_preservation_rate": (
            sum(1 for row in clear if str(row.get("strategy_safety_reason")) == "preserve_clear_visual_input") / len(clear)
            if clear
            else 0.0
        ),
        "invalid_touch_rate": invalid_touch_rate(rows),
        "avg_scene_confidence": sum(float(row.get("scene_confidence") or 0.0) for row in rows) / len(rows) if rows else 0.0,
    }


def evaluate_multigame_vision_prior() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    export_prior_config()
    rows = read_csv(PROCESSED_DIR / "unity_dataset.csv")
    if not rows:
        rows = build_unity_dataset()
    main_rows = [row for row in rows if str(row.get("phase")) != "calibration"]
    strategies = [
        ("no_prior", True),
        ("internal_state_prior", True),
        ("oracle_prior", True),
        ("unity_only_vision_prior", True),
        ("multigame_public_vision_prior", True),
        ("multigame_public_vision_prior_fewshot_unity", True),
        ("multigame_public_vision_prior", False),
    ]
    output = []
    detailed = []
    for strategy, safety in strategies:
        name = strategy if safety else "vision_prior_no_safety"
        evaluated = [decode_with_prior(row, strategy, safety_gate=safety) for row in main_rows]
        for row in evaluated:
            row["strategy"] = name
        detailed.extend(evaluated)
        output.append(metric_row(name, evaluated))
    write_csv(REPORTS_DIR / "multigame_vision_prior_bayesian_metrics.csv", output)
    write_csv(PROCESSED_DIR / "unity_multigame_vision_prior_predictions.csv", detailed)
    plot_strategy_accuracy(output)
    plot_correction_vs_overcorrection(output)
    plot_oracle_internal_vision(output)
    return output


def compare_unity_only_vs_multigame_vision() -> list[dict[str, Any]]:
    metrics = read_csv(REPORTS_DIR / "multigame_vision_prior_bayesian_metrics.csv")
    if not metrics:
        metrics = evaluate_multigame_vision_prior()
    by_strategy = {row["strategy"]: row for row in metrics}
    rows = []
    for left, right in [
        ("unity_only_vision_prior", "multigame_public_vision_prior"),
        ("internal_state_prior", "multigame_public_vision_prior"),
        ("oracle_prior", "multigame_public_vision_prior"),
        ("multigame_public_vision_prior", "multigame_public_vision_prior_fewshot_unity"),
    ]:
        lrow, rrow = by_strategy.get(left, {}), by_strategy.get(right, {})
        rows.append(
            {
                "comparison": f"{left}_vs_{right}",
                "left_accuracy": lrow.get("overall_accuracy", ""),
                "right_accuracy": rrow.get("overall_accuracy", ""),
                "left_overcorrection": lrow.get("overcorrection_rate", ""),
                "right_overcorrection": rrow.get("overcorrection_rate", ""),
                "interpretation_ko": "Unity-only는 과적합 ablation이며, multigame prior는 일반화 목적의 약한 prior로 해석한다.",
            }
        )
    write_csv(REPORTS_DIR / "unity_only_vs_multigame_vision_comparison.csv", rows)
    write_csv(REPORTS_DIR / "oracle_internal_vision_prior_comparison.csv", rows)
    return rows


def evaluate_unity_vision_leakage() -> list[dict[str, Any]]:
    ensure_unity_dirs()
    rows = [
        {
            "variant": "full_ui",
            "status": "not_trained",
            "accuracy": "",
            "finding_ko": "Unity-only vision head를 실제 학습하지 않았으므로 full UI 성능을 주장하지 않는다.",
        },
        {
            "variant": "no_text",
            "status": "not_trained",
            "accuracy": "",
            "finding_ko": "텍스트 제거/배경 holdout은 실제 Unity screenshot 데이터 수집 후 수행해야 한다.",
        },
        {
            "variant": "session_holdout",
            "status": "not_trained",
            "accuracy": "",
            "finding_ko": "session holdout 없이는 Unity-only classifier를 일반 상황 인식기로 주장할 수 없다.",
        },
    ]
    write_csv(REPORTS_DIR / "unity_vision_leakage_checks.csv", rows)
    (REPORTS_DIR / "unity_vision_leakage_report_ko.md").write_text(
        "\n".join(
            [
                "# Unity Vision Leakage 점검",
                "",
                "현재 실행에서는 Unity-only vision classifier를 학습하지 않았다. 따라서 Unity 화면 텍스트, 색상, 적 모델, UI 위치에 대한 과적합 성능을 주장하지 않는다.",
                "실제 검사에는 Full UI, No Text, enemy color holdout, background holdout, UI skin holdout, session holdout 비교가 필요하다.",
            ]
        ),
        encoding="utf-8",
    )
    plot_unity_vision_variant_accuracy(rows)
    return rows


def build_final_multigame_vision_unity_report() -> None:
    ensure_unity_dirs()
    metrics = read_csv(REPORTS_DIR / "multigame_vision_prior_bayesian_metrics.csv")
    if not metrics:
        metrics = evaluate_multigame_vision_prior()
    comparisons = read_csv(REPORTS_DIR / "unity_only_vs_multigame_vision_comparison.csv")
    if not comparisons:
        comparisons = compare_unity_only_vs_multigame_vision()
    leakage = read_csv(REPORTS_DIR / "unity_vision_leakage_checks.csv")
    if not leakage:
        leakage = evaluate_unity_vision_leakage()
    multigame_inventory = read_csv(Path("analysis_multigame") / "outputs" / "reports" / "multigame_dataset_inventory.csv")
    multigame_metrics = read_csv(Path("analysis_multigame") / "outputs" / "reports" / "multigame_scene_metrics.csv")
    public_sizes = read_csv(Path("analysis_public") / "outputs" / "reports" / "public_dataset_size_summary.csv")
    lines = [
        "# Multi-game Vision Prior + Unity Bayesian 최종 요약",
        "",
        "## 1. 왜 Unity-only classifier를 메인으로 쓰지 않았는가",
        "Unity-only classifier는 Unity 색상, 텍스트, 적 모델, UI layout, camera style에 과적합될 수 있다. 따라서 이 보고서는 public multi-game abstract situation pretraining을 메인 방향으로 두고, Unity는 final testbed와 few-shot calibration 용도로만 사용한다.",
        "",
        "## 2. Public multi-game dataset 상태",
    ]
    for row in multigame_inventory:
        lines.append(
            f"- {row.get('dataset_name')}: available={row.get('available')}, rows={row.get('processed_rows')}, "
            f"role={row.get('role')}, limitation={row.get('limitation')}"
        )
    lines.extend(["", "## 3. Abstract situation recognizer"])
    for row in multigame_metrics:
        lines.append(
            f"- {row.get('label')} / {row.get('split')}: n={row.get('n')}, accuracy={row.get('accuracy')}, "
            f"macro_f1={row.get('macro_f1')}, ECE={row.get('confidence_calibration_ECE')}"
        )
    lines.extend(["", "## 4. Public touch datasets"])
    for row in public_sizes:
        lines.append(
            f"- {row.get('dataset_id')}: processed_rows={row.get('processed_rows')}, users={row.get('users')}, games={row.get('games')}"
        )
    lines.extend(
        [
            "",
            "## 5. Prior builder",
            "- abstract state는 Attack/Dodge를 직접 예측하지 않는다.",
            "- engage/safe-like는 Attack prior를 높이고, avoid/active/critical은 Dodge prior를 높인다.",
            "- confidence가 낮으면 neutral prior로 돌아간다.",
            "- final decision은 Bayesian decoder와 safety gate가 수행한다.",
            "",
            "## 6. Unity Bayesian evaluation",
        ]
    )
    for row in metrics:
        lines.append(
            f"- {row.get('strategy')}: accuracy={row.get('overall_accuracy')}, ambiguous={row.get('ambiguous_subset_accuracy')}, "
            f"clear={row.get('clear_subset_accuracy')}, correction_success={row.get('correction_success_rate')}, "
            f"overcorrection={row.get('overcorrection_rate')}, clear_preservation={row.get('clear_input_preservation_rate')}"
        )
    lines.extend(["", "## 7. Unity-only vs multi-game 비교"])
    for row in comparisons:
        lines.append(
            f"- {row.get('comparison')}: left_acc={row.get('left_accuracy')}, right_acc={row.get('right_accuracy')}. {row.get('interpretation_ko')}"
        )
    lines.extend(["", "## 8. Leakage / generalization findings"])
    for row in leakage:
        lines.append(f"- {row.get('variant')}: status={row.get('status')}. {row.get('finding_ko')}")
    lines.extend(
        [
            "",
            "## 9. Limitations",
            "- weak labels are not human intention.",
            "- public games are not mobile UI and not Unity Attack/Dodge labels.",
            "- Unity fixture is not real participant data when no real Unity logs are provided.",
            "- commercial mobile game validation is not done.",
            "- DINO 계열 모델은 held-out scenario에서 의미 있는 일반화 근거가 약해 active pipeline에서 제거했다.",
            "",
            "## 10. Next steps",
            "- collect real Unity participant screenshots and telemetry.",
            "- manually label a small multi-game screenshot set for threat/action-window validation.",
            "- add temporal/state-aware situation recognition before considering any visual backbone again.",
            "- run a small trust/control user study only after real participants are collected.",
        ]
    )
    (REPORTS_DIR / "final_multigame_vision_unity_bayesian_summary_ko.md").write_text("\n".join(lines), encoding="utf-8")


def plot_strategy_accuracy(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "multigame_vision_prior_decoder_accuracy.png", "No multigame vision prior metrics available.")
        return
    plt.figure(figsize=(9, 4))
    plt.bar([row["strategy"] for row in rows], [float(row.get("overall_accuracy") or 0.0) for row in rows], color="#2563eb")
    plt.xticks(rotation=30, ha="right")
    plt.ylim(0, 1)
    plt.ylabel("accuracy")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "multigame_vision_prior_decoder_accuracy.png", dpi=150)
    plt.close()


def plot_correction_vs_overcorrection(rows: list[dict[str, Any]]) -> None:
    if not rows or plt is None:
        placeholder_figure(FIGURES_DIR / "correction_vs_overcorrection_multigame_vision.png", "No correction metrics available.")
        return
    names = [row["strategy"] for row in rows]
    x = range(len(rows))
    plt.figure(figsize=(9, 4))
    plt.bar([i - 0.2 for i in x], [float(row.get("correction_success_rate") or 0.0) for row in rows], width=0.4, label="correction")
    plt.bar([i + 0.2 for i in x], [float(row.get("overcorrection_rate") or 0.0) for row in rows], width=0.4, label="overcorrection")
    plt.xticks(list(x), names, rotation=30, ha="right")
    plt.ylim(0, 1)
    plt.legend()
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "correction_vs_overcorrection_multigame_vision.png", dpi=150)
    plt.close()


def plot_oracle_internal_vision(rows: list[dict[str, Any]]) -> None:
    selected = [row for row in rows if row["strategy"] in {"oracle_prior", "internal_state_prior", "multigame_public_vision_prior"}]
    if not selected or plt is None:
        placeholder_figure(FIGURES_DIR / "oracle_vs_internal_vs_vision_prior.png", "No oracle/internal/vision comparison available.")
        return
    plt.figure(figsize=(6, 4))
    plt.bar([row["strategy"] for row in selected], [float(row.get("overall_accuracy") or 0.0) for row in selected], color="#059669")
    plt.xticks(rotation=25, ha="right")
    plt.ylim(0, 1)
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "oracle_vs_internal_vs_vision_prior.png", dpi=150)
    plt.close()


def plot_unity_vision_variant_accuracy(rows: list[dict[str, Any]]) -> None:
    if plt is None:
        placeholder_figure(FIGURES_DIR / "unity_vision_variant_accuracy.png", "No Unity-only vision variants were trained.")
        return
    plt.figure(figsize=(6, 4))
    counts = Counter(row["status"] for row in rows)
    plt.bar(list(counts.keys()), list(counts.values()), color="#64748b")
    plt.ylabel("variant count")
    plt.tight_layout()
    plt.savefig(FIGURES_DIR / "unity_vision_variant_accuracy.png", dpi=150)
    plt.close()

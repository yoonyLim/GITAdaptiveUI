from __future__ import annotations

import argparse
from pathlib import Path

from analysis_d2e.src.io_utils import read_csv
from analysis_d2e.src.paths import REPORTS_DIR, ensure_dirs


def _table(rows: list[dict[str, str]], columns: list[str]) -> list[str]:
    if not rows:
        return ["_no rows_"]
    lines = ["| " + " | ".join(columns) + " |", "| " + " | ".join("---" for _ in columns) + " |"]
    for row in rows:
        lines.append("| " + " | ".join(str(row.get(column, "")) for column in columns) + " |")
    return lines


def build_core_keeper_pipeline_report(output_path: Path | None = None) -> Path:
    ensure_dirs()
    output_path = output_path or (REPORTS_DIR / "final_core_keeper_offense_defense_pipeline_ko.md")
    clip_summary = read_csv(REPORTS_DIR / "core_keeper_2key_clip_summary.csv") if (REPORTS_DIR / "core_keeper_2key_clip_summary.csv").exists() else []
    state_summary = read_csv(REPORTS_DIR / "core_keeper_state_feature_summary.csv") if (REPORTS_DIR / "core_keeper_state_feature_summary.csv").exists() else []
    metrics = read_csv(REPORTS_DIR / "core_keeper_offense_defense_metrics.csv") if (REPORTS_DIR / "core_keeper_offense_defense_metrics.csv").exists() else []
    aux = read_csv(REPORTS_DIR / "threat_state_auxiliary_summary.csv") if (REPORTS_DIR / "threat_state_auxiliary_summary.csv").exists() else []
    synth = read_csv(REPORTS_DIR / "synthetic_state_ablation.csv") if (REPORTS_DIR / "synthetic_state_ablation.csv").exists() else []

    lines = [
        "# Core Keeper 중심 offense/explicit-defense situation prior 파이프라인",
        "",
        "## 요약",
        "",
        "- 기존 `melee/ranged/aoe/defense` 4-class를 바로 학습하지 않고, 더 검증 가능한 1차 문제인 `offense` vs `explicit_defense` event prior로 축소했다.",
        "- D2E-Core Keeper는 실제 화면과 keyboard/mouse event 기반 weak action label에 사용한다.",
        "- Brotato/Vampire Survivors는 action label로 쓰지 않고 enemy density/threat/state estimator 보조 데이터로만 사용한다.",
        "- Procgen/Craftax는 실제 데이터가 아니라 clean-state synthetic ablation으로 사용한다.",
        "- 모바일 touch likelihood와 skill profile은 여전히 클라이언트 telemetry에서 온다고 가정한다.",
        "",
        "## Core Keeper 2-key event clip",
        "",
    ]
    lines.extend(_table(clip_summary, ["game_id", "clips", "offense", "explicit_defense", "movement_only", "unknown_ignore", "mean_threat_score", "enemy_visible_proxy_rows", "approaching_rows"]))
    lines.extend(["", "## Core Keeper state proxy summary", ""])
    lines.extend(_table(state_summary, ["metric", "value"]))
    lines.extend(["", "## Offense/explicit-defense prior model", ""])
    lines.extend(_table(metrics, ["status", "feature_policy", "raw_clip_count", "trainable_clip_count", "train_count", "test_count", "accuracy", "macro_f1", "offense_rows", "explicit_defense_rows", "movement_only_rows"]))
    lines.extend(
        [
            "",
            "## Threat/state auxiliary data",
            "",
            "- Brotato와 Vampire Survivors는 다수 적, 밀도, 접근/위협 proxy를 강화하기 위한 보조 데이터다.",
            "- 이 데이터는 `offense/explicit_defense` action label 학습에 직접 넣지 않는다.",
            "",
        ]
    )
    lines.extend(_table(aux, ["game_id", "rows", "mean_enemy_count_proxy", "enemy_approaching_rate", "projectile_visible_proxy_rate", "mean_threat_score", "use_as_action_label"]))
    lines.extend(["", "## Synthetic clean-state ablation", ""])
    lines.extend(_table(synth, ["validation_source", "policy", "rows", "accuracy", "offense_rows", "explicit_defense_rows", "movement_only_rows", "interpretation"]))
    lines.extend(
        [
            "",
            "## 현재 해석",
            "",
            "- 이 파이프라인은 `raw input -> 최종 버튼 정답`이 아니라, `화면/상태 proxy -> offense/explicit-defense prior`를 만드는 실험이다.",
            "- `movement_only`와 `unknown_ignore`를 보존해 action label noise를 숨기지 않는다.",
            "- `enemy_positions`, `enemy_count`, `nearest_enemy_distance`, `enemy_approaching_bool`, `projectile_visible_proxy`, `player_hp_norm`은 현재 vision proxy 또는 available metadata 기반 weak state다.",
            "- 적 개별 HP, 적 공격 중 bool, 실제 skill cooldown은 아직 D2E-Core Keeper에서 안정적으로 제공되지 않는다.",
            "",
            "## 부족한 점",
            "",
            "- 현재 enemy detector는 salience/grid 기반 proxy라 진짜 객체 검출기가 아니다.",
            "- Core Keeper raw input semantics는 게임별 수동 검수가 필요하다.",
            "- `explicit_defense` label은 dodge/block/escape 계열 input proxy이지 실제 방어 의도 ground truth가 아니다.",
            "- Procgen/Craftax 결과는 clean ablation이며 실제 D2E 성능으로 주장하면 안 된다.",
            "",
            "## 다음 단계",
            "",
            "1. Core Keeper clip 중 offense/explicit_defense/movement_only를 각 100개 이상 수동 검수한다.",
            "2. enemy detector를 salience proxy에서 bounding-box/manual-label 기반 detector로 교체한다.",
            "3. `enemy_approaching_bool`과 `projectile_visible_proxy`가 defense prior에 주는 영향을 D2E heldout episode에서 재평가한다.",
            "4. 충분히 안정화되면 `melee/ranged/aoe/defense` 4-class로 확장한다.",
        ]
    )
    output_path.write_text("\n".join(lines), encoding="utf-8")
    return output_path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args()
    print(build_core_keeper_pipeline_report(args.output))


if __name__ == "__main__":
    main()

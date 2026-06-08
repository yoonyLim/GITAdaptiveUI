from __future__ import annotations

import csv

from analysis_d2e.src.paths import REPORTS_DIR, ensure_dirs


def _read_csv(path):
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def build_report() -> None:
    ensure_dirs()
    baseline_rows = _read_csv(REPORTS_DIR / "baseline_comparison.csv")
    source_prior_rows = _read_csv(REPORTS_DIR / "source_prior_metrics.csv")
    derived_rows = _read_csv(REPORTS_DIR / "derived_decoder_metrics.csv")
    train_rows = _read_csv(REPORTS_DIR / "d2e_action_prior_training_metrics.csv")
    feature_audit_rows = _read_csv(REPORTS_DIR / "d2e_feature_audit.csv")
    primary_audit_rows = _read_csv(REPORTS_DIR / "d2e_primary_subset_audit.csv")
    phase_coverage_rows = _read_csv(REPORTS_DIR / "d2e_phase_coverage_audit.csv")
    per_game_train_rows = _read_csv(REPORTS_DIR / "d2e_action_prior_per_game_metrics.csv")
    heldout_rows = _read_csv(REPORTS_DIR / "d2e_action_prior_heldout_game_metrics.csv")
    heldout_episode_rows = _read_csv(REPORTS_DIR / "d2e_action_prior_heldout_episode_metrics.csv")
    preprocess_rows = _read_csv(REPORTS_DIR / "d2e_preprocess_summary.csv")
    hf_rows = _read_csv(REPORTS_DIR / "d2e_hf_subset_download_status.csv")
    has_real_d2e = any(row.get("source_type") == "real_d2e" for row in preprocess_rows)
    phases_with_real = sorted({row.get("phase", "") for row in preprocess_rows if row.get("source_type") == "real_d2e"})
    phase_sources = sorted({row.get("phase_source", "") for row in preprocess_rows if row.get("phase_source")})
    real_games = sorted({row.get("game_id", "") for row in preprocess_rows if row.get("source_type") == "real_d2e"})
    lines = [
        "# D2E 기반 상황 행동 prior + Bayesian touch decoder 결과",
        "",
        "## 구현 범위",
        "",
        "- D2E-480p 프레임과 raw keyboard/mouse input에서 `P(action | environment)`를 학습하는 파이프라인을 추가했다.",
        "- 모델 입력 feature는 최근 프레임 history에서만 만들고, raw input은 행동 label proxy로만 사용한다.",
        "- phase는 평가/보고서 grouping에만 쓰며 action-prior model 입력 feature로 넣지 않는다.",
        "- D2E는 모바일 터치 숙련도 데이터로 사용하지 않는다.",
        "- touch_profile과 skill_profile은 모바일 클라이언트가 제공한다고 가정한다.",
        "- context 버튼은 정답 클래스가 아니라 동적 보조 슬롯이며 `visible_label`을 함께 산출한다.",
        "- 명확한 입력은 Bayesian prior가 강해도 다른 버튼으로 바꾸지 않는다.",
        "",
        "## D2E 전처리 상태",
        "",
    ]
    if hf_rows:
        lines.extend(["### Latest Hugging Face download command status", "", "| game | file | status |", "|---|---|---|"])
        for row in hf_rows:
            lines.append(f"| {row.get('game_id')} | `{row.get('repo_file')}` | {row.get('status')} |")
        lines.extend(["", "- 위 표는 마지막 download command의 상태 로그다. 실제 로컬 raw 파일 전체 현황은 아래 primary/auxiliary subset audit을 기준으로 본다.", ""])
    if preprocess_rows:
        lines.extend(["| game | phase | phase source | source | rows |", "|---|---|---|---|---:|"])
        for row in preprocess_rows:
            lines.append(f"| {row.get('game_id')} | {row.get('phase')} | {row.get('phase_source', '')} | {row.get('source_type')} | {row.get('rows')} |")
        if has_real_d2e:
            lines.extend(
                [
                    "",
                    "- 실제 D2E subset 사용: yes",
                    f"- 실제 D2E phase coverage: {', '.join(phases_with_real) if phases_with_real else 'unknown'}",
                    f"- phase source: {', '.join(phase_sources) if phase_sources else 'unknown'}",
                    "- D2E에는 이 연구의 phase_1/2/3 label이 직접 제공되지 않는다. `provided`가 아닌 phase는 metadata 또는 action-input 기반 weak proxy이며 직접 전투 시나리오 정답으로 해석하면 안 된다.",
                ]
            )
    else:
        lines.append("- 아직 실제 D2E manifest가 없거나 전처리가 실행되지 않았다.")
    if primary_audit_rows:
        lines.extend(["", "### Primary / auxiliary subset audit", "", "| game | role | raw files | raw MB | processed | usable action | low/no-action | p1 | p2 | p3 | unknown |", "|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"])
        for row in primary_audit_rows:
            lines.append(
                f"| {row.get('game_id')} | {row.get('role')} | {row.get('raw_file_count')} | {float(row.get('raw_size_mb', 0)):.1f} | {row.get('processed_rows')} | {row.get('usable_action_rows')} | {row.get('no_action_or_low_confidence_rows')} | {row.get('phase_1_rows')} | {row.get('phase_2_rows')} | {row.get('phase_3_rows')} | {row.get('unknown_rows')} |"
            )
        unprocessed = [
            row.get("game_id")
            for row in primary_audit_rows
            if int(float(row.get("raw_file_count", 0) or 0)) > 0 and int(float(row.get("processed_rows", 0) or 0)) == 0
        ]
        if unprocessed:
            lines.append(f"- 처리 주의: {', '.join(unprocessed)}는 raw 일부가 있으나 paired video 또는 decode 가능 프레임이 없어 현재 processed row가 0이다.")
    if phase_coverage_rows:
        lines.extend(["", "### Phase coverage audit", "", "| phase | rows | usable action | provided | weak proxy | metadata | needs manual annotation |", "|---|---:|---:|---:|---:|---:|---|"])
        for row in phase_coverage_rows:
            lines.append(
                f"| {row.get('phase')} | {row.get('rows')} | {row.get('usable_action_rows')} | {row.get('provided_rows')} | {row.get('weak_action_proxy_rows')} | {row.get('metadata_heuristic_rows')} | {row.get('requires_manual_annotation')} |"
            )
        low_support = [row for row in phase_coverage_rows if int(float(row.get("usable_action_rows", 0) or 0)) < 30]
        if low_support:
            summary = ", ".join(f"{row.get('phase')}={row.get('usable_action_rows')}" for row in low_support)
            lines.extend(
                [
                    "",
                    f"- 해석 주의: usable action row가 30개 미만인 phase가 있다({summary}). 해당 phase의 decoder 결과는 D2E prior가 연결되더라도 저표본 sanity check로만 해석해야 한다.",
                ]
            )
    lines.extend(["", "## Action prior model", ""])
    if train_rows:
        row = train_rows[0]
        lines.extend(
            [
                f"- status: {row.get('status')}",
                f"- feature policy: {row.get('feature_policy', 'n/a')}",
                f"- raw sample count: {row.get('raw_sample_count', row.get('sample_count', '0'))}",
                f"- sample_count: {row.get('sample_count', '0')}",
                f"- filtered low-confidence rows: {row.get('filtered_low_confidence_count', '0')}",
                f"- min label confidence: {row.get('min_label_confidence', '0')}",
                f"- primary Barony rows: {row.get('primary_subset_barony_rows', '0')}",
                f"- mean history frame count: {row.get('mean_history_frame_count', 'n/a')}",
                f"- top1 action accuracy: {row.get('top1_action_accuracy', 'n/a')}",
                f"- mean KL: {row.get('mean_target_prediction_kl', 'n/a')}",
            ]
        )
    else:
        lines.append("- 모델 학습 결과가 아직 없다.")
    if feature_audit_rows:
        row = feature_audit_rows[0]
        lines.extend(
            [
                "",
                "### Feature leakage audit",
                "",
                f"- uses frame history: {row.get('uses_frame_history')}",
                f"- uses raw input as feature: {row.get('uses_raw_input_as_feature')}",
                f"- uses phase as feature: {row.get('uses_phase_as_feature')}",
                f"- raw input role: {row.get('raw_input_role')}",
                f"- phase role: {row.get('phase_role')}",
            ]
        )
    if per_game_train_rows:
        lines.extend(["", "### Per-game action prior metrics", "", "| game | samples | top1 acc | mean KL |", "|---|---:|---:|---:|"])
        for row in per_game_train_rows:
            lines.append(
                f"| {row.get('game_id')} | {row.get('sample_count')} | {float(row.get('top1_action_accuracy', 0)):.3f} | {float(row.get('mean_target_prediction_kl', 0)):.3f} |"
            )
    if heldout_rows:
        lines.extend(["", "### Held-out game generalization", "", "| heldout game | train | test | top1 acc | mean KL |", "|---|---:|---:|---:|---:|"])
        for row in heldout_rows:
            lines.append(
                f"| {row.get('heldout_game_id')} | {row.get('train_count')} | {row.get('test_count')} | {float(row.get('top1_action_accuracy', 0)):.3f} | {float(row.get('mean_target_prediction_kl', 0)):.3f} |"
            )
    if heldout_episode_rows:
        lines.extend(
            [
                "",
                "### Held-out recording generalization",
                "",
                "| game | heldout recording | train | test | top1 acc | mean KL |",
                "|---|---|---:|---:|---:|---:|",
            ]
        )
        for row in heldout_episode_rows:
            episode = str(row.get("heldout_episode_id", ""))
            short_episode = episode if len(episode) <= 48 else episode[:45] + "..."
            lines.append(
                f"| {row.get('heldout_game_id')} | `{short_episode}` | {row.get('train_count')} | {row.get('test_count')} | {float(row.get('top1_action_accuracy', 0)):.3f} | {float(row.get('mean_target_prediction_kl', 0)):.3f} |"
            )
        avg_acc = sum(float(row.get("top1_action_accuracy", 0)) for row in heldout_episode_rows) / max(1, len(heldout_episode_rows))
        avg_kl = sum(float(row.get("mean_target_prediction_kl", 0)) for row in heldout_episode_rows) / max(1, len(heldout_episode_rows))
        lines.extend(
            [
                "",
                f"- Held-out recording 평균 top1 accuracy: {avg_acc:.3f}, 평균 KL: {avg_kl:.3f}. 이 지표는 같은 recording의 인접 프레임 누수 가능성을 줄인 더 엄격한 sanity check다.",
            ]
        )
    lines.extend(["", "## Baseline comparison", ""])
    if baseline_rows:
        lines.extend(["| baseline | trials | misinput | correction success | overcorrection | context misfire | margin |", "|---|---:|---:|---:|---:|---:|---:|"])
        for row in baseline_rows:
            lines.append(
                f"| {row.get('baseline')} | {row.get('trials')} | {float(row.get('misinput_rate', 0)):.3f} | {float(row.get('correction_success_rate', 0)):.3f} | {float(row.get('overcorrection_rate', 0)):.3f} | {float(row.get('context_misfire_rate', 0)):.3f} | {float(row.get('posterior_margin_mean', 0)):.3f} |"
            )
        by_baseline = {row.get("baseline"): row for row in baseline_rows}
        skill_row = by_baseline.get("SituationUserSkillBayesian")
        user_row = by_baseline.get("UserSpecificHitbox")
        expanded_row = by_baseline.get("UniformExpandedHitbox")
        if skill_row and user_row:
            skill_mis = float(skill_row.get("misinput_rate", 0))
            user_mis = float(user_row.get("misinput_rate", 0))
            skill_corr = float(skill_row.get("correction_success_rate", 0))
            user_corr = float(user_row.get("correction_success_rate", 0))
            lines.extend(
                [
                    "",
                    "### Decoder interpretation",
                    "",
                    f"- SituationUserSkillBayesian은 UserSpecificHitbox 대비 misinput을 {user_mis - skill_mis:.3f} 낮추고 correction success를 {skill_corr - user_corr:.3f} 높였다.",
                    "- 이 결과는 현재 synthetic touch/profile simulation 기준이며, 실제 모바일 사용자 telemetry 검증을 대체하지 않는다.",
                ]
            )
        if expanded_row and skill_row and float(expanded_row.get("misinput_rate", 0)) < float(skill_row.get("misinput_rate", 0)):
            lines.append("- 주의: 이 실행에서는 UniformExpandedHitbox가 skill-aware Bayesian보다 raw misinput이 낮다. safety threshold와 prior 품질을 재점검해야 한다.")
    else:
        lines.append("- baseline 평가가 아직 실행되지 않았다.")
    lines.extend(["", "## Prior source comparison", ""])
    if source_prior_rows:
        lines.extend(["| source prior | baseline | phase | trials | misinput | correction success | overcorrection |", "|---|---|---|---:|---:|---:|---:|"])
        for row in source_prior_rows:
            lines.append(
                f"| {row.get('source_prior')} | {row.get('baseline')} | {row.get('phase')} | {row.get('trials')} | {float(row.get('misinput_rate', 0)):.3f} | {float(row.get('correction_success_rate', 0)):.3f} | {float(row.get('overcorrection_rate', 0)):.3f} |"
            )
    else:
        lines.append("- prior source별 평가가 아직 없다.")
    lines.extend(["", "## Derived metrics", ""])
    if derived_rows:
        lines.extend(["| metric | baseline | phase | N | value |", "|---|---|---|---:|---:|"])
        for row in derived_rows:
            lines.append(
                f"| {row.get('metric')} | {row.get('baseline', '')} | {row.get('phase', '')} | {row.get('n_buttons', '')} | {float(row.get('value', 0)):.3f} |"
            )
    else:
        lines.append("- derived metric 결과가 아직 없다.")
    lines.extend(
        [
            "",
            "## 실제 모바일 클라이언트 연동",
            "",
            "1. 클라이언트는 버튼 레이아웃, raw touch, touch_profile, skill_profile을 전송한다.",
            "2. 서버/온디바이스 모델은 최근 N프레임에서 atomic action prior를 산출한다.",
            "3. `ActionToNButtonProjector`가 N=2/3/4 버튼 prior로 변환한다.",
            "4. `ContextButtonPolicy`는 context action과 visible_label을 정한다.",
            "5. `BayesianInputDecoder`는 ambiguous touch에서만 posterior 보정을 적용한다.",
            "",
            "## Phase annotation handoff",
            "",
            "- `analysis_d2e/outputs/reports/phase_annotation_template.csv`: 게임별 대표 샘플 수동 phase 라벨링 템플릿.",
            "- `analysis_d2e/outputs/reports/phase_annotation_unknown_candidates.csv`: 현재 `unknown`으로 남은 샘플 중 phase_2 후보를 찾기 위한 템플릿.",
            "- 현재 자동 weak proxy의 phase_2 표본은 매우 작으므로, Phase 2 실험 주장은 이 파일을 통한 수동 annotation 또는 추가 D2E 구간 탐색 뒤에만 강화할 수 있다.",
            "",
            "## 한계",
            "",
            f"- 현재 실제 D2E 검증은 {len(real_games)}개 게임({', '.join(real_games) if real_games else 'none'})의 제한된 recording 부분 추출이다. 전체 D2E나 모든 후보 게임 성능으로 주장할 수 없다.",
            "- D2E에는 이 연구의 phase_1/2/3 label이 직접 제공되지 않으므로 phase 구분은 별도 annotation 또는 heuristic이 필요하다.",
            "- raw keyboard/mouse input은 행동 label proxy이며 사용자 의도 자체가 아니다.",
            "- 모바일 터치 좌표와 숙련도는 D2E가 아니라 클라이언트 telemetry로 검증해야 한다.",
            "- 모델 E가 항상 최고라는 보장은 없으며, expert 조건에서는 overcorrection risk를 별도로 봐야 한다.",
        ]
    )
    (REPORTS_DIR / "final_d2e_bayesian_decoder_report_ko.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    build_report()
    print(REPORTS_DIR / "final_d2e_bayesian_decoder_report_ko.md")


if __name__ == "__main__":
    main()

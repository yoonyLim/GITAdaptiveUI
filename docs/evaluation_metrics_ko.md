# Evaluation Metrics

주요 metric은 다음과 같다.

- overall accuracy
- per-condition accuracy
- per-scenario accuracy
- ambiguous_subset_accuracy
- clear_subset_accuracy
- invalid_touch_rate
- correction_success_rate
- overcorrection_rate
- no_correction_when_uncertain_rate
- reaction_time_mean
- hp_preservation
- damage_taken_mean
- survival_rate
- cooldown_waste_rate

`correction_success_rate`는 visual boundary가 required action을 틀린 ambiguous case에서 최종 action이 required action으로 복구된 비율이다.

`overcorrection_rate`는 visual boundary가 이미 맞았는데 Bayesian/safety 결과가 required action을 망가뜨린 비율이다. 이 값은 adaptive correction의 핵심 위험 지표다.

## 모드 / 정책 평가

추가 구현된 mode/policy layer는 다음 리포트로 확인한다.

- `mode_policy_summary.csv`: interaction mode별 trial 수, accuracy, correction success, overcorrection, 평균 correction strength, 평균 hitbox expansion, haptic/guidance/review 표시율.
- `trust_control_survey_summary.csv`: optional survey row 수와 trust/control/predictability 평균.
- `mode_policy_distribution.png`: smoke 또는 실제 세션에서 어떤 모드가 얼마나 사용됐는지 보여준다.

설문 점수는 실제 participant가 응답한 경우에만 신뢰/통제감 주장에 사용할 수 있다. synthetic fixture의 설문 row는 pipeline test 용도다.

# Experiment Protocol

## Calibration

- 기본 30 Attack taps, 30 Dodge taps.
- 순서는 randomized.
- `intended_action`은 highlighted button이며 `label_source=calibration_instruction`.
- enemy pressure 없이 user-specific Attack/Dodge touch distribution을 학습한다.

## Main Controlled Phase

기본 240 trials per participant를 목표로 한다. 조건, scenario, required action이 균형을 이루도록 구성한다.

- Safe / Idle: required_action=Attack, Attack prior high.
- Telegraph / Warning: required_action=Dodge, Dodge prior high.
- Attacking / Urgent: required_action=Dodge, Dodge prior stronger.
- Neutral: prior near uniform, required action randomized/instructed.

Trial type은 clear, near-boundary, ambiguous, outside-but-recoverable, invalid-far를 포함한다. Trial type은 instruction/scenario를 제어할 뿐 실제 user touch coordinate를 조작하지 않는다.

## Freeplay

선택 phase다. `intended_action`이 없을 수 있으므로 scenario rule 또는 HP/survival outcome 중심으로 해석한다.

## Optional Mode / Survey Flow

실험 중 `InteractionDemandModel`은 상태별로 `ActionFirst`, `CognitiveFirst`, `GuidanceProcedure`, `LearningReview` 중 하나를 선택할 수 있다. Editor panel에서는 manual mode override도 가능하다.

주관 설문은 선택 항목이다.

- trust score
- control score
- predictability score
- free text

이 설문은 실제 participant가 응답했을 때만 신뢰/통제감 분석에 사용한다. smoke fixture의 설문 row는 pipeline 검증용이다.

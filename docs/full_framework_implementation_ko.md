# 전체 ADUI 구조 구현 상태

현재 Unity 구현은 보고서의 큰 구조를 실행 가능한 형태로 확장했다. 다만 검증 범위는 여전히 `action_first -> interaction_error_tolerance -> ambiguity-gated Bayesian input decoding`을 중심으로 둔다.

## 구현된 모드

- `ActionFirst`: 시간 압박과 조작 밀도가 높을 때 Attack/Dodge 보정, hitbox expansion, haptic/강한 feedback을 사용한다.
- `CognitiveFirst`: 정보 우선순위와 가림 위험이 높을 때 HUD visibility/density/position constraint를 조절한다.
- `GuidanceProcedure`: 반복 invalid/불확실 입력이 많거나 skill proxy가 낮을 때 guidance panel과 강한 feedback을 표시한다.
- `LearningReview`: 압박이 낮은 상태에서 review panel과 낮은 correction strength를 사용한다.

## 구현된 조절 변수

- visibility
- emphasis
- density
- position constraint
- interaction error tolerance
- correction strength
- hitbox expansion ratio
- ambiguity margin
- haptic feedback
- guidance/review visibility

## Unity 연결

- `InteractionDemandModel`: enemy state, urgency, HP, action density, invalid/overcorrection signal로 demand를 계산한다.
- `AdaptiveUIPolicyEngine`: demand를 실제 UI policy 값으로 변환한다.
- `AdaptiveUIAdjustmentController`: CanvasGroup, button tint, button position constraint를 적용한다.
- `UserCorrectionSettings`: 사용자가 correction strength, correction on/off, haptic on/off를 조절한다.
- `ADUIFeedbackController`: invalid/corrected/clear input feedback과 haptic을 처리한다.
- `TrustControlSurveyManager`: optional trust/control/predictability 설문을 JSONL로 저장한다.
- `ModePolicyLogger`: 모드와 policy snapshot을 `mode_policy_events.jsonl`로 저장한다.

## 분석 연결

Unity analysis pipeline은 다음 추가 산출물을 만든다.

- `analysis_unity/outputs/reports/mode_policy_summary.csv`
- `analysis_unity/outputs/reports/trust_control_survey_summary.csv`
- `analysis_unity/outputs/figures/mode_policy_distribution.png`

## 해석 한계

- 공개 game touch logs는 touch dynamics 근거이지 Attack/Dodge correction 직접 검증이 아니다.
- TSI는 target-selection sanity check이지 game-state prior 검증이 아니다.
- CognitiveFirst/Guidance/LearningReview는 구현과 로깅이 되었지만, 사용자 신뢰/통제감 주장은 실제 사용자 설문이 있어야 가능하다.
- 현재 smoke fixture는 pipeline 검사용 synthetic data이며 실제 participant data가 아니다.

# Model Training

주 모델은 action_first mode를 유지한다.

```text
P(a_i | x_t, s_t, u) ∝ P(x_t | a_i, u, B_t) * P(a_i | s_t)
```

- `a_i`: Attack 또는 Dodge.
- `x_t`: raw touch coordinate.
- `s_t`: enemy/game state.
- `u`: user-specific touch profile.
- `B_t`: current button layout.

Calibration에서는 touch coordinate를 button-relative coordinate로 변환한다.

```text
r_x = (touch_x - button_center_x) / button_radius
r_y = (touch_y - button_center_y) / button_radius
```

Attack/Dodge별 mean과 variance를 추정하고 `user_touch_profile.json`으로 저장한다. Calibration 전에는 public target-selection data에서 나온 default variance와 hitbox margin을 사용할 수 있지만, 성능 주장은 Unity telemetry 기반으로만 한다.

## 상위 적응 모드와 정책

보고서의 전체 구조에 맞춰 Unity에는 네 개의 모드가 구현되어 있다.

- `ActionFirst`: 현재 직접 평가 대상이다. Attack/Dodge 입력 보정, error tolerance, safety gate가 핵심이다.
- `CognitiveFirst`: 정보 우선순위와 가림 위험이 높을 때 visibility/density/position constraint를 조절한다.
- `GuidanceProcedure`: 반복적인 invalid touch나 낮은 skill proxy에서 guidance UI와 강한 feedback을 사용한다.
- `LearningReview`: 낮은 압박 상태에서 review UI와 낮은 correction strength를 사용한다.

`InteractionDemandModel`이 action intensity, temporal urgency, information priority, occlusion risk, control continuity, ui skill을 계산하고, `AdaptiveUIPolicyEngine`이 이를 visibility/emphasis/density/position/error tolerance/correction strength/hitbox expansion 정책으로 바꾼다.

주의: 현재 직접 성능 검증은 Attack/Dodge telemetry에 한정한다. Cognitive/guidance/learning-review의 주관적 효과는 실제 사용자 설문과 실험이 있어야 주장할 수 있다.

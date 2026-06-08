# D2E prior + 모바일 Bayesian decoder 런타임 연동 스펙

이 문서는 모바일 클라이언트가 제공하는 touch/profile 정보를 D2E 기반 action prior와 결합해 최종 버튼 입력을 판정하는 최소 JSON 경계를 정의한다.

## 역할 분리

- D2E action prior model: 최근 프레임 history에서 `P(action | environment)`를 산출한다.
- raw keyboard/mouse input: D2E 학습 label proxy로만 사용한다. 런타임 feature로 쓰지 않는다.
- touch_profile: 클라이언트가 수집한 사용자별 터치 분포다.
- skill_profile: prior weight `lambda`, threshold `tau`, margin `delta`, correction radius, feedback intensity를 조절한다.
- BayesianInputDecoder: ambiguous touch에서만 보정을 적용한다.
- Context button: 정답 클래스가 아니라 동적 보조 슬롯이며, `visible_label`을 반드시 UI에 표시해야 한다.

## 요청 JSON

```json
{
  "n_buttons": 4,
  "frame_sample": {
    "history_frame_paths": [
      "C:/path/frame_0001.png",
      "C:/path/frame_0002.png"
    ]
  },
  "atomic_distribution": {
    "attack": 0.12,
    "defense": 0.22,
    "dodge": 0.42,
    "skill": 0.08,
    "heal": 0.08,
    "escape": 0.08
  },
  "situation": {
    "phase": "phase_3",
    "player_hp_norm": 0.55,
    "melee_enemy_count": 4,
    "ranged_enemy_count": 1,
    "boss_visible": true,
    "boss_telegraph": true
  },
  "button_layout": [
    {"button_id": "attack", "action": "attack", "center_x": 100, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
    {"button_id": "defense", "action": "defense", "center_x": 180, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
    {"button_id": "skill", "action": "skill", "center_x": 260, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45},
    {"button_id": "context", "action": "context", "center_x": 340, "center_y": 200, "visual_radius": 30, "hitbox_radius": 45}
  ],
  "touch": {"x": 220, "y": 200},
  "touch_profile": {
    "touch_variance": 700,
    "near_miss_rate": 0.14
  },
  "skill_profile": {
    "skill_level": "beginner",
    "reaction_time_mean": 430,
    "reinput_rate": 0.18,
    "joystick_drift": 0.20,
    "cooldown_misuse_rate": 0.20,
    "context_button_understanding": 0.35
  },
  "use_skill": true
}
```

`atomic_distribution`이 있으면 클라이언트/상위 모델이 제공한 prior를 그대로 사용한다. 없고 `frame_sample`이 있으면 저장된 D2E action prior model로 예측한다. 둘 다 없으면 uniform fallback을 사용하며, 이 경우 성능 주장에 쓰면 안 된다.

## 응답 JSON

```json
{
  "predicted_button": "defense",
  "invalid_touch": false,
  "corrected": true,
  "safety_gate_passed": true,
  "safety_gate_reason": "correction_applied",
  "visual_prediction": "attack",
  "expanded_prediction": "attack",
  "posterior": {"attack": 0.21, "defense": 0.73, "skill": 0.04, "context": 0.02},
  "button_prior": {"attack": 0.12, "defense": 0.49, "skill": 0.08, "context": 0.31},
  "prior_source": "d2e_action_prior_model",
  "context": {
    "context_action": "dodge",
    "visible_label": "Dodge",
    "confidence": 0.42
  },
  "skill_control": {
    "lambda": 0.8,
    "tau": 0.65,
    "delta": 0.15,
    "correction_radius": 1.46,
    "feedback_intensity": 0.85,
    "context_button_emphasis": 0.51
  }
}
```

## 실행 명령

```powershell
uv run --with pillow python -m analysis_d2e.src.runtime_decode --request .\request.json
```

표준 입력으로도 실행할 수 있다.

```powershell
Get-Content .\request.json | uv run --with pillow python -m analysis_d2e.src.runtime_decode
```

## 안전 규칙

1. `visual_radius` 내부의 명확한 입력은 보정하지 않는다.
2. recoverable radius 밖의 입력은 invalid로 둔다.
3. posterior가 `tau`보다 낮으면 보정하지 않는다.
4. 1등과 2등 posterior 차이가 `delta`보다 작으면 보정하지 않는다.
5. 선택 버튼이 cooldown 또는 executable 조건을 만족하지 않으면 보정하지 않는다.
6. context 버튼은 항상 `visible_label`을 보여준 뒤에만 눌릴 수 있는 보조 슬롯으로 취급한다.

## 현재 한계

- 현재 D2E subset은 Barony/Brotato/Core Keeper 일부 recording만 사용한다.
- D2E에는 모바일 터치 좌표가 없으므로 touch_profile은 실제 클라이언트 telemetry로 수집해야 한다.
- D2E에는 이 연구의 phase label이 직접 없으므로 phase는 보고서 grouping/annotation 대상으로만 사용한다.
- 현재 runtime decode는 Python reference implementation이다. Unity/모바일 배포에는 C# 포팅, ONNX/Sentis 또는 서버 추론 경계가 추가로 필요하다.

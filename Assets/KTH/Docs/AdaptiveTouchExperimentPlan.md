# Adaptive Touch Experiment Plan

이 문서는 현재 `AdaptivePrototype` 구현 기준으로 사용자 평가를 진행하기 위한 실험 구조와 마무리 질문을 정리한 것이다.

현재 데모의 핵심 주장은 "화면 상황에서 플레이어의 정답 행동을 완벽히 예측한다"가 아니다. 현재 구현의 평가 목표는 전투 중 작은 4버튼 입력에서 발생하는 오터치, 경계 터치, 이동 중 터치 불안정성을 Gaussian/Bayesian 기반 입력 보정이 얼마나 줄이는지 확인하는 것이다.

## 1. 실험 목적

본 실험은 모바일 탑다운 액션 게임 환경에서 다음 질문을 검증한다.

1. 기본 고정 버튼 대비, 상황 기반 Bayesian 입력 보정이 오터치와 조작 실패를 줄이는가?
2. 사용자별 calibration을 추가하면 보정 결과의 안정성, 납득성, 조작 만족도가 더 좋아지는가?
3. 전투 중 긴급도, 적 밀집도, HP, 쿨다운, 이동 입력이 결합된 상황에서 adaptive touch framework가 실제 플레이에 도움이 되는가?

핵심 평가는 action prediction accuracy가 아니라 다음 항목이다.

- 오터치율
- invalid touch 비율
- rejected / corrected / preserved 비율
- cooldown wasted 비율
- damage taken
- stage clear/fail
- 평균 touch error
- 사용자 주관 만족도
- 보정 결과의 납득성
- 모바일 액션 게임 적용 가능성

## 2. 실험 조건

현재 구현은 세 조건 비교가 가장 자연스럽다.

### 2.1 Raw Button

기본 버튼만 사용하는 조건이다.

- 조건명: `raw_button`
- 화면 표시: `Mode: RAW`
- Gaussian/Bayesian 보정 없음
- 버튼 hitbox 밖 터치는 실패 처리
- 기준선 조건

이 조건은 "그냥 작은 버튼을 누르는 모바일 액션 게임"을 대표한다.

### 2.2 Adaptive Without Calibration

전투 상황 기반 prior와 Bayesian decoding은 사용하지만, 사용자별 calibration은 수행하지 않는 조건이다.

- 조건명: `no_calibration_context_bayesian`
- 화면 표시: `Adaptive: ON`
- 사용자별 touch bias/spread 없음
- CombatContext 기반 action prior 사용
- Bayesian decoder + SafetyGate 사용
- runtime online adaptation 일부 반영

이 조건은 "사용자별 보정 없이, 현재 전투 상황만으로 입력 보정을 해도 효과가 있는가"를 확인하기 위한 조건이다.

### 2.3 Adaptive With Calibration

시작 전 4버튼 calibration을 수행한 뒤 adaptive 보정을 사용하는 조건이다.

- 조건명: `context_bayesian_calibrated`
- 화면 표시: `Adaptive: ON`
- 사용자별 버튼별 touch bias/spread 반영
- CombatContext 기반 action prior 사용
- Bayesian decoder + SafetyGate 사용
- calibration 이후 online adaptation 반영

이 조건은 최종 제안 방식이다. 사용자의 실제 터치 위치 편향과 전투 상황을 함께 사용한다.

## 3. 스테이지 구성

현재 게임은 3개 스테이지로 구성된다.

각 스테이지는 최대 30초로 제한한다. 30초가 지나면 해당 스테이지는 실패가 아니라 종료된 것으로 기록하고 다음 스테이지로 넘어간다. 플레이어가 사망하면 해당 조건은 즉시 종료된다.

### Stage 1: Attack Window

목적:

- 기본 공격 기회와 근접 압박 상황을 만든다.
- Attack과 Dodge 판단이 빈번하게 발생한다.
- 작은 버튼, 경계 터치, 빠른 반복 입력에서 차이를 보기 좋다.

주요 관찰:

- Attack 실행 안정성
- 불필요한 Dodge 보정 여부
- invalid touch 비율
- damage taken

### Stage 2: Move Pressure

목적:

- 조이스틱 이동 중 오른손 버튼 입력 상황을 만든다.
- 이동과 스킬 사용이 동시에 일어나는 bimanual interaction을 확인한다.
- MovingUnderPressure, PreDodgeWindow 계열의 상황을 유도한다.

주요 관찰:

- 조이스틱을 잡은 상태의 터치 안정성
- Dodge 보정의 납득성
- movement + action continuity
- damage taken

### Stage 3: Boss Low HP Threat

목적:

- 보스, 낮은 HP, 강한 위협, Heal/Dodge 충돌 상황을 만든다.
- LowHpThreat 상황에서 adaptive prior와 safety gate의 가치가 드러난다.
- Heal, Dodge, Whirlwind가 충돌하는 상황을 확인한다.

주요 관찰:

- Low HP에서 Heal과 Dodge의 충돌 처리
- boss HP / player HP 가시성
- 강한 위협에서 보정이 과하게 느껴지는지
- 최종 생존성

## 4. 실험 진행 흐름

### 전체 흐름

1. 참가자 번호 입력
2. 실험 목적과 조작 방법 안내
3. 조건 순서 배정
4. 조건 1 플레이
5. 조건 1 직후 설문
6. 조건 2 플레이
7. 조건 2 직후 설문
8. 조건 3 플레이
9. 조건 3 직후 설문
10. 최종 비교 설문
11. 각 조건 결과 화면의 `Copy Time-Series JSON` 버튼으로 로그 회수

### 조건별 플레이 흐름

각 조건에서 다음 흐름을 따른다.

1. 시작 버튼 선택
2. 필요 시 calibration 수행
3. Stage 1 플레이, 최대 30초
4. Stage 2 플레이, 최대 30초
5. Stage 3 플레이, 최대 30초
6. 사망하거나 Stage 3이 끝나면 결과 화면 표시
7. `Copy Time-Series JSON` 버튼 클릭
8. 복사된 JSON을 카카오톡, 메모, 메일, 또는 지정 수집 채널에 전송
9. 조건별 설문 작성

## 5. 조건 순서 배정

순서효과를 줄이기 위해 조건 순서는 참가자마다 바꾼다.

권장 순서:

| Participant | Order 1 | Order 2 | Order 3 |
|---|---|---|---|
| P01 | Raw Button | Adaptive Without Calibration | Adaptive With Calibration |
| P02 | Adaptive Without Calibration | Adaptive With Calibration | Raw Button |
| P03 | Adaptive With Calibration | Raw Button | Adaptive Without Calibration |
| P04 | Raw Button | Adaptive With Calibration | Adaptive Without Calibration |
| P05 | Adaptive Without Calibration | Raw Button | Adaptive With Calibration |
| P06 | Adaptive With Calibration | Adaptive Without Calibration | Raw Button |

참가자가 더 많으면 위 순서를 반복한다.

## 6. 예상 소요 시간

| 단계 | 예상 시간 |
|---|---:|
| 실험 설명 | 1-2분 |
| 조건당 플레이 | 최대 1분 30초 |
| 조건당 설문 | 1-2분 |
| 3조건 총 플레이 + 설문 | 9-12분 |
| 최종 비교 설문 | 2-3분 |
| 전체 | 약 12-15분 |

실험이 길어지는 경우 calibration 조건의 설명 시간을 줄이고, 참가자가 조작에 익숙해질 수 있도록 짧은 연습만 제공한다.

## 7. 자동 수집 로그

결과 화면에서 `Copy Time-Series JSON` 버튼을 누르면 해당 세션의 `final_session_logs.json` 전체가 클립보드에 복사된다.

복사되는 주요 로그는 다음이다.

- `session_meta.json`
- `session_final_summary.json`
- `calibration_events.jsonl`
- `evaluation_stage_summary.jsonl`
- `evaluation_touch_events.jsonl`
- `main_trials.jsonl`
- `mode_policy_events.jsonl`
- `showcase_events.jsonl`이 있으면 포함
- CSV/TXT 로그가 있으면 `text_logs`에 포함

### 주요 정량 지표

`evaluation_stage_summary.jsonl`에서 조건별/스테이지별로 다음 값을 본다.

- `stage_number`
- `stage_label`
- `skipped`
- `failed`
- `duration_seconds`
- `button_presses`
- `touch_events`
- `expected_match_count`
- `mis_touch_count`
- `invalid_touch_count`
- `rejected_count`
- `preserved_count`
- `corrected_count`
- `ambiguous_count`
- `cooldown_wasted_count`
- `action_first_count`
- `cognitive_first_count`
- `damage_taken`
- `healing_done`
- `final_hp`
- `enemies_remaining`
- `avg_touch_error_px`
- `avg_posterior_gap`
- `avg_policy_error_tolerance`
- `avg_policy_correction_strength`

`main_trials.jsonl`과 `evaluation_touch_events.jsonl`에서는 개별 입력 단위의 세부 정보를 본다.

- `touch_x`, `touch_y`
- 버튼 중심과의 거리
- visual / expanded / Gaussian prediction
- prior / posterior
- posterior gap
- final executed action
- invalid touch
- safety gate pass/reason
- corrected 여부
- cooldown wasted 여부
- player HP 변화
- enemy HP 변화
- demand 6개 값
- policy 5개 값

## 8. 분석 계획

### 8.1 Raw vs Adaptive Without Calibration

질문:

상황 기반 Bayesian 보정만으로 기본 버튼보다 나은가?

비교 지표:

- invalid touch 감소
- rejected 감소
- cooldown wasted 감소
- damage taken 감소
- final HP 증가
- corrected touch 중 납득 가능한 비율
- 사용자 만족도 증가

해석:

Raw보다 NoCal이 좋으면, CombatContext 기반 prior와 Bayesian decoder만으로도 의미 있는 보정 효과가 있다고 주장할 수 있다.

### 8.2 Adaptive Without Calibration vs Adaptive With Calibration

질문:

사용자별 calibration이 추가되면 더 좋아지는가?

비교 지표:

- avg touch error 감소
- ambiguous touch 처리 만족도 증가
- corrected touch의 납득성 증가
- 조이스틱 이동 중 버튼 입력 안정성 증가
- 과보정 불만 감소

해석:

Calibrated가 NoCal보다 좋으면, 개인별 touch bias/spread를 반영하는 calibration의 필요성을 주장할 수 있다.

### 8.3 Raw vs Adaptive With Calibration

질문:

최종 제안 방식이 기본 버튼 대비 실제로 도움이 되는가?

비교 지표:

- stage completion
- damage taken
- invalid/rejected/cooldown wasted
- 주관적 조작 만족도
- 신뢰성
- 모바일 액션 게임 적용 가능성

해석:

Calibrated가 Raw보다 좋으면, 전체 framework가 기본 모바일 버튼 UI보다 오터치 문제를 완화한다고 주장할 수 있다.

## 9. 조건별 설문

각 조건이 끝난 직후 7점 Likert 척도로 응답한다.

척도:

```text
1 = 전혀 동의하지 않는다
7 = 매우 동의한다
```

### 공통 질문

1. 버튼을 눌렀을 때 실행된 행동이 내가 의도한 행동과 잘 맞았다.
2. 버튼 경계 근처를 눌렀을 때 처리 방식이 납득 가능했다.
3. 빠르게 연속 입력하는 상황에서도 조작이 안정적이었다.
4. 조이스틱으로 이동하면서 오른손 버튼을 누르는 흐름이 자연스러웠다.
5. 전투 중 캐릭터, 적, 투사체, HP 상태에 집중하기 쉬웠다.
6. UI 버튼과 쿨다운 표시는 이해하기 쉬웠다.
7. 의도하지 않은 스킬 발동이나 조작 실수로 인한 답답함이 적었다.
8. 현재 방식은 모바일 액션 게임에서 유용할 것 같았다.
9. 현재 방식은 공정하고 신뢰할 수 있다고 느꼈다.
10. 전체적으로 이 조건의 조작감에 만족했다.

### Adaptive 조건 전용 질문

Adaptive Without Calibration과 Adaptive With Calibration 조건에서만 묻는다.

1. 입력 보정이 전투 중 조작 실수를 줄이는 데 도움이 되었다.
2. 애매한 터치를 시스템이 다른 행동으로 해석했을 때 그 결과가 납득 가능했다.
3. 자동 보정이 내 조작을 과하게 바꾼다고 느끼지 않았다.
4. 전투 상황에 따라 보정 강도가 달라지는 방식이 자연스럽게 느껴졌다.
5. 보정 결과를 신뢰하고 계속 플레이할 수 있다고 느꼈다.

### Calibration 조건 전용 질문

Adaptive With Calibration 조건에서만 묻는다.

1. 시작 전 calibration 과정이 이후 플레이에 도움이 된다고 느꼈다.
2. calibration에 걸린 시간은 받아들일 수 있는 수준이었다.
3. calibration 이후 버튼 입력이 내 손 위치 습관에 더 잘 맞는다고 느꼈다.
4. 이동 중 버튼 입력, 빠른 전환 입력, 경계 입력을 calibration에 포함한 것이 적절하다고 느꼈다.
5. calibration을 한 번 수행하고 이후 게임에 적용하는 방식이 실제 모바일 게임에서도 가능하다고 느꼈다.

## 10. 최종 비교 질문

모든 조건이 끝난 뒤 묻는다.

### 선택형 질문

1. 세 조건 중 가장 조작하기 쉬웠던 것은 무엇인가?
   - Raw Button
   - Adaptive Without Calibration
   - Adaptive With Calibration

2. 세 조건 중 가장 실수가 적었다고 느낀 것은 무엇인가?
   - Raw Button
   - Adaptive Without Calibration
   - Adaptive With Calibration

3. 세 조건 중 가장 공정하다고 느낀 것은 무엇인가?
   - Raw Button
   - Adaptive Without Calibration
   - Adaptive With Calibration

4. 세 조건 중 실제 모바일 게임에 적용된다면 가장 쓰고 싶은 것은 무엇인가?
   - Raw Button
   - Adaptive Without Calibration
   - Adaptive With Calibration

5. 가장 불편했던 조건은 무엇인가?
   - Raw Button
   - Adaptive Without Calibration
   - Adaptive With Calibration

### 7점 척도 질문

1. Adaptive With Calibration은 Raw Button보다 전투 중 오터치를 줄이는 데 도움이 되었다.
2. Adaptive With Calibration은 Adaptive Without Calibration보다 내 터치 습관에 더 잘 맞았다.
3. 자동 입력 보정은 게임 조작을 더 편하게 만들었다.
4. 자동 입력 보정은 게임을 불공정하게 만든다고 느끼지 않았다.
5. calibration을 매 게임 시작 전 또는 최초 1회 수행하는 것은 받아들일 수 있다.
6. 이 시스템은 모바일 액션 게임의 작은 버튼 오작동 문제를 줄이는 데 의미가 있다.

### 자유 응답 질문

1. 자동 보정이 가장 도움이 된 상황은 언제였는가?
2. 자동 보정이 어색하거나 불공정하게 느껴진 상황은 언제였는가?
3. 버튼 크기, 버튼 위치, 쿨다운 표시, HP 표시 중 개선이 필요한 부분은 무엇인가?
4. calibration 과정에서 불필요하거나 어려웠던 부분은 무엇인가?
5. 반대로 calibration에 더 추가하면 좋을 것 같은 상황은 무엇인가?
6. 실제 모바일 게임에 이 시스템이 들어간다면 어떤 방식으로 제공되는 것이 좋겠는가?
7. 전체적으로 이 시스템이 오터치 문제를 줄이는 데 의미 있다고 느꼈는가? 이유를 함께 적어 달라.

## 11. 실험자 체크리스트

실험 전:

- 빌드 실행 확인
- 참가자 번호 입력 가능 여부 확인
- 세 조건 시작 버튼 확인
- 4버튼이 80px 크기로 보이는지 확인
- Player HP가 중앙 하단에 보이는지 확인
- Boss HP가 Stage 3에서 보이는지 확인
- stage UI에 `Max 30s`가 보이는지 확인
- 결과 화면에 `Copy Time-Series JSON` 버튼이 보이는지 확인

조건 종료 후:

- 결과 화면에서 stage time이 30초 이하인지 확인
- `Copy Time-Series JSON` 버튼 클릭
- 복사된 JSON을 수집 채널에 붙여넣기
- 카카오톡에 직접 붙여넣기 어렵다면 `COPY_ALL_FINAL_SESSION_LOGS.txt` 파일로 전달
- 조건별 설문 작성 완료 확인

실험 후:

- 참가자별 세 조건 로그가 모두 있는지 확인
- 참가자별 최종 비교 설문이 있는지 확인
- 파일명이 참가자 번호와 조건을 구분할 수 있는지 확인

## 12. 보고서 작성 시 주장 구조

보고서에서는 다음 순서로 주장하는 것이 현재 구현과 가장 잘 맞는다.

1. 모바일 액션 게임의 4버튼 조작은 작은 목표, 이동 중 입력, 긴급 반응, 손가락 가림 때문에 오터치가 발생하기 쉽다.
2. 단순히 버튼을 크게 만드는 방식은 화면 가림과 게임 정보 가시성 문제를 만든다.
3. 따라서 버튼을 크게 보이게 하는 대신, 입력 위치의 불확실성을 Gaussian touch model로 표현하고 전투 상황 prior와 결합한다.
4. 그러나 사용자마다 터치 중심과 분산이 다르므로 calibration이 필요하다.
5. 본 데모는 Raw, NoCal Adaptive, Calibrated Adaptive 세 조건을 비교한다.
6. 평가는 action prediction accuracy가 아니라 오터치, 보정 납득성, 조작 안정성, 전투 결과, 사용자 만족도를 본다.
7. 로그는 각 입력을 시계열 JSONL로 저장하고, 최종 결과 화면에서 전체 JSON을 복사할 수 있게 했다.

## 13. 현재 구현과 직접 연결되는 파일

- `AdaptivePrototypeBootstrap.cs`: 시작 화면, 3조건 버튼, HUD, 결과 화면, copy button 생성
- `FourButtonCalibrationFlow.cs`: 4버튼 calibration flow, participant/condition 시작, calibration event logging
- `RoguelikeGameManager.cs`: stage 진행, 30초 stage timer, result screen, final log bundle copy
- `AdaptiveTouchManager.cs`: Bayesian decoding, direct input, correction, safety, trial logging
- `CombatManager.cs`: CombatContext 생성, action prior 계산
- `InteractionDemandModel.cs`: 6개 demand 계산
- `ExperimentSessionManager.cs`: final session log bundle 생성
- `DatasetSchema.cs`: JSON/JSONL record schema
- `UserEvaluationLogger.cs`: evaluation touch/stage summary logging
- `ModePolicyLogger.cs`: mode/policy time-series logging
- `AdaptiveGameHudController.cs`: HP, boss HP, cooldown, mode/scenario/policy overlay 표시
- `KTHFullPlaythroughRecorder.cs`: 자동 플레이/검증 녹화 및 final log copy

## 14. 실험 해석 시 주의점

- 현재 실험은 "AI가 플레이어의 최적 행동을 맞혔다"를 증명하는 실험이 아니다.
- 현재 실험은 "입력 불확실성을 상황과 사용자 calibration으로 보정했을 때 조작 경험이 개선되는가"를 확인한다.
- 전투 상황 prior는 보조적인 context prior이며, 최종 action choice ground truth는 사용자가 실제로 의도한 행동에 더 가깝다.
- Adaptive 보정이 항상 정답이어야 하는 것은 아니다. 중요한 것은 애매한 터치에서 보정이 납득 가능하고, 명확한 direct input을 보존하는 것이다.
- corrected가 많다고 무조건 좋은 것은 아니다. corrected가 많으면서도 사용자가 납득하지 못하면 과보정이다.
- Raw 대비 damage가 줄더라도, 보정이 불공정하거나 예측 불가능하게 느껴지면 사용자 경험 측면에서는 실패일 수 있다.


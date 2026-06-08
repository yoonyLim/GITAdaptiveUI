# Non-action 상황 보강 라벨링 라운드 3

## 1. 목적

이전 라운드에서 실제 게임 화면 데이터는 늘었지만, teacher label의 dominant mode가 `action_first`에 과도하게 몰렸다. 그래서 이번 라운드는 단순 랜덤 라벨링이 아니라, `cognition_first`와 `guidance_procedure` 후보를 의도적으로 골라 라벨링했다.

## 2. 타깃 샘플 선별 방식

새 스크립트:

- `analysis_multigame_scene/src/teacher/select_targeted_non_action_samples.py`

선별 기준:

- 기존 teacher label이 없는 sample만 선택
- 실제 image/video frame만 선택
- caption에 `menu`, `map`, `inventory`, `dialogue`, `quest`, `objective`, `tutorial`, `score`, `result`, `chat`, `selection` 같은 단어가 있으면 가산
- Among Us, Minecraft, Roblox, Genshin, Terraria, Forza처럼 상대적으로 non-action 후보가 많은 게임에 가산
- weak temporal urgency가 낮거나 action_window가 `wait/explore`면 가산
- ViZDoom/DOTA2/전투 영상은 감점

선별 결과:

| source | selected |
|---|---:|
| Gameplay Images | 30 |
| GameplayCaptions | 12 |
| Atari-HEAD | 12 |
| DOTA2 | 6 |

## 3. Codex teacher 라벨링 결과

실행 대상은 60개였고, 이 중 54개가 실제 신규 Codex gpt-5.5 호출이었다.

| 항목 | 값 |
|---|---:|
| 신규 요청 샘플 | 60 |
| 실제 신규 teacher 호출 | 54 |
| cache hit | 6 |
| 전체 누적 teacher label | 392 |
| 전체 실제 Codex label | 392 |
| fallback/dryrun | 0 |
| valid JSON rate | 1.0 |
| p50 latency | 18,124 ms |
| p95 latency | 22,273 ms |
| p99 latency | 24,080 ms |

## 4. 라벨 정규화 보정

라벨링 자체는 성공했지만, 중요한 문제가 있었다. Teacher가 Among Us 회의/투표/채팅 화면을 “high interaction demand”라고 표현하면 기존 parser가 이를 거의 자동으로 `action_first`로 정규화했다. 그러나 회의/투표/채팅 화면은 즉각 조작보다 정보 읽기와 사회적 판단이 더 중심이므로 `cognition_first`에 가깝다.

그래서 다음 텍스트 cue를 반영하는 보정 규칙을 추가했다.

- cognition cue: meeting, voting, chat, lobby, settings, menu, inventory, dialogue, text, reading, decision, social, results
- guidance cue: map, quest, objective, route, navigation, mission, checkpoint, tutorial
- low-action cue: low interaction, no immediate, waiting, setup phase, safe window, not urgent

새 스크립트:

- `analysis_multigame_scene/src/teacher/refine_teacher_labels.py`

보정 전후 mode 분포:

| mode | before | after | delta |
|---|---:|---:|---:|
| action_first | 353 | 171 | -182 |
| cognition_first | 18 | 100 | +82 |
| guidance_procedure | 21 | 121 | +100 |

주의: 이 보정은 teacher raw rationale을 바탕으로 한 schema normalization 보정이다. 새로운 인간 정답 라벨이 추가된 것은 아니다.

## 5. 재학습 결과

균형 보정된 392개 라벨로 MobileNetV3-Small 계열 student를 재학습했다.

| metric | value |
|---|---:|
| dominant_mode_accuracy | 0.453 |
| action_first_recall | 0.587 |
| cognition_first_recall | 0.395 |
| guidance_procedure_recall | 0.311 |
| threat_level_macro_f1 proxy | 0.599 |
| action_window_macro_f1 proxy | 0.357 |
| interaction_demand_MAE | 0.128 |
| p50 latency | 4.76 ms |
| p95 latency | 15.72 ms |
| p99 latency | 17.40 ms |

## 6. 해석

좋아진 점:

- mode 분포가 훨씬 균형 잡혔다.
- 이제 `action_first`만 배우는 데이터셋은 아니다.
- `cognition_first`와 `guidance_procedure`도 학습/평가 대상에 의미 있게 들어왔다.
- student latency는 p95 15.72ms로 비동기 상황 업데이트용으로 충분히 빠르다.

나빠 보이는 점:

- dominant_mode_accuracy는 이전보다 낮아졌다.
- 하지만 이것은 나쁜 신호만은 아니다. 이전 accuracy는 action_first 편향 덕분에 높게 나온 측면이 컸다.
- 균형이 맞춰지면서 문제가 더 어려워졌고, 지금의 낮은 accuracy가 더 현실적인 기준선이다.

## 7. 다음 단계

1. cognition/guidance 후보를 200개 이상 더 라벨링한다.
2. tutorial/result/learning_review 화면을 별도 수집한다.
3. teacher prompt를 “high interaction demand”와 “action_first”를 더 엄격히 구분하도록 수정한다.
4. 현재 보정 규칙을 사람이 30-50개 샘플로 검수한다.
5. 그 다음 ResNet18, MnasNet1.0, MobileNetV3-Small 후보 모델 benchmark를 다시 돌린다.

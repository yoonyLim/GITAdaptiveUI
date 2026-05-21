# 다중 게임 화면 데이터셋 확장 및 모델 재학습 결과

## 1. 데이터셋 구축 과정

이번 라운드에서는 symbolic MOBA render를 메인 학습 데이터에서 제외하고, 실제 화면 기반 데이터만 확장했다. 최종 통합 scene table은 `analysis_multigame_scene/data/processed/multigame_scene_samples.parquet`에 생성된다.

| 데이터 소스 | 화면 수 | 게임 수 | 역할 |
|---|---:|---:|---|
| ViZDoom generated frames | 25,000 | 1 | FPS/action-threat 제어 환경 |
| DOTA2 event extraction gameplay videos | 9,000 | 1 | 실제 MOBA gameplay frame |
| Microsoft bleeding-edge gameplay sample | 100 | 1 | 3인칭 action gameplay 보강 |
| GameplayCaptions | 500 | 1 | caption이 붙은 실제 gameplay screenshot 보강 |
| Bingsu Gameplay Images | 500 | 10 | Among Us, Apex, Fortnite, Forza, Free Fire, Genshin, God of War, Minecraft, Roblox, Terraria |

추가된 핵심 데이터는 `Bingsu/Gameplay_Images`다. 이 데이터는 10개 게임의 실제 유튜브 gameplay frame으로 구성되어 있어, 기존 ViZDoom+DOTA2 중심 구조보다 장르 다양성이 커졌다. 각 게임에서 최대 50장씩 균형 추출했다.

## 2. 왜 이렇게 데이터셋을 짰는가

목표는 Unity 전용 장면 분류기를 만드는 것이 아니라, 여러 게임 화면에서 공통적으로 나타나는 상황 패턴을 잡는 것이다. 그래서 한 게임의 색상, HUD, 캐릭터, 카메라 구도를 외우는 구조를 피하려고 다음 범주를 섞었다.

- FPS/action threat: ViZDoom, Apex/Free Fire/Fortnite 계열 화면
- MOBA: DOTA2 gameplay video frame
- third-person action: Bleeding Edge, God of War
- open-world/exploration: Genshin, Minecraft, Terraria
- racing/continuous control: Forza Horizon
- social/cognition-heavy scene: Among Us

이 구성은 아직 완전한 “일반 게임 상황 인식” 데이터셋은 아니지만, 두 종류 게임만 쓰던 상태보다는 장면 다양성이 크게 좋아졌다.

## 3. 전체 클라이언트/모델 아키텍처

```text
게임 화면 프레임
-> Codex CLI teacher labeling
-> interaction-demand labels
-> lightweight student model
-> 상황 prior / adaptation state
-> Unity Attack/Dodge Bayesian decoder
-> safety gate / clear input preservation
-> 최종 Attack, Dodge, Invalid
```

현재 teacher/student는 최종 사용자 입력을 직접 대체하기 위한 것이 아니라, Unity 쪽 decoder가 참고할 상황 prior를 공급하는 역할이다. Unity의 최종 입력 결정은 여전히 터치 위치 likelihood, 상황 prior, safety gate가 함께 결정한다.

## 4. 모델 구조 설계 이유

라벨 수가 300개로 아직 작기 때문에, 처음부터 큰 모델을 full fine-tuning하면 과적합 위험이 크다. 그래서 이번 학습은 ImageNet pretrained backbone을 고정하고, 위에 작은 multi-task head를 붙이는 구조로 진행했다.

예측 head는 다음을 동시에 학습한다.

- dominant_mode
- threat_level
- action_window
- urgency_level
- interaction-demand scores

multi-task 구조를 쓴 이유는 화면 상황이 단일 class로 끝나지 않기 때문이다. 예를 들어 같은 전투 장면이라도 action intensity, urgency, information priority가 다르게 나타날 수 있다.

## 5. Teacher labeling 결과

Codex CLI OAuth teacher labeling은 실제로 수행됐다.

| 항목 | 값 |
|---|---:|
| 전체 teacher label | 300 |
| 실제 Codex teacher label | 300 |
| fallback/dryrun label | 0 |
| valid JSON rate | 1.0 |
| 이번 추가 실행의 실제 호출 수 | 90 |
| 실제 teacher p50 latency | 18,731.77 ms |
| 실제 teacher p95 latency | 29,179.82 ms |
| 실제 teacher p99 latency | 35,437.04 ms |

해석: Codex teacher는 offline labeling용으로는 쓸 수 있지만, 런타임 상황 인식 모델로 쓰기에는 너무 느리다. 따라서 teacher는 데이터 라벨링용이고, 실제 시스템에는 student 모델이 필요하다.

## 6. Student 기본 모델 결과

현재 기본 student는 MobileNetV3-Small 계열 multi-task model이다.

| 지표 | 값 |
|---|---:|
| dominant_mode_accuracy | 0.884 |
| mode_macro_f1 | 0.884 |
| threat_level_macro_f1 | 0.584 |
| action_window_macro_f1 | 0.708 |
| interaction_demand_MAE | 0.0949 |
| student latency p50 | 8.26 ms |
| student latency p95 | 21.23 ms |
| supported update rate | 30Hz |

해석: dominant mode와 action_window는 어느 정도 학습됐지만, threat_level은 아직 중간 수준이다. 데이터가 action_first에 많이 치우쳐 있어 dominant mode accuracy만 보고 “잘 됐다”고 보기는 어렵다.

## 7. 후보 모델 비교

동일한 teacher label로 여러 CNN 후보를 다시 학습했다.

| 순위 | 모델 | mode acc | threat acc | action_window acc | demand MAE | p95 ms | 해석 |
|---:|---|---:|---:|---:|---:|---:|---|
| 1 | ResNet18 | 0.915 | 0.660 | 0.511 | 0.091 | 14.69 | 현재 종합 score 1위 |
| 2 | MnasNet1.0 | 0.872 | 0.574 | 0.638 | 0.135 | 12.52 | 정확도/지연 균형 좋음 |
| 3 | ShuffleNetV2 x1.0 | 0.936 | 0.660 | 0.340 | 0.111 | 10.33 | 빠르고 threat는 괜찮지만 action_window 약함 |
| 4 | MobileNetV2 | 0.745 | 0.638 | 0.489 | 0.091 | 11.57 | 균형형 baseline |
| 5 | MobileNetV3-Small | 0.936 | 0.532 | 0.383 | 0.103 | 7.73 | 가장 실용적인 경량 baseline |
| 13 | ConvNeXt-Tiny | 0.830 | 0.426 | 0.277 | 0.116 | 39.81 | 현재 데이터/지연 기준 부적합 |

이번 확장 후 추천은 두 갈래다.

- 정확도 우선: ResNet18
- 지연/정확도 균형: MnasNet1.0
- 경량 baseline 유지: MobileNetV3-Small

인간의 시각-인지-운동 반응 시간이 보통 수백 ms 단위라는 점을 고려하면, p95 10-20ms 모델은 비동기 상황 업데이트용으로 충분히 빠르다. 따라서 현재 단계에서는 7ms냐 14ms냐보다 threat/action_window 정확도가 더 중요하다. 이 기준에서는 ResNet18 또는 MnasNet1.0이 MobileNetV3-Small보다 더 설득력 있다.

## 8. Unity Bayesian proxy 평가

Teacher/student prior를 Unity Attack/Dodge decoder에 연결한 offline replay/proxy 평가도 갱신했다.

| prior source | accuracy | ambiguous accuracy | correction success | overcorrection |
|---|---:|---:|---:|---:|
| no_prior | 0.922 | 0.886 | 0.667 | 0.000 |
| internal_state_prior | 0.980 | 0.971 | 0.486 | 0.000 |
| oracle_prior | 1.000 | 1.000 | 1.000 | 0.000 |
| codex_teacher_prior_offline | 0.980 | 0.971 | 0.778 | 0.000 |
| lightweight_student_prior | 0.980 | 0.971 | 0.500 | 0.000 |
| student_prior_with_safety_gate | 0.980 | 0.971 | 0.500 | 0.000 |

이 결과는 실제 screenshot-aligned Unity participant log가 아니라 proxy/offline replay다. 따라서 “실제 사용자에서 검증됐다”고 주장하면 안 된다. 다만 연결 구조와 metric 계산은 작동한다.

## 9. 잘 된 점과 아직 부족한 점

잘 된 점:

- 실제 gameplay 화면 데이터가 2개 중심에서 5개 소스, 10개 추가 게임으로 확장됐다.
- Codex teacher 실제 라벨 300개를 확보했다.
- fallback/dryrun 없이 유효 JSON 100%로 라벨 파일을 만들었다.
- 여러 후보 모델을 같은 조건에서 학습해 지연/정확도 trade-off를 비교했다.
- Unity Bayesian decoder와 prior source 비교까지 연결됐다.

부족한 점:

- 300개 라벨은 모델 학습에는 아직 작다.
- dominant mode가 action_first에 치우쳐 있어 mode accuracy는 과대평가될 수 있다.
- threat/action_window 성능은 아직 실사용 수준이라고 보기 어렵다.
- GameplayCaptions는 현재 추출된 shard의 게임 다양성이 낮다.
- Unity 평가는 실제 screenshot-aligned participant log가 아니라 proxy다.

## 10. 다음 단계

1. teacher label을 최소 800-1,000개까지 늘린다.
2. action_first뿐 아니라 cognition_first, guidance_procedure, learning_review 장면을 의도적으로 더 모은다.
3. Bingsu Gameplay Images의 10개 게임에서 각 100-200장으로 늘린다.
4. GameplayCaptions shard를 더 넓혀 게임 다양성을 확보한다.
5. ResNet18, MnasNet1.0, MobileNetV3-Small 3개를 집중 비교한다.
6. 실제 Unity screenshot과 trial log를 맞춰 저장하고, scene prior가 실제 Attack/Dodge correction에 기여하는지 다시 평가한다.
7. 실제 참가자 3-5명 로그로 calibration, correction success, overcorrection을 재측정한다.

# 게임 화면 상황 인식 모델 선택 보고서

## 1. 왜 이런 아키텍처가 필요한가

이 프로젝트는 게임 화면을 보고 현재 장면의 상황을 인식한 뒤, 그 정보를 클라이언트의 적응형 UI 또는 입력 보정 로직에 활용하는 것을 목표로 한다. 따라서 필요한 것은 단순한 이미지 분류기가 아니라, 게임 장면의 상호작용 맥락을 읽는 상황 인식 모델이다.

초기 구조는 다음과 같이 잡았다.

```text
게임 화면 프레임
→ 상황 인식 모델
→ action_first / temporal_urgency / threat_level / action_window 등 상황 변수
→ 상황 prior 또는 UI adaptation state
→ 클라이언트 UI / 입력 보정 / 피드백 로직
```

여기서 중요한 점은 모델이 화면의 색상이나 특정 게임 UI만 외우는 것이 아니라, 여러 게임 장면에서 공통적으로 나타나는 상황적 특성을 잡아야 한다는 것이다. 예를 들어 전투가 급박한 장면, 정보 확인이 중요한 장면, 이동/탐색 중심 장면, 위험이 높은 장면을 구분할 수 있어야 한다.

## 2. 모델이 가져야 하는 특성

이번 정리에서는 다음 조건은 핵심 선택 기준에서 제외했다.

- 모델이 직접 행동을 예측하면 안 된다는 제약
- confidence가 낮으면 neutral prior로 돌아가야 한다는 제약
- Unity/Sentis/ONNX 배포 가능성을 최우선으로 봐야 한다는 제약

대신 현재 단계에서는 다음 특성을 우선 고려한다.

1. 게임 화면 상황 인식 정확도
2. 작은 teacher-labeled 데이터에서도 transfer learning이 가능한가
3. 다양한 장르 화면으로 확장 가능한가
4. 인간의 인지/반응 지연과 비교했을 때 모델 지연이 충분히 작은가
5. 추후 더 큰 데이터셋으로 학습했을 때 성능 향상 여지가 있는가

즉 지금은 모바일 배포 최적화보다, “어떤 모델이 게임 화면 상황을 가장 잘 읽는가”를 먼저 보는 단계다.

## 3. 인간 인지 지연을 왜 고려해야 하는가

게임 UI에서 모델 지연을 판단할 때는 단순히 “몇 ms가 빠른가”만 보면 안 된다. 사용자가 화면을 보고 이해하고 반응하는 데에도 시간이 걸리기 때문이다.

인간 정보처리 모델인 Model Human Processor는 지각, 인지, 운동 처리 단계를 나누며, 대표적으로 지각 처리 주기 약 100ms, 인지 처리 약 70ms, 운동 처리 약 70ms 수준을 사용한다. 또한 일반적인 시각 반응 시간은 대략 200ms 안팎으로 다뤄진다. 선택지가 늘어나는 선택 반응 상황에서는 Hick-Hyman law에 따라 반응 시간이 더 증가한다.

따라서 상황 인식 모델의 p95 지연이 10-20ms 수준이라면, 이는 인간의 시각-인지-운동 반응 시간에 비해 매우 작은 편이다. 즉 모델이 매 터치 입력을 직접 막는 구조가 아니라, 화면 상황을 비동기로 갱신하는 구조라면 10ms와 16ms의 차이는 사용자 체감보다 정확도 차이가 더 중요할 수 있다.

이 관점에서 보면:

- 5-10ms 모델은 매우 빠른 실시간 후보
- 10-20ms 모델은 인간 인지 지연 대비 충분히 실용적인 후보
- 30ms 이상 모델은 실시간 UI loop에는 부담이 될 수 있지만, 저주기 상황 갱신에는 가능
- 100ms 이상 모델은 런타임 상황 업데이트에는 부담이 크며 offline teacher 또는 분석용에 가깝다

## 4. 데이터셋을 간단히 만든 이유

본격적인 대규모 학습 전에, 먼저 구조가 실제로 작동하는지 확인하기 위해 작은 teacher-student 데이터셋을 구성했다.

사용한 화면 데이터는 다음과 같다.

| 데이터 소스 | 규모 | 역할 |
|---|---:|---|
| ViZDoom generated frames | 25,000 frames | FPS/action-threat 장면 |
| DOTA2 gameplay event extraction frames | 9,000 frames | 실제 MOBA gameplay 화면 |
| Microsoft bleeding-edge gameplay sample | 100 frames | 추가 실제 gameplay 화면 |
| GameplayCaptions sample | 100 frames | 다양한 게임 이미지 샘플 |

이 중 대표 샘플 200개를 Codex CLI teacher로 라벨링했다. 라벨은 게임의 정답 행동이 아니라 화면 상황을 설명하는 변수다.

주요 라벨:

- `action_intensity`
- `temporal_urgency`
- `information_priority`
- `occlusion_risk`
- `control_continuity`
- `dominant_mode`
- `threat_level`
- `action_window`
- `urgency_level`

이 데이터셋은 아직 작고 불균형하다. 특히 `action_first` 라벨이 많기 때문에 dominant mode 정확도는 과대평가될 수 있다. 따라서 모델 선택에서는 `threat_level`과 `action_window` 성능을 더 중요하게 봤다.

## 5. 비교한 모델 후보

최신 모델과 기본 baseline을 함께 조사했다.

| 모델 | 출처 | 성격 | 이번 실험 |
|---|---|---|---|
| MobileNetV4 | https://arxiv.org/abs/2404.10518 | 최신 모바일 모델 | 후보 조사 |
| MobileOne | https://arxiv.org/abs/2206.04040 | 1ms급 모바일 backbone 지향 | 후보 조사 |
| EdgeNeXt | https://arxiv.org/abs/2206.10589 | CNN-Transformer hybrid edge 모델 | 후보 조사 |
| FastViT | https://arxiv.org/abs/2303.14189 | 빠른 hybrid ViT | 후보 조사 |
| RepViT | https://arxiv.org/abs/2307.09283 | 모바일 CNN/ViT 절충 | 후보 조사 |
| EfficientFormer | https://arxiv.org/abs/2206.01191 | MobileNet 속도급 transformer | 후보 조사 |
| EfficientNet-B0 | https://research.google/pubs/efficientnet-rethinking-model-scaling-for-convolutional-neural-networks/ | 정확도/효율 baseline | 학습 |
| MnasNet1.0 | https://research.google/pubs/mnasnet-platform-aware-neural-architecture-search-for-mobile/ | latency-aware 모바일 모델 | 학습 |
| MobileNetV3-Small | https://arxiv.org/abs/1905.02244 | 모바일 경량 baseline | 학습 |
| ShuffleNetV2 | https://arxiv.org/abs/1807.11164 | 실제 지연 고려 경량 CNN | 학습 |
| SqueezeNet | https://arxiv.org/abs/1602.07360 | 작은 모델 크기 baseline | 학습 |
| ResNet18 | https://arxiv.org/abs/1512.03385 | 표준 CNN baseline | 학습 |
| ConvNeXt-Tiny | https://arxiv.org/abs/2201.03545 | 현대 ConvNet baseline | 학습 |

최신 모델 중 MobileNetV4, MobileOne, RepViT, FastViT, EfficientFormer는 논문상 좋은 후보지만, 현재 저장소에서 바로 안정적으로 pretrained weight, 학습 코드, Unity 이전 경로를 확보한 상태는 아니다. 그래서 이번 실험에서는 재현성이 높은 torchvision pretrained 모델을 먼저 돌렸다.

## 6. 학습 및 평가 방식

모든 학습 후보는 동일한 200개 teacher label을 사용했다.

공통 조건:

- ImageNet pretrained backbone 사용
- 작은 데이터셋이므로 backbone은 기본적으로 고정
- multi-head 상황 예측 head만 학습
- scenario holdout split 사용
- 평가 지표:
  - dominant mode accuracy
  - threat level accuracy
  - action window accuracy
  - interaction demand MAE
  - p95 inference latency

## 7. 실험 결과

| 순위 | 모델 | mode acc | threat acc | action_window acc | demand MAE | p95 latency |
|---:|---|---:|---:|---:|---:|---:|
| 1 | EfficientNet-B0 | 0.943 | 0.714 | 0.629 | 0.112 | 16.02ms |
| 2 | MnasNet1.0 | 0.943 | 0.686 | 0.629 | 0.116 | 10.42ms |
| 3 | RegNetY-400MF | 0.943 | 0.686 | 0.371 | 0.104 | 12.22ms |
| 4 | AlexNet | 0.943 | 0.686 | 0.371 | 0.113 | 8.47ms |
| 5 | MobileNetV3-Large | 0.943 | 0.486 | 0.514 | 0.117 | 9.75ms |
| 6 | MnasNet0.5 | 0.943 | 0.514 | 0.429 | 0.133 | 8.56ms |
| 7 | MobileNetV3-Small | 0.943 | 0.457 | 0.400 | 0.088 | 7.58ms |
| 8 | ShuffleNetV2 x0.5 | 0.914 | 0.600 | 0.286 | 0.095 | 7.81ms |
| 9 | ResNet18 | 0.943 | 0.457 | 0.371 | 0.109 | 14.24ms |
| 10 | ConvNeXt-Tiny | 0.914 | 0.086 | 0.171 | 0.123 | 22.10ms |

## 8. 결과 해석

현재 데이터셋 기준으로 가장 좋은 정확도는 EfficientNet-B0가 보였다. 특히 중요한 지표인 `threat_level`과 `action_window`에서 가장 좋은 편이다.

MnasNet1.0은 EfficientNet-B0보다 정확도가 약간 낮지만, p95 지연이 약 10.42ms로 더 안정적이다. 인간의 인지 지연을 고려하면 EfficientNet-B0의 16.02ms도 충분히 실용적인 범위지만, 실시간 화면 업데이트나 낮은 사양 기기를 고려하면 MnasNet1.0이 더 안전하다.

MobileNetV3-Small은 가장 보수적인 경량 후보로 남길 수 있다. 하지만 현재 teacher label 기준으로는 threat/action_window 성능이 낮기 때문에, 상황 인식 정확도를 우선하는 현재 단계의 주 모델로 삼기에는 부족하다.

ConvNeXt-Tiny는 최신 ConvNet baseline이지만, 현재 작은 데이터와 160px 입력 조건에서는 지연도 크고 성능도 좋지 않았다. 이 결과만으로 ConvNeXt 자체가 나쁘다고 말할 수는 없지만, 현재 프로젝트 조건에는 맞지 않는다.

## 9. 선택 결론

현재 단계의 1차 선택은 EfficientNet-B0가 타당하다.

이유:

1. threat_level 성능이 가장 높다.
2. action_window 성능도 공동 최고 수준이다.
3. p95 16.02ms는 인간 시각/인지/운동 반응 시간에 비해 충분히 작다.
4. 현재 목표가 Unity 모바일 배포가 아니라 게임 화면 상황 인식 성능 확인이므로 정확도를 우선할 수 있다.

다만 실시간 적용 후보까지 함께 고려하면 다음과 같이 분리하는 것이 가장 설득력 있다.

| 목적 | 추천 모델 |
|---|---|
| 정확도 중심 1차 상황 인식 모델 | EfficientNet-B0 |
| 지연-정확도 균형 모델 | MnasNet1.0 |
| 가장 가벼운 모바일 후보 | MobileNetV3-Small |
| 최신 모델 추가 검증 후보 | MobileNetV4, MobileOne, RepViT, FastViT |

따라서 보고서에서는 다음과 같이 주장하는 것이 적절하다.

> 인간의 인지 지연을 고려하면 10-20ms 수준의 모델 지연은 상황 인식 업데이트에서 치명적이지 않다. 따라서 현재 단계에서는 가장 높은 threat/action_window 성능을 보인 EfficientNet-B0를 1차 상황 인식 모델로 선택한다. 단, 실시간 배포나 저사양 기기 적용을 고려하는 후속 단계에서는 MnasNet1.0 또는 MobileNetV3-Small을 경량 대안으로 비교한다.

## 10. 현재 한계

현재 결과는 초기 실험이다.

- teacher label이 200개로 작다.
- `action_first` 라벨이 많아 dominant mode accuracy가 과대평가될 수 있다.
- `cognition_first`, `guidance_procedure`, `learning_review` 장면이 부족하다.
- 실제 Unity 사용자 데이터와 아직 충분히 연결되지 않았다.
- 최신 모델인 MobileNetV4, MobileOne, RepViT, FastViT는 아직 직접 학습하지 않았다.

따라서 이 결과는 최종 모델 확정이 아니라, “현재 데이터에서 어떤 방향이 유망한지 확인한 모델 선택 실험”으로 해석해야 한다.

## 11. 앞으로 해야 할 일

1. teacher label을 최소 1,000개 이상으로 확장한다.
2. action_first에 치우친 라벨 분포를 보정한다.
3. cognition_first, guidance_procedure, learning_review 장면을 의도적으로 추가한다.
4. EfficientNet-B0와 MnasNet1.0을 둘 다 유지하고, 더 큰 데이터에서 재평가한다.
5. 최신 후보인 MobileNetV4, MobileOne, RepViT, FastViT를 timm 또는 공식 구현으로 추가 실험한다.
6. 모델 예측 결과가 실제 UI adaptation 또는 입력 보정 성능을 개선하는지 Unity 로그와 연결해 평가한다.
7. 인간 인지 지연 기준으로 모델 지연을 해석하되, 실제 사용자 실험에서 체감 지연과 성능 개선을 함께 확인한다.


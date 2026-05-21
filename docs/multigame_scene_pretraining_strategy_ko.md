# Multi-game Situation Recognition 전략

## 왜 Unity-only classifier를 메인으로 쓰지 않는가

Unity 화면만으로 학습한 classifier는 Unity의 색상, 폰트, 적 모델, 카메라, 버튼 위치를 외울 수 있다. 따라서 이 프로젝트에서는 Unity-only scene classifier를 일반 상황 인식기로 주장하지 않는다.

## 목표

화면 모델은 Attack/Dodge를 직접 예측하지 않는다. 대신 다음 추상 상황 변수를 예측한다.

- `ui_phase`: gameplay / menu / loading / result / unknown
- `threat_level`: none / warning / active / critical / unknown
- `action_window`: engage / avoid / wait / explore / unknown
- `urgency_level`: low / medium / high / unknown
- `interaction_demand`: action intensity, temporal urgency, information priority, occlusion risk, control continuity

이 출력은 `VisionPriorBuilder`에서 Attack/Dodge prior로만 변환된다.

## 데이터 역할

- ViZDoom: game variables와 visual frames를 함께 만들 수 있어 abstract situation pretraining의 1차 소스다.
- Atari-HEAD: 인간 플레이, gaze, reward 기반의 선택적 public context source다. 대용량이므로 manual/subset mode를 사용한다.
- MineRL: 3D first-person gameplay 다양성 보조 소스다. 전체 데이터셋은 받지 않는다.
- DQN Replay Atari: 다양한 Atari state/action/reward transition 보조 소스다. 전체 replay는 받지 않는다.
- Unity: final testbed, few-shot adaptation, Bayesian decoder evaluation에만 사용한다.

## Prior mapping

- safe-like engage: `P(Attack)=0.85`, `P(Dodge)=0.15`
- warning but engage: `P(Attack)=0.65`, `P(Dodge)=0.35`
- active/avoid: `P(Attack)=0.15`, `P(Dodge)=0.85`
- critical: `P(Attack)=0.05`, `P(Dodge)=0.95`
- unknown/low confidence: neutral prior

Confidence mixing:

`P_final(a)=c*P_rule(a|abstract_state)+(1-c)*P_neutral(a)`

Low confidence에서는 neutral prior를 사용하고 clear input은 기존 safety gate가 보존한다.

## 한계

- weak label은 인간 의도 라벨이 아니다.
- public game screen은 Unity Attack/Dodge label을 직접 검증하지 않는다.
- DINO 계열 feature extractor는 active pipeline에서 제거한다. held-out scenario에서 상황 의미 일반화 근거가 약했기 때문이다.
- ViZDoom 런타임이 없는 환경의 procedural fixture는 파이프라인 검증용이지 public evidence가 아니다.

## 현재 구현된 실제 실행 경로

- `vizdoom` Python package가 설치 가능하면 `basic.cfg`, `defend_the_center.cfg`, `take_cover.cfg`, `health_gathering.cfg`, `deadly_corridor.cfg`를 실제 게임 루프로 실행해 frame/state/reward row를 생성한다.
- scene head는 ViZDoom environment/state/reward proxy feature를 사용한다.
- visual backbone을 다시 고려하려면 먼저 temporal/state-aware label 품질을 개선해야 한다.

# Vision Grounding Dataset Strategy

비전 layer의 목적은 화면 이미지에서 `B_t`와 `s_t`를 추정하는 것이다.

- `B_t`: Attack/Dodge 또는 button-like UI 위치, 크기, 의미.
- `s_t`: 장면/위험 상태. Unity에서는 `enemy_state`로 직접 알 수 있지만 일반 화면에서는 vision classifier가 필요하다.

## 확보/탐색한 공개 데이터셋

### Screen Annotation

- source: https://github.com/google-research-datasets/screen_annotation
- 현재 로컬 확보됨.
- 역할: UI element type, bbox, OCR/text/description grounding.
- 한계: CSV는 Rico `screen_id`를 제공하지만 screenshot image bytes는 포함하지 않는다. 실제 pixel detector 학습에는 Rico screenshot이 추가로 필요하다.

### Rico

- source: https://www.interactionmining.org/archive/rico
- 역할: mobile screenshots + view hierarchy + UI element bounds.
- 한계: 주요 archive가 크다. 현재 환경에서는 직접 네트워크 다운로드가 거부되어 manual placement를 지원한다.
- 배치 위치: `analysis_vision/data/rico/manual/`

### Widget Caption

- source: https://github.com/google-research-datasets/widget-caption
- 역할: widget 기능/의미 caption. 버튼 의미 분류와 "search/back/add/settings" 같은 action semantics에 유용하다.
- 한계: bbox/image는 Rico view hierarchy와 screenshot을 함께 사용해야 한다.

### ScreenQA

- source: https://github.com/google-research-datasets/screen_qa
- 역할: screen question answering, answer UI element bbox.
- 한계: Rico image id 기반이며 game/combat 상태 라벨은 없다.

### UICrit

- source: https://github.com/google-research-datasets/uicrit
- 역할: UI critique region bbox와 design quality labels.
- 한계: design critique 중심이며 button/action label이 아니다.

### VINS

- source: https://github.com/sbunian/VINS
- 역할: UI component detection. README 기준 11개 UI component class와 4,800 UI images.
- 한계: 공식 다운로드가 Google Drive라 현재 환경에서는 manual placement가 필요하다.

## 이 프로젝트에서의 사용

1. Screen Annotation으로 UI element detector / grounding parser의 공통 schema를 만든다.
2. Rico screenshot/view hierarchy가 확보되면 실제 image+bbox detector를 학습한다.
3. Widget Caption을 붙이면 button semantics classifier를 학습한다.
4. Unity에서 `VisionFrameLogger`로 screenshot + Attack/Dodge bbox + enemy_state를 저장해 game-specific fine-tuning data를 만든다.
5. Vision confidence가 낮으면 Bayesian correction strength를 낮추고 clear input preservation을 강화한다.

## 중요한 해석 제한

공개 UI dataset은 UI grounding을 지원하지만 Unity Attack/Dodge game-state prior를 직접 검증하지 않는다. Attack/Dodge 직접 검증은 Unity telemetry와 Unity vision labels가 필요하다.

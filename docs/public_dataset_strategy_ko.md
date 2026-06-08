# 공개 데이터셋 전략

이 프로젝트는 공개 데이터를 배경 설명으로만 두지 않고, 외부 근거와 sanity check로 사용한다. 다만 공개 데이터가 Unity Attack/Dodge Bayesian correction을 직접 검증한다는 주장은 하지 않는다.

## 데이터셋별 역할

- Touch-Dynamics-Research: 모바일 게임 touch dynamics, 사용자별 touch density, pressure, velocity, continuity proxy.
- MC-Snake-Results: Minecraft/Snake 계열 보조 모바일 게임 touch dynamics와 좌/우 영역 분석.
- Google TSI: target-labeled keyboard tap benchmark. Gaussian target-selection, expanded hitbox, overcorrection/correction metric 정의 검증.
- Henze Hit It / 100M taps: 수동 배치 시 game-like circular target hit/miss, visual boundary와 expanded boundary 비교.
- Rico: 모바일 UI layout, button-like component distribution, UI grounding support.
- Screen Annotation: UI element type/location/text/description annotation 기반 screen understanding support.

## 해석 제한

- Public game touch logs validate touch dynamics, not Attack/Dodge correction.
- Public target-selection datasets validate touch-target modeling and hitbox behavior, not game-state prior.
- Public UI datasets support UI grounding, not game-specific combat context.
- Unity controlled prototype validates the actual Attack/Dodge Bayesian pipeline.
- This is not yet commercial mobile game validation.


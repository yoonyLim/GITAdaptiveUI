# Public vs Unity Interpretation

공개 데이터와 Unity telemetry 비교의 목적은 “공개 데이터가 Unity combat correction을 검증했다”는 주장이 아니다.

비교 목적은 다음이다.

- Unity calibration touch offset이 공개 touch-target 분포와 비교해 비현실적으로 크거나 작지 않은지 확인한다.
- 공개 target-selection에서 추천된 variance, hitbox margin이 Unity 초기값으로 쓸 만한지 확인한다.
- public hitbox baseline과 Unity Attack/Dodge baseline의 차이를 분리해서 해석한다.

Unity controlled telemetry만이 Attack/Dodge action label, current button layout, enemy state, intended/required action, HP outcome을 동시에 갖는다. 따라서 직접 검증의 기준은 Unity 로그다.


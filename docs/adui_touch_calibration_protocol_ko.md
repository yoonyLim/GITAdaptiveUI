# ADUI Touch Calibration Protocol

## 목적

이 프로토콜은 플레이어 의도 자체를 학습하기 위한 것이 아니라, 사용자별 터치 입력 분포를 추정하기 위한 calibration 단계다. 이후 런타임에서는 `CombatContext -> action prior`와 `UserTouchModel -> touch likelihood`를 결합하고, `SafetyGate`가 명확한 시각 입력을 보존하면서 애매한 입력만 보정한다.

## 근거 문헌

- Fitts reciprocal tapping task는 포인팅 장치 평가에서 반복 타겟 획득, 이동 거리, 타겟 폭, 반응 좌표 분산을 함께 측정하는 표준적 형태다. 따라서 Attack/Dodge를 번갈아 누르는 serial tapping calibration은 버튼 간 이동 정확도와 속도-정확도 tradeoff를 측정하는 데 적합하다.
  - Sasangohar et al., "Evaluation of Mouse and Touch Input for a Tabletop Display Using Fitts' Reciprocal Tapping Task", HFES 2009.
  - URL: https://www.eng.uwaterloo.ca/~s9scott/wiki/uploads/Main/sasangohar_hfes2009.pdf
- 손가락 터치는 작은 타겟에서 일반 Fitts' law만으로 설명하기 어렵고, endpoint distribution과 절대적 finger precision을 따로 고려해야 한다. 따라서 center tap만이 아니라 boundary/gap 진단 trial도 필요하다.
  - Bi, Li, Zhai, "FFitts Law: Modeling Finger Touch with Fitts' Law", CHI 2013.
  - URL: https://research.google/pubs/ffitts-law-modeling-finger-touch-with-fitts-law/
- 터치 오차는 fat finger만이 아니라 사용자/자세/인식점 차이에 따른 systematic offset을 포함한다. 따라서 사용자별 mean offset과 variance를 저장하는 calibration profile이 필요하다.
  - Holz and Baudisch, "Understanding Touch", CHI 2011.
  - URL: https://static.siplab.org/papers/chi2011-understanding_touch.pdf
  - Holz and Baudisch, "The Generalized Perceived Input Point Model and How to Double Touch Accuracy by Extracting Fingerprints", CHI 2010.
  - URL: https://www.christianholz.net/2010-chi10-holz-baudisch-the_generalized_perceived_input_point_model_and_how_to_double_touch_accuracy_by_extracting_fingerprints.pdf
- 사용자별 touch model은 calibration game이나 입력 로그로 학습할 수 있고, 소량의 사용자별 학습 데이터로도 정확도 향상이 가능하다는 선행 근거가 있다.
  - Weir et al., "A User-specific Machine Learning approach for improving touch accuracy on mobile devices", UIST 2012.
  - URL: https://www.dfki.de/en/web/research/projects-and-publications/publication/6355
- 모바일 엄지 입력에서는 discrete target과 serial tapping이 다른 요구를 가진다. 따라서 단일 버튼 center tap과 버튼 간 연속 이동을 별도 블록으로 분리한다.
  - Parhi, Karlson, Bederson, "Target Size Study for One-Handed Thumb Use on Small Touchscreen Devices", MobileHCI 2006.
  - URL: https://www.microsoft.com/en-us/research/publication/target-size-study-for-one-handed-thumb-use-on-small-touchscreen-devices/

## Calibration 블록

1. `discrete_center`
   - Attack/Dodge 중심부를 각각 반복 탭한다.
   - 목적: 사용자별 기본 mean offset과 action별 variance 추정.
   - 모델 학습 사용: yes.

2. `reciprocal_alternating`
   - Attack -> Dodge -> Attack -> Dodge 순서로 빠르게 왕복한다.
   - 목적: 버튼 간 이동 상황에서 endpoint 분포와 serial tapping 안정성 측정.
   - 모델 학습 사용: yes.

3. `near_boundary`
   - 각 버튼의 안쪽 경계, 즉 두 버튼 사이에 가까운 영역을 의도적으로 탭한다.
   - 목적: visual boundary 보존과 near-boundary 오인식 위험 측정.
   - 모델 학습 사용: no. 의도적으로 치우친 터치이므로 base user variance를 오염시키지 않는다.

4. `ambiguous_gap`
   - Attack/Dodge 사이 gap에서 특정 의도 버튼을 목표로 탭한다.
   - 목적: Gaussian posterior와 SafetyGate가 애매한 입력을 어떻게 처리하는지 측정.
   - 모델 학습 사용: no.

5. `context_pressure`
   - Safe에서는 Attack, Telegraph/Attacking에서는 Dodge를 탭한다.
   - 목적: 상황 압박이 있을 때 터치 분산이 증가하는지, context prior와 touch likelihood 결합이 안정적인지 측정.
   - 모델 학습 사용: yes.

## 저장되는 핵심 로그

- `trial_type`
- `calibration_instruction`
- `calibration_used_for_touch_model`
- `intended_action`
- `distance_to_intended`
- `relative_intended_x`
- `relative_intended_y`
- `variance_attack`
- `variance_dodge`
- `posterior_attack`
- `posterior_dodge`
- `safety_gate_reason`

## 런타임 반영

- Calibration이 끝나면 `user_touch_profile.json`에 `attackMean`, `dodgeMean`, `attackVariance`, `dodgeVariance`를 저장한다.
- 일반 플레이 중에는 `preserve_clear_visual_input`으로 판정된 명확한 터치만 online adaptation sample로 사용한다.
- `correction_allowed`나 ambiguous touch는 자기강화 오류를 만들 수 있으므로 온라인 학습에 쓰지 않는다.

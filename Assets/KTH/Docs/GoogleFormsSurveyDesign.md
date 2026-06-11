# Google Forms Survey Design

이 문서는 현재 `AdaptivePrototype` 사용자 평가를 Google Forms로 운영하기 위한 폼 구성안이다.

현재 권장 구조는 폼 5개다.

1. 참가자 등록/사전 정보 폼: 참가자당 1회
2. Raw Button 플레이 직후 설문 폼: Raw 조건 직후 1회
3. Adaptive Without Calibration 플레이 직후 설문 폼: NoCal 조건 직후 1회
4. Adaptive With Calibration 플레이 직후 설문 폼: Calibrated 조건 직후 1회
5. 최종 비교 설문 폼: 참가자당 1회

조건별 플레이 직후 설문은 세 폼으로 분리한다. 다만 조건 간 직접 비교가 가능하도록 2A/2B/2C에는 동일한 공통 척도 문항 10개를 그대로 넣고, 각 조건 전용 문항만 뒤에 추가한다.

## 생성된 Google Forms 링크

2026-06-11에 Google Apps Script로 생성한 실제 운영용 링크다. 아래 v2 split/polite 링크를 사용한다.

### 1. 참가자 등록/사전 정보

- 편집: https://docs.google.com/forms/d/1PqXTMdVIOpuRXRbs-Of1-EtXvK_0fyyUydoHkyC2v-o/edit
- 응답: https://docs.google.com/forms/d/e/1FAIpQLSfcKhZOGynLxtbQCk-hJW7qOadjuZELwTxRle8X43ZLBKLs0A/viewform

### 2A. Raw Button 플레이 직후 설문

- 편집: https://docs.google.com/forms/d/1MhK0Vbp0LpcIWafN-qmlS96N3oGxEZkACC_CG5TAhG0/edit
- 응답: https://docs.google.com/forms/d/e/1FAIpQLScHcd0hx9pqNRxvSpTwZemmGe58LNUjPm294WsEnm054M37Qw/viewform

### 2B. Adaptive Without Calibration 플레이 직후 설문

- 편집: https://docs.google.com/forms/d/1eFqCKV0SevMmVjuz4Vn6ZYO3PScBLvNFjo-IAbTbjl4/edit
- 응답: https://docs.google.com/forms/d/e/1FAIpQLSfpUSiAy0Eqri0-4HVBpLpYQPg-UbZ8DHnGL3zF0-DnHN0GHw/viewform

### 2C. Adaptive With Calibration 플레이 직후 설문

- 편집: https://docs.google.com/forms/d/1a1hrrimKJ4IV6n0Wjm0FjpIIneTrnZotNPrt0TU7348/edit
- 응답: https://docs.google.com/forms/d/e/1FAIpQLScgPP_Q8xGnDYS7d4fU1JlwUsYFpgvi-_GLLSOArWh6xmWyXA/viewform

### 3. 최종 비교 설문

- 편집: https://docs.google.com/forms/d/14OufT3u81huIxFft8IQhKyOk3AaRsSNldtjKO_g9Wbg/edit
- 응답: https://docs.google.com/forms/d/e/1FAIpQLScvWFHc03Os42mGp7y7L1QQzn0uIlJc4zsObdEd3o1_us5Ehw/viewform

기존 3개 폼 버전과 조건별 통합 v2 폼은 사용하지 않는다. 운영 시에는 위 5개 split/polite 폼만 사용한다.

## 왜 5개 폼인가?

### 1개 폼으로 전부 합치는 방식

장점:

- 링크가 하나라 관리가 쉽다.

단점:

- 세 조건을 반복 입력해야 해서 폼이 길고 복잡해진다.
- 조건별 직후 느낌을 바로 받기 어렵다.
- 참가자가 중간에 헷갈릴 가능성이 높다.
- 로그 session_id와 조건 매칭이 꼬일 수 있다.

### 5개 폼으로 나누는 방식

장점:

- 실험 진행 흐름과 잘 맞는다.
- 조건별 설문이 서로 분리되어 참가자가 현재 조건을 헷갈릴 가능성이 낮다.
- 2A/2B/2C의 공통 문항은 동일하게 유지하므로 조건 간 점수 비교가 가능하다.
- 조건별 전용 문항은 각 폼 뒤에만 붙일 수 있다.
- 최종 비교 질문을 별도로 깔끔하게 받을 수 있다.
- 분석할 때 사전 정보, 조건별 점수, 최종 선호를 분리하기 쉽다.

단점:

- 링크가 5개 필요하다.
- 실험 진행자가 조건 순서에 맞는 폼 링크를 안내해야 한다.

현재 실험은 조건 3개를 반복 측정하는 구조이고, 조건별 전용 질문이 서로 다르므로 5개 폼을 권장한다.

## 로그 수집 방식 주의

`final_session_logs.json`은 풀 플레이 기준 대략 0.5MB-3MB 정도가 될 수 있다. Google Forms 장문 응답에 전체 JSON을 그대로 붙여넣는 방식은 응답 길이 제한이나 브라우저 성능 문제 때문에 불안정할 수 있다.

권장 방식:

- Google Form에는 `participant_id`, `condition`, `session_id`, `completion_state`, `stage`, `log transfer method`만 기록한다.
- 전체 로그는 게임 결과 화면의 `Copy Time-Series JSON` 버튼으로 복사한 뒤 카카오톡, 메모, 메일, 파일 공유 등 별도 채널로 회수한다.
- PC 환경이면 `final_session_logs.json` 또는 `COPY_ALL_FINAL_SESSION_LOGS.txt` 파일을 직접 회수한다.

조건별 폼에는 전체 JSON 붙여넣기 필드를 만들지 않는 것을 권장한다. 필요하다면 "로그 첫 줄 또는 session_id 확인용" 정도만 받는다.

---

# Form 1. Participant Registration

폼 제목:

```text
Adaptive Touch Experiment - Participant Registration
```

폼 설명:

```text
모바일 탑다운 액션 게임에서 작은 버튼 입력 보정 방식이 조작 안정성과 오터치 감소에 도움이 되는지 평가하는 실험입니다.
총 3개 조건을 플레이하며, 각 조건은 최대 3개 스테이지로 구성됩니다.
각 스테이지는 최대 30초입니다.
```

## Section 1. 참가자 정보

### Q1. 참가자 번호

타입: 단답형

필수: 예

예시:

```text
P01
```

### Q2. 실험 조건 순서

타입: 드롭다운

필수: 예

선택지:

```text
Order A: Raw -> NoCal -> Calibrated
Order B: NoCal -> Calibrated -> Raw
Order C: Calibrated -> Raw -> NoCal
Order D: Raw -> Calibrated -> NoCal
Order E: NoCal -> Raw -> Calibrated
Order F: Calibrated -> NoCal -> Raw
```

### Q3. 사용 기기

타입: 객관식

필수: 예

선택지:

```text
iPhone
Android
PC/Editor
기타
```

### Q4. 주로 사용하는 손

타입: 객관식

필수: 예

선택지:

```text
오른손
왼손
양손 비슷함
```

### Q5. 모바일 액션 게임 경험

타입: 객관식

필수: 예

선택지:

```text
거의 없음
가끔 함
자주 함
매우 자주 함
```

### Q6. 모바일 게임에서 조이스틱 이동과 스킬 버튼을 동시에 조작하는 게임을 해본 경험

타입: 객관식

필수: 예

선택지:

```text
없음
조금 있음
자주 있음
매우 익숙함
```

### Q7. 실험 진행 중 조작 로그를 수집하는 것에 동의하십니까?

타입: 객관식

필수: 예

선택지:

```text
동의합니다
동의하지 않습니다
```

---

# Form 2A/2B/2C. Condition-Specific Post-Condition Surveys

조건별 플레이 직후 설문은 세 개로 분리한다.

- Form 2A: Raw Button 플레이 직후 설문
- Form 2B: Adaptive Without Calibration 플레이 직후 설문
- Form 2C: Adaptive With Calibration 플레이 직후 설문

세 폼 모두 공통 척도 문항 10개는 같은 문장으로 유지한다. 그래야 Raw, NoCal, Calibrated 조건의 주관 평가 점수를 직접 비교할 수 있다. 조건별 전용 문항은 공통 문항 뒤에 추가한다.

폼 제목:

```text
Adaptive Touch 사용자 평가 - Raw Button 플레이 직후 설문
Adaptive Touch 사용자 평가 - Adaptive Without Calibration 플레이 직후 설문
Adaptive Touch 사용자 평가 - Adaptive With Calibration 플레이 직후 설문
```

폼 설명:

```text
방금 플레이하신 조건에 대한 설문입니다.
결과 화면의 Copy Time-Series JSON 버튼을 눌러 로그를 별도로 전달하신 뒤, 아래 문항에 응답해 주세요.
```

## Section 1. 조건 정보

### Q1. 참가자 번호

타입: 단답형

필수: 예

예시:

```text
P01
```

### Q2. 이번 조건의 순서

타입: 객관식

필수: 예

선택지:

```text
1번째 조건
2번째 조건
3번째 조건
```

### Q3. 결과 화면에 표시된 session_id

타입: 단답형

필수: 권장

설명:

```text
final_session_logs.json 안의 session_id 또는 결과 화면/복사 로그에 있는 session_id를 입력해 주세요.
```

### Q4. 로그 전달 방식

타입: 체크박스

필수: 예

선택지:

```text
Copy Time-Series JSON을 눌러 카카오톡으로 전달함
Copy Time-Series JSON을 눌러 메모/메일로 전달함
final_session_logs.json 파일로 전달함
COPY_ALL_FINAL_SESSION_LOGS.txt 파일로 전달함
아직 전달하지 않음
```

### Q5. 결과 화면 요약

타입: 단답형 또는 단락형

필수: 아니오

설명:

```text
예: Stage 3 failed, damage 219, button presses 137
```

## Section 2. 공통 질문

척도:

```text
1 = 전혀 그렇지 않습니다
7 = 매우 그렇습니다
```

질문 타입: 선형 배율

모든 질문 필수: 예

### Common Q1

```text
버튼을 눌렀을 때 실행된 행동이 내가 의도한 행동과 잘 맞았다.
```

### Common Q2

```text
버튼 경계 근처를 눌렀을 때 처리 방식이 납득 가능했다.
```

### Common Q3

```text
빠르게 연속 입력하는 상황에서도 조작이 안정적이었다.
```

### Common Q4

```text
조이스틱으로 이동하면서 오른손 버튼을 누르는 흐름이 자연스러웠다.
```

### Common Q5

```text
전투 중 캐릭터, 적, 투사체, HP 상태에 집중하기 쉬웠다.
```

### Common Q6

```text
UI 버튼과 쿨다운 표시는 이해하기 쉬웠다.
```

### Common Q7

```text
의도하지 않은 스킬 발동이나 조작 실수로 인한 답답함이 적었다.
```

### Common Q8

```text
현재 방식은 모바일 액션 게임에서 유용할 것 같았다.
```

### Common Q9

```text
현재 방식은 공정하고 신뢰할 수 있다고 느꼈다.
```

### Common Q10

```text
전체적으로 이 조건의 조작감에 만족했다.
```

## Section 3. Adaptive 조건 전용 질문

이 섹션은 `Adaptive Without Calibration`과 `Adaptive With Calibration` 조건에서만 사용한다.

척도:

```text
1 = 전혀 그렇지 않습니다
7 = 매우 그렇습니다
```

질문 타입: 선형 배율

모든 질문 필수: 예

### Adaptive Q1

```text
입력 보정이 전투 중 조작 실수를 줄이는 데 도움이 되었다.
```

### Adaptive Q2

```text
애매한 터치를 시스템이 다른 행동으로 해석했을 때 그 결과가 납득 가능했다.
```

### Adaptive Q3

```text
자동 보정이 내 조작을 과하게 바꾼다고 느끼지 않았다.
```

### Adaptive Q4

```text
전투 상황에 따라 보정 강도가 달라지는 방식이 자연스럽게 느껴졌다.
```

### Adaptive Q5

```text
보정 결과를 신뢰하고 계속 플레이할 수 있다고 느꼈다.
```

## Section 4. Calibration 조건 전용 질문

이 섹션은 `Adaptive With Calibration` 조건에서만 사용한다.

척도:

```text
1 = 전혀 그렇지 않습니다
7 = 매우 그렇습니다
```

질문 타입: 선형 배율

모든 질문 필수: 예

### Calibration Q1

```text
시작 전 calibration 과정이 이후 플레이에 도움이 된다고 느꼈다.
```

### Calibration Q2

```text
calibration에 걸린 시간은 받아들일 수 있는 수준이었다.
```

### Calibration Q3

```text
calibration 이후 버튼 입력이 내 손 위치 습관에 더 잘 맞는다고 느꼈다.
```

### Calibration Q4

```text
이동 중 버튼 입력, 빠른 전환 입력, 경계 입력을 calibration에 포함한 것이 적절하다고 느꼈다.
```

### Calibration Q5

```text
calibration을 한 번 수행하고 이후 게임에 적용하는 방식이 실제 모바일 게임에서도 가능하다고 느꼈다.
```

## Section 5. 조건별 자유 응답

### Q1. 이번 조건에서 조작이 가장 편했던 순간은 언제였는가?

타입: 단락형

필수: 아니오

### Q2. 이번 조건에서 조작이 가장 불편하거나 어색했던 순간은 언제였는가?

타입: 단락형

필수: 아니오

### Q3. 버튼 크기, 위치, HP 표시, 쿨다운 표시 중 개선이 필요한 부분이 있었는가?

타입: 단락형

필수: 아니오

---

# Form 3. Final Comparison Survey

폼 제목:

```text
Adaptive Touch Experiment - Final Comparison Survey
```

폼 설명:

```text
세 조건을 모두 플레이한 뒤 작성하는 최종 비교 설문입니다.
각 조건을 비교해 가장 조작하기 쉬웠던 방식, 가장 신뢰할 수 있었던 방식, 실제 모바일 게임에 적용하고 싶은 방식을 선택해 주세요.
```

## Section 1. 참가자 정보

### Q1. 참가자 번호

타입: 단답형

필수: 예

### Q2. 실제 플레이한 조건 순서

타입: 드롭다운

필수: 예

선택지:

```text
Raw -> NoCal -> Calibrated
NoCal -> Calibrated -> Raw
Calibrated -> Raw -> NoCal
Raw -> Calibrated -> NoCal
NoCal -> Raw -> Calibrated
Calibrated -> NoCal -> Raw
기타
```

## Section 2. 선택형 비교

### Q1. 세 조건 중 가장 조작하기 쉬웠던 것은 무엇인가?

타입: 객관식

필수: 예

선택지:

```text
Raw Button
Adaptive Without Calibration
Adaptive With Calibration
```

### Q2. 세 조건 중 가장 실수가 적었다고 느낀 것은 무엇인가?

타입: 객관식

필수: 예

선택지:

```text
Raw Button
Adaptive Without Calibration
Adaptive With Calibration
```

### Q3. 세 조건 중 가장 공정하다고 느낀 것은 무엇인가?

타입: 객관식

필수: 예

선택지:

```text
Raw Button
Adaptive Without Calibration
Adaptive With Calibration
```

### Q4. 세 조건 중 실제 모바일 게임에 적용된다면 가장 쓰고 싶은 것은 무엇인가?

타입: 객관식

필수: 예

선택지:

```text
Raw Button
Adaptive Without Calibration
Adaptive With Calibration
```

### Q5. 가장 불편했던 조건은 무엇인가?

타입: 객관식

필수: 예

선택지:

```text
Raw Button
Adaptive Without Calibration
Adaptive With Calibration
```

## Section 3. 최종 7점 척도 질문

척도:

```text
1 = 전혀 그렇지 않습니다
7 = 매우 그렇습니다
```

질문 타입: 선형 배율

모든 질문 필수: 예

### Final Q1

```text
Adaptive With Calibration은 Raw Button보다 전투 중 오터치를 줄이는 데 도움이 되었다.
```

### Final Q2

```text
Adaptive With Calibration은 Adaptive Without Calibration보다 내 터치 습관에 더 잘 맞았다.
```

### Final Q3

```text
자동 입력 보정은 게임 조작을 더 편하게 만들었다.
```

### Final Q4

```text
자동 입력 보정은 게임을 불공정하게 만든다고 느끼지 않았다.
```

### Final Q5

```text
calibration을 매 게임 시작 전 또는 최초 1회 수행하는 것은 받아들일 수 있다.
```

### Final Q6

```text
이 시스템은 모바일 액션 게임의 작은 버튼 오작동 문제를 줄이는 데 의미가 있다.
```

## Section 4. 자유 응답

### Q1. 자동 보정이 가장 도움이 된 상황은 언제였는가?

타입: 단락형

필수: 아니오

### Q2. 자동 보정이 어색하거나 불공정하게 느껴진 상황은 언제였는가?

타입: 단락형

필수: 아니오

### Q3. 버튼 크기, 버튼 위치, 쿨다운 표시, HP 표시 중 개선이 필요한 부분은 무엇인가?

타입: 단락형

필수: 아니오

### Q4. calibration 과정에서 불필요하거나 어려웠던 부분은 무엇인가?

타입: 단락형

필수: 아니오

### Q5. 반대로 calibration에 더 추가하면 좋을 것 같은 상황은 무엇인가?

타입: 단락형

필수: 아니오

### Q6. 실제 모바일 게임에 이 시스템이 들어간다면 어떤 방식으로 제공되는 것이 좋겠는가?

타입: 단락형

필수: 아니오

### Q7. 전체적으로 이 시스템이 오터치 문제를 줄이는 데 의미 있다고 느꼈는가? 이유를 함께 적어 달라.

타입: 단락형

필수: 예

---

# 운영 체크리스트

실험 진행자는 다음 순서로 운영한다.

1. Form 1 작성
2. 참가자 번호를 게임 시작 화면에 입력
3. 배정된 순서대로 조건 1 플레이
4. 결과 화면에서 `Copy Time-Series JSON` 클릭
5. 로그를 별도 채널로 전달
6. 해당 조건의 직후 설문 작성: Raw는 Form 2A, NoCal은 Form 2B, Calibrated는 Form 2C
7. 조건 2, 조건 3도 같은 방식으로 반복
8. Form 3 작성
9. 참가자별 3개 로그와 5개 설문 제출 여부 확인

참가자 1명당 필요한 제출물:

- Form 1 응답 1개
- Form 2A 응답 1개
- Form 2B 응답 1개
- Form 2C 응답 1개
- Form 3 응답 1개
- 로그 JSON 또는 TXT 3개

## 빠른 운영 버전

시간이 부족하면 Form 1은 생략하고 Form 2A/2B/2C와 Form 3만 사용한다.

이 경우 각 조건별 직후 설문 첫 섹션에 다음 질문을 추가한다.

```text
모바일 액션 게임 경험
주로 사용하는 손
사용 기기
실험 조건 순서
```

하지만 분석을 깔끔하게 하려면 Form 1을 별도로 두는 것이 좋다.

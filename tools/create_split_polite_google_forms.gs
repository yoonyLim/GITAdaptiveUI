function createAdaptiveTouchExperimentForms() {
  createSplitPoliteExperimentForms();
}

function createSplitPoliteExperimentForms() {
  const forms = [];
  forms.push(createParticipantRegistrationForm());
  forms.push(createRawPostConditionForm());
  forms.push(createAdaptiveNoCalibrationPostConditionForm());
  forms.push(createAdaptiveCalibratedPostConditionForm());
  forms.push(createFinalComparisonForm());

  Logger.log('Adaptive Touch split polite Google Forms created');
  forms.forEach(function(entry) {
    Logger.log('');
    Logger.log(entry.label);
    Logger.log('Edit URL: ' + entry.form.getEditUrl());
    Logger.log('Published URL: ' + entry.form.getPublishedUrl());
  });
}

function createParticipantRegistrationForm() {
  const form = FormApp.create('Adaptive Touch 사용자 평가 - 참가자 등록 v2');
  form.setDescription(
    '모바일 탑다운 액션 게임에서 작은 버튼 입력 보정 방식이 조작 안정성과 오터치 감소에 도움이 되는지 평가하는 실험입니다.\n\n' +
    '총 3개 조건을 플레이하시며, 각 조건은 최대 3개 스테이지로 구성됩니다. 각 스테이지는 최대 30초입니다.\n\n' +
    '실험 시작 전에 아래 정보를 작성해 주세요.'
  );
  form.setCollectEmail(false);
  form.setAllowResponseEdits(false);
  form.setLimitOneResponsePerUser(false);
  form.setConfirmationMessage('등록 정보가 저장되었습니다. 안내에 따라 첫 번째 조건을 플레이해 주세요.');

  form.addTextItem()
    .setTitle('참가자 번호를 입력해 주세요.')
    .setHelpText('예: P01')
    .setRequired(true);

  form.addListItem()
    .setTitle('배정받으신 실험 조건 순서를 선택해 주세요.')
    .setChoiceValues([
      'Order A: Raw -> NoCal -> Calibrated',
      'Order B: NoCal -> Calibrated -> Raw',
      'Order C: Calibrated -> Raw -> NoCal',
      'Order D: Raw -> Calibrated -> NoCal',
      'Order E: NoCal -> Raw -> Calibrated',
      'Order F: Calibrated -> NoCal -> Raw'
    ])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('실험에 사용하시는 기기를 선택해 주세요.')
    .setChoiceValues(['iPhone', 'Android', 'PC/Editor', '기타'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('주로 사용하시는 손을 선택해 주세요.')
    .setChoiceValues(['오른손', '왼손', '양손 비슷함'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('모바일 액션 게임을 어느 정도 플레이해 보셨습니까?')
    .setChoiceValues(['거의 없습니다', '가끔 플레이합니다', '자주 플레이합니다', '매우 자주 플레이합니다'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('모바일 게임에서 조이스틱 이동과 스킬 버튼을 동시에 조작해 보신 경험이 어느 정도 있으십니까?')
    .setChoiceValues(['없습니다', '조금 있습니다', '자주 있습니다', '매우 익숙합니다'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('실험 진행 중 조작 로그를 수집하는 것에 동의하십니까?')
    .setChoiceValues(['동의합니다', '동의하지 않습니다'])
    .setRequired(true);

  return { label: 'Form 1 - Participant registration', form: form };
}

function createRawPostConditionForm() {
  const form = createBasePostConditionForm(
    'Adaptive Touch 사용자 평가 - Raw Button 플레이 직후 설문',
    'Raw Button 조건을 방금 플레이하신 뒤 작성하는 설문입니다.\n\n' +
    '이 조건은 Gaussian/Bayesian 입력 보정이나 calibration 없이, 기본 고정 버튼만 사용하는 기준선 조건입니다.'
  );
  addCommonScaleItems(form);
  addScaleItems(form, [
    '작은 버튼을 직접 누르는 방식에서도 원하는 버튼을 정확히 누르기 쉬우셨습니까?',
    '버튼 경계 근처에서 입력 실패가 자주 발생하지 않았다고 느끼셨습니까?',
    '전투 중 버튼 위치를 계속 의식하지 않고 플레이하기 쉬우셨습니까?',
    'Raw Button 조건은 기준선 조작 방식으로 이해하기 쉬우셨습니까?'
  ]);
  addPostConditionOpenItems(form);
  return { label: 'Form 2A - Raw Button post-condition', form: form };
}

function createAdaptiveNoCalibrationPostConditionForm() {
  const form = createBasePostConditionForm(
    'Adaptive Touch 사용자 평가 - Adaptive Without Calibration 플레이 직후 설문',
    'Adaptive Without Calibration 조건을 방금 플레이하신 뒤 작성하는 설문입니다.\n\n' +
    '이 조건은 사용자별 calibration 없이, 전투 상황 기반 Gaussian/Bayesian 입력 보정만 사용하는 조건입니다.'
  );
  addCommonScaleItems(form);
  addAdaptiveScaleItems(form);
  addScaleItems(form, [
    '사용자별 calibration이 없어도 입력 보정이 자연스럽게 작동한다고 느끼셨습니까?',
    '전투 상황에 따라 보정 결과가 달라지는 방식이 납득 가능하셨습니까?',
    '경계 근처를 눌렀을 때 원하는 행동이 실행되는 경우가 많다고 느끼셨습니까?'
  ]);
  addPostConditionOpenItems(form);
  return { label: 'Form 2B - Adaptive without calibration post-condition', form: form };
}

function createAdaptiveCalibratedPostConditionForm() {
  const form = createBasePostConditionForm(
    'Adaptive Touch 사용자 평가 - Adaptive With Calibration 플레이 직후 설문',
    'Adaptive With Calibration 조건을 방금 플레이하신 뒤 작성하는 설문입니다.\n\n' +
    '이 조건은 시작 전 calibration으로 측정한 사용자별 터치 편향과 전투 상황 기반 Gaussian/Bayesian 입력 보정을 함께 사용하는 조건입니다.'
  );
  addCommonScaleItems(form);
  addAdaptiveScaleItems(form);
  addCalibrationScaleItems(form);
  addPostConditionOpenItems(form);
  return { label: 'Form 2C - Adaptive with calibration post-condition', form: form };
}

function createBasePostConditionForm(title, description) {
  const form = FormApp.create(title);
  form.setDescription(
    description + '\n\n' +
    '결과 화면의 Copy Time-Series JSON 버튼을 눌러 로그를 별도로 전달하신 뒤, 아래 문항에 응답해 주세요.'
  );
  form.setCollectEmail(false);
  form.setAllowResponseEdits(false);
  form.setLimitOneResponsePerUser(false);
  form.setConfirmationMessage('응답이 저장되었습니다. 다음 안내에 따라 실험을 계속 진행해 주세요.');

  form.addTextItem()
    .setTitle('참가자 번호를 입력해 주세요.')
    .setHelpText('예: P01')
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('이번 조건은 몇 번째로 플레이하셨습니까?')
    .setChoiceValues(['1번째 조건', '2번째 조건', '3번째 조건'])
    .setRequired(true);

  form.addTextItem()
    .setTitle('결과 화면 또는 final_session_logs.json에 표시된 session_id를 입력해 주세요.')
    .setHelpText('예: 20260611T015101472Z_P01')
    .setRequired(false);

  form.addCheckboxItem()
    .setTitle('로그를 어떤 방식으로 전달하셨습니까?')
    .setChoiceValues([
      'Copy Time-Series JSON을 눌러 카카오톡으로 전달했습니다',
      'Copy Time-Series JSON을 눌러 메모 또는 메일로 전달했습니다',
      'final_session_logs.json 파일로 전달했습니다',
      'COPY_ALL_FINAL_SESSION_LOGS.txt 파일로 전달했습니다',
      '아직 전달하지 않았습니다'
    ])
    .setRequired(true);

  form.addParagraphTextItem()
    .setTitle('결과 화면에 표시된 요약 정보를 입력해 주세요.')
    .setHelpText('예: Stage 3 failed, damage 219, button presses 137')
    .setRequired(false);

  return form;
}

function addCommonScaleItems(form) {
  addScaleItems(form, [
    '버튼을 눌렀을 때 실행된 행동이 본인이 의도한 행동과 잘 맞았다고 느끼셨습니까?',
    '버튼 경계 근처를 눌렀을 때의 처리 방식이 납득 가능하셨습니까?',
    '빠르게 연속 입력하는 상황에서도 조작이 안정적이라고 느끼셨습니까?',
    '조이스틱으로 이동하면서 오른손 버튼을 누르는 흐름이 자연스럽다고 느끼셨습니까?',
    '전투 중 캐릭터, 적, 투사체, HP 상태에 집중하기 쉬우셨습니까?',
    'UI 버튼과 쿨다운 표시를 이해하기 쉬우셨습니까?',
    '의도하지 않은 스킬 발동이나 조작 실수로 인한 답답함이 적었다고 느끼셨습니까?',
    '현재 조건의 조작 방식이 모바일 액션 게임에서 유용할 수 있다고 느끼셨습니까?',
    '현재 조건의 조작 방식이 공정하고 신뢰할 수 있다고 느끼셨습니까?',
    '전체적으로 이번 조건의 조작감에 만족하셨습니까?'
  ]);
}

function addAdaptiveScaleItems(form) {
  addScaleItems(form, [
    '입력 보정이 전투 중 조작 실수를 줄이는 데 도움이 되었다고 느끼셨습니까?',
    '애매한 터치를 시스템이 다른 행동으로 해석했을 때 그 결과가 납득 가능하셨습니까?',
    '자동 보정이 본인의 의도한 입력을 과도하게 바꾸지 않았다고 느끼셨습니까?',
    '보정 결과를 신뢰하고 계속 플레이할 수 있다고 느끼셨습니까?'
  ]);
}

function addCalibrationScaleItems(form) {
  addScaleItems(form, [
    '시작 전 calibration 과정이 이후 플레이에 도움이 되었다고 느끼셨습니까?',
    'calibration에 걸린 시간은 실험 참여 과정에서 받아들일 수 있는 수준이었습니까?',
    'calibration 이후 버튼 입력이 본인의 손 위치 습관에 더 잘 맞는다고 느끼셨습니까?',
    '이동 중 버튼 입력, 빠른 전환 입력, 경계 입력을 calibration에 포함한 것이 적절했다고 느끼셨습니까?',
    'calibration과 실시간 보정을 함께 사용하는 방식이 실제 모바일 게임에서도 가능하다고 느끼셨습니까?'
  ]);
}

function addPostConditionOpenItems(form) {
  form.addParagraphTextItem()
    .setTitle('이번 조건에서 조작이 가장 편하다고 느끼신 순간은 언제였습니까?')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('이번 조건에서 조작이 가장 불편하거나 어색하다고 느끼신 순간은 언제였습니까?')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('버튼 크기, 위치, HP 표시, 쿨다운 표시 중 개선이 필요하다고 느끼신 부분이 있으셨습니까?')
    .setRequired(false);
}

function createFinalComparisonForm() {
  const form = FormApp.create('Adaptive Touch 사용자 평가 - 최종 비교 설문 v2');
  form.setDescription(
    '세 조건을 모두 플레이하신 뒤 작성하는 최종 비교 설문입니다.\n\n' +
    'Raw Button, Adaptive Without Calibration, Adaptive With Calibration 조건을 비교해 응답해 주세요.'
  );
  form.setCollectEmail(false);
  form.setAllowResponseEdits(false);
  form.setLimitOneResponsePerUser(false);
  form.setConfirmationMessage('최종 설문 응답이 저장되었습니다. 실험에 참여해 주셔서 감사합니다.');

  form.addTextItem()
    .setTitle('참가자 번호를 입력해 주세요.')
    .setHelpText('예: P01')
    .setRequired(true);

  form.addListItem()
    .setTitle('실제로 플레이하신 실험 조건 순서를 선택해 주세요.')
    .setChoiceValues([
      'Order A: Raw -> NoCal -> Calibrated',
      'Order B: NoCal -> Calibrated -> Raw',
      'Order C: Calibrated -> Raw -> NoCal',
      'Order D: Raw -> Calibrated -> NoCal',
      'Order E: NoCal -> Raw -> Calibrated',
      'Order F: Calibrated -> NoCal -> Raw'
    ])
    .setRequired(true);

  addConditionChoiceItem(form, '전체적으로 가장 조작감이 좋았던 조건을 선택해 주세요.');
  addConditionChoiceItem(form, '오입력이나 실수로 인한 답답함이 가장 적었던 조건을 선택해 주세요.');
  addConditionChoiceItem(form, '전투 중 가장 공정하고 신뢰할 수 있다고 느끼신 조건을 선택해 주세요.');
  addConditionChoiceItem(form, '실제 모바일 액션 게임에 가장 적용할 만하다고 느끼신 조건을 선택해 주세요.');

  addScaleItems(form, [
    '세 조건의 차이를 충분히 구분하실 수 있었습니까?',
    'Adaptive Without Calibration 조건은 Raw Button 조건보다 조작 안정성에 도움이 되었다고 느끼셨습니까?',
    'Adaptive With Calibration 조건은 Raw Button 조건보다 조작 안정성에 도움이 되었다고 느끼셨습니까?',
    'Adaptive With Calibration 조건은 Adaptive Without Calibration 조건보다 본인의 터치 습관에 더 잘 맞는다고 느끼셨습니까?',
    'calibration 시간이 추가되더라도 그만한 가치가 있다고 느끼셨습니까?',
    'Adaptive Touch 방식은 향후 모바일 액션 게임에 적용할 가치가 있다고 느끼셨습니까?',
    '실험 난이도와 플레이 시간은 세 조건을 비교하기에 적절하셨습니까?',
    '게임 결과 화면의 로그 복사 방식은 이해하기 쉬우셨습니까?'
  ]);

  form.addParagraphTextItem()
    .setTitle('세 조건을 비교했을 때 가장 큰 차이가 무엇이라고 느끼셨습니까?')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('Adaptive Touch 방식에서 개선이 필요하다고 느끼신 점이 있으시면 적어 주세요.')
    .setRequired(false);

  form.addParagraphTextItem()
    .setTitle('추가로 남기고 싶으신 의견이 있으시면 자유롭게 작성해 주세요.')
    .setRequired(false);

  return { label: 'Form 3 - Final comparison', form: form };
}

function addConditionChoiceItem(form, title) {
  form.addMultipleChoiceItem()
    .setTitle(title)
    .setChoiceValues([
      'Raw Button',
      'Adaptive Without Calibration',
      'Adaptive With Calibration',
      '잘 모르겠습니다'
    ])
    .setRequired(true);
}

function addScaleItems(form, titles) {
  titles.forEach(function(title) {
    form.addScaleItem()
      .setTitle(title)
      .setBounds(1, 7)
      .setLabels('전혀 그렇지 않습니다', '매우 그렇습니다')
      .setRequired(true);
  });
}

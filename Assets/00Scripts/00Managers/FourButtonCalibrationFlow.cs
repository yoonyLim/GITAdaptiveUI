using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class FourButtonCalibrationFlow : MonoBehaviour
{
    public AdaptiveTouchManager touchManager;
    public UserContextPriorModel contextPriorModel;
    public RoguelikeGameManager gameManager;
    public ParticipantConfig participantConfig;
    public ConditionManager conditionManager;
    public GameObject startScreenRoot;
    public Button startButton;
    public Button rawStartButton;
    public Button noCalibrationStartButton;
    public TextMeshProUGUI stageText;
    public TextMeshProUGUI feedbackText;

    [Header("Protocol")]
    public int warmupTapsPerButton = 1;
    public int centerTapsPerButton = 2;
    public int edgeTapsPerButton = 1;
    public int transitionTapsPerButton = 1;
    public int joystickStressTapsPerButton = 1;
    public int validationTapsPerButton = 1;
    public bool runCombatScenarioCalibration = true;
    public int combatScenarioRepeats = 1;
    public bool compactCombatScenarioCalibration = true;
    public float combatScenarioSettleSeconds = 0.6f;
    public bool randomizeWithinBlocks = true;
    public int randomSeed = 4173;
    public bool autoStartGameAfterCalibration = true;
    public float gameStartDelaySeconds = 1.8f;

    [Header("Prompt Timing")]
    public CanvasGroup feedbackGroup;
    public float defaultInstructionReadSeconds = 0.35f;
    public float blockIntroReadSeconds = 1.15f;
    public float rapidSwitchReadSeconds = 0.95f;
    public float joystickReadSeconds = 0.95f;
    public float combatScenarioReadSeconds = 1.25f;
    public float validationReadSeconds = 0.8f;

    public bool CalibrationActive { get; private set; }
    public bool CalibrationComplete { get; private set; }
    public int CurrentTrialIndex { get; private set; }
    public int CalibrationTotalCount => trials.Count;
    public string CurrentTargetAction => CurrentTrial.targetAction;
    public string CurrentTrialType => CurrentTrial.trialType;
    public string CurrentScenarioKey => CurrentTrial.scenario.ToString();
    public string CurrentInstruction => CurrentTrial.instruction;
    public bool CurrentTrialAcceptsInput =>
        CalibrationActive &&
        !waitingForCombatSettle &&
        CurrentTrialIndex >= 0 &&
        CurrentTrialIndex < trials.Count &&
        Time.unscaledTime >= ignoreInputUntil;
    public float CurrentTrialReadRemaining => Mathf.Max(0f, ignoreInputUntil - Time.unscaledTime);

    private readonly List<CalibrationTrial> trials = new List<CalibrationTrial>();
    private readonly List<CalibrationTrial> reusableBlock = new List<CalibrationTrial>();
    private float ignoreInputUntil;
    private Coroutine startGameRoutine;
    private Coroutine combatAdvanceRoutine;
    private bool scenarioSceneActive;
    private bool waitingForCombatSettle;
    private bool currentTrialReadyPromptShown;
    private string lastStartedTrialType = "";

    private static readonly string[] Actions =
    {
        "Attack",
        "Dodge",
        "Heal",
        "Whirlwind"
    };

    private CalibrationTrial CurrentTrial =>
        CurrentTrialIndex >= 0 && CurrentTrialIndex < trials.Count
            ? trials[CurrentTrialIndex]
            : default;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Start()
    {
        ResolveReferences();
        ConfigureStartButtons();
        ShowStartScreen();
    }

    private void Update()
    {
        if (!CalibrationActive)
        {
            return;
        }

        UpdateCalibrationStageLabel();
        UpdateReadyPromptIfNeeded();

        if (Time.unscaledTime < ignoreInputUntil)
        {
            return;
        }

#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SubmitCalibrationTouch(Mouse.current.position.ReadValue());
        }
#endif

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                SubmitCalibrationTouch(touch.screenPosition);
            }
        }
    }

    public void BeginCalibration()
    {
        ResolveReferences();
        ApplyParticipantAndCondition(DatasetSchema.ConditionCalibratedContextBayesian);

        if (touchManager == null)
        {
            Debug.LogWarning("FourButtonCalibrationFlow requires AdaptiveTouchManager.");
            return;
        }

        touchManager.SetRawButtonOnlyMode(false);
        touchManager.SetAdaptiveTouchEnabled(true);
        touchManager.enableOnlineTouchAdaptation = true;

        if (startGameRoutine != null)
        {
            StopCoroutine(startGameRoutine);
            startGameRoutine = null;
        }

        BuildTrials();
        if (trials.Count == 0)
        {
            Debug.LogWarning("FourButtonCalibrationFlow has no trials.");
            return;
        }

        startScreenRoot?.SetActive(false);
        gameManager?.ClearCalibrationScenario();
        touchManager.ResetFourButtonCalibration();
        touchManager.SetFourButtonCalibrationActive(true);
        contextPriorModel?.ResetUserPriors();
        scenarioSceneActive = false;

        CalibrationActive = true;
        CalibrationComplete = false;
        CurrentTrialIndex = -1;
        currentTrialReadyPromptShown = false;
        lastStartedTrialType = "";
        ignoreInputUntil = Time.unscaledTime + 0.16f;

        BeginNextTrial();
    }

    public bool SubmitCalibrationTouch(Vector2 screenPosition)
    {
        if (!CalibrationActive || waitingForCombatSettle || CurrentTrialIndex < 0 || CurrentTrialIndex >= trials.Count)
        {
            return false;
        }

        if (Time.unscaledTime < ignoreInputUntil)
        {
            return false;
        }

        CalibrationTrial trial = CurrentTrial;
        if (trial.isCombatScenario)
        {
            return SubmitCombatScenarioTouch(trial, screenPosition);
        }

        if (!touchManager.IsTouchNearAction(trial.targetAction, screenPosition, trial.maxAcceptDistance))
        {
            SetFeedback($"{trial.targetAction} 버튼 근처를 누르세요.", Color.yellow);
            return false;
        }

        if (trial.useForModel)
        {
            touchManager.AddFourButtonCalibrationSample(
                trial.targetAction,
                screenPosition,
                trial.affectsCenterBias,
                trial.sampleWeight);
            string modelEffect = trial.affectsCenterBias ? "mean+spread" : "spread-only";
            string modelState = touchManager.GetFourButtonCalibrationModelState(trial.targetAction, modelEffect);
            SetFeedback(
                $"{trial.targetAction} {trial.trialType} 샘플 {touchManager.GetFourButtonCalibrationSampleCount(trial.targetAction)}개 저장.\n{modelState}",
                Color.cyan);
        }
        else if (trial.isValidation)
        {
            string validationResult = touchManager.RecordFourButtonCalibrationValidation(trial.targetAction, screenPosition, trial.trialType);
            SetFeedback(validationResult, validationResult.Contains("MISS") ? Color.yellow : Color.cyan);
        }
        else
        {
            SetFeedback($"{trial.targetAction} 워밍업 입력을 받았습니다.", Color.cyan);
        }

        ignoreInputUntil = Time.unscaledTime + 0.08f;
        BeginNextTrial();
        return true;
    }

    public bool TryGetCurrentTargetCenter(out Vector2 center)
    {
        center = Vector2.zero;
        if (!CalibrationActive || string.IsNullOrEmpty(CurrentTargetAction) || touchManager == null)
        {
            return false;
        }

        return touchManager.TryGetActionButtonCenter(CurrentTargetAction, out center);
    }

    private void BeginNextTrial()
    {
        ClearScenarioSceneIfNeeded();
        CurrentTrialIndex++;
        waitingForCombatSettle = false;
        if (CurrentTrialIndex >= trials.Count)
        {
            CompleteCalibration();
            return;
        }

        CalibrationTrial trial = CurrentTrial;
        touchManager.SetFourButtonCalibrationTarget(trial.targetAction);
        bool blockIntro = CurrentTrialIndex == 0 ||
                          !string.Equals(trial.trialType, lastStartedTrialType, StringComparison.OrdinalIgnoreCase);
        lastStartedTrialType = trial.trialType;
        float readSeconds = InstructionReadSecondsFor(trial, blockIntro);
        ignoreInputUntil = Time.unscaledTime + readSeconds;
        currentTrialReadyPromptShown = readSeconds <= 0.05f;

        if (trial.isCombatScenario)
        {
            touchManager.ClearFourButtonCalibrationTarget();
            gameManager?.BeginCalibrationScenario(trial.scenario, true);
            scenarioSceneActive = true;
        }

        if (stageText != null)
        {
            UpdateCalibrationStageLabel();
        }

        SetFeedback(BuildInstructionPrompt(trial, blockIntro), Color.white);
    }

    private void UpdateCalibrationStageLabel()
    {
        if (stageText == null || !CalibrationActive)
        {
            return;
        }

        stageText.text = $"캘리브레이션 {CurrentTrialIndex + 1} / {trials.Count}";
    }

    private void CompleteCalibration()
    {
        CalibrationActive = false;
        CalibrationComplete = true;
        ClearScenarioSceneIfNeeded();
        StopCombatAdvanceRoutine();
        touchManager.SetFourButtonCalibrationActive(false);
        touchManager.ClearFourButtonCalibrationTarget();

        if (stageText != null)
        {
            stageText.text = "캘리브레이션 완료";
        }

        string contextSummary = contextPriorModel != null
            ? $" | {contextPriorModel.Summary(ADUIContextScenario.AttackCommitWindow)} | {contextPriorModel.Summary(ADUIContextScenario.PreDodgeWindow)} | {contextPriorModel.Summary(ADUIContextScenario.ImmediateDodgeThreat)} | {contextPriorModel.Summary(ADUIContextScenario.ProjectileDodgeThreat)} | {contextPriorModel.Summary(ADUIContextScenario.MovingUnderPressure)} | {contextPriorModel.Summary(ADUIContextScenario.LowHpHeal)} | {contextPriorModel.Summary(ADUIContextScenario.LowHpThreat)} | {contextPriorModel.Summary(ADUIContextScenario.CrowdWhirlwind)}"
            : string.Empty;
        SetFeedback(
            "캘리브레이션 완료.\n터치 프로필과 상황별 prior가 준비되었습니다. 곧 게임을 시작합니다.",
            Color.green);
        Debug.Log($"[ADUI] Four-button calibration complete. {touchManager.FourButtonCalibrationSummary}{contextSummary}");

        if (autoStartGameAfterCalibration && startGameRoutine == null)
        {
            startGameRoutine = StartCoroutine(StartGameAfterDelay());
        }
    }

    private IEnumerator StartGameAfterDelay()
    {
        yield return new WaitForSeconds(gameStartDelaySeconds);
        HideFeedback();
        gameManager?.BeginPrototype();
        startGameRoutine = null;
    }

    public void BeginGameWithoutCalibration()
    {
        ResolveReferences();
        ApplyParticipantAndCondition(DatasetSchema.ConditionNoCalibrationContextBayesian);

        if (startGameRoutine != null)
        {
            StopCoroutine(startGameRoutine);
            startGameRoutine = null;
        }

        CalibrationActive = false;
        CalibrationComplete = false;
        ClearScenarioSceneIfNeeded();
        StopCombatAdvanceRoutine();
        touchManager?.ResetFourButtonCalibration();
        touchManager?.SetFourButtonCalibrationActive(false);
        touchManager?.SetRawButtonOnlyMode(false);
        touchManager?.SetAdaptiveTouchEnabled(true);
        if (touchManager != null)
        {
            touchManager.enableOnlineTouchAdaptation = true;
        }

        contextPriorModel?.ResetUserPriors();
        startScreenRoot?.SetActive(false);
        SetFeedback("캘리브레이션 없이 시작합니다. 기본 Gaussian 보정이 적용됩니다.", Color.white);
        gameManager?.BeginPrototype();
    }

    public void BeginRawGame()
    {
        ResolveReferences();
        ApplyParticipantAndCondition(DatasetSchema.ConditionRawButton);

        if (startGameRoutine != null)
        {
            StopCoroutine(startGameRoutine);
            startGameRoutine = null;
        }

        CalibrationActive = false;
        CalibrationComplete = false;
        ClearScenarioSceneIfNeeded();
        StopCombatAdvanceRoutine();
        touchManager?.ResetFourButtonCalibration();
        touchManager?.SetFourButtonCalibrationActive(false);
        if (touchManager != null)
        {
            touchManager.SetRawButtonOnlyMode(true);
            touchManager.enableOnlineTouchAdaptation = false;
        }

        contextPriorModel?.ResetUserPriors();
        startScreenRoot?.SetActive(false);
        SetFeedback("기본 버튼 조건으로 시작합니다. 버튼 원 안의 직접 입력만 실행됩니다.", Color.white);
        gameManager?.BeginPrototype();
    }

    public void HideCalibrationPrompt()
    {
        HideFeedback();
    }

    private void UpdateReadyPromptIfNeeded()
    {
        if (!CalibrationActive ||
            currentTrialReadyPromptShown ||
            waitingForCombatSettle ||
            CurrentTrialIndex < 0 ||
            CurrentTrialIndex >= trials.Count ||
            Time.unscaledTime < ignoreInputUntil)
        {
            return;
        }

        currentTrialReadyPromptShown = true;
        SetFeedback(BuildReadyPrompt(CurrentTrial), new Color(0.5f, 1f, 0.62f, 1f));
    }

    private float InstructionReadSecondsFor(CalibrationTrial trial, bool blockIntro)
    {
        float seconds = Mathf.Max(0f, defaultInstructionReadSeconds);

        if (string.Equals(trial.trialType, "rapid_switch", StringComparison.OrdinalIgnoreCase))
        {
            seconds = Mathf.Max(seconds, rapidSwitchReadSeconds);
        }
        else if (string.Equals(trial.trialType, "joystick_hold", StringComparison.OrdinalIgnoreCase))
        {
            seconds = Mathf.Max(seconds, joystickReadSeconds);
        }
        else if (string.Equals(trial.trialType, "validation", StringComparison.OrdinalIgnoreCase))
        {
            seconds = Mathf.Max(seconds, validationReadSeconds);
        }
        else if (trial.isCombatScenario)
        {
            seconds = Mathf.Max(seconds, combatScenarioReadSeconds);
        }

        if (blockIntro)
        {
            seconds = Mathf.Max(seconds, blockIntroReadSeconds);
        }

        return seconds;
    }

    private string BuildInstructionPrompt(CalibrationTrial trial, bool blockIntro)
    {
        string phase = blockIntro ? "새 단계 읽기" : "읽기";
        string hint = TrialHint(trial);
        return string.IsNullOrEmpty(hint)
            ? $"{phase}  {CurrentTrialIndex + 1}/{trials.Count}\n{trial.instruction}"
            : $"{phase}  {CurrentTrialIndex + 1}/{trials.Count}\n{trial.instruction}\n{hint}";
    }

    private string BuildReadyPrompt(CalibrationTrial trial)
    {
        string timing = IsTimingSensitiveTrial(trial)
            ? "짧은 입력 구간입니다. 지금 누르세요."
            : "준비되면 누르세요.";
        return $"시작  {CurrentTrialIndex + 1}/{trials.Count}\n{trial.instruction}\n{timing}";
    }

    private string TrialHint(CalibrationTrial trial)
    {
        if (trial.isCombatScenario)
        {
            return $"상황: {UserContextPriorModel.ScenarioLabel(trial.scenario)}";
        }

        if (string.Equals(trial.trialType, "rapid_switch", StringComparison.OrdinalIgnoreCase))
        {
            return "가까운 버튼 사이를 빠르게 전환할 때의 터치를 측정합니다.";
        }

        if (string.Equals(trial.trialType, "joystick_hold", StringComparison.OrdinalIgnoreCase))
        {
            return "왼쪽 조이스틱으로 이동을 유지한 상태에서 목표 버튼을 누르세요.";
        }

        if (string.Equals(trial.trialType, "inner_edge", StringComparison.OrdinalIgnoreCase))
        {
            return "모호한 터치를 측정하기 위해 버튼 안쪽 경계 근처를 누르세요.";
        }

        if (string.Equals(trial.trialType, "validation", StringComparison.OrdinalIgnoreCase))
        {
            return "검증 샘플입니다. 이 터치는 모델 업데이트에 사용하지 않습니다.";
        }

        return "";
    }

    private bool IsTimingSensitiveTrial(CalibrationTrial trial)
    {
        return trial.isCombatScenario ||
               string.Equals(trial.trialType, "rapid_switch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trial.trialType, "joystick_hold", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trial.trialType, "validation", StringComparison.OrdinalIgnoreCase);
    }

    private void BuildTrials()
    {
        trials.Clear();
        int warmupCount = Mathf.Clamp(warmupTapsPerButton, 0, 2);
        int centerCount = Mathf.Clamp(centerTapsPerButton, 2, 4);
        int edgeCount = Mathf.Clamp(edgeTapsPerButton, 1, 4);
        int transitionCount = Mathf.Clamp(transitionTapsPerButton, 0, 3);
        int joystickCount = Mathf.Clamp(joystickStressTapsPerButton, 0, 3);
        int validationCount = Mathf.Clamp(validationTapsPerButton, 1, 4);
        int combatScenarioCount = Mathf.Clamp(combatScenarioRepeats, 0, 2);

        AddRepeatedBlock(warmupCount, "warmup", "워밍업: {0} 버튼을 누르세요.", false, false, false, 0.5f, 420f);
        AddRepeatedBlock(centerCount, "center", "{0} 버튼의 중심을 누르세요.", true, true, false, 1f, 360f);
        AddRepeatedBlock(edgeCount, "inner_edge", "{0} 버튼의 안쪽 경계 근처를 누르세요.", true, false, false, 1.25f, 400f);
        AddRepeatedBlock(transitionCount, "rapid_switch", "전투 중 급하게 누르는 것처럼 {0} 버튼으로 빠르게 전환하세요.", true, false, false, 1.35f, 430f);
        AddRepeatedBlock(joystickCount, "joystick_hold", "왼쪽 조이스틱을 잡은 상태에서 {0} 버튼을 누르세요.", true, false, false, 1.25f, 430f);
        if (runCombatScenarioCalibration)
        {
            AddCombatScenarioBlock(combatScenarioCount);
        }

        AddRepeatedBlock(validationCount, "validation", "검증: 압박 상황이라고 생각하고 {0} 버튼을 누르세요.", false, false, true, 1f, 430f);
    }

    private bool SubmitCombatScenarioTouch(CalibrationTrial trial, Vector2 screenPosition)
    {
        if (contextPriorModel == null)
        {
            SetFeedback("Context prior model is unavailable.", Color.yellow);
            return false;
        }

        if (!touchManager.IsTouchNearAction(trial.targetAction, screenPosition, trial.maxAcceptDistance))
        {
            SetFeedback($"상황 샘플을 위해 {trial.targetAction} 버튼 근처를 누르세요.", Color.yellow);
            return false;
        }

        string actionName = trial.targetAction;
        bool resolved = touchManager.TryResolveFourButtonAction(screenPosition, out string resolvedActionName, out float confidence, out bool directHit);
        bool resolvedAsTarget = resolved && string.Equals(resolvedActionName, actionName, StringComparison.OrdinalIgnoreCase);

        float modelWeight = directHit && resolvedAsTarget
            ? trial.sampleWeight
            : trial.sampleWeight * (resolvedAsTarget ? 0.75f : 0.55f);
        touchManager.AddFourButtonCalibrationSample(
            actionName,
            screenPosition,
            false,
            modelWeight,
            trial.scenario,
            true);

        bool updatesContextPrior = string.Equals(
            actionName,
            UserContextPriorModel.DefaultResponseForScenario(trial.scenario),
            StringComparison.OrdinalIgnoreCase);
        if (updatesContextPrior)
        {
            contextPriorModel.RecordCalibrationResponse(trial.scenario, actionName);
        }
        bool executed = touchManager.TryExecuteNamedAction(actionName, CombatManager.Instance);
        string modelState = touchManager.GetFourButtonCalibrationModelState(
            actionName,
            directHit ? "combat spread" : "combat inferred");
        string contextModelState = touchManager.GetFourButtonContextModelState(
            actionName,
            trial.scenario,
            directHit ? "context gaussian" : "context inferred");
        SetFeedback(
            $"{UserContextPriorModel.ScenarioLabel(trial.scenario)} 상황 입력: {actionName} {(executed ? "실행" : "기록")} {(directHit && resolvedAsTarget ? "직접" : "의도")} 판정={(resolved ? resolvedActionName : "없음")} conf={confidence:F2}\n{modelState}\n{contextModelState} | {(updatesContextPrior ? contextPriorModel.Summary(trial.scenario) : "터치 모델만 업데이트")}",
            Color.cyan);

        ignoreInputUntil = Time.unscaledTime + Mathf.Max(0.08f, combatScenarioSettleSeconds);
        StopCombatAdvanceRoutine();
        waitingForCombatSettle = true;
        combatAdvanceRoutine = StartCoroutine(BeginNextTrialAfterCombatSettle());
        return true;
    }

    private IEnumerator BeginNextTrialAfterCombatSettle()
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, combatScenarioSettleSeconds));
        combatAdvanceRoutine = null;
        waitingForCombatSettle = false;
        BeginNextTrial();
    }

    private void StopCombatAdvanceRoutine()
    {
        waitingForCombatSettle = false;
        if (combatAdvanceRoutine != null)
        {
            StopCoroutine(combatAdvanceRoutine);
            combatAdvanceRoutine = null;
        }
    }

    private void ClearScenarioSceneIfNeeded()
    {
        if (!scenarioSceneActive)
        {
            return;
        }

        gameManager?.ClearCalibrationScenario();
        scenarioSceneActive = false;
    }

    private void AddRepeatedBlock(
        int repetitions,
        string type,
        string instructionFormat,
        bool useForModel,
        bool affectsCenterBias,
        bool isValidation,
        float sampleWeight,
        float maxAcceptDistance)
    {
        for (int i = 0; i < repetitions; i++)
        {
            reusableBlock.Clear();
            foreach (string action in Actions)
            {
                reusableBlock.Add(NewTrial(
                    action,
                    type,
                    string.Format(instructionFormat, action),
                    useForModel,
                    affectsCenterBias,
                    isValidation,
                    sampleWeight,
                    maxAcceptDistance));
            }

            if (randomizeWithinBlocks && type != "warmup")
            {
                ShuffleBlock(reusableBlock, randomSeed + trials.Count + i * 31);
            }

            trials.AddRange(reusableBlock);
        }
    }

    private void AddCombatScenarioBlock(int repetitions)
    {
        if (repetitions <= 0)
        {
            return;
        }

        ADUIContextScenario[] scenarios =
        {
            ADUIContextScenario.AttackCommitWindow,
            ADUIContextScenario.PreDodgeWindow,
            ADUIContextScenario.ImmediateDodgeThreat,
            ADUIContextScenario.ProjectileDodgeThreat,
            ADUIContextScenario.MovingUnderPressure,
            ADUIContextScenario.LowHpHeal,
            ADUIContextScenario.LowHpThreat,
            ADUIContextScenario.CrowdWhirlwind
        };

        for (int i = 0; i < repetitions; i++)
        {
            reusableBlock.Clear();
            for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                ADUIContextScenario scenario = scenarios[scenarioIndex];
                if (compactCombatScenarioCalibration)
                {
                    reusableBlock.Add(NewCombatScenarioTrial(
                        scenario,
                        UserContextPriorModel.DefaultResponseForScenario(scenario)));
                    if (scenario == ADUIContextScenario.LowHpThreat)
                    {
                        reusableBlock.Add(NewCombatScenarioTrial(scenario, "Dodge"));
                    }

                    continue;
                }

                foreach (string action in Actions)
                {
                    reusableBlock.Add(NewCombatScenarioTrial(scenario, action));
                }
            }

            if (randomizeWithinBlocks)
            {
                ShuffleBlock(reusableBlock, randomSeed + trials.Count + i * 47);
            }

            trials.AddRange(reusableBlock);
        }
    }

    private void ShuffleBlock(List<CalibrationTrial> block, int seed)
    {
        System.Random random = new System.Random(seed);
        for (int i = block.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            CalibrationTrial temp = block[i];
            block[i] = block[swapIndex];
            block[swapIndex] = temp;
        }
    }

    private CalibrationTrial NewTrial(
        string action,
        string type,
        string instruction,
        bool useForModel,
        bool affectsCenterBias,
        bool isValidation,
        float sampleWeight,
        float maxAcceptDistance)
    {
        return new CalibrationTrial
        {
            targetAction = action,
            trialType = type,
            instruction = instruction,
            useForModel = useForModel,
            affectsCenterBias = affectsCenterBias,
            isValidation = isValidation,
            sampleWeight = sampleWeight,
            maxAcceptDistance = maxAcceptDistance
        };
    }

    private CalibrationTrial NewCombatScenarioTrial(ADUIContextScenario scenario, string action)
    {
        return new CalibrationTrial
        {
            targetAction = action,
            trialType = "combat_scenario",
            instruction = $"{UserContextPriorModel.ScenarioInstruction(scenario)} 이 상황에서 {action} 버튼을 누르세요.",
            useForModel = true,
            affectsCenterBias = false,
            isValidation = false,
            isCombatScenario = true,
            scenario = scenario,
            sampleWeight = 1f,
            maxAcceptDistance = 480f
        };
    }

    private void ConfigureStartButtons()
    {
        if (rawStartButton != null)
        {
            rawStartButton.onClick.RemoveAllListeners();
            rawStartButton.onClick.AddListener(BeginRawGame);

            TextMeshProUGUI label = rawStartButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "기본 버튼";
            }
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(BeginCalibration);

            TextMeshProUGUI label = startButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "캘리브레이션 적응형";
            }
        }

        if (noCalibrationStartButton != null)
        {
            noCalibrationStartButton.onClick.RemoveAllListeners();
            noCalibrationStartButton.onClick.AddListener(BeginGameWithoutCalibration);

            TextMeshProUGUI label = noCalibrationStartButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "보정 없음 적응형";
            }
        }
    }

    private void ShowStartScreen()
    {
        startScreenRoot?.SetActive(true);
        if (stageText != null)
        {
            stageText.text = "준비 - 실험 조건 선택";
        }
    }

    private void ResolveReferences()
    {
        if (touchManager == null)
        {
            touchManager = FindAnyObjectByType<AdaptiveTouchManager>();
        }

        if (contextPriorModel == null)
        {
            contextPriorModel = FindAnyObjectByType<UserContextPriorModel>();
        }

        if (gameManager == null)
        {
            gameManager = RoguelikeGameManager.Instance != null
                ? RoguelikeGameManager.Instance
                : FindAnyObjectByType<RoguelikeGameManager>();
        }

        if (participantConfig == null)
        {
            participantConfig = FindAnyObjectByType<ParticipantConfig>();
        }

        if (conditionManager == null)
        {
            conditionManager = FindAnyObjectByType<ConditionManager>();
        }
    }

    private void ApplyParticipantAndCondition(string condition)
    {
        string participantId = participantConfig != null
            ? participantConfig.ApplyParticipantInput()
            : "test_user";
        conditionManager?.SetCondition(condition);
        Debug.Log($"[ADUI] Evaluation start participant={participantId} condition={condition}");
    }

    private void SetFeedback(string message, Color color)
    {
        if (feedbackGroup != null)
        {
            feedbackGroup.alpha = string.IsNullOrEmpty(message) ? 0f : 1f;
            feedbackGroup.interactable = false;
            feedbackGroup.blocksRaycasts = false;
        }

        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }

        Debug.Log($"[ADUI] Calibration: {message}");
    }

    private void HideFeedback()
    {
        if (feedbackGroup != null)
        {
            feedbackGroup.alpha = 0f;
        }

        if (feedbackText != null)
        {
            feedbackText.text = string.Empty;
        }
    }

    private struct CalibrationTrial
    {
        public string targetAction;
        public string trialType;
        public string instruction;
        public bool useForModel;
        public bool affectsCenterBias;
        public bool isValidation;
        public bool isCombatScenario;
        public ADUIContextScenario scenario;
        public float sampleWeight;
        public float maxAcceptDistance;
    }
}

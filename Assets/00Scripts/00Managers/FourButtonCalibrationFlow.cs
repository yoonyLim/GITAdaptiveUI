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
    public GameObject startScreenRoot;
    public Button startButton;
    public Button noCalibrationStartButton;
    public TextMeshProUGUI stageText;
    public TextMeshProUGUI feedbackText;

    [Header("Protocol")]
    public int warmupTapsPerButton = 1;
    public int centerTapsPerButton = 3;
    public int edgeTapsPerButton = 1;
    public int transitionTapsPerButton = 2;
    public int joystickStressTapsPerButton = 2;
    public int validationTapsPerButton = 2;
    public bool runCombatScenarioCalibration = true;
    public int combatScenarioRepeats = 2;
    public float combatScenarioSettleSeconds = 0.6f;
    public bool randomizeWithinBlocks = true;
    public int randomSeed = 4173;
    public bool autoStartGameAfterCalibration = true;
    public float gameStartDelaySeconds = 0.55f;

    public bool CalibrationActive { get; private set; }
    public bool CalibrationComplete { get; private set; }
    public int CurrentTrialIndex { get; private set; }
    public int CalibrationTotalCount => trials.Count;
    public string CurrentTargetAction => CurrentTrial.targetAction;
    public string CurrentTrialType => CurrentTrial.trialType;
    public string CurrentScenarioKey => CurrentTrial.scenario.ToString();
    public string CurrentInstruction => CurrentTrial.instruction;

    private readonly List<CalibrationTrial> trials = new List<CalibrationTrial>();
    private readonly List<CalibrationTrial> reusableBlock = new List<CalibrationTrial>();
    private float ignoreInputUntil;
    private Coroutine startGameRoutine;
    private Coroutine combatAdvanceRoutine;
    private bool scenarioSceneActive;
    private bool waitingForCombatSettle;

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

        if (touchManager == null)
        {
            Debug.LogWarning("FourButtonCalibrationFlow requires AdaptiveTouchManager.");
            return;
        }

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
        ignoreInputUntil = Time.unscaledTime + 0.16f;

        BeginNextTrial();
    }

    public bool SubmitCalibrationTouch(Vector2 screenPosition)
    {
        if (!CalibrationActive || waitingForCombatSettle || CurrentTrialIndex < 0 || CurrentTrialIndex >= trials.Count)
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
            SetFeedback($"Tap near {trial.targetAction}.", Color.yellow);
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
                $"{trial.targetAction} {trial.trialType} sample {touchManager.GetFourButtonCalibrationSampleCount(trial.targetAction)} saved.\n{modelState}",
                Color.cyan);
        }
        else if (trial.isValidation)
        {
            string validationResult = touchManager.RecordFourButtonCalibrationValidation(trial.targetAction, screenPosition, trial.trialType);
            SetFeedback(validationResult, validationResult.Contains("MISS") ? Color.yellow : Color.cyan);
        }
        else
        {
            SetFeedback($"{trial.targetAction} warm-up accepted.", Color.cyan);
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

        SetFeedback(trial.instruction, Color.white);
    }

    private void UpdateCalibrationStageLabel()
    {
        if (stageText == null || !CalibrationActive)
        {
            return;
        }

        stageText.text = $"Calibration {CurrentTrialIndex + 1} / {trials.Count}";
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
            stageText.text = "Calibration complete";
        }

        string contextSummary = contextPriorModel != null
            ? $" | {contextPriorModel.Summary(ADUIContextScenario.AttackCommitWindow)} | {contextPriorModel.Summary(ADUIContextScenario.PreDodgeWindow)} | {contextPriorModel.Summary(ADUIContextScenario.ImmediateDodgeThreat)} | {contextPriorModel.Summary(ADUIContextScenario.ProjectileDodgeThreat)} | {contextPriorModel.Summary(ADUIContextScenario.MovingUnderPressure)} | {contextPriorModel.Summary(ADUIContextScenario.LowHpHeal)} | {contextPriorModel.Summary(ADUIContextScenario.CrowdWhirlwind)}"
            : string.Empty;
        SetFeedback($"Calibration complete. {touchManager.FourButtonCalibrationSummary}\nCombat-scenario touch and context priors learned.", Color.green);
        Debug.Log($"[ADUI] Four-button calibration complete. {touchManager.FourButtonCalibrationSummary}{contextSummary}");

        if (autoStartGameAfterCalibration && startGameRoutine == null)
        {
            startGameRoutine = StartCoroutine(StartGameAfterDelay());
        }
    }

    private IEnumerator StartGameAfterDelay()
    {
        yield return new WaitForSeconds(gameStartDelaySeconds);
        gameManager?.BeginPrototype();
        startGameRoutine = null;
    }

    public void BeginGameWithoutCalibration()
    {
        ResolveReferences();

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
        contextPriorModel?.ResetUserPriors();
        startScreenRoot?.SetActive(false);
        SetFeedback("Starting without calibration. Default Gaussian profile is active.", Color.white);
        gameManager?.BeginPrototype();
    }

    private void BuildTrials()
    {
        trials.Clear();
        int warmupCount = Mathf.Clamp(warmupTapsPerButton, 0, 2);
        int centerCount = Mathf.Clamp(centerTapsPerButton, 3, 8);
        int edgeCount = Mathf.Clamp(edgeTapsPerButton, 1, 4);
        int transitionCount = Mathf.Clamp(transitionTapsPerButton, 0, 4);
        int joystickCount = Mathf.Clamp(joystickStressTapsPerButton, 0, 4);
        int validationCount = Mathf.Clamp(validationTapsPerButton, 1, 4);
        int combatScenarioCount = Mathf.Clamp(combatScenarioRepeats, 0, 4);

        AddRepeatedBlock(warmupCount, "warmup", "Warm-up: tap {0}.", false, false, false, 0.5f, 420f);
        AddRepeatedBlock(centerCount, "center", "Tap the center of {0}.", true, true, false, 1f, 360f);
        AddRepeatedBlock(edgeCount, "inner_edge", "Tap {0} near the inside edge between buttons.", true, false, false, 1.25f, 400f);
        AddRepeatedBlock(transitionCount, "rapid_switch", "Quickly switch to {0}, as if under combat pressure.", true, false, false, 1.35f, 430f);
        AddRepeatedBlock(joystickCount, "joystick_hold", "Hold the left joystick, then tap {0}.", true, false, false, 1.25f, 430f);
        if (runCombatScenarioCalibration)
        {
            AddCombatScenarioBlock(combatScenarioCount);
        }

        AddRepeatedBlock(validationCount, "validation", "Validation: tap {0} under pressure.", false, false, true, 1f, 430f);
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
            SetFeedback($"Tap near {trial.targetAction} for this context sample.", Color.yellow);
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
            $"{UserContextPriorModel.ScenarioLabel(trial.scenario)} combat action: {actionName} {(executed ? "executed" : "recorded")} {(directHit && resolvedAsTarget ? "direct" : "intended")} resolved={(resolved ? resolvedActionName : "none")} conf={confidence:F2}\n{modelState}\n{contextModelState} | {(updatesContextPrior ? contextPriorModel.Summary(trial.scenario) : "touch-only context sample")}",
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
            ADUIContextScenario.CrowdWhirlwind,
            ADUIContextScenario.LowHpThreat,
            ADUIContextScenario.CrowdLowHp
        };

        for (int i = 0; i < repetitions; i++)
        {
            reusableBlock.Clear();
            for (int scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                ADUIContextScenario scenario = scenarios[scenarioIndex];
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
            instruction = $"{UserContextPriorModel.ScenarioInstruction(scenario)} Tap {action} under this situation.",
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
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(BeginCalibration);

            TextMeshProUGUI label = startButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "Calibrate & Start";
            }
        }

        if (noCalibrationStartButton != null)
        {
            noCalibrationStartButton.onClick.RemoveAllListeners();
            noCalibrationStartButton.onClick.AddListener(BeginGameWithoutCalibration);

            TextMeshProUGUI label = noCalibrationStartButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "Start";
            }
        }
    }

    private void ShowStartScreen()
    {
        startScreenRoot?.SetActive(true);
        if (stageText != null)
        {
            stageText.text = "Ready - choose calibration";
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
    }

    private void SetFeedback(string message, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }

        Debug.Log($"[ADUI] Calibration: {message}");
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

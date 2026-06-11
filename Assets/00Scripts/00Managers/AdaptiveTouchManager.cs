using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class AdaptiveTouchManager : MonoBehaviour
{
    private enum AdaptiveAction
    {
        Attack,
        Dodge,
        Heal,
        Whirlwind
    }

    private struct ActionCandidate
    {
        public AdaptiveAction action;
        public string label;
        public Image image;
        public float prior;
        public float likelihood;
        public float posterior;
        public int contextSamples;
        public float contextBlend;
    }

    [Header("Visual Buttons (UI Images)")]
    public Canvas mainCanvas;
    public Image visualAttackButton;
    public Image visualDodgeButton;
    public Image visualHealButton;
    public Image visualWhirlwindButton;

    [Header("Skill Cooldown Labels")]
    public TextMeshProUGUI attackButtonLabel;
    public TextMeshProUGUI dodgeButtonLabel;
    public TextMeshProUGUI healButtonLabel;
    public TextMeshProUGUI whirlwindButtonLabel;

    [Header("Adaptive HUD")]
    public AdaptiveGameHudController gameHud;

    [Header("Button Feedback Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Gaussian Hitbox Visualizers")]
    public RectTransform attackHitboxVisualizer;
    public RectTransform dodgeHitboxVisualizer;
    public RectTransform healHitboxVisualizer;
    public RectTransform whirlwindHitboxVisualizer;

    [Header("Gaussian Model Center Markers")]
    public RectTransform attackModelCenterMarker;
    public RectTransform dodgeModelCenterMarker;
    public RectTransform healModelCenterMarker;
    public RectTransform whirlwindModelCenterMarker;

    [Header("Movement Touch Area")]
    public RectTransform movementJoystickTouchArea;

    [Header("Adaptive Mode Toggle")]
    public bool adaptiveTouchEnabled = true;
    public bool rawButtonOnlyMode;
    public Image adaptiveToggleButton;
    public TextMeshProUGUI adaptiveToggleLabel;

    [Header("Ignored Input Regions")]
    public RectTransform[] ignoredInputRegions;

    [Header("Touch Tuning")]
    [Tooltip("Represents the player's fat-finger spread in screen pixels. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f;

    [Tooltip("Minimum posterior required before a touch is accepted.")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    [Tooltip("Direct button taps only count inside this fraction of the visible circular button. Lower values create more edge-touch misses for the adaptive decoder to resolve.")]
    [Range(0.45f, 1f)]
    public float directHitRadiusScale = 0.66f;

    [Tooltip("Caps only the displayed Gaussian helper circle so it does not cover the combat view. The Bayesian model still uses the learned covariance.")]
    [Range(50f, 240f)]
    public float maxGaussianVisualizerRadius = 96f;

    [Header("Context-Conditioned Gaussian")]
    public bool enableContextConditionedGaussian = true;
    [Range(1f, 12f)]
    public float contextGaussianMatureSamples = 3f;
    [Range(0f, 1f)]
    public float onlineContextGaussianWeight = 0.35f;

    [Header("Decoder Pipeline")]
    public bool autoCreateDecoderPipeline = true;
    public bool enableRuntimeLogging = false;
    public bool collectCalibrationSamples = true;
    public bool enableOnlineTouchAdaptation = true;
    public bool saveCalibrationProfileOnComplete = true;
    public BayesianInputDecoder decoder;
    public SafetyGate safetyGate;
    public UserTouchModel userTouchModel;
    public UserContextPriorModel userContextPriorModel;
    public ExperimentSessionManager sessionManager;
    public ConditionManager conditionManager;
    public TrialScenarioManager trialScenarioManager;
    public RawTouchLogger rawTouchLogger;
    public BayesianDecisionLogger decisionLogger;
    public ButtonLayoutLogger layoutLogger;
    public HPOutcomeLogger hpOutcomeLogger;
    public ModePolicyLogger modePolicyLogger;
    public UserEvaluationLogger evaluationLogger;
    public InteractionDemandModel demandModel;
    public AdaptiveUIPolicyEngine policyEngine;
    public AdaptiveUIAdjustmentController adjustmentController;
    public ADUIFeedbackController feedbackController;
    public PublicPriorConfig publicPriorConfig;

    private Color attackBaseColor;
    private Color dodgeBaseColor;
    private Color healBaseColor;
    private Color whirlwindBaseColor;
    private bool capturedBaseColors;
    private float recentActionRate = 0.25f;
    private float lastTouchTime = -1f;
    private readonly List<Vector2>[] fourButtonCalibrationOffsets = new List<Vector2>[4];
    private readonly List<float>[] fourButtonCalibrationWeights = new List<float>[4];
    private readonly List<Vector2>[] fourButtonCenterCalibrationOffsets = new List<Vector2>[4];
    private readonly List<float>[] fourButtonCenterCalibrationWeights = new List<float>[4];
    private readonly Vector2[] fourButtonMeanOffsets = new Vector2[4];
    private readonly float[] fourButtonSpreads = new float[4];
    private readonly float[] fourButtonCovarianceXX = new float[4];
    private readonly float[] fourButtonCovarianceYY = new float[4];
    private readonly float[] fourButtonCovarianceXY = new float[4];
    private readonly int[] fourButtonSampleCounts = new int[4];
    private readonly List<Vector2>[,] contextCalibrationOffsets = new List<Vector2>[UserContextPriorModel.ScenarioCount, 4];
    private readonly List<float>[,] contextCalibrationWeights = new List<float>[UserContextPriorModel.ScenarioCount, 4];
    private readonly List<Vector2>[,] contextCenterCalibrationOffsets = new List<Vector2>[UserContextPriorModel.ScenarioCount, 4];
    private readonly List<float>[,] contextCenterCalibrationWeights = new List<float>[UserContextPriorModel.ScenarioCount, 4];
    private readonly Vector2[,] contextMeanOffsets = new Vector2[UserContextPriorModel.ScenarioCount, 4];
    private readonly float[,] contextCovarianceXX = new float[UserContextPriorModel.ScenarioCount, 4];
    private readonly float[,] contextCovarianceYY = new float[UserContextPriorModel.ScenarioCount, 4];
    private readonly float[,] contextCovarianceXY = new float[UserContextPriorModel.ScenarioCount, 4];
    private readonly float[,] contextSpreads = new float[UserContextPriorModel.ScenarioCount, 4];
    private readonly int[,] contextSampleCounts = new int[UserContextPriorModel.ScenarioCount, 4];
    private readonly int[] fourButtonValidationCounts = new int[4];
    private readonly int[,] fourButtonValidationConfusion = new int[4, 4];
    private int fourButtonValidationTotal;
    private int fourButtonValidationCorrect;
    private float fourButtonValidationDistanceSum;
    private bool fourButtonCalibrationActive;
    private ADUIInteractionDemand currentDemand = new ADUIInteractionDemand();
    private ADUIAdjustmentPolicy currentPolicy = new ADUIAdjustmentPolicy();
    private ADUIContextScenario currentScenario = ADUIContextScenario.General;
    private string runtimeDebugLine = "decoder idle";
    private string correctionToastMessage = "";
    private Color correctionToastColor = Color.white;
    private float correctionToastUntil;
    private float debugLineHoldUntil;

    public ADUIInteractionDemand CurrentDemand => currentDemand;
    public ADUIAdjustmentPolicy CurrentPolicy => currentPolicy;
    public ADUIContextScenario CurrentScenario => currentScenario;
    public string RuntimeDebugSummary => runtimeDebugLine;
    public string RuntimeModeSummary =>
        currentPolicy != null
            ? $"{currentPolicy.mode} | scenario={UserContextPriorModel.ScenarioLabel(currentScenario)} | errTol={currentPolicy.interactionErrorTolerance:0.00} correction={currentPolicy.correctionStrength:0.00}"
            : "mode unavailable";

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
        EnsureFourButtonCalibrationStorage();
        ResetFourButtonCalibration();
        ResolveDecoderPipeline();
    }

    private void Update()
    {
        CaptureBaseColorsIfNeeded();

        CombatManager combatManager = CombatManager.Instance;
        GetAdjustedActionPriors(
            combatManager,
            out float attackPrior,
            out float dodgePrior,
            out float healPrior,
            out float whirlwindPrior,
            out ADUIContextScenario scenario);

        RefreshRuntimeModel(combatManager, scenario);
        UpdateIdleDebugLine(attackPrior, dodgePrior, healPrior, whirlwindPrior);

        float policyRadiusScale = currentPolicy != null ? Mathf.Max(1f, currentPolicy.hitboxExpansionRatio) : 1f;
        UpdateHitboxVisualizer(attackHitboxVisualizer, CalculateDynamicRadius(Square(GetEffectiveSpread(AdaptiveAction.Attack, scenario)), attackPrior) * policyRadiusScale, GetEffectiveOffset(AdaptiveAction.Attack, scenario));
        UpdateHitboxVisualizer(dodgeHitboxVisualizer, CalculateDynamicRadius(Square(GetEffectiveSpread(AdaptiveAction.Dodge, scenario)), dodgePrior) * policyRadiusScale, GetEffectiveOffset(AdaptiveAction.Dodge, scenario));
        UpdateHitboxVisualizer(healHitboxVisualizer, CalculateDynamicRadius(Square(GetEffectiveSpread(AdaptiveAction.Heal, scenario)), healPrior) * policyRadiusScale, GetEffectiveOffset(AdaptiveAction.Heal, scenario));
        UpdateHitboxVisualizer(whirlwindHitboxVisualizer, CalculateDynamicRadius(Square(GetEffectiveSpread(AdaptiveAction.Whirlwind, scenario)), whirlwindPrior) * policyRadiusScale, GetEffectiveOffset(AdaptiveAction.Whirlwind, scenario));
        UpdateModelCenterMarker(attackModelCenterMarker, AdaptiveAction.Attack);
        UpdateModelCenterMarker(dodgeModelCenterMarker, AdaptiveAction.Dodge);
        UpdateModelCenterMarker(healModelCenterMarker, AdaptiveAction.Heal);
        UpdateModelCenterMarker(whirlwindModelCenterMarker, AdaptiveAction.Whirlwind);
        UpdateSkillCooldownLabels(combatManager);
        UpdateAdaptiveModeVisual();
        UpdateAdaptiveHud(combatManager);

#if UNITY_EDITOR
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                ProcessInputBegan(Mouse.current.position.ReadValue());
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                ProcessInputEnded();
            }
        }
#endif

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                ProcessInputBegan(touch.screenPosition);
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                     touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                ProcessInputEnded();
            }
        }
    }

    private void ProcessInputBegan(Vector2 inputPos)
    {
        CaptureBaseColorsIfNeeded();

        if (fourButtonCalibrationActive)
        {
            return;
        }

        if (IsInMovementJoystickArea(inputPos))
        {
            return;
        }

        if (TryToggleAdaptiveMode(inputPos))
        {
            return;
        }

        RoguelikeGameManager gameManager = RoguelikeGameManager.Instance;
        if (gameManager != null && !gameManager.IsStageRunning)
        {
            return;
        }

        ResolveDecoderPipeline();

        CombatManager combatManager = CombatManager.Instance;
        long touchMs = NowMs();
        int playerHpBefore = CurrentPlayerHp(combatManager);
        int enemyHpBefore = CurrentEnemyHp(combatManager);
        GetAdjustedActionPriors(
            combatManager,
            out float attackPrior,
            out float dodgePrior,
            out float healPrior,
            out float whirlwindPrior,
            out ADUIContextScenario scenario);
        RefreshRuntimeModel(combatManager, scenario);

        if (TryExecuteDirectButton(inputPos, combatManager))
        {
            return;
        }

        if (rawButtonOnlyMode)
        {
            RejectRawButtonMiss(inputPos, combatManager, playerHpBefore, enemyHpBefore, touchMs);
            return;
        }

        if (!adaptiveTouchEnabled)
        {
            return;
        }

        List<ActionCandidate> candidates = new List<ActionCandidate>(4);

        AddCandidate(candidates, AdaptiveAction.Attack, "ATTACK", visualAttackButton, attackPrior, inputPos, scenario);
        AddCandidate(candidates, AdaptiveAction.Dodge, "DODGE", visualDodgeButton, dodgePrior, inputPos, scenario);
        AddCandidate(candidates, AdaptiveAction.Heal, "HEAL", visualHealButton, healPrior, inputPos, scenario);
        AddCandidate(candidates, AdaptiveAction.Whirlwind, "WHIRLWIND", visualWhirlwindButton, whirlwindPrior, inputPos, scenario);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("AdaptiveTouchManager needs at least one visual button image assigned.");
            return;
        }

        ActionCandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].posterior > best.posterior)
            {
                best = candidates[i];
            }
        }

        float threshold = EffectivePosteriorThreshold();
        if (best.posterior < threshold)
        {
            float rejectedGap = PosteriorGap(candidates, best);
            string reason = $"rejected_low_confidence<{threshold:F3}";
            SetDecisionFeedback("Rejected: low confidence", new Color(0.82f, 0.82f, 0.82f, 1f));
            UpdateRuntimeDebugLine(candidates, best, threshold, rejectedGap, reason);
            demandModel?.UpdateRecentErrorSignals(true, rejectedGap < EffectiveAmbiguityGap());
            LogFourButtonDecision(inputPos, candidates, best.action, false, true, reason, playerHpBefore, enemyHpBefore, touchMs, NowMs());
            Debug.Log($"[Adaptive Touch] Rejected. Best posterior {best.posterior:F4} below {threshold:F4}. {FormatCandidatePosteriors(candidates)}");
            return;
        }

        best.image.color = Color.Lerp(GetBaseColor(best.action), pressedColor, 0.55f);
        Debug.Log(
            $"[Adaptive Touch] {best.label} accepted. Scenario={UserContextPriorModel.ScenarioLabel(scenario)}, Prior={best.prior:F2}, Likelihood={best.likelihood:F2}, Posterior={best.posterior:F3}. {FormatCandidatePosteriors(candidates)}");

        RecordActionTouch(best.image, inputPos);
        bool executed = ExecuteAction(best.action, combatManager);
        UpdateRecentActionRate();
        float posteriorGap = PosteriorGap(candidates, best);
        string safetyReason = posteriorGap < EffectiveAmbiguityGap()
            ? "correction_allowed_ambiguous"
            : "correction_allowed";
        SetDecisionFeedback(
            $"Corrected: {best.label}",
            currentPolicy != null && currentPolicy.mode == ADUIInteractionMode.CognitiveFirst
                ? new Color(0.48f, 0.76f, 1f, 1f)
                : new Color(1f, 0.78f, 0.24f, 1f));
        UpdateRuntimeDebugLine(candidates, best, threshold, posteriorGap, safetyReason);
        demandModel?.UpdateRecentErrorSignals(!executed, posteriorGap < EffectiveAmbiguityGap());
        LogFourButtonDecision(inputPos, candidates, best.action, executed, !executed, safetyReason, playerHpBefore, enemyHpBefore, touchMs, NowMs());
        if (executed && best.likelihood >= 0.7f)
        {
            AddOnlineFourButtonCalibrationSample(best.action, inputPos, 0.12f, scenario);
        }

        if (executed && posteriorGap >= 0.04f)
        {
            userContextPriorModel?.RecordOnlineResponse(scenario, best.action.ToString(), Mathf.Clamp01(best.posterior + posteriorGap));
        }
    }

    private void ProcessInputEnded()
    {
        if (fourButtonCalibrationActive)
        {
            return;
        }

        ResetButtonColor(visualAttackButton, attackBaseColor);
        ResetButtonColor(visualDodgeButton, dodgeBaseColor);
        ResetButtonColor(visualHealButton, healBaseColor);
        ResetButtonColor(visualWhirlwindButton, whirlwindBaseColor);
    }

    private void AddCandidate(
        List<ActionCandidate> candidates,
        AdaptiveAction action,
        string label,
        Image image,
        float prior,
        Vector2 inputPos,
        ADUIContextScenario scenario)
    {
        if (image == null)
        {
            return;
        }

        float likelihood = CalculateContextAwareGaussianLikelihood(action, scenario, inputPos, image, out int contextSamples, out float contextBlend);
        float priorExponent = currentPolicy != null
            ? Mathf.Lerp(0.88f, 1.38f, Mathf.Clamp01(currentPolicy.correctionStrength))
            : 1f;
        float policyPrior = Mathf.Pow(Mathf.Clamp(prior, 0.0001f, 1f), priorExponent);
        candidates.Add(new ActionCandidate
        {
            action = action,
            label = label,
            image = image,
            prior = Mathf.Clamp01(policyPrior),
            likelihood = likelihood,
            posterior = likelihood * Mathf.Clamp01(policyPrior),
            contextSamples = contextSamples,
            contextBlend = contextBlend
        });
    }

    private void GetAdjustedActionPriors(
        CombatManager combatManager,
        out float attackPrior,
        out float dodgePrior,
        out float healPrior,
        out float whirlwindPrior,
        out ADUIContextScenario scenario)
    {
        attackPrior = combatManager != null ? combatManager.priorAttack : 0.5f;
        dodgePrior = combatManager != null ? combatManager.priorDodge : 0.5f;
        healPrior = combatManager != null ? combatManager.priorHeal : 0.05f;
        whirlwindPrior = combatManager != null ? combatManager.priorWhirlwind : 0.05f;
        scenario = ADUIContextScenario.General;

        if (userContextPriorModel != null && combatManager != null)
        {
            scenario = userContextPriorModel.Classify(combatManager.CurrentContext, combatManager.playerController);
            userContextPriorModel.ApplyUserPriors(
                scenario,
                ref attackPrior,
                ref dodgePrior,
                ref healPrior,
                ref whirlwindPrior);
        }
    }

    public void ResetFourButtonCalibration()
    {
        EnsureFourButtonCalibrationStorage();

        for (int i = 0; i < fourButtonCalibrationOffsets.Length; i++)
        {
            fourButtonCalibrationOffsets[i].Clear();
            fourButtonCalibrationWeights[i].Clear();
            fourButtonCenterCalibrationOffsets[i].Clear();
            fourButtonCenterCalibrationWeights[i].Clear();
            fourButtonMeanOffsets[i] = Vector2.zero;
            fourButtonSpreads[i] = userTouchVariance;
            SetDefaultFourButtonCovariance(i);
            fourButtonSampleCounts[i] = 0;
            fourButtonValidationCounts[i] = 0;
        }

        ResetContextGaussianProfiles();

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                fourButtonValidationConfusion[row, column] = 0;
            }
        }

        fourButtonValidationTotal = 0;
        fourButtonValidationCorrect = 0;
        fourButtonValidationDistanceSum = 0f;
        ClearFourButtonCalibrationTarget();
    }

    public void SetFourButtonCalibrationActive(bool active)
    {
        fourButtonCalibrationActive = active;
        if (!active)
        {
            ClearFourButtonCalibrationTarget();
        }
    }

    public bool IsTouchNearAction(string actionName, Vector2 screenPosition, float maxDistance)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return false;
        }

        Image image = ImageForAction(action);
        return image != null &&
               Vector2.Distance(screenPosition, image.rectTransform.position) <= Mathf.Max(1f, maxDistance);
    }

    public void AddFourButtonCalibrationSample(string actionName, Vector2 touchPosition, bool affectsCenterBias = true, float sampleWeight = 1f)
    {
        AddFourButtonCalibrationSample(actionName, touchPosition, affectsCenterBias, sampleWeight, ADUIContextScenario.General, false);
    }

    public void AddFourButtonCalibrationSample(
        string actionName,
        Vector2 touchPosition,
        bool affectsCenterBias,
        float sampleWeight,
        ADUIContextScenario scenario,
        bool updateContextProfile)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return;
        }

        Image image = ImageForAction(action);
        if (image == null)
        {
            return;
        }

        EnsureFourButtonCalibrationStorage();

        int index = ActionIndex(action);
        Vector3 buttonPosition = image.rectTransform.position;
        Vector2 buttonCenter = new Vector2(buttonPosition.x, buttonPosition.y);
        Vector2 offset = touchPosition - buttonCenter;
        float weight = Mathf.Clamp(sampleWeight, 0.1f, 4f);
        fourButtonCalibrationOffsets[index].Add(offset);
        fourButtonCalibrationWeights[index].Add(weight);
        if (affectsCenterBias)
        {
            fourButtonCenterCalibrationOffsets[index].Add(offset);
            fourButtonCenterCalibrationWeights[index].Add(weight);
        }

        RecomputeFourButtonProfile(action);

        if (updateContextProfile && enableContextConditionedGaussian)
        {
            AddContextGaussianSample(action, scenario, offset, affectsCenterBias, weight);
        }
    }

    public string RecordFourButtonCalibrationValidation(string actionName, Vector2 touchPosition, string trialType)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction targetAction))
        {
            return "Validation ignored: unknown action.";
        }

        if (!TryPredictFourButtonCalibrationAction(touchPosition, out AdaptiveAction predictedAction, out float confidence))
        {
            return "Validation ignored: no calibrated buttons.";
        }

        Image targetImage = ImageForAction(targetAction);
        float distance = targetImage != null
            ? Vector2.Distance(touchPosition, targetImage.rectTransform.position)
            : 0f;
        int targetIndex = ActionIndex(targetAction);
        int predictedIndex = ActionIndex(predictedAction);
        fourButtonValidationCounts[targetIndex]++;
        fourButtonValidationConfusion[targetIndex, predictedIndex]++;
        fourButtonValidationTotal++;
        fourButtonValidationDistanceSum += distance;

        bool correct = predictedAction == targetAction;
        if (correct)
        {
            fourButtonValidationCorrect++;
        }

        string result = correct ? "OK" : $"MISS->{predictedAction}";
        string validationLabel = string.IsNullOrEmpty(trialType) ||
                                 string.Equals(trialType, "validation", System.StringComparison.OrdinalIgnoreCase)
            ? "pressure"
            : trialType;
        Debug.Log(
            $"[ADUI] Four-button calibration validation {validationLabel}: target={targetAction}, predicted={predictedAction}, confidence={confidence:F2}, distance={distance:F1}");
        return $"{targetAction} validation {result} conf={confidence:F2}";
    }

    public int GetFourButtonCalibrationSampleCount(string actionName)
    {
        return TryParseAdaptiveAction(actionName, out AdaptiveAction action)
            ? fourButtonSampleCounts[ActionIndex(action)]
            : 0;
    }

    public string FourButtonCalibrationSummary =>
        $"{CalibrationSummary(AdaptiveAction.Attack)} | {CalibrationSummary(AdaptiveAction.Dodge)} | " +
        $"{CalibrationSummary(AdaptiveAction.Heal)} | {CalibrationSummary(AdaptiveAction.Whirlwind)}" +
        ValidationSummarySuffix();

    public string GetFourButtonCalibrationModelState(string actionName, string modelEffect)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return "model unavailable";
        }

        int index = ActionIndex(action);
        Vector2 offset = fourButtonMeanOffsets[index];
        string effect = string.IsNullOrEmpty(modelEffect) ? "model" : modelEffect;
        return $"{effect} dx={offset.x:F0} dy={offset.y:F0} sx={Mathf.Sqrt(fourButtonCovarianceXX[index]):F0} sy={Mathf.Sqrt(fourButtonCovarianceYY[index]):F0}";
    }

    public string GetFourButtonContextModelState(string actionName, ADUIContextScenario scenario, string modelEffect)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return "context model unavailable";
        }

        int scenarioIndex = ScenarioIndex(scenario);
        int actionIndex = ActionIndex(action);
        Vector2 offset = contextMeanOffsets[scenarioIndex, actionIndex];
        int count = contextSampleCounts[scenarioIndex, actionIndex];
        float blend = ContextProfileBlend(count);
        string effect = string.IsNullOrEmpty(modelEffect) ? "context model" : modelEffect;
        return $"{effect} {UserContextPriorModel.ScenarioLabel(scenario)} {action} n={count} blend={blend:F2} dx={offset.x:F0} dy={offset.y:F0} sx={Mathf.Sqrt(contextCovarianceXX[scenarioIndex, actionIndex]):F0} sy={Mathf.Sqrt(contextCovarianceYY[scenarioIndex, actionIndex]):F0}";
    }

    public bool TryGetActionButtonCenter(string actionName, out Vector2 center)
    {
        center = Vector2.zero;
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return false;
        }

        Image image = ImageForAction(action);
        if (image == null)
        {
            return false;
        }

        Vector3 position = image.rectTransform.position;
        center = new Vector2(position.x, position.y);
        return true;
    }

    public bool TryResolveFourButtonAction(Vector2 screenPosition, out string actionName, out float confidence, out bool directHit)
    {
        actionName = "";
        confidence = 0f;
        directHit = false;

        for (int i = 0; i < 4; i++)
        {
            AdaptiveAction action = (AdaptiveAction)i;
            Image image = ImageForAction(action);
            if (IsInsideCircularActionButton(image, screenPosition))
            {
                actionName = action.ToString();
                confidence = 1f;
                directHit = true;
                return true;
            }
        }

        if (TryPredictFourButtonCalibrationAction(screenPosition, out AdaptiveAction predictedAction, out confidence))
        {
            actionName = predictedAction.ToString();
            return confidence >= 0.35f;
        }

        return false;
    }

    public bool TryExecuteNamedAction(string actionName, CombatManager combatManager)
    {
        return TryParseAdaptiveAction(actionName, out AdaptiveAction action) &&
               ExecuteAction(action, combatManager);
    }

    public void SetFourButtonCalibrationTarget(string actionName)
    {
        if (!TryParseAdaptiveAction(actionName, out AdaptiveAction action))
        {
            return;
        }

        CaptureBaseColorsIfNeeded();
        ClearFourButtonCalibrationTarget();

        Image image = ImageForAction(action);
        if (image == null)
        {
            return;
        }

        Color targetColor = Color.Lerp(GetBaseColor(action), Color.white, 0.42f);
        targetColor.a = Mathf.Max(0.9f, targetColor.a);
        image.color = targetColor;
    }

    public void ClearFourButtonCalibrationTarget()
    {
        if (!capturedBaseColors)
        {
            return;
        }

        ResetButtonColor(visualAttackButton, attackBaseColor);
        ResetButtonColor(visualDodgeButton, dodgeBaseColor);
        ResetButtonColor(visualHealButton, healBaseColor);
        ResetButtonColor(visualWhirlwindButton, whirlwindBaseColor);
    }

    private bool IsInMovementJoystickArea(Vector2 inputPos)
    {
        return movementJoystickTouchArea != null &&
               RectTransformUtility.RectangleContainsScreenPoint(movementJoystickTouchArea, inputPos, null);
    }

    private bool TryToggleAdaptiveMode(Vector2 inputPos)
    {
        if (adaptiveToggleButton == null ||
            !RectTransformUtility.RectangleContainsScreenPoint(adaptiveToggleButton.rectTransform, inputPos, null))
        {
            return false;
        }

        if (rawButtonOnlyMode)
        {
            SetDecisionFeedback("기본 버튼 조건에서는 자동 보정이 꺼져 있습니다.", new Color(0.82f, 0.82f, 0.82f, 1f));
            return true;
        }

        SetAdaptiveTouchEnabled(!adaptiveTouchEnabled);
        return true;
    }

    public void SetAdaptiveTouchEnabled(bool enabled)
    {
        adaptiveTouchEnabled = enabled;
        UpdateAdaptiveModeVisual();
    }

    public void SetRawButtonOnlyMode(bool enabled)
    {
        rawButtonOnlyMode = enabled;
        if (enabled)
        {
            adaptiveTouchEnabled = false;
        }

        UpdateAdaptiveModeVisual();
    }

    private bool TryExecuteDirectButton(Vector2 inputPos, CombatManager combatManager)
    {
        if (TryExecuteDirectAction(AdaptiveAction.Attack, "ATTACK", visualAttackButton, inputPos, combatManager) ||
            TryExecuteDirectAction(AdaptiveAction.Dodge, "DODGE", visualDodgeButton, inputPos, combatManager) ||
            TryExecuteDirectAction(AdaptiveAction.Heal, "HEAL", visualHealButton, inputPos, combatManager) ||
            TryExecuteDirectAction(AdaptiveAction.Whirlwind, "WHIRLWIND", visualWhirlwindButton, inputPos, combatManager))
        {
            return true;
        }

        return false;
    }

    private bool TryExecuteDirectAction(
        AdaptiveAction action,
        string label,
        Image image,
        Vector2 inputPos,
        CombatManager combatManager)
    {
        if (!IsInsideCircularActionButton(image, inputPos))
        {
            return false;
        }

        long touchMs = NowMs();
        int playerHpBefore = CurrentPlayerHp(combatManager);
        int enemyHpBefore = CurrentEnemyHp(combatManager);
        RecordActionTouch(image, inputPos);

        if (IsSkillCoolingDown(action, combatManager))
        {
            ReportCooldownBlocked(action, combatManager);
            UpdateRecentActionRate();
            demandModel?.UpdateRecentErrorSignals(true, true);
            SetDecisionFeedback("Rejected: cooldown", new Color(0.82f, 0.82f, 0.82f, 1f));
            runtimeDebugLine = $"direct {label} rejected | safety=cooldown_blocked | mode={currentPolicy.mode}";
            debugLineHoldUntil = Time.time + 1.5f;
            LogFourButtonDecision(inputPos, null, action, false, true, "cooldown_blocked", playerHpBefore, enemyHpBefore, touchMs, NowMs());
            return true;
        }

        image.color = Color.Lerp(GetBaseColor(action), pressedColor, 0.55f);
        Debug.Log($"[Adaptive Touch] {label} direct button tap accepted.");
        bool executed = ExecuteAction(action, combatManager);
        UpdateRecentActionRate();
        demandModel?.UpdateRecentErrorSignals(!executed, false);
        string directReason = rawButtonOnlyMode
            ? "raw_direct_button"
            : executed ? "preserve_clear_button" : "action_failed";
        SetDecisionFeedback(
            executed ? (rawButtonOnlyMode ? $"기본 입력: {label}" : $"Preserved: {label}") : $"Rejected: {label}",
            executed ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f));
        runtimeDebugLine = $"direct {label} {(executed ? "preserved" : "rejected")} | safety={directReason} | mode={currentPolicy.mode}";
        debugLineHoldUntil = Time.time + 1.5f;
        LogFourButtonDecision(inputPos, null, action, executed, !executed, directReason, playerHpBefore, enemyHpBefore, touchMs, NowMs());
        if (executed && !rawButtonOnlyMode)
        {
            if (userContextPriorModel != null && combatManager != null)
            {
                ADUIContextScenario scenario = userContextPriorModel.Classify(combatManager.CurrentContext, combatManager.playerController);
                AddOnlineFourButtonCalibrationSample(action, inputPos, 0.2f, scenario);
                userContextPriorModel.RecordOnlineResponse(scenario, action.ToString(), 1f);
            }
            else
            {
                AddOnlineFourButtonCalibrationSample(action, inputPos, 0.2f, ADUIContextScenario.General);
            }
        }

        return true;
    }

    private void RejectRawButtonMiss(
        Vector2 inputPos,
        CombatManager combatManager,
        int playerHpBefore,
        int enemyHpBefore,
        long touchMs)
    {
        AdaptiveAction nearest = NearestAction(inputPos);
        float gap = 0f;
        SetDecisionFeedback("거절: 버튼 밖 터치", new Color(0.82f, 0.82f, 0.82f, 1f));
        runtimeDebugLine = $"raw miss nearest={nearest} | safety=raw_miss_outside_button | mode={currentPolicy.mode}";
        debugLineHoldUntil = Time.time + 1.5f;
        demandModel?.UpdateRecentErrorSignals(true, true);
        LogFourButtonDecision(inputPos, null, nearest, false, true, "raw_miss_outside_button", playerHpBefore, enemyHpBefore, touchMs, NowMs());
        Debug.Log($"[Adaptive Touch] Raw button miss rejected. nearest={nearest}, gap={gap:F2}");
    }

    private AdaptiveAction NearestAction(Vector2 inputPos)
    {
        AdaptiveAction nearest = AdaptiveAction.Attack;
        float bestSqrDistance = float.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            AdaptiveAction action = (AdaptiveAction)i;
            Image image = ImageForAction(action);
            if (image == null)
            {
                continue;
            }

            Vector2 center = image.rectTransform.position;
            float sqrDistance = (inputPos - center).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                nearest = action;
            }
        }

        return nearest;
    }

    private bool IsInsideCircularActionButton(Image image, Vector2 screenPosition)
    {
        if (image == null)
        {
            return false;
        }

        RectTransform rectTransform = image.rectTransform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, null, out Vector2 localPoint))
        {
            Rect rect = rectTransform.rect;
            Vector2 delta = localPoint - rect.center;
            float radiusScale = Mathf.Clamp(directHitRadiusScale, 0.45f, 1f);
            float radiusX = Mathf.Max(1f, rect.width * 0.5f * radiusScale);
            float radiusY = Mathf.Max(1f, rect.height * 0.5f * radiusScale);
            float normalizedDistance =
                (delta.x * delta.x) / (radiusX * radiusX) +
                (delta.y * delta.y) / (radiusY * radiusY);
            return normalizedDistance <= 1f;
        }

        float fallbackRadius = Mathf.Max(
            1f,
            Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f * Mathf.Clamp(directHitRadiusScale, 0.45f, 1f));
        return Vector2.Distance(screenPosition, rectTransform.position) <= fallbackRadius;
    }

    private void AddOnlineFourButtonCalibrationSample(
        AdaptiveAction action,
        Vector2 inputPos,
        float sampleWeight,
        ADUIContextScenario scenario)
    {
        int index = ActionIndex(action);
        if (!enableOnlineTouchAdaptation || fourButtonSampleCounts[index] <= 0)
        {
            return;
        }

        AddFourButtonCalibrationSample(
            action.ToString(),
            inputPos,
            false,
            sampleWeight,
            scenario,
            true);
    }

    private bool IsSkillCoolingDown(AdaptiveAction action, CombatManager combatManager)
    {
        PlayerController player = combatManager != null ? combatManager.playerController : null;
        if (player == null)
        {
            return false;
        }

        return (action == AdaptiveAction.Heal && player.HealCooldownRemaining > 0f) ||
               (action == AdaptiveAction.Whirlwind && player.WhirlwindCooldownRemaining > 0f);
    }

    private void ReportCooldownBlocked(AdaptiveAction action, CombatManager combatManager)
    {
        PlayerController player = combatManager != null ? combatManager.playerController : null;
        float cooldown = 0f;
        string label = action.ToString();

        if (player != null && action == AdaptiveAction.Heal)
        {
            cooldown = player.HealCooldownRemaining;
        }
        else if (player != null && action == AdaptiveAction.Whirlwind)
        {
            cooldown = player.WhirlwindCooldownRemaining;
        }

        string message = $"{label} cooldown: {cooldown:F1}s.";
        combatManager?.ReportFeedback(message, Color.gray);
        Debug.Log($"[Adaptive Touch] {message}");
    }

    private void RecordActionTouch(Image image, Vector2 inputPos)
    {
        if (image == null || RoguelikeGameManager.Instance == null)
        {
            return;
        }

        float distance = Vector2.Distance(inputPos, image.rectTransform.position);
        RoguelikeGameManager.Instance.RecordButtonPress(distance);
    }

    private bool ExecuteAction(AdaptiveAction action, CombatManager combatManager)
    {
        if (combatManager == null)
        {
            Debug.LogWarning("AdaptiveTouchManager accepted an action, but no CombatManager is available.");
            return false;
        }

        switch (action)
        {
            case AdaptiveAction.Attack:
                return combatManager.OnPlayerAttack();
            case AdaptiveAction.Dodge:
                return combatManager.OnPlayerDodge();
            case AdaptiveAction.Heal:
                return combatManager.OnPlayerHeal();
            case AdaptiveAction.Whirlwind:
                return combatManager.OnPlayerWhirlwind();
        }

        return false;
    }

    private string FormatCandidatePosteriors(List<ActionCandidate> candidates)
    {
        string result = "Scores:";
        for (int i = 0; i < candidates.Count; i++)
        {
            result +=
                $" {candidates[i].label}[L={candidates[i].likelihood:F2},P={candidates[i].prior:F2},Post={candidates[i].posterior:F3},ctxN={candidates[i].contextSamples},ctxB={candidates[i].contextBlend:F2}]";
        }

        return result;
    }

    private float PosteriorGap(List<ActionCandidate> candidates, ActionCandidate best)
    {
        float runnerUp = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].label == best.label)
            {
                continue;
            }

            runnerUp = Mathf.Max(runnerUp, candidates[i].posterior);
        }

        return Mathf.Max(0f, best.posterior - runnerUp);
    }

    private void CaptureBaseColorsIfNeeded()
    {
        if (capturedBaseColors)
        {
            return;
        }

        attackBaseColor = visualAttackButton != null ? visualAttackButton.color : normalColor;
        dodgeBaseColor = visualDodgeButton != null ? visualDodgeButton.color : normalColor;
        healBaseColor = visualHealButton != null ? visualHealButton.color : normalColor;
        whirlwindBaseColor = visualWhirlwindButton != null ? visualWhirlwindButton.color : normalColor;
        capturedBaseColors = true;
    }

    private Color GetBaseColor(AdaptiveAction action)
    {
        switch (action)
        {
            case AdaptiveAction.Dodge:
                return dodgeBaseColor;
            case AdaptiveAction.Heal:
                return healBaseColor;
            case AdaptiveAction.Whirlwind:
                return whirlwindBaseColor;
            default:
                return attackBaseColor;
        }
    }

    private void ResetButtonColor(Image image, Color color)
    {
        if (image != null)
        {
            image.color = color;
        }
    }

    private void UpdateHitboxVisualizer(RectTransform visualizer, float screenRadius, Vector2 calibratedOffset)
    {
        if (visualizer == null)
        {
            return;
        }

        if (!adaptiveTouchEnabled)
        {
            if (visualizer.gameObject.activeSelf)
            {
                visualizer.gameObject.SetActive(false);
            }

            return;
        }

        if (!visualizer.gameObject.activeSelf)
        {
            visualizer.gameObject.SetActive(true);
        }

        float scaleFactor = mainCanvas != null ? Mathf.Max(0.01f, mainCanvas.scaleFactor) : 1f;
        float displayRadius = Mathf.Min(screenRadius, Mathf.Max(20f, maxGaussianVisualizerRadius));
        float uiSize = (displayRadius * 2f) / scaleFactor;
        visualizer.anchoredPosition = calibratedOffset / scaleFactor;
        visualizer.sizeDelta = new Vector2(uiSize, uiSize);

        Image visualizerImage = visualizer.GetComponent<Image>();
        if (visualizerImage != null)
        {
            Color color = visualizerImage.color;
            color.a = Mathf.Min(color.a, 0.1f);
            visualizerImage.color = color;
        }
    }

    private void UpdateModelCenterMarker(RectTransform marker, AdaptiveAction action)
    {
        if (marker == null)
        {
            return;
        }

        int index = ActionIndex(action);
        bool shouldShow = adaptiveTouchEnabled && fourButtonSampleCounts[index] > 0;
        if (marker.gameObject.activeSelf != shouldShow)
        {
            marker.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        float scaleFactor = mainCanvas != null ? Mathf.Max(0.01f, mainCanvas.scaleFactor) : 1f;
        marker.anchoredPosition = GetCalibrationOffset(action) / scaleFactor;
    }

    private void EnsureFourButtonCalibrationStorage()
    {
        for (int i = 0; i < fourButtonCalibrationOffsets.Length; i++)
        {
            if (fourButtonCalibrationOffsets[i] == null)
            {
                fourButtonCalibrationOffsets[i] = new List<Vector2>();
            }

            if (fourButtonCalibrationWeights[i] == null)
            {
                fourButtonCalibrationWeights[i] = new List<float>();
            }

            if (fourButtonCenterCalibrationOffsets[i] == null)
            {
                fourButtonCenterCalibrationOffsets[i] = new List<Vector2>();
            }

            if (fourButtonCenterCalibrationWeights[i] == null)
            {
                fourButtonCenterCalibrationWeights[i] = new List<float>();
            }
        }

        for (int scenario = 0; scenario < UserContextPriorModel.ScenarioCount; scenario++)
        {
            for (int action = 0; action < 4; action++)
            {
                if (contextCalibrationOffsets[scenario, action] == null)
                {
                    contextCalibrationOffsets[scenario, action] = new List<Vector2>();
                }

                if (contextCalibrationWeights[scenario, action] == null)
                {
                    contextCalibrationWeights[scenario, action] = new List<float>();
                }

                if (contextCenterCalibrationOffsets[scenario, action] == null)
                {
                    contextCenterCalibrationOffsets[scenario, action] = new List<Vector2>();
                }

                if (contextCenterCalibrationWeights[scenario, action] == null)
                {
                    contextCenterCalibrationWeights[scenario, action] = new List<float>();
                }
            }
        }
    }

    private bool TryParseAdaptiveAction(string value, out AdaptiveAction action)
    {
        action = AdaptiveAction.Attack;

        if (string.Equals(value, "Attack", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "ATTACK", System.StringComparison.OrdinalIgnoreCase))
        {
            action = AdaptiveAction.Attack;
            return true;
        }

        if (string.Equals(value, "Dodge", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "DODGE", System.StringComparison.OrdinalIgnoreCase))
        {
            action = AdaptiveAction.Dodge;
            return true;
        }

        if (string.Equals(value, "Heal", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "HEAL", System.StringComparison.OrdinalIgnoreCase))
        {
            action = AdaptiveAction.Heal;
            return true;
        }

        if (string.Equals(value, "Whirlwind", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "WHIRLWIND", System.StringComparison.OrdinalIgnoreCase))
        {
            action = AdaptiveAction.Whirlwind;
            return true;
        }

        return false;
    }

    private Image ImageForAction(AdaptiveAction action)
    {
        switch (action)
        {
            case AdaptiveAction.Dodge:
                return visualDodgeButton;
            case AdaptiveAction.Heal:
                return visualHealButton;
            case AdaptiveAction.Whirlwind:
                return visualWhirlwindButton;
            default:
                return visualAttackButton;
        }
    }

    private int ActionIndex(AdaptiveAction action)
    {
        return (int)action;
    }

    private Vector2 GetCalibrationOffset(AdaptiveAction action)
    {
        int index = ActionIndex(action);
        return fourButtonSampleCounts[index] > 0 ? fourButtonMeanOffsets[index] : Vector2.zero;
    }

    private Vector2 GetEffectiveOffset(AdaptiveAction action, ADUIContextScenario scenario)
    {
        Vector2 globalOffset = GetCalibrationOffset(action);
        if (!enableContextConditionedGaussian)
        {
            return globalOffset;
        }

        int scenarioIndex = ScenarioIndex(scenario);
        int actionIndex = ActionIndex(action);
        int count = contextSampleCounts[scenarioIndex, actionIndex];
        if (count <= 0)
        {
            return globalOffset;
        }

        float blend = ContextProfileBlend(count);
        return Vector2.Lerp(globalOffset, contextMeanOffsets[scenarioIndex, actionIndex], blend);
    }

    private Vector2 GetCalibratedCenter(AdaptiveAction action, Image image)
    {
        Vector3 position = image.rectTransform.position;
        Vector2 center = new Vector2(position.x, position.y);
        return center + GetCalibrationOffset(action);
    }

    private Vector2 GetContextCalibratedCenter(AdaptiveAction action, ADUIContextScenario scenario, Image image)
    {
        Vector3 position = image.rectTransform.position;
        Vector2 center = new Vector2(position.x, position.y);
        return center + GetEffectiveOffset(action, scenario);
    }

    private float GetCalibratedSpread(AdaptiveAction action)
    {
        int index = ActionIndex(action);
        return fourButtonSampleCounts[index] > 0
            ? Mathf.Max(1f, fourButtonSpreads[index])
            : Mathf.Max(1f, userTouchVariance);
    }

    private float GetEffectiveSpread(AdaptiveAction action, ADUIContextScenario scenario)
    {
        float globalSpread = GetCalibratedSpread(action);
        if (!enableContextConditionedGaussian)
        {
            return globalSpread;
        }

        int scenarioIndex = ScenarioIndex(scenario);
        int actionIndex = ActionIndex(action);
        int count = contextSampleCounts[scenarioIndex, actionIndex];
        if (count <= 0)
        {
            return globalSpread;
        }

        float blend = ContextProfileBlend(count);
        float contextSpread = Mathf.Max(1f, contextSpreads[scenarioIndex, actionIndex]);
        return Mathf.Lerp(globalSpread, contextSpread, blend);
    }

    private void RecomputeFourButtonProfile(AdaptiveAction action)
    {
        int index = ActionIndex(action);
        List<Vector2> spreadSamples = fourButtonCalibrationOffsets[index];
        List<float> spreadWeights = fourButtonCalibrationWeights[index];
        List<Vector2> centerSamples = fourButtonCenterCalibrationOffsets[index];
        List<float> centerWeights = fourButtonCenterCalibrationWeights[index];
        fourButtonSampleCounts[index] = spreadSamples.Count;

        Vector2 mean = centerSamples.Count > 0
            ? WeightedMean(centerSamples, centerWeights)
            : WeightedMean(spreadSamples, spreadWeights);
        fourButtonMeanOffsets[index] = ClampOffset(mean);

        if (spreadSamples.Count <= 1)
        {
            SetConservativeFourButtonCovariance(index, userTouchVariance * 0.75f);
            return;
        }

        float weightSum = 0f;
        float observedXX = 0f;
        float observedYY = 0f;
        float observedXY = 0f;
        for (int i = 0; i < spreadSamples.Count; i++)
        {
            float weight = i < spreadWeights.Count ? Mathf.Clamp(spreadWeights[i], 0.1f, 4f) : 1f;
            Vector2 delta = spreadSamples[i] - mean;
            observedXX += weight * delta.x * delta.x;
            observedYY += weight * delta.y * delta.y;
            observedXY += weight * delta.x * delta.y;
            weightSum += weight;
        }

        float denominator = Mathf.Max(1f, weightSum - 1f);
        observedXX /= denominator;
        observedYY /= denominator;
        observedXY /= denominator;

        float priorStd = Mathf.Max(1f, userTouchVariance * 0.65f);
        float priorVariance = priorStd * priorStd;
        float shrink = Mathf.Clamp01(weightSum / (weightSum + 6f));
        float paddingVariance = 28f * 28f;
        float xx = Mathf.Lerp(priorVariance, observedXX, shrink) + paddingVariance;
        float yy = Mathf.Lerp(priorVariance, observedYY, shrink) + paddingVariance;
        float xy = Mathf.Lerp(0f, observedXY, shrink);

        SetFourButtonCovariance(index, xx, yy, xy);
    }

    private void AddContextGaussianSample(
        AdaptiveAction action,
        ADUIContextScenario scenario,
        Vector2 offset,
        bool affectsCenterBias,
        float weight)
    {
        int scenarioIndex = ScenarioIndex(scenario);
        int actionIndex = ActionIndex(action);
        contextCalibrationOffsets[scenarioIndex, actionIndex].Add(offset);
        contextCalibrationWeights[scenarioIndex, actionIndex].Add(weight);

        // Context profiles intentionally learn both spread and directional bias; this is
        // the P(touch | action, context) term that differs from the global fallback.
        contextCenterCalibrationOffsets[scenarioIndex, actionIndex].Add(offset);
        contextCenterCalibrationWeights[scenarioIndex, actionIndex].Add(weight);

        RecomputeContextGaussianProfile(scenarioIndex, actionIndex);
    }

    private void RecomputeContextGaussianProfile(int scenarioIndex, int actionIndex)
    {
        List<Vector2> spreadSamples = contextCalibrationOffsets[scenarioIndex, actionIndex];
        List<float> spreadWeights = contextCalibrationWeights[scenarioIndex, actionIndex];
        List<Vector2> centerSamples = contextCenterCalibrationOffsets[scenarioIndex, actionIndex];
        List<float> centerWeights = contextCenterCalibrationWeights[scenarioIndex, actionIndex];
        contextSampleCounts[scenarioIndex, actionIndex] = spreadSamples.Count;

        Vector2 mean = centerSamples.Count > 0
            ? WeightedMean(centerSamples, centerWeights)
            : WeightedMean(spreadSamples, spreadWeights);
        contextMeanOffsets[scenarioIndex, actionIndex] = ClampOffset(mean);

        if (spreadSamples.Count <= 1)
        {
            SetContextCovariance(scenarioIndex, actionIndex, userTouchVariance * userTouchVariance, userTouchVariance * userTouchVariance, 0f);
            return;
        }

        float weightSum = 0f;
        float observedXX = 0f;
        float observedYY = 0f;
        float observedXY = 0f;
        for (int i = 0; i < spreadSamples.Count; i++)
        {
            float weight = i < spreadWeights.Count ? Mathf.Clamp(spreadWeights[i], 0.1f, 4f) : 1f;
            Vector2 delta = spreadSamples[i] - mean;
            observedXX += weight * delta.x * delta.x;
            observedYY += weight * delta.y * delta.y;
            observedXY += weight * delta.x * delta.y;
            weightSum += weight;
        }

        float denominator = Mathf.Max(1f, weightSum - 1f);
        observedXX /= denominator;
        observedYY /= denominator;
        observedXY /= denominator;

        float priorStd = Mathf.Max(1f, userTouchVariance * 0.85f);
        float priorVariance = priorStd * priorStd;
        float shrink = Mathf.Clamp01(weightSum / (weightSum + 3.5f));
        float paddingVariance = 22f * 22f;
        float xx = Mathf.Lerp(priorVariance, observedXX, shrink) + paddingVariance;
        float yy = Mathf.Lerp(priorVariance, observedYY, shrink) + paddingVariance;
        float xy = Mathf.Lerp(0f, observedXY, shrink);

        SetContextCovariance(scenarioIndex, actionIndex, xx, yy, xy);
    }

    private void SetContextCovariance(int scenarioIndex, int actionIndex, float xx, float yy, float xy)
    {
        float minStd = 48f;
        float maxStd = Mathf.Max(minStd, userTouchVariance * 2.15f);
        float minVariance = minStd * minStd;
        float maxVariance = maxStd * maxStd;

        xx = Mathf.Clamp(xx, minVariance, maxVariance);
        yy = Mathf.Clamp(yy, minVariance, maxVariance);

        float maxAbsXY = Mathf.Sqrt(xx * yy) * 0.72f;
        xy = Mathf.Clamp(xy, -maxAbsXY, maxAbsXY);

        if (xx * yy - xy * xy < minVariance * minVariance * 0.25f)
        {
            xy = 0f;
        }

        contextCovarianceXX[scenarioIndex, actionIndex] = xx;
        contextCovarianceYY[scenarioIndex, actionIndex] = yy;
        contextCovarianceXY[scenarioIndex, actionIndex] = xy;

        float trace = xx + yy;
        float discriminant = Mathf.Sqrt(Mathf.Max(0f, (xx - yy) * (xx - yy) + 4f * xy * xy));
        float maxEigenvalue = Mathf.Max(minVariance, (trace + discriminant) * 0.5f);
        contextSpreads[scenarioIndex, actionIndex] = Mathf.Sqrt(maxEigenvalue);
    }

    private void ResetContextGaussianProfiles()
    {
        for (int scenario = 0; scenario < UserContextPriorModel.ScenarioCount; scenario++)
        {
            for (int action = 0; action < 4; action++)
            {
                contextCalibrationOffsets[scenario, action].Clear();
                contextCalibrationWeights[scenario, action].Clear();
                contextCenterCalibrationOffsets[scenario, action].Clear();
                contextCenterCalibrationWeights[scenario, action].Clear();
                contextMeanOffsets[scenario, action] = Vector2.zero;
                contextSampleCounts[scenario, action] = 0;
                SetContextCovariance(scenario, action, userTouchVariance * userTouchVariance, userTouchVariance * userTouchVariance, 0f);
            }
        }
    }

    private int ScenarioIndex(ADUIContextScenario scenario)
    {
        return Mathf.Clamp((int)scenario, 0, UserContextPriorModel.ScenarioCount - 1);
    }

    private float ContextProfileBlend(int sampleCount)
    {
        return Mathf.Clamp01(sampleCount / Mathf.Max(0.001f, sampleCount + contextGaussianMatureSamples));
    }

    private Vector2 WeightedMean(List<Vector2> samples, List<float> weights)
    {
        if (samples == null || samples.Count == 0)
        {
            return Vector2.zero;
        }

        Vector2 sum = Vector2.zero;
        float weightSum = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            float weight = weights != null && i < weights.Count ? Mathf.Clamp(weights[i], 0.1f, 4f) : 1f;
            sum += samples[i] * weight;
            weightSum += weight;
        }

        return weightSum > 0f ? sum / weightSum : Vector2.zero;
    }

    private Vector2 ClampOffset(Vector2 offset)
    {
        float maxOffset = Mathf.Max(24f, userTouchVariance * 0.6f);
        return offset.magnitude > maxOffset ? offset.normalized * maxOffset : offset;
    }

    private string CalibrationSummary(AdaptiveAction action)
    {
        int index = ActionIndex(action);
        Vector2 offset = fourButtonMeanOffsets[index];
        return $"{action} n={fourButtonSampleCounts[index]} dx={offset.x:F0} dy={offset.y:F0} sx={Mathf.Sqrt(fourButtonCovarianceXX[index]):F0} sy={Mathf.Sqrt(fourButtonCovarianceYY[index]):F0}";
    }

    private string ValidationSummarySuffix()
    {
        if (fourButtonValidationTotal <= 0)
        {
            return string.Empty;
        }

        float accuracy = (float)fourButtonValidationCorrect / Mathf.Max(1, fourButtonValidationTotal);
        float meanDistance = fourButtonValidationDistanceSum / Mathf.Max(1, fourButtonValidationTotal);
        return $" | val={fourButtonValidationCorrect}/{fourButtonValidationTotal} ({accuracy:P0}) d={meanDistance:F0}";
    }

    private float Square(float value)
    {
        return value * value;
    }

    private void SetDefaultFourButtonCovariance(int index)
    {
        float std = Mathf.Max(1f, userTouchVariance);
        SetFourButtonCovariance(index, std * std, std * std, 0f);
    }

    private void SetConservativeFourButtonCovariance(int index, float std)
    {
        float clampedStd = Mathf.Clamp(std, 60f, Mathf.Max(60f, userTouchVariance * 1.9f));
        float variance = clampedStd * clampedStd;
        SetFourButtonCovariance(index, variance, variance, 0f);
    }

    private void SetFourButtonCovariance(int index, float xx, float yy, float xy)
    {
        float minStd = 58f;
        float maxStd = Mathf.Max(minStd, userTouchVariance * 1.9f);
        float minVariance = minStd * minStd;
        float maxVariance = maxStd * maxStd;

        xx = Mathf.Clamp(xx, minVariance, maxVariance);
        yy = Mathf.Clamp(yy, minVariance, maxVariance);

        float maxAbsXY = Mathf.Sqrt(xx * yy) * 0.65f;
        xy = Mathf.Clamp(xy, -maxAbsXY, maxAbsXY);

        if (xx * yy - xy * xy < minVariance * minVariance * 0.25f)
        {
            xy = 0f;
        }

        fourButtonCovarianceXX[index] = xx;
        fourButtonCovarianceYY[index] = yy;
        fourButtonCovarianceXY[index] = xy;

        float trace = xx + yy;
        float discriminant = Mathf.Sqrt(Mathf.Max(0f, (xx - yy) * (xx - yy) + 4f * xy * xy));
        float maxEigenvalue = Mathf.Max(minVariance, (trace + discriminant) * 0.5f);
        fourButtonSpreads[index] = Mathf.Sqrt(maxEigenvalue);
    }

    private bool TryPredictFourButtonCalibrationAction(Vector2 inputPos, out AdaptiveAction predictedAction, out float confidence)
    {
        predictedAction = AdaptiveAction.Attack;
        confidence = 0f;
        float bestLikelihood = -1f;
        float totalLikelihood = 0f;

        for (int i = 0; i < 4; i++)
        {
            AdaptiveAction action = (AdaptiveAction)i;
            Image image = ImageForAction(action);
            if (image == null)
            {
                continue;
            }

            float likelihood = CalculateCalibratedGaussianLikelihood(action, inputPos, image);
            totalLikelihood += likelihood;
            if (likelihood > bestLikelihood)
            {
                bestLikelihood = likelihood;
                predictedAction = action;
            }
        }

        if (bestLikelihood < 0f)
        {
            return false;
        }

        confidence = totalLikelihood > 0f ? bestLikelihood / totalLikelihood : 0f;
        return true;
    }

    private void UpdateAdaptiveModeVisual()
    {
        if (adaptiveToggleLabel != null)
        {
            adaptiveToggleLabel.text = rawButtonOnlyMode
                ? "Mode: RAW"
                : adaptiveTouchEnabled ? "Adaptive: ON" : "Adaptive: OFF";
        }

        if (adaptiveToggleButton != null)
        {
            adaptiveToggleButton.color = rawButtonOnlyMode
                ? new Color(0.32f, 0.34f, 0.36f, 0.92f)
                : adaptiveTouchEnabled
                ? new Color(0.18f, 0.72f, 0.44f, 0.92f)
                : new Color(0.32f, 0.34f, 0.36f, 0.92f);
        }
    }

    private void UpdateSkillCooldownLabels(CombatManager combatManager)
    {
        PlayerController player = combatManager != null ? combatManager.playerController : null;

        if (attackButtonLabel != null)
        {
            bool attackWindow = currentScenario == ADUIContextScenario.AttackCommitWindow ||
                                (combatManager != null && combatManager.CurrentContext.attackCommitTargets > 0);
            attackButtonLabel.text = attackWindow ? "Attack\nOPEN" : "Attack\nReady";
            attackButtonLabel.fontSize = 17f;
        }

        if (dodgeButtonLabel != null)
        {
            bool urgentDodge = currentDemand != null && currentDemand.temporalUrgency >= 0.55f;
            dodgeButtonLabel.text = urgentDodge ? "Dodge\nNOW" : "Dodge\nReady";
            dodgeButtonLabel.fontSize = 17f;
        }

        if (healButtonLabel != null)
        {
            healButtonLabel.text = player != null && player.HealCooldownRemaining > 0f
                ? $"Heal\n{player.HealCooldownRemaining:F1}s"
                : "Heal\nReady";
            healButtonLabel.fontSize = 17f;
        }

        if (whirlwindButtonLabel != null)
        {
            whirlwindButtonLabel.text = player != null && player.WhirlwindCooldownRemaining > 0f
                ? $"Whirl\n{player.WhirlwindCooldownRemaining:F1}s"
                : "Whirl\nReady";
            whirlwindButtonLabel.fontSize = 17f;
        }
    }

    private float CalculateCalibratedGaussianLikelihood(AdaptiveAction action, Vector2 inputPos, Image image)
    {
        if (image == null)
        {
            return 0f;
        }

        int index = ActionIndex(action);
        Vector2 center = GetCalibratedCenter(action, image);
        return CalculateGaussianLikelihood(
            inputPos,
            center,
            fourButtonCovarianceXX[index],
            fourButtonCovarianceYY[index],
            fourButtonCovarianceXY[index]);
    }

    private float CalculateContextAwareGaussianLikelihood(
        AdaptiveAction action,
        ADUIContextScenario scenario,
        Vector2 inputPos,
        Image image,
        out int contextSamples,
        out float contextBlend)
    {
        contextSamples = 0;
        contextBlend = 0f;
        float globalLikelihood = CalculateCalibratedGaussianLikelihood(action, inputPos, image);

        if (!enableContextConditionedGaussian || image == null)
        {
            return globalLikelihood;
        }

        int scenarioIndex = ScenarioIndex(scenario);
        int actionIndex = ActionIndex(action);
        contextSamples = contextSampleCounts[scenarioIndex, actionIndex];
        if (contextSamples <= 0)
        {
            return globalLikelihood;
        }

        contextBlend = ContextProfileBlend(contextSamples);
        if (currentPolicy != null)
        {
            float blendScale = Mathf.Lerp(0.62f, 1.45f, Mathf.Clamp01(currentPolicy.interactionErrorTolerance));
            contextBlend = Mathf.Clamp01(contextBlend * blendScale);
        }
        Vector2 contextCenter = GetContextCalibratedCenter(action, scenario, image);
        float contextLikelihood = CalculateGaussianLikelihood(
            inputPos,
            contextCenter,
            contextCovarianceXX[scenarioIndex, actionIndex],
            contextCovarianceYY[scenarioIndex, actionIndex],
            contextCovarianceXY[scenarioIndex, actionIndex]);

        return Mathf.Lerp(globalLikelihood, contextLikelihood, contextBlend);
    }

    private float CalculateGaussianLikelihood(Vector2 inputPos, Vector2 center, float covarianceXX, float covarianceYY, float covarianceXY)
    {
        Vector2 delta = inputPos - center;
        float xx = Mathf.Max(1f, covarianceXX);
        float yy = Mathf.Max(1f, covarianceYY);
        float xy = covarianceXY;
        float determinant = xx * yy - xy * xy;
        if (determinant <= 1f)
        {
            float std = Mathf.Max(1f, userTouchVariance);
            float distance = delta.magnitude;
            return Mathf.Exp(-(distance * distance) / (2f * std * std));
        }

        float mahalanobisSquared = (yy * delta.x * delta.x - 2f * xy * delta.x * delta.y + xx * delta.y * delta.y) / determinant;
        mahalanobisSquared = Mathf.Clamp(mahalanobisSquared, 0f, 80f);

        float likelihood = Mathf.Exp(-0.5f * mahalanobisSquared);
        float baselineVariance = Mathf.Max(1f, userTouchVariance * userTouchVariance);
        float peakPenalty = Mathf.Clamp(baselineVariance / Mathf.Sqrt(determinant), 0.35f, 1f);
        return Mathf.Clamp01(likelihood * peakPenalty);
    }

    private void ExecuteDecodedAction(ADUIAction action, CombatManager combatManager)
    {
        if (combatManager == null)
        {
            return;
        }

        if (action == ADUIAction.Attack)
        {
            combatManager.OnPlayerAttack();
        }
        else if (action == ADUIAction.Dodge)
        {
            combatManager.OnPlayerDodge();
        }
    }

    private void HighlightExecutedButton(ADUIAction action)
    {
        if (action == ADUIAction.Attack && visualAttackButton != null)
        {
            visualAttackButton.color = pressedColor;
        }
        else if (action == ADUIAction.Dodge && visualDodgeButton != null)
        {
            visualDodgeButton.color = pressedColor;
        }
    }

    private float CalculateDynamicRadius(float variance, float prior)
    {
        if (decoder != null)
        {
            return decoder.DynamicRadius(variance, prior);
        }

        return DynamicRadius(variance, prior);
    }

    private bool IsInsideIgnoredInputRegion(Vector2 inputPos)
    {
        if (ignoredInputRegions == null || ignoredInputRegions.Length == 0)
        {
            return false;
        }

        Camera eventCamera = null;
        if (mainCanvas != null && mainCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = mainCanvas.worldCamera;
        }

        foreach (RectTransform region in ignoredInputRegions)
        {
            if (region == null)
            {
                continue;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(region, inputPos, eventCamera, out Vector2 localPoint) &&
                region.rect.Contains(localPoint))
            {
                return true;
            }
        }

        return false;
    }

    private float DynamicRadius(float variance, float prior)
    {
        float std = Mathf.Sqrt(Mathf.Max(variance, 1f));
        float safePrior = Mathf.Clamp(prior, 0.01f, 0.99f);
        float thresholdRatio = (Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f)) / safePrior;
        thresholdRatio = Mathf.Clamp(thresholdRatio, 0.0001f, 0.999f);
        return std * Mathf.Sqrt(-2f * Mathf.Log(thresholdRatio));
    }

    private void ResolveDecoderPipeline()
    {
        if (decoder == null)
        {
            decoder = GetComponent<BayesianInputDecoder>();
            if (decoder == null)
            {
                decoder = FindAnyObjectByType<BayesianInputDecoder>();
            }
            if (decoder == null && autoCreateDecoderPipeline)
            {
                decoder = gameObject.AddComponent<BayesianInputDecoder>();
            }
        }

        if (safetyGate == null)
        {
            safetyGate = GetComponent<SafetyGate>();
            if (safetyGate == null)
            {
                safetyGate = FindAnyObjectByType<SafetyGate>();
            }
            if (safetyGate == null && autoCreateDecoderPipeline)
            {
                safetyGate = gameObject.AddComponent<SafetyGate>();
            }
        }

        if (userTouchModel == null)
        {
            userTouchModel = GetComponent<UserTouchModel>();
            if (userTouchModel == null)
            {
                userTouchModel = FindAnyObjectByType<UserTouchModel>();
            }
            if (userTouchModel == null && autoCreateDecoderPipeline)
            {
                userTouchModel = gameObject.AddComponent<UserTouchModel>();
            }
        }

        if (userTouchModel != null)
        {
            userTouchModel.ConfigureFromPublicDefault(userTouchVariance * userTouchVariance);
        }

        if (decoder != null)
        {
            decoder.userTouchModel = userTouchModel;
            decoder.minLikelihoodThreshold = Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f);
        }

        ResolveExperimentComponents();
        ResolveAdaptationComponents();
    }

    private void ResolveExperimentComponents()
    {
        if (!enableRuntimeLogging && !autoCreateDecoderPipeline)
        {
            return;
        }

        if (sessionManager == null)
        {
            sessionManager = GetComponent<ExperimentSessionManager>();
            if (sessionManager == null)
            {
                sessionManager = FindAnyObjectByType<ExperimentSessionManager>();
            }
            if (sessionManager == null && autoCreateDecoderPipeline)
            {
                sessionManager = gameObject.AddComponent<ExperimentSessionManager>();
            }
        }

        if (conditionManager == null)
        {
            conditionManager = GetComponent<ConditionManager>();
            if (conditionManager == null)
            {
                conditionManager = FindAnyObjectByType<ConditionManager>();
            }
            if (conditionManager == null && autoCreateDecoderPipeline)
            {
                conditionManager = gameObject.AddComponent<ConditionManager>();
            }
        }

        if (trialScenarioManager == null)
        {
            trialScenarioManager = GetComponent<TrialScenarioManager>();
            if (trialScenarioManager == null)
            {
                trialScenarioManager = FindAnyObjectByType<TrialScenarioManager>();
            }
            if (trialScenarioManager == null && autoCreateDecoderPipeline)
            {
                trialScenarioManager = gameObject.AddComponent<TrialScenarioManager>();
            }
        }

        if (trialScenarioManager != null)
        {
            trialScenarioManager.sessionManager = sessionManager;
            trialScenarioManager.conditionManager = conditionManager;
        }

        rawTouchLogger = ResolveLogger(rawTouchLogger);
        decisionLogger = ResolveLogger(decisionLogger);
        layoutLogger = ResolveLogger(layoutLogger);
        hpOutcomeLogger = ResolveLogger(hpOutcomeLogger);
        modePolicyLogger = ResolveLogger(modePolicyLogger);
    }

    private T ResolveLogger<T>(T logger) where T : MonoBehaviour
    {
        if (logger == null)
        {
            logger = GetComponent<T>();
            if (logger == null)
            {
                logger = FindAnyObjectByType<T>();
            }
            if (logger == null && autoCreateDecoderPipeline)
            {
                logger = gameObject.AddComponent<T>();
            }
        }

        if (logger is RawTouchLogger rawLogger)
        {
            rawLogger.sessionManager = sessionManager;
        }
        else if (logger is BayesianDecisionLogger decision)
        {
            decision.sessionManager = sessionManager;
        }
        else if (logger is ButtonLayoutLogger layout)
        {
            layout.sessionManager = sessionManager;
        }
        else if (logger is HPOutcomeLogger hp)
        {
            hp.sessionManager = sessionManager;
        }
        else if (logger is ModePolicyLogger mode)
        {
            mode.sessionManager = sessionManager;
        }

        return logger;
    }

    private void ResolveAdaptationComponents()
    {
        if (demandModel == null)
        {
            demandModel = GetComponent<InteractionDemandModel>();
            if (demandModel == null)
            {
                demandModel = FindAnyObjectByType<InteractionDemandModel>();
            }
            if (demandModel == null && autoCreateDecoderPipeline)
            {
                demandModel = gameObject.AddComponent<InteractionDemandModel>();
            }
        }

        if (policyEngine == null)
        {
            policyEngine = GetComponent<AdaptiveUIPolicyEngine>();
            if (policyEngine == null)
            {
                policyEngine = FindAnyObjectByType<AdaptiveUIPolicyEngine>();
            }
            if (policyEngine == null && autoCreateDecoderPipeline)
            {
                policyEngine = gameObject.AddComponent<AdaptiveUIPolicyEngine>();
            }
        }

        if (adjustmentController == null)
        {
            adjustmentController = GetComponent<AdaptiveUIAdjustmentController>();
            if (adjustmentController == null)
            {
                adjustmentController = FindAnyObjectByType<AdaptiveUIAdjustmentController>();
            }
            if (adjustmentController == null && autoCreateDecoderPipeline)
            {
                adjustmentController = gameObject.AddComponent<AdaptiveUIAdjustmentController>();
            }
        }

        if (adjustmentController != null)
        {
            adjustmentController.attackButtonImage = visualAttackButton;
            adjustmentController.dodgeButtonImage = visualDodgeButton;
            adjustmentController.healButtonImage = visualHealButton;
            adjustmentController.whirlwindButtonImage = visualWhirlwindButton;
            adjustmentController.attackButtonRoot = visualAttackButton != null ? visualAttackButton.rectTransform : null;
            adjustmentController.dodgeButtonRoot = visualDodgeButton != null ? visualDodgeButton.rectTransform : null;
            adjustmentController.healButtonRoot = visualHealButton != null ? visualHealButton.rectTransform : null;
            adjustmentController.whirlwindButtonRoot = visualWhirlwindButton != null ? visualWhirlwindButton.rectTransform : null;
        }

        if (gameHud == null)
        {
            gameHud = FindAnyObjectByType<AdaptiveGameHudController>();
        }

        if (feedbackController == null)
        {
            feedbackController = FindAnyObjectByType<ADUIFeedbackController>();
        }

        if (publicPriorConfig == null)
        {
            publicPriorConfig = GetComponent<PublicPriorConfig>();
            if (publicPriorConfig == null)
            {
                publicPriorConfig = FindAnyObjectByType<PublicPriorConfig>();
            }
        }

        if (publicPriorConfig != null)
        {
            publicPriorConfig.ApplyTo(userTouchModel, decoder);
        }
    }

    private ADUIInteractionDemand BuildDemand(ADUIEnemyState enemyState, int playerHp, CombatManager combatManager)
    {
        float normalizedHp = 1f;
        if (combatManager != null && combatManager.playerController != null && combatManager.playerController.maxHP > 0)
        {
            normalizedHp = Mathf.Clamp01((float)playerHp / combatManager.playerController.maxHP);
        }

        bool dangerWarningVisible = enemyState == ADUIEnemyState.Telegraph ||
                                    enemyState == ADUIEnemyState.Attacking ||
                                    enemyState == ADUIEnemyState.Urgent;

        if (demandModel != null && combatManager != null)
        {
            return demandModel.Evaluate(combatManager.CurrentContext, combatManager.playerController, recentActionRate);
        }

        if (demandModel != null)
        {
            return demandModel.Evaluate(enemyState, dangerWarningVisible, normalizedHp, recentActionRate);
        }

        return new ADUIInteractionDemand
        {
            actionIntensity = recentActionRate,
            temporalUrgency = dangerWarningVisible ? 0.85f : 0.25f,
            informationPriority = dangerWarningVisible ? 0.8f : 0.45f,
            occlusionRisk = dangerWarningVisible ? 0.7f : 0.35f,
            controlContinuity = Mathf.Clamp01(0.4f + recentActionRate * 0.4f),
            uiSkill = 0.75f,
            mode = dangerWarningVisible ? ADUIInteractionMode.ActionFirst : ADUIInteractionMode.CognitiveFirst
        };
    }

    private ADUIAdjustmentPolicy BuildPolicy(ADUIInteractionDemand demand)
    {
        if (policyEngine != null)
        {
            return policyEngine.BuildPolicy(demand);
        }

        return new ADUIAdjustmentPolicy
        {
            mode = demand.mode,
            interactionErrorTolerance = demand.ErrorToleranceNeed,
            correctionStrength = 0.5f,
            hitboxExpansionRatio = 1.25f,
            ambiguityMarginPx = 60f,
            preserveClearInput = true
        };
    }

    private void ApplyPolicy(ADUIAdjustmentPolicy policy)
    {
        adjustmentController?.ApplyPolicy(policy);
        if (decoder != null && policy != null)
        {
            float errorTolerance = Mathf.Clamp01(policy.interactionErrorTolerance);
            decoder.ambiguityMarginPx = policy.ambiguityMarginPx;
            decoder.tau = Mathf.Lerp(0.62f, 0.48f, errorTolerance);
            decoder.delta = Mathf.Lerp(0.16f, 0.055f, errorTolerance);
            decoder.priorStrength = Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01(policy.correctionStrength));
            decoder.minLikelihoodThreshold = EffectivePosteriorThreshold();
            decoder.recoverableRadiusMultiplier = Mathf.Lerp(1.35f, 2.05f, errorTolerance);
        }

        if (safetyGate != null && policy != null)
        {
            safetyGate.preserveClearInsideButtonInput = policy.preserveClearInput;
            safetyGate.recoverableRadiusMultiplier = Mathf.Lerp(1.35f, 2.05f, Mathf.Clamp01(policy.interactionErrorTolerance));
        }
    }

    private void RefreshRuntimeModel(CombatManager combatManager, ADUIContextScenario scenario)
    {
        currentScenario = scenario;
        currentDemand = BuildDemand(CurrentEnemyState(), CurrentPlayerHp(combatManager), combatManager);
        currentPolicy = BuildPolicy(currentDemand);
        ApplyPolicy(currentPolicy);
    }

    private void UpdateAdaptiveHud(CombatManager combatManager)
    {
        if (gameHud == null)
        {
            gameHud = FindAnyObjectByType<AdaptiveGameHudController>();
        }

        if (gameHud == null)
        {
            return;
        }

        bool toastVisible = Time.time < correctionToastUntil && !string.IsNullOrEmpty(correctionToastMessage);
        PlayerController player = combatManager != null ? combatManager.playerController : null;
        gameHud.ApplyRuntimeState(
            player,
            currentDemand,
            currentPolicy,
            currentScenario,
            runtimeDebugLine,
            correctionToastMessage,
            correctionToastColor,
            toastVisible);
    }

    private void UpdateIdleDebugLine(float attackPrior, float dodgePrior, float healPrior, float whirlwindPrior)
    {
        if (Time.time < debugLineHoldUntil)
        {
            return;
        }

        runtimeDebugLine =
            $"decoder idle | mode={currentPolicy.mode} scenario={UserContextPriorModel.ScenarioLabel(currentScenario)} | " +
            $"prior A={attackPrior:0.00} D={dodgePrior:0.00} H={healPrior:0.00} W={whirlwindPrior:0.00} | " +
            $"threshold={EffectivePosteriorThreshold():0.000} safety=waiting";
    }

    private float EffectivePosteriorThreshold()
    {
        float errorTolerance = currentPolicy != null ? Mathf.Clamp01(currentPolicy.interactionErrorTolerance) : 0.5f;
        return Mathf.Max(0.0001f, minLikelihoodThreshold * Mathf.Lerp(0.78f, 0.34f, errorTolerance));
    }

    private float EffectiveAmbiguityGap()
    {
        float errorTolerance = currentPolicy != null ? Mathf.Clamp01(currentPolicy.interactionErrorTolerance) : 0.5f;
        return Mathf.Lerp(0.12f, 0.045f, errorTolerance);
    }

    private void SetDecisionFeedback(string message, Color color)
    {
        correctionToastMessage = message;
        correctionToastColor = color;
        correctionToastUntil = Time.time + 1.35f;
    }

    private void UpdateRuntimeDebugLine(
        List<ActionCandidate> candidates,
        ActionCandidate best,
        float threshold,
        float posteriorGap,
        string safetyReason)
    {
        runtimeDebugLine =
            $"prior/posterior {FormatCandidatePosteriors(candidates)} | " +
            $"best={best.label} gap={posteriorGap:0.000} threshold={threshold:0.000} | " +
            $"safety={safetyReason} mode={currentPolicy.mode}";
        debugLineHoldUntil = Time.time + 1.8f;
    }

    private void LogFourButtonDecision(
        Vector2 inputPos,
        List<ActionCandidate> candidates,
        AdaptiveAction resolvedAction,
        bool executed,
        bool invalidTouch,
        string safetyReason,
        int playerHpBefore,
        int enemyHpBefore,
        long touchMs,
        long actionMs)
    {
        int trialId = trialScenarioManager != null ? trialScenarioManager.currentTrialId : 0;
        ADUIDecodeInput input = null;
        ADUIDecodeResult result = null;

        if (enableRuntimeLogging || evaluationLogger != null)
        {
            input = BuildFourButtonDecodeInput(inputPos);
            result = BuildFourButtonDecodeResult(input, candidates, resolvedAction, executed, invalidTouch, safetyReason);
        }

        if (enableRuntimeLogging && input != null && result != null)
        {
            LogTrialRecord(
                trialId,
                touchMs,
                actionMs,
                inputPos,
                input,
                result,
                currentDemand,
                currentPolicy,
                playerHpBefore,
                CurrentPlayerHp(CombatManager.Instance),
                enemyHpBefore,
                CurrentEnemyHp(CombatManager.Instance),
                executed);
        }

        if (evaluationLogger != null && input != null && result != null)
        {
            evaluationLogger.LogTouchDecision(
                trialId,
                input,
                result,
                currentDemand,
                currentPolicy,
                currentScenario,
                playerHpBefore,
                CurrentPlayerHp(CombatManager.Instance),
                enemyHpBefore,
                CurrentEnemyHp(CombatManager.Instance));
        }

        if (enableRuntimeLogging && modePolicyLogger != null)
        {
            modePolicyLogger.Log(trialId, currentDemand, currentPolicy);
        }
    }

    private ADUIDecodeInput BuildFourButtonDecodeInput(Vector2 inputPos)
    {
        float expansion = currentPolicy != null ? Mathf.Max(1f, currentPolicy.hitboxExpansionRatio) : 1.25f;
        return new ADUIDecodeInput
        {
            touchPosition = inputPos,
            attackButton = GeometryForImage("Attack", visualAttackButton, expansion),
            dodgeButton = GeometryForImage("Dodge", visualDodgeButton, expansion),
            enemyState = CurrentEnemyState(),
            condition = CurrentCondition()
        };
    }

    private ADUIButtonGeometry GeometryForImage(string actionName, Image image, float expansion)
    {
        if (image != null)
        {
            return ADUIButtonGeometry.FromRect(actionName, image.rectTransform, expansion);
        }

        return new ADUIButtonGeometry
        {
            action = actionName,
            centerX = 0f,
            centerY = 0f,
            visualRadius = 1f,
            hitboxRadius = Mathf.Max(1f, expansion)
        };
    }

    private ADUIDecodeResult BuildFourButtonDecodeResult(
        ADUIDecodeInput input,
        List<ActionCandidate> candidates,
        AdaptiveAction resolvedAction,
        bool executed,
        bool invalidTouch,
        string safetyReason)
    {
        ADUIAction mappedAction = invalidTouch || !executed ? ADUIAction.None : MapAction(resolvedAction);
        ADUIAction bayesianAction = invalidTouch ? MapAction(resolvedAction) : mappedAction;
        var result = new ADUIDecodeResult
        {
            distanceToAttack = Vector2.Distance(input.touchPosition, input.attackButton.Center),
            distanceToDodge = Vector2.Distance(input.touchPosition, input.dodgeButton.Center),
            varianceAttack = Square(GetEffectiveSpread(AdaptiveAction.Attack, currentScenario)),
            varianceDodge = Square(GetEffectiveSpread(AdaptiveAction.Dodge, currentScenario)),
            tau = EffectivePosteriorThreshold(),
            delta = EffectiveAmbiguityGap(),
            priorStrength = currentPolicy != null ? currentPolicy.correctionStrength : 0.5f,
            invalidTouch = invalidTouch,
            safetyGatePassed = !invalidTouch && safetyReason.StartsWith("correction"),
            safetyGateReason = safetyReason,
            finalExecutedAction = mappedAction,
            bayesianPrediction = bayesianAction,
            userGaussianPrediction = bayesianAction,
            contextPriorOnlyPrediction = bayesianAction,
            visualBoundaryPrediction = candidates == null ? mappedAction : ADUIAction.None,
            expandedHitboxPrediction = candidates == null ? mappedAction : ADUIAction.None
        };

        if (candidates == null || candidates.Count == 0)
        {
            ApplyDirectResultScores(result, resolvedAction, invalidTouch);
            return result;
        }

        float totalPosterior = 0f;
        ActionCandidate best = candidates[0];
        for (int i = 0; i < candidates.Count; i++)
        {
            totalPosterior += Mathf.Max(0f, candidates[i].posterior);
            if (candidates[i].posterior > best.posterior)
            {
                best = candidates[i];
            }
        }

        if (TryFindCandidate(candidates, AdaptiveAction.Attack, out ActionCandidate attackCandidate))
        {
            result.likelihoodAttack = attackCandidate.likelihood;
            result.priorAttack = attackCandidate.prior;
            result.posteriorAttack = totalPosterior > 0f ? attackCandidate.posterior / totalPosterior : 0f;
        }

        if (TryFindCandidate(candidates, AdaptiveAction.Dodge, out ActionCandidate dodgeCandidate))
        {
            result.likelihoodDodge = dodgeCandidate.likelihood;
            result.priorDodge = dodgeCandidate.prior;
            result.posteriorDodge = totalPosterior > 0f ? dodgeCandidate.posterior / totalPosterior : 0f;
        }

        float bestNormalized = totalPosterior > 0f ? best.posterior / totalPosterior : 0f;
        float runnerUp = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].label == best.label)
            {
                continue;
            }

            runnerUp = Mathf.Max(runnerUp, totalPosterior > 0f ? candidates[i].posterior / totalPosterior : 0f);
        }

        result.maxPosterior = bestNormalized;
        result.posteriorGap = Mathf.Max(0f, bestNormalized - runnerUp);
        result.isAmbiguous = result.posteriorGap < EffectiveAmbiguityGap();
        result.isNearBoundary = result.isAmbiguous;
        result.bayesianPrediction = MapAction(best.action);
        result.userGaussianPrediction = MapAction(best.action);
        result.contextPriorOnlyPrediction = MapAction(best.action);
        return result;
    }

    private void ApplyDirectResultScores(ADUIDecodeResult result, AdaptiveAction action, bool invalidTouch)
    {
        bool attack = action == AdaptiveAction.Attack;
        bool dodge = action == AdaptiveAction.Dodge;
        result.likelihoodAttack = attack ? 1f : 0f;
        result.likelihoodDodge = dodge ? 1f : 0f;
        result.priorAttack = attack ? 1f : 0f;
        result.priorDodge = dodge ? 1f : 0f;
        result.posteriorAttack = attack ? 1f : 0f;
        result.posteriorDodge = dodge ? 1f : 0f;
        result.maxPosterior = invalidTouch ? 0f : 1f;
        result.posteriorGap = invalidTouch ? 0f : 1f;
        result.isAmbiguous = false;
        result.isNearBoundary = false;
    }

    private bool TryFindCandidate(List<ActionCandidate> candidates, AdaptiveAction action, out ActionCandidate candidate)
    {
        candidate = new ActionCandidate();
        if (candidates == null)
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].action == action)
            {
                candidate = candidates[i];
                return true;
            }
        }

        return false;
    }

    private ADUIAction MapAction(AdaptiveAction action)
    {
        switch (action)
        {
            case AdaptiveAction.Dodge:
                return ADUIAction.Dodge;
            case AdaptiveAction.Heal:
                return ADUIAction.Heal;
            case AdaptiveAction.Whirlwind:
                return ADUIAction.Whirlwind;
            default:
                return ADUIAction.Attack;
        }
    }

    private bool IsCalibrationPhase()
    {
        return trialScenarioManager != null && trialScenarioManager.currentPhase == DatasetSchema.PhaseCalibration;
    }

    private void AddCalibrationSample(Vector2 touchPosition, ADUIDecodeInput input)
    {
        if (!collectCalibrationSamples || userTouchModel == null || trialScenarioManager == null)
        {
            return;
        }

        if (!trialScenarioManager.ShouldUseCurrentCalibrationTrialForTouchModel())
        {
            return;
        }

        ADUIAction intendedAction = ParseAction(trialScenarioManager.currentIntendedAction);
        ADUIButtonGeometry button = ButtonForAction(intendedAction, input);
        if (button == null)
        {
            return;
        }

        userTouchModel.AddCalibrationSample(intendedAction, touchPosition, button);
    }

    private void AddOnlineAdaptationSample(Vector2 touchPosition, ADUIDecodeInput input, ADUIDecodeResult result)
    {
        if (!enableOnlineTouchAdaptation || userTouchModel == null || result == null || result.invalidTouch)
        {
            return;
        }

        if (result.safetyGateReason != "preserve_clear_visual_input")
        {
            return;
        }

        ADUIAction action = result.visualBoundaryPrediction;
        ADUIButtonGeometry button = ButtonForAction(action, input);
        if (button == null)
        {
            return;
        }

        userTouchModel.AddOnlineAdaptationSample(action, touchPosition, button);
    }

    private void SaveCalibrationProfile()
    {
        if (!saveCalibrationProfileOnComplete || userTouchModel == null || sessionManager == null)
        {
            return;
        }

        string sessionDir = sessionManager.EnsureSession();
        string path = Path.Combine(sessionDir, "user_touch_profile.json");
        userTouchModel.SaveProfile(path);
        Debug.Log($"[ADUI] Calibration profile saved: {path}");
    }

    private ADUIAction ParseAction(string value)
    {
        if (string.Equals(value, "Attack", System.StringComparison.OrdinalIgnoreCase))
        {
            return ADUIAction.Attack;
        }

        if (string.Equals(value, "Dodge", System.StringComparison.OrdinalIgnoreCase))
        {
            return ADUIAction.Dodge;
        }

        if (string.Equals(value, "Heal", System.StringComparison.OrdinalIgnoreCase))
        {
            return ADUIAction.Heal;
        }

        if (string.Equals(value, "Whirlwind", System.StringComparison.OrdinalIgnoreCase))
        {
            return ADUIAction.Whirlwind;
        }

        return ADUIAction.None;
    }

    private ADUIButtonGeometry ButtonForAction(ADUIAction action, ADUIDecodeInput input)
    {
        if (action == ADUIAction.Attack)
        {
            return input.attackButton;
        }

        if (action == ADUIAction.Dodge)
        {
            return input.dodgeButton;
        }

        return null;
    }

    private void UpdateCalibrationInstructionUI(CombatManager combatManager)
    {
        if (!IsCalibrationPhase() || combatManager == null || combatManager.feedbackLogText == null)
        {
            return;
        }

        combatManager.feedbackLogText.text = trialScenarioManager.currentInstruction;
        combatManager.feedbackLogText.color = Color.white;
    }

    private ADUIEnemyState CurrentEnemyState()
    {
        if (trialScenarioManager != null)
        {
            return trialScenarioManager.CurrentEnemyState();
        }

        CombatManager combatManager = CombatManager.Instance;
        if (combatManager == null)
        {
            return ADUIEnemyState.Neutral;
        }

        switch (combatManager.currentState)
        {
            case CombatManager.CombatState.Attacking:
                return ADUIEnemyState.Attacking;
            case CombatManager.CombatState.Telegraph:
                return ADUIEnemyState.Telegraph;
            case CombatManager.CombatState.Safe:
            default:
                return ADUIEnemyState.Safe;
        }
    }

    private string CurrentCondition()
    {
        if (trialScenarioManager != null)
        {
            return trialScenarioManager.CurrentCondition();
        }

        return conditionManager != null
            ? conditionManager.currentCondition
            : DatasetSchema.ConditionContextBayesianSafety;
    }

    private void UpdateRecentActionRate()
    {
        float now = Time.time;
        if (lastTouchTime < 0f)
        {
            recentActionRate = 0.25f;
        }
        else
        {
            float interval = Mathf.Max(0.1f, now - lastTouchTime);
            recentActionRate = Mathf.Clamp01((1f / interval) / 4f);
        }

        lastTouchTime = now;
    }

    private int CurrentPlayerHp(CombatManager combatManager)
    {
        return combatManager != null && combatManager.playerController != null
            ? combatManager.playerController.CurrentHP
            : 0;
    }

    private int CurrentEnemyHp(CombatManager combatManager)
    {
        if (combatManager == null || combatManager.gameManager == null)
        {
            return 0;
        }

        int total = 0;
        var enemies = combatManager.gameManager.ActiveEnemies;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] != null && enemies[i].IsAlive)
            {
                total += enemies[i].CurrentHP;
            }
        }

        return total;
    }

    private bool ResolveActionSuccess(ADUIDecodeResult result)
    {
        string intended = trialScenarioManager != null ? trialScenarioManager.currentIntendedAction : "";
        if (!string.IsNullOrEmpty(intended))
        {
            return string.Equals(result.finalExecutedAction.ToString(), intended, System.StringComparison.OrdinalIgnoreCase);
        }

        return !result.invalidTouch && result.finalExecutedAction != ADUIAction.None;
    }

    private void LogTrialRecord(
        int trialId,
        long touchMs,
        long actionMs,
        Vector2 touchPosition,
        ADUIDecodeInput input,
        ADUIDecodeResult result,
        ADUIInteractionDemand demand,
        ADUIAdjustmentPolicy policy,
        int playerHpBefore,
        int playerHpAfter,
        int enemyHpBefore,
        int enemyHpAfter,
        bool actionSuccess)
    {
        if (!enableRuntimeLogging || sessionManager == null || sessionManager.exporter == null)
        {
            return;
        }

        sessionManager.EnsureSession();

        long endMs = NowMs();
        string phase = trialScenarioManager != null ? trialScenarioManager.currentPhase : DatasetSchema.PhaseFreeplay;
        string condition = input.condition;
        string intendedAction = trialScenarioManager != null ? trialScenarioManager.currentIntendedAction : "";
        string requiredAction = trialScenarioManager != null ? trialScenarioManager.currentRequiredAction : "";
        string labelSource = trialScenarioManager != null ? trialScenarioManager.currentLabelSource : "freeplay";
        long trialStartMs = trialScenarioManager != null && trialScenarioManager.currentTrialStartMs > 0
            ? trialScenarioManager.currentTrialStartMs
            : touchMs;
        ADUIButtonGeometry intendedButton = ButtonForAction(ParseAction(intendedAction), input);

        var record = new ADUITrialRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            block_id = trialScenarioManager != null ? trialScenarioManager.currentBlockId : 0,
            phase = phase,
            condition = condition,
            interaction_mode = policy != null ? policy.mode.ToString() : "",
            timestamp_trial_start_ms = trialStartMs,
            timestamp_touch_ms = touchMs,
            timestamp_action_ms = actionMs,
            timestamp_trial_end_ms = endMs,
            screen_width = Screen.width,
            screen_height = Screen.height,
            attack_center_x = input.attackButton.centerX,
            attack_center_y = input.attackButton.centerY,
            attack_visual_radius = input.attackButton.visualRadius,
            attack_hitbox_radius = input.attackButton.hitboxRadius,
            dodge_center_x = input.dodgeButton.centerX,
            dodge_center_y = input.dodgeButton.centerY,
            dodge_visual_radius = input.dodgeButton.visualRadius,
            dodge_hitbox_radius = input.dodgeButton.hitboxRadius,
            dynamic_attack_radius = DynamicRadius(result.varianceAttack, result.priorAttack),
            dynamic_dodge_radius = DynamicRadius(result.varianceDodge, result.priorDodge),
            enemy_state = input.enemyState.ToString(),
            danger_warning_visible = input.enemyState == ADUIEnemyState.Telegraph || input.enemyState == ADUIEnemyState.Attacking || input.enemyState == ADUIEnemyState.Urgent,
            enemy_distance = CombatManager.Instance != null && CombatManager.Instance.CurrentContext.totalEnemies > 0
                ? CombatManager.Instance.CurrentContext.closestEnemyDistance
                : 0f,
            player_hp_before = playerHpBefore,
            enemy_hp_before = enemyHpBefore,
            cooldown_attack = 0f,
            cooldown_dodge = 0f,
            required_action = requiredAction,
            intended_action = intendedAction,
            label_source = labelSource,
            trial_type = trialScenarioManager != null ? trialScenarioManager.currentTrialType : "",
            calibration_instruction = trialScenarioManager != null ? trialScenarioManager.currentInstruction : "",
            calibration_used_for_touch_model = trialScenarioManager != null && trialScenarioManager.ShouldUseCurrentCalibrationTrialForTouchModel(),
            touch_x = touchPosition.x,
            touch_y = touchPosition.y,
            touch_phase = "Began",
            touch_pressure = 0f,
            touch_radius = 0f,
            distance_to_attack = result.distanceToAttack,
            distance_to_dodge = result.distanceToDodge,
            relative_attack_x = RelativeX(touchPosition, input.attackButton),
            relative_attack_y = RelativeY(touchPosition, input.attackButton),
            relative_dodge_x = RelativeX(touchPosition, input.dodgeButton),
            relative_dodge_y = RelativeY(touchPosition, input.dodgeButton),
            intended_center_x = intendedButton != null ? intendedButton.centerX : 0f,
            intended_center_y = intendedButton != null ? intendedButton.centerY : 0f,
            distance_to_intended = intendedButton != null ? Vector2.Distance(touchPosition, intendedButton.Center) : 0f,
            relative_intended_x = intendedButton != null ? RelativeX(touchPosition, intendedButton) : 0f,
            relative_intended_y = intendedButton != null ? RelativeY(touchPosition, intendedButton) : 0f,
            is_inside_attack_visual = IsInside(touchPosition, input.attackButton, input.attackButton.visualRadius),
            is_inside_dodge_visual = IsInside(touchPosition, input.dodgeButton, input.dodgeButton.visualRadius),
            is_inside_attack_expanded = IsInside(touchPosition, input.attackButton, input.attackButton.hitboxRadius),
            is_inside_dodge_expanded = IsInside(touchPosition, input.dodgeButton, input.dodgeButton.hitboxRadius),
            is_near_boundary = result.isNearBoundary,
            is_ambiguous = result.isAmbiguous,
            likelihood_attack = result.likelihoodAttack,
            likelihood_dodge = result.likelihoodDodge,
            prior_attack = result.priorAttack,
            prior_dodge = result.priorDodge,
            posterior_attack = result.posteriorAttack,
            posterior_dodge = result.posteriorDodge,
            posterior_gap = result.posteriorGap,
            max_posterior = result.maxPosterior,
            tau = result.tau,
            delta = result.delta,
            variance_attack = result.varianceAttack,
            variance_dodge = result.varianceDodge,
            prior_strength = result.priorStrength,
            public_prior_source = result.publicPriorSource,
            public_variance_source = result.publicVarianceSource,
            visual_boundary_prediction = result.visualBoundaryPrediction.ToString(),
            expanded_hitbox_prediction = result.expandedHitboxPrediction.ToString(),
            user_gaussian_prediction = result.userGaussianPrediction.ToString(),
            context_prior_only_prediction = result.contextPriorOnlyPrediction.ToString(),
            bayesian_prediction = result.bayesianPrediction.ToString(),
            final_executed_action = result.finalExecutedAction.ToString(),
            invalid_touch = result.invalidTouch,
            safety_gate_passed = result.safetyGatePassed,
            safety_gate_reason = result.safetyGateReason,
            action_success = actionSuccess,
            hp_after = playerHpAfter,
            enemy_hp_after = enemyHpAfter,
            damage_taken = Mathf.Max(0, playerHpBefore - playerHpAfter),
            damage_dealt = Mathf.Max(0, enemyHpBefore - enemyHpAfter),
            survived = playerHpAfter > 0,
            cooldown_wasted = result.finalExecutedAction == ADUIAction.None,
            reaction_time_ms = actionMs - trialStartMs,
            feedback_type = actionSuccess ? "success" : "fail",
            button_feedback_color = actionSuccess ? "green" : "red",
            feedback_message = result.finalExecutedAction.ToString(),
            haptic_feedback_triggered = feedbackController != null && feedbackController.LastHapticTriggered,
            hitbox_visualization_enabled = attackHitboxVisualizer != null && attackHitboxVisualizer.gameObject.activeInHierarchy,
            demand_action_intensity = demand != null ? demand.actionIntensity : 0f,
            demand_temporal_urgency = demand != null ? demand.temporalUrgency : 0f,
            demand_information_priority = demand != null ? demand.informationPriority : 0f,
            demand_occlusion_risk = demand != null ? demand.occlusionRisk : 0f,
            demand_control_continuity = demand != null ? demand.controlContinuity : 0f,
            demand_ui_skill = demand != null ? demand.uiSkill : 0f,
            policy_visibility = policy != null ? policy.visibility : 0f,
            policy_emphasis = policy != null ? policy.emphasis : 0f,
            policy_density = policy != null ? policy.density : 0f,
            policy_position_constraint = policy != null ? policy.positionConstraint : 0f,
            policy_error_tolerance = policy != null ? policy.interactionErrorTolerance : 0f,
            policy_feedback_intensity = policy != null ? policy.feedbackIntensity : 0f,
            policy_correction_strength = policy != null ? policy.correctionStrength : 0f,
            policy_hitbox_expansion_ratio = policy != null ? policy.hitboxExpansionRatio : 0f,
            policy_ambiguity_margin_px = policy != null ? policy.ambiguityMarginPx : 0f,
            policy_preserve_clear_input = policy != null && policy.preserveClearInput,
            policy_haptic_enabled = policy != null && policy.hapticEnabled,
            policy_guidance_visible = policy != null && policy.showGuidance,
            policy_review_visible = policy != null && policy.showReview,
            policy_reason = policy != null ? policy.policyReason : "",
            user_correction_enabled = true,
            user_correction_strength = policy != null ? policy.correctionStrength : 0f
        };

        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "main_trials.jsonl", record);
    }

    private float RelativeX(Vector2 touchPosition, ADUIButtonGeometry button)
    {
        return (touchPosition.x - button.centerX) / Mathf.Max(button.visualRadius, 1f);
    }

    private float RelativeY(Vector2 touchPosition, ADUIButtonGeometry button)
    {
        return (touchPosition.y - button.centerY) / Mathf.Max(button.visualRadius, 1f);
    }

    private bool IsInside(Vector2 touchPosition, ADUIButtonGeometry button, float radius)
    {
        return Vector2.Distance(touchPosition, button.Center) <= radius;
    }

    private long NowMs()
    {
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

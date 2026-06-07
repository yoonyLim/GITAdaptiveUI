using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class AdaptiveTouchManager : MonoBehaviour
{
    [Header("Visual Buttons (UI Images)")]
    public Canvas mainCanvas;
    public Image visualAttackButton;
    public Image visualDodgeButton;

    [Header("Button Feedback Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Gaussian Hitbox Visualizers")]
    public RectTransform attackHitboxVisualizer;
    public RectTransform dodgeHitboxVisualizer;

    [Header("Ignored Input Regions")]
    public RectTransform[] ignoredInputRegions;

    [Header("Touch Tuning")]
    [Tooltip("Represents the player's fat-finger spread in screen pixels. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f;

    [Tooltip("Minimum posterior required before a touch is accepted.")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    [Header("ADUI Decoder Pipeline")]
    public bool autoCreateDecoderPipeline = true;
    public bool enableRuntimeLogging = true;

    [Header("Calibration & Online Adaptation")]
    public bool collectCalibrationSamples = true;
    public bool suppressGameplayActionsDuringCalibration = true;
    public bool saveCalibrationProfileOnComplete = true;
    public bool enableOnlineTouchAdaptation = true;

    public BayesianInputDecoder decoder;
    public SafetyGate safetyGate;
    public UserTouchModel userTouchModel;
    public ExperimentSessionManager sessionManager;
    public TrialScenarioManager trialScenarioManager;
    public ConditionManager conditionManager;
    public RawTouchLogger rawTouchLogger;
    public BayesianDecisionLogger decisionLogger;
    public ButtonLayoutLogger layoutLogger;
    public HPOutcomeLogger hpOutcomeLogger;
    public ModePolicyLogger modePolicyLogger;
    public InteractionDemandModel demandModel;
    public AdaptiveUIPolicyEngine policyEngine;
    public AdaptiveUIAdjustmentController adjustmentController;
    public ADUIFeedbackController feedbackController;
    public PublicPriorConfig publicPriorConfig;

    private ADUIAdjustmentPolicy currentPolicy = new ADUIAdjustmentPolicy();
    private ADUIInteractionDemand currentDemand = new ADUIInteractionDemand();
    private float lastTouchTime = -100f;
    private float recentActionRate;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
        ResolveDecoderPipeline();
    }

    private void Update()
    {
        ResolveDecoderPipeline();

        CombatManager combatManager = CombatManager.Instance;
        float attackPrior = combatManager != null ? combatManager.priorAttack : 0.5f;
        float dodgePrior = combatManager != null ? combatManager.priorDodge : 0.5f;

        float attackVariance = userTouchModel != null ? userTouchModel.GetVariance(ADUIAction.Attack) : userTouchVariance * userTouchVariance;
        float dodgeVariance = userTouchModel != null ? userTouchModel.GetVariance(ADUIAction.Dodge) : userTouchVariance * userTouchVariance;

        UpdateHitboxVisualizer(attackHitboxVisualizer, CalculateDynamicRadius(attackVariance, attackPrior));
        UpdateHitboxVisualizer(dodgeHitboxVisualizer, CalculateDynamicRadius(dodgeVariance, dodgePrior));
        UpdateCalibrationInstructionUI(combatManager);

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
        if (IsInsideIgnoredInputRegion(inputPos))
        {
            return;
        }

        if (visualAttackButton == null || visualDodgeButton == null)
        {
            Debug.LogWarning("AdaptiveTouchManager needs both attack and dodge button images assigned.");
            return;
        }

        ResolveDecoderPipeline();

        CombatManager combatManager = CombatManager.Instance;
        ADUIEnemyState enemyState = CurrentEnemyState();
        string condition = CurrentCondition();
        bool isCalibration = IsCalibrationPhase();
        int trialId = trialScenarioManager != null ? trialScenarioManager.currentTrialId : 0;
        long touchMs = NowMs();
        int playerHpBefore = CurrentPlayerHp(combatManager);
        int enemyHpBefore = CurrentEnemyHp(combatManager);

        UpdateRecentActionRate();
        currentDemand = BuildDemand(enemyState, playerHpBefore, combatManager);
        currentPolicy = BuildPolicy(currentDemand);
        ApplyPolicy(currentPolicy);

        ADUIButtonGeometry attackButton = ADUIButtonGeometry.FromRect("Attack", visualAttackButton.rectTransform, currentPolicy.hitboxExpansionRatio);
        ADUIButtonGeometry dodgeButton = ADUIButtonGeometry.FromRect("Dodge", visualDodgeButton.rectTransform, currentPolicy.hitboxExpansionRatio);

        if (enableRuntimeLogging)
        {
            sessionManager?.EnsureSession();
            rawTouchLogger?.Log(trialId, inputPos, "Began");
        }

        if (decoder == null)
        {
            Debug.LogWarning("AdaptiveTouchManager could not find or create BayesianInputDecoder.");
            return;
        }

        if (isCalibration && trialScenarioManager != null)
        {
            Vector2 priors = decoder.PriorForState(enemyState);
            decoder.SetExternalActionPrior(priors.x, priors.y, "calibration_scenario_prior");
        }
        else if (combatManager != null)
        {
            string source = !string.IsNullOrEmpty(combatManager.CurrentPriorResult.source)
                ? combatManager.CurrentPriorResult.source
                : "combat_context_rule_prior";
            decoder.SetExternalActionPrior(combatManager.priorAttack, combatManager.priorDodge, source);
        }
        else
        {
            decoder.ClearExternalActionPrior();
        }

        decoder.minLikelihoodThreshold = Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f);
        decoder.ambiguityMarginPx = currentPolicy.ambiguityMarginPx;

        ADUIDecodeInput decodeInput = new ADUIDecodeInput
        {
            touchPosition = inputPos,
            attackButton = attackButton,
            dodgeButton = dodgeButton,
            enemyState = enemyState,
            condition = condition
        };

        ADUIDecodeResult result = decoder.Decode(decodeInput);
        result = ApplyConditionPolicy(decodeInput, result, condition);

        HighlightExecutedButton(result.finalExecutedAction);
        if (!isCalibration || !suppressGameplayActionsDuringCalibration)
        {
            ExecuteDecodedAction(result.finalExecutedAction, combatManager);
        }

        int playerHpAfter = CurrentPlayerHp(combatManager);
        int enemyHpAfter = CurrentEnemyHp(combatManager);
        bool actionSuccess = ResolveActionSuccess(result);
        long actionMs = NowMs();

        if (enableRuntimeLogging)
        {
            decisionLogger?.Log(trialId, condition, enemyState, result);
            layoutLogger?.Log(
                trialId,
                attackButton,
                dodgeButton,
                DynamicRadius(result.varianceAttack, result.priorAttack),
                DynamicRadius(result.varianceDodge, result.priorDodge));
            hpOutcomeLogger?.Log(trialId, playerHpBefore, playerHpAfter, enemyHpBefore, enemyHpAfter, actionSuccess);
            modePolicyLogger?.Log(trialId, currentDemand, currentPolicy);
        }
        feedbackController?.ShowDecisionFeedback(result, currentPolicy);
        LogTrialRecord(
            trialId,
            touchMs,
            actionMs,
            inputPos,
            decodeInput,
            result,
            currentDemand,
            currentPolicy,
            playerHpBefore,
            playerHpAfter,
            enemyHpBefore,
            enemyHpAfter,
            actionSuccess);

        bool overcorrectionRisk =
            result.safetyGatePassed &&
            result.visualBoundaryPrediction != ADUIAction.None &&
            result.finalExecutedAction != result.visualBoundaryPrediction;
        demandModel?.UpdateRecentErrorSignals(result.invalidTouch, overcorrectionRisk);

        if (isCalibration)
        {
            AddCalibrationSample(inputPos, decodeInput);
        }
        else
        {
            AddOnlineAdaptationSample(inputPos, decodeInput, result);
        }

        trialScenarioManager?.CompleteTrial();
        if (isCalibration && !IsCalibrationPhase())
        {
            SaveCalibrationProfile();
        }

        if (result.finalExecutedAction == ADUIAction.None)
        {
            Debug.Log($"[Adaptive Touch] Rejected by {result.safetyGateReason}.");
        }
        else
        {
            Debug.Log(
                $"[Adaptive Touch] {result.finalExecutedAction} accepted. Prior A/D={result.priorAttack:F2}/{result.priorDodge:F2}, Posterior A/D={result.posteriorAttack:F3}/{result.posteriorDodge:F3}, Gate={result.safetyGateReason}");
        }
    }

    private void ProcessInputEnded()
    {
        if (visualAttackButton != null)
        {
            visualAttackButton.color = normalColor;
        }

        if (visualDodgeButton != null)
        {
            visualDodgeButton.color = normalColor;
        }
    }

    private void UpdateHitboxVisualizer(RectTransform visualizer, float screenRadius)
    {
        if (visualizer == null)
        {
            return;
        }

        float scaleFactor = mainCanvas != null ? Mathf.Max(0.01f, mainCanvas.scaleFactor) : 1f;
        float uiSize = (screenRadius * 2f) / scaleFactor;
        visualizer.sizeDelta = new Vector2(uiSize, uiSize);
    }

    private ADUIDecodeResult ApplyConditionPolicy(ADUIDecodeInput input, ADUIDecodeResult result, string condition)
    {
        switch (condition)
        {
            case DatasetSchema.ConditionVisualBoundary:
                result.finalExecutedAction = result.visualBoundaryPrediction;
                result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
                result.safetyGatePassed = false;
                result.safetyGateReason = "visual_boundary_baseline";
                return result;

            case DatasetSchema.ConditionExpandedHitbox:
                result.finalExecutedAction = result.expandedHitboxPrediction;
                result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
                result.safetyGatePassed = false;
                result.safetyGateReason = "expanded_hitbox_baseline";
                return result;

            case DatasetSchema.ConditionUserGaussian:
                result.finalExecutedAction = result.invalidTouch ? ADUIAction.None : result.userGaussianPrediction;
                result.safetyGatePassed = false;
                result.safetyGateReason = "user_gaussian_baseline";
                return result;

            case DatasetSchema.ConditionContextPriorOnly:
                result.finalExecutedAction = result.invalidTouch ? ADUIAction.None : result.contextPriorOnlyPrediction;
                result.safetyGatePassed = false;
                result.safetyGateReason = "context_prior_only_baseline";
                return result;

            case DatasetSchema.ConditionContextBayesianNoSafety:
                result.finalExecutedAction = result.invalidTouch ? ADUIAction.None : result.bayesianPrediction;
                result.safetyGatePassed = false;
                result.safetyGateReason = "context_bayesian_no_safety";
                return result;

            case DatasetSchema.ConditionContextBayesianSafety:
            default:
                return safetyGate != null ? safetyGate.Apply(input, result) : result;
        }
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
        }

        if (adjustmentController != null)
        {
            adjustmentController.attackButtonImage = visualAttackButton;
            adjustmentController.dodgeButtonImage = visualDodgeButton;
            adjustmentController.attackButtonRoot = visualAttackButton != null ? visualAttackButton.rectTransform : null;
            adjustmentController.dodgeButtonRoot = visualDodgeButton != null ? visualDodgeButton.rectTransform : null;
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
            mode = dangerWarningVisible ? ADUIInteractionMode.ActionFirst : ADUIInteractionMode.LearningReview
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
        if (safetyGate != null)
        {
            safetyGate.preserveClearInsideButtonInput = policy.preserveClearInput;
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

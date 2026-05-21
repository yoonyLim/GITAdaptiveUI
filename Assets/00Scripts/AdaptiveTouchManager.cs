using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem; 
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

    [Header("ADUI Models")]
    public bool useActionFirstDecoder = true;
    public float expandedHitboxRatio = 1.25f;
    public UserTouchModel userTouchModel;
    public BayesianInputDecoder bayesianDecoder;
    public SafetyGate safetyGate;
    public PublicPriorConfig publicPriorConfig;
    public InteractionDemandModel demandModel;
    public AdaptiveUIPolicyEngine policyEngine;
    public AdaptiveUIAdjustmentController uiAdjustmentController;
    public UserCorrectionSettings userCorrectionSettings;
    public ADUIFeedbackController aduiFeedbackController;

    [Header("Optional Multi-game Vision Prior")]
    public bool useMultigameVisionPrior = false;
    public MultigameVisionPriorConfig multigameVisionPriorConfig;
    public VisionPriorBuilder visionPriorBuilder;
    public VisionPriorReplayLoader visionPriorReplayLoader;
    public VisionInferenceStub visionInferenceStub;

    [Header("ADUI Data Logging")]
    public ExperimentSessionManager sessionManager;
    public TrialScenarioManager trialScenarioManager;
    public RawTouchLogger rawTouchLogger;
    public ButtonLayoutLogger buttonLayoutLogger;
    public BayesianDecisionLogger bayesianDecisionLogger;
    public HPOutcomeLogger hpOutcomeLogger;
    public ModePolicyLogger modePolicyLogger;

    [Header("Touch Tuning")]
    [Tooltip("Represents the player's fat finger spread/variance. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f; 
    
    [Tooltip("Spatial Likelihood: less than this is ignored (e.g.: 0.05 = 5%)")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    private ADUIInteractionDemand lastDemand;
    private ADUIAdjustmentPolicy lastPolicy = new ADUIAdjustmentPolicy();
    public ADUIDecodeResult LastDecodeResult { get; private set; }
    public ADUIAdjustmentPolicy LastPolicy => lastPolicy;
    public ADUIInteractionDemand LastDemand => lastDemand;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Start()
    {
        if (publicPriorConfig && publicPriorConfig.LoadPublicDefaults())
        {
            userTouchVariance = Mathf.Sqrt(Mathf.Max(publicPriorConfig.recommendedDefaultVariance, 1f));
            expandedHitboxRatio = publicPriorConfig.recommendedExpandedHitboxMargin;
            publicPriorConfig.ApplyTo(userTouchModel, bayesianDecoder);
        }

        if (bayesianDecoder && userTouchModel && !bayesianDecoder.userTouchModel)
        {
            bayesianDecoder.userTouchModel = userTouchModel;
        }

        if (multigameVisionPriorConfig)
        {
            multigameVisionPriorConfig.LoadConfig();
        }
        if (visionPriorReplayLoader)
        {
            visionPriorReplayLoader.LoadReplay();
        }
    }
    
    private void Update()
    {
        var enemyState = trialScenarioManager ? trialScenarioManager.CurrentEnemyState() : CurrentEnemyStateFromCombat();
        var policy = EvaluateAndApplyPolicy(enemyState, 0);
        float policyExpansion = Mathf.Max(policy.hitboxExpansionRatio, 1f);
        float priorAttack = CombatManager.Instance ? CombatManager.Instance.priorAttack : PriorAttackFromState(enemyState);
        float priorDodge = CombatManager.Instance ? CombatManager.Instance.priorDodge : PriorDodgeFromState(enemyState);
        float attackScreenRadius = CalculateDynamicRadius(priorAttack) * policyExpansion;
        float dodgeScreenRadius = CalculateDynamicRadius(priorDodge) * policyExpansion;
        
        float scaleFactor = mainCanvas ? mainCanvas.scaleFactor : 1f;
        
        float attackUiSize = (attackScreenRadius * 2f) / scaleFactor;
        float dodgeUiSize = (dodgeScreenRadius * 2f) / scaleFactor;
        
        if (attackHitboxVisualizer) attackHitboxVisualizer.sizeDelta = new Vector2(attackUiSize, attackUiSize);
        if (dodgeHitboxVisualizer) dodgeHitboxVisualizer.sizeDelta = new Vector2(dodgeUiSize, dodgeUiSize);

        // for unity editor testing
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
        
        // for mobile touch
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
        if (!visualAttackButton || !visualDodgeButton)
        {
            Debug.LogWarning("[ADUI] Attack/Dodge button references are missing.");
            return;
        }

        if (useActionFirstDecoder && bayesianDecoder)
        {
            ProcessActionFirstInput(inputPos);
            return;
        }

        // get the screen-space center positions directly
        Vector2 attackCenter = visualAttackButton.rectTransform.position;
        Vector2 dodgeCenter = visualDodgeButton.rectTransform.position;

        // calculate Spatial Likelihood P(I|A)
        float distToAttack = Vector2.Distance(inputPos, attackCenter);
        float distToDodge = Vector2.Distance(inputPos, dodgeCenter);

        // Gaussian spatial likelihood P(I|A) calculation
        float likelihoodAttack = CalculateGaussianLikelihood(distToAttack, userTouchVariance);
        float likelihoodDodge = CalculateGaussianLikelihood(distToDodge, userTouchVariance);
        
        // contextual prior P(A) from Game State
        float priorAttack = CombatManager.Instance.priorAttack;
        float priorDodge = CombatManager.Instance.priorDodge;
        
        // augmented Bayesian Decoder: P(A|I) ∝ P(I|A) * P(A)
        float posteriorAttack = likelihoodAttack * priorAttack;
        float posteriorDodge = likelihoodDodge * priorDodge;
        
        // dynamic threshold
        float effectiveThreshold = minLikelihoodThreshold * 0.5f;
        
        if (posteriorAttack < effectiveThreshold && posteriorDodge < effectiveThreshold)
        {
            Debug.Log($"<color=grey>[Invalid Touch]</color> Posterior likelihood out of threshold({effectiveThreshold}). (Attack posterior: {posteriorAttack:F4}, Dodge posterior: {posteriorDodge:F4})");
            return;
        }
        
        string logContext = CombatManager.Instance.currentState == CombatManager.CombatState.Safe ? "Safe" : "Urgent";

        // 5. Execute Action and Apply Visual Feedback
        if (posteriorAttack > posteriorDodge)
        {
            Debug.Log($"<color=red>[Action Executed] ATTACK!</color> (Enemy State: {logContext})");
            visualAttackButton.color = pressedColor;
            CombatManager.Instance.OnPlayerAttack();
        }
        else
        {
            Debug.Log($"<color=blue>[Action Executed] DODGE!</color> (Enemy State: {logContext})");
            visualDodgeButton.color = pressedColor;
            CombatManager.Instance.OnPlayerDodge();
        }
    }

    private void ProcessInputEnded()
    {
        if (uiAdjustmentController && lastPolicy != null)
        {
            uiAdjustmentController.ApplyPolicy(lastPolicy);
            return;
        }
        if (visualAttackButton) visualAttackButton.color = normalColor;
        if (visualDodgeButton) visualDodgeButton.color = normalColor;
    }

    private float CalculateGaussianLikelihood(float distance, float variance)
    {
        float safeVariance = Mathf.Max(variance, 0.1f); 
        return Mathf.Exp(-(distance * distance) / (2 * safeVariance * safeVariance));
    }
    
    private float CalculateDynamicRadius(float prior)
    {
        // adjust threshold according to the current prior probability; neutral: 0.5
        float effectiveThresholdRatio = (minLikelihoodThreshold * 0.5f) / prior;
        
        // clamp to prevent log NAN
        effectiveThresholdRatio = Mathf.Clamp(effectiveThresholdRatio, 0.0001f, 0.999f);
        
        // Inverse Gaussian
        return userTouchVariance * Mathf.Sqrt(-2f * Mathf.Log(effectiveThresholdRatio));
    }

    private void ProcessActionFirstInput(Vector2 inputPos)
    {
        var trialId = trialScenarioManager ? trialScenarioManager.currentTrialId : 0;
        var condition = trialScenarioManager ? trialScenarioManager.CurrentCondition() : DatasetSchema.ConditionContextBayesianSafety;
        var phase = trialScenarioManager ? trialScenarioManager.currentPhase : DatasetSchema.PhaseFreeplay;
        var enemyState = trialScenarioManager ? trialScenarioManager.CurrentEnemyState() : CurrentEnemyStateFromCombat();
        var policy = EvaluateAndApplyPolicy(enemyState, trialId);
        ApplyPolicyToDecoder(policy);
        ApplyVisionPriorToDecoder(trialId);

        var attackButton = ADUIButtonGeometry.FromRect("Attack", visualAttackButton.rectTransform, expandedHitboxRatio);
        var dodgeButton = ADUIButtonGeometry.FromRect("Dodge", visualDodgeButton.rectTransform, expandedHitboxRatio);
        attackButton.hitboxRadius = Mathf.Max(attackButton.visualRadius * policy.hitboxExpansionRatio, attackButton.visualRadius);
        dodgeButton.hitboxRadius = Mathf.Max(dodgeButton.visualRadius * policy.hitboxExpansionRatio, dodgeButton.visualRadius);

        if (sessionManager) sessionManager.EnsureSession();
        if (rawTouchLogger) rawTouchLogger.Log(trialId, inputPos, "Began");

        var decodeInput = new ADUIDecodeInput
        {
            touchPosition = inputPos,
            attackButton = attackButton,
            dodgeButton = dodgeButton,
            enemyState = enemyState,
            condition = condition
        };

        var result = bayesianDecoder.Decode(decodeInput);
        if (bayesianDecoder) bayesianDecoder.ClearExternalActionPrior();
        result = ApplyConditionPolicy(condition, decodeInput, result);
        LastDecodeResult = result;

        var dynamicAttackRadius = bayesianDecoder.DynamicRadius(result.varianceAttack, result.priorAttack);
        var dynamicDodgeRadius = bayesianDecoder.DynamicRadius(result.varianceDodge, result.priorDodge);
        if (buttonLayoutLogger) buttonLayoutLogger.Log(trialId, attackButton, dodgeButton, dynamicAttackRadius, dynamicDodgeRadius);
        if (bayesianDecisionLogger) bayesianDecisionLogger.Log(trialId, condition, enemyState, result);
        if (modePolicyLogger) modePolicyLogger.Log(trialId, lastDemand, policy);

        if (phase == DatasetSchema.PhaseCalibration && userTouchModel && trialScenarioManager)
        {
            var intended = ParseAction(trialScenarioManager.currentIntendedAction);
            if (intended == ADUIAction.Attack) userTouchModel.AddCalibrationSample(intended, inputPos, attackButton);
            if (intended == ADUIAction.Dodge) userTouchModel.AddCalibrationSample(intended, inputPos, dodgeButton);
            if (sessionManager && sessionManager.HasActiveSession)
            {
                userTouchModel.SaveProfile(System.IO.Path.Combine(sessionManager.sessionDirectory, "user_touch_profile.json"));
            }
        }

        var playerHpBefore = CombatManager.Instance ? CombatManager.Instance.CurrentPlayerHP : 0;
        var enemyHpBefore = CombatManager.Instance ? CombatManager.Instance.CurrentEnemyHP : 0;

        if (result.invalidTouch || result.finalExecutedAction == ADUIAction.None)
        {
            Debug.Log($"<color=grey>[Invalid Touch]</color> {result.safetyGateReason}");
            if (aduiFeedbackController) aduiFeedbackController.ShowDecisionFeedback(result, policy);
            LogTrial(decodeInput, result, playerHpBefore, enemyHpBefore, playerHpBefore, enemyHpBefore, dynamicAttackRadius, dynamicDodgeRadius);
            if (demandModel) demandModel.UpdateRecentErrorSignals(true, false);
            if (trialScenarioManager) trialScenarioManager.CompleteTrial();
            return;
        }

        ExecuteAction(result.finalExecutedAction);

        var playerHpAfter = CombatManager.Instance ? CombatManager.Instance.CurrentPlayerHP : playerHpBefore;
        var enemyHpAfter = CombatManager.Instance ? CombatManager.Instance.CurrentEnemyHP : enemyHpBefore;
        var required = trialScenarioManager ? trialScenarioManager.currentRequiredAction : "";
        var actionSuccess = string.IsNullOrEmpty(required) || result.finalExecutedAction.ToString() == required;
        if (hpOutcomeLogger) hpOutcomeLogger.Log(trialId, playerHpBefore, playerHpAfter, enemyHpBefore, enemyHpAfter, actionSuccess);
        if (aduiFeedbackController) aduiFeedbackController.ShowDecisionFeedback(result, policy);
        if (demandModel) demandModel.UpdateRecentErrorSignals(result.invalidTouch, result.visualBoundaryPrediction.ToString() == required && result.finalExecutedAction.ToString() != required);

        LogTrial(decodeInput, result, playerHpBefore, enemyHpBefore, playerHpAfter, enemyHpAfter, dynamicAttackRadius, dynamicDodgeRadius);
        if (trialScenarioManager) trialScenarioManager.CompleteTrial();
    }

    private ADUIDecodeResult ApplyConditionPolicy(string condition, ADUIDecodeInput input, ADUIDecodeResult result)
    {
        if (condition == DatasetSchema.ConditionVisualBoundary)
        {
            result.finalExecutedAction = result.visualBoundaryPrediction;
            result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
            result.safetyGateReason = result.invalidTouch ? "visual_boundary_no_hit" : "visual_boundary_baseline";
            return result;
        }
        if (condition == DatasetSchema.ConditionExpandedHitbox)
        {
            result.finalExecutedAction = result.expandedHitboxPrediction;
            result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
            result.safetyGateReason = result.invalidTouch ? "expanded_hitbox_no_hit" : "expanded_hitbox_baseline";
            return result;
        }
        if (condition == DatasetSchema.ConditionUserGaussian)
        {
            result.finalExecutedAction = result.userGaussianPrediction;
            result.safetyGateReason = "user_gaussian_baseline";
            return result;
        }
        if (condition == DatasetSchema.ConditionContextPriorOnly)
        {
            result.finalExecutedAction = result.contextPriorOnlyPrediction;
            result.safetyGateReason = "context_prior_only_baseline";
            return result;
        }
        if (condition == DatasetSchema.ConditionContextBayesianNoSafety)
        {
            result.finalExecutedAction = result.bayesianPrediction;
            result.safetyGateReason = result.invalidTouch ? "bayesian_no_safety_invalid_likelihood" : "bayesian_no_safety";
            return result;
        }
        if (safetyGate && safetyGate.enabled)
        {
            return safetyGate.Apply(input, result);
        }
        result.finalExecutedAction = result.visualBoundaryPrediction != ADUIAction.None ? result.visualBoundaryPrediction : ADUIAction.None;
        result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
        result.safetyGatePassed = false;
        result.safetyGateReason = result.invalidTouch ? "correction_disabled_invalid_touch" : "correction_disabled_visual_boundary";
        return result;
    }

    private ADUIAdjustmentPolicy EvaluateAndApplyPolicy(ADUIEnemyState enemyState, int trialId)
    {
        var normalizedHp = 1f;
        if (CombatManager.Instance && CombatManager.Instance.playerMaxHP > 0)
        {
            normalizedHp = Mathf.Clamp01((float)CombatManager.Instance.CurrentPlayerHP / CombatManager.Instance.playerMaxHP);
        }
        var recentActionRate = trialScenarioManager && trialScenarioManager.currentPhase == DatasetSchema.PhaseTest ? 0.8f : 0.35f;
        lastDemand = demandModel ? demandModel.Evaluate(enemyState, IsDangerState(enemyState), normalizedHp, recentActionRate) : DefaultDemand(enemyState);
        lastPolicy = policyEngine ? policyEngine.BuildPolicy(lastDemand) : DefaultPolicy(lastDemand);
        if (userCorrectionSettings) userCorrectionSettings.ApplyPolicy(lastPolicy);
        if (uiAdjustmentController) uiAdjustmentController.ApplyPolicy(lastPolicy);
        expandedHitboxRatio = Mathf.Max(lastPolicy.hitboxExpansionRatio, 1f);
        return lastPolicy;
    }

    private void ApplyPolicyToDecoder(ADUIAdjustmentPolicy policy)
    {
        if (!bayesianDecoder || policy == null) return;
        bayesianDecoder.tau = Mathf.Lerp(0.7f, 0.45f, policy.correctionStrength);
        bayesianDecoder.delta = Mathf.Lerp(0.22f, 0.08f, policy.correctionStrength);
        bayesianDecoder.priorStrength = Mathf.Lerp(0.6f, 1.4f, policy.correctionStrength);
        bayesianDecoder.ambiguityMarginPx = Mathf.Max(policy.ambiguityMarginPx, 0f);
        if (safetyGate)
        {
            safetyGate.preserveClearInsideButtonInput = policy.preserveClearInput;
            safetyGate.recoverableRadiusMultiplier = Mathf.Lerp(1.25f, 2.1f, policy.interactionErrorTolerance);
        }
    }

    private void ApplyVisionPriorToDecoder(int trialId)
    {
        if (!bayesianDecoder) return;
        if (!useMultigameVisionPrior)
        {
            bayesianDecoder.ClearExternalActionPrior();
            return;
        }

        ADUIVisionSituation situation;
        var hasReplay = visionPriorReplayLoader && visionPriorReplayLoader.TryGetSituation(trialId, out situation);
        if (!hasReplay)
        {
            situation = visionInferenceStub ? visionInferenceStub.PredictNeutral() : new ADUIVisionSituation
            {
                threatLevel = ADUIThreatLevel.Unknown,
                actionWindow = ADUIActionWindow.Unknown,
                confidence = 0f,
                source = "no_vision_prediction_neutral"
            };
        }

        var builder = visionPriorBuilder ? visionPriorBuilder : GetComponent<VisionPriorBuilder>();
        if (!builder)
        {
            bayesianDecoder.ClearExternalActionPrior();
            return;
        }

        var prior = builder.BuildPrior(situation);
        bayesianDecoder.SetExternalActionPrior(prior.attack, prior.dodge, prior.source + ":" + prior.reason);
    }

    private float PriorAttackFromState(ADUIEnemyState enemyState)
    {
        if (bayesianDecoder) return bayesianDecoder.PriorForState(enemyState).x;
        return IsDangerState(enemyState) ? 0.1f : 0.9f;
    }

    private float PriorDodgeFromState(ADUIEnemyState enemyState)
    {
        if (bayesianDecoder) return bayesianDecoder.PriorForState(enemyState).y;
        return IsDangerState(enemyState) ? 0.9f : 0.1f;
    }

    private ADUIInteractionDemand DefaultDemand(ADUIEnemyState enemyState)
    {
        var demand = new ADUIInteractionDemand();
        demand.temporalUrgency = IsDangerState(enemyState) ? 0.85f : 0.3f;
        demand.actionIntensity = IsDangerState(enemyState) ? 0.75f : 0.35f;
        demand.informationPriority = IsDangerState(enemyState) ? 0.75f : 0.45f;
        demand.occlusionRisk = 0.45f;
        demand.controlContinuity = 0.55f;
        demand.uiSkill = 0.7f;
        demand.mode = IsDangerState(enemyState) ? ADUIInteractionMode.ActionFirst : ADUIInteractionMode.LearningReview;
        return demand;
    }

    private ADUIAdjustmentPolicy DefaultPolicy(ADUIInteractionDemand demand)
    {
        var policy = new ADUIAdjustmentPolicy();
        policy.mode = demand.mode;
        policy.interactionErrorTolerance = demand.ErrorToleranceNeed;
        policy.hitboxExpansionRatio = demand.mode == ADUIInteractionMode.ActionFirst ? 1.25f : 1.1f;
        policy.correctionStrength = demand.mode == ADUIInteractionMode.ActionFirst ? 0.55f : 0.25f;
        policy.feedbackIntensity = 0.45f;
        policy.policyReason = "default_policy";
        return policy;
    }

    private bool IsDangerState(ADUIEnemyState enemyState)
    {
        return enemyState == ADUIEnemyState.Telegraph || enemyState == ADUIEnemyState.Attacking || enemyState == ADUIEnemyState.Urgent;
    }

    private void ExecuteAction(ADUIAction action)
    {
        if (action == ADUIAction.Attack)
        {
            Debug.Log("<color=red>[Action Executed] ATTACK!</color>");
            visualAttackButton.color = pressedColor;
            if (CombatManager.Instance) CombatManager.Instance.OnPlayerAttack();
        }
        else if (action == ADUIAction.Dodge)
        {
            Debug.Log("<color=blue>[Action Executed] DODGE!</color>");
            visualDodgeButton.color = pressedColor;
            if (CombatManager.Instance) CombatManager.Instance.OnPlayerDodge();
        }
    }

    private void LogTrial(
        ADUIDecodeInput input,
        ADUIDecodeResult result,
        int playerHpBefore,
        int enemyHpBefore,
        int playerHpAfter,
        int enemyHpAfter,
        float dynamicAttackRadius,
        float dynamicDodgeRadius)
    {
        if (!sessionManager || !sessionManager.exporter) return;
        var trialId = trialScenarioManager ? trialScenarioManager.currentTrialId : 0;
        var phase = trialScenarioManager ? trialScenarioManager.currentPhase : DatasetSchema.PhaseFreeplay;
        var startMs = trialScenarioManager ? trialScenarioManager.currentTrialStartMs : System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var requiredAction = trialScenarioManager ? trialScenarioManager.currentRequiredAction : "";
        var intendedAction = trialScenarioManager ? trialScenarioManager.currentIntendedAction : "";
        var labelSource = trialScenarioManager ? trialScenarioManager.currentLabelSource : "unavailable";
        var condition = trialScenarioManager ? trialScenarioManager.CurrentCondition() : input.condition;

        var record = new ADUITrialRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            block_id = trialScenarioManager ? trialScenarioManager.currentBlockId : 0,
            phase = phase,
            condition = condition,
            interaction_mode = lastPolicy != null ? lastPolicy.mode.ToString() : "",
            timestamp_trial_start_ms = startMs,
            timestamp_touch_ms = now,
            timestamp_action_ms = now,
            timestamp_trial_end_ms = now,
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
            dynamic_attack_radius = dynamicAttackRadius,
            dynamic_dodge_radius = dynamicDodgeRadius,
            enemy_state = input.enemyState.ToString(),
            danger_warning_visible = input.enemyState == ADUIEnemyState.Telegraph || input.enemyState == ADUIEnemyState.Attacking || input.enemyState == ADUIEnemyState.Urgent,
            enemy_distance = 0f,
            player_hp_before = playerHpBefore,
            enemy_hp_before = enemyHpBefore,
            cooldown_attack = 0f,
            cooldown_dodge = 0f,
            required_action = requiredAction,
            intended_action = intendedAction,
            label_source = labelSource,
            touch_x = input.touchPosition.x,
            touch_y = input.touchPosition.y,
            touch_phase = "Began",
            touch_pressure = 0f,
            touch_radius = 0f,
            distance_to_attack = result.distanceToAttack,
            distance_to_dodge = result.distanceToDodge,
            relative_attack_x = (input.touchPosition.x - input.attackButton.centerX) / Mathf.Max(input.attackButton.visualRadius, 1f),
            relative_attack_y = (input.touchPosition.y - input.attackButton.centerY) / Mathf.Max(input.attackButton.visualRadius, 1f),
            relative_dodge_x = (input.touchPosition.x - input.dodgeButton.centerX) / Mathf.Max(input.dodgeButton.visualRadius, 1f),
            relative_dodge_y = (input.touchPosition.y - input.dodgeButton.centerY) / Mathf.Max(input.dodgeButton.visualRadius, 1f),
            is_inside_attack_visual = result.distanceToAttack <= input.attackButton.visualRadius,
            is_inside_dodge_visual = result.distanceToDodge <= input.dodgeButton.visualRadius,
            is_inside_attack_expanded = result.distanceToAttack <= input.attackButton.hitboxRadius,
            is_inside_dodge_expanded = result.distanceToDodge <= input.dodgeButton.hitboxRadius,
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
            action_success = string.IsNullOrEmpty(requiredAction) || result.finalExecutedAction.ToString() == requiredAction,
            hp_after = playerHpAfter,
            enemy_hp_after = enemyHpAfter,
            damage_taken = Mathf.Max(0, playerHpBefore - playerHpAfter),
            damage_dealt = Mathf.Max(0, enemyHpBefore - enemyHpAfter),
            survived = playerHpAfter > 0,
            cooldown_wasted = result.finalExecutedAction == ADUIAction.Dodge && requiredAction == "Attack",
            reaction_time_ms = now - startMs,
            feedback_type = CombatManager.Instance ? CombatManager.Instance.LastFeedbackMessage : "",
            button_feedback_color = CombatManager.Instance ? CombatManager.Instance.LastFeedbackColor.ToString() : "",
            feedback_message = aduiFeedbackController ? aduiFeedbackController.LastFeedbackMessage : "",
            haptic_feedback_triggered = aduiFeedbackController && aduiFeedbackController.LastHapticTriggered,
            hitbox_visualization_enabled = attackHitboxVisualizer && attackHitboxVisualizer.gameObject.activeInHierarchy,
            demand_action_intensity = lastDemand != null ? lastDemand.actionIntensity : 0f,
            demand_temporal_urgency = lastDemand != null ? lastDemand.temporalUrgency : 0f,
            demand_information_priority = lastDemand != null ? lastDemand.informationPriority : 0f,
            demand_occlusion_risk = lastDemand != null ? lastDemand.occlusionRisk : 0f,
            demand_control_continuity = lastDemand != null ? lastDemand.controlContinuity : 0f,
            demand_ui_skill = lastDemand != null ? lastDemand.uiSkill : 0f,
            policy_visibility = lastPolicy != null ? lastPolicy.visibility : 0f,
            policy_emphasis = lastPolicy != null ? lastPolicy.emphasis : 0f,
            policy_density = lastPolicy != null ? lastPolicy.density : 0f,
            policy_position_constraint = lastPolicy != null ? lastPolicy.positionConstraint : 0f,
            policy_error_tolerance = lastPolicy != null ? lastPolicy.interactionErrorTolerance : 0f,
            policy_feedback_intensity = lastPolicy != null ? lastPolicy.feedbackIntensity : 0f,
            policy_correction_strength = lastPolicy != null ? lastPolicy.correctionStrength : 0f,
            policy_hitbox_expansion_ratio = lastPolicy != null ? lastPolicy.hitboxExpansionRatio : expandedHitboxRatio,
            policy_ambiguity_margin_px = lastPolicy != null ? lastPolicy.ambiguityMarginPx : 0f,
            policy_preserve_clear_input = lastPolicy == null || lastPolicy.preserveClearInput,
            policy_haptic_enabled = lastPolicy != null && lastPolicy.hapticEnabled,
            policy_guidance_visible = lastPolicy != null && lastPolicy.showGuidance,
            policy_review_visible = lastPolicy != null && lastPolicy.showReview,
            policy_reason = lastPolicy != null ? lastPolicy.policyReason : "",
            user_correction_enabled = !userCorrectionSettings || userCorrectionSettings.correctionEnabled,
            user_correction_strength = userCorrectionSettings ? userCorrectionSettings.correctionStrength : 0.5f
        };
        var fileName = phase == DatasetSchema.PhaseCalibration ? "calibration_trials.jsonl" : "main_trials.jsonl";
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), fileName, record);
    }

    private ADUIEnemyState CurrentEnemyStateFromCombat()
    {
        if (!CombatManager.Instance) return ADUIEnemyState.Neutral;
        switch (CombatManager.Instance.currentState)
        {
            case CombatManager.CombatState.Safe:
                return ADUIEnemyState.Safe;
            case CombatManager.CombatState.Telegraph:
                return ADUIEnemyState.Telegraph;
            case CombatManager.CombatState.Attacking:
                return ADUIEnemyState.Attacking;
            default:
                return ADUIEnemyState.Neutral;
        }
    }

    private ADUIAction ParseAction(string action)
    {
        if (action == "Attack") return ADUIAction.Attack;
        if (action == "Dodge") return ADUIAction.Dodge;
        return ADUIAction.None;
    }
}

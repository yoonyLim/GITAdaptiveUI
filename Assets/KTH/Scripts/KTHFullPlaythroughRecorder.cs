using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KTHFullPlaythroughRecorder : MonoBehaviour
{
    public string outputDir = "";
    public int captureFps = 15;
    public int captureWidth = 1280;
    public int captureHeight = 720;
    public string requestedCondition = "calibrated";
    public float maxPlaySeconds = 240f;
    public bool simulateHighTouchErrorPressure = true;
    public bool runContextShowcaseAfterCalibration = true;
    public bool stopAfterContextShowcase;
    public float contextShowcaseHoldSeconds = 2.25f;
    public bool compactRecorderOverlay = true;
    public bool showResearchOverlayInRecording;
    public bool forceGameOverCheck;
    public float calibrationReadyPreviewSeconds = 0.2f;
    public float calibrationPostTouchSeconds = 0.28f;
    [Range(0.4f, 1.6f)]
    public float edgeTouchRadiusMultiplier = 1.08f;

    private AdaptiveTouchManager touchManager;
    private FourButtonCalibrationFlow calibrationFlow;
    private UserContextPriorModel contextPriorModel;
    private CombatManager combatManager;
    private RoguelikeGameManager gameManager;
    private PlayerController playerController;
    private AdaptiveGameHudController gameHud;
    private ExperimentSessionManager sessionManager;
    private MethodInfo processInputBegan;
    private MethodInfo processInputEnded;
    private CanvasGroup overlayGroup;
    private TextMeshProUGUI overlayText;
    private RectTransform touchMarker;
    private int frameIndex;
    private float nextCaptureTime;
    private float nextActionTime;
    private float nextDodgeAllowedTime;
    private float maxAbsPlayerX;
    private float maxAbsPlayerY;
    private int actionTouchIndex;
    private string currentCase = "Boot";
    private string currentAction = "waiting";
    private string lastDecisionLine = "";
    private Vector2 currentTouch;
    private bool markerVisible;
    private string copiedSessionLogBundlePath = "";

    private void OnEnable()
    {
        Application.logMessageReceived += OnLogMessage;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessage;
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Application.dataPath, "..", "outputs", "unity_recordings", "full_playthrough");
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "frames"));

        Application.runInBackground = true;
        Screen.SetResolution(captureWidth, captureHeight, false);
        Application.targetFrameRate = 60;
        Time.captureFramerate = 0;

        StartCoroutine(RunPlaythrough());
    }

    private void Update()
    {
        UpdateOverlay();
    }

    private IEnumerator RunPlaythrough()
    {
        yield return WaitForPrototype();
        ResolveReferences();
        ConfigureManagers();
        SetupOverlay();

        yield return CaptureForSeconds(1.0f, "Start screen");

        string normalizedCondition = NormalizedCondition();
        if (calibrationFlow != null)
        {
            if (normalizedCondition == "raw")
            {
                currentCase = "Condition: raw fixed button";
                calibrationFlow.BeginRawGame();
                yield return WaitForGameStartedOrTimeout();
            }
            else if (normalizedCondition == "no_calibration")
            {
                currentCase = "Condition: adaptive without calibration";
                calibrationFlow.BeginGameWithoutCalibration();
                yield return WaitForGameStartedOrTimeout();
            }
            else
            {
                currentCase = "Condition: calibrated adaptive";
                if (runContextShowcaseAfterCalibration)
                {
                    calibrationFlow.autoStartGameAfterCalibration = false;
                }

                calibrationFlow.BeginCalibration();
                yield return AutoRunFourButtonCalibration();
                if (runContextShowcaseAfterCalibration)
                {
                    yield return RunContextScenarioShowcase();
                    if (stopAfterContextShowcase)
                    {
                        markerVisible = false;
                        yield return CaptureForSeconds(0.5f, "Context showcase recording complete");
                        WriteDoneFile();
                        QuitApplication();
                        yield break;
                    }

                    gameManager?.BeginPrototype();
                    calibrationFlow.HideCalibrationPrompt();
                }
                else
                {
                    yield return WaitForGameStartedOrTimeout();
                }
            }
        }
        else
        {
            gameManager?.BeginPrototype();
            calibrationFlow?.HideCalibrationPrompt();
        }

        if (forceGameOverCheck)
        {
            ResolveReferences();
            yield return CaptureForSeconds(0.75f, "Game over check: before lethal damage");
            if (playerController != null)
            {
                playerController.TakeDamage(Mathf.Max(1, playerController.CurrentHP));
            }

            yield return CaptureForSeconds(2.0f, "Game over check");
            WriteDoneFile();
            QuitApplication();
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + maxPlaySeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            ResolveReferences();
            if (IsPrototypeEnded())
            {
                break;
            }

            if (gameManager != null && !gameManager.IsStageRunning)
            {
                yield return CaptureForSeconds(0.1f, "Waiting for next stage");
                continue;
            }

            DriveMovementAndAction();
            yield return CaptureTick();
        }

        if (playerController != null)
        {
            playerController.ClearVirtualMoveInput();
        }

        markerVisible = false;
        yield return CaptureForSeconds(2.0f, IsPrototypeComplete() ? "Full playthrough complete" : IsPrototypeFailed() ? "Game over" : "Full playthrough timeout");

        WriteDoneFile();
        QuitApplication();
    }

    private void WriteDoneFile()
    {
        CopyFinalLogsToOutputDir();
        File.WriteAllText(
            Path.Combine(outputDir, "done.txt"),
            $"frames={frameIndex}{Environment.NewLine}" +
            $"condition={NormalizedCondition()}{Environment.NewLine}" +
            $"complete={IsPrototypeComplete()}{Environment.NewLine}" +
            $"failed={IsPrototypeFailed()}{Environment.NewLine}" +
            $"stage={(gameManager != null ? gameManager.CurrentStage : -1)}{Environment.NewLine}" +
            $"hp={(playerController != null ? playerController.CurrentHP : -1)}{Environment.NewLine}" +
            $"sessionLogBundle={copiedSessionLogBundlePath}{Environment.NewLine}" +
            $"maxAbsPlayerX={maxAbsPlayerX:F2}{Environment.NewLine}" +
            $"maxAbsPlayerY={maxAbsPlayerY:F2}{Environment.NewLine}");
    }

    private void CopyFinalLogsToOutputDir()
    {
        ResolveReferences();
        if (sessionManager == null)
        {
            return;
        }

        string state = IsPrototypeComplete() ? "complete" : IsPrototypeFailed() ? "failed" : "timeout_or_stopped";
        string sourcePath = sessionManager.WriteFinalLogBundle(state, gameManager != null ? gameManager.CurrentStage : -1);
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(outputDir);
        copiedSessionLogBundlePath = Path.Combine(outputDir, "final_session_logs.json");
        File.Copy(sourcePath, copiedSessionLogBundlePath, true);
    }

    private void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    private IEnumerator WaitForPrototype()
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ResolveReferences();
            if (touchManager != null && combatManager != null && gameManager != null && playerController != null)
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("[KTH] Full playthrough recorder timed out while waiting for AdaptivePrototype.");
    }

    private IEnumerator AutoRunFourButtonCalibration()
    {
        if (calibrationFlow == null)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup +
                         Mathf.Max(42f, calibrationFlow.CalibrationTotalCount * 1.8f + 18f);
        int observedTrial = -1;
        while (calibrationFlow.CalibrationActive && Time.realtimeSinceStartup < deadline)
        {
            currentCase = CurrentCalibrationLabel();

            if (observedTrial != calibrationFlow.CurrentTrialIndex)
            {
                observedTrial = calibrationFlow.CurrentTrialIndex;
                markerVisible = false;
                yield return CaptureUntilCalibrationReady(observedTrial);
            }

            currentTouch = CalibrationTouchPosition();
            markerVisible = true;
            yield return CaptureForSeconds(calibrationReadyPreviewSeconds, currentCase);
            calibrationFlow.SubmitCalibrationTouch(currentTouch);
            yield return CaptureForSeconds(calibrationPostTouchSeconds, currentCase);
            markerVisible = false;
        }

        markerVisible = false;
    }

    private IEnumerator CaptureUntilCalibrationReady(int trialIndex)
    {
        while (calibrationFlow != null &&
               calibrationFlow.CalibrationActive &&
               calibrationFlow.CurrentTrialIndex == trialIndex &&
               !calibrationFlow.CurrentTrialAcceptsInput)
        {
            currentCase = CurrentCalibrationLabel();
            yield return CaptureTick();
        }
    }

    private IEnumerator RunContextScenarioShowcase()
    {
        if (gameManager == null)
        {
            yield break;
        }

        ADUIContextScenario[] showcaseScenarios =
        {
            ADUIContextScenario.AttackCommitWindow,
            ADUIContextScenario.PreDodgeWindow,
            ADUIContextScenario.ImmediateDodgeThreat,
            ADUIContextScenario.ProjectileDodgeThreat,
            ADUIContextScenario.MovingUnderPressure,
            ADUIContextScenario.LowHpThreat
        };

        yield return CaptureForSeconds(0.4f, "Context showcase setup");
        for (int i = 0; i < showcaseScenarios.Length; i++)
        {
            ADUIContextScenario scenario = showcaseScenarios[i];
            gameManager.BeginCalibrationScenario(scenario, false);
            CombatManager.Instance?.ForceRefreshCombatContext();
            currentAction = $"hold={UserContextPriorModel.DefaultResponseForScenario(scenario)}";
            string label = $"Context showcase {i + 1}/{showcaseScenarios.Length}: {UserContextPriorModel.ScenarioLabel(scenario)}";
            yield return CaptureForSeconds(Mathf.Max(1.8f, contextShowcaseHoldSeconds), label);
        }

        gameManager.BeginModeShowcaseScenario(ADUIInteractionMode.CognitiveFirst);
        CombatManager.Instance?.ForceRefreshCombatContext();
        currentAction = "hold=ReadState";
        yield return CaptureForSeconds(Mathf.Max(2.0f, contextShowcaseHoldSeconds), "Mode showcase: CognitiveFirst information state");

        gameManager.ClearCalibrationScenario();
        CombatManager.Instance?.ForceRefreshCombatContext();
        yield return CaptureForSeconds(0.45f, "Context showcase complete");
    }

    private IEnumerator WaitForGameStartedOrTimeout()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ResolveReferences();
            if (gameManager != null && gameManager.IsStageRunning)
            {
                yield break;
            }

            yield return CaptureForSeconds(0.1f, "캘리브레이션 완료 - 게임 시작 대기");
        }
    }

    private void DriveMovementAndAction()
    {
        if (gameManager == null || playerController == null || combatManager == null)
        {
            return;
        }

        EnemyControllerBase target = SelectTarget();
        Vector2 moveInput = ChooseMoveInput(target);
        playerController.SetVirtualMoveInput(moveInput);

        if (Time.time < nextActionTime)
        {
            currentCase = $"Stage {gameManager.CurrentStage}: moving";
            currentAction = $"move=({moveInput.x:0.00},{moveInput.y:0.00})";
            return;
        }

        CombatManager.CombatContext context = combatManager.CurrentContext;
        bool immediatePressure = context.immediateThreats > 0 || context.projectileThreats > 0;
        bool preDodgeWindow = context.preDodgeEnemies > 0 || context.movingTowardDangerEnemies > 0;
        bool attackCommitWindow = context.attackCommitTargets > 0 && !immediatePressure && !preDodgeWindow;
        bool threatPresent = immediatePressure || preDodgeWindow;
        bool safeAttackWindow = attackCommitWindow &&
                                context.attackOpportunityScore >= context.dodgeUrgencyScore;
        bool targetInReach = target != null &&
                             target.DistanceToPlayer <= playerController.attackRange + 0.45f;
        bool farFromTarget = target != null &&
                             target.DistanceToPlayer > playerController.attackRange + 1.4f;
        string action = "";
        if (playerController.CanHeal && playerController.CurrentHP <= 35)
        {
            action = "Heal";
        }
        else if (playerController.CanWhirlwind && context.whirlwindTargets >= 3)
        {
            action = "Whirlwind";
        }
        else if (immediatePressure)
        {
            if (Time.time >= nextDodgeAllowedTime &&
                (!farFromTarget || playerController.CurrentHP <= 42 || immediatePressure))
            {
                action = "Dodge";
            }
        }
        else if (preDodgeWindow)
        {
            if (Time.time >= nextDodgeAllowedTime)
            {
                action = "Dodge";
            }
            else if (targetInReach)
            {
                action = "Attack";
            }
        }
        else if (playerController.CanWhirlwind && (context.whirlwindTargets >= 2 || context.closeEnemies >= 4))
        {
            action = "Whirlwind";
        }
        else if (playerController.CanHeal && playerController.CurrentHP <= 58)
        {
            action = "Heal";
        }
        else if (targetInReach || safeAttackWindow)
        {
            action = "Attack";
        }
        else if (playerController.CanWhirlwind && context.whirlwindTargets >= 1 && gameManager.CurrentStage == 3)
        {
            action = "Whirlwind";
        }

        if (string.IsNullOrEmpty(action) && targetInReach && !threatPresent)
        {
            action = "Attack";
        }

        if (!string.IsNullOrEmpty(action))
        {
            TapAction(action);
            currentAction = simulateHighTouchErrorPressure
                ? $"tap={action} edge-biased"
                : $"tap={action}";
            if (string.Equals(action, "Dodge", StringComparison.OrdinalIgnoreCase))
            {
                nextDodgeAllowedTime = Time.time + 1.75f;
            }

            nextActionTime = Time.time + (action == "Attack" ? 0.2f : action == "Dodge" ? 0.82f : 0.48f);
        }
        else
        {
            currentAction = $"move=({moveInput.x:0.00},{moveInput.y:0.00})";
        }

        currentCase = $"Stage {gameManager.CurrentStage}: full playthrough";
    }

    private EnemyControllerBase SelectTarget()
    {
        if (gameManager == null || gameManager.ActiveEnemies == null || gameManager.ActiveEnemies.Count == 0)
        {
            return null;
        }

        EnemyControllerBase best = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < gameManager.ActiveEnemies.Count; i++)
        {
            EnemyControllerBase enemy = gameManager.ActiveEnemies[i];
            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            float score = -enemy.DistanceToPlayer;
            if (enemy.enemyKind == EnemyKind.Ranged)
            {
                score += 1.2f;
            }
            else if (enemy.enemyKind == EnemyKind.Boss)
            {
                score += gameManager.CurrentStage == 3 ? 0.6f : 0f;
            }

            if (enemy.IsTelegraphing || enemy.IsAttacking)
            {
                score += 0.75f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }

    private Vector2 ChooseMoveInput(EnemyControllerBase target)
    {
        if (playerController == null || combatManager == null)
        {
            return Vector2.zero;
        }

        Vector2 playerPosition = playerController.transform.position;
        CombatManager.CombatContext context = combatManager.CurrentContext;

        if (playerController.CurrentHP <= 35 || context.attackingEnemies > 0)
        {
            Vector2 away = AwayFromCluster(playerPosition);
            if (away.sqrMagnitude > 0.001f)
            {
                return away.normalized;
            }
        }

        if (target == null)
        {
            return Vector2.zero;
        }

        Vector2 toTarget = (Vector2)target.transform.position - playerPosition;
        float desiredDistance = target.enemyKind == EnemyKind.Boss ? 2.25f : 2.05f;
        if (context.incomingProjectiles > 0 && toTarget.magnitude > desiredDistance)
        {
            Vector2 strafeWithApproach = new Vector2(-toTarget.y, toTarget.x).normalized * 0.45f + toTarget.normalized * 0.85f;
            return strafeWithApproach.sqrMagnitude > 0.001f ? strafeWithApproach.normalized : toTarget.normalized;
        }

        if (toTarget.magnitude > desiredDistance)
        {
            return toTarget.normalized;
        }

        if (context.closeEnemies >= 4 && playerController.CanWhirlwind)
        {
            return Vector2.zero;
        }

        Vector2 strafe = new Vector2(-toTarget.y, toTarget.x);
        return strafe.sqrMagnitude > 0.001f ? strafe.normalized * 0.65f : Vector2.zero;
    }

    private Vector2 AwayFromCluster(Vector2 playerPosition)
    {
        if (gameManager == null || gameManager.ActiveEnemies == null)
        {
            return Vector2.zero;
        }

        Vector2 away = Vector2.zero;
        for (int i = 0; i < gameManager.ActiveEnemies.Count; i++)
        {
            EnemyControllerBase enemy = gameManager.ActiveEnemies[i];
            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            Vector2 delta = playerPosition - (Vector2)enemy.transform.position;
            float distance = Mathf.Max(0.2f, delta.magnitude);
            away += delta / (distance * distance);
        }

        return away;
    }

    private void TapAction(string actionName)
    {
        if (touchManager == null || processInputBegan == null)
        {
            return;
        }

        if (!touchManager.TryGetActionButtonCenter(actionName, out Vector2 center))
        {
            return;
        }

        currentTouch = SimulatedActionTouch(actionName, center);
        markerVisible = true;
        processInputBegan.Invoke(touchManager, new object[] { currentTouch });
        processInputEnded?.Invoke(touchManager, null);
    }

    private Vector2 SimulatedActionTouch(string actionName, Vector2 center)
    {
        if (!simulateHighTouchErrorPressure)
        {
            return center + new Vector2(12f, -8f);
        }

        float radius = ActionButtonScreenRadius(actionName);
        Vector2 neighborDirection = NearestNeighborDirection(actionName, center);
        Vector2 clusterDirection = FourButtonClusterCenter() - center;
        if (clusterDirection.sqrMagnitude < 1f)
        {
            clusterDirection = neighborDirection;
        }

        Vector2 direction = actionTouchIndex % 3 == 0 ? neighborDirection : clusterDirection.normalized;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector2.left;
        }

        float[] multipliers = { 0.82f, 1.02f, 1.22f, 0.94f, 1.36f, 0.72f };
        float multiplier = multipliers[actionTouchIndex % multipliers.Length] * edgeTouchRadiusMultiplier;
        Vector2 handBias = new Vector2(18f, -12f) * (actionTouchIndex % 2 == 0 ? 0.6f : 1f);
        Vector2 jitter = new Vector2(((actionTouchIndex % 4) - 1.5f) * 5f, (((actionTouchIndex / 2) % 3) - 1f) * 6f);
        actionTouchIndex++;
        return center + direction.normalized * radius * multiplier + handBias + jitter;
    }

    private bool IsPrototypeComplete()
    {
        return gameManager != null && gameManager.PrototypeComplete;
    }

    private bool IsPrototypeFailed()
    {
        return gameManager != null && gameManager.PrototypeFailed;
    }

    private bool IsPrototypeEnded()
    {
        return gameManager != null && gameManager.PrototypeEnded;
    }

    private IEnumerator CaptureForSeconds(float seconds, string label)
    {
        currentCase = label;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return CaptureTick();
        }
    }

    private IEnumerator CaptureTick()
    {
        if (Time.realtimeSinceStartup >= nextCaptureTime)
        {
            yield return new WaitForEndOfFrame();
            CaptureFrame();
            nextCaptureTime = Time.realtimeSinceStartup + 1f / Mathf.Max(1, captureFps);
            markerVisible = false;
        }
        else
        {
            yield return null;
        }
    }

    private string CurrentCalibrationLabel()
    {
        if (calibrationFlow == null)
        {
            return "캘리브레이션 상태 없음";
        }

        string targetLabel = IsScenarioCalibrationTrial()
            ? $"scenario {calibrationFlow.CurrentScenarioKey} {calibrationFlow.CurrentTargetAction}"
            : $"{calibrationFlow.CurrentTargetAction} {calibrationFlow.CurrentTrialType}";
        return $"캘리브레이션 {calibrationFlow.CurrentTrialIndex + 1}/{calibrationFlow.CalibrationTotalCount}: {targetLabel}";
    }

    private Vector2 CalibrationTouchPosition()
    {
        if (calibrationFlow != null &&
            IsScenarioCalibrationTrial() &&
            Enum.TryParse(calibrationFlow.CurrentScenarioKey, out ADUIContextScenario scenario))
        {
            string responseAction = !string.IsNullOrEmpty(calibrationFlow.CurrentTargetAction)
                ? calibrationFlow.CurrentTargetAction
                : UserContextPriorModel.DefaultResponseForScenario(scenario);
            if (touchManager != null && touchManager.TryGetActionButtonCenter(responseAction, out Vector2 responseCenter))
            {
                return ScenarioCalibrationTouch(responseAction, scenario, responseCenter);
            }
        }

        if (calibrationFlow == null || !calibrationFlow.TryGetCurrentTargetCenter(out Vector2 center))
        {
            return ActionButtonCenter("Attack");
        }

        Vector2 consistentBias = new Vector2(18f, -12f);
        if (string.Equals(calibrationFlow.CurrentTrialType, "inner_edge", StringComparison.OrdinalIgnoreCase))
        {
            Vector2 clusterCenter = FourButtonClusterCenter();
            Vector2 inward = clusterCenter - center;
            if (inward.sqrMagnitude < 1f)
            {
                inward = Vector2.left;
            }

            return center + inward.normalized * 62f + consistentBias * 0.4f;
        }

        if (string.Equals(calibrationFlow.CurrentTrialType, "rapid_switch", StringComparison.OrdinalIgnoreCase))
        {
            Vector2 clusterCenter = FourButtonClusterCenter();
            Vector2 inward = clusterCenter - center;
            Vector2 diagonalRush = inward.sqrMagnitude > 1f ? inward.normalized * 42f : Vector2.left * 42f;
            return center + diagonalRush + consistentBias * 0.7f + new Vector2(-8f, -10f);
        }

        if (string.Equals(calibrationFlow.CurrentTrialType, "joystick_hold", StringComparison.OrdinalIgnoreCase))
        {
            return center + consistentBias + new Vector2(-18f, -24f);
        }

        if (string.Equals(calibrationFlow.CurrentTrialType, "validation", StringComparison.OrdinalIgnoreCase))
        {
            Vector2 clusterCenter = FourButtonClusterCenter();
            Vector2 inward = clusterCenter - center;
            Vector2 pressureBias = inward.sqrMagnitude > 1f ? inward.normalized * 50f : Vector2.left * 50f;
            return center + pressureBias + consistentBias * 0.55f;
        }

        int index = Mathf.Max(0, calibrationFlow.CurrentTrialIndex);
        Vector2 jitter = new Vector2(((index % 3) - 1) * 6f, (((index / 3) % 3) - 1) * 5f);
        return center + consistentBias + jitter;
    }

    private Vector2 ScenarioCalibrationTouch(string actionName, ADUIContextScenario scenario, Vector2 center)
    {
        int trialIndex = calibrationFlow != null ? Mathf.Max(0, calibrationFlow.CurrentTrialIndex) : actionTouchIndex;
        int seed = 1103 + trialIndex * 9176 + ((int)scenario + 1) * 6113 + ActionOrdinal(actionName) * 2719;
        float radius = ActionButtonScreenRadius(actionName);
        Vector2 scenarioBias = ScenarioTouchBias(actionName, scenario, center, radius);
        Vector2 jitter = DeterministicJitter(seed, ScenarioTouchJitterRadius(scenario, actionName, radius));
        Vector2 fatigueDrift = new Vector2(
            Mathf.Sin((trialIndex + 1) * 0.71f + (int)scenario) * radius * 0.11f,
            Mathf.Cos((trialIndex + 3) * 0.53f + ActionOrdinal(actionName)) * radius * 0.1f);
        return center + scenarioBias + jitter + fatigueDrift;
    }

    private Vector2 ScenarioTouchBias(string actionName, ADUIContextScenario scenario, Vector2 center, float radius)
    {
        Vector2 toCluster = SafeDirection(FourButtonClusterCenter() - center, Vector2.left);
        Vector2 toAttack = SafeDirection(ActionButtonCenter("Attack") - center, toCluster);
        Vector2 toDodge = SafeDirection(ActionButtonCenter("Dodge") - center, toCluster);
        Vector2 toHeal = SafeDirection(ActionButtonCenter("Heal") - center, toCluster);
        Vector2 toWhirlwind = SafeDirection(ActionButtonCenter("Whirlwind") - center, toCluster);
        Vector2 handBias = new Vector2(radius * 0.24f, -radius * 0.18f);

        switch (scenario)
        {
            case ADUIContextScenario.AttackCommitWindow:
                return handBias + (IsAction(actionName, "Attack") ? toAttack * radius * 0.08f : toAttack * radius * 0.26f);
            case ADUIContextScenario.SafeAttackOpportunity:
                return handBias + (IsAction(actionName, "Attack") ? toAttack * radius * 0.12f : toAttack * radius * 0.3f);
            case ADUIContextScenario.AttackOpportunity:
                return handBias + (IsAction(actionName, "Attack") ? toAttack * radius * 0.18f : toAttack * radius * 0.42f);
            case ADUIContextScenario.PreDodgeWindow:
                return handBias + toDodge * radius * (IsAction(actionName, "Dodge") ? 0.58f : 0.9f) + toCluster * radius * 0.16f;
            case ADUIContextScenario.RiskyCloseEnemy:
                return handBias + toDodge * radius * (IsAction(actionName, "Dodge") ? 0.5f : 0.78f) + toCluster * radius * 0.18f;
            case ADUIContextScenario.ImmediateDodgeThreat:
                return handBias + toDodge * radius * (IsAction(actionName, "Attack") ? 1.05f : 0.68f) + Vector2.down * radius * 0.32f;
            case ADUIContextScenario.ProjectileDodgeThreat:
                return handBias + toDodge * radius * (IsAction(actionName, "Dodge") ? 0.7f : 0.9f) + new Vector2(-radius * 0.22f, -radius * 0.18f);
            case ADUIContextScenario.MovingUnderPressure:
                return handBias + toCluster * radius * 0.7f + toDodge * radius * 0.45f + new Vector2(-radius * 0.42f, -radius * 0.52f);
            case ADUIContextScenario.DodgeThreat:
                return handBias + toDodge * radius * (IsAction(actionName, "Attack") ? 0.95f : 0.62f) + Vector2.down * radius * 0.26f;
            case ADUIContextScenario.LowHpHeal:
                return handBias + toHeal * radius * (IsAction(actionName, "Heal") ? 0.42f : 0.72f) + Vector2.down * radius * 0.12f;
            case ADUIContextScenario.CrowdWhirlwind:
                return handBias + toWhirlwind * radius * (IsAction(actionName, "Whirlwind") ? 0.5f : 0.8f) + toCluster * radius * 0.18f;
            case ADUIContextScenario.MovementThreat:
                return handBias + toCluster * radius * 0.65f + new Vector2(-radius * 0.35f, -radius * 0.5f);
            case ADUIContextScenario.LowHpThreat:
                if (IsAction(actionName, "Heal"))
                {
                    return handBias + toHeal * radius * 0.62f + Vector2.down * radius * 0.16f;
                }

                if (IsAction(actionName, "Dodge"))
                {
                    return handBias + toDodge * radius * 0.64f + Vector2.down * radius * 0.22f;
                }

                return handBias + (toDodge + toHeal).normalized * radius * 0.66f + Vector2.down * radius * 0.22f;
            case ADUIContextScenario.CrowdLowHp:
                return handBias + (toWhirlwind + toHeal).normalized * radius * 0.68f + toCluster * radius * 0.22f;
            default:
                return handBias + toCluster * radius * 0.24f;
        }
    }

    private float ScenarioTouchJitterRadius(ADUIContextScenario scenario, string actionName, float buttonRadius)
    {
        float multiplier;
        switch (scenario)
        {
            case ADUIContextScenario.AttackCommitWindow:
                multiplier = 0.14f;
                break;
            case ADUIContextScenario.SafeAttackOpportunity:
                multiplier = 0.16f;
                break;
            case ADUIContextScenario.AttackOpportunity:
                multiplier = 0.18f;
                break;
            case ADUIContextScenario.PreDodgeWindow:
                multiplier = 0.46f;
                break;
            case ADUIContextScenario.RiskyCloseEnemy:
                multiplier = 0.4f;
                break;
            case ADUIContextScenario.ImmediateDodgeThreat:
            case ADUIContextScenario.ProjectileDodgeThreat:
                multiplier = 0.54f;
                break;
            case ADUIContextScenario.MovingUnderPressure:
                multiplier = 0.62f;
                break;
            case ADUIContextScenario.DodgeThreat:
            case ADUIContextScenario.LowHpThreat:
                multiplier = 0.48f;
                break;
            case ADUIContextScenario.MovementThreat:
                multiplier = 0.56f;
                break;
            case ADUIContextScenario.CrowdWhirlwind:
            case ADUIContextScenario.CrowdLowHp:
                multiplier = 0.42f;
                break;
            case ADUIContextScenario.LowHpHeal:
                multiplier = 0.34f;
                break;
            default:
                multiplier = 0.24f;
                break;
        }

        if (IsAction(actionName, "Attack") &&
            (scenario == ADUIContextScenario.DodgeThreat ||
             scenario == ADUIContextScenario.MovementThreat ||
             scenario == ADUIContextScenario.RiskyCloseEnemy ||
             scenario == ADUIContextScenario.PreDodgeWindow ||
             scenario == ADUIContextScenario.ImmediateDodgeThreat ||
             scenario == ADUIContextScenario.ProjectileDodgeThreat ||
             scenario == ADUIContextScenario.MovingUnderPressure))
        {
            multiplier += 0.14f;
        }

        return Mathf.Max(6f, buttonRadius * multiplier);
    }

    private Vector2 DeterministicJitter(int seed, float maxMagnitude)
    {
        System.Random random = new System.Random(seed);
        float angle = (float)(random.NextDouble() * Math.PI * 2.0);
        float magnitude = maxMagnitude * (0.35f + (float)random.NextDouble() * 0.65f);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * magnitude;
    }

    private Vector2 SafeDirection(Vector2 direction, Vector2 fallback)
    {
        return direction.sqrMagnitude > 1f ? direction.normalized : fallback.normalized;
    }

    private bool IsAction(string actionName, string expected)
    {
        return string.Equals(actionName, expected, StringComparison.OrdinalIgnoreCase);
    }

    private int ActionOrdinal(string actionName)
    {
        if (IsAction(actionName, "Dodge"))
        {
            return 1;
        }

        if (IsAction(actionName, "Heal"))
        {
            return 2;
        }

        if (IsAction(actionName, "Whirlwind"))
        {
            return 3;
        }

        return 0;
    }

    private bool IsScenarioCalibrationTrial()
    {
        return calibrationFlow != null &&
               (string.Equals(calibrationFlow.CurrentTrialType, "combat_scenario", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(calibrationFlow.CurrentTrialType, "scenario_response", StringComparison.OrdinalIgnoreCase));
    }

    private Vector2 FourButtonClusterCenter()
    {
        string[] actions = { "Attack", "Dodge", "Heal", "Whirlwind" };
        Vector2 sum = Vector2.zero;
        int count = 0;
        for (int i = 0; i < actions.Length; i++)
        {
            if (touchManager != null && touchManager.TryGetActionButtonCenter(actions[i], out Vector2 center))
            {
                sum += center;
                count++;
            }
        }

        return count > 0 ? sum / count : ActionButtonCenter("Attack");
    }

    private Vector2 ActionButtonCenter(string actionName)
    {
        if (touchManager != null && touchManager.TryGetActionButtonCenter(actionName, out Vector2 center))
        {
            return center;
        }

        return new Vector2(Screen.width * 0.86f, Screen.height * 0.18f);
    }

    private float ActionButtonScreenRadius(string actionName)
    {
        Image image = ActionImage(actionName);
        if (image == null)
        {
            return 46f;
        }

        float scaleFactor = touchManager != null && touchManager.mainCanvas != null
            ? Mathf.Max(0.01f, touchManager.mainCanvas.scaleFactor)
            : 1f;
        Rect rect = image.rectTransform.rect;
        return Mathf.Max(16f, Mathf.Min(rect.width, rect.height) * 0.5f * scaleFactor);
    }

    private Vector2 NearestNeighborDirection(string actionName, Vector2 center)
    {
        string[] actions = { "Attack", "Dodge", "Heal", "Whirlwind" };
        Vector2 bestDirection = Vector2.zero;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < actions.Length; i++)
        {
            if (string.Equals(actions[i], actionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Vector2 otherCenter = ActionButtonCenter(actions[i]);
            float distance = Vector2.Distance(center, otherCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestDirection = otherCenter - center;
            }
        }

        return bestDirection.sqrMagnitude > 1f ? bestDirection.normalized : Vector2.left;
    }

    private Image ActionImage(string actionName)
    {
        if (touchManager == null)
        {
            return null;
        }

        if (string.Equals(actionName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return touchManager.visualAttackButton;
        }

        if (string.Equals(actionName, "Dodge", StringComparison.OrdinalIgnoreCase))
        {
            return touchManager.visualDodgeButton;
        }

        if (string.Equals(actionName, "Heal", StringComparison.OrdinalIgnoreCase))
        {
            return touchManager.visualHealButton;
        }

        if (string.Equals(actionName, "Whirlwind", StringComparison.OrdinalIgnoreCase))
        {
            return touchManager.visualWhirlwindButton;
        }

        return null;
    }

    private void ResolveReferences()
    {
        touchManager = FindAnyObjectByType<AdaptiveTouchManager>();
        calibrationFlow = FindAnyObjectByType<FourButtonCalibrationFlow>();
        contextPriorModel = FindAnyObjectByType<UserContextPriorModel>();
        combatManager = CombatManager.Instance != null ? CombatManager.Instance : FindAnyObjectByType<CombatManager>();
        gameManager = RoguelikeGameManager.Instance != null ? RoguelikeGameManager.Instance : FindAnyObjectByType<RoguelikeGameManager>();
        gameHud = FindAnyObjectByType<AdaptiveGameHudController>();
        sessionManager = FindAnyObjectByType<ExperimentSessionManager>();
        playerController = gameManager != null && gameManager.PlayerController != null
            ? gameManager.PlayerController
            : FindAnyObjectByType<PlayerController>();

        if (touchManager != null && processInputBegan == null)
        {
            Type type = typeof(AdaptiveTouchManager);
            processInputBegan = type.GetMethod("ProcessInputBegan", BindingFlags.Instance | BindingFlags.NonPublic);
            processInputEnded = type.GetMethod("ProcessInputEnded", BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    private void ConfigureManagers()
    {
        if (touchManager != null)
        {
            touchManager.autoCreateDecoderPipeline = true;
            touchManager.enableRuntimeLogging = true;
            touchManager.userTouchVariance = 180f;
            touchManager.minLikelihoodThreshold = 0.05f;
            touchManager.directHitRadiusScale = 0.62f;
            touchManager.maxGaussianVisualizerRadius = 88f;
        }

        if (gameHud != null)
        {
            gameHud.showResearchOverlay = showResearchOverlayInRecording;
        }
    }

    private void SetupOverlay()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("KTH Full Playthrough Overlay Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject panel = new GameObject("KTH Full Playthrough Overlay");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = compactRecorderOverlay
            ? new Vector2(20f, -154f)
            : new Vector2(18f, -286f);
        panelRect.sizeDelta = compactRecorderOverlay
            ? new Vector2(760f, 138f)
            : new Vector2(990f, 240f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = compactRecorderOverlay
            ? new Color(0.03f, 0.04f, 0.06f, 0.84f)
            : new Color(0.03f, 0.04f, 0.06f, 0.78f);
        overlayGroup = panel.AddComponent<CanvasGroup>();
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;

        GameObject textObject = new GameObject("KTH Full Playthrough Overlay Text");
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);
        overlayText = textObject.AddComponent<TextMeshProUGUI>();
        KoreanTmpFontUtility.Apply(overlayText);
        overlayText.fontSize = compactRecorderOverlay ? 17f : 16f;
        overlayText.color = Color.white;
        overlayText.alignment = TextAlignmentOptions.TopLeft;
        overlayText.textWrappingMode = TextWrappingModes.Normal;
        overlayText.outlineWidth = compactRecorderOverlay ? 0.12f : 0f;
        overlayText.outlineColor = new Color(0f, 0f, 0f, 0.85f);

        GameObject marker = new GameObject("KTH Full Playthrough Touch Marker");
        marker.transform.SetParent(canvas.transform, false);
        touchMarker = marker.AddComponent<RectTransform>();
        touchMarker.sizeDelta = new Vector2(42f, 42f);
        Image markerImage = marker.AddComponent<Image>();
        markerImage.color = new Color(1f, 0.85f, 0.1f, 0.78f);
    }

    private void UpdateOverlay()
    {
        TrackPlayerBounds();

        if (touchMarker != null)
        {
            touchMarker.gameObject.SetActive(markerVisible);
            touchMarker.position = currentTouch;
        }

        if (overlayText == null)
        {
            return;
        }

        if (overlayGroup != null)
        {
            bool hideDuringCalibrationPrompt = compactRecorderOverlay &&
                                               calibrationFlow != null &&
                                               calibrationFlow.CalibrationActive;
            overlayGroup.alpha = hideDuringCalibrationPrompt ? 0f : 1f;
        }

        if (compactRecorderOverlay)
        {
            overlayText.text = BuildCompactOverlayText();
            return;
        }

        string context = combatManager != null
            ? $"context enemies={combatManager.CurrentContext.totalEnemies}, close={combatManager.CurrentContext.closeEnemies}, commit={combatManager.CurrentContext.attackCommitTargets}, preDodge={combatManager.CurrentContext.preDodgeEnemies}, moveDanger={combatManager.CurrentContext.movingTowardDangerEnemies}, immediate={combatManager.CurrentContext.immediateThreats}, projectiles={combatManager.CurrentContext.projectileThreats}"
            : "context unavailable";
        string priors = combatManager != null
            ? $"priors A={combatManager.priorAttack:0.00} D={combatManager.priorDodge:0.00} H={combatManager.priorHeal:0.00} W={combatManager.priorWhirlwind:0.00}"
            : "priors unavailable";
        string userPrior = contextPriorModel != null && combatManager != null
            ? contextPriorModel.Summary(contextPriorModel.Classify(combatManager.CurrentContext, playerController))
            : "user_prior unavailable";
        string stage = gameManager != null
            ? $"stage={gameManager.CurrentStage}/3 running={gameManager.IsStageRunning} active={gameManager.ActiveEnemies.Count}"
            : "stage unavailable";
        string hp = playerController != null
            ? $"hp={playerController.CurrentHP}/{playerController.maxHP} healCD={playerController.HealCooldownRemaining:0.0} whirlCD={playerController.WhirlwindCooldownRemaining:0.0}"
            : "hp unavailable";
        string adaptiveSummary = touchManager != null
            ? $"{touchManager.RuntimeModeSummary}\n{touchManager.RuntimeDebugSummary}"
            : "adaptive runtime unavailable";

        overlayText.text =
            $"{currentCase}\n" +
            "mode=full playthrough | path=start screen -> calibration -> stage 1 -> stage 2 -> stage 3 -> result\n" +
            $"{stage} | {hp} | action={currentAction}\n" +
            $"{context} | {priors}\n" +
            $"{userPrior}\n" +
            $"{adaptiveSummary}\n" +
            lastDecisionLine;
    }

    private string BuildCompactOverlayText()
    {
        if (calibrationFlow != null && calibrationFlow.CalibrationActive)
        {
            string phase = calibrationFlow.CurrentTrialAcceptsInput ? "시작" : "읽기";
            string instruction = calibrationFlow.CurrentInstruction;
            if (instruction.Length > 82)
            {
                instruction = instruction.Substring(0, 79) + "...";
            }

            return $"{RecordingConditionLabel()} | {phase}  캘리브레이션 {calibrationFlow.CurrentTrialIndex + 1}/{calibrationFlow.CalibrationTotalCount}\n" +
                   instruction;
        }

        if (!IsPrototypeFailed() &&
            calibrationFlow != null &&
            calibrationFlow.CalibrationComplete &&
            gameManager != null &&
            !gameManager.IsStageRunning)
        {
            return $"{RecordingConditionLabel()}\n캘리브레이션 완료 - 학습된 터치 프로필로 전투 시작";
        }

        if (IsPrototypeFailed())
        {
            string failedHp = playerController != null
                ? $"HP {playerController.CurrentHP}/{playerController.maxHP}"
                : "HP 0";
            return $"Game Over | Stage {gameManager.CurrentStage}/3\n{failedHp}";
        }

        string stage = gameManager != null
            ? $"Stage {gameManager.CurrentStage}/3"
            : "Stage ?";
        string hp = playerController != null
            ? $"HP {playerController.CurrentHP}/{playerController.maxHP}"
            : "HP ?";
        string scenario = touchManager != null
            ? UserContextPriorModel.ScenarioLabel(touchManager.CurrentScenario)
            : "scenario unavailable";
        string mode = touchManager != null && touchManager.CurrentPolicy != null
            ? touchManager.CurrentPolicy.mode.ToString()
            : "mode unavailable";

        return $"{RecordingConditionLabel()} | {stage} | {hp} | {mode}\n" +
               $"scenario={scenario} | {currentAction}\n" +
               CompactCombatContextLine() + "\n" +
               CompactDemandPolicyLine();
    }

    private string NormalizedCondition()
    {
        string value = string.IsNullOrWhiteSpace(requestedCondition)
            ? "calibrated"
            : requestedCondition.Trim().ToLowerInvariant();

        if (value == "raw" ||
            value == "raw_button" ||
            value == "raw_fixed_button" ||
            value == "fixed")
        {
            return "raw";
        }

        if (value == "no_cal" ||
            value == "no_calibration" ||
            value == "nocal" ||
            value == "default_adaptive")
        {
            return "no_calibration";
        }

        return "calibrated";
    }

    private string RecordingConditionLabel()
    {
        switch (NormalizedCondition())
        {
            case "raw":
                return "조건 1: 기본 버튼";
            case "no_calibration":
                return "조건 2: 보정 없음 적응형";
            default:
                return "조건 3: 캘리브레이션 적응형";
        }
    }

    private string CompactCombatContextLine()
    {
        if (combatManager == null)
        {
            return "ctx unavailable";
        }

        CombatManager.CombatContext context = combatManager.CurrentContext;
        return $"ctx E={context.totalEnemies} close={context.closeEnemies} atk={context.attackingEnemies} tele={context.telegraphingEnemies} proj={context.projectileThreats} commit={context.attackCommitTargets} pre={context.preDodgeEnemies} | prior A={combatManager.priorAttack:0.00} D={combatManager.priorDodge:0.00} H={combatManager.priorHeal:0.00} W={combatManager.priorWhirlwind:0.00}";
    }

    private string CompactDemandPolicyLine()
    {
        if (touchManager == null || touchManager.CurrentDemand == null || touchManager.CurrentPolicy == null)
        {
            return "demand/policy unavailable";
        }

        ADUIInteractionDemand demand = touchManager.CurrentDemand;
        ADUIAdjustmentPolicy policy = touchManager.CurrentPolicy;
        return $"demand act={demand.actionIntensity:0.00} urg={demand.temporalUrgency:0.00} info={demand.informationPriority:0.00} occ={demand.occlusionRisk:0.00} cont={demand.controlContinuity:0.00} skill={demand.uiSkill:0.00} | err={policy.interactionErrorTolerance:0.00} corr={policy.correctionStrength:0.00}";
    }

    private void TrackPlayerBounds()
    {
        if (playerController == null)
        {
            return;
        }

        Vector3 position = playerController.transform.position;
        maxAbsPlayerX = Mathf.Max(maxAbsPlayerX, Mathf.Abs(position.x));
        maxAbsPlayerY = Mathf.Max(maxAbsPlayerY, Mathf.Abs(position.y));
    }

    private void CaptureFrame()
    {
        string framePath = Path.Combine(outputDir, "frames", $"frame_{frameIndex:0000}.png");
        Texture2D texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(framePath, texture.EncodeToPNG());
        Destroy(texture);
        frameIndex++;
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (condition.StartsWith("[Adaptive Touch]", StringComparison.Ordinal) ||
            condition.StartsWith("Starting Stage", StringComparison.Ordinal) ||
            condition.StartsWith("All stages complete", StringComparison.Ordinal))
        {
            lastDecisionLine = condition;
        }
    }
}

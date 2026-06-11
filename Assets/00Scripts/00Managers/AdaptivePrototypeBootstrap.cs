using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class AdaptivePrototypeBootstrap : MonoBehaviour
{
    private const float ActionButtonSize = 80f;
    private const float ActionButtonHitboxSize = 104f;
    private const float ActionButtonLabelWidth = 74f;
    private const float ActionButtonClusterCenterOffset = 209f;
    private const float ActionButtonClusterSpacing = 142f;
    private const float GameplayCameraOrthographicSize = 3.85f;

    public bool buildOnAwake = true;

    [Header("Startup Flow")]
    public bool startGameOnPlay = false;
    public bool runCalibrationBeforeGame = true;
    public int gameStartStage = 1;

    [Header("Calibration Counts")]
    public int centerTapsPerButton = 2;
    public int reciprocalAlternationPairs = 2;
    public int boundaryTapsPerButton = 1;
    public int ambiguousTapsPerButton = 1;
    public int contextTapsPerState = 1;

    private void Awake()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        if (!buildOnAwake || FindAnyObjectByType<RoguelikeGameManager>() != null)
        {
            return;
        }

        BuildPrototypeScene();
    }

    public void BuildPrototypeScene()
    {
        Camera mainCamera = CreateCameraIfNeeded();
        PlayerController player = CreatePlayerIfNeeded();
        ConfigureCameraFollow(mainCamera, player.transform);
        Canvas canvas = CreateCanvas();
        CreateEventSystemIfNeeded();

        TextMeshProUGUI stageText = CreateText(canvas.transform, "Stage Text", "Stage 1 / 3", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, -34f), new Vector2(400f, 44f), 24, TextAlignmentOptions.Left);
        TextMeshProUGUI stateText = CreateText(canvas.transform, "State Text", "Context", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -78f), new Vector2(460f, 38f), 20, TextAlignmentOptions.Left);
        TextMeshProUGUI playerHpText = CreateText(canvas.transform, "Player HP Text", "Player HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -116f), new Vector2(460f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI enemyText = CreateText(canvas.transform, "Enemy Context Text", "Enemies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, -152f), new Vector2(560f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI priorText = CreateText(canvas.transform, "Prior Text", "P(A) 50%   P(D) 50%   P(H) 0%   P(W) 0%", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(405f, -188f), new Vector2(780f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI contextText = CreateText(canvas.transform, "Detailed Context Text", "Closest: None", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(520f, -224f), new Vector2(1000f, 34f), 17, TextAlignmentOptions.Left);
        TextMeshProUGUI feedbackText = CreateText(canvas.transform, "Feedback Text", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(920f, 88f), 18, TextAlignmentOptions.Center);
        feedbackText.textWrappingMode = TextWrappingModes.Normal;
        CanvasGroup calibrationPromptGroup;
        TextMeshProUGUI calibrationPromptText = CreateCalibrationPrompt(canvas.transform, out calibrationPromptGroup);

        stateText.gameObject.SetActive(false);
        playerHpText.gameObject.SetActive(false);
        enemyText.gameObject.SetActive(false);
        priorText.gameObject.SetActive(false);
        contextText.gameObject.SetActive(false);
        CreateAdaptiveHud(
            canvas.transform,
            out CanvasGroup playerHudGroup,
            out Image hpFillImage,
            out TextMeshProUGUI hpNumericText,
            out Image modeChipBackground,
            out TextMeshProUGUI modeChipText,
            out TextMeshProUGUI scenarioTagText,
            out TextMeshProUGUI dangerIndicatorText,
            out TextMeshProUGUI correctionToastText,
            out CanvasGroup bossHudGroup,
            out Image bossHpFillImage,
            out TextMeshProUGUI bossHpText,
            out CanvasGroup researchOverlayGroup,
            out TextMeshProUGUI researchOverlayText);

        float actionHalfSpacing = ActionButtonClusterSpacing * 0.5f;
        float actionRightColumnX = -ActionButtonClusterCenterOffset + actionHalfSpacing;
        float actionLeftColumnX = -ActionButtonClusterCenterOffset - actionHalfSpacing;
        float actionBottomRowY = ActionButtonClusterCenterOffset - actionHalfSpacing;
        float actionTopRowY = ActionButtonClusterCenterOffset + actionHalfSpacing;
        Image attackImage = CreateActionButton(canvas.transform, "Attack Button", "Attack", new Vector2(1f, 0f), new Vector2(actionRightColumnX, actionBottomRowY), new Color(0.95f, 0.22f, 0.18f, 0.88f), out RectTransform attackHitbox, out RectTransform attackModelMarker, out TextMeshProUGUI attackLabel);
        Image dodgeImage = CreateActionButton(canvas.transform, "Dodge Button", "Dodge", new Vector2(1f, 0f), new Vector2(actionLeftColumnX, actionBottomRowY), new Color(0.12f, 0.58f, 1f, 0.88f), out RectTransform dodgeHitbox, out RectTransform dodgeModelMarker, out TextMeshProUGUI dodgeLabel);
        Image healImage = CreateActionButton(canvas.transform, "Heal Button", "Heal", new Vector2(1f, 0f), new Vector2(actionRightColumnX, actionTopRowY), new Color(0.18f, 0.78f, 0.38f, 0.88f), out RectTransform healHitbox, out RectTransform healModelMarker, out TextMeshProUGUI healLabel);
        Image whirlwindImage = CreateActionButton(canvas.transform, "Whirlwind Button", "Whirlwind", new Vector2(1f, 0f), new Vector2(actionLeftColumnX, actionTopRowY), new Color(1f, 0.68f, 0.12f, 0.88f), out RectTransform whirlwindHitbox, out RectTransform whirlwindModelMarker, out TextMeshProUGUI whirlwindLabel);
        RectTransform joystickTouchArea = CreateMovementJoystick(canvas.transform, player);
        Button skipButton = CreateSkipButton(canvas.transform);
        Image adaptiveToggleImage = CreateAdaptiveToggleButton(canvas.transform, out TextMeshProUGUI adaptiveToggleLabel);
        GameObject startScreen = CreateStartScreen(
            canvas.transform,
            out Button rawStartButton,
            out Button noCalibrationStartButton,
            out Button calibrationStartButton,
            out TMP_InputField participantIdInput);
        GameObject resultScreen = CreateResultScreen(canvas.transform, out TextMeshProUGUI resultText, out Button restartButton, out Button copyLogsButton);

        GameObject managerObject = new GameObject("Game Manager");
        CombatManager combatManager = managerObject.AddComponent<CombatManager>();
        CombatActionPriorBuilder priorBuilder = managerObject.AddComponent<CombatActionPriorBuilder>();
        RoguelikeGameManager gameManager = managerObject.AddComponent<RoguelikeGameManager>();
        ParticipantConfig participantConfig = managerObject.AddComponent<ParticipantConfig>();
        ExperimentSessionManager sessionManager = managerObject.AddComponent<ExperimentSessionManager>();
        ConditionManager conditionManager = managerObject.AddComponent<ConditionManager>();
        TrialScenarioManager trialScenarioManager = managerObject.AddComponent<TrialScenarioManager>();
        UserTouchModel userTouchModel = managerObject.AddComponent<UserTouchModel>();
        UserContextPriorModel userContextPriorModel = managerObject.AddComponent<UserContextPriorModel>();
        BayesianInputDecoder decoder = managerObject.AddComponent<BayesianInputDecoder>();
        SafetyGate safetyGate = managerObject.AddComponent<SafetyGate>();
        RawTouchLogger rawTouchLogger = managerObject.AddComponent<RawTouchLogger>();
        BayesianDecisionLogger decisionLogger = managerObject.AddComponent<BayesianDecisionLogger>();
        ButtonLayoutLogger layoutLogger = managerObject.AddComponent<ButtonLayoutLogger>();
        HPOutcomeLogger hpOutcomeLogger = managerObject.AddComponent<HPOutcomeLogger>();
        ModePolicyLogger modePolicyLogger = managerObject.AddComponent<ModePolicyLogger>();
        UserEvaluationLogger evaluationLogger = managerObject.AddComponent<UserEvaluationLogger>();
        InteractionDemandModel demandModel = managerObject.AddComponent<InteractionDemandModel>();
        AdaptiveUIPolicyEngine policyEngine = managerObject.AddComponent<AdaptiveUIPolicyEngine>();
        AdaptiveUIAdjustmentController adjustmentController = managerObject.AddComponent<AdaptiveUIAdjustmentController>();
        AdaptiveGameHudController gameHudController = managerObject.AddComponent<AdaptiveGameHudController>();
        AdaptiveTouchManager touchManager = managerObject.AddComponent<AdaptiveTouchManager>();

        gameManager.playerTransform = player.transform;
        gameManager.playerController = player;
        gameManager.skipButton = skipButton;
        gameManager.startButton = noCalibrationStartButton;
        gameManager.restartButton = restartButton;
        gameManager.copyLogsButton = copyLogsButton;
        gameManager.startScreenRoot = startScreen;
        gameManager.resultScreenRoot = resultScreen;
        gameManager.stageText = stageText;
        gameManager.resultText = resultText;
        gameManager.startStageOnPlay = startGameOnPlay && !runCalibrationBeforeGame;
        gameManager.startingStage = gameStartStage;
        if (int.TryParse(ReadCommandLineArgument("-kthStartStage"), out int commandLineStartStage))
        {
            gameManager.startingStage = Mathf.Clamp(commandLineStartStage, 1, 3);
        }

        gameManager.evaluationLogger = evaluationLogger;
        gameManager.sessionManager = sessionManager;

        combatManager.gameManager = gameManager;
        combatManager.playerController = player;
        combatManager.priorBuilder = priorBuilder;
        combatManager.stateText = stateText;
        combatManager.playerHpText = hpNumericText;
        combatManager.enemyHpText = enemyText;
        combatManager.feedbackLogText = feedbackText;
        combatManager.priorText = priorText;
        combatManager.contextText = contextText;

        sessionManager.participantConfig = participantConfig;
        participantConfig.participantIdInput = participantIdInput;
        string commandLineParticipant = ReadCommandLineArgument("-kthParticipantId");
        if (!string.IsNullOrWhiteSpace(commandLineParticipant))
        {
            participantIdInput.text = commandLineParticipant;
            participantConfig.ApplyParticipantInput();
        }

        trialScenarioManager.sessionManager = sessionManager;
        trialScenarioManager.conditionManager = conditionManager;
        trialScenarioManager.centerTapsPerButton = centerTapsPerButton;
        trialScenarioManager.reciprocalAlternationPairs = reciprocalAlternationPairs;
        trialScenarioManager.boundaryTapsPerButton = boundaryTapsPerButton;
        trialScenarioManager.ambiguousTapsPerButton = ambiguousTapsPerButton;
        trialScenarioManager.contextTapsPerState = contextTapsPerState;
        rawTouchLogger.sessionManager = sessionManager;
        decisionLogger.sessionManager = sessionManager;
        layoutLogger.sessionManager = sessionManager;
        hpOutcomeLogger.sessionManager = sessionManager;
        modePolicyLogger.sessionManager = sessionManager;
        evaluationLogger.sessionManager = sessionManager;
        evaluationLogger.conditionManager = conditionManager;
        decoder.userTouchModel = userTouchModel;

        gameHudController.playerHudGroup = playerHudGroup;
        gameHudController.hpFillImage = hpFillImage;
        gameHudController.hpText = hpNumericText;
        gameHudController.modeChipBackground = modeChipBackground;
        gameHudController.modeChipText = modeChipText;
        gameHudController.scenarioTagText = scenarioTagText;
        gameHudController.dangerIndicatorText = dangerIndicatorText;
        gameHudController.correctionToastText = correctionToastText;
        gameHudController.bossHudGroup = bossHudGroup;
        gameHudController.bossHpFillImage = bossHpFillImage;
        gameHudController.bossHpText = bossHpText;
        gameHudController.researchOverlayGroup = researchOverlayGroup;
        gameHudController.researchOverlayText = researchOverlayText;

        touchManager.mainCanvas = canvas;
        touchManager.visualAttackButton = attackImage;
        touchManager.visualDodgeButton = dodgeImage;
        touchManager.visualHealButton = healImage;
        touchManager.visualWhirlwindButton = whirlwindImage;
        touchManager.attackButtonLabel = attackLabel;
        touchManager.dodgeButtonLabel = dodgeLabel;
        touchManager.healButtonLabel = healLabel;
        touchManager.whirlwindButtonLabel = whirlwindLabel;
        touchManager.gameHud = gameHudController;
        touchManager.attackHitboxVisualizer = attackHitbox;
        touchManager.dodgeHitboxVisualizer = dodgeHitbox;
        touchManager.healHitboxVisualizer = healHitbox;
        touchManager.whirlwindHitboxVisualizer = whirlwindHitbox;
        touchManager.attackModelCenterMarker = attackModelMarker;
        touchManager.dodgeModelCenterMarker = dodgeModelMarker;
        touchManager.healModelCenterMarker = healModelMarker;
        touchManager.whirlwindModelCenterMarker = whirlwindModelMarker;
        touchManager.userContextPriorModel = userContextPriorModel;
        touchManager.movementJoystickTouchArea = joystickTouchArea;
        touchManager.adaptiveToggleButton = adaptiveToggleImage;
        touchManager.adaptiveToggleLabel = adaptiveToggleLabel;
        touchManager.decoder = decoder;
        touchManager.safetyGate = safetyGate;
        touchManager.userTouchModel = userTouchModel;
        touchManager.sessionManager = sessionManager;
        touchManager.conditionManager = conditionManager;
        touchManager.trialScenarioManager = trialScenarioManager;
        touchManager.rawTouchLogger = rawTouchLogger;
        touchManager.decisionLogger = decisionLogger;
        touchManager.layoutLogger = layoutLogger;
        touchManager.hpOutcomeLogger = hpOutcomeLogger;
        touchManager.modePolicyLogger = modePolicyLogger;
        touchManager.evaluationLogger = evaluationLogger;
        touchManager.demandModel = demandModel;
        touchManager.policyEngine = policyEngine;
        touchManager.adjustmentController = adjustmentController;
        adjustmentController.attackButtonImage = attackImage;
        adjustmentController.dodgeButtonImage = dodgeImage;
        adjustmentController.healButtonImage = healImage;
        adjustmentController.whirlwindButtonImage = whirlwindImage;
        adjustmentController.attackButtonRoot = attackImage.rectTransform;
        adjustmentController.dodgeButtonRoot = dodgeImage.rectTransform;
        adjustmentController.healButtonRoot = healImage.rectTransform;
        adjustmentController.whirlwindButtonRoot = whirlwindImage.rectTransform;

        FourButtonCalibrationFlow calibrationFlow = managerObject.AddComponent<FourButtonCalibrationFlow>();
        calibrationFlow.touchManager = touchManager;
        calibrationFlow.contextPriorModel = userContextPriorModel;
        calibrationFlow.gameManager = gameManager;
        calibrationFlow.participantConfig = participantConfig;
        calibrationFlow.conditionManager = conditionManager;
        calibrationFlow.sessionManager = sessionManager;
        calibrationFlow.startScreenRoot = startScreen;
        calibrationFlow.startButton = calibrationStartButton;
        calibrationFlow.rawStartButton = rawStartButton;
        calibrationFlow.noCalibrationStartButton = noCalibrationStartButton;
        calibrationFlow.stageText = stageText;
        calibrationFlow.feedbackText = calibrationPromptText;
        calibrationFlow.feedbackGroup = calibrationPromptGroup;
        calibrationFlow.warmupTapsPerButton = 1;
        calibrationFlow.centerTapsPerButton = Mathf.Clamp(centerTapsPerButton, 2, 4);
        calibrationFlow.edgeTapsPerButton = Mathf.Clamp(boundaryTapsPerButton, 1, 2);
        calibrationFlow.transitionTapsPerButton = Mathf.Clamp(Mathf.CeilToInt(reciprocalAlternationPairs / 2f), 1, 2);
        calibrationFlow.joystickStressTapsPerButton = Mathf.Clamp(contextTapsPerState, 1, 2);
        calibrationFlow.validationTapsPerButton = Mathf.Clamp(ambiguousTapsPerButton, 1, 2);
        calibrationFlow.combatScenarioRepeats = 1;
        calibrationFlow.compactCombatScenarioCalibration = true;

        AttachCommandLineRecorderIfRequested();
    }

    private Camera CreateCameraIfNeeded()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = GameplayCameraOrthographicSize;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.08f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        cameraObject.AddComponent<AudioListener>();
        return camera;
    }

    private void ConfigureCameraFollow(Camera mainCamera, Transform playerTransform)
    {
        if (mainCamera == null || playerTransform == null)
        {
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = GameplayCameraOrthographicSize;

        TopDownCameraFollow follow = mainCamera.GetComponent<TopDownCameraFollow>();
        if (follow == null)
        {
            follow = mainCamera.gameObject.AddComponent<TopDownCameraFollow>();
        }

        follow.SetTarget(playerTransform);
    }

    private PlayerController CreatePlayerIfNeeded()
    {
        PlayerController existingPlayer = FindAnyObjectByType<PlayerController>();
        if (existingPlayer != null)
        {
            return existingPlayer;
        }

        GameObject playerObject = new GameObject("Player");
        playerObject.tag = "Player";
        playerObject.transform.position = Vector3.zero;
        playerObject.AddComponent<Rigidbody2D>();
        playerObject.AddComponent<CircleCollider2D>();
        return playerObject.AddComponent<PlayerController>();
    }

    private Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Adaptive UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private void CreateEventSystemIfNeeded()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private Image CreateActionButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Color color,
        out RectTransform hitbox,
        out RectTransform modelCenterMarker,
        out TextMeshProUGUI labelText)
    {
        RectTransform buttonTransform = CreateRectTransform(name, parent, anchor, anchor, anchoredPosition, new Vector2(ActionButtonSize, ActionButtonSize));
        Image image = buttonTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.CircleSprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;

        hitbox = CreateRectTransform(name + " Hitbox", buttonTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ActionButtonHitboxSize, ActionButtonHitboxSize));
        Image hitboxImage = hitbox.gameObject.AddComponent<Image>();
        hitboxImage.sprite = PrototypeVisualFactory.CircleSprite;
        hitboxImage.color = new Color(color.r, color.g, color.b, 0.1f);
        hitboxImage.raycastTarget = false;

        labelText = CreateText(buttonTransform, label + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ActionButtonLabelWidth, 36f), 18, TextAlignmentOptions.Center);
        labelText.fontSize = label.Length > 7 ? 10f : 14f;
        labelText.color = Color.white;

        modelCenterMarker = CreateRectTransform(name + " Gaussian Center", buttonTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
        Image markerImage = modelCenterMarker.gameObject.AddComponent<Image>();
        markerImage.sprite = PrototypeVisualFactory.CircleSprite;
        markerImage.color = new Color(1f, 1f, 1f, 0.92f);
        markerImage.raycastTarget = false;
        markerImage.preserveAspect = true;
        modelCenterMarker.gameObject.SetActive(false);
        return image;
    }

    private TextMeshProUGUI CreateCalibrationPrompt(Transform parent, out CanvasGroup promptGroup)
    {
        RectTransform promptPanel = CreateRectTransform(
            "Calibration Prompt Panel",
            parent,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -300f),
            new Vector2(860f, 104f));

        Image background = promptPanel.gameObject.AddComponent<Image>();
        background.sprite = PrototypeVisualFactory.SquareSprite;
        background.color = new Color(0.02f, 0.025f, 0.03f, 0.9f);
        background.raycastTarget = false;

        promptGroup = promptPanel.gameObject.AddComponent<CanvasGroup>();
        promptGroup.alpha = 0f;
        promptGroup.interactable = false;
        promptGroup.blocksRaycasts = false;

        TextMeshProUGUI promptText = CreateText(
            promptPanel,
            "Calibration Prompt Text",
            string.Empty,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero,
            24,
            TextAlignmentOptions.Center);
        promptText.margin = new Vector4(18f, 10f, 18f, 10f);
        promptText.textWrappingMode = TextWrappingModes.Normal;
        promptText.overflowMode = TextOverflowModes.Ellipsis;
        promptText.enableAutoSizing = true;
        promptText.fontSizeMin = 15f;
        promptText.fontSizeMax = 24f;
        promptText.fontStyle = FontStyles.Bold;
        promptText.outlineWidth = 0.16f;
        promptText.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        return promptText;
    }

    private void CreateAdaptiveHud(
        Transform parent,
        out CanvasGroup playerHudGroup,
        out Image hpFillImage,
        out TextMeshProUGUI hpNumericText,
        out Image modeChipBackground,
        out TextMeshProUGUI modeChipText,
        out TextMeshProUGUI scenarioTagText,
        out TextMeshProUGUI dangerIndicatorText,
        out TextMeshProUGUI correctionToastText,
        out CanvasGroup bossHudGroup,
        out Image bossHpFillImage,
        out TextMeshProUGUI bossHpText,
        out CanvasGroup researchOverlayGroup,
        out TextMeshProUGUI researchOverlayText)
    {
        RectTransform hudRoot = CreateRectTransform("Adaptive Player HUD", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(360f, 56f));
        playerHudGroup = hudRoot.gameObject.AddComponent<CanvasGroup>();

        RectTransform hpBar = CreateRectTransform("HP Bar", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340f, 42f));
        Image hpBackground = hpBar.gameObject.AddComponent<Image>();
        hpBackground.sprite = PrototypeVisualFactory.SquareSprite;
        hpBackground.color = new Color(0.015f, 0.018f, 0.02f, 0.94f);
        hpBackground.raycastTarget = false;

        RectTransform hpFill = CreateRectTransform("HP Fill", hpBar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        hpFill.offsetMin = new Vector2(4f, 4f);
        hpFill.offsetMax = new Vector2(-4f, -4f);
        hpFillImage = hpFill.gameObject.AddComponent<Image>();
        hpFillImage.sprite = PrototypeVisualFactory.SquareSprite;
        hpFillImage.type = Image.Type.Filled;
        hpFillImage.fillMethod = Image.FillMethod.Horizontal;
        hpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        hpFillImage.fillAmount = 1f;
        hpFillImage.color = new Color(0.24f, 0.82f, 0.42f, 0.95f);
        hpFillImage.raycastTarget = false;

        hpNumericText = CreateText(hpBar, "HP Numeric Text", "HP 100/100", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 20, TextAlignmentOptions.Center);
        hpNumericText.fontStyle = FontStyles.Bold;
        hpNumericText.outlineWidth = 0.18f;
        hpNumericText.outlineColor = new Color(0f, 0f, 0f, 0.95f);

        RectTransform modeChip = CreateRectTransform("Mode Chip", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(250f, 42f));
        modeChipBackground = modeChip.gameObject.AddComponent<Image>();
        modeChipBackground.sprite = PrototypeVisualFactory.SquareSprite;
        modeChipBackground.color = new Color(0.95f, 0.28f, 0.18f, 0.92f);
        modeChipBackground.raycastTarget = false;
        modeChipText = CreateText(modeChip, "Mode Chip Text", "ACTION FIRST", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18, TextAlignmentOptions.Center);
        modeChipText.fontStyle = FontStyles.Bold;

        scenarioTagText = CreateText(parent, "Scenario Tag Text", "General", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(430f, 34f), 17, TextAlignmentOptions.Center);
        scenarioTagText.color = new Color(0.92f, 0.96f, 1f, 1f);
        dangerIndicatorText = CreateText(parent, "Danger Indicator Text", "STABLE", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(430f, 34f), 18, TextAlignmentOptions.Center);
        dangerIndicatorText.fontStyle = FontStyles.Bold;

        RectTransform bossBar = CreateRectTransform("Boss HP Bar", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -158f), new Vector2(540f, 34f));
        bossHudGroup = bossBar.gameObject.AddComponent<CanvasGroup>();
        bossHudGroup.alpha = 0f;
        bossHudGroup.interactable = false;
        bossHudGroup.blocksRaycasts = false;

        Image bossBackground = bossBar.gameObject.AddComponent<Image>();
        bossBackground.sprite = PrototypeVisualFactory.SquareSprite;
        bossBackground.color = new Color(0.025f, 0.018f, 0.03f, 0.94f);
        bossBackground.raycastTarget = false;

        RectTransform bossFill = CreateRectTransform("Boss HP Fill", bossBar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        bossFill.offsetMin = new Vector2(4f, 4f);
        bossFill.offsetMax = new Vector2(-4f, -4f);
        bossHpFillImage = bossFill.gameObject.AddComponent<Image>();
        bossHpFillImage.sprite = PrototypeVisualFactory.SquareSprite;
        bossHpFillImage.type = Image.Type.Filled;
        bossHpFillImage.fillMethod = Image.FillMethod.Horizontal;
        bossHpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        bossHpFillImage.fillAmount = 0f;
        bossHpFillImage.color = new Color(0.7f, 0.28f, 0.92f, 0.95f);
        bossHpFillImage.raycastTarget = false;

        bossHpText = CreateText(bossBar, "Boss HP Text", string.Empty, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 17, TextAlignmentOptions.Center);
        bossHpText.fontStyle = FontStyles.Bold;
        bossHpText.outlineWidth = 0.16f;
        bossHpText.outlineColor = new Color(0f, 0f, 0f, 0.95f);

        correctionToastText = CreateText(parent, "Correction Toast Text", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 154f), new Vector2(620f, 42f), 22, TextAlignmentOptions.Center);
        correctionToastText.fontStyle = FontStyles.Bold;

        RectTransform researchPanel = CreateRectTransform("Research Demand Policy Overlay", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-520f, -96f), new Vector2(940f, 150f));
        researchOverlayGroup = researchPanel.gameObject.AddComponent<CanvasGroup>();
        researchOverlayGroup.alpha = 0f;
        Image researchBackground = researchPanel.gameObject.AddComponent<Image>();
        researchBackground.sprite = PrototypeVisualFactory.SquareSprite;
        researchBackground.color = new Color(0.02f, 0.025f, 0.035f, 0.76f);
        researchBackground.raycastTarget = false;

        researchOverlayText = CreateText(researchPanel, "Research Demand Policy Text", string.Empty, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 15, TextAlignmentOptions.TopLeft);
        researchOverlayText.margin = new Vector4(12f, 8f, 12f, 8f);
        researchOverlayText.textWrappingMode = TextWrappingModes.Normal;
    }

    private RectTransform CreateMovementJoystick(Transform parent, PlayerController player)
    {
        RectTransform baseTransform = CreateRectTransform("Movement Joystick", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 170f), new Vector2(280f, 280f));
        Image baseImage = baseTransform.gameObject.AddComponent<Image>();
        baseImage.sprite = PrototypeVisualFactory.CircleSprite;
        baseImage.color = new Color(0.72f, 0.82f, 0.9f, 0.2f);
        baseImage.raycastTarget = true;
        baseImage.preserveAspect = true;

        RectTransform handleTransform = CreateRectTransform("Movement Joystick Handle", baseTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
        Image handleImage = handleTransform.gameObject.AddComponent<Image>();
        handleImage.sprite = PrototypeVisualFactory.CircleSprite;
        handleImage.color = new Color(0.95f, 0.98f, 1f, 0.55f);
        handleImage.raycastTarget = false;
        handleImage.preserveAspect = true;

        VirtualJoystick joystick = baseTransform.gameObject.AddComponent<VirtualJoystick>();
        joystick.targetPlayer = player;
        joystick.joystickBase = baseTransform;
        joystick.joystickHandle = handleTransform;
        return baseTransform;
    }

    private Button CreateSkipButton(Transform parent)
    {
        RectTransform rectTransform = CreateRectTransform("Skip Stage Button", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-130f, -44f), new Vector2(190f, 52f));
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.SquareSprite;
        image.color = new Color(0.16f, 0.18f, 0.16f, 0.92f);

        Button button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI text = CreateText(rectTransform, "Skip Text", "Skip Stage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 38f), 18, TextAlignmentOptions.Center);
        text.color = Color.white;
        return button;
    }

    private Image CreateAdaptiveToggleButton(Transform parent, out TextMeshProUGUI labelText)
    {
        RectTransform rectTransform = CreateRectTransform("Adaptive Toggle Button", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-390f, -44f), new Vector2(260f, 52f));
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.SquareSprite;
        image.color = new Color(0.18f, 0.72f, 0.44f, 0.92f);
        image.raycastTarget = false;

        labelText = CreateText(rectTransform, "Adaptive Toggle Text", "Adaptive: ON", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 38f), 18, TextAlignmentOptions.Center);
        labelText.color = Color.white;
        return image;
    }

    private GameObject CreateStartScreen(
        Transform parent,
        out Button rawStartButton,
        out Button noCalibrationStartButton,
        out Button calibrationStartButton,
        out TMP_InputField participantIdInput)
    {
        GameObject screen = CreateOverlayPanel(parent, "Start Screen", new Color(0.02f, 0.03f, 0.03f, 0.88f));
        TextMeshProUGUI title = CreateText(screen.transform, "Start Title", "Adaptive Touch 실험", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 184f), new Vector2(760f, 84f), 46, TextAlignmentOptions.Center);
        title.color = Color.white;

        TextMeshProUGUI subtitle = CreateText(screen.transform, "Start Subtitle", "참가자 번호를 입력한 뒤 실험 조건을 선택하세요.", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 108f), new Vector2(920f, 64f), 22, TextAlignmentOptions.Center);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.color = new Color(0.86f, 0.9f, 0.92f, 1f);

        participantIdInput = CreateParticipantInput(screen.transform, new Vector2(0f, 22f));
        rawStartButton = CreateMenuButton(screen.transform, "Raw Start Button", "기본 버튼", new Vector2(-390f, -106f), new Vector2(310f, 78f), new Color(0.34f, 0.36f, 0.39f, 0.95f));
        noCalibrationStartButton = CreateMenuButton(screen.transform, "Start Without Calibration Button", "보정 없음 적응형", new Vector2(0f, -106f), new Vector2(350f, 78f), new Color(0.24f, 0.46f, 0.82f, 0.95f));
        calibrationStartButton = CreateMenuButton(screen.transform, "Calibrate Start Button", "캘리브레이션 적응형", new Vector2(410f, -106f), new Vector2(390f, 78f), new Color(0.18f, 0.72f, 0.44f, 0.95f));
        return screen;
    }

    private TMP_InputField CreateParticipantInput(Transform parent, Vector2 anchoredPosition)
    {
        TextMeshProUGUI label = CreateText(parent, "Participant Label", "Player No.", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition + new Vector2(-250f, 0f), new Vector2(180f, 42f), 22, TextAlignmentOptions.Right);
        label.color = new Color(0.9f, 0.94f, 0.96f, 1f);

        RectTransform inputRect = CreateRectTransform("Participant ID Input", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition + new Vector2(40f, 0f), new Vector2(360f, 58f));
        Image inputBackground = inputRect.gameObject.AddComponent<Image>();
        inputBackground.sprite = PrototypeVisualFactory.SquareSprite;
        inputBackground.color = new Color(0.08f, 0.11f, 0.13f, 0.96f);

        TMP_InputField input = inputRect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = inputBackground;
        input.characterLimit = 24;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.text = "P01";

        TextMeshProUGUI text = CreateText(inputRect, "Participant ID Text", "P01", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-34f, -14f), 24, TextAlignmentOptions.MidlineLeft);
        text.rectTransform.offsetMin = new Vector2(18f, 8f);
        text.rectTransform.offsetMax = new Vector2(-18f, -8f);
        text.raycastTarget = true;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI placeholder = CreateText(inputRect, "Participant ID Placeholder", "P01", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-34f, -14f), 24, TextAlignmentOptions.MidlineLeft);
        placeholder.rectTransform.offsetMin = new Vector2(18f, 8f);
        placeholder.rectTransform.offsetMax = new Vector2(-18f, -8f);
        placeholder.color = new Color(0.7f, 0.76f, 0.8f, 0.72f);

        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private GameObject CreateResultScreen(Transform parent, out TextMeshProUGUI resultText, out Button restartButton, out Button copyLogsButton)
    {
        GameObject screen = CreateOverlayPanel(parent, "Result Screen", new Color(0.02f, 0.03f, 0.03f, 0.9f));
        resultText = CreateText(screen.transform, "Result Text", "Final Results", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(1040f, 630f), 18, TextAlignmentOptions.TopLeft);
        resultText.textWrappingMode = TextWrappingModes.Normal;

        copyLogsButton = CreateMenuButton(screen.transform, "Copy Logs Button", "Copy Time-Series JSON", new Vector2(-190f, -410f), new Vector2(330f, 70f), new Color(0.24f, 0.46f, 0.82f, 0.95f));
        restartButton = CreateMenuButton(screen.transform, "Restart Button", "Restart", new Vector2(190f, -410f), new Vector2(250f, 70f), new Color(0.18f, 0.72f, 0.44f, 0.95f));
        screen.SetActive(false);
        return screen;
    }

    private GameObject CreateOverlayPanel(Transform parent, string name, Color color)
    {
        RectTransform rectTransform = CreateRectTransform(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.SquareSprite;
        image.color = color;
        image.raycastTarget = true;
        return rectTransform.gameObject;
    }

    private Button CreateMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        RectTransform rectTransform = CreateRectTransform(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, size);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.SquareSprite;
        image.color = color;

        Button button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI text = CreateText(rectTransform, name + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(20f, 20f), 24, TextAlignmentOptions.Center);
        text.color = Color.white;
        return button;
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string value,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rectTransform = CreateRectTransform(name, parent, anchorMin, anchorMax, anchoredPosition, size);
        TextMeshProUGUI text = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
        KoreanTmpFontUtility.Apply(text);
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private RectTransform CreateRectTransform(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);

        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
        return rectTransform;
    }

    private void AttachCommandLineRecorderIfRequested()
    {
        string outputDir = ReadCommandLineArgument("-kthOutputDir");
        if (string.IsNullOrEmpty(outputDir) ||
            FindAnyObjectByType<KTHTopDownPrototypePlayRecorder>() != null ||
            FindAnyObjectByType<KTHFullPlaythroughRecorder>() != null)
        {
            return;
        }

        bool fullPlaythrough = string.Equals(ReadCommandLineArgument("-kthFullPlaythrough"), "1", System.StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(ReadCommandLineArgument("-kthFullPlaythrough"), "true", System.StringComparison.OrdinalIgnoreCase);
        if (fullPlaythrough)
        {
            GameObject recorderObject = new GameObject("KTH Full Playthrough Recorder");
            KTHFullPlaythroughRecorder recorder = recorderObject.AddComponent<KTHFullPlaythroughRecorder>();
            recorder.outputDir = outputDir;
            recorder.captureFps = 15;
            recorder.captureWidth = 1280;
            recorder.captureHeight = 720;
            string requestedCondition = ReadCommandLineArgument("-kthCondition");
            if (!string.IsNullOrWhiteSpace(requestedCondition))
            {
                recorder.requestedCondition = requestedCondition;
            }

            recorder.stopAfterContextShowcase = string.Equals(ReadCommandLineArgument("-kthContextShowcaseOnly"), "1", System.StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(ReadCommandLineArgument("-kthContextShowcaseOnly"), "true", System.StringComparison.OrdinalIgnoreCase);
            bool skipContextShowcase = string.Equals(ReadCommandLineArgument("-kthSkipContextShowcase"), "1", System.StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ReadCommandLineArgument("-kthSkipContextShowcase"), "true", System.StringComparison.OrdinalIgnoreCase);
            recorder.runContextShowcaseAfterCalibration = !skipContextShowcase;
            recorder.forceGameOverCheck = string.Equals(ReadCommandLineArgument("-kthForceGameOverCheck"), "1", System.StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(ReadCommandLineArgument("-kthForceGameOverCheck"), "true", System.StringComparison.OrdinalIgnoreCase);
            if (float.TryParse(ReadCommandLineArgument("-kthMaxPlaySeconds"), out float maxPlaySeconds) && maxPlaySeconds > 0f)
            {
                recorder.maxPlaySeconds = maxPlaySeconds;
            }

            Debug.Log($"[KTH] Runtime full playthrough recorder attached. outputDir={outputDir}");
            return;
        }

        GameObject demoRecorderObject = new GameObject("KTH TopDown Prototype Play Recorder");
        KTHTopDownPrototypePlayRecorder demoRecorder = demoRecorderObject.AddComponent<KTHTopDownPrototypePlayRecorder>();
        demoRecorder.outputDir = outputDir;
        demoRecorder.captureFps = 15;
        demoRecorder.captureWidth = 1280;
        demoRecorder.captureHeight = 720;
        Debug.Log($"[KTH] Runtime top-down recorder attached. outputDir={outputDir}");
    }

    private string ReadCommandLineArgument(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, System.StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return "";
    }
}

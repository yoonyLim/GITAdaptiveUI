using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class AdaptivePrototypeBootstrap : MonoBehaviour
{
    public bool buildOnAwake = true;

    [Header("Startup Flow")]
    public bool startGameOnPlay = true;
    public bool runCalibrationBeforeGame;
    public int gameStartStage = 1;

    [Header("Calibration Counts")]
    public int centerTapsPerButton = 8;
    public int reciprocalAlternationPairs = 10;
    public int boundaryTapsPerButton = 4;
    public int ambiguousTapsPerButton = 4;
    public int contextTapsPerState = 4;

    private void Awake()
    {
        if (!buildOnAwake || FindAnyObjectByType<RoguelikeGameManager>() != null)
        {
            return;
        }

        BuildPrototypeScene();
    }

    public void BuildPrototypeScene()
    {
        CreateCameraIfNeeded();
        PlayerController player = CreatePlayerIfNeeded();
        Canvas canvas = CreateCanvas();
        CreateEventSystemIfNeeded();

        TextMeshProUGUI stageText = CreateText(canvas.transform, "Stage Text", "Stage 1 / 3", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, -34f), new Vector2(400f, 44f), 24, TextAlignmentOptions.Left);
        TextMeshProUGUI stateText = CreateText(canvas.transform, "State Text", "Context", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -78f), new Vector2(460f, 38f), 20, TextAlignmentOptions.Left);
        TextMeshProUGUI playerHpText = CreateText(canvas.transform, "Player HP Text", "Player HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -116f), new Vector2(460f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI enemyText = CreateText(canvas.transform, "Enemy Context Text", "Enemies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, -152f), new Vector2(560f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI priorText = CreateText(canvas.transform, "Prior Text", "P(Attack) 50%   P(Dodge) 50%", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(312f, -188f), new Vector2(590f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI contextText = CreateText(canvas.transform, "Detailed Context Text", "Closest: None", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(370f, -224f), new Vector2(700f, 34f), 17, TextAlignmentOptions.Left);
        TextMeshProUGUI feedbackText = CreateText(canvas.transform, "Feedback Text", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(700f, 42f), 22, TextAlignmentOptions.Center);

        VirtualJoystick joystick = CreateVirtualJoystick(canvas.transform, player);
        Image attackImage = CreateActionButton(canvas.transform, "Attack Button", "Attack", new Vector2(1f, 0f), new Vector2(-160f, 150f), new Color(0.95f, 0.22f, 0.18f, 0.88f), out RectTransform attackHitbox);
        Image dodgeImage = CreateActionButton(canvas.transform, "Dodge Button", "Dodge", new Vector2(1f, 0f), new Vector2(-340f, 118f), new Color(0.12f, 0.58f, 1f, 0.88f), out RectTransform dodgeHitbox);
        Button skipButton = CreateSkipButton(canvas.transform);

        GameObject managerObject = new GameObject("Game Manager");
        CombatManager combatManager = managerObject.AddComponent<CombatManager>();
        CombatActionPriorBuilder priorBuilder = managerObject.AddComponent<CombatActionPriorBuilder>();
        RoguelikeGameManager gameManager = managerObject.AddComponent<RoguelikeGameManager>();
        ParticipantConfig participantConfig = managerObject.AddComponent<ParticipantConfig>();
        ExperimentSessionManager sessionManager = managerObject.AddComponent<ExperimentSessionManager>();
        ConditionManager conditionManager = managerObject.AddComponent<ConditionManager>();
        TrialScenarioManager trialScenarioManager = managerObject.AddComponent<TrialScenarioManager>();
        UserTouchModel userTouchModel = managerObject.AddComponent<UserTouchModel>();
        BayesianInputDecoder decoder = managerObject.AddComponent<BayesianInputDecoder>();
        SafetyGate safetyGate = managerObject.AddComponent<SafetyGate>();
        RawTouchLogger rawTouchLogger = managerObject.AddComponent<RawTouchLogger>();
        BayesianDecisionLogger decisionLogger = managerObject.AddComponent<BayesianDecisionLogger>();
        ButtonLayoutLogger layoutLogger = managerObject.AddComponent<ButtonLayoutLogger>();
        HPOutcomeLogger hpOutcomeLogger = managerObject.AddComponent<HPOutcomeLogger>();
        ModePolicyLogger modePolicyLogger = managerObject.AddComponent<ModePolicyLogger>();
        InteractionDemandModel demandModel = managerObject.AddComponent<InteractionDemandModel>();
        AdaptiveUIPolicyEngine policyEngine = managerObject.AddComponent<AdaptiveUIPolicyEngine>();
        AdaptiveTouchManager touchManager = managerObject.AddComponent<AdaptiveTouchManager>();

        gameManager.playerTransform = player.transform;
        gameManager.playerController = player;
        gameManager.skipButton = skipButton;
        gameManager.stageText = stageText;
        gameManager.startStageOnPlay = startGameOnPlay && !runCalibrationBeforeGame;
        gameManager.startingStage = gameStartStage;

        combatManager.gameManager = gameManager;
        combatManager.playerController = player;
        combatManager.priorBuilder = priorBuilder;
        combatManager.stateText = stateText;
        combatManager.playerHpText = playerHpText;
        combatManager.enemyHpText = enemyText;
        combatManager.feedbackLogText = feedbackText;
        combatManager.priorText = priorText;
        combatManager.contextText = contextText;

        sessionManager.participantConfig = participantConfig;
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
        decoder.userTouchModel = userTouchModel;

        touchManager.mainCanvas = canvas;
        touchManager.visualAttackButton = attackImage;
        touchManager.visualDodgeButton = dodgeImage;
        touchManager.attackHitboxVisualizer = attackHitbox;
        touchManager.dodgeHitboxVisualizer = dodgeHitbox;
        touchManager.ignoredInputRegions = joystick != null ? new[] { joystick.background } : null;
        touchManager.decoder = decoder;
        touchManager.safetyGate = safetyGate;
        touchManager.userTouchModel = userTouchModel;
        touchManager.sessionManager = sessionManager;
        touchManager.trialScenarioManager = trialScenarioManager;
        touchManager.conditionManager = conditionManager;
        touchManager.rawTouchLogger = rawTouchLogger;
        touchManager.decisionLogger = decisionLogger;
        touchManager.layoutLogger = layoutLogger;
        touchManager.hpOutcomeLogger = hpOutcomeLogger;
        touchManager.modePolicyLogger = modePolicyLogger;
        touchManager.demandModel = demandModel;
        touchManager.policyEngine = policyEngine;

        if (runCalibrationBeforeGame)
        {
            KTHCalibrationGameFlow flow = managerObject.AddComponent<KTHCalibrationGameFlow>();
            flow.gameManager = gameManager;
            flow.trialScenarioManager = trialScenarioManager;
            flow.sessionManager = sessionManager;
            flow.userTouchModel = userTouchModel;
            flow.stageText = stageText;
            flow.gameStartStage = gameStartStage;
        }
    }

    private void CreateCameraIfNeeded()
    {
        if (Camera.main != null)
        {
            return;
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 7.2f;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.08f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        cameraObject.AddComponent<AudioListener>();
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
        out RectTransform hitbox)
    {
        RectTransform buttonTransform = CreateRectTransform(name, parent, anchor, anchor, anchoredPosition, new Vector2(150f, 150f));
        Image image = buttonTransform.gameObject.AddComponent<Image>();
        image.sprite = PrototypeVisualFactory.SquareSprite;
        image.color = color;
        image.raycastTarget = false;

        hitbox = CreateRectTransform(name + " Hitbox", buttonTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 180f));
        Image hitboxImage = hitbox.gameObject.AddComponent<Image>();
        hitboxImage.sprite = PrototypeVisualFactory.SquareSprite;
        hitboxImage.color = label == "Attack" ? new Color(1f, 0f, 0f, 0.18f) : new Color(0f, 0.45f, 1f, 0.18f);
        hitboxImage.raycastTarget = false;

        TextMeshProUGUI text = CreateText(buttonTransform, label + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 44f), 24, TextAlignmentOptions.Center);
        text.color = Color.white;
        return image;
    }

    private VirtualJoystick CreateVirtualJoystick(Transform parent, PlayerController player)
    {
        RectTransform background = CreateRectTransform("Movement Joystick", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 160f), new Vector2(230f, 230f));
        Image backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.sprite = PrototypeVisualFactory.CircleSprite;
        backgroundImage.color = new Color(0.08f, 0.14f, 0.16f, 0.62f);
        backgroundImage.raycastTarget = true;

        RectTransform guide = CreateRectTransform("Movement Joystick Guide", background, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(154f, 154f));
        Image guideImage = guide.gameObject.AddComponent<Image>();
        guideImage.sprite = PrototypeVisualFactory.CircleSprite;
        guideImage.color = new Color(0.45f, 0.8f, 0.88f, 0.22f);
        guideImage.raycastTarget = false;

        RectTransform handle = CreateRectTransform("Movement Joystick Handle", background, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(74f, 74f));
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = PrototypeVisualFactory.CircleSprite;
        handleImage.color = new Color(0.85f, 0.95f, 1f, 0.9f);
        handleImage.raycastTarget = false;

        VirtualJoystick joystick = background.gameObject.AddComponent<VirtualJoystick>();
        joystick.playerController = player;
        joystick.background = background;
        joystick.handle = handle;
        joystick.canvas = parent.GetComponentInParent<Canvas>();
        joystick.radius = 78f;
        joystick.deadZone = 0.12f;
        return joystick;
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
}

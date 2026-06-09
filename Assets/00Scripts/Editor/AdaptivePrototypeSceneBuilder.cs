#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class AdaptivePrototypeSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/AdaptivePrototype.unity";
    private const string GeneratedFolderPath = "Assets/00Scripts/Editor/Generated";
    private const string CircleSpriteAssetPath = GeneratedFolderPath + "/AdaptiveHitboxCircle.asset";
    private const float ActionButtonSize = 124f;
    private const float ActionButtonHitboxSize = 148f;
    private const float ActionButtonLabelWidth = 116f;

    [MenuItem("Tools/Adaptive UI/Create Prototype Scene")]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Camera camera = CreateCamera();
        PlayerController player = CreatePlayer();
        ConfigureCameraFollow(camera, player.transform);
        Canvas canvas = CreateCanvas();
        CreateEventSystem();

        TextMeshProUGUI stageText = CreateText(canvas.transform, "Stage Text", "Stage 1 / 3", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, -34f), new Vector2(400f, 44f), 24, TextAlignmentOptions.Left);
        TextMeshProUGUI stateText = CreateText(canvas.transform, "State Text", "Context", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -78f), new Vector2(460f, 38f), 20, TextAlignmentOptions.Left);
        TextMeshProUGUI playerHpText = CreateText(canvas.transform, "Player HP Text", "Player HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -116f), new Vector2(460f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI enemyText = CreateText(canvas.transform, "Enemy Context Text", "Enemies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, -152f), new Vector2(560f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI priorText = CreateText(canvas.transform, "Prior Text", "P(A) 50%   P(D) 50%   P(H) 0%   P(W) 0%", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(405f, -188f), new Vector2(780f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI contextText = CreateText(canvas.transform, "Detailed Context Text", "Closest: None", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(520f, -224f), new Vector2(1000f, 34f), 17, TextAlignmentOptions.Left);
        TextMeshProUGUI feedbackText = CreateText(canvas.transform, "Feedback Text", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(700f, 42f), 22, TextAlignmentOptions.Center);

        Image attackImage = CreateActionButton(canvas.transform, "Attack Button", "Attack", new Vector2(1f, 0f), new Vector2(-126f, 126f), new Color(0.95f, 0.22f, 0.18f, 0.88f), out RectTransform attackHitbox, out _);
        Image dodgeImage = CreateActionButton(canvas.transform, "Dodge Button", "Dodge", new Vector2(1f, 0f), new Vector2(-292f, 126f), new Color(0.12f, 0.58f, 1f, 0.88f), out RectTransform dodgeHitbox, out _);
        Image healImage = CreateActionButton(canvas.transform, "Heal Button", "Heal", new Vector2(1f, 0f), new Vector2(-126f, 292f), new Color(0.18f, 0.78f, 0.38f, 0.88f), out RectTransform healHitbox, out TextMeshProUGUI healLabel);
        Image whirlwindImage = CreateActionButton(canvas.transform, "Whirlwind Button", "Whirlwind", new Vector2(1f, 0f), new Vector2(-292f, 292f), new Color(1f, 0.68f, 0.12f, 0.88f), out RectTransform whirlwindHitbox, out TextMeshProUGUI whirlwindLabel);
        RectTransform joystickTouchArea = CreateMovementJoystick(canvas.transform, player);
        Button skipButton = CreateSkipButton(canvas.transform);
        Image adaptiveToggleImage = CreateAdaptiveToggleButton(canvas.transform, out TextMeshProUGUI adaptiveToggleLabel);
        GameObject startScreen = CreateStartScreen(canvas.transform, out Button startButton);
        GameObject resultScreen = CreateResultScreen(canvas.transform, out TextMeshProUGUI resultText, out Button restartButton);

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
        gameManager.startButton = startButton;
        gameManager.restartButton = restartButton;
        gameManager.startScreenRoot = startScreen;
        gameManager.resultScreenRoot = resultScreen;
        gameManager.stageText = stageText;
        gameManager.resultText = resultText;

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
        rawTouchLogger.sessionManager = sessionManager;
        decisionLogger.sessionManager = sessionManager;
        layoutLogger.sessionManager = sessionManager;
        hpOutcomeLogger.sessionManager = sessionManager;
        modePolicyLogger.sessionManager = sessionManager;
        decoder.userTouchModel = userTouchModel;

        touchManager.mainCanvas = canvas;
        touchManager.visualAttackButton = attackImage;
        touchManager.visualDodgeButton = dodgeImage;
        touchManager.visualHealButton = healImage;
        touchManager.visualWhirlwindButton = whirlwindImage;
        touchManager.healButtonLabel = healLabel;
        touchManager.whirlwindButtonLabel = whirlwindLabel;
        touchManager.attackHitboxVisualizer = attackHitbox;
        touchManager.dodgeHitboxVisualizer = dodgeHitbox;
        touchManager.healHitboxVisualizer = healHitbox;
        touchManager.whirlwindHitboxVisualizer = whirlwindHitbox;
        touchManager.movementJoystickTouchArea = joystickTouchArea;
        touchManager.adaptiveToggleButton = adaptiveToggleImage;
        touchManager.adaptiveToggleLabel = adaptiveToggleLabel;

        Selection.activeGameObject = managerObject;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Created adaptive UI prototype scene at {ScenePath} with camera {camera.name}.");
    }

    public static void CreateSceneFromCommandLine()
    {
        CreateScene();
    }

    private static Camera CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.4f;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.08f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        cameraObject.AddComponent<AudioListener>();
        return camera;
    }

    private static void ConfigureCameraFollow(Camera camera, Transform playerTransform)
    {
        TopDownCameraFollow follow = camera.gameObject.AddComponent<TopDownCameraFollow>();
        follow.SetTarget(playerTransform);
    }

    private static PlayerController CreatePlayer()
    {
        GameObject playerObject = new GameObject("Player");
        playerObject.tag = "Player";
        playerObject.transform.position = Vector3.zero;
        playerObject.AddComponent<Rigidbody2D>();
        playerObject.AddComponent<CircleCollider2D>();
        return playerObject.AddComponent<PlayerController>();
    }

    private static Canvas CreateCanvas()
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

    private static void CreateEventSystem()
    {
        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private static Image CreateActionButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Color color,
        out RectTransform hitbox,
        out TextMeshProUGUI labelText)
    {
        RectTransform buttonTransform = CreateRectTransform(name, parent, anchor, anchor, anchoredPosition, new Vector2(ActionButtonSize, ActionButtonSize));
        Image image = buttonTransform.gameObject.AddComponent<Image>();
        image.sprite = GetCircleSprite();
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;

        hitbox = CreateRectTransform(name + " Hitbox", buttonTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ActionButtonHitboxSize, ActionButtonHitboxSize));
        Image hitboxImage = hitbox.gameObject.AddComponent<Image>();
        hitboxImage.sprite = GetCircleSprite();
        hitboxImage.color = new Color(color.r, color.g, color.b, 0.1f);
        hitboxImage.raycastTarget = false;

        labelText = CreateText(buttonTransform, label + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ActionButtonLabelWidth, 42f), 22, TextAlignmentOptions.Center);
        labelText.fontSize = label.Length > 7 ? 14f : 21f;
        labelText.color = Color.white;

        return image;
    }

    private static RectTransform CreateMovementJoystick(Transform parent, PlayerController player)
    {
        RectTransform baseTransform = CreateRectTransform("Movement Joystick", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 170f), new Vector2(280f, 280f));
        Image baseImage = baseTransform.gameObject.AddComponent<Image>();
        baseImage.sprite = GetCircleSprite();
        baseImage.color = new Color(0.72f, 0.82f, 0.9f, 0.2f);
        baseImage.raycastTarget = true;
        baseImage.preserveAspect = true;

        RectTransform handleTransform = CreateRectTransform("Movement Joystick Handle", baseTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
        Image handleImage = handleTransform.gameObject.AddComponent<Image>();
        handleImage.sprite = GetCircleSprite();
        handleImage.color = new Color(0.95f, 0.98f, 1f, 0.55f);
        handleImage.raycastTarget = false;
        handleImage.preserveAspect = true;

        VirtualJoystick joystick = baseTransform.gameObject.AddComponent<VirtualJoystick>();
        joystick.targetPlayer = player;
        joystick.joystickBase = baseTransform;
        joystick.joystickHandle = handleTransform;
        return baseTransform;
    }

    private static Button CreateSkipButton(Transform parent)
    {
        RectTransform rectTransform = CreateRectTransform("Skip Stage Button", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-130f, -44f), new Vector2(190f, 52f));
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = new Color(0.16f, 0.18f, 0.16f, 0.92f);

        Button button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI text = CreateText(rectTransform, "Skip Text", "Skip Stage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 38f), 18, TextAlignmentOptions.Center);
        text.color = Color.white;
        return button;
    }

    private static Image CreateAdaptiveToggleButton(Transform parent, out TextMeshProUGUI labelText)
    {
        RectTransform rectTransform = CreateRectTransform("Adaptive Toggle Button", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-390f, -44f), new Vector2(260f, 52f));
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = new Color(0.18f, 0.72f, 0.44f, 0.92f);
        image.raycastTarget = false;

        labelText = CreateText(rectTransform, "Adaptive Toggle Text", "Adaptive: ON", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 38f), 18, TextAlignmentOptions.Center);
        labelText.color = Color.white;
        return image;
    }

    private static GameObject CreateStartScreen(Transform parent, out Button startButton)
    {
        GameObject screen = CreateOverlayPanel(parent, "Start Screen", new Color(0.02f, 0.03f, 0.03f, 0.88f));
        TextMeshProUGUI title = CreateText(screen.transform, "Start Title", "Adaptive UI Test", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(760f, 84f), 46, TextAlignmentOptions.Center);
        title.color = Color.white;

        startButton = CreateMenuButton(screen.transform, "Start Button", "Start", new Vector2(0f, -28f), new Vector2(280f, 78f), new Color(0.18f, 0.72f, 0.44f, 0.95f));
        return screen;
    }

    private static GameObject CreateResultScreen(Transform parent, out TextMeshProUGUI resultText, out Button restartButton)
    {
        GameObject screen = CreateOverlayPanel(parent, "Result Screen", new Color(0.02f, 0.03f, 0.03f, 0.9f));
        resultText = CreateText(screen.transform, "Result Text", "Final Results", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 72f), new Vector2(860f, 690f), 22, TextAlignmentOptions.TopLeft);
        resultText.textWrappingMode = TextWrappingModes.Normal;

        restartButton = CreateMenuButton(screen.transform, "Restart Button", "Restart", new Vector2(0f, -410f), new Vector2(250f, 70f), new Color(0.18f, 0.72f, 0.44f, 0.95f));
        screen.SetActive(false);
        return screen;
    }

    private static GameObject CreateOverlayPanel(Transform parent, string name, Color color)
    {
        RectTransform rectTransform = CreateRectTransform(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = color;
        image.raycastTarget = true;
        return rectTransform.gameObject;
    }

    private static Button CreateMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        RectTransform rectTransform = CreateRectTransform(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, size);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = color;

        Button button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        TextMeshProUGUI text = CreateText(rectTransform, name + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(20f, 20f), 24, TextAlignmentOptions.Center);
        text.color = Color.white;
        return button;
    }

    private static TextMeshProUGUI CreateText(
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

    private static RectTransform CreateRectTransform(
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

    private static Sprite GetUiSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    private static Sprite GetCircleSprite()
    {
        Sprite existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpriteAssetPath);
        if (existingSprite != null)
        {
            return existingSprite;
        }

        if (!AssetDatabase.IsValidFolder(GeneratedFolderPath))
        {
            AssetDatabase.CreateFolder("Assets/00Scripts/Editor", "Generated");
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Adaptive Hitbox Circle Texture";
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                pixels[y * size + x] = new Color(1f, 1f, 1f, distance <= radius ? 1f : 0f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.name = "Adaptive Hitbox Circle";

        AssetDatabase.CreateAsset(texture, CircleSpriteAssetPath);
        AssetDatabase.AddObjectToAsset(sprite, texture);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(CircleSpriteAssetPath);
        return AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpriteAssetPath);
    }
}
#endif

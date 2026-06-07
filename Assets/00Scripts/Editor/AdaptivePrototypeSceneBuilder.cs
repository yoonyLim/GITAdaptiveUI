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

    [MenuItem("Tools/Adaptive UI/Create Prototype Scene")]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Camera camera = CreateCamera();
        PlayerController player = CreatePlayer();
        Canvas canvas = CreateCanvas();
        CreateEventSystem();

        TextMeshProUGUI stageText = CreateText(canvas.transform, "Stage Text", "Stage 1 / 3", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, -34f), new Vector2(400f, 44f), 24, TextAlignmentOptions.Left);
        TextMeshProUGUI stateText = CreateText(canvas.transform, "State Text", "Context", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -78f), new Vector2(460f, 38f), 20, TextAlignmentOptions.Left);
        TextMeshProUGUI playerHpText = CreateText(canvas.transform, "Player HP Text", "Player HP", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -116f), new Vector2(460f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI enemyText = CreateText(canvas.transform, "Enemy Context Text", "Enemies", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(300f, -152f), new Vector2(560f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI priorText = CreateText(canvas.transform, "Prior Text", "P(A) 50%   P(D) 50%   P(H) 0%   P(W) 0%", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(405f, -188f), new Vector2(780f, 34f), 18, TextAlignmentOptions.Left);
        TextMeshProUGUI contextText = CreateText(canvas.transform, "Detailed Context Text", "Closest: None", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(520f, -224f), new Vector2(1000f, 34f), 17, TextAlignmentOptions.Left);
        TextMeshProUGUI feedbackText = CreateText(canvas.transform, "Feedback Text", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(700f, 42f), 22, TextAlignmentOptions.Center);

        Image attackImage = CreateActionButton(canvas.transform, "Attack Button", "Attack", new Vector2(1f, 0f), new Vector2(-150f, 150f), new Color(0.95f, 0.22f, 0.18f, 0.88f), out RectTransform attackHitbox, out _);
        Image dodgeImage = CreateActionButton(canvas.transform, "Dodge Button", "Dodge", new Vector2(1f, 0f), new Vector2(-370f, 150f), new Color(0.12f, 0.58f, 1f, 0.88f), out RectTransform dodgeHitbox, out _);
        Image healImage = CreateActionButton(canvas.transform, "Heal Button", "Heal", new Vector2(1f, 0f), new Vector2(-150f, 370f), new Color(0.18f, 0.78f, 0.38f, 0.88f), out RectTransform healHitbox, out TextMeshProUGUI healLabel);
        Image whirlwindImage = CreateActionButton(canvas.transform, "Whirlwind Button", "Whirlwind", new Vector2(1f, 0f), new Vector2(-370f, 370f), new Color(1f, 0.68f, 0.12f, 0.88f), out RectTransform whirlwindHitbox, out TextMeshProUGUI whirlwindLabel);
        RectTransform joystickTouchArea = CreateMovementJoystick(canvas.transform, player);
        Button skipButton = CreateSkipButton(canvas.transform);

        GameObject managerObject = new GameObject("Game Manager");
        CombatManager combatManager = managerObject.AddComponent<CombatManager>();
        RoguelikeGameManager gameManager = managerObject.AddComponent<RoguelikeGameManager>();
        AdaptiveTouchManager touchManager = managerObject.AddComponent<AdaptiveTouchManager>();

        gameManager.playerTransform = player.transform;
        gameManager.playerController = player;
        gameManager.skipButton = skipButton;
        gameManager.stageText = stageText;

        combatManager.gameManager = gameManager;
        combatManager.playerController = player;
        combatManager.stateText = stateText;
        combatManager.playerHpText = playerHpText;
        combatManager.enemyHpText = enemyText;
        combatManager.feedbackLogText = feedbackText;
        combatManager.priorText = priorText;
        combatManager.contextText = contextText;

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
        camera.orthographicSize = 7.2f;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.08f, 1f);
        camera.clearFlags = CameraClearFlags.SolidColor;

        cameraObject.AddComponent<AudioListener>();
        return camera;
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
        RectTransform buttonTransform = CreateRectTransform(name, parent, anchor, anchor, anchoredPosition, new Vector2(160f, 160f));
        Image image = buttonTransform.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = color;
        image.raycastTarget = false;

        hitbox = CreateRectTransform(name + " Hitbox", buttonTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 200f));
        Image hitboxImage = hitbox.gameObject.AddComponent<Image>();
        hitboxImage.sprite = GetCircleSprite();
        hitboxImage.color = new Color(color.r, color.g, color.b, 0.18f);
        hitboxImage.raycastTarget = false;

        labelText = CreateText(buttonTransform, label + " Text", label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 48f), 24, TextAlignmentOptions.Center);
        labelText.fontSize = label.Length > 7 ? 17f : 24f;
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

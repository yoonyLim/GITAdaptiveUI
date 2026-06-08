using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KTHTopDownPrototypePlayRecorder : MonoBehaviour
{
    public string outputDir = "";
    public int captureFps = 15;
    public int captureWidth = 1280;
    public int captureHeight = 720;

    private AdaptiveTouchManager touchManager;
    private CombatManager combatManager;
    private RoguelikeGameManager gameManager;
    private ConditionManager conditionManager;
    private MethodInfo processInputBegan;
    private MethodInfo processInputEnded;
    private TextMeshProUGUI overlayText;
    private RectTransform touchMarker;
    private int frameIndex;
    private float nextCaptureTime;
    private string currentCase = "Boot";
    private string lastDecisionLine = "";
    private Vector2 currentTouch;
    private bool markerVisible;

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
            outputDir = Path.Combine(Application.dataPath, "..", "outputs", "unity_recordings", "topdown_prototype_gaussian_runtime");
        }

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "frames"));

        Screen.SetResolution(captureWidth, captureHeight, false);
        Application.targetFrameRate = 60;
        Time.captureFramerate = 0;

        StartCoroutine(RunRecording());
    }

    private void Update()
    {
        UpdateOverlay();
    }

    private IEnumerator RunRecording()
    {
        yield return WaitForPrototype();
        ResolveReferences();
        ConfigureManagers();
        SetupOverlay();

        yield return CaptureForSeconds(1.0f, "AdaptivePrototype boot: orthographic top-down");

        StartStage(1);
        yield return CaptureForSeconds(1.4f, "Stage 1: melee enemies spawn around player");
        yield return TapCase("Stage 1: clear Attack tap", AttackButtonCenter(), 1.4f);

        yield return CaptureUntilThreatOrTimeout(4.0f, "Stage 1: enemies close in");
        yield return TapCase("Stage 1: ambiguous midpoint under combat context", BetweenButtons(), 1.8f);

        StartStage(3);
        yield return CaptureForSeconds(1.4f, "Stage 3: boss + mixed enemies");
        yield return CaptureUntilThreatOrTimeout(5.0f, "Stage 3: telegraphs/projectiles influence prior");
        yield return TapCase("Stage 3: far touch rejected", new Vector2(Screen.width * 0.93f, Screen.height * 0.82f), 1.5f);

        markerVisible = false;
        yield return CaptureForSeconds(0.8f, "Recording complete");

        File.WriteAllText(Path.Combine(outputDir, "done.txt"), $"frames={frameIndex}{Environment.NewLine}");

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
            if (touchManager != null && combatManager != null && gameManager != null)
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("KTH top-down recorder timed out while waiting for AdaptivePrototype bootstrap.");
    }

    private void StartStage(int stage)
    {
        ResolveReferences();
        ConfigureManagers();

        if (gameManager != null)
        {
            gameManager.StartStage(stage);
        }
    }

    private IEnumerator CaptureUntilThreatOrTimeout(float seconds, string label)
    {
        currentCase = label;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            ResolveReferences();
            if (combatManager != null &&
                (combatManager.CurrentContext.telegraphingEnemies > 0 ||
                 combatManager.CurrentContext.attackingEnemies > 0 ||
                 combatManager.CurrentContext.incomingProjectiles > 0 ||
                 combatManager.CurrentContext.closeEnemies >= 3))
            {
                yield return CaptureForSeconds(0.6f, label);
                yield break;
            }

            if (Time.realtimeSinceStartup >= nextCaptureTime)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame();
                nextCaptureTime = Time.realtimeSinceStartup + 1f / Mathf.Max(1, captureFps);
            }
            else
            {
                yield return null;
            }
        }
    }

    private IEnumerator TapCase(string label, Vector2 touch, float duration)
    {
        currentCase = label;
        currentTouch = touch;
        markerVisible = true;
        InvokeTouch(touch);
        yield return CaptureForSeconds(0.45f, label);
        InvokeTouchEnded();
        yield return CaptureForSeconds(Mathf.Max(0.1f, duration - 0.45f), label);
        markerVisible = false;
    }

    private IEnumerator CaptureForSeconds(float seconds, string label)
    {
        currentCase = label;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            if (Time.realtimeSinceStartup >= nextCaptureTime)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame();
                nextCaptureTime = Time.realtimeSinceStartup + 1f / Mathf.Max(1, captureFps);
            }
            else
            {
                yield return null;
            }
        }
    }

    private void ResolveReferences()
    {
        touchManager = FindAnyObjectByType<AdaptiveTouchManager>();
        combatManager = CombatManager.Instance != null ? CombatManager.Instance : FindAnyObjectByType<CombatManager>();
        gameManager = RoguelikeGameManager.Instance != null ? RoguelikeGameManager.Instance : FindAnyObjectByType<RoguelikeGameManager>();
        conditionManager = FindAnyObjectByType<ConditionManager>();

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
        }

        if (conditionManager == null && touchManager != null)
        {
            conditionManager = touchManager.GetComponent<ConditionManager>();
        }

        if (conditionManager != null)
        {
            conditionManager.randomizeConditionOrder = false;
            conditionManager.SetCondition(DatasetSchema.ConditionContextBayesianSafety);
        }
    }

    private Vector2 AttackButtonCenter()
    {
        if (touchManager != null && touchManager.visualAttackButton != null)
        {
            return touchManager.visualAttackButton.rectTransform.position;
        }

        return new Vector2(Screen.width * 0.86f, Screen.height * 0.18f);
    }

    private Vector2 DodgeButtonCenter()
    {
        if (touchManager != null && touchManager.visualDodgeButton != null)
        {
            return touchManager.visualDodgeButton.rectTransform.position;
        }

        return new Vector2(Screen.width * 0.74f, Screen.height * 0.15f);
    }

    private Vector2 BetweenButtons()
    {
        return (AttackButtonCenter() + DodgeButtonCenter()) * 0.5f;
    }

    private void InvokeTouch(Vector2 position)
    {
        ResolveReferences();
        if (touchManager == null || processInputBegan == null)
        {
            Debug.LogWarning("KTH top-down recorder could not find AdaptiveTouchManager.ProcessInputBegan.");
            return;
        }

        processInputBegan.Invoke(touchManager, new object[] { position });
    }

    private void InvokeTouchEnded()
    {
        if (touchManager != null && processInputEnded != null)
        {
            processInputEnded.Invoke(touchManager, null);
        }
    }

    private void SetupOverlay()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("KTH TopDown Overlay Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject panel = new GameObject("KTH TopDown Recording Overlay");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(18f, -18f);
        panelRect.sizeDelta = new Vector2(820f, 166f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.03f, 0.04f, 0.06f, 0.78f);

        GameObject textObject = new GameObject("KTH TopDown Recording Overlay Text");
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);
        overlayText = textObject.AddComponent<TextMeshProUGUI>();
        overlayText.fontSize = 18f;
        overlayText.color = Color.white;
        overlayText.alignment = TextAlignmentOptions.TopLeft;

        GameObject marker = new GameObject("KTH TopDown Touch Marker");
        marker.transform.SetParent(canvas.transform, false);
        touchMarker = marker.AddComponent<RectTransform>();
        touchMarker.sizeDelta = new Vector2(42f, 42f);
        Image markerImage = marker.AddComponent<Image>();
        markerImage.color = new Color(1f, 0.85f, 0.1f, 0.78f);
    }

    private void UpdateOverlay()
    {
        if (touchMarker != null)
        {
            touchMarker.gameObject.SetActive(markerVisible);
            touchMarker.position = currentTouch;
        }

        if (overlayText == null)
        {
            return;
        }

        float attackPrior = combatManager != null ? combatManager.priorAttack : 0.5f;
        float dodgePrior = combatManager != null ? combatManager.priorDodge : 0.5f;
        string source = combatManager != null ? combatManager.CurrentPriorResult.source : "";
        if (string.IsNullOrEmpty(source))
        {
            source = "waiting";
        }

        string context = combatManager != null
            ? $"enemies={combatManager.CurrentContext.totalEnemies}, close={combatManager.CurrentContext.closeEnemies}, telegraph={combatManager.CurrentContext.telegraphingEnemies}, attacking={combatManager.CurrentContext.attackingEnemies}, projectiles={combatManager.CurrentContext.incomingProjectiles}, state={combatManager.currentState}"
            : "combat manager unavailable";

        overlayText.text =
            $"{currentCase}\n" +
            "scene=Assets/Scenes/AdaptivePrototype.unity | camera=orthographic top-down | manager=RoguelikeGameManager\n" +
            $"condition=context_bayesian_safety | source={source} | prior A/D={attackPrior:0.00}/{dodgePrior:0.00}\n" +
            $"{context}\n" +
            lastDecisionLine;
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
        if (condition.StartsWith("[Adaptive Touch]", StringComparison.Ordinal))
        {
            lastDecisionLine = condition;
        }
    }
}

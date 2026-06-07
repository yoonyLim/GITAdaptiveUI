using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KTHOriginalScenePlayRecorder : MonoBehaviour
{
    public string outputDir = "";
    public int captureFps = 15;
    public int captureWidth = 1280;
    public int captureHeight = 720;

    private AdaptiveTouchManager touchManager;
    private CombatManager combatManager;
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
            outputDir = Path.Combine(Application.dataPath, "..", "outputs", "unity_recordings", "sample_scene_state_gaussian_runtime");
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
        ResolveReferences();
        SetupOverlay();

        yield return new WaitForSeconds(1.0f);
        ResolveReferences();
        ConfigureManagers();

        yield return CaptureForSeconds(0.9f, "Original SampleScene boot");

        yield return RunStateCase(
            CombatManager.CombatState.Safe,
            "Safe state: clear Attack tap is preserved",
            AttackButtonCenter(),
            1.8f);

        yield return RunStateCase(
            CombatManager.CombatState.Telegraph,
            "Telegraph state: ambiguous midpoint corrects to Dodge",
            BetweenButtons(),
            2.2f);

        yield return RunStateCase(
            CombatManager.CombatState.Attacking,
            "Attacking state: clear Attack tap is preserved by SafetyGate",
            AttackButtonCenter() + new Vector2(20f, 0f),
            2.2f);

        yield return RunStateCase(
            CombatManager.CombatState.Attacking,
            "Attacking state: far touch is rejected",
            new Vector2(Screen.width * 0.93f, Screen.height * 0.82f),
            1.8f);

        markerVisible = false;
        yield return CaptureForSeconds(0.8f, "Recording complete");

        File.WriteAllText(Path.Combine(outputDir, "done.txt"), $"frames={frameIndex}{Environment.NewLine}");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    private IEnumerator RunStateCase(CombatManager.CombatState state, string label, Vector2 touch, float duration)
    {
        SetCombatState(state);
        yield return CaptureForSeconds(0.65f, $"SampleScene state={state}");
        yield return TapCase(label, touch, duration);
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
        if (combatManager != null)
        {
            combatManager.useLegacyStatePriorFallback = true;
        }

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

    private void SetCombatState(CombatManager.CombatState state)
    {
        ResolveReferences();
        ConfigureManagers();

        if (combatManager != null)
        {
            combatManager.currentState = state;
        }
    }

    private Vector2 AttackButtonCenter()
    {
        if (touchManager != null && touchManager.visualAttackButton != null)
        {
            return touchManager.visualAttackButton.rectTransform.position;
        }

        return new Vector2(Screen.width * 0.78f, Screen.height * 0.18f);
    }

    private Vector2 DodgeButtonCenter()
    {
        if (touchManager != null && touchManager.visualDodgeButton != null)
        {
            return touchManager.visualDodgeButton.rectTransform.position;
        }

        return new Vector2(Screen.width * 0.88f, Screen.height * 0.18f);
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
            Debug.LogWarning("KTH recorder could not find AdaptiveTouchManager.ProcessInputBegan.");
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
            GameObject canvasObject = new GameObject("KTH Recording Overlay Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject panel = new GameObject("KTH Recording Overlay");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(18f, -18f);
        panelRect.sizeDelta = new Vector2(760f, 154f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.03f, 0.04f, 0.06f, 0.78f);

        GameObject textObject = new GameObject("KTH Recording Overlay Text");
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);
        overlayText = textObject.AddComponent<TextMeshProUGUI>();
        overlayText.fontSize = 19f;
        overlayText.color = Color.white;
        overlayText.alignment = TextAlignmentOptions.TopLeft;

        GameObject marker = new GameObject("KTH Touch Marker");
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
        string state = combatManager != null ? combatManager.currentState.ToString() : "Unavailable";
        string source = combatManager != null ? combatManager.CurrentPriorResult.source : "";
        if (string.IsNullOrEmpty(source))
        {
            source = "waiting";
        }

        overlayText.text =
            $"{currentCase}\n" +
            $"scene=Assets/Scenes/SampleScene.unity | actor=SimulatedEnemy | state={state}\n" +
            $"condition=context_bayesian_safety | source={source} | prior A/D={attackPrior:0.00}/{dodgePrior:0.00}\n" +
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
        else if (condition.Contains("SampleScene state prototype"))
        {
            lastDecisionLine = condition;
        }
    }
}

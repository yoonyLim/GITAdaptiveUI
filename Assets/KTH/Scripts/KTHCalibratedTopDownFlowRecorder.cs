using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class KTHCalibratedTopDownFlowRecorder : MonoBehaviour
{
    public string outputDir = "";
    public int captureFps = 15;
    public int captureWidth = 1280;
    public int captureHeight = 720;
    public float tapHoldSeconds = 0.06f;
    public float tapGapSeconds = 0.08f;

    private AdaptiveTouchManager touchManager;
    private CombatManager combatManager;
    private RoguelikeGameManager gameManager;
    private TrialScenarioManager trialScenarioManager;
    private UserTouchModel userTouchModel;
    private KTHCalibrationGameFlow flow;
    private PlayerController playerController;
    private VirtualJoystick joystick;
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
            outputDir = Path.Combine(Application.dataPath, "..", "outputs", "unity_recordings", "calibrated_topdown_runtime");
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
        yield return WaitForFlow();
        ResolveReferences();
        SetupOverlay();

        yield return CaptureForSeconds(0.8f, "Calibration-first scene boot");
        yield return AutoRunCalibration();
        yield return CaptureForSeconds(1.2f, "Calibration profile saved; game starting");
        yield return WaitForGameStarted();
        yield return CaptureForSeconds(1.4f, "Game started using calibrated touch profile");

        yield return JoystickCase("Gameplay: move with left joystick", new Vector2(-0.85f, 0.35f), 1.4f);
        yield return TapCase("Gameplay: clear Attack after calibration", AttackButtonCenter(), 1.2f);
        yield return TapCase("Gameplay: ambiguous midpoint after calibration", BetweenButtons(), 1.5f);
        yield return TapCase("Gameplay: far touch rejected after calibration", new Vector2(Screen.width * 0.93f, Screen.height * 0.82f), 1.2f);

        markerVisible = false;
        yield return CaptureForSeconds(0.8f, "Recording complete");

        File.WriteAllText(Path.Combine(outputDir, "done.txt"), $"frames={frameIndex}{Environment.NewLine}");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    private IEnumerator WaitForFlow()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ResolveReferences();
            if (touchManager != null && trialScenarioManager != null && flow != null)
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("KTH calibrated recorder timed out while waiting for calibration flow.");
    }

    private IEnumerator AutoRunCalibration()
    {
        while (trialScenarioManager != null && trialScenarioManager.currentPhase == DatasetSchema.PhaseCalibration)
        {
            string label = $"Calibration {trialScenarioManager.currentTrialId}/{Mathf.Max(trialScenarioManager.calibrationTotalCount, 1)}: {trialScenarioManager.currentTrialType}";
            Vector2 touch = CalibrationTouchPosition();
            yield return TapCase(label, touch, tapHoldSeconds + tapGapSeconds);
            yield return null;
        }
    }

    private IEnumerator WaitForGameStarted()
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ResolveReferences();
            if (flow != null && flow.GameStarted && gameManager != null && gameManager.ActiveEnemies.Count > 0)
            {
                yield break;
            }

            yield return CaptureForSeconds(0.12f, "Waiting for calibrated game start");
        }
    }

    private IEnumerator TapCase(string label, Vector2 touch, float duration)
    {
        currentCase = label;
        currentTouch = touch;
        markerVisible = true;
        InvokeTouch(touch);
        yield return CaptureForSeconds(Mathf.Min(tapHoldSeconds, duration), label);
        InvokeTouchEnded();
        yield return CaptureForSeconds(Mathf.Max(0.02f, duration - tapHoldSeconds), label);
        markerVisible = false;
    }

    private IEnumerator JoystickCase(string label, Vector2 direction, float duration)
    {
        currentCase = label;
        markerVisible = false;
        ResolveReferences();

        if (joystick != null)
        {
            joystick.SetInput(direction);
        }
        else if (playerController != null)
        {
            playerController.SetVirtualMoveInput(direction);
        }

        yield return CaptureForSeconds(duration, label);

        if (joystick != null)
        {
            joystick.SetInput(Vector2.zero);
        }
        else if (playerController != null)
        {
            playerController.ClearVirtualMoveInput();
        }

        yield return CaptureForSeconds(0.25f, "Gameplay: joystick released");
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
        trialScenarioManager = FindAnyObjectByType<TrialScenarioManager>();
        userTouchModel = FindAnyObjectByType<UserTouchModel>();
        flow = FindAnyObjectByType<KTHCalibrationGameFlow>();
        playerController = FindAnyObjectByType<PlayerController>();
        joystick = FindAnyObjectByType<VirtualJoystick>();

        if (touchManager != null && processInputBegan == null)
        {
            Type type = typeof(AdaptiveTouchManager);
            processInputBegan = type.GetMethod("ProcessInputBegan", BindingFlags.Instance | BindingFlags.NonPublic);
            processInputEnded = type.GetMethod("ProcessInputEnded", BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    private Vector2 CalibrationTouchPosition()
    {
        if (trialScenarioManager == null)
        {
            return AttackButtonCenter();
        }

        string intended = trialScenarioManager.currentIntendedAction;
        string type = trialScenarioManager.currentTrialType;
        Vector2 attack = AttackButtonCenter();
        Vector2 dodge = DodgeButtonCenter();
        Vector2 center = string.Equals(intended, "Dodge", StringComparison.OrdinalIgnoreCase) ? dodge : attack;
        Vector2 other = string.Equals(intended, "Dodge", StringComparison.OrdinalIgnoreCase) ? attack : dodge;

        if (type.StartsWith("near_boundary", StringComparison.OrdinalIgnoreCase))
        {
            return Vector2.Lerp(center, other, 0.34f);
        }

        if (type.StartsWith("ambiguous_gap", StringComparison.OrdinalIgnoreCase))
        {
            return Vector2.Lerp(center, other, 0.48f);
        }

        return center;
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
            Debug.LogWarning("KTH calibrated recorder could not find AdaptiveTouchManager.ProcessInputBegan.");
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
            GameObject canvasObject = new GameObject("KTH Calibrated Flow Overlay Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject panel = new GameObject("KTH Calibrated Flow Recording Overlay");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(18f, -18f);
        panelRect.sizeDelta = new Vector2(900f, 222f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.03f, 0.04f, 0.06f, 0.78f);

        GameObject textObject = new GameObject("KTH Calibrated Flow Recording Overlay Text");
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-14f, -10f);
        overlayText = textObject.AddComponent<TextMeshProUGUI>();
        overlayText.fontSize = 17f;
        overlayText.color = Color.white;
        overlayText.alignment = TextAlignmentOptions.TopLeft;

        GameObject marker = new GameObject("KTH Calibrated Flow Touch Marker");
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

        string phase = trialScenarioManager != null ? trialScenarioManager.currentPhase : "waiting";
        string trial = trialScenarioManager != null
            ? $"{trialScenarioManager.currentTrialId}/{Mathf.Max(trialScenarioManager.calibrationTotalCount, 1)} {trialScenarioManager.currentTrialType}"
            : "unavailable";
        string instruction = trialScenarioManager != null ? trialScenarioManager.currentInstruction : "";
        string samples = userTouchModel != null
            ? $"samples A/D={userTouchModel.attackSampleCount}/{userTouchModel.dodgeSampleCount}, variance A/D={userTouchModel.attackVariance:0}/{userTouchModel.dodgeVariance:0}"
            : "touch profile unavailable";
        string context = combatManager != null
            ? $"context enemies={combatManager.CurrentContext.totalEnemies}, close={combatManager.CurrentContext.closeEnemies}, state={combatManager.currentState}, prior A/D={combatManager.priorAttack:0.00}/{combatManager.priorDodge:0.00}"
            : "combat context unavailable";
        string player = playerController != null
            ? $"player position={playerController.transform.position.x:0.00},{playerController.transform.position.y:0.00}"
            : "player unavailable";

        overlayText.text =
            $"{currentCase}\n" +
            "scene=Assets/KTH/Scenes/KTHCalibratedTopDownPrototype.unity | flow=calibration -> calibrated top-down game\n" +
            $"phase={phase} | trial={trial} | instruction={instruction}\n" +
            $"{samples}\n" +
            $"{context}\n" +
            $"{player}\n" +
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
        if (condition.StartsWith("[Adaptive Touch]", StringComparison.Ordinal) ||
            condition.StartsWith("[ADUI] Calibration profile saved", StringComparison.Ordinal))
        {
            lastDecisionLine = condition;
        }
    }
}

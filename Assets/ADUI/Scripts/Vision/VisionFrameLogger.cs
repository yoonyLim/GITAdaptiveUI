using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class VisionFrameLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;
    public TrialScenarioManager trialScenarioManager;
    public Image visualAttackButton;
    public Image visualDodgeButton;
    public Camera captureCamera;

    public string screenshotDirectoryName = "vision_frames";

    public void CaptureCurrentFrame()
    {
        StartCoroutine(CaptureFrameAtEndOfFrame());
    }

    private IEnumerator CaptureFrameAtEndOfFrame()
    {
        if (!sessionManager) yield break;
        var sessionDir = sessionManager.EnsureSession();
        var imageDir = Path.Combine(sessionDir, screenshotDirectoryName);
        Directory.CreateDirectory(imageDir);

        yield return new WaitForEndOfFrame();

        var trialId = trialScenarioManager ? trialScenarioManager.currentTrialId : 0;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var imageName = $"frame_{trialId}_{timestamp}.png";
        var imagePath = Path.Combine(imageDir, imageName);
        ScreenCapture.CaptureScreenshot(imagePath);

        var record = new ADUIVisionFrameRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_ms = timestamp,
            image_path = Path.Combine(screenshotDirectoryName, imageName).Replace("\\", "/"),
            screen_width = Screen.width,
            screen_height = Screen.height,
            enemy_state = trialScenarioManager ? trialScenarioManager.CurrentEnemyState().ToString() : CurrentEnemyStateFromCombat().ToString(),
            attack_bbox = RectToString(visualAttackButton ? visualAttackButton.rectTransform : null),
            dodge_bbox = RectToString(visualDodgeButton ? visualDodgeButton.rectTransform : null),
            label_source = "unity_object_state"
        };
        sessionManager.exporter.AppendJsonl(sessionDir, "vision_frames.jsonl", record);
    }

    private string RectToString(RectTransform rect)
    {
        if (!rect) return "";
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var x1 = Mathf.Min(corners[0].x, corners[2].x);
        var x2 = Mathf.Max(corners[0].x, corners[2].x);
        var y1 = Mathf.Min(corners[0].y, corners[2].y);
        var y2 = Mathf.Max(corners[0].y, corners[2].y);
        return $"{x1:F1},{y1:F1},{x2:F1},{y2:F1}";
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
}
